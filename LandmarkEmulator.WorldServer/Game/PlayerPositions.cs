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
    /// Players on the map (TASKS 5.4, "friends and players on the map (0x18 / 0x24)"; trace/systems/commands.md 0x24).
    /// <para>
    /// <b>Request</b>, CommandPacketRequestPlayerPositions, Command 0x1E sub 0x24 [P, 0.695]: built only by the Lua native
    /// <c>Ui.RequestPlayerPositions</c> FUN_1435c8060 (binding 0x1447a7340, luabind_695.tsv; no other code reference) and
    /// sent on the World channel (FUN_1435d4df0(…, 1, 1, 0)). Body after the u16 sub, writer FUN_1435d3cb0: u64 (+0x20) =
    /// the local player's guid (the player object at +0x282c8, its +0x278 guid getter vtable +0x28, the same getter the
    /// client compares entity guids with, part_0065 :15136), u8 (+0x28) = 1, u8 (+0x29) = 1 (the builder sets the pair
    /// to 0x101). No shipped 0.695 script calls the native (UI/Default/*.lua, Resources/Scripts/*.lua,
    /// UI/ScriptsBase.bin, re/as3_695 grep: none), so the unmodified client never sends it; like ChatChannelOn / Off it
    /// is reachable from any Lua (e.g. the dev <c>luacode</c> command).
    /// </para>
    /// <para>
    /// <b>Reply</b>, CommandPacketRequestPlayerPositionsReply, Command 0x1E sub 0x25 [P, 0.695 reader FUN_1433831b0]:
    /// u64 (+0x20), bool (+0x28), bool (+0x29), then the list FUN_1433880c0: u32 n × PlayerPosition (element reader
    /// FUN_143385790, element ctor FUN_1433c05b0) {u64 guid (+0x00), string name (+0x08, plain u32-length string,
    /// FUN_142d4b5d0), bool (+0x30), bool (+0x32), bool (+0x31), 4 × u32 (+0x20, +0x24, +0x28, +0x2C)}. The 0.589
    /// reader FUN_1432d63c0 is the same (coverage-695: "same").
    /// </para>
    /// <para>
    /// <b>What the client does with it</b> [P]: the Command dispatcher's case 0x25 (part_0102 :2816) reads the packet
    /// with FUN_1433831b0 and frees the list (FUN_1433c4dc0) — nothing is stored or shown. So the answer has no visible
    /// effect on the retail client; it is built so the request is answered the way the client reads it.
    /// </para>
    /// <para>
    /// Ours [I] (not in the client): the u64 and the two flags are echoed; the list holds the requester's friends and
    /// party members on the same landscape (the players a map would mark, as the friends-only 0x18 does), not the
    /// requester and not admins hidden with /hideme, at most <see cref="MaxListed"/>; the three bools are false (their
    /// meaning isn't in the client) and the four u32 are f32 x, y, z and 1 (a vec4 position; the element ctor zeroes all
    /// four). One reply per request, nothing pushed.
    /// </para>
    /// </summary>
    public static class PlayerPositions
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort RequestSub = 0x24;
        private const ushort ReplySub = 0x25;
        private const int MaxListed = 100;

        public static void Handle(WorldSession session, byte[] body)
        {
            ulong echoGuid = session.PlayerGuid;
            bool first = true, second = true;
            try
            {
                var r = new GamePacketReader(body);
                echoGuid = r.ReadULong();
                first = r.ReadByte() != 0;
                second = r.ReadByte() != 0;
            }
            catch (Exception)
            {
                // A short body: the values Ui.RequestPlayerPositions always sends.
            }

            // Friends: FriendOf(x) lists who has x on their friends list, so the requester lists x exactly when the
            // requester is in FriendOf(x) (in-process and with the social service; as FriendPositions). Party members:
            // Groups.MembersOf (this world's members, with or without the social service).
            var shown = new HashSet<WorldSession>(Groups.MembersOf(session));
            foreach (WorldSession p in PlayerManager.Snapshot())
                if (Social.SocialServices.FriendCache.FriendOf(p.PlayerGuid).Contains(session.PlayerGuid))
                    shown.Add(p);
            List<WorldSession> listed = shown
                .Where(p => p != session && !p.Disconnected && p.ZoneId == session.ZoneId && !AdminCommands.IsHidden(p))
                .Take(MaxListed).ToList();

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(ReplySub);
            w.Write(echoGuid);
            w.Write(first);
            w.Write(second);
            w.Write((uint)listed.Count);
            foreach (WorldSession p in listed)
            {
                w.Write(p.PlayerGuid);
                w.Write(p.CharacterName ?? "");
                w.Write(false);                          // +0x30
                w.Write(false);                          // +0x32
                w.Write(false);                          // +0x31
                w.Write(p.Position.X);
                w.Write(p.Position.Y);
                w.Write(p.Position.Z);
                w.Write(1f);
            }
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
            log.Debug($"{session.CharacterName}: player positions → {listed.Count} player(s).");
        }
    }
}
