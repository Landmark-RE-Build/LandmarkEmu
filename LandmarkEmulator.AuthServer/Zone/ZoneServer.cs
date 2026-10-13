using LandmarkEmulator.AuthServer.Network.Message.Model.Shared;
using LandmarkEmulator.AuthServer.Zone.Static;
using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network.Message;
using System;

namespace LandmarkEmulator.AuthServer.Zone
{
    public class ZoneServer : IBuildable<Server>
    {
        public ulong Id { get; }
        public uint NameId { get; }
        public string ZoneName { get; } = "";
        public string Host { get; }
        public ushort Port { get; }
        public ServerFlags Flags { get; }
        /// <summary>Client text "OFFLINE" (Global.Text.51883).</summary>
        public const uint OfflineTextId = 51883;
        /// <summary>Is the world server running (its heartbeat is fresh)? See <see cref="ZoneServerManager.Update"/>.</summary>
        public bool Up { get; set; }
        public uint PlayersOnline { get; set; }

        /// <summary>
        /// Creates a new <see cref="ZoneServer"/> from a <see cref="ZoneServerModel"/>.
        /// </summary>
        public ZoneServer(ZoneServerModel model)
        {
            Id       = model.Id;
            NameId   = model.NameId;
            ZoneName = TextManager.Instance.GetTextForId(NameId);
            Host     = model.Host;
            Port     = model.Port;
            Flags    = (ServerFlags)model.Flags;
        }

        /// <summary>
        /// Returns a <see cref="Server"/> instance for use in Network messaging.
        /// </summary>
        public Server Build()
        {
            return new Server
            {
                ServerId = Id,
                AllowedAccess = true, // TODO: Contact ZoneServer to determine if user is allowed access
                IsLocked = false,     // TODO: Contact ZoneServer to determine if it is locked 
                // The world list shows names by text id only (the client's own text table; the Name string is
                // ignored, FUN_14300f120 / FUN_14301d4a0 - checked in-client 2026-10-14) and the client has no
                // "<name> (OFFLINE)" text, so a world that is down is listed under its "OFFLINE" text (id 51883).
                NameId = Up ? NameId : OfflineTextId,
                Name = ZoneName,
                // Status flags (the client copies them into its world entry, FUN_14300f120 -> +0xc): bit 0 locked,
                // bit 1 down (character select's IsActive 0, FUN_14301d4a0). Always 0 here: when no world is active
                // the client's CharacterSelection.lua shows "No Servers!" and quits on OK, and players should be able
                // to wait at character select through a restart instead. A world that is down answers Play with
                // "Game Server not found." (LoginHandler.HandleCharacterLoginRequest).
                PopulationLevel = 0u,
                ServerInfo = $"<ServerInfo IsRecommended=\"{Convert.ToByte((Flags & ServerFlags.IsRecommended) != 0)}\"></ServerInfo>" // Region is supported, but weird in-game so leaving it out
            };
        }
    }
}
