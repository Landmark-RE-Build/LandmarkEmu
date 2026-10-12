using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Moving props: doors swing, portcullises rise, shutters open, levers flip, fans spin. The client ships the moves
    /// as SimpleAnimationSets.txt (21 sets: 1 "Door Open/Close (+90y)", 3 "Portcullis Translate Up/Down (4.5y)", 14
    /// "Lever On/Off (+45y)"...) with their keyframes (SimpleAnimation.txt, SimpleAnimationFrames.txt; index 0 = open /
    /// on, 1 = close / off), but not which prop uses which set: the server says so.
    ///
    /// Packet: UpdatePropInteractionState 0xFD with behaviour type 0 (ClientPropSimpleAnimationStateBehavior; the client
    /// registers 0 simple animation, 1 composite effect, 2 setting, 3 Morpheme request, 4 restriction, 5 graphics):
    /// u64 prop, u32 0, u32 key (the set), state {u32 index, u32 index (FUN_1433c8a90 → FUN_143bad550 animates to it),
    /// u64, u32 n × i32 [set, 0, 0, 0], u32 n × string}, bool.
    /// </summary>
    public static class PropAnimations
    {
        private const uint SimpleAnimationBehaviour = 0;
        public const uint Open = 0, Close = 1;

        private static readonly Dictionary<uint, uint> setByProp = new();

        /// <summary>The animation set for a prop by its name (0 = none).</summary>
        public static uint SetFor(uint propId)
        {
            lock (setByProp)
            {
                if (setByProp.TryGetValue(propId, out uint cached))
                    return cached;
                uint nameId = GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0;
                string n = nameId != 0 ? Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? "" : "";
                bool Has(string word) => n.Contains(word, StringComparison.OrdinalIgnoreCase);
                uint set =
                    Has("Trap Door") ? 2u :
                    Has("Secret Door") ? 4u :
                    Has("Sliding Door") ? (Has("Right") ? 19u : 18u) :
                    Has("Portcullis") ? (Has("Dark Elf") || Has("DELF") ? 10u : 3u) :
                    Has("Door") ? (Has("Left") ? 7u : 1u) :
                    Has("Gate") ? (Has("Left") ? 13u : 12u) :
                    Has("Shutter") ? (Has("Bahama") ? 15u : Has("Right") ? 17u : 16u) :
                    Has("Lever") || Has("Switch") ? 14u :
                    Has("Fan") ? (Has("Slow") ? 6u : 5u) :
                    Has("Sliding Platform") ? 8u : 0u;
                setByProp[propId] = set;
                return set;
            }
        }

        /// <summary>Plays a prop's open (on) or close (off) move for one viewer. Nothing for props without a set.</summary>
        public static bool Send(WorldSession session, uint propId, ulong guid, uint index)
        {
            uint set = SetFor(propId);
            if (set == 0)
                return false;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(guid);
            w.Write(SimpleAnimationBehaviour);
            w.Write(set);
            w.Write(index);
            w.Write(index);
            w.Write(0ul);
            w.Write(4u);
            w.Write(set);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);                                 // strings
            w.Write(false);
            session.EnqueueMessage(ZoneMessageOpcode.UpdatePropInteractionState, output.ToArray());
            return true;
        }
    }
}
