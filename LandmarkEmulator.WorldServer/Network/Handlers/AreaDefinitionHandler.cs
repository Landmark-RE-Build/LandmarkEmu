using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Area Tool requests (family 0xEA AreaDefinition, client → server): create, update, remove and transfer-shape.
    /// The rules live in <see cref="Game.AreaDefinitions"/>; layouts in trace/0695/area-definitions-695.md.
    /// </summary>
    public static class AreaDefinitionHandler
    {
        /// <summary>Sub 7: the Area Tool's new box (FUN_143739100 → FUN_1431a93e0). Answered with sub 8.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AreaDefinitionCreateTriggerAreaRequest)]
        public static void HandleCreate(WorldSession session, AreaDefinitionCreateTriggerAreaRequest request)
            => Game.AreaDefinitions.Create(session, request);

        /// <summary>Sub 3: a changed area (Lua Area.SetName / AddProperty / Rotate ...). Answered with sub 6.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AreaDefinitionUpdateDefinition)]
        public static void HandleUpdate(WorldSession session, AreaDefinitionUpdateRequest request)
            => Game.AreaDefinitions.Update(session, request);

        /// <summary>Sub 4: an area whose last shape was removed (Lua Area.Remove). Answered with sub 6.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AreaDefinitionRemoveDefinition)]
        public static void HandleRemove(WorldSession session, AreaDefinitionRemoveRequest request)
            => Game.AreaDefinitions.Remove(session, request);

        /// <summary>Sub 2: a shape moved between areas (Lua Area.Group / Area.Ungroup). Answered with sub 6.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AreaDefinitionTransferShape)]
        public static void HandleTransferShape(WorldSession session, AreaDefinitionTransferShapeRequest request)
            => Game.AreaDefinitions.TransferShape(session, request);
    }
}
