using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// One server the player added: just its address. Everything else (name, login port, web pages, patches) comes from
    /// the server's /info when needed, so a server owner's changes reach players by themselves.
    /// </summary>
    public sealed class ServerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        /// <summary>Host name or IP address.</summary>
        public string Address { get; set; } = "127.0.0.1";
        /// <summary>The account service (auth server web API); typed as "address:port" when it isn't 5000.</summary>
        public int ApiPort { get; set; } = 5000;
        public string Account { get; set; } = "";
        /// <summary>
        /// The SRP key (PBKDF2 of the password with this account's salt) when "remember me" is on: it works only for this
        /// account on this server; the password itself is never stored.
        /// </summary>
        public string SavedKey { get; set; } = "";
        public string SavedSalt { get; set; } = "";
        /// <summary>"host" or "host:port" as the player typed it.</summary>
        public string Display => ApiPort == 5000 ? Address : $"{Address}:{ApiPort}";
    }

    /// <summary>The launcher's settings: %AppData%\LandmarkEmu\launchpad.json.</summary>
    public sealed class Settings
    {
        public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LandmarkEmu");
        private static string FilePath => Path.Combine(Folder, "launchpad.json");

        public List<ServerEntry> Servers { get; set; } = new();
        public string SelectedServer { get; set; } = "";
        public bool AutoPlay { get; set; }
        /// <summary>Client language (Internationalization:Locale), e.g. en_US; empty = the game's default.</summary>
        public string Locale { get; set; } = "";
        /// <summary>
        /// May the update be installed from a server that hosts it (Settings > Options)? On by default. Even then LaunchPad
        /// asks every time before it downloads anything (server, build, size), and checks every byte against its own list
        /// of genuine files (ServerPatches). Off: servers' patches are never offered.
        /// </summary>
        public bool AllowServerUpdates { get; set; } = true;
        /// <summary>
        /// "Allow the February 2017 build's three files without a strong fingerprint (local pack files only)": off by
        /// default. On, a pack the player chose or made themselves may write Daybreak's final build's Landmark64.exe and
        /// Locale\en_us_data.dat / .dir checked by size and CRC32 against Daybreak's list only (no genuine copy's SHA-256 is
        /// known yet; Patching.WeakAllowed). Never for a server's pack, never any other file; once a LaunchPad update
        /// carries their SHA-256 it has no effect.
        /// </summary>
        public bool AllowFebruaryWeak { get; set; }
        /// <summary>
        /// Advanced mode (Settings > Options) and the Client settings choices, per game folder (its full path). A folder
        /// gets an entry only once the player changes something there; without one (or with advanced mode off) PLAY
        /// writes LaunchPad's recommended ClientConfig.ini values, as it always did (ClientConfigFile.Apply).
        /// </summary>
        public Dictionary<string, ClientConfigOptions> ClientConfig { get; set; } = new();

        /// <summary>The folder's advanced-mode settings, or null when the player never changed any.</summary>
        public ClientConfigOptions? ConfigFor(string folder)
        {
            string key = FolderKey(folder);
            return ClientConfig.FirstOrDefault(p => FolderKey(p.Key) == key).Value;
        }

        /// <summary>The folder's advanced-mode settings, made when missing (for a change).</summary>
        public ClientConfigOptions ConfigToChange(string folder)
        {
            if (ConfigFor(folder) is ClientConfigOptions known)
                return known;
            var made = new ClientConfigOptions();
            ClientConfig[Path.GetFullPath(folder).TrimEnd('\\')] = made;
            return made;
        }

        private static string FolderKey(string folder) => Path.GetFullPath(folder).TrimEnd('\\').ToUpperInvariant();

        private static readonly JsonSerializerOptions json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), json) ?? new Settings();
            }
            catch (Exception)
            {
                // A damaged settings file: start fresh rather than refuse to open (the old one is kept aside).
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch (Exception) { }
            }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, json));
            File.Move(temp, FilePath, true);
        }

        public ServerEntry? Selected => Servers.FirstOrDefault(s => s.Id == SelectedServer) ?? Servers.FirstOrDefault();
    }
}
