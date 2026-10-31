using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The admin slash commands of the unmodified retail client. They are unlocked by the admin-player flag the server
    /// sends in SendSelfToClient (<c>IsAdminMaybe</c>, player +0x7b0d8 in 0.695; set for WorldServer.json "Admins",
    /// <see cref="Admin.IsAdmin"/>) and arrive as subs of the Command family (0x1E, u16 sub) — live's zone server
    /// ("ZoneCommand" handler group, the packet class names' PTR_s_ZoneCommand) answered them. Every sub is gated here
    /// again with <see cref="Admin.IsAdmin"/>: a flagged client is the only one that sends them, but a modified client
    /// could, so a non-admin's command is refused and logged.
    /// Layouts come from the 0.695 client's packet writers (trace/0695/admin-commands-695.md has each one with its
    /// function; [P] proven from the writer, [I] inferred). The common writer prefix is
    /// FUN_142c12ce0: u16 opcode 0x1E, u16 sub; strings are u32 length + UTF-8 bytes (FUN_142d4b510); u64 guids are
    /// written whole (FUN_142c02460).
    /// </summary>
    public static partial class AdminCommands
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // Command subs (0.695 = 0.589; trace/0695/coverage-695.md rows).
        public const ushort ProfileSub = 0x3EC;        // AdminCommandPacketProfile* (u16 profile sub inside)
        public const ushort GameTimeSub = 0x429;       // AdminCommandPacketGameTime
        public const ushort ClientTimeSub = 0x42A;     // AdminCommandPacketClientTime
        public const ushort GotoSub = 0x42E;           // AdminCommandPacketGotoRequest
        public const ushort GotoWaypointSub = 0x430;   // AdminCommandPacketGotoWaypointRequest
        public const ushort HideMeSub = 0x458;         // AdminCommandPacketHideMe
        public const ushort AddAmmoSub = 0x499;        // AdminCommandPacketAddAmmo
        public const ushort SpawnVehicleSub = 0x49C;   // AdminCommandPacketSpawnVehicle
        public const ushort VerifyTargetSub = 0x4B5;   // AdminPacketVerifyAdminTarget (both ways)
        public const ushort ForceAddFriendSub = 0x4CF; // AdminCommandPacketForceAddFriendRequest
        public const ushort ForceRemoveFriendSub = 0x4D0; // AdminCommandPacketForceRemoveFriendRequest
        public const ushort RunspeedSub = 0x4D8;       // AdminCommandPacketRunspeed
        public const ushort SpawnPropSub = 0x4EA;      // AdminCommandPacketSpawnProp
        public const ushort DeleteAllPropsSub = 0x4ED; // AdminCommandPacketDeleteAllProps
        public const ushort WeaponSetSub = 0x4EE;      // AdminCommandPacketWeaponSet
        public const ushort PlacedEffectSub = 0x4EF;   // AdminCommandPacketPlacedEffect
        public const ushort SetModelSub = 0x4F0;       // AdminCommandPacketSetModel
        public const ushort FreeFlyHideSub = 0x4F1;    // AdminCommandPacketFreeFlyHide

        /// <summary>Is this Command sub one of the admin commands handled here?</summary>
        public static bool IsAdminSub(ushort sub) => sub switch
        {
            ProfileSub or GameTimeSub or ClientTimeSub or GotoSub or GotoWaypointSub or HideMeSub or AddAmmoSub
                or SpawnVehicleSub or VerifyTargetSub or ForceAddFriendSub or ForceRemoveFriendSub or RunspeedSub
                or SpawnPropSub or DeleteAllPropsSub or WeaponSetSub or PlacedEffectSub or SetModelSub or FreeFlyHideSub => true,
            _ => IsItemSub(sub) || IsMonitorSub(sub),
        };

        /// <summary>
        /// Dispatches an admin Command sub. The gate: only a player in WorldServer.json "Admins" may run any of them;
        /// anyone else is logged (the retail client never sends these without the flag).
        /// </summary>
        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            if (!Admin.IsAdmin(session))
            {
                log.Warn($"{session.CharacterName} sent admin command 0x{sub:X} without being an admin (refused): {BitConverter.ToString(body)}");
                return;
            }
            try
            {
                switch (sub)
                {
                    case GotoSub: Goto(session, body); break;
                    case GotoWaypointSub: GotoWaypoint(session, body); break;
                    case VerifyTargetSub: VerifyTarget(session, body); break;
                    case HideMeSub: HideMe(session, body); break;
                    case RunspeedSub: Runspeed(session, body); break;
                    case GameTimeSub: GameTime(session, body); break;
                    case ClientTimeSub: ClientTime(session, body); break;
                    case SpawnPropSub: SpawnProp(session, body); break;
                    case DeleteAllPropsSub: DeleteAllProps(session, body); break;
                    case SpawnVehicleSub: SpawnVehicle(session, body); break;
                    case SetModelSub: SetModel(session, body); break;
                    case AddAmmoSub: AddAmmo(session, body); break;
                    case WeaponSetSub: WeaponSet(session, body); break;
                    case PlacedEffectSub: PlacedEffect(session, body); break;
                    case FreeFlyHideSub: FreeFlyHide(session, body); break;
                    case ForceAddFriendSub: ForceAddFriend(session, body); break;
                    case ForceRemoveFriendSub: ForceRemoveFriend(session, body); break;
                    case ProfileSub: Profile(session, body); break;
                    default:
                        if (IsItemSub(sub))
                            HandleItem(session, sub, body);
                        else if (IsMonitorSub(sub))
                            HandleMonitor(session, sub, body);
                        break;
                }
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Admin command 0x{sub:X} from {session.CharacterName} failed: {BitConverter.ToString(body)}");
                Chat.System(session, $"Admin command 0x{sub:X} failed on the server (see the server log).");
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Target resolution shared by the commands

        /// <summary>
        /// The client's "no guid" value (DAT_145462af8, the invalid-guid global): the commands put it in the guid field
        /// when the admin typed a name or had no admin target. The global lies in .bss (past the raw size 0x21fa00 of
        /// .data) and has 123 RIP-relative reads / compares but no write, LEA or pointer anywhere in the 0.695 exe
        /// (agent-leads-sweep.md L105, ripref.py + refscan.py), so its value is 0 [P]. All-ones is kept as a defensive
        /// alias only (no client sends it).
        /// </summary>
        private static bool NoGuid(ulong guid) => guid == 0 || guid == ulong.MaxValue;

        /// <summary>
        /// An online player by guid, else by name: exact (case-insensitive) first, then a unique prefix, then a unique
        /// substring ("/goto ... Accepts partial strings", the client's usage text).
        /// </summary>
        private static WorldSession FindOnline(ulong guid, string name)
        {
            if (!NoGuid(guid) && PlayerManager.FindByGuid(guid) is WorldSession byGuid)
                return byGuid;
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return null;
            List<WorldSession> online = PlayerManager.Snapshot().Where(p => !p.Disconnected && p.CharacterName != null).ToList();
            WorldSession exact = online.FirstOrDefault(p => string.Equals(p.CharacterName, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            var prefix = online.Where(p => p.CharacterName.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (prefix.Count == 1)
                return prefix[0];
            var contains = online.Where(p => p.CharacterName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            return contains.Count == 1 ? contains[0] : null;
        }

        /// <summary>A character (online or not) by guid or exact name, from the character database.</summary>
        private static CharacterModel FindCharacter(ulong guid, string name)
        {
            var database = DatabaseManager.Instance.CharacterDatabase;
            CharacterModel character = null;
            if (!NoGuid(guid))
                character = database.GetCharacterById(guid).GetAwaiter().GetResult();
            if (character == null && !string.IsNullOrWhiteSpace(name))
                character = database.FindCharacterByName(name.Trim());
            return character?.DeleteTime == null ? character : null;
        }

        /// <summary>A Command-family packet to the client: u16 sub, then the body.</summary>
        private static void Send(WorldSession session, ushort sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(sub);
            body(writer);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
        }

        // ------------------------------------------------------------------------------------------------------------
        // 1. /goto, /warp

        /// <summary>
        /// /goto [player|npc] &lt;name/guid&gt; — AdminCommandPacketGotoRequest 0x1E / 0x42E, sent by the client's
        /// command function FUN_1433a7d70 through the writer FUN_14338a3c0:
        /// u64 guid (+0x20: the typed guid, else the admin target, else the invalid guid) [P], string name (+0x28,
        /// StringFixed&lt;64&gt;: the typed name, empty when a guid was given) [P], u64 (+0x88, never assigned by the
        /// command: 0 / garbage, ignored) [P]. The client refuses to send it while mounted ("You must exit your vehicle
        /// to use the /goto command."). The server moves the admin next to the player, landscape change included
        /// (<see cref="Teleport.ToZone"/>, the same path as dev "tpto").
        /// </summary>
        public static void Goto(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong guid = reader.ReadULong();
            string name = reader.ReadString();
            WorldSession target = FindOnline(guid, name);
            if (target == null)
            {
                Chat.System(session, $"/goto: no online player matches \"{name}\"{(NoGuid(guid) ? "" : $" / guid {guid}")}.");
                log.Info($"{session.CharacterName} /goto {name} ({guid}): not found online.");
                return;
            }
            if (target == session)
            {
                Chat.System(session, "/goto: that is you.");
                return;
            }
            Vector3 spot = target.Position + new Vector3(1f, 0f, 0f);
            Teleport.ToZone(session, spot, target.ZoneId);
            Chat.System(session, $"Warping to {target.CharacterName} (landscape {target.ZoneId}, {spot.X:0.#}, {spot.Y:0.#}, {spot.Z:0.#}).");
            log.Info($"Admin {session.CharacterName} /goto {target.CharacterName} on {target.ZoneId} at {spot}.");
        }

        /// <summary>
        /// /goto waypoint [name/guid] (also "way") — AdminCommandPacketGotoWaypointRequest 0x1E / 0x430 (ctor
        /// FUN_1433960f0, writer FUN_14338a580): u64 guid (+0x20: the admin's own guid when no target was typed, else the
        /// typed guid / admin target) [P], string name (+0x28, StringFixed&lt;64&gt;) [P]. Live warped the admin to that
        /// player's map waypoint. The server keeps no player waypoints yet (the map waypoint is placed client-side and
        /// no packet carrying it has been traced: OPEN-LEADS L104), so this is logged and answered with a chat line.
        /// </summary>
        public static void GotoWaypoint(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong guid = reader.ReadULong();
            string name = reader.ReadString();
            log.Info($"Admin {session.CharacterName} /goto waypoint guid {guid} name \"{name}\": waypoints are not kept on the server yet (L104).");
            Chat.System(session, "/goto waypoint: the server does not know player waypoints yet.");
        }

        // ------------------------------------------------------------------------------------------------------------
        // 2. /target, /gm target

        /// <summary>
        /// /target &lt;name/guid&gt; (and /gm target) — AdminPacketVerifyAdminTarget 0x1E / 0x4B5. The client
        /// (FUN_1433a7420) first looks for the character among the entities it can see (FUN_142f853f0 by name,
        /// FUN_142cd1cb0 by guid) and sets the admin target itself; only when it is not in view it asks the server with
        /// the writer FUN_14338c780: u64 guid (+0x20: the parsed guid, else the invalid guid) [P], string name (+0x28,
        /// StringFixed&lt;32&gt;) [P]. The reply is the same sub, read by FUN_1433919c0: u16 0x1E, u16 0x4B5, u64 guid,
        /// string (i32 length + bytes) [P]; the handler FUN_1433bf9b0 (admin-gated) sets the admin target to the guid
        /// and prints "Admin Target set to Guid: %llu" — the string is read and not used. The admin target then feeds
        /// /goto, /ammo add, /profile ... (their "Admin Target Compatible" usage text).
        /// Any character counts, online or not (the character database), as live's target was a guid.
        /// "/target clear" is client-side ("Admin Target Cleared.").
        /// </summary>
        public static void VerifyTarget(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong guid = reader.ReadULong();
            string name = reader.ReadString();
            CharacterModel character = FindCharacter(guid, name);
            if (character == null)
            {
                // The client's own text for a miss is "Could not locate target." (local lookups); no reply means the
                // target stays as it was.
                Chat.System(session, $"Could not locate target \"{name}\".");
                log.Info($"Admin {session.CharacterName} /target {name} ({guid}): no such character.");
                return;
            }
            Send(session, VerifyTargetSub, w =>
            {
                w.Write(character.Id);
                w.Write(character.Name ?? "");
            });
            log.Info($"Admin {session.CharacterName} /target {character.Name} = guid {character.Id}.");
        }

        // ------------------------------------------------------------------------------------------------------------
        // 3. /hideme

        /// <summary>Admins hidden from the other players (guids); checked by <see cref="PlayerManager"/>.</summary>
        private static readonly HashSet<ulong> hidden = new();

        /// <summary>Is this player hidden from everyone else (/hideme)?</summary>
        public static bool IsHidden(WorldSession session)
        {
            lock (hidden)
                return session != null && hidden.Contains(session.PlayerGuid);
        }

        /// <summary>
        /// /hideme [on|off] — AdminCommandPacketHideMe 0x1E / 0x458 (command FUN_1433ac520, writer FUN_14338a730):
        /// u8 hide (+0x20) [P]. Without an argument the client toggles from its own entity's visibility bit (bit 31 of
        /// the u32 at entity +0xa20: visible → 1 = hide). The server takes the admin out of everyone else's view
        /// (PlayerUpdateRemovePlayer, as a landscape change does) and keeps them out while hidden: no introduction to
        /// players arriving later, no movement fan-out; unhiding introduces them again (<see cref="PlayerManager.Reshow"/>).
        /// The admin's own client keeps its character.
        /// </summary>
        public static void HideMe(WorldSession session, byte[] body)
        {
            bool hide = body.Length >= 1 && body[0] != 0;
            SetHidden(session, hide);
            Chat.System(session, hide ? "You are now hidden from other players." : "You are visible to other players again.");
            log.Info($"Admin {session.CharacterName} /hideme {(hide ? "on" : "off")}.");
        }

        private static void SetHidden(WorldSession session, bool hide)
        {
            lock (hidden)
            {
                if (hide)
                    hidden.Add(session.PlayerGuid);
                else
                    hidden.Remove(session.PlayerGuid);
            }
            // Reshow removes the player from everyone on the landscape and introduces them again — the introduction is
            // skipped while hidden, so the same call hides and unhides.
            PlayerManager.Reshow(session);
        }

        /// <summary>A hidden admin that logs out is forgotten (the flag is per session, not saved).</summary>
        public static void Leave(WorldSession session)
        {
            lock (hidden)
                hidden.Remove(session.PlayerGuid);
            lock (runSpeeds)
                runSpeeds.Remove(session.PlayerGuid);
        }

        // ------------------------------------------------------------------------------------------------------------
        // 4. /runspeed

        /// <summary>Admin run speeds (m/s) that replace the movement-speed stat (cStatTypeMovementSpeed 5).</summary>
        private static readonly Dictionary<ulong, float> runSpeeds = new();

        /// <summary>The admin's chosen run speed, or null for the normal stat (<see cref="Abilities.CalculatedStats"/>).</summary>
        public static float? RunSpeedOf(WorldSession session)
        {
            lock (runSpeeds)
                return session != null && runSpeeds.TryGetValue(session.PlayerGuid, out float speed) ? speed : null;
        }

        /// <summary>
        /// /runspeed inc|dec &lt;n&gt; | default | &lt;speed&gt; — AdminCommandPacketRunspeed 0x1E / 0x4D8 (command
        /// FUN_1433b2a20, writer FUN_14338ba60): f32 speed (+0x20) [P], the run speed in metres per second the client
        /// worked out ("run speed is %.2f meters/sec": the controller's current speed, vfunc +0xC0). What the command
        /// sends (agent-leads-sweep.md L105) [P]:
        /// <list type="bullet">
        /// <item>"default" sends <b>0.0</b> (LAB_1433b2ec5 sets both locals to 0) — the client's own "reset" value;</item>
        /// <item>"inc n" sends the current speed + n and "dec n" the current speed − n, both floored at DAT_144c410f0 =
        /// f32 1.0 (.rdata bytes 00 00 80 3F);</item>
        /// <item>an absolute n below 0 is sent as 0, so a typed "/runspeed 0" also means default.</item>
        /// </list>
        /// The client prints "Player run speed set to %.3f" itself and keeps the value (DAT_145439f60, 0 = no override;
        /// offline it prints "Player run speed reset to default." for 0). So the server treats 0 (anything under 0.001) as
        /// <b>remove the override</b>, as does the normal speed itself; before this, 0 was stored as a 0 m/s run speed and pinned the admin in place. Any other speed is
        /// applied as the calculated movement-speed stat (5; the client's default is 12, DefaultCalculatedStats) and the
        /// stats are re-sent [I: the stat is what the server has for run speed; live's zone server may have changed the
        /// controller directly]. The client can also read this sub back (FUN_143391910, same layout) — not sent.
        /// </summary>
        public static void Runspeed(WorldSession session, byte[] body)
        {
            float speed = new GamePacketReader(body).ReadSingle();
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 1000f)
            {
                Chat.System(session, $"/runspeed: {speed} is out of range.");
                return;
            }
            float normal = DefaultCalculatedStats.Player.FirstOrDefault(s => s.StatId == 5).Value;
            bool reset = speed < 0.001f || Math.Abs(speed - normal) < 0.001f;
            lock (runSpeeds)
            {
                if (reset)
                    runSpeeds.Remove(session.PlayerGuid);
                else
                    runSpeeds[session.PlayerGuid] = speed;
            }
            session.SendCalculatedStats();
            if (speed < 0.001f)
                Chat.System(session, $"Run speed reset to default ({normal:0.##} m/s).");
            log.Info($"Admin {session.CharacterName} /runspeed {(reset ? "default" : speed.ToString("0.###"))} (stat 5; normal {normal}).");
        }

        // ------------------------------------------------------------------------------------------------------------
        // 5. /gametime, /servertime, /zonetime, /clienttime

        /// <summary>
        /// /servertime (FUN_1433b3d00, tunnel channel 1 = world) and /zonetime (FUN_1433b7350, channel 0 = zone):
        /// hour &lt;h&gt; | pause | sync | warp &lt;n&gt; — AdminCommandPacketGameTime 0x1E / 0x429, writer
        /// FUN_14338a220 → FUN_14337f8d0: u8 +0x20, u8 +0x21, u8 +0x22, u8 +0x23, u8 +0x24, f32 +0x28 [P]. The
        /// command fills them as: hour → {0, 0, hour, 0xFF, 0xFF, 0}; pause → {1, 0, 0xFF, 0xFF, 0xFF, 0};
        /// sync → {0, 1, 0xFF, 0xFF, 0xFF, 0}; warp → {0, 0, 0xFF, 0xFF, 0xFF, n} [P]. So: +0x20 = toggle pause,
        /// +0x21 = resync, +0x22 = hour (0xFF none; /servertime checks 0-24), +0x23 / +0x24 = 0xFF (never set by the
        /// command: minute / second by position [I]), +0x28 = game seconds per real second (0 none). Both commands go
        /// to the same world clock here; everyone online is re-synced (GameTimeSync) after a change.
        /// </summary>
        public static void GameTime(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            byte pause = reader.ReadByte(), sync = reader.ReadByte(), hour = reader.ReadByte();
            byte minute = reader.ReadByte(), second = reader.ReadByte();
            uint warpBits = reader.ReadUInt();
            float warp = BitConverter.Int32BitsToSingle((int)warpBits);
            string what;
            if (pause != 0)
            {
                WorldClock.SetPaused(!WorldClock.Paused);
                what = WorldClock.Paused ? "paused" : "resumed";
            }
            else if (sync != 0)
                what = "re-synced";
            else if (hour != 0xFF)
            {
                double h = hour + (minute != 0xFF ? minute / 60.0 : 0) + (second != 0xFF ? second / 3600.0 : 0);
                WorldClock.SetHour(h % 24);
                what = $"set to {hour:00}:{(minute != 0xFF ? minute : 0):00}";
            }
            else if (warp != 0f && !float.IsNaN(warp) && !float.IsInfinity(warp))
            {
                WorldClock.SetRate(warp);
                what = $"warp {WorldClock.Rate:0.##} game s per real s";
            }
            else
            {
                log.Info($"Admin {session.CharacterName} /gametime with no change: {BitConverter.ToString(body)}");
                return;
            }
            SyncEveryone();
            Chat.System(session, $"World time {what}: now {WorldClock.SecondsOfDay / 3600:0.00} h{(WorldClock.Paused ? " (paused)" : "")}.");
            log.Info($"Admin {session.CharacterName} /gametime: {what}; clock {WorldClock.SecondsOfDay / 3600:F2} h, rate {WorldClock.CycleSpeed}.");
        }

        /// <summary>Sends the world clock to everyone online (the GameTimeSync the client asks for periodically).</summary>
        private static void SyncEveryone()
        {
            foreach (WorldSession player in PlayerManager.Snapshot().Where(p => !p.Disconnected))
                player.EnqueueMessage(new GameTimeSync
                {
                    Time = WorldClock.SyncTime(Network.Handlers.MiscHandler.LastClientTime),
                    CycleSpeed = WorldClock.CycleSpeed,
                    Paused = false,
                });
        }

        /// <summary>
        /// /clienttime set|hour|pause|sync|warp|advance|reverse — AdminCommandPacketClientTime 0x1E / 0x42A (command
        /// FUN_1433b55e0, writer FUN_1433897f0 → FUN_14337f650): u32 mode (+0x20), field a (+0x24), b (+0x28), c (+0x2C)
        /// [P]. The client reads the same sub back (FUN_143381ee0 → handler FUN_1433ba260, admin-gated by the
        /// admin-player flag +0x7b0d8, mode − 1 &lt; 7) and applies it to its own clock, ClientGameTimeManager
        /// DAT_1454319a0 (constructor FUN_1431da650): the 7-entry jump table at 0x1433ba644 (cases 0x1433ba349, 36b,
        /// 3dd, 3f9, 44f, 48d, 531; agent-leads-sweep.md L106) [P]:
        /// <list type="table">
        /// <item><term>1 set</term><description>SetTime(a, b, c, false) — integers h, m, s (vtable +0x28)</description></item>
        /// <item><term>2 hour</term><description>f32 a hours → h = int(a) % 24, m = int(a·60) % 60, s = int(a·3600) % 60</description></item>
        /// <item><term>3 pause</term><description>toggles paused (+0xc0)</description></item>
        /// <item><term>4 sync</term><description>clears the warp flag (+0xc1) and paused, rate (+0xb4) = 8.0</description></item>
        /// <item><term>5 warp</term><description>if f32 a ≠ 0: warp flag on, rate = a game s per real s</description></item>
        /// <item><term>6 advance</term><description>now + a·3600 s (f32 a hours; constant 3600.0 at 0x144722210)</description></item>
        /// <item><term>7 reverse</term><description>now − a·3600 s, mod 86400</description></item>
        /// </list>
        /// So the command changes only the admin's own client clock, and live's zone server answered by echoing the
        /// packet to the sender [I: the client has a reader and handler for the sub and no other source of it]. The
        /// server echoes valid modes unchanged; it refuses mode 0 / &gt; 7 and NaN / infinite floats (the client would
        /// drop the first and corrupt its clock with the second). The client's default rate 8.0 (DAT_144722208, a
        /// double; a setting with hash −0x7e31ddd1 could override it) is the server's
        /// <see cref="WorldClock.DefaultCycleSpeed"/>. The next GameTimeSync (handler FUN_1431dace0: sets the time, rate
        /// +0xb4 and paused unconditionally) puts the client back on the world clock, so a /clienttime change lasts until
        /// then. For "sync" the server sends that GameTimeSync right after the echo, so the client rejoins the world
        /// clock at once, not just its default rate [I: the name; the client's own case only resets the rate].
        /// </summary>
        public static void ClientTime(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            uint mode = reader.ReadUInt(), a = reader.ReadUInt(), b = reader.ReadUInt(), c = reader.ReadUInt();
            float fa = BitConverter.Int32BitsToSingle((int)a);
            string what;
            switch (mode)
            {
                case 1: what = $"set to {a:00}:{b:00}:{c:00}"; break;
                case 2: what = $"hour {fa:0.##}"; break;
                case 3: what = "pause toggled"; break;
                case 4: what = "synced to the world clock"; break;
                case 5: what = fa != 0f ? $"warp {fa:0.##} game s per real s" : "warp 0 (ignored by the client)"; break;
                case 6: what = $"advanced {fa:0.##} h"; break;
                case 7: what = $"reversed {fa:0.##} h"; break;
                default:
                    Chat.System(session, $"/clienttime: mode {mode} is not one the client knows (1-7).");
                    log.Info($"Admin {session.CharacterName} /clienttime mode {mode}: refused.");
                    return;
            }
            if ((mode is 2 or 5 or 6 or 7) && (float.IsNaN(fa) || float.IsInfinity(fa)))
            {
                Chat.System(session, "/clienttime: the value is not a number.");
                return;
            }
            Send(session, ClientTimeSub, w => { w.Write(mode); w.Write(a); w.Write(b); w.Write(c); });
            if (mode == 4)
                session.EnqueueMessage(new GameTimeSync
                {
                    Time = WorldClock.SyncTime(Network.Handlers.MiscHandler.LastClientTime),
                    CycleSpeed = WorldClock.CycleSpeed,
                    Paused = false,
                });
            Chat.System(session, $"Client time {what} (your client only; the next time sync restores the world clock).");
            log.Info($"Admin {session.CharacterName} /clienttime {what} ({a:X8} {b:X8} {c:X8}): echoed to the client.");
        }

        // ------------------------------------------------------------------------------------------------------------
        // 6. /prop

        /// <summary>
        /// /prop &lt;prop id&gt; — AdminCommandPacketSpawnProp 0x1E / 0x4EA (command FUN_1433b1e50, writer
        /// FUN_14338c210, element writer FUN_143380290): u32 count, then per prop {u32 propId, f32 x, f32 y, f32 z,
        /// vec4 rotation (quaternion from the camera yaw, FUN_1433b88f0), vec4 scale (the client's unit vector
        /// constant)} [P]. The client finds the ground under its look-at point first (FUN_142b7e470 ray cast:
        /// "Failed to find ground to spawn Prop.") and prints "Trying to summon prop with prop ID %d". The server puts a
        /// free prop there (<see cref="Props.PlaceAdmin"/>) owned by the admin, persisted, shown to the landscape.
        /// </summary>
        public static void SpawnProp(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            uint count = reader.ReadUInt();
            if (count > 64)
            {
                Chat.System(session, $"/prop: {count} props in one command is too many.");
                return;
            }
            for (uint i = 0; i < count; i++)
            {
                uint propId = reader.ReadUInt();
                var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var rotation = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var scale = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                ulong guid = Props.PlaceAdmin(session, propId, position, rotation, scale);
                if (guid == 0)
                    Chat.System(session, $"/prop: prop {propId} has no model (not placed).");
                else
                    Chat.System(session, $"Prop {propId} placed as 0x{guid:X} at {position.X:0.#}, {position.Y:0.#}, {position.Z:0.#}.");
                log.Info($"Admin {session.CharacterName} /prop {propId} at {position} rot {rotation} scale {scale}: guid 0x{guid:X}.");
            }
        }

        /// <summary>
        /// /prop reset_zone_and_wipe_db_forever_with_no_undo — AdminCommandPacketDeleteAllProps 0x1E / 0x4ED (writer
        /// FUN_143389990): header only, no body [P]. Live wiped every prop of the zone. Refused here: logged with who
        /// tried, nothing deleted (a dev-only wipe stays a server-side operation).
        /// </summary>
        public static void DeleteAllProps(WorldSession session, byte[] body)
        {
            log.Warn($"Admin {session.CharacterName} asked to delete ALL props on landscape {session.ZoneId} (0x4ED): refused.");
            Chat.System(session, "Deleting all props is disabled on this server.");
        }

        // ------------------------------------------------------------------------------------------------------------
        // 7. Decoded and logged: no matching server feature yet (or the semantics are not proven)

        /// <summary>
        /// /vehicle &lt;vehicle id&gt; [auto mount] [faction id] — AdminCommandPacketSpawnVehicle 0x1E / 0x49C (command
        /// FUN_1433b6030, writer FUN_14338c420 → FUN_143380400): u32 vehicleId (+0x20), u8 factionId (+0x3C; the
        /// command accepts 1-3), f32 x, f32 y, f32 z (+0x24..: the ground under the look-at point), f32 yaw (+0x30,
        /// camera yaw in radians), f32 (+0x34, 0), bool autoMount (+0x38: "1" / "t" / "T") [P]. Vehicles are not
        /// built on the server (no vehicle module yet: OPEN-LEADS L107), so this is logged with its fields.
        /// </summary>
        public static void SpawnVehicle(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            uint vehicleId = reader.ReadUInt();
            byte faction = reader.ReadByte();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float yaw = reader.ReadSingle(), unused = reader.ReadSingle();
            bool autoMount = reader.BytesRemaining >= 1 && reader.ReadBool();
            log.Info($"Admin {session.CharacterName} /vehicle {vehicleId} faction {faction} at {position} yaw {yaw:0.##} ({unused}) autoMount {autoMount}: vehicles are not built yet (L107).");
            Chat.System(session, $"/vehicle {vehicleId}: vehicles are not on this server yet.");
        }

        /// <summary>
        /// /setmodel &lt;model id&gt; — AdminCommandPacketSetModel 0x1E / 0x4F0 (command FUN_1433b4110, writer
        /// FUN_14338bde0): u64 targetGuid (+0x20: the admin target, else the invalid guid), u32 modelId (+0x28) [P];
        /// the client prints "Processing set model model_id=[%d]". The target (the admin when no admin target is set) is
        /// shown as that Models.txt model to everyone on the landscape: PlayerUpdate UpdateTemporaryAppearance 0x26 / 9,
        /// the only client path into ProxiedCharacter::SwitchToTemporaryAppearance (<see cref="TemporaryAppearance"/>).
        /// <c>/setmodel 0</c> brings the own body back (RemoveTemporaryAppearance 0x26 / 0xA). Until 2026-10-26 logged only.
        /// </summary>
        public static void SetModel(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong target = reader.ReadULong();
            uint modelId = reader.ReadUInt();
            WorldSession who = NoGuid(target) ? session : PlayerManager.FindByGuid(target);
            if (who == null)
            {
                log.Info($"Admin {session.CharacterName} /setmodel {modelId} for {target}: no such player online.");
                Chat.System(session, $"/setmodel {modelId}: that player is not online.");
                return;
            }
            log.Info($"Admin {session.CharacterName} /setmodel {modelId} for {who.CharacterName}.");
            TemporaryAppearance.Set(who, modelId);
            Chat.System(session, modelId == 0
                ? $"/setmodel: {who.CharacterName} has their own body again."
                : $"/setmodel {modelId}: {who.CharacterName} is shown as model {modelId}.");
        }

        /// <summary>
        /// /ammo add &lt;count&gt; [ammo slot] [equipment slot] | addto &lt;name/guid&gt; &lt;count&gt; [slot] [slot] —
        /// AdminCommandPacketAddAmmo 0x1E / 0x499 (command FUN_1433a3170, writer FUN_143389300 → FUN_14337f410):
        /// string targetName (+0x20, StringFixed&lt;32&gt;; "addto" name, else empty), u64 targetGuid (+0x60: the
        /// admin target / parsed guid, else the invalid guid), u32 count (+0x68), u32 ammoSlot (+0x6C, -1 default),
        /// u32 equipmentSlot (+0x70) [P]. There is no ammo system in Landmark's shipped weapons (OPEN-LEADS L107):
        /// logged.
        /// </summary>
        public static void AddAmmo(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            string name = reader.ReadString();
            ulong target = reader.ReadULong();
            uint count = reader.ReadUInt(), ammoSlot = reader.ReadUInt(), equipSlot = reader.ReadUInt();
            log.Info($"Admin {session.CharacterName} /ammo {count} to \"{name}\" / {target} (ammo slot {(int)ammoSlot}, equipment slot {(int)equipSlot}): no ammo system (L107).");
            Chat.System(session, "/ammo: there is no ammo on this server.");
        }

        /// <summary>
        /// /weaponset profile|equip|cycle|dump|select ... — AdminCommandPacketWeaponSet 0x1E / 0x4EE (command
        /// FUN_1433b64e0, writer FUN_14338c5c0): u64 targetGuid (+0x20: admin target or invalid), string command
        /// (+0x28), string args (+0x40) [P]; client text "Processing weapon set command=[%s] args=[%s]". Weapon-set
        /// profiles are not a server feature here (the hotbar loadouts are the client's): logged.
        /// </summary>
        public static void WeaponSet(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong target = reader.ReadULong();
            string command = reader.ReadString(), args = reader.ReadString();
            log.Info($"Admin {session.CharacterName} /weaponset {command} [{args}] for {(NoGuid(target) ? "self" : target.ToString())}: not built (L107).");
            Chat.System(session, $"/weaponset {command}: not built on this server yet.");
        }

        /// <summary>
        /// /placedeffect &lt;command&gt; &lt;args&gt; — AdminCommandPacketPlacedEffect 0x1E / 0x4EF (command
        /// FUN_1433b02d0, writer FUN_14338aac0): u64 targetGuid (+0x20), string command (+0x28), string args (+0x40)
        /// [P]; client text "Processing placed effect command=[%s] args=[%s]". The sub-commands are server-side strings
        /// the client never names (OPEN-LEADS L107): logged.
        /// </summary>
        public static void PlacedEffect(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ulong target = reader.ReadULong();
            string command = reader.ReadString(), args = reader.ReadString();
            log.Info($"Admin {session.CharacterName} /placedeffect {command} [{args}] for {(NoGuid(target) ? "self" : target.ToString())}: not built (L107).");
            Chat.System(session, $"/placedeffect {command}: not built on this server yet.");
        }

        /// <summary>
        /// Free-fly hide — AdminCommandPacketFreeFlyHide 0x1E / 0x4F1 (command FUN_1433a5d80, writer FUN_143389e90):
        /// u8 hide (+0x20) [P]; without an argument the client toggles from bit 35 of the u64 at its entity +0xa20
        /// (another visibility bit than /hideme's bit 31). What live hid (the character while free-flying, or the
        /// free-fly camera's marker) is not proven (OPEN-LEADS L107), so this only logs; /hideme hides the player.
        /// </summary>
        public static void FreeFlyHide(WorldSession session, byte[] body)
        {
            bool hide = body.Length >= 1 && body[0] != 0;
            log.Info($"Admin {session.CharacterName} free-fly hide {(hide ? "on" : "off")}: semantics not proven (L107), nothing applied.");
        }

        // ------------------------------------------------------------------------------------------------------------
        // Friends

        /// <summary>
        /// /friend forceadd &lt;target&gt; [source] — AdminCommandPacketForceAddFriendRequest 0x1E / 0x4CF (command
        /// FUN_1433a5e60, writer FUN_143389b30): identity target (+0x20), identity source (+0xD0; the client fills in the
        /// player's own name when none is typed) [P]. Puts the target on the source's friends list without a request.
        /// Handed to the social service like the player's own friend requests (<see cref="Social.ISocialService.AdminFriend"/>:
        /// in-process <see cref="Friends.HandleAdminForce"/>, or the remote service when SOCIAL_ADDRESS is set), so the
        /// change lands where the friends lists really live; layout and rules: Internal/Social/AdminFriends.cs.
        /// </summary>
        public static void ForceAddFriend(WorldSession session, byte[] body)
            => Social.SocialServices.Service.AdminFriend(session, ForceAddFriendSub, body);

        /// <summary>
        /// /friend remove &lt;target&gt; [source] — AdminCommandPacketForceRemoveFriendRequest 0x1E / 0x4D0 (writer
        /// FUN_143389ce0; the client prints "Removing friend:  %s" itself): identity target, identity source [P]. Takes the
        /// target off the source's list, as the source's own RemoveFriendRequest would, through the social service
        /// (<see cref="ForceAddFriend"/>).
        /// </summary>
        public static void ForceRemoveFriend(WorldSession session, byte[] body)
            => Social.SocialServices.Service.AdminFriend(session, ForceRemoveFriendSub, body);

        /// <summary>
        /// The outcome of a force-add / force-remove, wherever it was carried out: logged as it really ended, and told to
        /// the admin (when still online) — except a removal from the admin's own list, for which their client already
        /// shows text 560713 "[X] has been removed from your friends list.".
        /// </summary>
        public static void ReportFriendResult(WorldSession admin, ushort sub, Internal.Social.AdminFriendOutcome outcome,
            string target, string source, bool sourceIsAdmin, string adminName = null)
        {
            string line = Internal.Social.AdminFriends.Describe(sub, outcome, target, source, sourceIsAdmin);
            log.Info($"Admin {admin?.CharacterName ?? adminName ?? "?"} /friend {Internal.Social.AdminFriends.Verb(sub)} {target} [{source}]: {outcome} — {line}");
            if (admin == null || (sourceIsAdmin && outcome == Internal.Social.AdminFriendOutcome.Removed))
                return;
            Chat.System(admin, line);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Profiles

        /// <summary>
        /// /profile add|setgroup|delete|select|setrank|setJobExp|showSelected|list — AdminCommandPacketProfile* 0x1E /
        /// 0x3EC (command FUN_1433b0810). All start with u16 profileSub (+0x20) [P]: 1 Add, 2 Delete, 3 SetXp, 4
        /// SetRank, 5 Select, 6 ShowSelected, 7 ListAll, 8 SetProfileGroup. Bodies [P from the writers]:
        /// Add / Delete / Select / SetProfileGroup (FUN_14338ac80 / aec0 / b2f0 / bfd0): u32 profileId (+0x28), u64
        /// targetGuid (+0x30); SetRank / SetXp (FUN_14338b530 / b6d0 → FUN_14337fe20 / fef0): u32 profileId (+0x28),
        /// u32 rank / experience (+0x2C), u64 targetGuid (+0x30); ListAll / ShowSelected (FUN_14338b100 / b870): u64
        /// targetGuid (+0x28). The target is the typed guid or the admin target. Live's job profiles (ranks, job
        /// experience) are not a server feature of this build's game (OPEN-LEADS L107): decoded and logged.
        /// </summary>
        public static void Profile(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            ushort profileSub = reader.ReadUShort();
            string[] names = { "", "add", "delete", "setxp", "setrank", "select", "showselected", "list", "setgroup" };
            string name = profileSub < names.Length ? names[profileSub] : $"sub {profileSub}";
            string detail;
            switch (profileSub)
            {
                case 1: case 2: case 5: case 8:
                    detail = $"profile {reader.ReadUInt()} target {reader.ReadULong()}";
                    break;
                case 3: case 4:
                    detail = $"profile {reader.ReadUInt()} value {reader.ReadUInt()} target {reader.ReadULong()}";
                    break;
                case 6: case 7:
                    detail = $"target {reader.ReadULong()}";
                    break;
                default:
                    detail = BitConverter.ToString(body);
                    break;
            }
            log.Info($"Admin {session.CharacterName} /profile {name} {detail}: job profiles are not built (L107).");
            Chat.System(session, $"/profile {name}: not built on this server yet.");
        }
    }
}
