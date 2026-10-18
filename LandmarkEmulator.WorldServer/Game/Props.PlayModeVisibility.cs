using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Story tools in Play Mode, and the Respawn Point's enabled look (TASKS E.17, OPEN-LEADS L20; full trace with proof
    /// marks: trace/0695/agent-playmode.md).
    /// <para>
    /// <b>Hidden in Play Mode.</b> Live hid the story tools while their site was in Play Mode (hidden in Play Mode like
    /// every story tool, 30 Jun 2016, P5960; Story Nodes show no chat bubbles because Play Mode hides them, 11 May
    /// 2016, P5288); the Scoreboard is a counting node that is always visible (P6182) and the Starting Location shows
    /// in Play Mode only with the Build Mode UI open (wiki).
    /// The client has no rule of its own for this [P]: the claim entry's mode (+0x378, set by the trivial vtable +0x28
    /// FUN_143c17a40 from ClaimInfoUpdate kind 0x13) is read only by the map label FUN_142d99b70 ("PlayMode"), the
    /// AdventureStatus data sources, the camera / grapple / boot-ability rules and the build-tool pick mask
    /// (FUN_142f7ce40); no prop code reads it, and the Counting Node and Scoreboard differ only in their mesh (same
    /// PropDefs row shape, same live prop-list record, identical .adr apart from the mesh names, byte-identical .dma).
    /// So the server keeps them from the client: the entity is removed (PlayerUpdate RemovePlayer) and not spawned again
    /// while the site plays. That is also what the bubble rule needs: DisplayPropText (FUN_142f859b0) shows a bubble
    /// only over an entity it finds, and its chat line drops the "[name] says:" part when the entity is missing.
    /// </para>
    /// <para>
    /// <b>Respawn Point look.</b> The Respawn Point's actor (model 1739, RespawnAnimationNetwork.mrn, anim set
    /// RespawnMarker02) has a switch on control parameter <c>Active</c> (0 → SimpleObject_Chest_Idle_01, 1 → _Idle_02);
    /// "Active" exists in no other 0.695 network, and AnimationSelection 848 / 849 (new in 0.695) set Active 1 / 0. The
    /// prop spawn carries selections to play on creation (<see cref="Network.Message.Model.PlayerUpdateAddLightweightProp.SpawnAnimations"/>),
    /// so the server sends 848 for an enabled point and 849 for a disabled one (P6178: "The appearance of the prop
    /// changes to indicate whether it is enabled or disabled"). [I, strong] Active 1 is the enabled look.
    /// </para>
    /// </summary>
    public static partial class Props
    {
        /// <summary>
        /// Story tools hidden while their site is in Play Mode: every wiki story tool page that says visible in Edit
        /// Mode only (Combat, Counting, Damage, Focus, Reset, Story, Server Time Node; Logic, Probability,
        /// Random Link, Timer Filter; Respawn Point; Travel Point), plus [I] the IF Gate 2976 (Aug 2016, no wiki page;
        /// "Like all Story Tools", P5960) and the Hour Node 2945 (the Server Time Node's kind). Not here: the Scoreboard
        /// 3000 (visible), the Starting Location 2655 (<see cref="StoryTools.StartingLocationProp"/>, its own rule), the
        /// Game Table 2313 and the Score / Victory Nodes 3011 / 3010 (no statement found; see the report's open leads).
        /// </summary>
        private static readonly HashSet<uint> hiddenInPlayMode = new()
        {
            StoryTools.CombatNodeProp, StoryTools.CountingNodeProp, StoryTools.DamageNodeProp, StoryTools.FocusNodeProp,
            StoryTools.ResetNodeProp, StoryTools.StoryNodeProp, StoryTools.ServerTimeNodeProp, StoryTools.HourNodeProp,
            StoryTools.LogicFilterProp, StoryTools.DiceRollProp, StoryTools.RandomLinkFilterProp, StoryTools.TimerProp,
            StoryTools.RespawnPointProp, StoryTools.TravelPointProp, StoryTools.IfGateProp
        };

        /// <summary>AnimationSelection 848 (Active 1) / 849 (Active 0): the Respawn Point's enabled / disabled look.</summary>
        public const uint RespawnLookEnabled = 848, RespawnLookDisabled = 849;

        /// <summary>Who may see a prop while its site plays.</summary>
        public enum PlayModeSight { Everyone, BuildModeOnly, Nobody }

        private static Vector3 PositionOf(PlacedProp prop) => new(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);

        /// <summary>Whether the prop is one of the tools Play Mode hides (Starting Location included).</summary>
        private static bool HidesInPlayMode(uint propId)
            => propId == StoryTools.StartingLocationProp || hiddenInPlayMode.Contains(propId);

        /// <summary>
        /// Who sees a placed prop right now: everyone, unless it is a hidden story tool on a site in Play Mode (the
        /// Starting Location then only to players in build mode). A prop still being placed is always shown.
        /// </summary>
        public static PlayModeSight SightOf(PlacedProp prop)
        {
            if (prop == null || !HidesInPlayMode(prop.PropId) || !prop.Placed || prop.AwaitingPlacement)
                return PlayModeSight.Everyone;
            ulong? site = Claims.ClaimAt(prop.Zone, PositionOf(prop));
            if (site == null || !Claims.IsPlaying(site.Value))
                return PlayModeSight.Everyone;
            return prop.PropId == StoryTools.StartingLocationProp ? PlayModeSight.BuildModeOnly : PlayModeSight.Nobody;
        }

        /// <summary>Who sees a placed prop by guid (<see cref="SightOf(PlacedProp)"/>; Everyone when it isn't a placed prop).</summary>
        public static PlayModeSight SightOf(ulong guid) => SightOf(Find(guid));

        /// <summary>Whether a viewer sees a prop of this sight.</summary>
        public static bool Sees(WorldSession viewer, PlayModeSight sight)
            => sight == PlayModeSight.Everyone || (sight == PlayModeSight.BuildModeOnly && BuildModeHolster.InBuildMode(viewer));

        private static bool HiddenFrom(WorldSession viewer, PlacedProp prop) => !Sees(viewer, SightOf(prop));

        /// <summary>
        /// The site switched mode (<see cref="Claims.ApplyMode"/>, after the story started or stopped): its hidden story
        /// tools leave every viewer's world (Play) or come back (Edit). A Starting Location stays for players in build
        /// mode. Only players streaming each tool (Props.Stream.cs) get it: a player mid re-zone streams nothing (their
        /// arrival streams the props in with the site's mode applied, <see cref="SendSpawns"/>), one in an arrival hold has
        /// only the props sent early (<see cref="SendEarly"/>; review 2026-10-18 #3c), and one far away gets the tools when
        /// they come into range.
        /// </summary>
        public static void PlayModeVisibilityChanged(ClaimModel claim, bool play)
        {
            List<PlacedProp> tools;
            lock (sync)
                tools = props.Where(p => p.Placed && !p.AwaitingPlacement && p.Zone == claim.ZoneId && HidesInPlayMode(p.PropId)).ToList();
            tools = tools.Where(p => Claims.ClaimAt(p.Zone, PositionOf(p)) == claim.Id).ToList();
            if (tools.Count == 0)
                return;
            foreach (WorldSession viewer in PlayerManager.InZone(claim.ZoneId))
            {
                bool building = BuildModeHolster.InBuildMode(viewer);
                var theirs = tools.Where(p => (!building || p.PropId != StoryTools.StartingLocationProp)
                    && Streams(viewer, p.Spawn.Guid)).ToList();
                if (theirs.Count == 0)
                    continue;
                if (play)
                    foreach (PlacedProp prop in theirs)
                        SendRemove(viewer, prop);
                else
                    SendSpawns(viewer, theirs);
            }
            log.Info($"Site {claim.Id}: {tools.Count} story tool(s) {(play ? "hidden for Play Mode" : "shown again for Edit Mode")}.");
        }

        /// <summary>
        /// A player entered or left build mode (<see cref="BuildModeHolster.BuildMode"/>): the Starting Locations of the
        /// playing sites on their landscape appear (entered) or go (left).
        /// </summary>
        public static void BuildModeVisibilityChanged(WorldSession session, bool building)
        {
            // As PlayModeVisibilityChanged: only the ones streamed to the player (none mid re-zone; an arrival hold's early
            // props get the change now).
            List<PlacedProp> starts;
            uint zone = session.ZoneId;
            lock (sync)
                starts = props.Where(p => p.Zone == zone && p.PropId == StoryTools.StartingLocationProp).ToList();
            starts = starts.Where(p => SightOf(p) == PlayModeSight.BuildModeOnly && Streams(session, p.Spawn.Guid)).ToList();
            if (starts.Count == 0)
                return;
            if (building)
                SendSpawns(session, starts);
            else
                foreach (PlacedProp prop in starts)
                    SendRemove(session, prop);
        }

        /// <summary>The spawn animations a prop carries: a Respawn Point's enabled / disabled look (null for the rest).</summary>
        private static uint[] SpawnLook(PlacedProp prop)
        {
            if (prop.PropId != StoryTools.RespawnPointProp)
                return null;
            bool enabled = !PropSettings.TryGetSaved(prop.Spawn.Guid, PropSettings.RespawnPointEnabled, out float v) || v != 0f;
            return new[] { enabled ? RespawnLookEnabled : RespawnLookDisabled };
        }

        /// <summary>
        /// A Respawn Point's "Enabled" box (row 103) changed: viewers who see it get the new look at once.
        /// <list type="bullet">
        /// <item>An upright point (rotated about the vertical only): AbilityPlayAnim 848 / 849 on the prop (the client
        /// finds props by guid like creatures, FUN_142f85390). That packet also sets the entity's heading from the
        /// "towards" point (FUN_142e1b9d0 → FUN_142ec4e60, heading only), so it is aimed along the prop's own forward
        /// (its rotation turns +Z, the convention <see cref="Creatures"/> uses for spawn rotations and headings): the
        /// facing stays as it was. [I] the heading convention is the creatures' one, not separately proven for props.</item>
        /// <item>A tilted point would lose its tilt that way, so it is removed and spawned again with the look in its
        /// spawn (<see cref="SpawnLook"/>, proven path) instead.</item>
        /// </list>
        /// </summary>
        public static void RespawnLookChanged(ulong guid)
        {
            PlacedProp prop = Find(guid);
            if (prop == null || prop.PropId != StoryTools.RespawnPointProp || !prop.Placed || prop.AwaitingPlacement)
                return;
            uint look = SpawnLook(prop)[0];
            prop.Spawn.SpawnAnimations = new[] { look };
            var q = new Quaternion(prop.Spawn.Rotation.X, prop.Spawn.Rotation.Y, prop.Spawn.Rotation.Z, prop.Spawn.Rotation.W);
            q = q.LengthSquared() < 1e-6f ? Quaternion.Identity : Quaternion.Normalize(q);
            bool upright = System.MathF.Abs(q.X) < 1e-3f && System.MathF.Abs(q.Z) < 1e-3f;
            byte[] body = null;
            if (upright)
            {
                Vector3 forward = Vector3.Transform(Vector3.UnitZ, q);
                body = Creatures.AnimationBody(guid, look, PositionOf(prop) + forward * 5f);
            }
            PlayModeSight sight = SightOf(prop);
            foreach (WorldSession viewer in ViewersOf(prop))
            {
                if (viewer.RezoneTarget != null || !Sees(viewer, sight))
                    continue;
                if (body != null)
                    viewer.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityPlayAnim, body);
                else
                {
                    SendRemove(viewer, prop);
                    SendSpawns(viewer, new[] { prop });
                }
            }
        }
    }
}
