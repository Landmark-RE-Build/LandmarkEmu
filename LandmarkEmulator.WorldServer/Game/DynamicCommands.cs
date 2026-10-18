using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Server commands added one at a time (trace/0695/architecture-leads.md L5 "dynamic commands", TASKS
    /// "AddWorldCommand / AddZoneCommand / ExecuteCommand"). Besides the CommandListPacket (0x1E / 0x44, sent whole at
    /// zone-in by <see cref="Emotes.SendCommandList"/>), live could register single commands:
    /// <list type="bullet">
    /// <item><b>0x1E / 0x45 CommandPacketAddWorldCommand</b> and <b>0x1E / 0x46 CommandPacketAddZoneCommand</b>
    /// {u16 0x1E, u16 sub, string name} (reader 0.695 FUN_143391be0 / FUN_143391ce0, handler FUN_1433baf00 cases 0x45 /
    /// 0x46) [P]. The client makes the name an alias of its own <c>__sendworldcommand</c> / <c>__sendzonecommand</c>
    /// (FUN_1433a0890): it keeps the name's hash (<see cref="Hash"/>) in one table and skips a name that is already
    /// registered or that is already a command of any kind (FUN_142bfb0f0) [P].</item>
    /// <item>Typing "/name args" then runs <c>__sendworldcommand name args</c> (FUN_1433a2710 → FUN_1433b6900) or
    /// <c>__sendzonecommand</c> (FUN_1433a2790 → FUN_1433b6fb0): the first word is hashed and, when it is in the table,
    /// <b>0x1E / 0x47 CommandPacketExecuteCommand</b> {u32 hash, string rest of the line} (ctor FUN_143397d00: +0x20
    /// u32, +0x28 string) goes out on channel 1 (World) or channel 0 (Zone) (FUN_14338d040(..., 1 / 0, 1)) [P layout
    /// fields; the u32-then-string wire order is the struct order, as every other Command packet, I].</item>
    /// </list>
    /// The name never travels back, only its hash, so the server keeps hash → name per command it added. Both channels
    /// end in this zone process (the world-channel service relays channel-1 packets it does not sink unchanged,
    /// world-channel-service.md §6.2), so one handler serves both.
    /// Ours: the duel commands are world commands (duels are world-channel encounters, docs/encounters.md); which
    /// commands live added this way is server data the client does not hold (OPEN-LEADS L5 notes).
    /// </summary>
    public static class DynamicCommands
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort AddWorldCommandSub = 0x45, AddZoneCommandSub = 0x46, ExecuteCommandSub = 0x47;

        /// <summary>A command added by name: its handler gets the session and the rest of the typed line (trimmed).</summary>
        private sealed record Command(string Name, bool World, Action<WorldSession, string> Run);

        private static readonly Dictionary<uint, Command> commands = new();
        private static readonly object sync = new();

        static DynamicCommands()
        {
            // Duels (an encounter of the world channel): /duel name, /duelaccept, /dueldecline, /duelleave.
            foreach (string name in Emotes.DuelCommands)
                Register(name, world: true, (session, args) => Emotes.Run(session, name, args));
            // /who [name]: the client has the reply but no request (Who.cs).
            Register(Who.CommandName, world: true, Who.Run);
        }

        /// <summary>
        /// The client's name hash for these commands (FUN_1433a0890 / FUN_1433b6900): Jenkins one-at-a-time over the
        /// upper-cased characters (towupper), with arithmetic (signed) right shifts.
        /// </summary>
        public static uint Hash(string name)
        {
            uint h = 0;
            foreach (char c in name)
            {
                h = (char.ToUpperInvariant(c) + h) * 0x401;
                h ^= (uint)((int)h >> 6);
            }
            uint t = h * 9;
            return (t ^ (uint)((int)t >> 11)) * 0x8001;
        }

        /// <summary>Adds a command this server answers through <see cref="HandleExecute"/>.</summary>
        public static void Register(string name, bool world, Action<WorldSession, string> run)
        {
            lock (sync)
                commands[Hash(name)] = new Command(name, world, run);
        }

        /// <summary>
        /// Start-up check (OPEN-LEADS L74, trace/0695/agent-leads3-a.md): the zone list (<see cref="Emotes.ZoneCommandNames"/>,
        /// CommandList source 1), these added world / zone commands and the gateway's list (GatewaySession.GatewayCommands,
        /// source 3) must share no name. [P] The client keeps the first registration of a name for the whole session: the
        /// CommandList handler FUN_1433bad50 skips a name already registered by any source ("Duplicate command from server,
        /// ignored: %s"), and an AddWorldCommand / AddZoneCommand skips a name that is already a command (FUN_142bfb0f0); the
        /// source chosen at registration decides where typing it goes (1 → channel 0, 2 → channel 1, 3 → gateway opcode 9,
        /// ServerCommand FUN_1433c3d80). So a duplicate would silently route to the wrong back end. The added commands are also
        /// keyed by name hash on both sides (FUN_1433a0890; <see cref="commands"/>), so two names with one hash are reported
        /// too. Logs an error naming each duplicate; returns how many there are.
        /// </summary>
        public static int CheckUniqueNames()
        {
            var lists = new List<(string Source, IEnumerable<string> Names)>
            {
                ("zone list (Emotes)", Emotes.ZoneCommandNames()),
            };
            List<Command> added;
            lock (sync)
                added = new List<Command>(commands.Values);
            lists.Add(("added world commands (DynamicCommands)", added.Where(c => c.World).Select(c => c.Name)));
            lists.Add(("added zone commands (DynamicCommands)", added.Where(c => !c.World).Select(c => c.Name)));
            // The gateway's list is private to the gateway library (another process in the Docker layout); read by
            // reflection so the check covers it without the gateway exposing it.
            var gatewayField = typeof(LandmarkEmulator.Gateway.Network.GatewaySession).GetField("GatewayCommands",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (gatewayField?.GetValue(null) is string[] gateway)
                lists.Add(("gateway list (GatewaySession)", gateway));
            else
                log.Warn("Command names: the gateway's command list (GatewaySession.GatewayCommands) was not found; not checked.");

            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int duplicates = 0;
            foreach ((string source, IEnumerable<string> names) in lists)
                foreach (string name in names)
                {
                    if (seen.TryGetValue(name, out string first))
                    {
                        duplicates++;
                        log.Error($"Command name \"{name}\" is in both the {first} and the {source}: the client keeps the first "
                            + "one it is sent and ignores the other for the whole session (FUN_1433bad50).");
                    }
                    else
                        seen[name] = source;
                }
            foreach (var clash in seen.Keys.GroupBy(Hash).Where(g => g.Count() > 1))
            {
                duplicates++;
                log.Error($"Command names {string.Join(", ", clash.Select(n => $"\"{n}\""))} share the hash 0x{clash.Key:X8}: an "
                    + "added command is keyed by its hash (FUN_1433a0890).");
            }
            if (duplicates == 0)
                log.Info($"Command names: {seen.Count} names across the zone, world and gateway lists, no duplicates.");
            return duplicates;
        }

        /// <summary>Whether a name is one of these commands (kept out of the zone's CommandListPacket).</summary>
        public static bool IsDynamic(string name)
        {
            lock (sync)
                return commands.TryGetValue(Hash(name), out Command c) && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sends every added command to a player, each as its own AddWorldCommand / AddZoneCommand (zone-in, after the
        /// command list). The client ignores names it already has, so repeating this on every zone-in is harmless.
        /// </summary>
        public static void SendAll(WorldSession session)
        {
            List<Command> all;
            lock (sync)
                all = new List<Command>(commands.Values);
            foreach (Command c in all)
                Send(session, c.Name, c.World);
        }

        /// <summary>One AddWorldCommand (0x45) or AddZoneCommand (0x46): {u16 sub, string name}.</summary>
        public static void Send(WorldSession session, string name, bool world)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(world ? AddWorldCommandSub : AddZoneCommandSub);
            w.Write(name);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
        }

        /// <summary>
        /// ExecuteCommand (0x47) {u32 hash, string rest of the line}: runs the command whose name has that hash. An
        /// unknown hash (a command added by an older server run) is logged and dropped, as the client expects no answer.
        /// </summary>
        public static void HandleExecute(WorldSession session, byte[] body)
        {
            uint hash;
            string args;
            try
            {
                var r = new GamePacketReader(body);
                hash = r.ReadUInt();
                args = r.ReadString().Trim();
            }
            catch (Exception)
            {
                log.Warn($"Unreadable ExecuteCommand from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }
            Command command;
            lock (sync)
                commands.TryGetValue(hash, out command);
            if (command == null)
            {
                log.Info($"{session.CharacterName} ran an added command we don't know (hash 0x{hash:X8}, \"{args}\").");
                return;
            }
            log.Info($"{session.CharacterName} ran {(command.World ? "world" : "zone")} command {command.Name} {args}".TrimEnd());
            command.Run(session, args);
        }
    }
}
