using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Prop requests (PropBase 0xED, u8 sub): place from the palette (0x12), save the gizmo position (3), pick up (5),
    /// delete (7), duplicate (0x0F), sign text (0x13 set, 0x14 ask), name (0x19), on/off (0x1A), tint (0x16, 0x18).
    /// The logic is in <see cref="Game.Props"/>.
    /// </summary>
    public static class PropHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Routes one prop request by sub-opcode.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PropBase)]
        public static void HandleProp(WorldSession session, PropRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            switch (request.SubOpcode)
            {
                case PropRequest.Access:
                    if (request.Body.Length >= 8)
                        Game.Props.Access(session, reader.ReadULong());
                    break;
                case PropRequest.PlacePropById:
                {
                    // The palette shows (and places from) the prop items the player carries: spend one of those when
                    // there is one, else the prop's recipe materials.
                    uint propId = reader.ReadUInt();
                    var owned = Game.Inventory.ItemDefinitionManager.ItemsForProp(propId)
                        .SelectMany(d => session.Inventory.Items.Where(i => i.ItemDefinitionId == d.Id && i.Count > 0))
                        .FirstOrDefault();
                    if (owned != null)
                        Game.Props.BeginPlacement(session, propId, owned.ItemDefinitionId, owned.Id);
                    else
                        Game.Props.BeginPlacement(session, propId);
                    break;
                }
                case PropRequest.Save:
                    Game.Props.Save(session, request.Body);
                    break;
                case PropRequest.PickUp:
                case PropRequest.Delete:
                    if (request.Body.Length >= 8)
                        Game.Props.PickUp(session, reader.ReadULong(), request.SubOpcode == PropRequest.Delete);
                    break;
                case 0x16:
                    Game.Props.SetTint(session, request.Body);
                    break;
                case 0x18:
                    // AvailableTintsRequest: the build tray's tint tab opened.
                    Game.Props.SendTintList(session);
                    break;
                case 0x0F:
                    if (request.Body.Length >= 8)
                        Game.Props.Duplicate(session, reader.ReadULong());
                    break;
                case 0x13:
                    if (request.Body.Length >= 12)
                        Game.Props.SetText(session, reader.ReadULong(), reader.ReadString());
                    break;
                case 0x14:
                    if (request.Body.Length >= 8)
                        Game.Props.SendText(session, reader.ReadULong());
                    break;
                case 0x19:
                    if (request.Body.Length >= 12)
                        Game.Props.SetName(session, reader.ReadULong(), reader.ReadString());
                    break;
                case 0x1A:
                    if (request.Body.Length >= 9)
                        Game.Props.SetEnabled(session, reader.ReadULong(), reader.ReadBool());
                    break;
                case 0x1B:
                    // EditSettingsRequest {u64 prop} (Lua Prop.SetupObject / SetGlowStrength): the settings window.
                    if (request.Body.Length >= 8)
                        Game.PropSettings.SendSettings(session, reader.ReadULong());
                    break;
                case 0x0C:
                    // PropProcessorStateNotification: the client's placement tool reporting its state
                    // ({u32 state, u64, u32}); informational, no reply.
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Prop 0xED", request.SubOpcode);
                    log.Debug($"Unhandled prop request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
