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
            // A prop request means the build-mode prop menu is closed (a button was used, or the player went on to
            // something else): its name label goes (Game/Props.MenuName.cs). Not for the prop tool's own state reports.
            if (request.SubOpcode != 0x0C)
                Game.Props.HideMenuName(session);
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
                    // PickUp {u64 prop, u8}: u8 1 is the menu's Recycle (Prop.DisassembleObject), 0 Send to Inventory
                    // (Prop.PickUpObject); 0.695 FUN_142fb6740 modes 3 / 1, 0.589 FUN_142f17890 (TASKS 6.38).
                    if (request.Body.Length >= 8)
                    {
                        ulong guid = reader.ReadULong();
                        bool recycle = request.SubOpcode == PropRequest.PickUp && request.Body.Length >= 9 && reader.ReadByte() != 0;
                        Game.Props.PickUp(session, guid, request.SubOpcode == PropRequest.Delete, recycle);
                    }
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
                    // SetPropName: {string name, u64 prop} (FUN_1435fca20) [P].
                    if (request.Body.Length >= 12)
                    {
                        string name = reader.ReadString();
                        if (reader.BytesRemaining >= 8)
                            Game.Props.SetName(session, reader.ReadULong(), name);
                    }
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
                case 0x1D:
                    // PropPacketDialogResponse (0.695; Lua Prop.OnConfirmNpcDialog / OnDenyNpcDialog, FUN_1435fbf80 /
                    // FUN_1435fc030): {u64 question id as the server sent it, bool accepted}.
                    if (request.Body.Length >= 9)
                    {
                        ulong questionId = reader.ReadULong();
                        bool accepted = reader.ReadBool();
                        if (!Game.Npc.WorldNpcs.DialogResponse(session, questionId, accepted))
                            Game.StoryTools.DialogResponse(session, questionId, accepted);
                    }
                    break;
                case 0x0C:
                    // PropProcessorStateNotification: the client's placement tool reporting its state
                    // ({u32 state, u64, u32}); informational, no reply.
                    break;
                case 0x09:
                    // PropPacketRequestList {string} (/queryprops: CommandQueryProps FUN_1433a22d0 → FUN_1433b2480, no
                    // admin check; the prop manager's own sender FUN_142fc0320 has no reference) [P]. A developer query
                    // with no live answer found: the ListReply 0xED/0xA (reader FUN_142faa9b0) went out at zone-in (L52)
                    // [I]. Logged only (agent-l140.md B4).
                    log.Debug($"{session.CharacterName}: /queryprops {BitConverter.ToString(request.Body)} (no reply).");
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Prop 0xED", request.SubOpcode);
                    log.Debug($"Unhandled prop request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
