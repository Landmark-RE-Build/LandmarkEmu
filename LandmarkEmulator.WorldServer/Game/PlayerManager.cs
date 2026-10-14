using System;
using LandmarkEmulator.Shared.Network;
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
            // A character that reconnects before its old session times out replaces it. The old one gets the full leave
            // clean-up (open trades, chest windows, gathering state...), or e.g. a trade with the crashed client stays
            // open and the partner can't trade again. Done outside the lock: Leave also tells groups, friends, guilds.
            List<WorldSession> stale;
            lock (sync)
                stale = players.Where(p => p.PlayerGuid == session.PlayerGuid && p != session).ToList();
            foreach (WorldSession old in stale)
            {
                Leave(old);
                // A second login of the character while the first client is still connected: like live, the first
                // client is kicked with the client's own message box (PacketClientKickedFromServer 0x53 {string
                // StringFixed<128>}, Zone_DispatchPacket case 0x24: ErrorLabelWarning + KickedFromServerMessage with
                // [message]), then its connection is closed once the message has gone out.
                if (!old.Disconnected)
                {
                    var output = new List<byte>();
                    new GamePacketWriter(output).Write("Your character has logged in from another location.");
                    old.EnqueueMessage(ZoneMessageOpcode.ClientKickedFromServer, output.ToArray());
                    _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => { if (!old.Disconnected) old.OnDisconnect(); });
                    log.Info($"{old.CharacterName}: an older session was kicked (logged in again).");
                }
            }

            lock (sync)
            {
                if (players.Contains(session))
                    return;

                PruneDead();

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
                Props.SendAll(session);
                Claims.EnsureStartingLocation(session);
                if (session.OnHomeContinent)
                {
                    Spire.Enter(session);
                    WarpNetwork.Enter(session);
                }
                log.Info($"{session.CharacterName} entered the world ({players.Count} online).");
            }
            Friends.Enter(session);
            Ignores.Enter(session);
            // Player Studio: every player is a registered member (free; live required signing up on a web page).
            session.EnqueueMessage(ZoneMessageOpcode.PlayerStudioRegistered, new byte[] { 1 });
            Guilds.Enter(session);
            Store.SignIn(session);
            Admin.Enter(session);
            SendMotd(session);
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
            viewer.EnqueueMessage(subject.BuildAddLightweightPc());
            foreach ((uint slot, var item) in subject.VisibleEquipment())
                viewer.EnqueueMessage(subject.BuildEquipmentSlot(slot, item));
            if (Titles.Shows(subject))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayerTitleWire, Titles.UpdatePacket(subject));
            Health.IntroduceDead(viewer, subject);
        }

        /// <summary>
        /// After a re-zone (<see cref="Teleport.Rezone"/>) the client has dropped every entity but its own character
        /// (FUN_142ee4930 clears the entity tables on ClientBeginZoning). Sends back what zone-in sent: the other
        /// players and their visible equipment, placed props and the Spire; trees, plants and creatures are forgotten so
        /// their streaming sends the regions around the new position again. Nothing is sent to the other players: their
        /// view of this player is unchanged.
        /// </summary>
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

        public static void Reintroduce(WorldSession session)
        {
            lock (sync)
            {
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId))
                    Introduce(session, other);
            }
            Gathering.Forget(session);
            Creatures.Forget(session);
            // A full claims update replaces the client's list with the new landscape's build sites.
            Claims.SendAll(session);
            Props.SendAll(session);
            Claims.EnsureStartingLocation(session);
            if (session.OnHomeContinent)
            {
                Spire.Enter(session);
                WarpNetwork.Enter(session);
            }
            // The client never re-asks for the warp network after its first load: push the new landscape's.
            WarpNetwork.EnterLandscape(session);
        }

        /// <summary>
        /// A player moved to another landscape: players on the old one stop seeing them, players on the new one start
        /// (with their visible equipment). Their own view is rebuilt by <see cref="Reintroduce"/> after the re-zone.
        /// </summary>
        public static void ChangedZone(WorldSession session, uint oldZone)
        {
            Trades.Moved(session, intoGame: false);
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
        /// online, removes them from everyone else's view and tells friends, group and guild.
        /// </summary>
        public static void Leave(WorldSession session)
        {
            lock (sync)
            {
                if (!players.Remove(session))
                    return;
                Gathering.Forget(session);
                InstancedGames.Forget(session);
                StoryTools.Forget(session);
                Health.Forget(session);
                Creatures.Forget(session);
                Chests.Forget(session);
                Trades.Forget(session);
                Store.Forget(session);
                Vendors.Forget(session);
                session.MarkOnline();

                foreach (WorldSession other in players)
                    other.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = session.PlayerGuid });
                log.Info($"{session.CharacterName} left the world ({players.Count} online).");
            }
            Groups.Leave(session);
            ChatRooms.Forget(session);
            WarpNetwork.Forget(session);
            Outposts.Forget(session);
            LootChests.Forget(session);
            DailyGoals.Forget(session);
            Combat.WeaponCombat.Forget(session);
            Vaults.Forget(session);
            PropLimits.Forget(session);
            WorldEditing.Forget(session);
            BuildModeHolster.Forget(session);
            Friends.Leave(session);
            Ignores.Leave(session);
            Guilds.Leave(session);
            Encounters.Leave(session, "logout");
        }

        /// <summary>
        /// Relays a HeroicMovement packet (opcode 0x105 bytes, u8 sub, u64 entity, body) to the other players on the
        /// landscape within <see cref="HeroicRelayRange"/>, with the entity set to the sender.
        /// </summary>
        public static void RelayHeroicMovement(WorldSession session, byte[] raw)
        {
            byte[] packet = (byte[])raw.Clone();
            BitConverter.GetBytes(session.PlayerGuid).CopyTo(packet, 3);
            List<WorldSession> others;
            lock (sync)
                others = players.Where(p => p != session && p.ZoneId == session.ZoneId
                    && System.Numerics.Vector3.Distance(p.Position, session.Position) <= HeroicRelayRange).ToList();
            foreach (WorldSession other in others)
                other.SendRaw(packet);
        }

        /// <summary>Range (m) within which others see a player's grappling hook and swimming.</summary>
        private const float HeroicRelayRange = 200f;

        /// <summary>
        /// Forwards a movement body from the PositionUpdate channel to everyone else.
        /// </summary>
        public static void RelayMovement(WorldSession session, byte[] movement)
        {
            lock (sync)
            {
                if (movement.Length == 0 || !players.Contains(session))
                    return;

                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId
                        && !p.Disconnected && !p.Heartbeat.Flatline))
                    other.QueueMovement(session.TransientId, movement);
            }
        }

        /// <summary>Tells the other players on the landscape where this player stands (after a teleport).</summary>
        public static void ShowPosition(WorldSession session)
        {
            byte[] movement = Creatures.MovementBody(session.Position, 0f, System.Numerics.Vector3.Zero);
            lock (sync)
                foreach (WorldSession other in players.Where(p => p != session && p.ZoneId == session.ZoneId && !p.Disconnected))
                    other.QueueMovement(session.TransientId, movement);
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
                return players.FirstOrDefault(p => p.PlayerGuid == guid && !p.Disconnected && !p.Heartbeat.Flatline);
        }

        /// <summary>
        /// Sessions that time out are dropped by the network manager without OnDisconnect; they leave the world here
        /// (same cleanup and notifications as a normal exit).
        /// </summary>
        private static void PruneDead()
        {
            List<WorldSession> dead;
            lock (sync)
                dead = players.Where(p => p.Disconnected || p.Heartbeat.Flatline).ToList();
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
        }
    }
}
