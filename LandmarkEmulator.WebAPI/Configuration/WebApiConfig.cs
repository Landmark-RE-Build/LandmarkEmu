namespace LandmarkEmulator.WebAPI.Configuration
{
    public class WebApiConfig
    {
        public bool Enabled { get; set; } = true;
        public string BaseURI { get; set; } = "https://0.0.0.0:5000";

        /// <summary>
        /// Address of a reverse proxy in front of this API (the account website, docker/web). Only requests from it may
        /// name the player's own address in X-Forwarded-For; everyone else is counted by the address they connect from.
        /// Empty: no proxy is trusted.
        /// </summary>
        public string TrustedProxy { get; set; } = "";

        /// <summary>Can new accounts be created (the launcher, the account website)?</summary>
        public bool RegistrationOpen { get; set; } = true;

        /// <summary>New accounts per address per hour (beyond it: 429).</summary>
        public int RegistrationsPerAddressPerHour { get; set; } = 10;

        /// <summary>New accounts per hour from all addresses together (stops a spammer with many addresses).</summary>
        public int RegistrationsPerHour { get; set; } = 60;

        // ---- What the launcher shows for this server (GET /info) ------------------------------------------------

        /// <summary>The server's name in the launcher.</summary>
        public string ServerName { get; set; } = "LandmarkEmu";

        /// <summary>The account website (the launcher's "Create account on the website" link); empty = none.</summary>
        public string WebsiteUrl { get; set; } = "";

        /// <summary>
        /// The game build players need ("0.589.1.128895", "0.695.1.132647"); the launcher asks for a patch pack when
        /// the installed game is another build. Empty = any.
        /// </summary>
        public string RequiredClientBuild { get; set; } = "";

        /// <summary>
        /// A JSON file with the launcher's news: [{ "title": "...", "date": "2026-10-14", "text": "..." }, ...], newest
        /// first. Re-read on every request, so it can be edited while the server runs. Empty or missing = no news.
        /// </summary>
        public string NewsFile { get; set; } = "";

        /// <summary>
        /// The login port the launcher sends the game to. 0 = the auth server's own port (Network.Port); set it when
        /// players reach the server through a different port (a router or Docker port mapping).
        /// </summary>
        public int LoginPort { get; set; }

        /// <summary>
        /// A folder with the patch pack the launcher downloads and installs on top of a verified Steam copy of the game
        /// (<see cref="PatchHost"/>): the update's .lmpatch as LaunchPad.exe --make-update makes it; packs apply in file-name
        /// order. Empty = no patches. (Players' LaunchPad has server patches switched off for now.)
        /// </summary>
        public string PatchFolder { get; set; } = "";
    }
}
