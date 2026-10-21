using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model.Shared;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Turns a lightweight character placeholder into a full, visible character.
    /// </summary>
    /// <remarks>
    /// Layout per the 0.589 client (PlayerUpdate case 0x5B). After this packet's own fields the client
    /// reads three more from the same buffer: a movement body, a PlayerUpdateUpdateStat and a
    /// ClientUpdateUpdateCalculatedStats, each with its own opcode header.
    /// The character data blob is found by transient id, so it must match the lightweight spawn.
    /// Fields the server has nothing for are zero and every list empty, which keeps the reply small.
    /// </remarks>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateLightweightToFullPcWire)]
    public class PlayerUpdateLightweightToFullPc : IWritable
    {
        /// <summary>
        /// Smallest valid movement body: type 1 carries only a 2-bit-length uint. The 6th byte is the movement
        /// version, which must be 1 like the clients' own updates.
        /// </summary>
        public static readonly byte[] EmptyMovement = { 0x01, 0, 0, 0, 0, 1, 0 };

        public ulong Guid { get; set; }
        public uint TransientId { get; set; }
        public uint ModelId { get; set; }
        public CharacterModelInfo Model { get; set; } = new();
        public byte[] Movement { get; set; } = EmptyMovement;
        public List<(uint StatId, float Value)> Stats { get; } = new();
        /// <summary>
        /// Character stats for the nested PlayerUpdate UpdateStat: the SEND_TO_REMOTE_CLIENT movement times
        /// (Game.CharacterStats.ForOthers, L162); the client copies them into this character's animation (vtable +0x588).
        /// </summary>
        public List<Game.CharacterStats.Stat> CharacterStats { get; } = new();
        /// <summary>
        /// Chat bubble foreground, background and size: the 2nd-4th u32 after the transient id (0.695 blob reader
        /// FUN_142c21350 +0x8 / +0xc / +0x10, copied by the case-0x5B handler in FUN_142cdfa60 to the character entity
        /// +0x219c / +0x21a0 / +0x21a4, the fields CommandPacketSetChatBubbleColor sets). Client defaults 0xFFFFFF,
        /// 0xD4E2F0, 1 (FUN_142e88840).
        /// </summary>
        public (uint Foreground, uint Background, uint Size) ChatBubble { get; set; } = (0xFFFFFF, 0xD4E2F0, 1);

        /// <summary>
        /// The title shown with the name, blob +0x118 {u32 id, u32 type, u32 name text} (read by FUN_142c25e20, the same
        /// reader as PlayerUpdate PlayerTitle). The 0.695 case-0x5B handler (PlayerUpdate dispatcher FUN_142cdfa60 case
        /// 0x5a) hands it to FUN_142ec5330 — the PlayerTitle setter, which overwrites entity +0x1434 / +0x143c and rebuilds
        /// the nameplate — unconditionally, so a zero here wiped the title the introduction had just sent (the observer saw
        /// no title while the player's own screen showed it; audit 2026-10-21) [P].
        /// </summary>
        public (uint Id, uint Type, uint NameId) Title { get; set; }

        /// <summary>
        /// Blob +0x174, the first of the three bools: the AFK flag. The same handler passes it to FUN_142ec2c10, which
        /// writes entity +0x2290 (the byte PlayerUpdate AFK sets, case 0x56 → the same setter) unconditionally, so false
        /// here cleared the AFK the introduction had sent (audit 2026-10-21) [P].
        /// </summary>
        public bool Afk { get; set; }

        public void Write(GamePacketWriter writer)
        {
            var blob = new List<byte>();
            WriteCharacterData(new GamePacketWriter(blob));
            writer.Write(false);                // not compressed
            writer.Write((uint)blob.Count);
            writer.WriteBytes(blob.ToArray());

            writer.WriteBytes(Movement);

            // PlayerUpdateUpdateStat nested in the body: base 0x26, u32 sub 0x53 — the 0.695 reader FUN_142c2cd60 reads the
            // u32 sub; the old one-byte sub left the body 3 bytes short and the client dropped the whole packet, so other
            // players stayed placeholders (trace/0695/audit-wire-a.md M1) [P].
            writer.WriteBytes(new byte[] { 0x26, 0x00, 0x53, 0x00, 0x00, 0x00 });
            writer.Write(Guid);
            Game.CharacterStats.WriteList(writer, CharacterStats);

            // ClientUpdateUpdateCalculatedStats: base 0x29, u16 sub-opcode 0x2C.
            writer.WriteBytes(new byte[] { 0x29, 0x00, 0x2C, 0x00 });
            writer.Write(Guid);
            writer.Write((uint)Stats.Count);
            foreach ((uint statId, float value) in Stats)
            {
                writer.Write(statId);
                writer.Write(value);
            }
        }

        private void WriteCharacterData(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)TransientId);
            writer.Write(0u);
            writer.Write(ChatBubble.Foreground);
            writer.Write(ChatBubble.Background);
            writer.Write(ChatBubble.Size);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);                   // empty list
            Model.Write(writer);
            writer.Write(0u);
            writer.Write("");
            writer.Write("");
            writer.Write("");
            // Temporary appearance model (ProxiedCharacter::SwitchToTemporaryAppearance). Non-zero swaps the character
            // to that bare model and hides its own body, so it stays 0 unless a transformation is wanted.
            writer.Write(0u);
            writer.Write(Title.Id);             // +0x118 title (FUN_142c25e20), applied by FUN_142ec5330
            writer.Write(Title.Type);
            writer.Write(Title.NameId);
            for (int i = 0; i < 4; i++)
                writer.Write(0u);
            writer.Write(0u);                   // empty list
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(Afk);                  // +0x174 AFK (FUN_142ec2c10 → entity +0x2290)
            writer.WriteBytes(new byte[2]);     // +0x175 (→ entity +0xcb7), +0x176
            writer.Write(0u);                   // byte array
            writer.Write(0u);                   // empty list
            writer.Write(0u);                   // byte array
        }
    }
}
