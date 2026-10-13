using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Props placed in the world (campfires, furniture, crafting stations). Kept in memory and shown to everyone in
    /// the world, including players who arrive later.
    /// </summary>
    public static class Props
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public class PlacedProp
        {
            public PlayerUpdateAddLightweightProp Spawn { get; init; }
            public uint PropId { get; init; }
            public ulong OwnerGuid { get; init; }
            /// <summary>The landscape it stands on; only players there are sent it.</summary>
            public uint Zone { get; set; }
            /// <summary>False while its owner is still positioning it; only the owner sees it then.</summary>
            public bool Placed { get; set; }
            /// <summary>Spawned for its owner's placement; the session starts once the prop is full.</summary>
            public bool AwaitingPlacement { get; set; }
            /// <summary>The item definition that pays for this placement (0: the prop's recipe materials).</summary>
            public uint PaidByItem { get; set; }
            /// <summary>The inventory item (guid) being placed, when placing from the bag.</summary>
            public ulong PaidByItemGuid { get; set; }
            /// <summary>Placed for free (a site game's marker added from the game editor): costs nothing.</summary>
            public bool Free { get; set; }
            /// <summary>When its owner last moved it after placement; others are updated once it settles.</summary>
            public DateTime? MovedAt { get; set; }
            /// <summary>The owner's own name for it (PropBase 0x19 SetPropName; PropDefs ALLOW_USER_RENAME).</summary>
            public string CustomName { get; set; } = "";
            /// <summary>Sign text (PropBase 0x13 UpdatePropText).</summary>
            public string Text { get; set; } = "";
            /// <summary>Switched off with PropBase 0x1A SetPropEnable: its light or effect is dark.</summary>
            public bool Disabled { get; set; }
        }

        /// <summary>
        /// A prop as a copy or a saved design keeps it (TemplateCommon::PropData on the wire): its definition, where it
        /// stands from the box's minimum corner (metres), its turn (quaternion) and size, tint, name and sign text.
        /// </summary>
        public sealed record DesignProp(uint PropId, Vector3 Offset, Vector4 Rotation, Vector4 Scale, string Tint, string Name, string Text);

        /// <summary>The placed props (anyone's) standing inside a box (local metres), as design props from its corner.</summary>
        public static List<DesignProp> InBox(uint zone, Vector3 min, Vector3 max)
        {
            lock (sync)
                return props.Where(p => p.Placed && p.Zone == zone && !p.Free
                        && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                        && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                        && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z)
                    .Select(p => new DesignProp(p.PropId, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z) - min,
                        p.Spawn.Rotation, p.Spawn.Scale, p.Spawn.TintAlias ?? "", p.CustomName ?? "", p.Text ?? ""))
                    .ToList();
        }

        /// <summary>Players this close (metres) to a prop see the text it displays.</summary>
        private const float TextDisplayRange = 60f;

        /// <summary>
        /// Shows a prop's text (Links "...Have this sign display the text."): PlayerUpdate 0x79 DisplayPropText (reader
        /// FUN_142b8e6c0, handler FUN_142ee6ce0) {u64 prop, string text, u32 mode, u32 seconds} to the players near it.
        /// Modes: 1 the text window (HudStateManager:OnOpenTextDisplay), 2 on screen (FFF6AA), 3 chat
        /// "[prop name] says: text", 4 a bubble over the prop, 5 bubble and chat. Seconds ≤ 0 use the client's default.
        /// </summary>
        public static void DisplayText(ulong guid, uint mode, uint seconds)
        {
            PlacedProp prop = Find(guid);
            if (prop == null || string.IsNullOrEmpty(prop.Text))
                return;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(guid);
            writer.Write(prop.Text);
            writer.Write(mode);
            writer.Write(seconds);
            byte[] body = output.ToArray();
            var at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            foreach (WorldSession session in PlayerManager.InZone(prop.Zone))
                if (Vector3.Distance(session.Position, at) <= TextDisplayRange)
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateDisplayPropText, body);
        }

        /// <summary>A prop definition's name text (for the skipped-props list).</summary>
        public static uint NameIdOf(uint propId) => GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;

        /// <summary>
        /// Places one prop of a pasted copy or design, paid with an item of it from the player's bag (a design gives the
        /// layout, not the props). Returns its guid, or 0 when the player has none of it, it has no model, or the spot
        /// is not theirs to build on.
        /// </summary>
        public static ulong PlaceDesignProp(WorldSession owner, DesignProp design, Vector3 position, Vector4 rotation, out bool missing)
        {
            missing = false;
            if (!propModels.TryGetValue(design.PropId, out uint modelId) || !MayPlaceAt(owner, position, owner.ZoneId))
                return 0;
            var item = Inventory.ItemDefinitionManager.ItemsForProp(design.PropId)
                .SelectMany(d => owner.Inventory.Items.Where(i => i.ItemDefinitionId == d.Id && i.Count > 0))
                .FirstOrDefault();
            if (item == null || !owner.TakeItem(item.ItemDefinitionId, 1))
            {
                missing = true;
                return 0;
            }
            PlacedProp prop = Create(owner, design.PropId, modelId, NameIdOf(design.PropId), position);
            prop.Spawn.Rotation = rotation;
            prop.Spawn.Scale = design.Scale;
            prop.Spawn.TintAlias = design.Tint ?? "";
            prop.CustomName = design.Name ?? "";
            prop.Text = design.Text ?? "";
            prop.PaidByItem = item.ItemDefinitionId;
            prop.Placed = true;
            Persist(prop);
            Broadcast(prop);
            Achievements.PropPlaced(owner, prop.PropId);
            if (Chests.Slots(prop.PropId) > 0)
                PropLimits.Changed(prop.Zone, position);
            return prop.Spawn.Guid;
        }

        /// <summary>A placed prop's position, rotation and scale (PropBase Save vectors 0-2).</summary>
        public readonly record struct Transform(Vector4 Position, Vector4 Rotation, Vector4 Scale);

        /// <summary>
        /// Each player's open edit session of a placed prop (granted PropAccess): the prop and where it stood when the
        /// session opened, for the undo step its moves make (Building.RememberPropMove).
        /// </summary>
        private sealed class EditSession
        {
            public ulong Guid { get; init; }
            public Transform Start { get; init; }
        }

        private static readonly Dictionary<ulong, EditSession> editSessions = new();

        /// <summary>
        /// Undo / redo of a prop move: back to <paramref name="to"/> when the player may still rearrange it there.
        /// The player's client is told with PropBase 0x10 UndoRedoNotification (reader FUN_142f09990, handler
        /// FUN_142f1c880): u8 sub, u64 player, u64 prop, vec4 position, vec4 rotation, vec4 scale, u32 (passed to the
        /// prop tool's observers, FUN_142f1aaa0); it moves its copy and any open gizmo. Others get the move like any
        /// other once it settles (Update).
        /// </summary>
        public static bool UndoMove(WorldSession player, ulong guid, Transform to)
        {
            PlacedProp prop = Find(guid);
            var target = new Vector3(to.Position.X, to.Position.Y, to.Position.Z);
            if (prop == null || !prop.Placed || prop.Zone != player.ZoneId || !MayRearrange(player, prop)
                || (prop.OwnerGuid == player.PlayerGuid ? !MayPlaceAt(player, target, prop.Zone) : !MayDecorateAt(player, target, prop.Zone)))
                return false;
            prop.Spawn.Position = to.Position;
            prop.Spawn.Rotation = to.Rotation;
            prop.Spawn.Scale = to.Scale;
            prop.MovedAt = DateTime.UtcNow;
            Persist(prop);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)0x10);
            writer.Write(player.PlayerGuid);
            writer.Write(guid);
            foreach (Vector4 v in new[] { to.Position, to.Rotation, to.Scale })
            {
                writer.Write(v.X);
                writer.Write(v.Y);
                writer.Write(v.Z);
                writer.Write(v.W);
            }
            writer.Write(0u);
            player.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
            return true;
        }

        /// <summary>How long a moved prop must stay put before other players are sent its new place.</summary>
        private static readonly TimeSpan MoveSettle = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Sends settled prop moves to the other players (server tick). A drag produces a Save every half second;
        /// relaying each would cost a despawn + spawn per step, so only the final position goes out.
        /// </summary>
        public static void Update()
        {
            List<PlacedProp> settled = null;
            DateTime now = DateTime.UtcNow;
            lock (sync)
                foreach (PlacedProp prop in props)
                    if (prop.MovedAt is DateTime moved && now - moved >= MoveSettle)
                    {
                        prop.MovedAt = null;
                        (settled ??= new()).Add(prop);
                    }
            if (settled == null)
                return;
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                List<PlacedProp> theirs = settled.Where(p => p.Zone == session.ZoneId && p.OwnerGuid != session.PlayerGuid).ToList();
                if (theirs.Count == 0)
                    continue;
                foreach (PlacedProp prop in theirs)
                    SendRemove(session, prop);
                SendSpawns(session, theirs);
            }
        }

        private static readonly object sync = new();
        private static readonly List<PlacedProp> props = new();
        // Above item and character guids.
        private static long nextGuid = 0x2000000;

        /// <summary>
        /// Prop id → Models.txt id, read from the 0.695 prop definition list we hold (the 0.589 client has the prop
        /// table, PropDefs.txt, but not the models).
        /// </summary>
        private static Dictionary<uint, uint> propModels = new();

        /// <summary>
        /// Model shown for props placed by using an item (item definition id → Models.txt id).
        /// </summary>
        private static readonly Dictionary<uint, uint> itemModels = new()
        {
            [3894] = 2 // Cozy Campfire → Freeport stone-circle campfire
        };

        /// <summary>
        /// Reads the prop definition dump: opcode bytes, u8 sub, u64, u8, u32 count, then per prop: model file, u32 prop
        /// id, two names, f32, f32, u32 image set, u32, u32, u32 model id.
        /// </summary>
        public static void LoadDefinitions(string dumpPath)
        {
            var models = new Dictionary<uint, uint>();
            var modelNames = new Dictionary<uint, string>();
            var effectInfo = new Dictionary<uint, (string, string)>();
            var internalNames = new Dictionary<uint, string>();
            var reader = new GamePacketReader(File.ReadAllBytes(dumpPath));
            reader.ReadBytes(2 + 1 + 8 + 1);
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                string modelName = reader.ReadString();
                uint propId = reader.ReadUInt();
                string internalName = reader.ReadString();
                string description = reader.ReadString();
                reader.ReadSingle();
                reader.ReadSingle();
                reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadUInt();
                uint modelId = reader.ReadUInt();
                models.TryAdd(propId, modelId);
                modelNames.TryAdd(propId, modelName);
                effectInfo.TryAdd(propId, (modelName, description));
                internalNames.TryAdd(propId, internalName);
            }
            FixMissingModels(models, internalNames, modelNames);
            propModels = models;
            propInternalNames = internalNames;
            CraftingStations.Build(modelNames);
            PropEffects.Build(effectInfo);
            log.Info($"Loaded {models.Count} prop models.");
        }


        /// <summary>
        /// Furthest a prop can be put from the player; further targets (the cursor on the horizon) put it at their
        /// feet instead, nudged toward the target.
        /// </summary>
        private const float MaxPlaceDistance = 5f;

        /// <summary>
        /// Places a temporary prop by using an item (the Cozy Campfire): it goes straight into the world near the aim
        /// point, isn't saved (the item's effect removes it again) and is returned as (guid, position).
        /// </summary>
        public static (ulong Guid, Vector3 Position) PlaceFromItem(WorldSession owner, uint itemDefinitionId, Vector3 target)
        {
            Vector3 feet = owner.Position;
            Vector3 position = target;
            if (Vector3.Distance(target, feet) > MaxPlaceDistance)
            {
                Vector3 toward = new(target.X - feet.X, 0f, target.Z - feet.Z);
                position = toward.LengthSquared() > 0.01f ? feet + Vector3.Normalize(toward) * 2f : feet;
            }

            var definition = Inventory.ItemDefinitionManager.Get(itemDefinitionId);
            PlacedProp prop = Create(owner, 0, itemModels[itemDefinitionId], definition?.NameId ?? 0, position);
            prop.Placed = true;
            Broadcast(prop);
            return (prop.Spawn.Guid, position);
        }

        /// <summary>
        /// The build palette asked to place a prop: spawn it for its owner and start the client's placement session
        /// (PropBase sub 2), which lets the player move it; the client then sends Save.
        /// </summary>
        public static PlacedProp BeginPlacement(WorldSession owner, uint propId, uint paidByItem = 0, ulong paidByItemGuid = 0, bool free = false)
        {
            if (!propModels.TryGetValue(propId, out uint modelId))
            {
                log.Debug($"Placement of unknown prop {propId}.");
                return null;
            }
            if (paidByItem != 0 ? owner.Inventory.CountOf(paidByItem) == 0 : !free && !FreeBuild && !CanAfford(owner, propId))
            {
                log.Info($"{owner.CharacterName} tried to place prop {propId} without the materials or item for it.");
                return null;
            }
            if (paidByItem == 0 && !free && !FreeBuild && !RecipeBook.KnowsProp(owner, propId))
            {
                Chat.System(owner, "You haven't learned how to place that yet.");
                log.Info($"{owner.CharacterName} tried to place prop {propId} without knowing its recipe.");
                return null;
            }
            // Props go only where the player may build. The live server refused the request (PropBase sub 2, success
            // false): the client's placement handler (FUN_142f1ab30) then resets its placement state and animation
            // (FUN_142fe7780) and shows the error. Without the refusal the client was left holding a prop it could
            // neither move nor cancel.
            if (!MayPlaceAt(owner, owner.Position, owner.ZoneId))
            {
                owner.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildRefusal(owner.PlayerGuid, NoClaimPermission));
                log.Info($"{owner.CharacterName} tried to place prop {propId} where they may not build.");
                return null;
            }
            if (PlacementRefusal(owner, propId) is (uint error, ulong detail))
            {
                owner.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildRefusal(owner.PlayerGuid, error, detail));
                log.Info($"{owner.CharacterName} was refused prop {propId}: error {error}.");
                return null;
            }
            var definition = GameTableManager.Instance.PropDefs.GetEntry(propId);
            PlacedProp prop = Create(owner, propId, modelId, definition?.NameStringId ?? 0, owner.Position);
            prop.AwaitingPlacement = true;
            prop.PaidByItem = paidByItem;
            prop.PaidByItemGuid = paidByItemGuid;
            prop.Free = free && paidByItem == 0;
            // The placement session (FUN_142f1ab30) only takes a full prop; the client asks for the full data after
            // the spawn, and the session is started once that is sent (see SendFull).
            SendSpawns(owner, new[] { prop });
            log.Info($"{owner.CharacterName} is placing prop {propId} (model {modelId}).");
            return prop;
        }

        /// <summary>
        /// The prop dump was captured from a later client build: a few props name model ids this client's Models.txt
        /// doesn't have (the spawn points 1223 / 1367 / 1371 use 1739). The client then creates no entity for them at
        /// all (BaseClient::HandlePlayerUpdateAddLightweightProp FUN_142c44700 → FUN_142eebb10 fails): invisible, and
        /// the game editor's Go To can't find them. Such props get the model named like their internal name
        /// ("prop: internal: Theme_Widgets_RuleSetObjects01_SpawnLocation01 [Blue]" → model 1699), and props sharing
        /// the same missing id get the same replacement.
        /// </summary>
        private static void FixMissingModels(Dictionary<uint, uint> models, Dictionary<uint, string> internalNames,
            Dictionary<uint, string> modelFiles)
        {
            var known = new HashSet<uint>();
            var byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] row in GameDataFiles.Rows("Models.txt"))
            {
                if (row.Length < 5 || !uint.TryParse(row[0], out uint id))
                    continue;
                known.Add(id);
                byName.TryAdd(row[1], id);
                byName.TryAdd(row[4] + ".adr", id);
            }
            if (known.Count == 0)
                return;
            var replaced = new Dictionary<uint, uint>();
            var perProp = new Dictionary<uint, uint>();
            foreach (uint propId in models.Keys.ToList())
            {
                uint model = models[propId];
                if (known.Contains(model))
                    continue;
                string name = internalNames.TryGetValue(propId, out string n) ? n : "";
                int at = name.IndexOf("internal:", StringComparison.OrdinalIgnoreCase);
                name = (at >= 0 ? name[(at + 9)..] : name).Trim();
                name = name.Split(' ', '[')[0];
                if (name.Length > 0 && byName.TryGetValue(name + ".adr", out uint fix))
                    replaced.TryAdd(model, fix);
            }
            // Props whose dump name says nothing ("landmark_v2:"): their PropDefs name text ("[auto] Theme_Widgets_
            // DMWidgets_RandomNode01"). The Theme_Widgets_DMWidgets_* model rows (1666...) create no entity in this
            // build; the story widgets that work use Char_SimpleObject_RuleSetObjects01_DMWidgets_* (Dice 1657, Timer
            // 1659, Logic Filter 1655), so that variant is preferred; logic widgets with no model get the Logic Node.
            foreach (uint propId in models.Keys.ToList())
            {
                if (known.Contains(models[propId]) || replaced.ContainsKey(models[propId]))
                    continue;
                uint nameId = GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;
                string text = nameId != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? "" : "";
                text = text.Replace("[auto]", "").Trim().Split(' ', '[')[0];
                const string themeWidget = "Theme_Widgets_DMWidgets_", charWidget = "Char_SimpleObject_RuleSetObjects01_DMWidgets_";
                if (text.StartsWith(themeWidget) && byName.TryGetValue(charWidget + text[themeWidget.Length..] + ".adr", out uint widget))
                    perProp[propId] = widget;
                else if (text.Length > 0 && !text.StartsWith(themeWidget) && byName.TryGetValue(text + ".adr", out uint fix))
                    perProp[propId] = fix;
                else if ((modelFiles.TryGetValue(propId, out string file) && (file.Contains("DMWidgets") || file.Contains("Node")))
                    && byName.TryGetValue(charWidget + "LogicNode01.adr", out uint logic))
                    perProp[propId] = logic;
            }
            int fixedCount = 0, missing = 0;
            foreach (uint propId in models.Keys.ToList())
            {
                if (known.Contains(models[propId]))
                    continue;
                if (perProp.TryGetValue(propId, out uint own))
                {
                    models[propId] = own;
                    fixedCount++;
                }
                else if (replaced.TryGetValue(models[propId], out uint fix))
                {
                    models[propId] = fix;
                    fixedCount++;
                }
                else
                    missing++;
            }
            log.Info($"Prop models: {fixedCount} missing from this client replaced by name, {missing} still missing.");
        }

        /// <summary>
        /// Cancels a placement the player hasn't finished (a prop they own that isn't placed yet), as the client's own
        /// Escape would: the placement-start refusal (PropBase sub 2, success false) with error 0 makes the client
        /// reset its placement state silently (FUN_142f1ab30 → FUN_142fe7780; codes 4-8 / 10-13 show a message), then
        /// the prop goes away. Needed before a re-zone: re-zoning in the middle of a placement crashed the client in
        /// the ProxiedCharacter destructor (FUN_142dee1b0, entity +0x4998) — reproduced 2026-10-11 (scenario crashB).
        /// Returns whether there was one.
        /// </summary>
        public static bool CancelPendingPlacement(WorldSession session)
        {
            List<PlacedProp> pending;
            lock (sync)
                pending = props.Where(p => p.OwnerGuid == session.PlayerGuid && !p.Placed).ToList();
            if (pending.Count == 0)
                return false;
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildRefusal(session.PlayerGuid, 0));
            foreach (PlacedProp prop in pending)
                Remove(prop, null);
            log.Info($"{session.CharacterName}: unfinished placement of {pending.Count} prop(s) cancelled (re-zone).");
            return true;
        }

        /// <summary>
        /// Puts a prop straight into the world at an offset from the player (dev command).
        /// </summary>
        public static ulong PlaceNear(WorldSession owner, uint propId, Vector3 offset)
        {
            if (!propModels.TryGetValue(propId, out uint modelId))
                return 0;
            var definition = GameTableManager.Instance.PropDefs.GetEntry(propId);
            PlacedProp prop = Create(owner, propId, modelId, definition?.NameStringId ?? 0,
                owner.Position + offset);
            prop.Placed = true;
            Persist(prop);
            Broadcast(prop);
            if (Chests.Slots(propId) > 0)
                PropLimits.Changed(owner.ZoneId, owner.Position + offset);
            return prop.Spawn.Guid;
        }

        /// <summary>The model a prop definition shows (0 when unknown): for creature spawners, the creature itself.</summary>
        private static Dictionary<uint, string> propInternalNames = new();

        /// <summary>A prop's internal name from the prop dump (e.g. "Spawner: Ruins: Barren Melee (100 Limit)"), or null.</summary>
        public static string InternalName(uint propId) => propInternalNames.TryGetValue(propId, out string n) ? n : null;

        public static uint ModelOf(uint propId) => propModels.TryGetValue(propId, out uint model) ? model : 0;

        /// <summary>Puts a prop straight into the world at a local position on the owner's landscape.</summary>
        public static ulong PlaceAt(WorldSession owner, uint propId, Vector3 position)
            => PlaceNear(owner, propId, position - owner.Position);

        /// <summary>
        /// A new prop owned by a player: its prop definition, a fresh guid and transient id, and the spawn packet at the
        /// given position.
        /// </summary>
        private static PlacedProp Create(WorldSession owner, uint propId, uint modelId, uint nameId, Vector3 position)
        {
            var prop = new PlacedProp
            {
                PropId = propId,
                OwnerGuid = owner.PlayerGuid,
                Zone = owner.ZoneId,
                Spawn = new PlayerUpdateAddLightweightProp
                {
                    Guid = (ulong)Interlocked.Increment(ref nextGuid),
                    TransientId = PlayerManager.AllocateTransientId(),
                    NameId = nameId,
                    ModelId = modelId,
                    Position = new Vector4(position, 1f),
                    OwnerGuid = owner.PlayerGuid
                }
            };
            lock (sync)
                props.Add(prop);
            return prop;
        }

        /// <summary>
        /// PropBase sub 2 (reader FUN_142f08e10, placement session FUN_142f1ab30): header (u64, success), prop guid,
        /// u32 prop id, string, 5 u32, bool editing-existing, rotation quaternion, u32, u32 name id, u8, two lists,
        /// string, two u32.
        /// </summary>
        private static byte[] BuildPropData(ulong playerGuid, PlacedProp prop, bool newPlacement = true)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)2);
            writer.Write(playerGuid);
            writer.Write(true);
            writer.Write(prop.Spawn.Guid);
            writer.Write(prop.PropId);
            writer.Write(prop.CustomName);
            for (int i = 0; i < 5; i++)
                writer.Write(0u);
            // +0x6c "new placement": the session only starts while the client is waiting for its own request (+0x159)
            // or with this set; for a palette placement the 0.589 client clears the wait before our reply arrives, so
            // it must be set. For a placed prop (PropAccess: move / fine-tune) the client is waiting, and the flag must
            // be clear: with it set FUN_142f1ab30 takes the new-placement path and never starts the fine-tune handles
            // (tool state 7 → FUN_142f21d20).
            writer.Write(newPlacement);
            writer.Write(prop.Spawn.Rotation);
            writer.Write(0u);
            writer.Write(prop.Spawn.NameId);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);
            return output.ToArray();
        }

        /// <summary>
        /// Props go on a build site where the player has build permission ("This prop can be placed on your build
        /// site"): unclaimed land, open to mining and terrain edits, takes no props. Positions are landscape-local.
        /// </summary>
        private static bool MayPlaceAt(WorldSession owner, Vector3 local, uint zone)
        {
            if (Claims.ClaimAt(zone, local) == null)
                return false;
            var global = Terrain.TerrainEdits.ToGlobal(local);
            return Claims.MayEdit(owner.PlayerGuid, global.X, global.Y, global.Z, zone);
        }

        /// <summary>
        /// May the player use (click) a prop? Outside sites yes; on a site the "Play Around" access (bit 0x01: "can use
        /// props on your Build Site to have fun", ClaimPermissionAccessLevelMappings) — "No Touching" visitors only look.
        /// </summary>
        public static bool MayUse(WorldSession player, ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop == null || prop.OwnerGuid == player.PlayerGuid)
                return true;
            var global = Terrain.TerrainEdits.ToGlobal(new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z));
            return Claims.Allows(player.PlayerGuid, global.X, global.Y, global.Z, Claims.PermissionUse, prop.Zone);
        }

        /// <summary>May the player rearrange props at a local point (the site's "Let's Decorate" access, 0x08)?</summary>
        private static bool MayDecorateAt(WorldSession player, Vector3 local, uint zone)
        {
            var global = Terrain.TerrainEdits.ToGlobal(local);
            return Claims.Allows(player.PlayerGuid, global.X, global.Y, global.Z, Claims.PermissionDecorate, zone);
        }

        /// <summary>
        /// Placement errors (FUN_142f1ab30): 4 chest cap (with the cap), 5 rule failure (a reason text), 6 PropLocInvalid,
        /// 7 no-trade item, 8 rule set, 10 no claim permission, 11 chest holds soulbound items, 13 not in edit mode.
        /// </summary>
        private const uint LocationInvalid = 6, NoClaimPermission = 10;

        /// <summary>
        /// PropBase sub 2 refusing a placement: the header with success false, the error (u32) and a u64 (FUN_142f09c10),
        /// then the usual body, empty. +0x6c is set as in a granted placement so the client handles the reply.
        /// </summary>
        /// <summary>
        /// Placement refusal codes the client's placement session shows (FUN_142f1ab30, reply +0x24, detail +0x28):
        /// 4 chest limit ("Only [maxCount] chests can be placed", detail = the limit), 6 "You cannot put that there.",
        /// 7 soulbound item on someone else's site, 8 a second game table on a site, 10 no site permission, 11 chest with
        /// soulbound items on someone else's site, 12 site limit for a prop category (detail: category text id in the high
        /// half, limit in the low), 13 the site is in Play Mode.
        /// </summary>
        private const uint ChestLimitReached = 4, SecondGameTable = 8, CategoryLimitReached = 12, SiteInPlayMode = 13;
        /// <summary>PropCategory 93 "Chests" name text: the site's category limit (<see cref="PropLimits"/>).</summary>
        private const ulong ChestsCategoryName = 573121;
        /// <summary>Storage chests an account may have placed (wiki Chests: "up to 50 chests on their Build Sites (per account)").</summary>
        private const int MaxChestsPerAccount = 50;
        /// <summary>Props one build site may hold (client constant Claims.PropLimit).</summary>
        private const int MaxPropsPerSite = 20000;

        /// <summary>
        /// The live server's placement rules beyond build permission, checked where the player stands (where the prop
        /// starts): the site is in Play Mode; a game table where the site already has one; the account's chest limit;
        /// the site's prop limit. Null when the prop may be placed.
        /// </summary>
        private static (uint Error, ulong Detail)? PlacementRefusal(WorldSession owner, uint propId)
        {
            ulong? site = Claims.ClaimAt(owner.ZoneId, owner.Position);
            if (site is ulong siteId)
            {
                if (Claims.IsPlaying(siteId))
                    return (SiteInPlayMode, 0);
                List<(ulong Guid, uint PropId, Vector3 Position, string Name)> onSite;
                lock (sync)
                    onSite = props.Where(p => p.Placed && p.Zone == owner.ZoneId)
                        .Select(p => (p.Spawn.Guid, p.PropId, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z), ""))
                        .Where(p => Claims.ClaimAt(owner.ZoneId, p.Item3) == siteId).ToList();
                if (InstancedGames.IsGameTable(propId) && onSite.Any(p => InstancedGames.IsGameTable(p.PropId)))
                    return (SecondGameTable, 0);
                if (onSite.Count >= MaxPropsPerSite)
                    return (LocationInvalid, 0);
                // "You've reached the limit of [max] placeable props for category: Chests" (error 12; detail = category
                // name text in the high half, the limit in the low half; Prop.PickupErrorReachedClaimPropLimitCategory).
                if (Chests.Slots(propId) > 0 && onSite.Count(p => Chests.Slots(p.PropId) > 0) >= Chests.MaxPerSite)
                    return (CategoryLimitReached, (ChestsCategoryName << 32) | (uint)Chests.MaxPerSite);
            }
            if (Chests.Slots(propId) > 0)
            {
                var characters = DatabaseManager.Instance.CharacterDatabase.GetCharacters(owner.Account.Id)
                    .GetAwaiter().GetResult().Select(c => c.Id).ToHashSet();
                int chests;
                lock (sync)
                    chests = props.Count(p => p.Placed && characters.Contains(p.OwnerGuid) && Chests.Slots(p.PropId) > 0);
                if (chests >= MaxChestsPerAccount)
                    return (ChestLimitReached, MaxChestsPerAccount);
            }
            return null;
        }

        private static byte[] BuildRefusal(ulong playerGuid, uint error, ulong detail = 0)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)2);
            writer.Write(playerGuid);
            writer.Write(false);
            writer.Write(error);
            writer.Write(detail);
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write("");
            for (int i = 0; i < 5; i++)
                writer.Write(0u);
            writer.Write(true);
            writer.Write(new Vector4(0f, 0f, 0f, 1f));
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);
            return output.ToArray();
        }

        /// <summary>
        /// The size of PropBase Save (writer FUN_142f08c10) after its header: u32, u8, u64 prop guid, u32, six
        /// vectors (+0x30 +0x40 +0x50 +0x70 +0x80 +0x90; seen: position, rotation quaternion, scale, then the
        /// position before the move), u8, u64, u8. Read from the end so the header length
        /// doesn't matter.
        /// </summary>
        private const int SaveBodyLength = 4 + 1 + 8 + 4 + 6 * 16 + 1 + 8 + 1;

        /// <summary>
        /// The owner confirmed where the prop goes: move it there, show it to everyone, and confirm with SaveReply
        /// (sub 4, reader FUN_142f0c0e0: header, u32, u64).
        /// </summary>
        public static void Save(WorldSession owner, byte[] body)
        {
            if (body.Length < SaveBodyLength)
            {
                log.Warn($"Short prop save from {owner.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }
            var reader = new GamePacketReader(body);
            reader.ReadBytes((uint)(body.Length - SaveBodyLength));
            uint u0 = reader.ReadUInt();
            byte u1 = reader.ReadByte();
            ulong guid = reader.ReadULong();
            uint u2 = reader.ReadUInt();
            var vectors = new Vector4[6];
            for (int i = 0; i < vectors.Length; i++)
                vectors[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            log.Debug($"Prop save from {owner.CharacterName}: guid 0x{guid:X} u32 {u0} u8 {u1} u32 {u2} vectors "
                + string.Join(" ", vectors.Select(v => v.ToString())));

            PlacedProp prop;
            lock (sync)
                prop = props.FirstOrDefault(p => p.Spawn.Guid == guid);
            // The owner, or (for a placed prop) a visitor with "Let's Decorate" on its site, who may only move it
            // within decorate access.
            bool isOwner = prop != null && prop.OwnerGuid == owner.PlayerGuid;
            if (prop == null || (!isOwner && !(prop.Placed && MayRearrange(owner, prop))))
                return;

            var position = new Vector4(vectors[0].X, vectors[0].Y, vectors[0].Z, 1f);
            var target = new Vector3(position.X, position.Y, position.Z);
            if (isOwner ? !MayPlaceAt(owner, target, prop.Zone)
                : Claims.ClaimAt(prop.Zone, target) == null || !MayDecorateAt(owner, target, prop.Zone))
            {
                // SaveReply failure (FUN_142f1c7b0): "That object can not be placed there."; the gizmo stays open so
                // the player can move it back onto their site or cancel.
                var refusal = new List<byte>();
                var w = new GamePacketWriter(refusal);
                w.Write((byte)4);
                w.Write(owner.PlayerGuid);
                w.Write(false);
                w.Write(LocationInvalid);
                w.Write(0ul);
                w.Write(0u);
                w.Write(guid);
                owner.EnqueueMessage(ZoneMessageOpcode.PropBase, refusal.ToArray());
                return;
            }
            bool wasPlaced = prop.Placed;
            // The client repeats Save while the placement gizmo is open; an unchanged one only needs its reply.
            bool changed = !wasPlaced || prop.Spawn.Position != position || prop.Spawn.Rotation != vectors[1]
                || prop.Spawn.Scale != vectors[2];
            if (wasPlaced && changed)
            {
                EditSession editing;
                lock (sync)
                    editSessions.TryGetValue(owner.PlayerGuid, out editing);
                if (editing != null && editing.Guid == guid)
                    Building.RememberPropMove(owner, editing, guid, editing.Start, new Transform(position, vectors[1], vectors[2]));
            }
            prop.Spawn.Position = position;
            prop.Spawn.Rotation = vectors[1];
            prop.Spawn.Scale = vectors[2];
            // First save of a palette placement uses up the item (unless the server allows free building).
            // A prop placed from an inventory item always uses that item up.
            if (!wasPlaced && Chests.Slots(prop.PropId) > 0 && Claims.ClaimAt(owner.ZoneId, new Vector3(position.X, position.Y, position.Z)) is ulong site
                && CountChestsOn(owner.ZoneId, site) >= Chests.MaxPerSite)
            {
                // "You can place up to 50 chests on a claim" (update notes, 21 March 2016).
                Chat.System(owner, $"A build site can hold at most {Chests.MaxPerSite} chests.");
                return;
            }
            // A chest that still holds items is taken as that very item, and its contents move into the new chest.
            bool carried = !wasPlaced && prop.PaidByItemGuid != 0 && Chests.HasItems(prop.PaidByItemGuid);
            if (carried)
            {
                var removed = owner.Inventory.RemoveFrom(prop.PaidByItemGuid, 1);
                if (removed == null)
                    return;
                owner.EnqueueMessage(new ClientUpdateItemDelete { Guid = prop.PaidByItemGuid });
                Chests.Move(prop.PaidByItemGuid, prop.Spawn.Guid);
            }
            else if (!wasPlaced && (prop.PaidByItem != 0 ? !owner.TakeItem(prop.PaidByItem, 1)
                    : !prop.Free && !FreeBuild && !Pay(owner, prop.PropId)))
            {
                log.Info($"{owner.CharacterName} no longer has the materials or item for prop {prop.PropId}.");
                return;
            }
            prop.Placed = true;
            if (!wasPlaced)
            {
                Achievements.PropPlaced(owner, prop.PropId);
                if (Chests.Slots(prop.PropId) > 0)
                    PropLimits.Changed(prop.Zone, new Vector3(position.X, position.Y, position.Z));
                log.Info($"{owner.CharacterName} placed prop {prop.PropId} (paid with {(carried ? "a carried chest" : prop.PaidByItem != 0 ? $"item {prop.PaidByItem}" : FreeBuild ? "nothing (free build)" : "its recipe materials")}).");
            }
            if (changed)
            {
                Persist(prop);
                if (wasPlaced)
                    prop.MovedAt = DateTime.UtcNow;
            }
            foreach (WorldSession session in PlayerManager.InZone(prop.Zone))
                if (session != owner && !wasPlaced)
                    SendSpawns(session, new[] { prop });

            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)4);
            writer.Write(owner.PlayerGuid);
            writer.Write(true);
            // Handler FUN_142f1c7b0: 0 = done (a non-zero value starts a follow-up step, FUN_142f1aa00).
            writer.Write(0u);
            writer.Write(guid);
            owner.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
        }

        /// <summary>
        /// The prop tint palette, PropBase 0x17 AvailableTintsReply (reader FUN_143481840) as a live server sent it:
        /// u8 sub, u64 player, bool success (false adds u32 error, u64), then tint groups {u32 count; each u32 key, u32
        /// id, u32 name id, u32, u32, set of tint ids (u32 count, u32 each)} and tint values {u32 count; each u32 key
        /// (art tint id), u32 art tint id, u32 name id, u32 sort order, u32 image set, string alias
        /// ("GlobalTint_OrchidBlue"), u32 alias hash}. One group, 59 tints (PropTintGroupMappings.txt).
        /// </summary>
        private static readonly Lazy<byte[]> tintList = new(() => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "PropTints.bin")));

        /// <summary>
        /// Sends the tint palette (at login, and whenever the build tray's tint tab asks: AvailableTintsRequest 0x18).
        /// </summary>
        public static void SendTintList(WorldSession session)
        {
            byte[] body = (byte[])tintList.Value.Clone();
            BitConverter.GetBytes(session.PlayerGuid).CopyTo(body, 1);
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, body);
        }

        /// <summary>The art tint id of a palette tint alias (PropData +0x80, looked up in the client's tint manager).</summary>
        private static readonly Lazy<Dictionary<string, uint>> tintIdByAlias = new(() =>
        {
            var r = new GamePacketReader(tintList.Value);
            r.ReadByte(); r.ReadULong();
            if (!r.ReadBool()) { r.ReadUInt(); r.ReadULong(); }
            uint groups = r.ReadUInt();
            for (uint g = 0; g < groups; g++)
            {
                for (int i = 0; i < 5; i++) r.ReadUInt();
                uint n = r.ReadUInt();
                for (uint i = 0; i < n; i++) r.ReadUInt();
            }
            var map = new Dictionary<string, uint>();
            uint values = r.ReadUInt();
            for (uint v = 0; v < values; v++)
            {
                r.ReadUInt();
                uint id = r.ReadUInt();
                for (int i = 0; i < 3; i++) r.ReadUInt();
                map[r.ReadString()] = id;
                r.ReadUInt();
            }
            return map;
        });

        public static uint TintIdOf(string alias) => alias != null && tintIdByAlias.Value.TryGetValue(alias, out uint id) ? id : 0;

        /// <summary>Tint aliases of the palette by their hash (the client's SetTintRequest sends only the hash).</summary>
        private static readonly Lazy<Dictionary<uint, string>> tintByHash = new(() =>
        {
            // Skip the reply header and the tint groups to reach the values (layout above).
            var r = new GamePacketReader(tintList.Value);
            r.ReadByte(); r.ReadULong();
            if (!r.ReadBool()) { r.ReadUInt(); r.ReadULong(); }
            uint groups = r.ReadUInt();
            for (uint g = 0; g < groups; g++)
            {
                for (int i = 0; i < 5; i++) r.ReadUInt();
                uint n = r.ReadUInt();
                for (uint i = 0; i < n; i++) r.ReadUInt();
            }
            var map = new Dictionary<uint, string>();
            uint values = r.ReadUInt();
            for (uint v = 0; v < values; v++)
            {
                for (int i = 0; i < 5; i++) r.ReadUInt();
                string alias = r.ReadString();
                map[r.ReadUInt()] = alias;
            }
            return map;
        });

        /// <summary>
        /// The build tray's tint tab: PropBase 0x16 SetTintRequest {u32 count, count × u64 prop guid, u64 player, u32
        /// tint alias hash (0 or unknown = clear)}. Tints the player's own props, saves them and shows everyone on the
        /// landscape (PlayerUpdate 0x7A SetPropTint {u64 guid, string alias}, handler → FUN_142e26580, the same call
        /// the client makes for its own preview).
        /// </summary>
        public static void SetTint(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            uint count = r.ReadUInt();
            if (count > 256 || body.Length < 4 + count * 8 + 12)
                return;
            var guids = new List<ulong>();
            for (uint i = 0; i < count; i++)
                guids.Add(r.ReadULong());
            r.ReadULong();
            uint hash = r.ReadUInt();
            string alias = tintByHash.Value.TryGetValue(hash, out string a) ? a : "";
            foreach (ulong guid in guids)
            {
                PlacedProp prop = Find(guid);
                if (prop == null || prop.OwnerGuid != session.PlayerGuid || prop.Spawn.TintAlias == alias)
                    continue;
                prop.Spawn.TintAlias = alias;
                if (prop.Placed)
                    Persist(prop);
                if (alias.Length > 0)
                    Achievements.PropTinted(session, prop.PropId);
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write(guid);
                writer.Write(alias);
                byte[] update = output.ToArray();
                foreach (WorldSession other in PlayerManager.InZone(prop.Zone))
                    other.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetPropTint, update);
            }
        }

        /// <summary>A prop its owner (or a builder on its site) may change.</summary>
        /// <summary>May this player adjust, move or remove a placed prop (theirs, or on a site they may build on)?</summary>
        public static bool CanEdit(WorldSession session, ulong guid) => Editable(session, guid) != null;

        private static PlacedProp Editable(WorldSession session, ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop == null || !prop.Placed)
                return null;
            if (prop.OwnerGuid == session.PlayerGuid)
                return prop;
            // Other players' props: whoever has "Let's Decorate" (0x08) on the site — "Visitors can move props around"
            // (wiki Build Site Management); full access includes it.
            Vector4 at = prop.Spawn.Position;
            return MayDecorateAt(session, new Vector3(at.X, at.Y, at.Z), prop.Zone) ? prop : null;
        }

        /// <summary>
        /// PropBase 0x1A SetPropEnable {u64 guid, bool}: switches a light or effect prop on or off for everyone in the
        /// landscape (UpdatePropInteractionState, a few dozen bytes per viewer) and remembers it.
        /// </summary>
        public static void SetEnabled(WorldSession session, ulong guid, bool enabled)
        {
            PlacedProp prop = Editable(session, guid);
            if (prop == null || prop.Disabled == !enabled)
                return;
            prop.Disabled = !enabled;
            Persist(prop);
            if (PropEffects.HasEffect(prop.PropId))
                foreach (WorldSession other in PlayerManager.InZone(prop.Zone))
                    PropEffects.Send(other, prop.PropId, guid, enabled);
            log.Info($"{session.CharacterName} switched prop 0x{guid:X} {(enabled ? "on" : "off")}.");
        }

        /// <summary>
        /// PropBase 0x19 SetPropName {u64 guid, string (up to 128)}: the owner names a prop whose PropDefs row allows it.
        /// The name travels in the prop's PropData; no label is drawn over the prop.
        /// </summary>
        public static void SetName(WorldSession session, ulong guid, string name)
        {
            PlacedProp prop = Editable(session, guid);
            if (prop == null || prop.OwnerGuid != session.PlayerGuid)
                return;
            if (GameTableManager.Instance.PropDefs.GetEntry(prop.PropId) is { AllowUserRename: false })
                return;
            name = (name ?? "").Trim();
            if (name.Length > 128)
                name = name[..128];
            prop.CustomName = name;
            Persist(prop);
            byte[] update = PropNameUpdate(prop);
            foreach (WorldSession viewer in PlayerManager.InZone(prop.Zone))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, update);
            log.Info($"{session.CharacterName} named prop 0x{guid:X} \"{name}\".");
        }

        /// <summary>PropBase 0x13 UpdatePropText {u64 guid, string}: new sign text (the client filters profanity).</summary>
        public static void SetText(WorldSession session, ulong guid, string text)
        {
            PlacedProp prop = Editable(session, guid);
            if (prop == null)
                return;
            text ??= "";
            if (text.Length > 1024)
                text = text[..1024];
            prop.Text = text;
            Persist(prop);
            SendText(session, guid);
        }

        /// <summary>
        /// PropBase 0x14 EditPropTextRequest {u64 guid} → 0x15 EditPropTextReply (reader FUN_142f097d0): u8 sub,
        /// u64 player, u64 prop guid, string text, u32, bool.
        /// </summary>
        public static void SendText(WorldSession session, ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop == null)
                return;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)0x15);
            writer.Write(session.PlayerGuid);
            writer.Write(guid);
            writer.Write(prop.Text);
            writer.Write(0u);
            writer.Write(true);
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
        }

        /// <summary>
        /// PropBase 0x0F Duplicate {u64 prop guid, u64 player, u8, u8}: places another of the same prop (paid like a
        /// palette placement) with the original's tint.
        /// </summary>
        public static void Duplicate(WorldSession session, ulong guid)
        {
            PlacedProp original = Editable(session, guid);
            if (original == null)
                return;
            var owned = Inventory.ItemDefinitionManager.ItemsForProp(original.PropId)
                .SelectMany(d => session.Inventory.Items.Where(i => i.ItemDefinitionId == d.Id && i.Count > 0))
                .FirstOrDefault();
            PlacedProp copy = owned != null
                ? BeginPlacement(session, original.PropId, owned.ItemDefinitionId, owned.Id)
                : BeginPlacement(session, original.PropId);
            if (copy != null)
                copy.Spawn.TintAlias = original.Spawn.TintAlias;
        }

        /// <summary>Dev "propremove &lt;guid&gt;": deletes a placed prop without its owner online (no refund).</summary>
        public static void DevRemove(ulong guid) => Despawn(guid);

        /// <summary>Removes a placed prop for everyone, without refund (temporary props: a summoned repair bot, a campfire).</summary>
        public static void Despawn(ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop != null)
                Remove(prop, null);
        }

        /// <summary>Dev "proptint &lt;guid&gt; [alias]": sets (or clears) a placed prop's tint for everyone.</summary>
        public static void DevSetTint(ulong guid, string alias)
        {
            PlacedProp prop = Find(guid);
            if (prop == null)
                return;
            prop.Spawn.TintAlias = alias ?? "";
            if (prop.Placed)
                Persist(prop);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(guid);
            writer.Write(prop.Spawn.TintAlias);
            byte[] update = output.ToArray();
            foreach (WorldSession other in PlayerManager.InZone(prop.Zone))
                other.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetPropTint, update);
        }

        /// <summary>
        /// PickUp (PropBase sub 5: u64 guid, u8) or Delete (sub 7: u64 guid) of a placed prop. Only its owner may;
        /// picking up returns the item it was placed from, deleting doesn't. Answered with PickUpReply (sub 6) or
        /// DeleteReply (sub 8): u64 player, bool success, u32 error (10 = no claim permission), u64 prop guid.
        /// </summary>
        /// <summary>
        /// PropAccess (sub 1 {u64 prop, u64, u32}; FUN_142f15300 / FUN_142f15550): the player picked a placed prop with the
        /// prop tool to move it. The client marks itself waiting (+0x159) and opens the gizmo only when sub 2 answers
        /// (FUN_142f1b980 case 2 → FUN_142f1ab30, which clears the wait): the prop's data when allowed, else a refusal
        /// with the client's code (10 no permission, 13 the site is in Play Mode). Who may: as for picking up, the
        /// prop's owner or anyone with "Let's Decorate" on its site. Without an answer the tool stayed stuck waiting
        /// (seen in play: three PropAccess requests for one prop, no gizmo).
        /// </summary>
        public static void Access(WorldSession player, ulong guid)
        {
            PlacedProp prop = Find(guid);
            uint error = 0;
            if (prop == null || !prop.Placed)
                error = NoClaimPermission;
            else
            {
                Vector3 at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
                ulong? site = Claims.ClaimAt(prop.Zone, at);
                if (site is ulong siteId && Claims.IsPlaying(siteId))
                    error = SiteInPlayMode;
                else if (!MayRearrange(player, prop))
                    error = NoClaimPermission;
            }
            if (error == 0)
                lock (sync)
                    editSessions[player.PlayerGuid] = new EditSession
                    {
                        Guid = guid,
                        Start = new Transform(prop.Spawn.Position, prop.Spawn.Rotation, prop.Spawn.Scale)
                    };
            player.EnqueueMessage(ZoneMessageOpcode.PropBase,
                error == 0 ? BuildPropData(player.PlayerGuid, prop, newPlacement: false) : BuildRefusal(player.PlayerGuid, error));
            log.Info($"{player.CharacterName} asked to edit prop 0x{guid:X}: {(error == 0 ? "ok" : $"refused ({error})")}.");
        }

        /// <summary>May the player move this placed prop: its owner, or "Let's Decorate" on the site it stands on?</summary>
        private static bool MayRearrange(WorldSession player, PlacedProp prop)
        {
            if (prop.OwnerGuid == player.PlayerGuid)
                return true;
            Vector3 at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            return Claims.ClaimAt(prop.Zone, at) != null && MayDecorateAt(player, at, prop.Zone);
        }

        public static void PickUp(WorldSession owner, ulong guid, bool delete)
        {
            PlacedProp prop;
            lock (sync)
                prop = props.FirstOrDefault(p => p.Spawn.Guid == guid);
            // Client pick-up errors (FUN_142f1b980): 9 vendor still has stock ("Prop.PickUpErrorVendorInventoryNotEmpty"),
            // 10 no permission, 13 the site is in Play Mode. Who may: the prop's owner, or anyone with "Let's Decorate"
            // on the site it stands on (the site owner always) — the access level that lets visitors move props.
            bool stocked = prop != null && prop.PropId == Vendors.VendorProp && Vendors.HasStock(guid);
            Vector3 at = prop == null ? default : new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            ulong? site = prop == null ? null : Claims.ClaimAt(prop.Zone, at);
            bool playing = site is ulong siteId && Claims.IsPlaying(siteId);
            bool allowed = prop != null && (prop.OwnerGuid == owner.PlayerGuid
                || (site != null && MayDecorateAt(owner, at, prop.Zone)));
            bool ok = allowed && !playing && !(delete && Chests.HasItems(guid)) && !stocked;
            if (ok)
                Remove(prop, delete ? null : owner);
            else if (allowed && !playing && delete && Chests.HasItems(guid))
                Chat.System(owner, "Empty the chest or pick it up before deleting it.");
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)(delete ? 8 : 6));
            writer.Write(owner.PlayerGuid);
            writer.Write(ok);
            writer.Write(ok ? 0u : stocked ? 9u : playing ? SiteInPlayMode : allowed ? 0u : NoClaimPermission);
            writer.Write(guid);
            owner.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
            log.Info($"{owner.CharacterName} {(delete ? "deleted" : "picked up")} prop 0x{guid:X}: {(ok ? "ok" : "refused")}.");
        }

        /// <summary>Picks up every prop an owner placed inside a local-space box (their build site going away).</summary>
        /// <summary>Zone of props packed away from a lapsed build site (no landscape: nobody sees them).</summary>
        public const uint StowedZone = 0;

        /// <summary>
        /// A lapsed site's props (an offline owner's, inside the box): taken out of the world and kept, in the database
        /// too, on <see cref="StowedZone"/> until the owner opens their Build Site Contents (<see cref="Unstow"/>).
        /// Chests keep their contents. Returns how many.
        /// </summary>
        public static int StowInBox(ulong ownerGuid, Vector3 min, Vector3 max, uint zone)
        {
            List<PlacedProp> inside;
            lock (sync)
                inside = props.Where(p => p.OwnerGuid == ownerGuid && p.Placed && p.Zone == zone
                    && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                    && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                    && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z).ToList();
            // Free placements (the Starting Location, game markers) are not packed: they just go with the site.
            foreach (PlacedProp free in inside.Where(GivesNothingBack).ToList())
            {
                Remove(free, null);
                inside.Remove(free);
            }
            foreach (PlacedProp prop in inside)
            {
                foreach (WorldSession session in PlayerManager.InZone(prop.Zone))
                    SendRemove(session, prop);
                lock (sync)
                    prop.Zone = StowedZone;
            }
            var ids = inside.Select(p => p.Spawn.Guid).ToHashSet();
            if (ids.Count > 0)
                DatabaseManager.Instance.CharacterDatabase.Save(c =>
                {
                    foreach (WorldPropModel stored in c.WorldProp.Where(p => ids.Contains(p.Id)))
                        stored.ZoneId = StowedZone;
                }).ContinueWith(t => log.Error(t.Exception, "Failed to stow props."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            return inside.Count;
        }

        /// <summary>
        /// Gives a player back the items of all their stowed props (and removes the props). Returns how many. A stowed
        /// chest goes to the overflow tab with its contents (wiki Chests: on a lost site "the items from the chest are
        /// removed and placed in the overflow inventory, along with the chest").
        /// </summary>
        public static int Unstow(WorldSession owner)
        {
            List<PlacedProp> stowed;
            lock (sync)
                stowed = props.Where(p => p.OwnerGuid == owner.PlayerGuid && p.Zone == StowedZone).ToList();
            foreach (PlacedProp prop in stowed)
            {
                if (Chests.Slots(prop.PropId) == 0)
                {
                    Remove(prop, owner);
                    continue;
                }
                Chests.EmptyToOverflow(prop.Spawn.Guid, owner);
                uint item = prop.PaidByItem != 0 ? prop.PaidByItem
                    : Inventory.ItemDefinitionManager.ItemsForProp(prop.PropId).FirstOrDefault()?.Id ?? 0;
                Remove(prop, null);
                if (item != 0 && !GivesNothingBack(prop))
                    foreach ((var changed, bool isNew) in owner.Inventory.AddToOverflow(item, 1))
                        owner.SendItem(changed, replace: !isNew);
                log.Info($"{owner.CharacterName}: chest prop {prop.PropId} back as item {item} in overflow.");
            }
            return stowed.Count;
        }

        public static int ReturnInBox(WorldSession owner, Vector3 min, Vector3 max, uint zone)
        {
            List<PlacedProp> inside;
            lock (sync)
                inside = props.Where(p => p.OwnerGuid == owner.PlayerGuid && p.Placed && p.Zone == zone
                    && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                    && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                    && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z).ToList();
            foreach (PlacedProp prop in inside)
                Remove(prop, owner);
            return inside.Count;
        }

        /// <summary>Takes a prop out of the world (and the database); <paramref name="refundTo"/> gets its item back.</summary>
        /// <summary>Dev/testing: removes every prop a player owns (no refund). Returns how many.</summary>
        public static int DevClear(WorldSession owner)
        {
            List<PlacedProp> mine;
            lock (sync)
                mine = props.Where(p => p.OwnerGuid == owner.PlayerGuid).ToList();
            foreach (PlacedProp prop in mine)
                Remove(prop, null);
            return mine.Count;
        }

        /// <summary>
        /// A prop placed for free: site game markers added from the game editor, the site's Starting Location (wiki: "free
        /// to place and does not require resources ... a new one can always be placed for free"; every new site gets one,
        /// <see cref="Claims.EnsureStartingLocation"/>), and anything placed with <see cref="PlacedProp.Free"/> — unless it
        /// was placed from a bag item. Picking one up returns nothing, and a lapsed site's contents leave it out.
        /// </summary>
        private static bool GivesNothingBack(PlacedProp prop)
            => prop.PaidByItem == 0 && (prop.Free || InstancedGames.IsMarker(prop.PropId)
                || prop.PropId == StoryTools.StartingLocationProp);

        private static void Remove(PlacedProp prop, WorldSession refundTo)
        {
            lock (sync)
                props.Remove(prop);
            if (prop.Placed && Chests.Slots(prop.PropId) > 0)
                PropLimits.Changed(prop.Zone, new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z));
            StoryTools.PropRemoved(prop.Spawn.Guid);
            PropSettings.PropRemoved(prop.Spawn.Guid);
            foreach (WorldSession session in PlayerManager.InZone(prop.Zone))
                SendRemove(session, prop);
            ulong id = prop.Spawn.Guid;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                WorldPropModel stored = c.WorldProp.FirstOrDefault(p => p.Id == id);
                if (stored != null)
                    c.WorldProp.Remove(stored);
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to delete prop 0x{id:X}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            // Free placements give nothing back (no farming of free placements) unless placed from a bag item.
            if (refundTo == null || !prop.Placed || GivesNothingBack(prop))
                return;
            uint item = prop.PaidByItem != 0 ? prop.PaidByItem
                : Inventory.ItemDefinitionManager.ItemsForProp(prop.PropId).FirstOrDefault()?.Id ?? 0;
            if (item == 0)
                return;
            if (Chests.HasItems(prop.Spawn.Guid))
            {
                // Picking up a chest keeps its contents inside it: it comes back as its own (unstacked) item.
                var single = refundTo.Inventory.AddSingle(item);
                if (single != null)
                {
                    refundTo.SendItem(single, replace: false);
                    Chests.Move(prop.Spawn.Guid, single.Id);
                }
                return;
            }
            refundTo.GiveItem(item, 1);
        }

        /// <summary>
        /// Shows a placed prop to every player on its landscape.
        /// </summary>
        private static void Broadcast(PlacedProp prop)
        {
            foreach (WorldSession session in PlayerManager.InZone(prop.Zone))
                SendSpawns(session, new[] { prop });
        }

        /// <summary>
        /// Spawns props for one client, then flags the crafting stations among them as interactable in a single
        /// packet (the client then asks for their interaction info).
        /// </summary>
        private static void SendSpawns(WorldSession session, IEnumerable<PlacedProp> spawns)
        {
            var flagging = new DynamicInteractionFlagging();
            foreach (PlacedProp prop in spawns)
            {
                session.EnqueueMessage(prop.Spawn);
                if (!string.IsNullOrEmpty(prop.CustomName))
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, PropNameUpdate(prop));
                if (!prop.Disabled)
                    PropEffects.Send(session, prop.PropId, prop.Spawn.Guid);
                // Usable props, and the props this viewer may edit: the client only lets the cursor pick (right-click
                // menu, tint, edit tools) entities the server flagged and answered as interactable (editable ones
                // only while in build mode, see SendInteractionInfo).
                if (CraftingStations.StationForProp(prop.PropId) != 0 || Chests.Slots(prop.PropId) > 0 || StoryTools.IsClickable(prop.PropId)
                    || prop.PropId == 489 || Vaults.IsVaultProp(prop.PropId) || MayRearrange(session, prop))
                    flagging.Guids.Add(prop.Spawn.Guid);
            }
            if (flagging.Guids.Count > 0)
                session.EnqueueMessage(flagging);
        }

        /// <summary>
        /// PlayerUpdate 0x7C UpdatePropName {u64 prop, string name} (reader FUN_142bb4650, PlayerUpdate_Dispatch case
        /// 0x7b): the prop entity's shown name; an empty name goes back to the definition's (FUN_142cf1dc0).
        /// </summary>
        private static byte[] PropNameUpdate(PlacedProp prop)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(prop.Spawn.Guid);
            writer.Write(prop.CustomName ?? "");
            return output.ToArray();
        }

        /// <summary>Cursors.txt 10 cursor_interaction_generic.</summary>
        private const uint InteractionCursor = 10;

        /// <summary>
        /// Answers the client's InteractionInfoRequest: crafting stations, chests and the Spire's usable props are
        /// interactable, everything else not.
        /// </summary>
        public static void SendInteractionInfo(WorldSession session, IEnumerable<ulong> guids)
        {
            var reply = new InteractionInfoReply();
            foreach (ulong guid in guids)
            {
                bool usable = StationAt(guid) != 0 || Chests.IsChest(guid) || Vendors.IsVendor(guid) || Spire.Usable(guid)
                    || IsMailbox(guid) || Vaults.IsVault(guid)
                    || WarpNetwork.IsCrystal(guid) || Outposts.Usable(guid) || LootChests.IsChest(guid)
                    || (Find(guid) is PlacedProp switchProp && StoryTools.IsClickable(switchProp.PropId))
                    || Creatures.IsStory(guid);
                // A prop the player may edit is pickable too (right-click menu, tint, move), without the highlight,
                // but only in build mode: outside it the client would show the interact / tool cursor on fences and
                // decorations that do nothing (user request 2026-10-13). RefreshEditable updates them on Tab.
                bool editable = !usable && BuildModeHolster.InBuildMode(session)
                    && Find(guid) is PlacedProp own && own.Placed && MayRearrange(session, own);
                reply.Entries.Add(new InteractionInfoReply.Entry
                {
                    Guid = guid,
                    Type = usable || editable ? InteractionInfoReply.Interactable : InteractionInfoReply.None,
                    // Entity +0x4118; the client's default leaves a station out of reach.
                    Range = usable || editable ? StationRange : -1f,
                    // The gold / red highlight (InteractionProcessor highlighter FUN_143324bd0): the 0.589 client lights
                    // the nearest usable entity near the player (gold in reach, red out of reach); there is no
                    // mouse-only hover outline in this build. On for usable objects (user request 2026-10-13).
                    Outline = usable,
                    // Hovering a usable prop within reach shows the interaction cursor. Props that are only editable
                    // (fences, decorations) keep the normal cursor: they do nothing when used (user request 2026-10-13).
                    CursorInReach = usable ? InteractionCursor : 0
                });
            }
            if (reply.Entries.Count > 0)
                session.EnqueueMessage(reply);
        }

        /// <summary>
        /// Build mode toggled: re-sends the interaction info of the nearby props this player may edit, so they become
        /// pickable (build mode) or plain scenery (out of it) at once. The client applies a reply whether or not it
        /// asked (FUN_142ee6b90); type 0 clears the interactable state. About 48 bytes per prop.
        /// </summary>
        public static void RefreshEditable(WorldSession session)
        {
            var guids = PlacedNear(session, EditableRefreshRange).Where(p => p.Placed && MayRearrange(session, p))
                .Select(p => p.Spawn.Guid).ToList();
            if (guids.Count > 0)
                SendInteractionInfo(session, guids);
        }

        /// <summary>Metres: the client asks again about anything further away once it comes near.</summary>
        private const float EditableRefreshRange = 64f;

        private static bool FreeBuild => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config.FreeBuild;

        /// <summary>
        /// What placing a prop costs: the materials of its prop recipe (PropDefs PROP_RECIPE_ID →
        /// PropRecipeComponentMaps → RecipeComponents, as the palette shows), or else one item that places it.
        /// </summary>
        private static List<(uint Item, uint Count)> RecipeCost(uint propId)
        {
            uint recipe = GameTableManager.Instance.PropDefs.GetEntry(propId)?.PropRecipeId ?? 0;
            if (recipe == 0)
                return null;
            var cost = new List<(uint, uint)>();
            foreach (var map in GameTableManager.Instance.PropRecipeComponentMaps.Entries.Where(m => m.PropRecipeId == recipe))
            {
                var component = GameTableManager.Instance.RecipeComponents.GetEntry(map.RecipeComponentId);
                if (component != null && component.ComponentType == 1)
                    cost.Add((component.ComponentId, component.ComponentCount));
            }
            return cost;
        }

        /// <summary>
        /// Can the player pay for a prop: its recipe's components (PropDefs PROP_RECIPE_ID), or, for props without a recipe,
        /// a prop item of that kind in the bag?
        /// </summary>
        /// <summary>Dev: a prop's placement cost and whether the player can pay it.</summary>
        public static string DescribeCost(WorldSession owner, uint propId)
        {
            List<(uint Item, uint Count)> cost = RecipeCost(propId);
            string parts = cost == null ? "no recipe (needs a prop item)"
                : string.Join(", ", cost.Select(c => $"{c.Count} x item {c.Item} (have {owner.Inventory.CountOf(c.Item)})"));
            uint recipe = GameTableManager.Instance.PropDefs.GetEntry(propId)?.PropRecipeId ?? 0;
            var maps = GameTableManager.Instance.PropRecipeComponentMaps.Entries.Where(m => m.PropRecipeId == recipe).ToList();
            parts += $" [recipe {recipe}, maps {string.Join(",", maps.Select(m => m.RecipeComponentId))}, component types "
                + string.Join(",", maps.Select(m => GameTableManager.Instance.RecipeComponents.GetEntry(m.RecipeComponentId)?.ComponentType.ToString() ?? "missing")) + "]";
            return $"prop {propId}: {parts}; can afford {CanAfford(owner, propId)}, knows {RecipeBook.KnowsProp(owner, propId)}";
        }

        private static bool CanAfford(WorldSession owner, uint propId)
        {
            List<(uint Item, uint Count)> cost = RecipeCost(propId);
            return cost != null
                ? cost.All(c => owner.Inventory.CountOf(c.Item) >= c.Count)
                : PlacementItem(owner, propId) != 0;
        }

        /// <summary>
        /// Takes the prop's cost from the player (recipe components, or one prop item). False when they can't afford it.
        /// </summary>
        private static bool Pay(WorldSession owner, uint propId)
        {
            if (!CanAfford(owner, propId))
                return false;
            List<(uint Item, uint Count)> cost = RecipeCost(propId);
            if (cost == null)
                return owner.TakeItem(PlacementItem(owner, propId), 1);
            foreach ((uint item, uint count) in cost)
                owner.TakeItem(item, count);
            return true;
        }

        /// <summary>An item definition the player holds that places this prop (0 if none).</summary>
        private static uint PlacementItem(WorldSession owner, uint propId)
            => Inventory.ItemDefinitionManager.ItemsForProp(propId)
                .FirstOrDefault(d => owner.Inventory.CountOf(d.Id) > 0)?.Id ?? 0;

        /// <summary>How close a player must be to use a crafting station, in metres.</summary>
        private const float StationRange = 6f;

        /// <summary>
        /// Writes a placed prop's current state to the database (insert or update, off the game thread).
        /// </summary>
        private static void Persist(PlacedProp prop)
        {
            PlayerUpdateAddLightweightProp spawn = prop.Spawn;
            var model = new WorldPropModel
            {
                Id = spawn.Guid,
                PropId = prop.PropId,
                ModelId = spawn.ModelId,
                NameId = spawn.NameId,
                OwnerGuid = prop.OwnerGuid,
                PositionX = spawn.Position.X,
                PositionY = spawn.Position.Y,
                PositionZ = spawn.Position.Z,
                RotationX = spawn.Rotation.X,
                RotationY = spawn.Rotation.Y,
                RotationZ = spawn.Rotation.Z,
                RotationW = spawn.Rotation.W,
                Scale = spawn.Scale.X,
                ZoneId = prop.Zone,
                TintAlias = spawn.TintAlias,
                CustomName = prop.CustomName,
                Text = prop.Text,
                Disabled = prop.Disabled
            };
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                if (c.WorldProp.Any(p => p.Id == model.Id))
                    c.WorldProp.Update(model);
                else
                    c.WorldProp.Add(model);
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to save prop 0x{model.Id:X}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Loads the props placed in earlier runs (after the prop definitions).
        /// </summary>
        public static void LoadWorld()
        {
            List<WorldPropModel> stored = DatabaseManager.Instance.CharacterDatabase.GetWorldProps();
            lock (sync)
            {
                foreach (WorldPropModel model in stored)
                {
                    props.Add(new PlacedProp
                    {
                        PropId = model.PropId,
                        OwnerGuid = model.OwnerGuid,
                        Zone = model.ZoneId,
                        Placed = true,
                        CustomName = model.CustomName ?? "",
                        Text = model.Text ?? "",
                        Disabled = model.Disabled,
                        Spawn = new PlayerUpdateAddLightweightProp
                        {
                            Guid = model.Id,
                            TransientId = PlayerManager.AllocateTransientId(),
                            NameId = model.NameId,
                            // The prop's current model (models fixed for this client apply to props saved before).
                            ModelId = model.PropId != 0 && propModels.TryGetValue(model.PropId, out uint current) ? current : model.ModelId,
                            Position = new Vector4(model.PositionX, model.PositionY, model.PositionZ, 1f),
                            Rotation = new Vector4(model.RotationX, model.RotationY, model.RotationZ, model.RotationW),
                            Scale = new Vector4(model.Scale, model.Scale, model.Scale, 1f),
                            OwnerGuid = model.OwnerGuid,
                            TintAlias = model.TintAlias ?? ""
                        }
                    });
                    if ((long)model.Id > Interlocked.Read(ref nextGuid))
                        Interlocked.Exchange(ref nextGuid, (long)model.Id);
                }
            }
            log.Info($"Loaded {stored.Count} placed props.");
        }

        /// <summary>
        /// Where a placed prop stands, or null when there is no such prop.
        /// </summary>
        /// <summary>Placed props of some prop definitions on a landscape: (guid, prop id, position, custom name).</summary>
        public static List<(ulong Guid, uint PropId, Vector3 Position, string Name)> PlacedOf(uint zone, ISet<uint> propIds)
        {
            lock (sync)
                return props.Where(p => p.Placed && p.Zone == zone && propIds.Contains(p.PropId))
                    .Select(p => (p.Spawn.Guid, p.PropId, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z),
                        p.CustomName ?? ""))
                    .ToList();
        }

        /// <summary>
        /// Placed props of some prop definitions (null: all) on every landscape: (guid, prop id, zone, position, custom name).
        /// </summary>
        public static List<(ulong Guid, uint PropId, uint Zone, Vector3 Position, string Name)> PlacedAll(ISet<uint> propIds)
        {
            lock (sync)
                return props.Where(p => p.Placed && p.Zone != StowedZone && (propIds == null || propIds.Contains(p.PropId)))
                    .Select(p => (p.Spawn.Guid, p.PropId, p.Zone, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z),
                        p.CustomName ?? ""))
                    .ToList();
        }

        public static System.Numerics.Vector3? PositionOf(ulong guid)
        {
            lock (sync)
            {
                PlacedProp prop = props.FirstOrDefault(p => p.Spawn.Guid == guid);
                return prop == null ? null : new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            }
        }

        /// <summary>The nearest placed chest within 20 m of a player on their landscape (0 = none).</summary>
        public static ulong NearestChest(WorldSession session)
        {
            lock (sync)
                return props.Where(p => p.Zone == session.ZoneId && Chests.Slots(p.PropId) > 0)
                    .Select(p => (p, d: Vector3.Distance(new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z), session.Position)))
                    .Where(x => x.d < 20f).OrderBy(x => x.d).Select(x => x.p.Spawn.Guid).FirstOrDefault();
        }

        /// <summary>Placed chests standing inside a build site (its "Chests" count, <see cref="PropLimits"/>).</summary>
        public static int ChestsOn(uint zone, ulong site) => CountChestsOn(zone, site);

        /// <summary>Placed chests standing inside a build site.</summary>
        private static int CountChestsOn(uint zone, ulong site)
        {
            List<PlacedProp> chests;
            lock (sync)
                chests = props.Where(p => p.Placed && p.Zone == zone && Chests.Slots(p.PropId) > 0).ToList();
            return chests.Count(p => Claims.ClaimAt(zone, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z)) == site);
        }

        /// <summary>A placed prop by guid, or null.</summary>
        /// <summary>Every prop a player placed (copies of the list, any landscape).</summary>
        /// <summary>Placed props within <paramref name="range"/> metres of a player, nearest first.</summary>
        public static List<PlacedProp> PlacedNear(WorldSession session, float range)
        {
            var me = session.Position;
            lock (sync)
                return props.Where(p => p.Zone == session.ZoneId
                        && Vector3.Distance(new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z), me) <= range)
                    .OrderBy(p => Vector3.Distance(new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z), me)).ToList();
        }

        public static List<PlacedProp> OwnedBy(ulong ownerGuid)
        {
            lock (sync)
                return props.Where(p => p.OwnerGuid == ownerGuid).ToList();
        }

        /// <summary>
        /// A placed Mailbox (PropDefs 489): clicking it opens the player's own inbox, as the Spire's does (wiki
        /// "Mailbox": "you can access your mail anywhere … this mailbox makes it more fun to check when you click in it").
        /// </summary>
        public static bool IsMailbox(ulong guid) => Find(guid)?.PropId == 489;

        public static PlacedProp Find(ulong guid)
        {
            lock (sync)
                return props.FirstOrDefault(p => p.Spawn.Guid == guid);
        }

        /// <summary>The crafting station a placed prop provides (0 = none).</summary>
        public static uint StationAt(ulong guid)
        {
            if (Spire.StationAt(guid) is uint spire and not 0)
                return spire;
            lock (sync)
                return CraftingStations.StationForProp(props.FirstOrDefault(p => p.Spawn.Guid == guid)?.PropId ?? 0);
        }

        /// <summary>
        /// Answers the client's FullCharacterDataRequest for a prop, and starts its owner's placement session if it
        /// was waiting for that.
        /// </summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            PlacedProp prop;
            lock (sync)
                prop = props.FirstOrDefault(p => p.Spawn.Guid == guid);
            if (prop == null)
                return false;

            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.Spawn.TransientId });
            if (prop.AwaitingPlacement && session.PlayerGuid == prop.OwnerGuid)
            {
                prop.AwaitingPlacement = false;
                session.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildPropData(session.PlayerGuid, prop));
            }
            return true;
        }

        /// <summary>
        /// Removes a prop from one viewer. Its light/fire effect is switched off first: the composite effect outlives
        /// the entity (seen after cancelling a placement: the prop vanished, its glowing wisps stayed).
        /// </summary>
        private static void SendRemove(WorldSession session, PlacedProp prop)
        {
            PropEffects.Send(session, prop.PropId, prop.Spawn.Guid, on: false);
            session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = prop.Spawn.Guid });
        }

        /// <summary>
        /// Sends every placed prop on their landscape to a player entering it.
        /// </summary>
        public static void SendAll(WorldSession session)
        {
            List<PlacedProp> placed;
            uint zone = session.ZoneId;
            lock (sync)
                placed = props.Where(p => p.Placed && p.Zone == zone).ToList();
            SendSpawns(session, placed);
        }
    }
}
