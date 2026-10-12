using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Common loot ("treasure") chests (wiki "Chests", launch 2016): found in surface caves and out in the open in the
    /// Chaos Caverns, tucked in dead ends and dark corners; "a slight golden glow … gold swirls floating up". Anyone
    /// within 50 m of a chest when it opens gets loot, each their own — resources (raw wood, gems, ore). The chest then
    /// goes and comes back later.
    /// <para>
    /// Sites from the client's own cave placements (ContinentCavePlacements[continent]_Surface / _Underground.cpa): the
    /// surface caves' rooms (tiles 51 / 52 MediumRoom, 108 LargeRoom) and dead ends (32 / 33 Cap) and the caverns'
    /// middles (115 CavernMiddle02), a deterministic share of each; the chest stands on the cave floor there. Model: the
    /// "Treasure Chest" prop (2144); effects PFX_Loot_TreasureChest_Ambient_Generic 11279, _Open 11228.
    /// </para>
    /// </summary>
    public static class LootChests
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const uint ChestProp = 2144, Glow = 11279, Open = 11228;
        private const ulong GuidBase = 0x5443_0000_0000_0000;
        private const float ViewRange = 120f, DropRange = 170f, ShareRange = 50f;
        private static readonly TimeSpan Respawn = TimeSpan.FromMinutes(20);
        private static readonly uint[] SurfaceTiles = { 51, 52, 108, 32, 33 };
        private const uint CavernTile = 115;

        private sealed class Chest
        {
            public int Index;
            public Vector3 At;
            public bool Deep;
            public PlayerUpdateAddLightweightProp Prop;
            public bool Ready = true;
            public DateTime Back;
        }

        private static readonly List<Chest> chests = new();
        private static readonly Dictionary<WorldSession, HashSet<int>> sent = new();
        private static readonly object sync = new();
        private static readonly Random random = new();
        private static DateTime nextStream;

        public static void Load(string gameDirectory, uint continent)
        {
            string caves = Path.Combine(gameDirectory, "CommonData", "Voxel", "caves");
            AddSites(Path.Combine(caves, $"ContinentCavePlacements[{continent}]_Surface.cpa"), SurfaceTiles, 5, false);
            AddSites(Path.Combine(caves, $"ContinentCavePlacements[{continent}]_Underground.cpa"), new[] { CavernTile }, 8, true);
            log.Info($"Loot chests: {chests.Count} ({chests.Count(c => !c.Deep)} in surface caves, {chests.Count(c => c.Deep)} in the caverns).");
        }

        private static void AddSites(string path, uint[] tiles, int oneIn, bool deep)
        {
            if (!File.Exists(path))
                return;
            byte[] d = File.ReadAllBytes(path);
            int n = BitConverter.ToInt32(d, 4);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            for (int i = 0, o = 8; i < n && o + 39 <= d.Length; i++, o += 39)
            {
                if (!tiles.Contains(BitConverter.ToUInt32(d, o)) || (uint)HashCode.Combine(i, deep, 0x4c43) % (uint)oneIn != 0)
                    continue;
                double x = BitConverter.ToDouble(d, o + 4), y = BitConverter.ToDouble(d, o + 12), z = BitConverter.ToDouble(d, o + 20);
                var piece = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10));
                Vector3? floor = deep ? Caves.Floor(piece + new Vector3(0f, 10f, 0f)) : Caves.FloorNear(piece + new Vector3(0f, 2f, 0f), 12f);
                if (floor is not Vector3 at)
                    continue;
                var chest = new Chest { Index = chests.Count, At = at, Deep = deep };
                chest.Prop = new PlayerUpdateAddLightweightProp
                {
                    Guid = GuidBase | (ulong)chest.Index,
                    TransientId = PlayerManager.AllocateTransientId(),
                    ModelId = Props.ModelOf(ChestProp),
                    Position = new Vector4(at, 1f),
                    Rotation = new Vector4(0f, MathF.Sin(i * 0.37f), 0f, MathF.Cos(i * 0.37f)),
                    Scale = new Vector4(1f, 1f, 1f, 1f),
                    NameId = GameTableManager.Instance.PropDefs.GetEntry(ChestProp)?.NameStringId ?? 0
                };
                chests.Add(chest);
            }
        }

        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextStream || chests.Count == 0)
                return;
            nextStream = now.AddSeconds(1);
            foreach (Chest c in chests.Where(c => !c.Ready && now >= c.Back))
            {
                c.Ready = true;
                foreach (WorldSession s in Viewers(c))
                    Show(s, c);
            }
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                if (session.IsArriving)
                    continue;
                HashSet<int> have;
                lock (sync)
                    if (!sent.TryGetValue(session, out have))
                        sent[session] = have = new HashSet<int>();
                bool home = session.OnHomeContinent;
                var shown = new List<ulong>();
                foreach (Chest c in chests)
                {
                    float d = Vector3.Distance(c.At, session.Position);
                    if (home && d <= ViewRange && have.Add(c.Index))
                    {
                        if (c.Ready)
                        {
                            Show(session, c);
                            shown.Add(c.Prop.Guid);
                        }
                    }
                    else if ((!home || d > DropRange) && have.Remove(c.Index) && c.Ready)
                        session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = c.Prop.Guid });
                }
                if (shown.Count > 0)
                {
                    var flagging = new DynamicInteractionFlagging();
                    flagging.Guids.AddRange(shown);
                    session.EnqueueMessage(flagging);
                }
            }
        }

        private static void Show(WorldSession session, Chest c)
        {
            session.EnqueueMessage(c.Prop);
            HitEffects.PlayOn(c.Prop.Guid, Glow, new[] { session });
        }

        private static List<WorldSession> Viewers(Chest c)
        {
            lock (sync)
                return sent.Where(s => s.Value.Contains(c.Index)).Select(s => s.Key).ToList();
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
                sent.Remove(session);
        }

        private static Chest ByGuid(ulong guid) =>
            (guid & 0xFFFF_0000_0000_0000) == GuidBase ? chests.ElementAtOrDefault((int)(guid & 0xFFFF_FFFF)) : null;

        public static bool IsChest(ulong guid) => ByGuid(guid) != null;

        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Chest c)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = c.Prop.TransientId });
            return true;
        }

        /// <summary>The chest opens: everyone within 50 m gets their own resources; it comes back after a while.</summary>
        public static void OpenChest(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Chest c || !c.Ready || Health.IsDead(session))
                return;
            c.Ready = false;
            c.Back = DateTime.UtcNow + Respawn;
            List<WorldSession> viewers = Viewers(c);
            HitEffects.PlayOn(c.Prop.Guid, Open, viewers);
            uint[] resources = { 34, 35, 36, 37, 39, 51, 56, 57, 58, 2000, 2001, 2002 };
            int sharers = 0;
            foreach (WorldSession p in PlayerManager.Snapshot().Where(p => p.OnHomeContinent && !Health.IsDead(p)
                && Vector3.Distance(p.Position, c.At) <= ShareRange))
            {
                var loot = new List<(uint, uint)>();
                for (int i = 0; i < 3; i++)
                    loot.Add((resources[random.Next(resources.Length)], (uint)random.Next(c.Deep ? 6 : 4, c.Deep ? 16 : 11)));
                if (random.NextDouble() < 0.1)
                    loot.Add((Gathering.AdventureEssence, 1));
                LootPinatas.SpawnBag(p, c.At + new Vector3(0f, 0.6f, 0f), loot);
                sharers++;
            }
            foreach (WorldSession s in viewers)
                s.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = c.Prop.Guid });
            log.Info($"{session.CharacterName} opened a treasure chest ({sharers} share the loot).");
        }

        /// <summary>Dev: the nearest chest to a player (index, distance), or null.</summary>
        public static (int Index, float Distance, ulong Guid)? Nearest(Vector3 at)
        {
            Chest c = chests.OrderBy(x => Vector3.Distance(x.At, at)).FirstOrDefault();
            return c == null ? null : (c.Index, Vector3.Distance(c.At, at), c.Prop.Guid);
        }

        public static Vector3 PositionOf(int index) => chests[index].At;
    }
}
