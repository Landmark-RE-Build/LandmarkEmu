using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Crafting requests (RecipeBase 0x42, u32 sub): 1 start crafting {u32 recipe, u32 count, u64}, 0x1C the recipe
    /// component icon list.
    /// </summary>
    public static class CraftingHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Starts a craft (sub 1) or answers the component image-set request (sub 0x1C) with an empty list.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.RecipeBase)]
        public static void HandleRecipe(WorldSession session, RecipeRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            switch (request.SubOpcode)
            {
                case RecipeRequest.Start:
                    Game.Crafting.HandleStart(session, reader.ReadUInt(), reader.ReadUInt());
                    break;
                case 0x1C:
                {
                    // RecipecComponentImageSetIdsRequest (empty): answer 0x1D ResponsePacket {u32 list of image set
                    // ids, a HashList<int>}; the client stores it at recipe manager +0x398. Empty for now.
                    var output = new System.Collections.Generic.List<byte>();
                    var writer = new GamePacketWriter(output);
                    writer.Write(0x1Du);
                    writer.Write(0u);
                    session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
                    break;
                }
                case 0x03:
                    Game.Crafting.Cancel(session);
                    break;
                case 0x23:
                    Game.Crafting.UiStationOpened(session, reader.ReadUInt());
                    break;
                case 0x06:
                    Game.Crafting.StationClosed(session);
                    break;
                case Game.Durability.RepairItemRequest:
                    Game.Durability.HandleRepair(session, request.Body);
                    break;
                case Game.Salvaging.AddItemRequest:
                case Game.Salvaging.StartRequest:
                    Game.Salvaging.Handle(session, request.SubOpcode, request.Body);
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Recipe", request.SubOpcode);
                    log.Debug($"Unhandled recipe request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
