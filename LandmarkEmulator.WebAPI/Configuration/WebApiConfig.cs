namespace LandmarkEmulator.WebAPI.Configuration
{
    public class WebApiConfig
    {
        /// <summary>Run the account / launcher web API. Internal (no .env line): the Docker auth service always runs it.</summary>
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

        /// <summary>
        /// New accounts per address per hour (beyond it: 429; requests from this PC are not counted). Default 3, the same
        /// as docker/auth compose and .env.example (REGISTRATIONS_PER_ADDRESS_PER_HOUR).
        /// </summary>
        public int RegistrationsPerAddressPerHour { get; set; } = 3;

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
        /// players reach the server through a different port (a router or Docker port mapping). Internal under Docker (no
        /// .env line): docker/auth publishes LOGIN_PORT on the same number inside and outside, so 0 is right; change
        /// LOGIN_PORT instead.
        /// </summary>
        public int LoginPort { get; set; }

        /// <summary>
        /// The patch folder: the server owner puts (uploads) the update pack here, as LaunchPad makes it (Settings > Patch
        /// packs > Create update pack, or LaunchPad.exe --make-update), and players' LaunchPad offers it to them (it asks
        /// first, and checks every byte against its own list of genuine files). A pack is only served after it has passed
        /// the same check here (<see cref="PatchLibrary"/>); one that fails is moved to rejected\ with the reason. Packs
        /// are listed in file-name order. Empty = no patches. Docker: docker/launcher/patches (PATCH_DIR).
        /// </summary>
        public string PatchFolder { get; set; } = "";

        /// <summary>
        /// Optional: a Steam 0.589 copy of Landmark (read only). With it the patch folder's check also replays every edit
        /// and patch in a pack on the Steam files and checks each result's SHA-256 (without it, they are checked by their
        /// headers, and each player's LaunchPad replays them on their own copy before anything changes). Empty or not a
        /// Steam 0.589 game folder = none. Docker: PATCH_VERIFY_STEAM_DIR.
        /// </summary>
        public string PatchVerifySteamDir { get; set; } = "";
    }
}
