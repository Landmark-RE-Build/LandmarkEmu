using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A player's resource pools (Resources.txt): Life (4, type 1 Health), Energy (19, type 2 "Mana", 456, regenerating
    /// 0.02 per ms) and Stamina (13, type 10, 100, 0.05 per ms). The client checks costs and movement (sprinting)
    /// against these; with no Energy or Stamina pool sent, Shift gave "You don't have enough energy!". The client runs
    /// the regeneration itself from Resources.txt, so the pools are sent once, full, on world entry (and Life whenever
    /// health changes, see <see cref="Health"/>).
    ///
    /// ResourcesBase 0xC4 sub 2 SetCharacterResource (client FUN_143569530): u32 sequence (must grow per player), u8 sub,
    /// u64 character guid, then the record (FUN_142b7f0c0): u32 resource id, u32 resource type, u32 variable count
    /// (0: the maximum comes from Resources.txt), 10 u32 starting with the current value, u32, 4 × u64, 3 bools, u32.
    /// </summary>
    public static class CharacterResources
    {
        public const uint LifeId = 4, LifeType = 1;
        public const uint EnergyId = 19, EnergyType = 2, EnergyMax = 456;
        public const uint StaminaId = 13, StaminaType = 10, StaminaMax = 100;

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, uint> sequences = new();

        /// <summary>Energy and Stamina, full, on world entry.</summary>
        public static void SendInitial(WorldSession session)
        {
            Send(session, EnergyId, EnergyType, EnergyMax);
            Send(session, StaminaId, StaminaType, StaminaMax);
        }

        /// <summary>Sets one resource's current value on the player's client.</summary>
        public static void Send(WorldSession session, uint resourceId, uint resourceType, uint value)
            => SendFor(session, session.PlayerGuid, resourceId, resourceType, value);

        /// <summary>
        /// Sets a resource of any entity (a creature's Life too) on one player's client; the sequence is that player's.
        /// </summary>
        public static void SendFor(WorldSession session, ulong guid, uint resourceId, uint resourceType, uint value)
        {
            uint sequence;
            lock (sync)
            {
                sequences.TryGetValue(session, out sequence);
                sequences[session] = ++sequence;
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sequence);
            writer.Write((byte)2);
            writer.Write(guid);
            writer.Write(resourceId);
            writer.Write(resourceType);
            writer.Write(0u);
            writer.Write(value);
            for (int i = 0; i < 9; i++)
                writer.Write(0u);
            writer.Write(0u);
            for (int i = 0; i < 4; i++)
                writer.Write(0ul);
            writer.Write(false);
            writer.Write(false);
            writer.Write(false);
            writer.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.ResourcesBase, output.ToArray());
        }

        /// <summary>Forgets a player who left.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                sequences.Remove(session);
        }
    }
}
