using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WebAPI.Configuration;

namespace LandmarkEmulator.AuthServer
{
    public class AuthServerConfiguration
    {
        /// <summary>
        /// Character creation rules (the JSON key is the property <see cref="CreationRules"/>, e.g. CreationRules__StrictName;
        /// the type's name is not part of any key, so the 2026-10-18 rename from "CharacteterCreateRules" changes no setting).
        /// </summary>
        public struct CharacterCreateRules
        {
            /// <summary>Character names RPG-style only (a capital first letter, letters only). Docker: STRICT_NAMES.</summary>
            public bool StrictName { get; set; }
        }

        /// <summary>The login port (Docker: LOGIN_PORT). Network__Host is internal: the listen address (Docker: every interface).</summary>
        public NetworkConfig Network { get; set; }
        /// <summary>Auth and Character connection strings (Docker sets them from DB_PASSWORD); Provider is internal (MySql).</summary>
        public DatabaseConfig Database { get; set; }
        public WebApiConfig WebApi { get; set; }
        public string GameDataPath { get; set; }
        public CharacterCreateRules CreationRules { get; set; }

        /// <summary>
        /// The world server address clients are sent to after character select (CharacterLoginReply ServerAddress).
        /// Empty: the lmemu_auth.zone_server row (127.0.0.1, this PC only). Set it to the address other players reach
        /// this machine on (LAN IP, public IP or host name) when the game runs on a different PC than the servers —
        /// otherwise their client tries to load into the world on its own PC and stops on the loading screen.
        /// </summary>
        /// <remarks>"host" keeps the row's port; "host:port" replaces both.</remarks>
        public string WorldAddress { get; set; } = "";

        /// <summary>
        /// The login service's internal port (gateway-service.md §3.7): gateways running as their own process consume
        /// server tickets and get relog sessions here (LoginInternalService). Default 0.0.0.0:20210, reached only over the
        /// Docker network; Port 0 turns it off. Internal (no .env line): the container layout fixes it, and the gateway and
        /// world-channel containers reach it as LOGIN_INTERNAL_ADDRESS.
        /// </summary>
        public NetworkConfig Internal { get; set; } = new() { Host = "0.0.0.0", Port = 20210 };

        /// <summary>
        /// Each character in the character list carries its friends list (CharacterSelectFriends.cs): the client counts
        /// each world's online friends from it (server list column FriendsOnline). Default true, as live [I]; false sends
        /// the lists empty. Docker: CHARACTER_SELECT_FRIENDS.
        /// </summary>
        public bool CharacterSelectFriends { get; set; } = true;

        /// <summary>
        /// "host:port" of the shared social service (the worlds' SOCIAL_ADDRESS, e.g. social:20200): the friends lists and
        /// who is online come from it. Empty: the lists come from lmemu_character with every friend offline (each world
        /// keeps its own presence then).
        /// </summary>
        public string SocialAddress { get; set; } = "";
    }
}
