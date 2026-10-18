using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Hotbar (ActionManager 0x9A) requests. Sub 1 clears a slot {u32 bar, u32 slot}; sub 2 (a drag onto the bar, Lua
    /// Ui.SetSlot) sets a slot {u32 bar, u32 slot, u32 type, u64 value}, where type 0 = empty, 1 = item definition id,
    /// 2 = item guid. Slots are validated, saved per character and echoed back by <see cref="WorldSession.SetActionSlot"/>;
    /// sub 3 / 4 put one slot / every slot back to its default. A right-clicked bag tool no longer arrives here on 0.695
    /// (the 0.589 client sent sub 2 for it; 0.695 sends an equip): the server places it, InventoryHandler.IsRightClickEquip.
    /// </summary>
    public static class ActionBarHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const ushort DefaultSlot = 3, DefaultAllSlots = 4;

        /// <summary>
        /// Routes one ActionManager request: clear a slot or set a slot.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ActionManagerBase)]
        public static void HandleActionManager(WorldSession session, ActionManagerRequest request)
        {
            // Sub 1 {u32 bar, u32 slot} clears a slot (dragging a slot elsewhere sends set-new then clear-old).
            if (request.SubOpcode == ActionManagerRequest.ClearSlot && request.Body.Length >= 8)
            {
                session.SetActionSlot(BitConverter.ToUInt32(request.Body, 0), BitConverter.ToUInt32(request.Body, 4),
                    WorldSession.ActionSlotEmpty, 0);
                return;
            }
            // Sub 3 DefaultSlot {u32 bar, u32 slot} / sub 4 DefaultAllSlots {}: the client already put the slot(s)
            // back to ActionSlotDefinitions' default (FUN_14339f280 / FUN_14339f180); forget the saved choice so the
            // default stays after a relog.
            if (request.SubOpcode == DefaultSlot && request.Body.Length >= 8)
            {
                session.ForgetActionSlots(BitConverter.ToUInt32(request.Body, 0), BitConverter.ToUInt32(request.Body, 4));
                return;
            }
            if (request.SubOpcode == DefaultAllSlots)
            {
                session.ForgetActionSlots();
                return;
            }
            if (request.SubOpcode != ActionManagerRequest.SetSlot || request.Body.Length < 20)
            {
                Network.OpcodeStats.Unanswered("ActionBar", request.SubOpcode);
                log.Debug($"Unhandled action bar request {request.SubOpcode} ({request.Body.Length} bytes).");
                return;
            }
            uint bar = BitConverter.ToUInt32(request.Body, 0);
            uint slot = BitConverter.ToUInt32(request.Body, 4);
            uint type = BitConverter.ToUInt32(request.Body, 8);
            ulong value = BitConverter.ToUInt64(request.Body, 12);
            session.SetActionSlot(bar, slot, type, value);
        }
    }
}
