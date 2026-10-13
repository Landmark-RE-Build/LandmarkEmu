using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tab puts the held item away and brings it back (requested behaviour; the 0.589 client itself keeps holding it).
    /// The client reports build mode with CommandPacketBuilderMode {u8 on} (Command 0x1E sub 0x5A, FUN_1433bec40). What
    /// a hotbar press does is move the item between the bag and the main hand (TransferItem; the hotbar window's
    /// DoAction(slot) → equipment slot 2 to hold it, again → back to the bag), so build mode does the same: on, the
    /// main-hand item goes to the bag and is remembered; off, it is equipped again if it is still in the bag.
    /// </summary>
    public static class BuildModeHolster
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>EquipmentSlotDefinitions: the main hand.</summary>
        private const uint MainHand = 2;

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, ulong> holstered = new();
        private static readonly HashSet<WorldSession> building = new();

        /// <summary>Is the player in build mode (Tab)? The prop right-click menu is offered only then.</summary>
        public static bool InBuildMode(WorldSession session)
        {
            lock (sync)
                return building.Contains(session);
        }

        public static void BuildMode(WorldSession session, bool on)
        {
            lock (sync)
                if (on)
                    building.Add(session);
                else
                    building.Remove(session);
            Props.RefreshEditable(session);
            if (on)
            {
                uint? slot = Inventory.Inventory.EquippedSlotFor(MainHand);
                CharacterItemModel held = slot == null ? null : session.Inventory.At(Inventory.Inventory.EquippedContainer, slot.Value);
                if (held == null)
                    return;
                lock (sync)
                    holstered[session] = held.Id;
                Network.Handlers.InventoryHandler.Unequip(session, MainHand);
                log.Debug($"{session.CharacterName}: build mode, put away {held.ItemDefinitionId}.");
                return;
            }
            ulong guid;
            lock (sync)
                if (!holstered.Remove(session, out guid))
                    return;
            CharacterItemModel item = session.Inventory.Get(guid);
            if (item == null || item.Container != Inventory.Inventory.BagContainer)
                return;
            Network.Handlers.InventoryHandler.Equip(session, guid, MainHand);
            log.Debug($"{session.CharacterName}: left build mode, holding {item.ItemDefinitionId} again.");
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                holstered.Remove(session);
                building.Remove(session);
            }
        }
    }
}
