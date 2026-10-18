using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Friends on the map (TASKS 5.4; trace/systems/commands.md 0x18): the client's CommandPacketFriendsPositionRequest
    /// (Command 0x1E sub 0x18, Lua GetFriendPositions; sent on the World channel, FUN_14337d950 → FUN_14338d230(…, 1, …))
    /// is answered with FriendUpdatePositionsPacket, FriendBase 0x4D u8 sub 5 [P: reader FUN_14353d860 / entries
    /// FUN_14353cb80 + FUN_14353bba0, 0.695]: u32 n × { u64 friend guid, string name, bool hidden, then — only when hidden
    /// is false — f32 x, f32 y, f32 z }. The handler FUN_143543760 finds each guid in the client's own friend list and
    /// copies hidden / x / y / z into that entry (+0x114..+0x120) for the map; unknown guids are skipped.
    /// Ours [I]: positions are local metres; friends online on another landscape of this world are sent hidden (no
    /// marker); friends on other worlds are left out.
    /// </summary>
    public static class FriendPositions
    {
        public const ushort RequestSub = 0x18;
        private const byte PositionsSub = 5;

        public static void Handle(WorldSession session)
        {
            // The requester's friends who are online on this world: FriendOf(x) lists who has x on their friends list,
            // so the requester lists x exactly when the requester is in FriendOf(x) (works in-process and with the
            // social service, ZoneSocialCaches.IFriendOfCache).
            var friends = PlayerManager.Snapshot()
                .Where(p => p != session && Social.SocialServices.FriendCache.FriendOf(p.PlayerGuid).Contains(session.PlayerGuid))
                .ToList();
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(PositionsSub);
            w.Write((uint)friends.Count);
            foreach (WorldSession f in friends)
            {
                bool hidden = f.ZoneId != session.ZoneId;
                w.Write(f.PlayerGuid);
                w.Write(f.CharacterName);
                w.Write(hidden);
                if (!hidden)
                {
                    w.Write(f.Position.X);
                    w.Write(f.Position.Y);
                    w.Write(f.Position.Z);
                }
            }
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase, output.ToArray());
        }
    }
}
