using System;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tracks the players who are in the world and keeps every client's view of the others up to date: spawns on
    /// entry, relays movement, despawns on leave.
    /// </summary>
    public static class PlayerManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly object sync = new();
        private static readonly List<WorldSession> players = new();
        private static int nextTransientId;

        /// <summary>
        /// Players in the world whose login loading screen is still up, with the time they entered. Nobody is shown to
        /// them and they are shown to nobody until it drops (<see cref="LoadingScreenLeft"/>), so both sides meet at the
        /// same moment, when the new client can show the others (bug 2026-10-18: after a login the others saw the
        /// newcomer 15-25 s before the newcomer saw anyone).
        /// </summary>
        private static readonly Dictionary<WorldSession, DateTime> loading = new();

        /// <summary>
        /// A loading screen that never reports its drop is given up on after this long and the player is shown anyway
        /// [I ours; the dev world's logins took 15-25 s from ClientFinishedLoading to the drop].
        /// </summary>
        public static readonly TimeSpan LoadingShowLimit = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Transient ids identify an entity in movement packets. 0 is avoided as it reads as "none".
        /// </summary>
        public static uint AllocateTransientId() => (uint)Interlocked.Increment(ref nextTransientId);

        /// <summary>
        /// A player enters the world: replaces a stale session of the same character, introduces the player and everyone
        /// already online to each other (AddLightweightPc and visible equipment), then sends the world state that isn't in
        /// SendSelf: claims, health, placed props, the design list and the Spire. Friends and guilds are told the player is
        /// online.
        /// </summary>
        public static void Enter(WorldSession session)
        {
            // A connection that has already ended never enters (a delayed zone-in after the player dropped would add a
            // ghost and kick a fresh login of the same character; review 2026-10-18 #2).
            if (session.Disconnected)
            {
                log.Info($"{session.CharacterName}: not entered, the connection has ended.");
                return;
            }
            // Replaced by a newer login of the character while it was still loading: it stays out.
            if (session.Replaced)
            {
                log.Info($"{session.CharacterName}: not entered, a newer login of the character replaced it.");
                return;
            }
            // Called with every movement packet of a session in the world: nothing more to do then.
            lock (sync)
                if (players.Contains(session))
                    return;
            // Normally done at the login already (WorldSession.OnCharacterLogin); here for the rest (a re-attach after a
            // zone restart: of a dead and a live connection of one character only the live one gets this far).
            ReplaceOlderSessions(session);

            lock (sync)
            {
                if (players.Contains(session))
                    return;

                PruneDead();

                // Still behind its loading screen (ClientLoading 0x6C {0} not in yet): the introductions below are held
                // until it drops (Introduce skips the pair).
                if (!session.LoadingScreenDown)
                    loading[session] = DateTime.UtcNow;
                foreach (WorldSession other in players.Where(p => p.ZoneId == session.ZoneId))
                {
                    Introduce(session, other);
                    Introduce(other, session);
                }

                players.Add(session);
                session.MarkOnline();
                Health.SendInitial(session);
                session.SendCurrencies();
                CharacterResources.SendInitial(session);
                SiteUpkeep.Enter(session);
                SiteContents.Deliver(session);
                Templates.SendList(session);
                Claims.SendAll(session);
                Props.StreamIn(session);   // the placed props within PropStreamRange (Props.Stream.cs)
                Claims.EnsureStartingLocation(session);
                Spire.Enter(session);   // every landscape has its Spire (TASKS 6.49)
                Spire.ReassertCollision(session);   // its deck's collision once the models have loaded
                WarpNetwork.Enter(session);   // the landscape's underground portal crystals (every landscape with caverns, L130)
                log.Info($"{session.CharacterName} entered the world ({players.Count} online).");
            }
            // Friends list, ignore list and guild (the social service's character-entered event).
            Social.SocialServices.Service.PlayerEntered(session);
            // Player Studio: every player is a registered member (free; live required signing up on a web page).
            WorldChannelServices.Commerce.PlayerStudioRegistered(session);
            // Player Studio's listing fee and minimum price (CommerceConfiguration 0x2E); the zone's in both commerce
            // modes, as the fee is charged in this world's Lumens (the wallet went with SendCurrencies above).
            Commerce.SendConfiguration(session);
            // The client's store 1: live's Marketplace products this store sells and the membership row, at this player's
            // prices, so the Crimson Parlor lists the Fantasy Parlor Pack's options and tints with its Unlock button
            // (TASKS 4.9; Game/StoreBundles.cs). The zone's in both commerce modes: the Showcase store and its prices are
            // this world's.
            StoreBundles.Send(session);
            WorldChannelServices.Commerce.ShowcaseSignIn(session);
            Admin.Enter(session);
            // Founder's Pack / Year Zero / promotional items, once per character while switched on (TASKS 6.59).
            Entitlements.Enter(session);
            SendMotd(session);
            // The account's trial key letter, once (TASKS 3.12).
            Membership.Enter(session);
            // A character under grief suspension is told and turned away (Game/Grief).
            Grief.Enter(session);
        }

        /// <summary>
        /// A login (or re-attach) of <paramref name="session"/>'s character: every other session of the same character —
        /// in the world, or still loading — leaves now, before the new client's loading screen, so the others stop seeing
        /// a character whose client died (crash, killed process, Alt+F4, lost network) and hasn't timed out yet
        /// (CLIENT_TIMEOUT_SECONDS). The old one gets the full leave clean-up (open trades, chest windows, gathering
        /// state...), or e.g. a trade with the crashed client stays open and the partner can't trade again; a craft it
        /// was running finishes into the new session's inventory (ZoneSessions.ReplacementOf). Before 2026-10-18 this ran
        /// only when the new client entered the world (its ClientIsReady, ~30 s after the login). World thread; outside
        /// the lock, as Leave also tells groups, friends and guilds.
        /// </summary>
        public static void ReplaceOlderSessions(WorldSession session)
        {
            if (session == null || session.PlayerGuid == 0)
                return;
            List<WorldSession> older;
            lock (sync)
                older = players.Where(p => p.PlayerGuid == session.PlayerGuid && p != session).ToList();
            foreach (WorldSession other in ZoneSessions.All())
                if (other != session && other.PlayerGuid == session.PlayerGuid && !other.Replaced && !older.Contains(other))
                    older.Add(other);
            foreach (WorldSession old in older)
            {
                old.Replaced = true;
                Leave(old);
                // A second login of the character while the first client is still connected: like live, the first
                // client is kicked with the client's own message box (PacketClientKickedFromServer 0x53 {string
                // StringFixed<128>}, Zone_DispatchPacket case 0x24: ErrorLabelWarning + KickedFromServerMessage with
                // [message]), then its connection is closed once the message has gone out (a dead client's connection
                // closes the same way, without waiting for its timeout).
                if (!old.Disconnected)
                {
                    Kicks.KickNow(old, "Your character has logged in from another location.");
                    log.Info($"{old.CharacterName}: an older session was kicked (logged in again).");
                }
            }
        }

        /// <summary>
        /// PacketMOTD (0x5A, reader FUN_142bb0af0: {string subject, string body}; zone dispatcher shows
        /// "subject\nbody" through ShowSystemMessage and appends {Subject, body} to the UI.MessageOfTheDay data source),
        /// once per login, from WorldServer.json MotdSubject / MotdBody.
        /// </summary>
        private static void SendMotd(WorldSession session)
        {
            if (session.MotdSent)
                return;
            var config = Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            string subject = config.MotdSubject ?? "", body = config.MotdBody ?? "";
            if (subject.Length == 0 && body.Length == 0)
                return;
            session.MotdSent = true;
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            w.Write(subject);
            w.Write(body);
            session.EnqueueMessage(ZoneMessageOpcode.MOTD, output.ToArray());
        }

        /// <summary>
        /// Shows one player to another: the character (AddLightweightPc), what they visibly wear and wield, and their
        /// title when they show one.
        /// </summary>
        private static void Introduce(WorldSession viewer, WorldSession subject)
        {
            if (AdminCommands.IsHidden(subject))
                return;   // an admin under /hideme stays out of everyone else's view
            if (loading.ContainsKey(viewer) || loading.ContainsKey(subject))
                return;   // one of them is still behind its login loading screen: they meet when it drops
            viewer.EnqueueMessage(subject.BuildAddLightweightPc());
            foreach ((uint slot, var item) in subject.VisibleEquipment())
                viewer.EnqueueMessage(subject.BuildEquipmentSlot(slot, item));
            if (Titles.Shows(subject))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayerTitleWire, Titles.UpdatePacket(subject));
            Health.IntroduceDead(viewer, subject);
            Afk.Introduce(viewer, subject);
            HeroicMovement.IntroduceSwimmer(viewer, subject);
            Combat.BlockStance.Introduce(viewer, subject);   // a blocking stance still up (entity state bit 45 + Block effect)
        }

        /// <summary>
        /// The client's login loading screen dropped (ClientLoading 0x6C {0}, MiscHandler.HandleLoading): the player and
        /// everyone on the landscape are shown to each other now, once each way. The client would take the characters
        /// earlier too (AddLightweightPc has no run-state gate, dispatcher FUN_142cdfa60 case 0x57 [P]), but they then
        /// stood on the others' screens for the whole load while this client showed nobody.
        /// </summary>
        public static void LoadingScreenLeft(WorldSession session)
        {
            lock (sync)
            {
                if (!loading.Remove(session) || !players.Contains(session))
                    return;
                ShowToZone(session);
                log.Info($"{session.CharacterName}: loading screen down, shown to the landscape's players.");
            }
        }

        /// <summary>The player and everyone else on the landscape meet, both ways (under the lock).</summary>
        private static void ShowToZone(WorldSession session)
        {
            foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId))
            {
                Introduce(session, other);
                Introduce(other, session);
            }
        }

        /// <summary>
        /// Once a second: forgets held players who left, and shows the ones whose loading screen never reported its
        /// drop within <see cref="LoadingShowLimit"/>.
        /// </summary>
        private static void ShowOverdue(DateTime now)
        {
            lock (sync)
            {
                foreach ((WorldSession session, DateTime since) in loading.ToList())
                {
                    if (!players.Contains(session))
                        loading.Remove(session);
                    else if (now - since > LoadingShowLimit)
                    {
                        loading.Remove(session);
                        ShowToZone(session);
                        log.Warn($"{session.CharacterName}: no loading-screen drop after {LoadingShowLimit.TotalSeconds:0} s, shown anyway.");
                    }
                }
            }
        }

        /// <summary>
        /// A player's look changed (the barbershop): everyone else on the landscape forgets them and meets them again, so
        /// their clients ask for the new full data (LightweightToFullPc carries the appearance).
        /// </summary>
        public static void Reshow(WorldSession session)
        {
            lock (sync)
            {
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId))
                {
                    other.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = session.PlayerGuid });
                    Introduce(other, session);
                }
            }
        }

        /// <summary>
        /// After a re-zone (<see cref="Teleport.Rezone"/>) the client has dropped every entity but its own character
        /// (FUN_142ee4930 clears the entity tables on ClientBeginZoning). Sends back what zone-in sent: the other
        /// players and their visible equipment, placed props and the Spire; trees, plants and creatures are forgotten so
        /// their streaming sends the regions around the new position again. Nothing is sent to the other players: their
        /// view of this player is unchanged.
        /// </summary>
        public static void Reintroduce(WorldSession session)
        {
            lock (sync)
            {
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId))
                    Introduce(session, other);
            }
            Gathering.Forget(session);
            Creatures.Forget(session);
            LandmarksOfLandmark.Forget(session);   // the client dropped them with the re-zone: sent again when near
            Pets.Reintroduce(session);
            // A full claims update replaces the client's list with the new landscape's build sites.
            Claims.SendAll(session);
            // The placed props within PropStreamRange (Props.Stream.cs); an arrival over props already sent the ones near
            // it at ClientIsReady (MiscHandler, Props.SendEarly): the rest now, then the stream tick follows the player.
            Props.StreamIn(session);
            Claims.EnsureStartingLocation(session);
            // The Spire's props after every re-zone: ClientBeginZoning deletes every entity but the local player, in
            // place or not (zone dispatcher FUN_142d1ccf0 case 8 → FUN_142d60340; trace/0695/agent-prop-collision.md)
            // [P]. Their collision is then re-run by Spire.ReassertCollision (Teleport.FinishRezone).
            if (!session.SpireSentEarly)
                Spire.Enter(session);
            session.SpireSentEarly = false;
            WarpNetwork.Enter(session);
            // The client never re-asks for the warp network after its first load: push the new landscape's.
            WarpNetwork.EnterLandscape(session);
        }

        /// <summary>
        /// A player moved to another landscape: players on the old one stop seeing them, players on the new one start
        /// (with their visible equipment). Their own view is rebuilt by <see cref="Reintroduce"/> after the re-zone.
        /// </summary>
        public static void ChangedZone(WorldSession session, uint oldZone)
        {
            WorldChannelServices.WorldChannel.TradePartyMoved(session, intoGame: false);
            Social.SocialServices.Service.PlayerChangedZone(session);
            // A separate gateway routes by landscape (ZoneChanged 0x27, gateway-service.md §3.3).
            GatewayLink.SendZoneChanged(session);
            lock (sync)
                foreach (WorldSession other in players.Where(p => p != session))
                {
                    if (other.ZoneId == oldZone)
                        other.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = session.PlayerGuid });
                    else if (other.ZoneId == session.ZoneId)
                        Introduce(other, session);
                }
        }

        /// <summary>Players on a landscape.</summary>
        public static List<WorldSession> InZone(uint zone)
        {
            lock (sync)
                return players.Where(p => p.ZoneId == zone).ToList();
        }

        /// <summary>Players on the home continent: those that get its terrain, sites, props, trees and creatures.</summary>
        public static List<WorldSession> OnHome()
        {
            lock (sync)
                return players.Where(p => p.OnHomeContinent).ToList();
        }

        /// <summary>
        /// A player leaves (disconnect or logout): drops their gathering/health/creature state, records when they were last
        /// online, removes them from everyone else's view and tells friends, group and guild. A session that never entered
        /// the world (dropped while loading, or turned away early) only has its per-session state forgotten
        /// (<see cref="ForgetNeverEntered"/>).
        /// </summary>
        public static void Leave(WorldSession session)
        {
            // Outside the lock, as the other modules' forgets after it below (their own locks).
            bool inWorld;
            lock (sync)
                inWorld = players.Contains(session);
            if (!inWorld)
            {
                ForgetNeverEntered(session);
                return;
            }
            lock (sync)
            {
                if (!players.Remove(session))
                    return;
                AdminCommands.Leave(session);
                Gathering.Forget(session);
                Props.Forget(session);
                InstancedGames.Forget(session);
                StoryTools.Forget(session);
                Health.Forget(session);
                MovementEffects.Forget(session);
                Afk.Forget(session);
                Emotes.Forget(session);
                SafeEject.Forget(session);
                Creatures.Forget(session);
                Chests.Forget(session);
                WorldChannelServices.WorldChannel.TradePartyLeft(session);
                WorldChannelServices.Commerce.ShowcaseSignOut(session);
                Vendors.Forget(session);
                session.MarkOnline();

                foreach (WorldSession other in players)
                    other.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = session.PlayerGuid });
                log.Info($"{session.CharacterName} left the world ({players.Count} online).");
            }
            Groups.Leave(session);
            Pets.Leave(session);
            Grief.Leave(session);
            Telemetry.Leave(session);
            WarpNetwork.Forget(session);
            Outposts.Forget(session);
            LandmarksOfLandmark.Forget(session);
            LootChests.Forget(session);
            Graveyards.Forget(session);
            DailyGoals.Forget(session);
            Combat.WeaponCombat.Forget(session);
            Vaults.Forget(session);
            Crafting.Forget(session);   // a craft still running is finished into the inventory, not lost
            PropLimits.Forget(session);
            // A build site placement still waiting for its confirm: Claims.pending held the session for good, Claims.Forget
            // was never called (bughunt-build B6).
            Claims.Forget(session);
            // A prop still on the cursor, or one put back on it (bughunt-build B7).
            Props.LeftWorld(session);
            WorldEditing.Forget(session);
            HeroicMovement.Forget(session);
            BuildModeHolster.Forget(session);
            // Out of chat rooms, offline to friends and guildmates (the social service's character-left event).
            Social.SocialServices.Service.PlayerLeft(session);
            WorldChannelServices.WorldChannel.EncounterLeave(session, "logout");
        }

        /// <summary>
        /// The per-session state of a session that is not (or no longer) in the world: SendSelf and the loading already
        /// create some (Grief's state at SendSelf, health and resources, streaming records), and a client that dropped before
        /// <see cref="Enter"/> kept it for good — Grief.Update walked the state every second and it held the whole session
        /// (review 2026-10-18 #7). Only forgets that are idempotent and tell nobody run here: no offline notices, group or
        /// encounter leave and no last-online time, as the player never came online. A session that already left runs them
        /// a second time (LinkClosed after PruneDead), which does nothing.
        /// </summary>
        private static void ForgetNeverEntered(WorldSession session)
        {
            AdminCommands.Leave(session);
            Gathering.Forget(session);
            Props.Forget(session);
            InstancedGames.Forget(session);
            StoryTools.Forget(session);
            Health.Forget(session);
            MovementEffects.Forget(session);
            Afk.Forget(session);
            Emotes.Forget(session);
            SafeEject.Forget(session);
            Creatures.Forget(session);
            Chests.Forget(session);
            Vendors.Forget(session);
            Pets.Leave(session);
            Grief.Leave(session);
            Telemetry.Leave(session);
            WarpNetwork.Forget(session);
            Outposts.Forget(session);
            LandmarksOfLandmark.Forget(session);
            LootChests.Forget(session);
            Graveyards.Forget(session);
            DailyGoals.Forget(session);
            Combat.WeaponCombat.Forget(session);
            Vaults.Forget(session);
            PropLimits.Forget(session);
            Claims.Forget(session);
            WorldEditing.Forget(session);
            HeroicMovement.Forget(session);
            BuildModeHolster.Forget(session);
        }

        /// <summary>
        /// Forwards a movement body from the PositionUpdate channel to everyone else.
        /// </summary>
        public static void RelayMovement(WorldSession session, byte[] movement)
        {
            lock (sync)
            {
                if (movement.Length == 0 || !players.Contains(session) || AdminCommands.IsHidden(session)
                        || loading.ContainsKey(session))
                    return;

                // Through the gateway's one-to-many list when the viewers are on the separate gateway (MovementFanout).
                // Not to a player still loading: it hasn't been shown this one yet.
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId
                        && !p.Disconnected && !loading.ContainsKey(p)))
                    MovementFanout.Queue(other, session.TransientId, movement);
            }
        }

        /// <summary>Tells the other players on the landscape where this player stands (after a teleport).</summary>
        public static void ShowPosition(WorldSession session)
        {
            if (AdminCommands.IsHidden(session))
                return;
            byte[] movement = Creatures.MovementBody(session.Position, 0f, System.Numerics.Vector3.Zero);
            lock (sync)
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId && !p.Disconnected
                        && !loading.ContainsKey(p) && !loading.ContainsKey(session)))
                    MovementFanout.Queue(other, session.TransientId, movement);
        }

        /// <summary>
        /// A copy of the online players, safe to iterate outside the lock.
        /// </summary>
        public static List<WorldSession> Snapshot()
        {
            lock (sync)
                return players.ToList();
        }

        /// <summary>
        /// The live session of a character, or null when they are offline or their connection has died.
        /// </summary>
        public static WorldSession FindByGuid(ulong guid)
        {
            lock (sync)
                return players.FirstOrDefault(p => p.PlayerGuid == guid && !p.Disconnected);
        }

        /// <summary>
        /// Sessions that time out are dropped by the network manager without OnDisconnect; they leave the world here
        /// (same cleanup and notifications as a normal exit).
        /// </summary>
        private static void PruneDead()
        {
            List<WorldSession> dead;
            lock (sync)
                dead = players.Where(p => p.Disconnected).ToList();
            foreach (WorldSession session in dead)
            {
                log.Info($"{session.CharacterName} timed out.");
                Leave(session);
            }
        }

        private static DateTime nextPrune;

        /// <summary>Server tick: checks for timed-out players once a second.</summary>
        public static void Update()
        {
            if (DateTime.UtcNow < nextPrune)
                return;
            nextPrune = DateTime.UtcNow.AddSeconds(1);
            PruneDead();
            ShowOverdue(DateTime.UtcNow);
        }
    }
}
