using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Loot pinatas: things that drop into the world and fly into their owner when they walk near — the resource chunks
    /// that pop out of a mined hole, a chopped tree or a picked plant, and the loot bags creatures drop.
    ///
    /// LootPinataBase 0xFB, u8 sub:
    /// - 1 SpawnLoot (S→C, reader FUN_1430775a0, handler FUN_14307a310): u32 loot id, u32 definition (LootPinata.txt:
    ///   model; 5 = loot bag, 21 stone, 79 plain wood, 85 cotton…), vec3 position, u32 item, u32, u32 count, f32,
    ///   u64 owner, string, u32 kind (0 keeps the position; 3 = harvest, tells the action manager), u32 ×3.
    /// - 5 PickupLoot (C→S {u32 id, u64 player}) when one reaches its owner (FUN_14307d0f0: auto-scoop in range);
    ///   answered by 3 PickupLootResponse {u32 id, u64 player, bool} which removes it.
    /// - 2 RemoveLoot (S→C {u32 id, u64 player}): the server clears one that was never collected.
    /// - 7 RequestClaimOwnerlessLoot (C→S {u32 id, u64 none}) and 4 NotifyOwnerlessLootClaimed (S→C {u32 id, u64 new
    ///   owner}): the claim of a pinata nobody owns (<see cref="ClaimOwnerless"/>).
    ///
    /// Harvest chunks are only looks (the resources were given at the swing); a loot bag carries its items, given on
    /// pickup. Uncollected pinatas are removed after <see cref="ChunkLinger"/> / <see cref="BagLinger"/>.
    /// </summary>
    public static class LootPinatas
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte SpawnLootSub = 1, RemoveLootSub = 2, PickupLootResponseSub = 3, NotifyOwnerlessLootClaimedSub = 4,
            PickupLootSub = 5, ClaimOwnerlessLootSub = 7;

        /// <summary>LootPinata.txt loot bag (Theme_Loot_Containers_Bag01).</summary>
        public const uint LootBag = 5;
        /// <summary>Fallback chunk for resources without a model of their own (stone).</summary>
        public const uint DefaultChunk = 21;

        /// <summary>
        /// How long an uncollected pinata lies before the server removes it. Bags: the client constant
        /// <c>LootPinata.DecayAge = 120</c> (StringHashToValue.txt:374). Its oaat hash 0x65741807 is read nowhere in the
        /// 0.695 client, so it is a server value shipped in the shared table [P unread]; that it is the lifetime in
        /// seconds is [I] (the name and the unit of its neighbours, e.g. MaxDroppingTime 7). Chunks are looks only and
        /// fly in at once, so they keep a short life.
        /// </summary>
        private static readonly TimeSpan ChunkLinger = TimeSpan.FromSeconds(10), BagLinger = TimeSpan.FromSeconds(120);

        /// <summary>
        /// <c>LootPinata.DistanceToPickupOwnerlessLoot = 5.0</c> (StringHashToValue.txt:376, hash 0x1d59bdcd read by the
        /// per-pinata update FUN_143127a10): a client asks to claim only within 5 m of the pinata. The server allows
        /// slack for the pinata's drop arc and movement lag [I].
        /// </summary>
        private const float OwnerlessClaimRange = 5f + 5f;

        private static int nextId;

        /// <summary>
        /// A pinata not collected yet: its owner (null = ownerless, anyone near may claim it — replaced by the claimer
        /// once a claim is granted), what it holds, who sees it and where it was dropped.
        /// </summary>
        private sealed record Pending(WorldSession Owner, IReadOnlyList<(uint Item, uint Count)> Contents, IReadOnlyList<WorldSession> Viewers,
            Vector3 Position);

        /// <summary>Pinatas not collected yet, by loot id.</summary>
        private static readonly ConcurrentDictionary<uint, Pending> pending = new();

        /// <summary>Overrides where the name match picks the wrong model (resource item → LootPinata.txt id).</summary>
        private static readonly Dictionary<uint, uint> pinataOverrides = new()
        {
            [44] = 14, [45] = 19, [46] = 21, [76] = 43,
            [34] = 59, [35] = 63, [36] = 61, [37] = 75, [39] = 69,
            [47] = 16, [48] = 12, [49] = 18, [50] = 10, [51] = 23,
            [71] = 79, [72] = 77, [73] = 81,
        };

        private static Dictionary<uint, uint> byItem;

        /// <summary>
        /// The chunk model for a resource item: an override, else the LootPinata.txt model whose name words are all in
        /// the item's name ("Biome_Generic_Loot_Wood_Plain01" → plain, wood; "…Plants_Cotton_Loot01" → cotton;
        /// "…Metals01_Dull_Copper01" → copper), else stone.
        /// </summary>
        public static uint PinataFor(uint item)
        {
            if (pinataOverrides.TryGetValue(item, out uint pinata))
                return pinata;
            byItem ??= BuildNameIndex();
            return byItem.TryGetValue(item, out pinata) ? pinata : DefaultChunk;
        }

        private static readonly HashSet<string> modelNoise = new()
        {
            "biome", "generic", "loot", "plants", "earth", "liquid", "gemstone", "metals", "dull", "shiny", "emmisive",
            "resource", "dme", "adr", "ashfang", "everfrost", "crystalcavern", "junglecavern", "forestcavern",
            "desertcavern", "lavastorm", "succulents", "flowers", "scrub"
        };

        private static HashSet<string> Words(string text)
            => Regex.Split(Regex.Replace(text, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(), "[^a-z]+")
                .Where(w => w.Length > 1 && !modelNoise.Contains(w)).ToHashSet();

        private static Dictionary<uint, uint> BuildNameIndex()
        {
            // Resource models only: ids up to 106 (above are gear models for gear drops).
            var models = GameDataFiles.Rows("LootPinata.txt").Where(r => r.U(0) is > 9 and <= 106)
                .Select(r => (Id: r.U(0), Words: Words(r.S(1)))).Where(m => m.Words.Count > 0).ToList();
            var map = new Dictionary<uint, uint>();
            foreach (var item in Inventory.ItemDefinitionManager.All)
            {
                string name = TextManager.Instance.GetTextForId(item.NameId) ?? "";
                if (name.Length == 0)
                    continue;
                HashSet<string> words = Words(name);
                var best = models.Where(m => m.Words.IsSubsetOf(words)).OrderByDescending(m => m.Words.Count).FirstOrDefault();
                if (best.Id != 0)
                    map[item.Id] = best.Id;
            }
            log.Info($"{map.Count} items have a loot chunk model.");
            return map;
        }

        /// <summary>
        /// PickupLoot from the client: a pinata reached its owner. Gives a bag's contents, then always answers so the
        /// client removes it.
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (body.Length < 4)
                return;
            uint id = BitConverter.ToUInt32(body, 0);
            if (sub == ClaimOwnerlessLootSub)
            {
                ClaimOwnerless(session, id);
                return;
            }
            if (sub != PickupLootSub)
                return;
            // Only the owner's pickup takes it (another player's request no longer removes the owner's loot).
            if (pending.TryGetValue(id, out Pending loot) && loot.Owner == session && pending.TryRemove(id, out loot))
            {
                foreach ((uint item, uint count) in loot.Contents)
                {
                    session.GiveItem(item, count);
                    Notify(session, item, count);
                }
                // A once-ownerless bag: the other viewers' copies go now (a viewer whose copy never reaches the claimer
                // would otherwise keep it lying on the ground; agent-loot-rule.md).
                foreach (WorldSession other in loot.Viewers)
                    if (other != session && !other.Disconnected)
                        other.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, RemoveLoot(id, other.PlayerGuid));
            }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(PickupLootResponseSub);
            w.Write(id);
            w.Write(session.PlayerGuid);
            w.Write(true);
            session.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, output.ToArray());
        }

        /// <summary>
        /// Inventory LootNotification (0x2D, u16 sub 0x1A; reader FUN_142e78620, handler FUN_142e81590 →
        /// ClientPlayerInventoryManager::DisplayLootNotification FUN_142e7f050): {string loot name, u32 quality (its colour
        /// from the client's quality table), u32 count, u32 icon} → the "LootNotificationMultiple" toast
        /// (NotificationWindow:OnShowFormattedMessage) when loot reaches the player.
        /// </summary>
        public static void Notify(WorldSession session, uint item, uint count)
        {
            var def = Inventory.ItemDefinitionManager.Get(item);
            if (def == null || count == 0)
                return;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(0x1A);
            w.Write(Shared.GameTable.Text.TextManager.Instance.GetTextForId(def.NameId) ?? "");
            w.Write(def.Quality);
            w.Write(count);
            w.Write(def.ImageSetId);
            session.EnqueueMessage(ZoneMessageOpcode.InventoryBase, output.ToArray());
        }

        /// <summary>
        /// RequestClaimOwnerlessLoot (LootPinataBase sub 7) {u32 loot id, u64 none}. The client's per-pinata update
        /// FUN_143127a10 sends it on every update while its player is within DistanceToPickupOwnerlessLoot (5 m) of a
        /// pinata whose owner (+0x200) is none, and puts the pinata in state 2 (claim requested). The answer the client
        /// expects is <b>NotifyOwnerlessLootClaimed (sub 4) {u32 id, u64 new owner}</b> (reader FUN_143121c60, case 4 of
        /// LootPinataManager::HandlePacket FUN_143125240): it writes the new owner to +0x200 / +0x70 and sets state 3
        /// (scooping). From then on every viewer's copy flies to the owner entity; the owner's own client sends the
        /// ordinary PickupLoot (sub 5) on arrival — answered by <see cref="Handle"/>'s owner branch with the contents and
        /// PickupLootResponse — and every other viewer's client just sets its destroy flag (+0x1E4) on arrival, or decays
        /// it (state 4) at once when it does not know the owner entity. So:
        /// - the first claim from a viewer in range sets the owner and sends sub 4 to every viewer (claimer included);
        ///   the contents are not given yet;
        /// - repeated claims (the claimer's own, sent each update until sub 4 arrives) and losing claims get <b>no
        ///   reply</b>: PickupLootResponse (sub 3) and RemoveLoot (sub 2) both delete the pinata at once
        ///   (FUN_143121d20 / FUN_143121e00 → FUN_143126f70), which would cut the fly-to-the-claimer animation short;
        /// - the RemoveLoot cleanup after <see cref="BagLinger"/> (<see cref="RemoveIfLeft"/>) still reaches every viewer,
        ///   for a claimer who never picks it up (a full bag: FUN_143124180 drops it back to state 1).
        /// When live made loot ownerless is open (OPEN-LEADS L141): <see cref="SpawnOwnerless"/> is the way to make one.
        /// </summary>
        private static void ClaimOwnerless(WorldSession session, uint id)
        {
            if (!pending.TryGetValue(id, out Pending loot) || loot.Owner != null || !loot.Viewers.Contains(session))
                return;
            float distance = Vector3.Distance(session.Position, loot.Position);
            if (distance > OwnerlessClaimRange)
            {
                log.Debug($"{session.CharacterName} claimed ownerless loot {id} from {distance:0.0} m; ignored.");
                return;
            }
            // First claim wins: only one caller can swap the ownerless entry for an owned one.
            if (!pending.TryUpdate(id, loot with { Owner = session }, loot))
                return;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(NotifyOwnerlessLootClaimedSub);
            w.Write(id);
            w.Write(session.PlayerGuid);
            byte[] notify = output.ToArray();
            foreach (WorldSession viewer in loot.Viewers)
                if (!viewer.Disconnected)
                    viewer.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, notify);
            log.Info($"{session.CharacterName} claimed ownerless loot {id}.");
        }

        /// <summary>RemoveLoot (sub 2) {u32 id, u64 player}.</summary>
        private static byte[] RemoveLoot(uint id, ulong player)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(RemoveLootSub);
            w.Write(id);
            w.Write(player);
            return output.ToArray();
        }

        /// <summary>
        /// A loot bag with no owner, shown to <paramref name="viewers"/>: the spawn's owner guid is 0 (the client's "none"
        /// guid DAT_145453608 has no writer, so it is 0), so each client shows it — FUN_143125240 sub 1 shows a pinata only
        /// when it is yours or ownerless — and asks to claim it when its player comes near (<see cref="ClaimOwnerless"/>).
        /// Nothing calls this yet: when live made loot ownerless is a server rule the client does not carry (L141, for the
        /// user to decide).
        /// </summary>
        public static void SpawnOwnerless(IReadOnlyList<WorldSession> viewers, Vector3 position, IReadOnlyList<(uint Item, uint Count)> contents)
        {
            if (viewers.Count == 0 || contents.Count == 0)
                return;
            uint id = (uint)Interlocked.Increment(ref nextId);
            pending[id] = new Pending(null, contents, viewers, position);
            Task.Delay(BagLinger).ContinueWith(_ => RemoveIfLeft(id));
            // The last three SpawnLoot fields (+0x64 / +0x68 / +0x6C) are floats the client uses only for an ownerless
            // pinata: its throw angle, heading and speed, so every viewer sees the same bag land in the same place
            // (owned bags roll them locally; 0x143123311-0x14312341a, trace/0695/agent-loot-rule.md). One roll per bag.
            uint angle = BitConverter.SingleToUInt32Bits(Random.Shared.NextSingle()),
                heading = BitConverter.SingleToUInt32Bits(Random.Shared.NextSingle()),
                speed = BitConverter.SingleToUInt32Bits(Random.Shared.NextSingle());
            byte[] spawn = SpawnBody(id, LootBag, position, 0, 0, 0, angle, heading, speed, 0ul);
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, spawn);
        }

        /// <summary>A harvest chunk (looks only): a resource's chunk that flies into the player.</summary>
        public static void SpawnChunk(WorldSession owner, Vector3 position, uint item, uint count = 1)
            => Spawn(owner, position, PinataFor(item), item: item, count: Math.Max(1u, count));

        /// <summary>A loot bag holding items, given to the owner when they collect it.</summary>
        public static void SpawnBag(WorldSession owner, Vector3 position, IReadOnlyList<(uint Item, uint Count)> contents)
        {
            if (contents.Count == 0)
                return;
            Spawn(owner, position, LootBag, contents: contents);
        }

        /// <summary>
        /// Spawns a pinata for one player: SpawnLoot with a new id at the position. Kind 0 keeps the position (kind 3
        /// makes the client zero it).
        /// </summary>
        public static void Spawn(WorldSession owner, Vector3 position, uint pinata, uint kind = 0, uint a = 0, uint b = 0, uint c = 0,
            IReadOnlyList<(uint Item, uint Count)> contents = null, uint item = 0, uint count = 0)
        {
            uint id = (uint)Interlocked.Increment(ref nextId);
            pending[id] = new Pending(owner, contents ?? Array.Empty<(uint, uint)>(), new[] { owner }, position);
            Task.Delay(contents is { Count: > 0 } ? BagLinger : ChunkLinger).ContinueWith(_ => RemoveIfLeft(id));
            owner.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, SpawnBody(id, pinata, position, item, count, kind, a, b, c, owner.PlayerGuid));
        }

        /// <summary>SpawnLoot (sub 1) for one loot id; <paramref name="owner"/> 0 = ownerless.</summary>
        private static byte[] SpawnBody(uint id, uint pinata, Vector3 position, uint item, uint count, uint kind, uint a, uint b, uint c, ulong owner)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(SpawnLootSub);
            w.Write(id);
            w.Write(pinata);
            w.Write(position.X);
            w.Write(position.Y);
            w.Write(position.Z);
            // Then the pickup's item: u32 item definition, u32 (a looked-up id, 0 = default), u32 count, u32
            // (pinata ctor FUN_143078130: [5] -> +0x1c4 item (looked up in the item definitions), [0xb] -> +0x1c8,
            // [0xc] -> +0x1cc count, [0xd] -> +0x1d0; the position is [2..4], the vec3 above). The client checks these
            // before scooping it up (FUN_143079860: can the bag or the resource/Collections container take item,
            // count?) - a copy of the position here was read as a nonsense item, so with a full bag even resources
            // that go to Collections were refused ("Your inventory is full!", 2026-10-14).
            w.Write(item);
            w.Write(0u);
            w.Write(count);
            w.Write(1f);
            w.Write(owner);
            w.Write("");
            w.Write(kind);
            w.Write(a);
            w.Write(b);
            w.Write(c);
            return output.ToArray();
        }

        /// <summary>Clears a pinata its owner never collected (RemoveLoot); a bag's items are lost with it.</summary>
        private static void RemoveIfLeft(uint id)
        {
            if (!pending.TryRemove(id, out Pending loot))
                return;
            foreach (WorldSession viewer in loot.Viewers)
                if (!viewer.Disconnected)
                    viewer.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, RemoveLoot(id, viewer.PlayerGuid));
        }
    }
}
