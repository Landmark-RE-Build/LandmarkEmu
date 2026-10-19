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
    /// Props placed in the world (campfires, furniture, crafting stations). Kept in memory and streamed to the players
    /// near them by 64 m region, including players who arrive later (Props.Stream.cs, setting PropStreamRange).
    /// </summary>
    public static partial class Props
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
            /// <summary>
            /// Who made that move (its owner, or a visitor with "Let's Decorate"): their own client moved it already, so the
            /// settled move (<see cref="Update"/>) skips only them.
            /// </summary>
            public ulong MovedBy { get; set; }
            /// <summary>The owner's own name for it (PropBase 0x19 SetPropName; PropDefs ALLOW_USER_RENAME).</summary>
            public string CustomName { get; set; } = "";
            /// <summary>Sign text (PropBase 0x13 UpdatePropText).</summary>
            public string Text { get; set; } = "";
            /// <summary>Switched off with PropBase 0x1A SetPropEnable: its light or effect is dark.</summary>
            public bool Disabled { get; set; }
            /// <summary>
            /// A placed prop picked up onto a player's cursor again (that player's guid; 0 = none): a second Starting
            /// Location placed on a site moves the site's existing one instead (<see cref="BeginReplacement"/>, TASKS 6.37).
            /// Cleared by that player's first Save.
            /// </summary>
            public ulong ReplacingBy { get; set; }
            /// <summary>
            /// A copy made by Duplicate (PropBase 0x0F): the original's guid until the copy is first placed, when it takes
            /// the original's window settings (<see cref="PropSettings.CopySettings"/>, TASKS 6.30); 0 otherwise.
            /// </summary>
            public ulong DuplicateOf { get; set; }
            /// <summary>The placement pack's group id this prop is a part of (Props.Movers); 0 = not a part.</summary>
            public ulong PackGroup { get; set; }
            /// <summary>The spawner prop that placed its pack (refunded when a part is picked up); 0 = not a part.</summary>
            public uint PackSpawner { get; set; }
            /// <summary>
            /// The 64 m regions it is streamed with (Props.Stream.cs): its own, or for a pack part every part's; null while
            /// it isn't streamed (still on its owner's cursor, stowed, gone).
            /// </summary>
            public long[] StreamRegions { get; internal set; }
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

        /// <summary>
        /// The placed props in a box (local metres) that block creature paths (Game/Pathing site grids, OPEN-LEADS L133;
        /// props, large trees most of all, got a much smaller collision area so creatures path past them more easily, 7
        /// Jun 2016, P5781): those whose model has a Models.txt PATH_BLOCK_RADIUS
        /// (<see cref="ModelFootprints"/>), the radius × the prop's scale around its PATH_BLOCK_OFFSET (turned with the prop).
        /// Left out: props still being positioned, free game markers and Linking Areas (<see cref="PlacedProp.Free"/>),
        /// creature spawners (the creature stands on its spawner), moving platforms and their docks (creatures may stand on
        /// them) and pack parts. Which models block is the client's table [P]; that live's server used that column for its
        /// mesh is [I] (the column's name; P5781 names props as path blockers).
        /// </summary>
        public static List<Pathing.Obstacle> PathObstaclesIn(uint zone, Vector3 min, Vector3 max)
        {
            var list = new List<Pathing.Obstacle>();
            lock (sync)
                foreach (PlacedProp p in props)
                {
                    if (!p.Placed || p.Free || p.Zone != zone || p.PackGroup != 0 || p.AwaitingPlacement)
                        continue;
                    Vector4 at = p.Spawn.Position;
                    if (at.X < min.X || at.X > max.X || at.Y < min.Y || at.Y > max.Y || at.Z < min.Z || at.Z > max.Z)
                        continue;
                    if (ModelFootprints.Of(p.Spawn.ModelId) is not (float radius, Vector3 offset) || radius <= 0f
                        || platformParts.Contains(p.PropId) || platformDocks.Contains(p.PropId) || IsSpawner(p.PropId))
                        continue;
                    float scale = p.Spawn.Scale.X > 0f ? p.Spawn.Scale.X : 1f;
                    var turn = new Quaternion(p.Spawn.Rotation.X, p.Spawn.Rotation.Y, p.Spawn.Rotation.Z, p.Spawn.Rotation.W);
                    Vector3 centre = new Vector3(at.X, at.Y, at.Z) + Vector3.Transform(offset * scale, turn.LengthSquared() > 0.0001f ? Quaternion.Normalize(turn) : Quaternion.Identity);
                    list.Add(new Pathing.Obstacle(p.Spawn.Guid, centre, radius * scale, at.Y));
                }
            return list;
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
            var at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            DisplayTextBy(guid, prop.Zone, at, prop.Text, mode, seconds);
        }

        /// <summary>
        /// DisplayPropText with any speaker the client knows (a prop or a story creature: FUN_142f859b0 looks the second
        /// u64 up in the client's actor table) to the players near <paramref name="at"/>. A question (0.695, modes 4 / 5
        /// only: the bubble carries Yes / No) sends its <paramref name="questionId"/>, which comes back in
        /// PropPacketDialogResponse 0xED / 0x1D (trace/0695/plan-storytools-next.md section 0, item 4).
        /// </summary>
        /// <param name="creature">
        /// A story creature that should speak itself: players whose client has it as a full entity get the bubble over the
        /// creature (tested 2026-10-17, L96: a bubble spoken by a full kobold shows over its head); the others, whose
        /// client would show nothing for an entity it doesn't know as full, get it from <paramref name="speaker"/>.
        /// </param>
        public static void DisplayTextBy(ulong speaker, uint zone, Vector3 at, string text, uint mode, uint seconds, ulong questionId = 0,
            ulong creature = 0)
        {
            if (string.IsNullOrEmpty(text))
                return;
            byte[] body = WritePropText(speaker, text, mode, seconds, questionId);
            byte[] byCreature = creature == 0 ? null : WritePropText(creature, text, mode, seconds, questionId);
            // Not to players in the middle of a re-zone (anything sent with it can hang their client; docs/terrain.md).
            foreach (WorldSession session in PlayerManager.InZone(zone))
                if (session.RezoneTarget == null && Vector3.Distance(session.Position, at) <= TextDisplayRange)
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateDisplayPropText,
                        byCreature != null && Creatures.IsFullFor(session, creature) ? byCreature : body);
        }

        /// <summary>
        /// DisplayPropText to one player (world NPC dialogue). Same speaker / creature rules as
        /// <see cref="DisplayTextBy"/>; Yes / No only when <paramref name="questionId"/> ≠ 0 and mode is 4 or 5, and
        /// the packet's speaker entity is in that client's full actor table (L96 / L30).
        /// </summary>
        public static void DisplayTextTo(WorldSession session, ulong speaker, string text, uint mode, uint seconds,
            ulong questionId = 0, ulong creature = 0)
        {
            if (session == null || session.RezoneTarget != null || string.IsNullOrEmpty(text))
                return;
            byte[] body = WritePropText(speaker, text, mode, seconds, questionId);
            byte[] byCreature = creature == 0 ? null : WritePropText(creature, text, mode, seconds, questionId);
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateDisplayPropText,
                byCreature != null && Creatures.IsFullFor(session, creature) ? byCreature : body);
        }

        /// <summary>The props whose text can be shown (Links "display the text"): Rustic Sign, Fjorden Books, Gold Scroll.</summary>
        private static readonly HashSet<uint> textProps = new()
        {
            StoryTools.RusticSignProp, StoryTools.FjordenBookOpenProp, StoryTools.FjordenBookClosedProp, StoryTools.GoldScrollProp
        };

        /// <summary>A placed text prop that has text: using it opens the text for the reader.</summary>
        public static bool IsReadable(ulong guid) => Find(guid) is PlacedProp p && p.Placed && textProps.Contains(p.PropId) && !string.IsNullOrEmpty(p.Text);

        /// <summary>
        /// Using a sign / book / scroll with text: DisplayPropText mode 1, the client's text window
        /// (HudStateManager:OnOpenTextDisplay, TextDisplayWidget) for the reader only. The editor's "Window (Char 4000)"
        /// is the same window; mode 1 has no other source in the client.
        /// </summary>
        public static void Read(WorldSession reader, ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop == null || string.IsNullOrEmpty(prop.Text))
                return;
            reader.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateDisplayPropText, WritePropText(guid, prop.Text, 1, 0));
        }

        /// <summary>
        /// The DisplayPropText body. 0.695 (reader FUN_142c27f40, dispatcher case 0x78 → FUN_142f859b0) adds a u64 before
        /// the prop and a bool at the end: {u64 question id, u64 prop, string, u32 mode, u32 seconds, bool question}. The
        /// handler looks the prop up from the second u64 exactly as 0.589's FUN_142ee6ce0 does from its only one, reads the
        /// mode just after the string in both builds, and hands the first u64 and the bool to the speech bubble
        /// (FUN_142ebfcb0: a question shows Yes / No, answered by PropPacketDialogResponse). Plain text: 0 and false.
        /// </summary>
        private static byte[] WritePropText(ulong guid, string text, uint mode, uint seconds, ulong questionId = 0)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(questionId);
            writer.Write(guid);
            writer.Write(text);
            writer.Write(mode);
            writer.Write(seconds);
            writer.Write(questionId != 0);
            return output.ToArray();
        }

        /// <summary>Most visible characters in a prop's text (0.695 LargeTextInput.as 27: maxChars 512).</summary>
        private const int MaxVisibleText = 512;

        /// <summary>Hard cap on the stored text with its tags, in characters (ours).</summary>
        private const int MaxStoredText = 4096;

        /// <summary>The formatting tags the 0.695 text editor produces (Flash htmlText after its tokenizeText).</summary>
        private static readonly HashSet<string> allowedTags = new(StringComparer.OrdinalIgnoreCase) { "P", "/P", "B", "/B", "I", "/I", "U", "/U", "/FONT" };

        private static readonly System.Text.RegularExpressions.Regex fontTag =
            new(@"^FONT COLOR=""#[0-9A-Fa-f]{6}""$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// A sign's / Story Node's text as the 0.695 editor sends it (LargeTextInput.as 106, 151, 239-259): HTML tags
        /// tokenised as <c>~[~TAG~]~</c>, and <c>~a~</c> / <c>~q~</c> standing for one character each. Kept as sent (the
        /// 0.695 bubble, chat and text window turn it back: SpeechBubbleWidget.as 90-95, ChatDocumentClass.as 331-336),
        /// except: tags other than P, B, I, U and FONT COLOR="#rrggbb" are dropped, and the text is cut at 512 visible
        /// characters (a tag cut off is dropped) and at <see cref="MaxStoredText"/> overall (TASKS 2.21).
        /// </summary>
        public static string StoryText(string text)
        {
            var output = new System.Text.StringBuilder();
            int visible = 0, i = 0;
            while (i < text.Length && output.Length < MaxStoredText)
            {
                if (text.AsSpan(i).StartsWith("~[~"))
                {
                    int end = text.IndexOf("~]~", i + 3, StringComparison.Ordinal);
                    if (end < 0)
                        break;                                  // an unfinished tag: drop the rest
                    string tag = text.Substring(i + 3, end - i - 3);
                    if (allowedTags.Contains(tag) || fontTag.IsMatch(tag))
                        output.Append(text, i, end + 3 - i);
                    i = end + 3;
                    continue;
                }
                if (visible >= MaxVisibleText)
                {
                    i++;
                    continue;                                   // keep reading: closing tags after the cut still count
                }
                if (text.AsSpan(i).StartsWith("~a~") || text.AsSpan(i).StartsWith("~q~"))
                {
                    output.Append(text, i, 3);
                    i += 3;
                }
                else
                    output.Append(text[i++]);
                visible++;
            }
            return output.ToString();
        }

        /// <summary>A prop definition's name text (for the skipped-props list).</summary>
        public static uint NameIdOf(uint propId) => GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;

        /// <summary>Admin monitor (/admin/monitor): placed props per landscape.</summary>
        public static Dictionary<uint, int> PlacedByZone()
        {
            lock (sync)
                return props.Where(p => p.Placed).GroupBy(p => p.Zone).ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>Why a prop of a pasted copy or design was not placed (<see cref="PlaceDesignProp"/>).</summary>
        public enum DesignPropShortfall
        {
            /// <summary>Placed, or left out for a reason the player isn't told about (no model, no build permission).</summary>
            None,
            /// <summary>
            /// Neither a bag item of it nor the materials for it: VoxelConstruction 9 ZoneMissingPropList, client text
            /// Construction.SkippedProps 562273 "The following props were skipped because you lack sufficient resources
            /// or they were not found in your inventory:".
            /// </summary>
            Missing,
            /// <summary>
            /// No bag item of it and the recipe isn't known: VoxelConstruction 8 ZoneUnknownPropList, client text
            /// Construction.MissingPropKnowledge 576184 "You seem to be missing the required knowledge of one or more of
            /// the props included in this action:".
            /// </summary>
            Unknown
        }

        /// <summary>
        /// Places one prop of a pasted copy or design (TASKS 6.40). Live's rule, from the developers: a prop that
        /// already exists in your inventory can be placed, but without the recipe Build Mode or a design can't create
        /// one (hotfix 1 Apr 2016, P4462 — so with the recipe a design does
        /// create it), and designs use the prop items in the inventory even when the character lacks the recipe (13 Apr
        /// 2016, P4569). So, in order:
        /// <list type="number">
        /// <item>a bag item of the prop is used up (no recipe needed);</item>
        /// <item>else, when the player knows the prop's recipe (<see cref="RecipeBook.KnowsProp"/>) and holds its
        /// materials (<see cref="RecipeCost"/>), the materials are used up — the prop is then a recipe placement like
        /// one from the palette (<see cref="PlacedProp.PaidByItem"/> 0: Recycle returns the materials);</item>
        /// <item>else it is skipped: <see cref="DesignPropShortfall.Unknown"/> when it has a recipe the player doesn't
        /// know, <see cref="DesignPropShortfall.Missing"/> otherwise (recipe known but materials short, or a prop
        /// without a recipe and none in the bag). The caller lists them in the client's two skipped-props messages.</item>
        /// </list>
        /// The server's free-build setting (testing) places it for nothing, as it does palette placements. Returns the
        /// new prop's guid, or 0 when it wasn't placed (also when it has no model or the spot is not theirs to build on:
        /// <see cref="DesignPropShortfall.None"/>).
        /// </summary>
        public static ulong PlaceDesignProp(WorldSession owner, DesignProp design, Vector3 position, Vector4 rotation, out DesignPropShortfall shortfall)
        {
            shortfall = DesignPropShortfall.None;
            if (!propModels.TryGetValue(design.PropId, out uint modelId) || !MayPlaceAt(owner, position, owner.ZoneId))
                return 0;
            var item = Inventory.ItemDefinitionManager.ItemsForProp(design.PropId)
                .SelectMany(d => owner.Inventory.Items.Where(i => i.ItemDefinitionId == d.Id && i.Count > 0))
                .FirstOrDefault();
            uint paidByItem = 0;
            if (item != null && owner.TakeItem(item.ItemDefinitionId, 1))
                paidByItem = item.ItemDefinitionId;
            else if (!FreeBuild)
            {
                List<(uint Item, uint Count)> cost = RecipeCost(design.PropId);
                if (cost == null)
                {
                    shortfall = DesignPropShortfall.Missing;      // only ever placed from a bag item
                    return 0;
                }
                if (!RecipeBook.KnowsProp(owner, design.PropId))
                {
                    shortfall = DesignPropShortfall.Unknown;
                    return 0;
                }
                if (!Pay(owner, design.PropId))
                {
                    shortfall = DesignPropShortfall.Missing;
                    return 0;
                }
            }
            PlacedProp prop = Create(owner, design.PropId, modelId, NameIdOf(design.PropId), position);
            prop.Spawn.Rotation = rotation;
            prop.Spawn.Scale = design.Scale;
            prop.Spawn.TintAlias = design.Tint ?? "";
            prop.CustomName = design.Name ?? "";
            prop.Text = design.Text ?? "";
            prop.PaidByItem = paidByItem;
            prop.Placed = true;
            Persist(prop);
            Broadcast(prop);
            Achievements.PropPlaced(owner, prop.PropId);
            if (Chests.Slots(prop.PropId) > 0 || IsSpawner(prop.PropId))
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
            prop.MovedBy = player.PlayerGuid;
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
        /// Sends settled prop moves to the other players streaming the prop (server tick), and runs the prop stream
        /// (Props.Stream.cs) and the moving platforms. A drag produces a Save every half second; relaying each would cost
        /// a despawn + spawn per step, so only the final position goes out.
        /// </summary>
        public static void Update()
        {
            UpdatePlatforms();
            StreamUpdate();
            UpdateMenuNames();
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
            PlatformsRespawned(settled);
            // Only to the players streaming it (or now holding its new region; dropped for those who no longer do), the
            // mover's client excepted: it moved the prop itself. That is whoever saved the move (MovedBy), not always the
            // owner: when a visitor with "Let's Decorate" moved it, the owner's client was skipped and kept the prop where it
            // stood, while the visitor's got a needless remove + spawn (bughunt-build B4). A pack part's move can change where its pack is streamed,
            // so its mates are brought in line too (no respawn: they didn't move).
            var mates = new List<PlacedProp>();
            List<WorldSession> online = PlayerManager.Snapshot();
            foreach (PlacedProp prop in settled)
            {
                Reconcile(prop, respawn: true, mover: online.FirstOrDefault(s => s.PlayerGuid == prop.MovedBy));
                if (prop.PackGroup != 0)
                    mates.AddRange(PackParts(prop.PackGroup).Where(m => m != prop && !settled.Contains(m) && !mates.Contains(m)));
            }
            foreach (PlacedProp mate in mates)
                Reconcile(mate, respawn: false);
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

        /// <summary>How far below the player a prop waiting for its placement session is spawned (BeginPlacement).</summary>
        private const float PlacementSpawnDepth = 20f;

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
            // One Starting Location per site (TASKS 6.37): placing a second Starting Location picks up the one already
            // on the site and puts it on the cursor to move (hotfix 26 Apr 2016, P4818).
            if (propId == StoryTools.StartingLocationProp && paidByItem == 0
                && Claims.ClaimAt(owner.ZoneId, owner.Position) is ulong startSite
                && StartingLocationOn(owner.ZoneId, startSite) is PlacedProp existingStart)
                return BeginReplacement(owner, existingStart);
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
            // Spawned out of the player's way: the client starts its placement session (FUN_142f1ab30 →
            // FUN_142f215a0) only after it has asked for and received the prop's full data, and until then the prop is
            // an ordinary solid entity — at the player's feet it pushed them up (2026-10-14). The session then moves it
            // with the cursor and Save sends the real transform, so where it first appears doesn't matter.
            PlacedProp prop = Create(owner, propId, modelId, definition?.NameStringId ?? 0, owner.Position - new Vector3(0f, PlacementSpawnDepth, 0f));
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

        /// <summary>A placed prop's rotation (its spawn's quaternion), or null when there is no such prop.</summary>
        public static Quaternion? RotationOf(ulong guid)
        {
            lock (sync)
                return props.FirstOrDefault(p => p.Placed && p.Spawn.Guid == guid) is PlacedProp p
                    ? new Quaternion(p.Spawn.Rotation.X, p.Spawn.Rotation.Y, p.Spawn.Rotation.Z, p.Spawn.Rotation.W) : null;
        }

        /// <summary>The newest placed Starting Location standing on a site (null: none).</summary>
        public static PlacedProp StartingLocationOn(uint zone, ulong site)
        {
            List<PlacedProp> starts;
            lock (sync)
                starts = props.Where(p => p.Placed && p.Zone == zone && p.PropId == StoryTools.StartingLocationProp).ToList();
            return starts.Where(p => Claims.ClaimAt(zone, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z)) == site)
                .OrderByDescending(p => p.Spawn.Guid).FirstOrDefault();
        }

        /// <summary>
        /// Puts a placed prop back on a player's cursor as if it were a new placement (TASKS 6.37, the site's Starting
        /// Location). Same refusals as picking it with the prop tool (<see cref="Access"/>: 13 the site is in Play Mode,
        /// 10 no "Let's Decorate" there). The prop keeps its guid, so its Links and story state stay. The player's client
        /// gets it removed and spawned again: the placement session (FUN_142f1ab30 / 0.695 FUN_142fb9ba0) only takes a
        /// prop it has asked the full data of (see <see cref="BeginPlacement"/>), and <see cref="SendFull"/> then opens
        /// the session with the new-placement flag and <see cref="PlacementFlagRestoreOnCancel"/>, so Escape puts it back
        /// where it stood (and the client's restoring Save tells the server) instead of picking it up. Its moves are the
        /// ordinary Saves of a placed prop; other players see it settle (<see cref="Update"/>). Nothing is paid.
        /// </summary>
        private static PlacedProp BeginReplacement(WorldSession player, PlacedProp prop)
        {
            Vector3 at = new(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            uint error = Claims.ClaimAt(prop.Zone, at) is ulong site && Claims.IsPlaying(site) ? SiteInPlayMode
                : !MayRearrange(player, prop) ? NoClaimPermission : 0u;
            if (error != 0)
            {
                player.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildRefusal(player.PlayerGuid, error));
                log.Info($"{player.CharacterName} may not move the Starting Location 0x{prop.Spawn.Guid:X}: error {error}.");
                return null;
            }
            prop.ReplacingBy = player.PlayerGuid;
            prop.AwaitingPlacement = true;
            Respawn(player, prop);
            log.Info($"{player.CharacterName} placed another Starting Location: the site's one (0x{prop.Spawn.Guid:X}) goes on the cursor.");
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
        /// <summary>
        /// Dev ("placeat x y z [yaw°]"): finishes the player's pending placement as the client's PropSave would, with the
        /// prop at that spot (local metres) — the client's own Save carries the cursor transform, which a harness without
        /// mouse input never moves (FinishPlaceObject then saves 0, 0, 0). Goes through <see cref="Save"/> unchanged.
        /// </summary>
        public static bool DevSavePending(WorldSession owner, Vector3 at, float yawDegrees = 0f)
        {
            PlacedProp pending;
            lock (sync)
                pending = props.LastOrDefault(p => p.OwnerGuid == owner.PlayerGuid && !p.Placed);
            if (pending == null)
                return false;
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
            var body = new List<byte>();
            var w = new GamePacketWriter(body);
            w.Write(0u);
            w.Write((byte)1);
            w.Write(pending.Spawn.Guid);
            w.Write(0u);
            Vector4 position = new(at, 1f), rotation = new(turn.X, turn.Y, turn.Z, turn.W), scale = new(1f, 1f, 1f, 1f);
            foreach (Vector4 v in new[] { position, rotation, scale, pending.Spawn.Position, pending.Spawn.Rotation, pending.Spawn.Scale })
            {
                w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(v.W);
            }
            w.Write((byte)0);
            w.Write(0ul);
            w.Write((byte)0);
            Save(owner, body.ToArray());
            return true;
        }

        /// <summary>Is the player in the middle of a placement (a prop of theirs not placed yet, or one put back on their cursor)?</summary>
        public static bool HasPendingPlacement(WorldSession session)
        {
            lock (sync)
                return props.Any(p => (p.OwnerGuid == session.PlayerGuid && !p.Placed) || p.ReplacingBy == session.PlayerGuid);
        }

        public static bool CancelPendingPlacement(WorldSession session)
        {
            List<PlacedProp> pending, replacing;
            lock (sync)
            {
                pending = props.Where(p => p.OwnerGuid == session.PlayerGuid && !p.Placed).ToList();
                replacing = props.Where(p => p.ReplacingBy == session.PlayerGuid).ToList();
            }
            if (pending.Count == 0 && replacing.Count == 0)
                return false;
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildRefusal(session.PlayerGuid, 0));
            foreach (PlacedProp prop in pending)
                Remove(prop, null);
            // A placed prop put back on the cursor (BeginReplacement) just stays where it stood.
            foreach (PlacedProp prop in replacing)
            {
                prop.ReplacingBy = 0;
                prop.AwaitingPlacement = false;
            }
            log.Info($"{session.CharacterName}: unfinished placement of {pending.Count + replacing.Count} prop(s) cancelled (re-zone).");
            return true;
        }

        /// <summary>
        /// The player left the world (PlayerManager.Leave): a prop still on their cursor goes (it was never paid for: the
        /// first Save pays), a placed prop they had put back on the cursor (<see cref="BeginReplacement"/>) stays where it
        /// stands, and their prop edit session is dropped. Before 2026-10-18 nothing did this on a disconnect: the unplaced
        /// prop stayed in memory for good (one per disconnect mid-placement), HasPendingPlacement stayed true for the
        /// character (BuildModeHolster then never closed a bag placement's build mode, the next re-zone waited a second
        /// for a cancel), and a Starting Location left with ReplacingBy / AwaitingPlacement set opened the placement
        /// session on the cursor again whenever that player's client next asked for its full data (SendFull)
        /// (bughunt-build B7). Nothing is sent to the player who left. A newer session of the same character (a login
        /// that kicked this one) keeps its own placement.
        /// </summary>
        public static void LeftWorld(WorldSession session)
        {
            if (PlayerManager.FindByGuid(session.PlayerGuid) is WorldSession newer && newer != session)
                return;
            List<PlacedProp> pending, replacing;
            lock (sync)
            {
                pending = props.Where(p => p.OwnerGuid == session.PlayerGuid && !p.Placed).ToList();
                replacing = props.Where(p => p.ReplacingBy == session.PlayerGuid).ToList();
                editSessions.Remove(session.PlayerGuid);
            }
            foreach (PlacedProp prop in pending)
                Remove(prop, null);
            foreach (PlacedProp prop in replacing)
            {
                prop.ReplacingBy = 0;
                prop.AwaitingPlacement = false;
            }
            if (pending.Count + replacing.Count > 0)
                log.Info($"{session.CharacterName} left with {pending.Count + replacing.Count} unfinished placement(s): dropped.");
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
            if (Chests.Slots(propId) > 0 || IsSpawner(propId))
                PropLimits.Changed(owner.ZoneId, owner.Position + offset);
            return prop.Spawn.Guid;
        }

        /// <summary>
        /// Linking Area hook (TASKS 2.33): the Linking Area prop (1590, the server-side setting
        /// <c>GameScripting.PropNpcDefinitionId</c>) that stands for a trigger volume made with the Area Tool. It is placed
        /// at the box centre with the box size as its scale (the model is a unit cube: the tool reads the box back as
        /// position ± scale / 2, 0.695 FUN_143739860) and carries the area guid in its spawn. Free: the tool has no cost
        /// path. Persisted and shown to everyone on the landscape. Returns null when the prop has no model.
        /// </summary>
        public static PlacedProp PlaceLinkingArea(WorldSession owner, Vector3 centre, Vector4 rotation, Vector3 size, ulong areaGuid)
        {
            uint propId = StoryTools.LinkingAreaProp;
            if (!propModels.TryGetValue(propId, out uint modelId))
                return null;
            var definition = GameTableManager.Instance.PropDefs.GetEntry(propId);
            PlacedProp prop = Create(owner, propId, modelId, definition?.NameStringId ?? 0, centre);
            prop.Free = true;
            prop.Placed = true;
            prop.Spawn.Rotation = rotation;
            prop.Spawn.Scale = new Vector4(size, 1f);
            prop.Spawn.AreaGuid = areaGuid;
            Persist(prop);
            Broadcast(prop);
            return prop;
        }

        /// <summary>
        /// Linking Area hook (TASKS 2.33): a Linking Area prop whose box was changed through its area (UpdateDefinition)
        /// rather than by moving the prop: everyone streaming it, its owner included, gets it again in its new place
        /// (and it is streamed by its new region). Rare (the old area window), so a despawn + spawn is fine.
        /// </summary>
        public static void RespawnLinkingArea(PlacedProp prop)
        {
            Persist(prop);
            Reconcile(prop, respawn: true);
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
        /// Admin /prop (AdminCommandPacketSpawnProp 0x1E / 0x4EA, <see cref="AdminCommands.SpawnProp"/>): a free prop
        /// (no cost, no refund) at the position, rotation and scale the client computed (the ground under the admin's
        /// look-at point, facing the camera yaw), owned by the admin, persisted and shown to everyone on the landscape.
        /// Returns the guid, or 0 when the prop id has no model.
        /// </summary>
        public static ulong PlaceAdmin(WorldSession owner, uint propId, Vector3 position, Vector4 rotation, Vector4 scale)
        {
            if (!propModels.TryGetValue(propId, out uint modelId))
                return 0;
            var definition = GameTableManager.Instance.PropDefs.GetEntry(propId);
            PlacedProp prop = Create(owner, propId, modelId, definition?.NameStringId ?? 0, position);
            prop.Free = true;
            prop.Placed = true;
            if (rotation != Vector4.Zero)
                prop.Spawn.Rotation = rotation;
            if (scale.X > 0f && scale.Y > 0f && scale.Z > 0f)
                prop.Spawn.Scale = scale;
            Persist(prop);
            Broadcast(prop);
            if (Chests.Slots(propId) > 0 || IsSpawner(propId))
                PropLimits.Changed(owner.ZoneId, position);
            return prop.Spawn.Guid;
        }

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
            {
                props.Add(prop);
                byGuid[prop.Spawn.Guid] = prop;
            }
            return prop;
        }

        /// <summary>
        /// Bits of the PropData u8 after the name id (0.695 reader FUN_142fa7b40 +0x88, copied by the session start
        /// FUN_142fb9ba0 to the prop tool's +0x178; 0.589 the same): only the placement cancel reads them (Lua
        /// Prop.CancelObjectPlacement → 0.695 FUN_142fb3b80, 0.589 FUN_1435488d0 → FUN_142f14cb0, identical tests), for a
        /// new placement (tool mode 5):
        /// <list type="bullet">
        /// <item>0x20 set: Escape restores the prop to where the session started (FUN_142ec4e60 on the start transform +
        /// FUN_142fbf3e0, which sends a Save of it) — used for a placed prop put back on the cursor
        /// (<see cref="BeginReplacement"/>);</item>
        /// <item>else 0x40 set: Escape sends PropPickUp with u8 1 (the radial menu's Recycle, FUN_142fb63e0);</item>
        /// <item>else Escape sends PropPickUp with u8 0 (Send to Inventory, FUN_142fb66a0).</item>
        /// </list>
        /// <b>Inferred</b> (no capture of a live server's PropData): live set 0x40 for placements paid with recipe
        /// materials, so a cancelled one gives the materials back, and neither for one placed from a bag item. The server
        /// answers both pick-ups by what was paid anyway (<see cref="PickUp"/>).
        /// </summary>
        private const byte PlacementFlagRestoreOnCancel = 0x20, PlacementFlagRecycleOnCancel = 0x40;

        /// <summary>
        /// PropBase sub 2 (reader FUN_142f08e10, placement session FUN_142f1ab30): header (u64, success), prop guid,
        /// u32 prop id, string, 5 u32, bool editing-existing, rotation quaternion, u32, u32 name id, u8 placement flags
        /// (<see cref="PlacementFlagRecycleOnCancel"/>), two lists, string, two u32.
        /// </summary>
        private static byte[] BuildPropData(ulong playerGuid, PlacedProp prop, bool newPlacement = true, byte flags = 0)
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
            writer.Write(flags);
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
        /// <summary>
        /// Storage chests one account may have placed across all its sites; 0 = no account cap (WorldServer.json
        /// <c>ChestAccountCap</c>, Docker CHEST_ACCOUNT_CAP). Live's rule is the developer notes' per-site cap (21 March
        /// 2016: at most 50 chests per site, <see cref="Chests.MaxPerSite"/>) [P]; the per-account cap is
        /// only the wiki's reading (Chests: 50 chests per account) [W], so it is off by
        /// default (trace/0695/audit-wiki-gaps.md #9). The client has an error for each (4 "Only [maxCount] chests can be
        /// placed", 12 the category limit; FUN_142f1ab30), so both existed at some point [P]; which one live enforced in
        /// 2016 is not proven.
        /// </summary>
        private static int MaxChestsPerAccount =>
            Math.Max(0, Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.ChestAccountCap ?? 0);
        /// <summary>
        /// Props one build site may hold: the 0.695 client constant Claims.PropLimit (StringHashToValue.txt) is still
        /// 20,000, although the 25 May 2016 notes (P4195-P4198) speak of 100,000; the client's own number is kept.
        /// </summary>
        private const int MaxPropsPerSite = 20000;

        /// <summary>Creature spawners one build site may hold (25 May 2016, P4195-P4198: at most 125 creature spawners
        /// per site).</summary>
        public const int MaxSpawnersPerSite = 125;

        /// <summary>PropCategory 98 "Creatures" name text: the spawner limit's category (<see cref="PropLimits"/>).</summary>
        public const ulong CreaturesCategoryName = 573378;

        /// <summary>Whether a prop is a creature spawner (StoryTools' spawner kind).</summary>
        public static bool IsSpawner(uint propId) => StoryTools.KindOf(propId)?.Name == "creature spawner";

        /// <summary>Placed creature spawners standing inside a build site.</summary>
        public static int SpawnersOn(uint zone, ulong site)
        {
            List<PlacedProp> spawners;
            lock (sync)
                spawners = props.Where(p => p.Placed && p.Zone == zone && IsSpawner(p.PropId)).ToList();
            return spawners.Count(p => Claims.ClaimAt(zone, new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z)) == site);
        }

        /// <summary>
        /// The live server's placement rules beyond build permission, checked where the player stands (where the prop
        /// starts): the site is in Play Mode; a game table where the site already has one; the site's prop, chest and
        /// creature-spawner limits; the account's chest limit when the server owner set one. Null when the prop may be placed.
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
                if (IsSpawner(propId) && onSite.Count(p => IsSpawner(p.PropId)) >= MaxSpawnersPerSite)
                    return (CategoryLimitReached, (CreaturesCategoryName << 32) | (uint)MaxSpawnersPerSite);
            }
            if (Chests.Slots(propId) > 0 && MaxChestsPerAccount is int accountCap && accountCap > 0)
            {
                var characters = DatabaseManager.Instance.CharacterDatabase.GetCharacters(owner.Account.Id)
                    .GetAwaiter().GetResult().Select(c => c.Id).ToHashSet();
                int chests;
                lock (sync)
                    chests = props.Count(p => p.Placed && characters.Contains(p.OwnerGuid) && Chests.Slots(p.PropId) > 0);
                if (chests >= accountCap)
                    return (ChestLimitReached, (ulong)accountCap);
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
            if (prop.ReplacingBy == owner.PlayerGuid)
                prop.ReplacingBy = 0;   // the re-placement session is under way (its first Save), as a new one is placed

            var position = new Vector4(vectors[0].X, vectors[0].Y, vectors[0].Z, 1f);
            var target = new Vector3(position.X, position.Y, position.Z);
            if ((isOwner ? !MayPlaceAt(owner, target, prop.Zone)
                : Claims.ClaimAt(prop.Zone, target) == null || !MayDecorateAt(owner, target, prop.Zone))
                || !PackAllows(prop, target))
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
                // At most 50 chests per site (update notes, 21 March 2016).
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
            else if (!wasPlaced && (prop.PaidByItem != 0 ? !TakePlacementItem(owner, prop)
                    : !prop.Free && !FreeBuild && !Pay(owner, prop.PropId)))
            {
                log.Info($"{owner.CharacterName} no longer has the materials or item for prop {prop.PropId}.");
                return;
            }
            prop.Placed = true;
            // A bag placement that opened build mode is over: build mode closes again (BuildModeHolster).
            if (!wasPlaced)
                BuildModeHolster.PlacementEnded(guid);
            // Props To You "Place the Starting Location prop" (1313): every site gets one by itself (EnsureStartingLocation),
            // so moving it is the placement a player makes (the 7 Jun 2016 "Props to You" fix; audit-equipment-goals F4).
            if (wasPlaced && changed && prop.PropId == StoryTools.StartingLocationProp)
                Achievements.PropPlaced(owner, prop.PropId);
            if (!wasPlaced)
            {
                Achievements.PropPlaced(owner, prop.PropId);
                FirstTimeEvents.PropPlaced(owner, prop.PropId);   // story-tool help pages (moment inferred, Spec 2)
                // A duplicate takes its original's settings now that it exists (TASKS 6.30); the original may be gone.
                if (prop.DuplicateOf != 0)
                {
                    int copied = PropSettings.CopySettings(prop.DuplicateOf, prop.Spawn.Guid, prop.PropId);
                    if (copied > 0)
                        log.Info($"{owner.CharacterName}: duplicate 0x{prop.Spawn.Guid:X} took {copied} setting(s) of 0x{prop.DuplicateOf:X}.");
                    prop.DuplicateOf = 0;
                }
                if (Chests.Slots(prop.PropId) > 0 || IsSpawner(prop.PropId))
                    PropLimits.Changed(prop.Zone, new Vector3(position.X, position.Y, position.Z));
                log.Info($"{owner.CharacterName} placed prop {prop.PropId} (paid with {(carried ? "a carried chest" : prop.PaidByItem != 0 ? $"item {prop.PaidByItem}" : FreeBuild ? "nothing (free build)" : "its recipe materials")}).");
                // A mover's spawner (Translocation Pad, Flinger, Magical Platform …) becomes its pack's parts (TASKS E.1).
                if (PackOf(prop.PropId) != 0 && ExpandPack(owner, prop).Count > 0)
                {
                    var done = new List<byte>();
                    var doneWriter = new GamePacketWriter(done);
                    doneWriter.Write((byte)4);
                    doneWriter.Write(owner.PlayerGuid);
                    doneWriter.Write(true);
                    doneWriter.Write(0u);
                    doneWriter.Write(guid);
                    owner.EnqueueMessage(ZoneMessageOpcode.PropBase, done.ToArray());
                    return;
                }
            }
            if (changed)
            {
                Persist(prop);
                if (wasPlaced)
                {
                    prop.MovedAt = DateTime.UtcNow;
                    prop.MovedBy = owner.PlayerGuid;
                }
                // Linking Area hook: the Area Tool edits a box by moving / scaling its prop and saving it (0.695
                // FUN_143739440), so the area's shape follows the prop (TASKS 2.33).
                if (prop.PropId == StoryTools.LinkingAreaProp)
                    AreaDefinitions.PropSaved(prop);
            }
            // A new placement: streamed to the players holding its region (the owner has it already, from BeginPlacement).
            if (!wasPlaced)
            {
                lock (sync)
                    IndexLocked(prop);
                MarkStreamed(owner, prop);
                Show(prop, except: owner);
            }

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
                // Creatures placed on build sites can't be tinted any more (1 Apr 2016, P4354).
                if (IsSpawner(prop.PropId))
                    continue;
                prop.Spawn.TintAlias = alias;
                if (prop.Placed)
                    Persist(prop);
                if (alias.Length > 0)
                    Achievements.PropTinted(session, prop.PropId, alias);
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write(guid);
                writer.Write(alias);
                byte[] update = output.ToArray();
                foreach (WorldSession other in ViewersOf(prop))
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
            // Other players' props: whoever has "Let's Decorate" (0x08) on the site — visitors may move props
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
                foreach (WorldSession other in ViewersOf(prop))
                    PropEffects.Send(other, prop.PropId, guid, enabled);
            // The client's own copy of the flag too (PlayerUpdate 0x26/0x73 {u64 guid, bool}, FUN_142f85dc0 →
            // FUN_1430abea0) [P], so a pad / flinger switched off stops firing at once [I: live used it for this].
            var body = new byte[9];
            BitConverter.GetBytes(guid).CopyTo(body, 0);
            body[8] = (byte)(enabled ? 1 : 0);
            foreach (WorldSession other in ViewersOf(prop))
                other.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetEnabledWire, body);
            log.Info($"{session.CharacterName} switched prop 0x{guid:X} {(enabled ? "on" : "off")}.");
        }

        /// <summary>
        /// PropBase 0x19 SetPropName {string (up to 128), u64 guid} (client writer FUN_1435fca20; 0.589 the same) [P]: the
        /// owner names a prop whose PropDefs row allows it. Read guid-first until 2026-10-17 (audit-wire-b.md #2).
        /// The name travels in the prop's PropData and in PlayerUpdate 0x7C, which floats it over the prop for everyone
        /// (the Prop HUD indicator shows the entity's name; Props.MenuName.cs).
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
            foreach (WorldSession viewer in ViewersOf(prop))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, update);
            log.Info($"{session.CharacterName} named prop 0x{guid:X} \"{name}\".");
        }

        /// <summary>PropBase 0x13 UpdatePropText {u64 guid, string}: new sign text (the client filters profanity).</summary>
        public static void SetText(WorldSession session, ulong guid, string text)
        {
            PlacedProp prop = Editable(session, guid);
            if (prop == null)
                return;
            text = StoryText(text ?? "");
            prop.Text = text;
            Persist(prop);
            // No reply: EditPropTextReply (0x15) always opens the editor (HudStateManager:OnOpenTextEdit), which the
            // player just closed with Save.
            // A sign that now has text can be read: flag it so nearby clients ask for its interaction info.
            if (IsReadable(guid))
                foreach (WorldSession viewer in ViewersOf(prop))
                {
                    var flagging = new DynamicInteractionFlagging();
                    flagging.Guids.Add(guid);
                    viewer.EnqueueMessage(flagging);
                }
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
        /// palette placement) with the original's tint and name. Its settings (a creature's Reaction / faction and
        /// respawn, a timer's duration...) follow when the copy is placed: P4352 (1 Apr 2016) a duplicated creature
        /// keeps its name, faction and respawn settings (TASKS 6.30). Settings are copied at placement, not
        /// here, so a cancelled copy leaves nothing behind.
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
            if (copy == null)
                return;
            copy.Spawn.TintAlias = original.Spawn.TintAlias;
            copy.DuplicateOf = original.Spawn.Guid;
            if (!string.IsNullOrEmpty(original.CustomName))
            {
                // The name shows on the copy at once (the owner already has it spawned for placement; others get it with
                // the copy's spawn once it is placed, SendSpawns).
                copy.CustomName = original.CustomName;
                session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, PropNameUpdate(copy));
            }
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
            foreach (WorldSession other in ViewersOf(prop))
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

        /// <summary>
        /// May the player take this prop out of the world (Send to Inventory, Recycle, Delete)? Its owner, or a player with
        /// Ultimate Power (<see cref="Claims.PermissionBuild"/>) on the site it stands on. The client's own texts for the
        /// ranks [P] (ClaimPermissionStringMappings → text): Let's Decorate 576196 / 576943 "When you need a helping hand,
        /// but don't want to give full control. Luminaries with this access can help you decorate by moving props on your
        /// Build Site"; Ultimate Power 576197 / 576944 "can do anything they want on your Build Site. Anything!". What a
        /// non-owner picks up goes to their own bag, as on live (hotfix 22 May 2015, P3596: picking up someone else's
        /// chest that holds a soulbound item now says the owner must take that item out first). Until 2026-10-18 "Let's
        /// Decorate" was enough, so a visitor trusted only to move props could take
        /// every prop on the site into their own bag (bughunt-build B2).
        /// </summary>
        private static bool MayRemove(WorldSession player, PlacedProp prop)
        {
            if (prop.OwnerGuid == player.PlayerGuid)
                return true;
            Vector3 at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            if (Claims.ClaimAt(prop.Zone, at) == null)
                return false;
            var global = Terrain.TerrainEdits.ToGlobal(at);
            return Claims.MayEdit(player.PlayerGuid, global.X, global.Y, global.Z, prop.Zone);
        }

        /// <summary>Whether the prop menu offers this player Send to Inventory / Recycle / Delete (<see cref="MayRemove"/>).</summary>
        public static bool CanRemove(WorldSession player, ulong guid) => Find(guid) is PlacedProp prop && prop.Placed && MayRemove(player, prop);

        /// <summary>May the player move this placed prop: its owner, or "Let's Decorate" on the site it stands on?</summary>
        private static bool MayRearrange(WorldSession player, PlacedProp prop)
        {
            if (prop.OwnerGuid == player.PlayerGuid)
                return true;
            Vector3 at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            return Claims.ClaimAt(prop.Zone, at) != null && MayDecorateAt(player, at, prop.Zone);
        }

        /// <summary>
        /// The three ways a placed prop leaves the world from the prop menu (RadialMenuButtons menu 1, both builds; the Lua
        /// natives in luabind_695.tsv 1333 / 1341 / 1342):
        /// <list type="bullet">
        /// <item>"Delete" (row 3, text 557190, Prop.DeleteObject → confirmation "Prop.DeleteConfirmation" → FUN_142fb6740
        /// mode 2): PropBase 0x07 Delete {u64 prop}. Nothing comes back.</item>
        /// <item>"Send to Inventory" (row 17, text 559201, Prop.PickUpObject FUN_1435fade0 → FUN_142fb66a0 → FUN_142fb6740
        /// mode 1): PropBase 0x05 PickUp {u64 prop, u8 0}. <see cref="Refund.AsPlaced"/>.</item>
        /// <item>"Recycle" (row 64, text 569162, Prop.DisassembleObject FUN_1435fae40 → FUN_142fb63e0 → FUN_142fb6740
        /// mode 3): the same PickUp with u8 <b>1</b>. <see cref="Refund.AsMaterials"/>. 0.589 sends the same
        /// (FUN_143548cc0 → FUN_142f17530 → FUN_142f17890, u8 1; Send to Inventory u8 0).</item>
        /// </list>
        /// A cancelled new placement sends one of the two pick-ups as well (<see cref="PlacementFlagRecycleOnCancel"/>).
        /// </summary>
        public enum Refund
        {
            /// <summary>Nothing back (Delete, a despawn).</summary>
            None,
            /// <summary>
            /// What paid for it: the bag item it was placed from, or for a prop that only exists as an item, that item; a
            /// prop paid with recipe materials gives its materials (so undoing or cancelling a placement is free, and
            /// materials never turn into prop items). <b>Inferred</b> for the material case: live's Send to Inventory on
            /// a recipe prop isn't documented (the wiki's prop menu lists Recycle; P4870 speaks against turning props
            /// into items).
            /// </summary>
            AsPlaced,
            /// <summary>
            /// The prop's recipe materials, "all ... no loss" (wiki Prop / Build Mode; trace/systems/building.md), with a
            /// loot notice per material (hotfix 11 May 2016, P5344: "When a prop is recycled, you will see what resources
            /// you got back in the loot window"). A prop without a recipe gives its item.
            /// </summary>
            AsMaterials
        }

        /// <summary>
        /// Client text Chest.PickupErrorHasItems 560928 "Can't pick up chest. Please clear out items first." (no client
        /// code shows it: a server message, trace/code-strings.md).
        /// </summary>
        private const string ChestHasItemsKey = "Chest.PickupErrorHasItems";

        /// <summary>
        /// PickUp (PropBase 0x05 {u64 prop, u8 recycle}) or Delete (0x07 {u64 prop}) of a placed prop (see
        /// <see cref="Refund"/> for what each gives back). Answered with PickUpReply (0x06) or DeleteReply (0x08): u64
        /// player, bool success, u32 error, u64 prop guid. PickUpReply errors the client shows (0.695 FUN_142fba9f0 case
        /// 6; 0.589 FUN_142f1b980): 3 inventory full, 6 PropLocInvalid, 7 no-trade item, 9 vendor still has stock, 10 no
        /// permission, 11 chest with soulbound items, 13 the site is in Play Mode, 14 a loot chest that paid out; 0 shows
        /// nothing.
        /// <para>
        /// Chests (TASKS 6.41): chests can't be reclaimed on purpose, since they may hold items (hotfix 26 Apr
        /// 2016, P4853, answering a report that the right-click menu had no reclaim for chests, forum thread 56901). So
        /// the prop
        /// menu offers no Recycle for chests (<see cref="OffersRecycle"/>) and a recycle request for one is answered as
        /// Send to Inventory; a chest that holds items is not picked up at all, with the live server's own text
        /// <see cref="ChestHasItemsKey"/> (and error 0, so the client adds nothing). Deleting a full chest stays refused.
        /// </para>
        /// </summary>
        public static void PickUp(WorldSession owner, ulong guid, bool delete, bool recycle = false)
        {
            PlacedProp prop;
            lock (sync)
                prop = props.FirstOrDefault(p => p.Spawn.Guid == guid);
            // Client pick-up errors (FUN_142f1b980): 9 vendor still has stock ("Prop.PickUpErrorVendorInventoryNotEmpty"),
            // 10 no permission, 13 the site is in Play Mode. Who may: see MayRemove (the owner, or Ultimate Power on the
            // site; not "Let's Decorate", which only moves props).
            bool stocked = prop != null && prop.PropId == Vendors.VendorProp && Vendors.HasStock(guid);
            Vector3 at = prop == null ? default : new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            ulong? site = prop == null ? null : Claims.ClaimAt(prop.Zone, at);
            bool playing = site is ulong siteId && Claims.IsPlaying(siteId);
            bool allowed = prop != null && MayRemove(owner, prop);
            // A reward chest that has given out items can't be picked up (0.695 FUN_142fba9f0 case 6: error 0xE →
            // "You cannot pickup a loot chest that has already given out items!"); deleting it stays allowed (no client
            // error exists for delete; spec-reward-chests.md §5.7).
            bool paidOut = !delete && prop != null && RewardChests.HasPaidOut(guid, prop.PropId);
            bool full = prop != null && Chests.HasItems(guid);
            bool ok = allowed && !playing && !full && !stocked && !paidOut;
            Refund mode = delete ? Refund.None
                : recycle && prop != null && Chests.Slots(prop.PropId) == 0 ? Refund.AsMaterials : Refund.AsPlaced;
            List<(uint Item, uint Count)> returned = null;
            if (ok)
            {
                RemoveOtherParts(prop);   // a mover's other parts go with it (Props.Movers)
                returned = Remove(prop, delete ? null : owner, mode);
            }
            else if (allowed && !playing && full)
            {
                if (delete)
                    Chat.System(owner, "Empty the chest before deleting it.");
                else
                    SystemMessages.ShowKey(owner, ChestHasItemsKey);
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            // Reply header (0.695 FUN_142fa8940, dispatcher FUN_142fba9f0 cases 6 / 8): u64 player, bool ok, and only when
            // not ok u32 error + u64; then the u64 prop [P]. Written {bool, u32, u64} always until the 2026-10-17 wire audit
            // (audit-wire-b.md #1): refusals were dropped by the client and a success named the wrong prop.
            writer.Write((byte)(delete ? 8 : 6));
            writer.Write(owner.PlayerGuid);
            writer.Write(ok);
            if (!ok)
            {
                writer.Write(stocked ? 9u : playing ? SiteInPlayMode : !allowed ? NoClaimPermission
                    : paidOut ? RewardChests.PartialLootChestError : 0u);
                writer.Write(0ul);
            }
            writer.Write(guid);
            owner.EnqueueMessage(ZoneMessageOpcode.PropBase, output.ToArray());
            // The loot window: one LootNotification per material a recycle gave back (P5344).
            if (mode == Refund.AsMaterials && returned != null)
                foreach ((uint item, uint count) in returned)
                    LootPinatas.Notify(owner, item, count);
            log.Info($"{owner.CharacterName} {(delete ? "deleted" : recycle ? "recycled" : "picked up")} prop 0x{guid:X}: "
                + $"{(ok ? "ok" : "refused")}{(returned is { Count: > 0 } ? $", back: {string.Join(", ", returned.Select(r => $"{r.Count} x {r.Item}"))}" : "")}.");
        }

        /// <summary>
        /// Does the prop menu offer "Recycle" (RadialMenuButtons row 64) for this prop? Props with a recipe give their
        /// materials back (wiki Prop: the menu's "Recycle"), except chests (P4853, see <see cref="PickUp"/>).
        /// </summary>
        public static bool OffersRecycle(ulong guid)
            => Find(guid) is PlacedProp prop && !GivesNothingBack(prop) && Chests.Slots(prop.PropId) == 0
               && RecipeCost(prop.PropId) is { Count: > 0 };

        /// <summary>
        /// Does the prop menu offer "Send to Inventory" (row 17)? For props that come back as an item: placed from a bag
        /// item (<see cref="PlacedProp.PaidByItem"/>, stored since the claim-inventory migration; older props have 0), or
        /// props that only exist as items (no
        /// recipe), and chests (live's only way to take one back, P4853). Free props (Starting Location, game markers)
        /// keep it so they can still be taken away. <b>Inferred</b> split: live's server chose the buttons; the wiki lists
        /// Recycle for recipe props.
        /// </summary>
        public static bool OffersSendToInventory(ulong guid)
            => Find(guid) is not PlacedProp prop || prop.PaidByItem != 0 || GivesNothingBack(prop)
               || Chests.Slots(prop.PropId) > 0 || RecipeCost(prop.PropId) is not { Count: > 0 };

        /// <summary>Picks up every prop an owner placed inside a local-space box (their build site going away).</summary>
        /// <summary>Zone of props packed away from a lapsed build site (no landscape: nobody sees them).</summary>
        public const uint StowedZone = 0;

        /// <summary>
        /// A lapsed site's props (an offline owner's, inside the box), TASKS 6.39. deleting a site, or losing it to
        /// upkeep, gives its props back as materials rather than as items in the overflow (hotfix 26 Apr 2016,
        /// P4870): every prop leaves the world and its materials (<see cref="Refund.AsMaterials"/>) are added to
        /// <paramref name="resources"/> for the expiry mail (<see cref="SiteContents.Pack"/>). Chests that hold items
        /// can't be emptied without their owner online, so they alone are kept, in the database too, on
        /// <see cref="StowedZone"/> until the owner opens their Build Site Contents (<see cref="Unstow"/>). Free placements
        /// (Starting Location, game markers) just go. Returns how many props left the site.
        /// </summary>
        public static int StowInBox(ulong ownerGuid, Vector3 min, Vector3 max, uint zone, List<(uint Item, uint Count)> resources)
        {
            List<PlacedProp> inside;
            lock (sync)
                inside = props.Where(p => p.OwnerGuid == ownerGuid && p.Placed && p.Zone == zone
                    && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                    && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                    && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z).ToList();
            int count = inside.Count;
            // Free placements (the Starting Location, game markers) are not packed: they just go with the site.
            foreach (PlacedProp free in inside.Where(GivesNothingBack).ToList())
            {
                Remove(free, null);
                inside.Remove(free);
            }
            // Everything but full chests: its materials go into the mail. A mover pack goes as a whole and is refunded once,
            // as its spawner (as a pick-up, RemoveOtherParts): every part refunds the spawner's materials (ReturnOf uses
            // PackSpawner), so refunding each part paid a Magical Platform back three times and a Translocation Pad pair
            // twice (bughunt-build B1).
            var packs = new HashSet<ulong>();
            foreach (PlacedProp prop in inside.Where(p => !Chests.HasItems(p.Spawn.Guid)).ToList())
            {
                inside.Remove(prop);
                if (prop.PackGroup != 0 && !packs.Add(prop.PackGroup))
                    continue;   // gone with the first part of its pack
                resources.AddRange(ReturnOf(prop, Refund.AsMaterials));
                RemoveOtherParts(prop);
                Remove(prop, null);
            }
            foreach (PlacedProp prop in inside)
            {
                Hide(prop);
                lock (sync)
                    prop.Zone = StowedZone;
            }
            var ids = inside.Select(p => p.Spawn.Guid).ToHashSet();
            if (ids.Count > 0)
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    foreach (WorldPropModel stored in c.WorldProp.Where(p => ids.Contains(p.Id)))
                        stored.ZoneId = StowedZone;
                }).ContinueWith(t => log.Error(t.Exception, "Failed to stow props."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            return count;
        }

        /// <summary>
        /// Build Site Contents opened: every stowed prop leaves for good and its materials go to the owner (P4870: props
        /// as materials, <see cref="Refund.AsMaterials"/>); a chest's contents go to the overflow tab (wiki Chests: on a
        /// lost site the chest's items go to the overflow inventory). Props stowed before
        /// 2026-10-16 (any kind, not only chests) are handled the same way. Returns how many.
        /// </summary>
        public static int Unstow(WorldSession owner)
        {
            List<PlacedProp> stowed;
            lock (sync)
                stowed = props.Where(p => p.OwnerGuid == owner.PlayerGuid && p.Zone == StowedZone).ToList();
            foreach (PlacedProp prop in stowed)
            {
                if (Chests.HasItems(prop.Spawn.Guid))
                    Chests.EmptyToOverflow(prop.Spawn.Guid, owner);
                List<(uint Item, uint Count)> back = Remove(prop, owner, Refund.AsMaterials);
                log.Info($"{owner.CharacterName}: stowed prop {prop.PropId} back as {string.Join(", ", back.Select(b => $"{b.Count} x {b.Item}"))}.");
            }
            return stowed.Count;
        }

        /// <summary>
        /// The owner deleted their site (Claims.Delete, owner online on its landscape): every prop of theirs inside the
        /// box leaves and comes back as materials (P4870 props come back as materials, not overflow items; <see
        /// cref="Refund.AsMaterials"/>); chests' contents go to the overflow tab.
        /// Returns how many props, and adds what came back to <paramref name="returned"/>.
        /// </summary>
        public static int ReturnInBox(WorldSession owner, Vector3 min, Vector3 max, uint zone, List<(uint Item, uint Count)> returned = null)
        {
            List<PlacedProp> inside;
            lock (sync)
                inside = props.Where(p => p.OwnerGuid == owner.PlayerGuid && p.Placed && p.Zone == zone
                    && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                    && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                    && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z).ToList();
            // A mover pack goes as a whole and is refunded once, as its spawner (see StowInBox; bughunt-build B1). The
            // removal is not inside "returned?.AddRange(...)": with no list given, that skipped the removal itself.
            var packs = new HashSet<ulong>();
            foreach (PlacedProp prop in inside)
            {
                if (prop.PackGroup != 0 && !packs.Add(prop.PackGroup))
                    continue;   // gone with the first part of its pack
                RemoveOtherParts(prop);
                List<(uint Item, uint Count)> back = Remove(prop, owner, Refund.AsMaterials);
                returned?.AddRange(back);
            }
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
        /// A prop placed for free: site game markers added from the game editor, the site's Starting Location (wiki: it
        /// costs no resources, and a new one can always be placed for free; every new site gets one,
        /// <see cref="Claims.EnsureStartingLocation"/>), and anything placed with <see cref="PlacedProp.Free"/> — unless it
        /// was placed from a bag item. Picking one up returns nothing, and a lapsed site's contents leave it out.
        /// </summary>
        private static bool GivesNothingBack(PlacedProp prop)
            => prop.PaidByItem == 0 && (prop.Free || InstancedGames.IsMarker(prop.PropId)
                || prop.PropId == StoryTools.StartingLocationProp);

        /// <summary>
        /// What a prop gives back when it leaves the world in <paramref name="mode"/> (see <see cref="Refund"/>): nothing
        /// for free placements (<see cref="GivesNothingBack"/>). Recipe materials are the PropRecipeComponentMaps
        /// components (<see cref="RecipeCost"/>), the same the palette charged.
        /// </summary>
        public static List<(uint Item, uint Count)> ReturnOf(PlacedProp prop, Refund mode)
        {
            var back = new List<(uint Item, uint Count)>();
            if (mode == Refund.None || GivesNothingBack(prop))
                return back;
            uint refundProp = prop.PackSpawner != 0 ? prop.PackSpawner : prop.PropId;   // a mover part: its spawner
            List<(uint Item, uint Count)> cost = RecipeCost(refundProp);
            uint itemForm = Inventory.ItemDefinitionManager.ItemsForProp(refundProp).FirstOrDefault()?.Id ?? 0;
            if (mode == Refund.AsPlaced)
            {
                uint item = prop.PaidByItem != 0 ? prop.PaidByItem : cost == null ? itemForm : 0;
                if (item != 0)
                {
                    back.Add((item, 1));
                    return back;
                }
            }
            if (cost != null)
                back.AddRange(cost.Where(c => c.Item != 0 && c.Count > 0));
            else if (itemForm != 0)
                back.Add((itemForm, 1));
            return back;
        }

        /// <summary>
        /// Uses up the bag item a placement started from: that very stack (<see cref="PlacedProp.PaidByItemGuid"/>, the item
        /// the player activated) when it is still there, else any one of its kind. Taking "any one" by definition first
        /// could take a different stack of the same item: with a picked-up chest that still holds items (its own single
        /// item, Chests.Move) and an empty one of the same kind in the bag, placing the empty one could delete the full
        /// one, whose contents then stayed keyed to a guid nothing held any more (lost), while the empty one stayed.
        /// </summary>
        private static bool TakePlacementItem(WorldSession owner, PlacedProp prop)
        {
            if (prop.PaidByItemGuid != 0 && owner.Inventory.Get(prop.PaidByItemGuid) is { } item
                && item.ItemDefinitionId == prop.PaidByItem)
                return owner.TakeFromStack(item.Id, 1) != null;
            return owner.TakeItem(prop.PaidByItem, 1);
        }

        /// <summary>
        /// Takes a prop out of the world (and the database). <paramref name="refundTo"/> (when set) gets back
        /// <see cref="ReturnOf"/> the prop in <paramref name="mode"/>; returned is what they got. A chest's contents: when it
        /// comes back as its own item, the contents stay inside that item (<see cref="Chests.Move"/>); when it comes back
        /// as materials, they go to the overflow tab (<see cref="Chests.EmptyToOverflow"/>, as the wiki's lost-site chests).
        /// </summary>
        private static List<(uint Item, uint Count)> Remove(PlacedProp prop, WorldSession refundTo, Refund mode = Refund.AsPlaced)
        {
            lock (sync)
            {
                props.Remove(prop);
                byGuid.Remove(prop.Spawn.Guid);
            }
            // An unplaced prop going away is a cancelled placement (Escape, re-zone): build mode closes again if a bag
            // placement opened it (BuildModeHolster).
            if (!prop.Placed)
                BuildModeHolster.PlacementEnded(prop.Spawn.Guid);
            if (prop.Placed && (Chests.Slots(prop.PropId) > 0 || IsSpawner(prop.PropId)))
                PropLimits.Changed(prop.Zone, new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z));
            StoryTools.PropRemoved(prop.Spawn.Guid);
            PropSettings.PropRemoved(prop.Spawn.Guid);
            // Linking Area hook: a Linking Area's trigger volume goes with its prop (TASKS 2.33).
            if (prop.PropId == StoryTools.LinkingAreaProp)
                AreaDefinitions.PropRemoved(prop.Spawn.Guid);
            Hide(prop);   // the players streaming it (a prop still on its owner's cursor: the owner)
            ulong id = prop.Spawn.Guid;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                WorldPropModel stored = c.WorldProp.FirstOrDefault(p => p.Id == id);
                if (stored != null)
                    c.WorldProp.Remove(stored);
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to delete prop 0x{id:X}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            // Free placements give nothing back (no farming of free placements) unless placed from a bag item.
            var none = new List<(uint Item, uint Count)>();
            if (refundTo == null || !prop.Placed || mode == Refund.None)
                return none;
            List<(uint Item, uint Count)> back = ReturnOf(prop, mode);
            bool asItem = back.Count == 1 && back[0].Count == 1 && Inventory.ItemDefinitionManager.Get(back[0].Item)?.PropId == prop.PropId;
            if (Chests.HasItems(prop.Spawn.Guid))
            {
                if (asItem)
                {
                    // A chest coming back as its item keeps its contents inside it: its own (unstacked) item.
                    var single = refundTo.Inventory.AddSingle(back[0].Item);
                    if (single != null)
                    {
                        refundTo.SendItem(single, replace: false);
                        Chests.Move(prop.Spawn.Guid, single.Id);
                        return back;
                    }
                    // No free bag slot (an admin's claimItemToInventory: a player's own pick-up of a full chest is
                    // refused): the contents go to the overflow tab and the chest item with them below. Before, nothing
                    // was given and the contents stayed keyed to the removed prop: the chest and its items were lost.
                }
                Chests.EmptyToOverflow(prop.Spawn.Guid, refundTo);
            }
            foreach ((uint item, uint count) in back)
                refundTo.GiveItem(item, count);
            return back;
        }

        /// <summary>
        /// A new placed prop: streamed by its region (Props.Stream.cs) to the players holding it.
        /// </summary>
        private static void Broadcast(PlacedProp prop)
        {
            lock (sync)
                IndexLocked(prop);
            Show(prop);
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
                // Story tools stay out of the world while their site plays (Props.PlayModeVisibility.cs, L20).
                if (HiddenFrom(session, prop))
                    continue;
                // A Respawn Point carries its enabled / disabled look (AnimationSelection 848 / 849).
                if (prop.PropId == StoryTools.RespawnPointProp)
                    prop.Spawn.SpawnAnimations = SpawnLook(prop);
                prop.Spawn.Enabled = !prop.Disabled;   // switched off from the prop menu: a pad / flinger stays off
                session.EnqueueMessage(prop.Spawn);
                ReleaseIfServerMoved(session, prop);   // a moving platform: the client must not keep it (Props.Platforms)
                if (!string.IsNullOrEmpty(prop.CustomName))
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, PropNameUpdate(prop));
                if (!prop.Disabled)
                    PropEffects.Send(session, prop.PropId, prop.Spawn.Guid);
                StoryTools.SendStateTo(session, prop.Spawn.Guid);
                // Usable props, and the props this viewer may edit: the client only lets the cursor pick (right-click
                // menu, tint, edit tools) entities the server flagged and answered as interactable (editable ones
                // only while in build mode, see SendInteractionInfo).
                if (CraftingStations.StationForProp(prop.PropId) != 0 || Chests.Slots(prop.PropId) > 0 || StoryTools.IsClickable(prop.PropId)
                    || RewardChests.IsRewardChestProp(prop.PropId)
                    || StoryTools.IsClickLinked(prop.Spawn.Guid) || IsReadable(prop.Spawn.Guid)
                    || prop.PropId == 489 || Vaults.IsVaultProp(prop.PropId) || MayRearrange(session, prop))
                    flagging.Guids.Add(prop.Spawn.Guid);
            }
            if (flagging.Guids.Count > 0)
                session.EnqueueMessage(flagging);
        }

        /// <summary>
        /// PlayerUpdate 0x7C UpdatePropName {u64 prop, string name} (reader FUN_142bb4650, PlayerUpdate_Dispatch case
        /// 0x7b; 0.695 reader FUN_142c4de60, FUN_142cdfa60 case 0x7b): the prop entity's shown name; an empty name is
        /// replaced by the text of the spawn's NameId (+0x4e98 = spawn +0x58, 0 on our props), see Props.MenuName.cs.
        /// </summary>
        private static byte[] PropNameUpdate(PlacedProp prop) => PropNameUpdate(prop.Spawn.Guid, prop.CustomName ?? "");

        /// <summary>The 0x7C body for any name (the build menu's label, Props.MenuName.cs).</summary>
        private static byte[] PropNameUpdate(ulong guid, string name)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(guid);
            writer.Write(name ?? "");
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
                    || IsMailbox(guid) || IsParlor(guid) || Vaults.IsVault(guid) || IsReadable(guid)
                    || WarpNetwork.IsCrystal(guid) || Outposts.Usable(guid) || LootChests.IsChest(guid)
                    || (Find(guid) is PlacedProp switchProp && StoryTools.IsClickable(switchProp.PropId)) || StoryTools.IsClickLinked(guid)
                    || RewardChests.IsRewardChest(guid)
                    || Creatures.IsInteractable(guid);
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
            var guids = PlacedNear(session, EditableRefreshRange).Where(p => p.Placed && MayRearrange(session, p) && !HiddenFrom(session, p)
                    && Streams(session, p.Spawn.Guid))
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
        public static List<(uint Item, uint Count)> RecipeCostOf(uint propId) => RecipeCost(propId);

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
                Disabled = prop.Disabled,
                PackGroup = prop.PackGroup,
                PackSpawner = prop.PackSpawner,
                PaidByItem = prop.PaidByItem,
                PaidByItemGuid = prop.PaidByItemGuid
            };
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
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
                        PackGroup = model.PackGroup,
                        PackSpawner = model.PackSpawner,
                        PaidByItem = model.PaidByItem,
                        PaidByItemGuid = model.PaidByItemGuid,
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
            lock (sync)
            {
                foreach (PlacedProp part in props.Where(p => p.PackGroup != 0))
                    BindMover(part);
                foreach (PlacedProp p in props)
                    byGuid[p.Spawn.Guid] = p;
                IndexAllLocked();
            }
            log.Info($"Loaded {stored.Count} placed props ({byRegion.Count} stream region(s)).");
            // Linking Area hook: the Area Tool's trigger volumes, which give their props back the box size (the prop table
            // keeps one scale) and the area guid in the spawn, before anyone is sent them (TASKS 2.33).
            AreaDefinitions.Load();
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

        /// <summary>The nearest placed prop within 20 m of a player on their landscape (0 = none; dev "propmenu").</summary>
        public static ulong NearestPlaced(WorldSession session)
        {
            lock (sync)
                return props.Where(p => p.Placed && p.Zone == session.ZoneId)
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
        /// "Mailbox": mail can be read anywhere, the mailbox just makes checking it more fun).
        /// </summary>
        public static bool IsMailbox(ulong guid) => Find(guid)?.PropId == 489;

        /// <summary>
        /// A placed Crimson Parlor (PropDefs 2942): clicking it opens the barber window, as the Spire's does — 7 Jun 2016,
        /// P5725: now at every spire hub, and as a prop for build sites (TASKS 3.3).
        /// </summary>
        public static bool IsParlor(ulong guid) => Find(guid)?.PropId == 2942;

        public static PlacedProp Find(ulong guid)
        {
            lock (sync)
                return byGuid.TryGetValue(guid, out PlacedProp p) ? p : null;
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
            bool replacing = prop.ReplacingBy != 0 && prop.ReplacingBy == session.PlayerGuid;
            if (prop.AwaitingPlacement && (replacing || (prop.ReplacingBy == 0 && session.PlayerGuid == prop.OwnerGuid)))
            {
                prop.AwaitingPlacement = false;
                byte flags = replacing ? PlacementFlagRestoreOnCancel
                    : prop.PaidByItem == 0 && !prop.Free && RecipeCost(prop.PropId) != null ? PlacementFlagRecycleOnCancel : (byte)0;
                session.EnqueueMessage(ZoneMessageOpcode.PropBase, BuildPropData(session.PlayerGuid, prop, flags: flags));
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
    }
}
