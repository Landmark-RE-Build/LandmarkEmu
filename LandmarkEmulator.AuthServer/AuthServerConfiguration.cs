using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WebAPI.Configuration;

namespace LandmarkEmulator.AuthServer
{
    public class AuthServerConfiguration
    {
        public struct CharacteterCreateRules
        {
            public bool StrictName { get; set; }
        }

        public NetworkConfig Network { get; set; }
        public DatabaseConfig Database { get; set; }
        public WebApiConfig WebApi { get; set; }
        public string GameDataPath { get; set; }
        public CharacteterCreateRules CreationRules { get; set; }

        /// <summary>
        /// The world server address clients are sent to after character select (CharacterLoginReply ServerAddress).
        /// Empty: the lmemu_auth.zone_server row (127.0.0.1, this PC only). Set it to the address other players reach
        /// this machine on (LAN IP, public IP or host name) when the game runs on a different PC than the servers —
        /// otherwise their client tries to load into the world on its own PC and stops on the loading screen.
        /// </summary>
        /// <remarks>"host" keeps the row's port; "host:port" replaces both.</remarks>
        public string WorldAddress { get; set; } = "";
    }
}
