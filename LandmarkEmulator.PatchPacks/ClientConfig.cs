using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// Experimental in-memory allocator slot expand (64→127) for 0.695 / 0.699. Not a ClientConfig.ini key —
    /// stored on <see cref="ClientConfigOptions"/> in launchpad.json (and LandmarkLocal settings).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AllocatorSlotExpand>))]
    public enum AllocatorSlotExpand
    {
        Off = 0,
        /// <summary>Apply only when the machine has 16 or more logical processors.</summary>
        Auto = 1,
        On = 2,
    }

    /// <summary>
    /// Experimental in-memory contour / Contouring Job raise (3→5) for 0.695 / 0.699. Requires
    /// <see cref="AllocatorSlotExpand"/> Auto or On. Not a ClientConfig.ini key.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ContourThreadExpand>))]
    public enum ContourThreadExpand
    {
        Off = 0,
        /// <summary>Apply whenever allocator slot expand applies this launch.</summary>
        Auto = 1,
        On = 2,
    }

    /// <summary>What LaunchPad does with one value it manages in ClientConfig.ini (Settings > Client settings, advanced mode).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ConfigChoice>))]
    public enum ConfigChoice
    {
        /// <summary>LaunchPad writes its recommended value on every PLAY (the default, and always so without advanced mode).</summary>
        Recommended,
        /// <summary>LaunchPad writes the player's own value on every PLAY.</summary>
        Custom,
        /// <summary>LaunchPad doesn't touch the line: whatever the file says (or the game's own default when it has no line).</summary>
        Leave
    }

    /// <summary>The player's choice for one managed value.</summary>
    public sealed class SettingChoice
    {
        public ConfigChoice Mode { get; set; } = ConfigChoice.Recommended;
        /// <summary>The value for <see cref="ConfigChoice.Custom"/> (checked by <see cref="ManagedSetting.Validate"/> when set).</summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// One game folder's advanced-mode settings (launchpad.json "ClientConfig", keyed by the game folder). Off, or never
    /// created, LaunchPad writes its recommended values on PLAY exactly as before advanced mode existed; the choices are
    /// kept while it is off and only count while it is on.
    /// </summary>
    public sealed class ClientConfigOptions
    {
        public bool Advanced { get; set; }
        public Dictionary<string, SettingChoice> Choices { get; set; } = new();
        /// <summary>
        /// Experimental: expand the thread-allocator slot table in memory on PLAY (0.695 / 0.699 only).
        /// Independent of ClientConfig.ini; default Off. Shown in Client settings while advanced mode is on.
        /// </summary>
        public AllocatorSlotExpand AllocatorSlotExpand { get; set; } = AllocatorSlotExpand.Off;

        /// <summary>
        /// Experimental: raise contour / Contouring Job workers from 3 to 5 in memory. Default Off.
        /// Only meaningful when <see cref="AllocatorSlotExpand"/> is Auto or On (cleared to Off when allocator is Off).
        /// </summary>
        public ContourThreadExpand ContourThreadExpand { get; set; } = ContourThreadExpand.Off;

        /// <summary>The choice in force for a key: Recommended unless advanced mode is on and the player chose otherwise.</summary>
        public SettingChoice Effective(string key)
            => Advanced && Choices.TryGetValue(key, out SettingChoice? c) && c != null ? c : new SettingChoice();
    }

    /// <summary>A ClientConfig.ini value LaunchPad writes on PLAY.</summary>
    /// <param name="Section">"" for the lines before the first [section] (Server=).</param>
    /// <param name="Editable">May the player choose (advanced mode)? Server= and MarketplaceUrl= are always the chosen server's.</param>
    /// <param name="Shipped">The value in the ClientConfig.ini Steam installs (the 0.695 update carries no settings file).</param>
    /// <param name="CodeDefault">What the game uses when the line is missing ("" = nothing useful).</param>
    public sealed record ManagedSetting(string Section, string Key, bool Editable, string Recommended, string Shipped, string CodeDefault,
        int Min, int Max, string Title, string What, string Fixed)
    {
        /// <summary>A player's value: null when fine, else why not (an integer in Min..Max for the numeric ones).</summary>
        public string? Validate(string value)
        {
            if (!Editable)
                return $"{Key} is always set by LaunchPad.";
            string v = value.Trim();
            if (!Regex.IsMatch(v, @"^[0-9]{1,3}$") || !int.TryParse(v, out int n) || n < Min || n > Max)
                return $"{Key} must be a whole number from {Min} to {Max}.";
            return null;
        }
    }

    /// <summary>
    /// The game's ClientConfig.ini as LaunchPad changes it on PLAY (<see cref="PointAt"/>): the chosen server, its web
    /// pages, and the asset thread count by the player's choice in advanced mode (Settings > Client settings). Nothing
    /// else is ever written: no other key, no [Terrain] UseCachedMeshes / CacheMeshes lines (an old workspace script
    /// added those; they do nothing, UserOptions.ini decides the mesh cache), no file but ClientConfig.ini and, once,
    /// ClientConfig.ini.orig. LaunchPad.exe (Bridge.cs) calls these; the rules are here so the tests run them.
    /// </summary>
    public static class ClientConfigFile
    {
        public const string FileName = "ClientConfig.ini";
        /// <summary>The file as it was before LaunchPad's first change (kept once, never overwritten).</summary>
        public const string OriginalName = FileName + ".orig";

        public const string ThreadsKey = "DirectThreadCount";

        /// <summary>
        /// [AssetDelivery] DirectThreadCount. The 0.695 client reads it with its integer reader (FUN_142b21540: atoi-style
        /// FUN_142b213d0, default 4 when the line is missing, no clamp) in CreateAssetSystem (FUN_142caff80) and hands it
        /// to AssetDeliveryDirect (FUN_143b30030; also through AssetDeliveryWrapper FUN_143b32550 / FUN_143b32cb0 when
        /// IndirectEnabled=1 as shipped), whose Init (FUN_143b310f0) starts that many "FileLoader" threads:
        /// <c>for (i = 0; i &lt; count; i++)</c>. So 0, a negative number or text gives no loader thread at all (nothing
        /// loads): 1 is the least that works. There is no upper limit in the code; every thread takes one of the 64
        /// per-thread slots of the game's allocator (docs/terrain.md "Slow loading screen"), and with the shipped 10 the
        /// terrain contour threads started without one and the loading screen stalled at 50 %. LaunchPad allows 1 to
        /// the shipped 10 and recommends 2 (58 slots used, scene 0.8 s, 2026-10-14).
        /// </summary>
        public static readonly ManagedSetting Threads = new("AssetDelivery", ThreadsKey, true, "2", "10", "4", 1, 10,
            "Asset loading threads",
            "How many threads the game starts to read its asset files (models, textures, sounds). Each thread takes one of " +
            "the 64 thread slots in the game's memory allocator. With Steam's 10, the terrain threads that start when you " +
            "enter a world can find no slot left, and the loading screen stalls at 50 % for minutes (the 2016 \"number of " +
            "cores\" bug). 2 loads quickly and leaves slots for the terrain. 0 or less starts no thread at all and nothing " +
            "loads; the game sets no upper limit, LaunchPad allows up to Steam's 10.",
            "");

        public static readonly ManagedSetting Server = new("", "Server", false, "", "lvseql-liv-l01.eqnlandmark.com:20042", "", 0, 0,
            "Login server",
            "The login server the game connects to: the server you chose in LaunchPad, at its login port.",
            "LaunchPad always sets this on PLAY: without it the game can't reach the server you chose (Steam's value is " +
            "Daybreak's login server, which is gone).");

        public static readonly ManagedSetting Marketplace = new("Marketplace", "MarketplaceUrl", false, "", "https://store.landmarkthegame.com", "", 0, 0,
            "In-game web pages",
            "The address of the Showcase window's pages (store, gallery, following), served by the server's world.",
            "LaunchPad always sets this on PLAY to the address the server gives. The server signs your Showcase in on its " +
            "own pages only, so another address would show a store and gallery that don't know you; one that doesn't " +
            "answer makes the game's browser reload forever, which stalls the game. A server owner changes it with " +
            "WebPagesUrl on the server.");

        /// <summary>
        /// [Marketplace] MarketplaceAssets: where the 0.695 client downloads design and build-site screenshots (the
        /// Designs window tiles <c>template_%llu_%d_small</c>, part_0086.c:1625; Lua Templates.GetTemplateUrl FUN_1435dc0f0 and
        /// Claims.GetClaimUrl FUN_1435e5290 for the screenshot viewers). The server's web pages serve them
        /// (WebPages TemplateImage / SiteImage, /uploads/lm/images/...), so it gets the same address as MarketplaceUrl.
        /// Until 2026-10-26 LaunchPad left Daybreak's dead image server here and every screenshot stayed blank
        /// (TemplateImageCache.log "failed to download image, result: 404"; trace/0695/phaseE4-clientlogs.md F1).
        /// </summary>
        public static readonly ManagedSetting MarketplaceAssets = new("Marketplace", "MarketplaceAssets", false, "",
            "http://assets-cdn.daybreakgames.com", "", 0, 0,
            "Design and site screenshots",
            "Where the game downloads the screenshots of designs and build sites (the Designs window, Player Studio, site " +
            "management), served by the server's world on the same address as the in-game web pages.",
            "LaunchPad always sets this on PLAY to the in-game web pages' address: Steam's value is Daybreak's image server, " +
            "which is gone, so every screenshot stayed blank.");

        /// <summary>Every value LaunchPad writes, in the order the panel lists them.</summary>
        public static readonly IReadOnlyList<ManagedSetting> Managed = [Server, Marketplace, MarketplaceAssets, Threads];

        /// <summary>
        /// The text with the server and the managed values applied (what PLAY writes; pure, for the tests). Server= and
        /// MarketplaceUrl= and MarketplaceAssets= (the same address) are always set; DirectThreadCount by
        /// <paramref name="options"/> (no options or advanced mode off: the recommended 2, as LaunchPad always did). Only
        /// these four lines change, plus the [Marketplace] /
        /// [AssetDelivery] header (after a blank line) when a file has no such section; every other line stays as it is.
        /// </summary>
        public static string Apply(string text, string login, string marketplaceUrl, ClientConfigOptions? options, out bool changed)
        {
            var ini = IniText.Parse(text);
            changed = false;
            changed |= ini.Set(Server.Section, Server.Key, login);
            changed |= ini.Set(Marketplace.Section, Marketplace.Key, marketplaceUrl);
            changed |= ini.Set(MarketplaceAssets.Section, MarketplaceAssets.Key, marketplaceUrl);
            if (ThreadsToWrite(options) is string threads)
                changed |= ini.Set(Threads.Section, Threads.Key, threads);
            return changed ? ini.ToString() : text;
        }

        /// <summary>The DirectThreadCount PLAY writes, or null for "leave the file as it is".</summary>
        public static string? ThreadsToWrite(ClientConfigOptions? options)
        {
            SettingChoice choice = options?.Effective(ThreadsKey) ?? new SettingChoice();
            return choice.Mode switch
            {
                ConfigChoice.Leave => null,
                // A value that fails the check (launchpad.json edited by hand) falls back to the recommended one.
                ConfigChoice.Custom when Threads.Validate(choice.Value) == null => int.Parse(choice.Value.Trim()).ToString(),
                _ => Threads.Recommended
            };
        }

        /// <summary>
        /// Points ClientConfig.ini at a server (<see cref="Apply"/>). The first change keeps the original as
        /// ClientConfig.ini.orig; the file is replaced in one step (written beside it, then renamed over it).
        /// </summary>
        public static bool PointAt(string folder, string login, string marketplaceUrl, ClientConfigOptions? options)
        {
            string path = Path.Combine(folder, FileName);
            string text = File.Exists(path) ? ReadText(path) : "";
            string updated = Apply(text, login, marketplaceUrl, options, out bool changed);
            if (!changed)
                return false;
            KeepOriginal(folder);
            WriteText(path, updated);
            return true;
        }

        /// <summary>A key's value in the text ("" section = before the first [section]), or null when it has no line.</summary>
        public static string? Value(string text, string section, string key) => IniText.Parse(text).Get(section, key);

        /// <summary>
        /// The game's original value: from ClientConfig.ini.orig when LaunchPad kept one (null value = no line there: the
        /// game's default), else the value in the file Steam installs.
        /// </summary>
        public static (string? Value, string Source) Original(string folder, ManagedSetting setting)
        {
            string original = Path.Combine(folder, OriginalName);
            if (File.Exists(original))
                return (Value(ReadText(original), setting.Section, setting.Key), OriginalName);
            return (setting.Shipped, "Steam's ClientConfig.ini");
        }

        // ---- the file: one byte per character, written in one step

        /// <summary>The game's .ini files are one byte per character (Steam's are ASCII); Latin-1 keeps every byte as it is.</summary>
        internal static readonly Encoding FileEncoding = Encoding.Latin1;

        internal static string ReadText(string path) => File.ReadAllText(path, FileEncoding);

        /// <summary>
        /// Written beside the file, then renamed over it: never half a file, a hard link to Steam's copy is replaced rather
        /// than written through, and a failed write leaves no temporary file behind.
        /// </summary>
        internal static void WriteText(string path, string text)
        {
            string temp = path + ".lpnew";
            try
            {
                File.WriteAllText(temp, text, FileEncoding);
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        /// <summary>Before LaunchPad's first change: ClientConfig.ini.orig (never overwritten).</summary>
        internal static void KeepOriginal(string folder)
        {
            string config = Path.Combine(folder, FileName);
            string original = Path.Combine(folder, OriginalName);
            if (File.Exists(config) && !File.Exists(original))
                File.Copy(config, original);
        }
    }

    /// <summary>
    /// An .ini file's lines, kept exactly as they are (comments, blank lines, order); only the lines LaunchPad sets change.
    /// Sections and keys are matched without regard to case, as the game's reader does.
    /// </summary>
    internal sealed class IniText
    {
        public List<string> Lines { get; } = new();
        private bool endsWithNewline = true;

        public static IniText Parse(string text)
        {
            var ini = new IniText();
            if (text.Length == 0)
                return ini;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            ini.endsWithNewline = text.EndsWith('\n') || text.EndsWith('\r');
            ini.Lines.AddRange(ini.endsWithNewline ? lines[..^1] : lines);
            return ini;
        }

        /// <summary>CRLF line ends, as Steam's file has.</summary>
        public override string ToString()
            => Lines.Count == 0 ? "" : string.Join("\r\n", Lines) + (endsWithNewline ? "\r\n" : "");

        /// <summary>The section name of a "[Name]" line, else null.</summary>
        public static string? SectionOf(string line)
        {
            string t = line.Trim();
            return t.Length >= 2 && t[0] == '[' && t[^1] == ']' ? t[1..^1].Trim() : null;
        }

        /// <summary>The key of a "key=value" line (not a comment or section), else null.</summary>
        public static string? KeyOf(string line)
        {
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] is '#' or ';' or '[')
                return null;
            int eq = t.IndexOf('=');
            return eq > 0 ? t[..eq].Trim() : null;
        }

        /// <summary>The first line of the key in the section, or -1.</summary>
        public int Find(string section, string key)
        {
            string current = "";
            for (int i = 0; i < Lines.Count; i++)
            {
                if (SectionOf(Lines[i]) is string name)
                    current = name;
                else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && KeyOf(Lines[i]) is string k
                         && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public string? Get(string section, string key)
        {
            int i = Find(section, key);
            return i < 0 ? null : Lines[i][(Lines[i].IndexOf('=') + 1)..].Trim();
        }

        /// <summary>Sets key=value in the section (adding the line, and the section, when missing); true when the text changed.</summary>
        public bool Set(string section, string key, string value)
        {
            string line = $"{key}={value}";
            int i = Find(section, key);
            if (i >= 0)
            {
                if (Lines[i] == line)
                    return false;
                Lines[i] = line;
                return true;
            }
            if (section.Length == 0)
            {
                // Before the first section, after the keys already there (Steam's file starts World=, Server=).
                int first = Lines.FindIndex(l => SectionOf(l) != null);
                int at = first < 0 ? Lines.Count : first;
                while (at > 0 && Lines[at - 1].Trim().Length == 0)
                    at--;
                Lines.Insert(at, line);
                return true;
            }
            int header = Lines.FindIndex(l => SectionOf(l) is string s && s.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (header >= 0)
            {
                Lines.Insert(header + 1, line);
                return true;
            }
            if (Lines.Count > 0 && Lines[^1].Trim().Length > 0)
                Lines.Add("");
            Lines.Add($"[{section}]");
            Lines.Add(line);
            return true;
        }
    }
}
