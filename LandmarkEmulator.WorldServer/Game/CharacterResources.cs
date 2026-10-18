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
    /// <para>
    /// Variables (0.695 FUN_142c2ec50 → FUN_142c3c070): u32 count, then per variable u32 key followed by the entry
    /// (FUN_142c2ed20) {u32 variable id (= key), f32 base, f32 modifier}; value = base + modifier. Key
    /// <see cref="MaxValueVariable"/> is the resource's maximum: FUN_143217a00 returns base + modifier of that variable,
    /// falling back to Resources.txt MAX_VALUE (resource definition +4, loader part_0146) when the record has none.
    /// A Set replaces the record's whole variable list (FUN_143550610 → FUN_14354f240 clears it, then copies), so a
    /// raised maximum must ride on every Set of that resource and an omitted one falls back to the table's value.
    /// </para>
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

        /// <summary>
        /// Resource variable id of the maximum (Resources.txt MAX_VALUE): FUN_143217a00 / FUN_143c77590 (part_0146) map
        /// 0x2C7CD6E6 to the definition's MAX_VALUE.
        /// </summary>
        public const uint MaxValueVariable = 0x2C7CD6E6;

        /// <summary>Sets one resource's current value on the player's client.</summary>
        public static void Send(WorldSession session, uint resourceId, uint resourceType, uint value,
            (uint Base, uint Max)? maximum = null)
            => SendFor(session, session.PlayerGuid, resourceId, resourceType, value, maximum);

        /// <summary>
        /// Sets a resource of any entity (a creature's Life too) on one player's client; the sequence is that player's.
        /// With <paramref name="maximum"/> the record carries the MAX_VALUE variable {base = the table's maximum,
        /// modifier = Max − Base}, which is what the HUD bar and the character sheet show as the maximum (16 bytes more);
        /// without it the client uses Resources.txt MAX_VALUE.
        /// </summary>
        public static void SendFor(WorldSession session, ulong guid, uint resourceId, uint resourceType, uint value,
            (uint Base, uint Max)? maximum = null)
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
            if (maximum is (uint baseMax, uint max))
            {
                writer.Write(1u);                          // one variable
                writer.Write(MaxValueVariable);            // key
                writer.Write(MaxValueVariable);            // entry: variable id
                writer.Write((float)baseMax);              //        base
                writer.Write((float)max - baseMax);        //        modifier (max = base + modifier)
            }
            else
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
