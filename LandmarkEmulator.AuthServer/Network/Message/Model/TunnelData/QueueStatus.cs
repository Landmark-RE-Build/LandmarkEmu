using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;

namespace LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData
{
    /// <summary>
    /// Tunnel type 3: the login queue's place (0.695 client: login tunnel switch FUN_1430b5370 case 3 → reader
    /// FUN_1430adc90 → FUN_1430b2dd0 → the queue data source FUN_14368afa0 in state 1, "LoginQueueDataSource": QueuePosition
    /// (shown + 1), MembersInQueue, NonMembersInQueue, IsMember, EstimatedWaitTime). Sent while a character waits after
    /// CharacterLoginReply result 8 (CharacterQueued), which opens the client's queue dialog (characterselecthandler.lua
    /// OnCharacterLogin → CreateLoginQueueDialog). Wire [P]: i32 position (0-based), i32 members in queue, i32 non-members
    /// in queue, i32 member seconds per place, i32 non-member seconds per place, u8 is member.
    /// </summary>
    [TunnelData(TunnelDataType.QueueStatus)]
    public class QueueStatus : ITunnelData
    {
        public int Position { get; set; }
        public int MembersInQueue { get; set; }
        public int NonMembersInQueue { get; set; }
        public int MemberSecondsPerPlace { get; set; }
        public int NonMemberSecondsPerPlace { get; set; }
        public bool IsMember { get; set; } = true;

        public uint GetSize() => 21;

        public void Read(GamePacketReader reader)
        {
            Position = reader.ReadInt();
            MembersInQueue = reader.ReadInt();
            NonMembersInQueue = reader.ReadInt();
            MemberSecondsPerPlace = reader.ReadInt();
            NonMemberSecondsPerPlace = reader.ReadInt();
            IsMember = reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Position);
            writer.Write(MembersInQueue);
            writer.Write(NonMembersInQueue);
            writer.Write(MemberSecondsPerPlace);
            writer.Write(NonMemberSecondsPerPlace);
            writer.Write(IsMember);
        }
    }

    /// <summary>
    /// Tunnel type 5, client → server: the player left the login queue — the queue dialog's Cancel
    /// (UI/Default/Dialog.lua OnCancelQueueButtonClicked → Lua CharacterSelect.CancelQueue, FUN_1435dab90 →
    /// FUN_1430b1980 → FUN_1430ae970: u8 5, then u64 from the login state +0x38, the character being logged in [I]).
    /// </summary>
    [TunnelData(TunnelDataType.QueueCancel)]
    public class QueueCancel : ITunnelData
    {
        public ulong CharacterId { get; set; }

        public uint GetSize() => 8;

        public void Read(GamePacketReader reader) => CharacterId = reader.ReadULong();

        public void Write(GamePacketWriter writer) => writer.Write(CharacterId);
    }

    /// <summary>
    /// Tunnel type 4: the queued character's turn (0.695 FUN_1430b5370 case 4 → FUN_1430b3cb0): the client closes its
    /// queue dialog (FUN_14368acd0, Lua GameEvents:OnQueueShow(0)) and sends its CharacterLoginRequest again, which the
    /// login server now answers with the world's address. No payload.
    /// </summary>
    [TunnelData(TunnelDataType.ServerQueue)]
    public class ServerQueue : ITunnelData
    {
        public uint GetSize() => 0;

        public void Read(GamePacketReader reader)
        {
        }

        public void Write(GamePacketWriter writer)
        {
        }
    }
}
