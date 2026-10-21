using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandmarkLocal;

/// <summary>Persisted knobs for LandmarkLocal (Landmark folder + admin name + generated DB password).</summary>
public sealed class LocalSettings
{
    public string LandmarkFolder { get; set; } = "";
    /// <summary>Play account name; also the local world's admin (<c>account:Name</c>).</summary>
    public string AdminAccountName { get; set; } = "";
    /// <summary>SRP key (not the password) remembered after Play, same as LaunchPad "remember me".</summary>
    public string SavedSrpKey { get; set; } = "";
    public string SavedSrpSalt { get; set; } = "";
    /// <summary>Client language for Landmark64 (e.g. en_US); empty = game default.</summary>
    public string Locale { get; set; } = "";
    /// <summary>Allow February 2017 (0.699) weak fingerprints for local packs — on for solo grandma installs.</summary>
    public bool AllowFebruaryWeak { get; set; } = true;
    public string DbPassword { get; set; } = "";
    public string DbRootPassword { get; set; } = "";
    public int DbPort { get; set; } = Defaults.DbPort;
    public int LoginPort { get; set; } = Defaults.LoginPort;
    public int AccountPort { get; set; } = Defaults.AccountPort;
    public int WorldPort { get; set; } = Defaults.WorldPort;
    public int WebPagesPort { get; set; } = Defaults.WebPagesPort;
    public string WorldKey { get; set; } = Defaults.WorldKey;
    public uint WorldNameId { get; set; } = Defaults.WorldNameId;

    public static class Defaults
    {
        public const int DbPort = 3307;
        public const int LoginPort = 20042;
        public const int AccountPort = 5000;
        public const int WorldPort = 19000;
        public const int WebPagesPort = 19001;
        public const string WorldKey = "main";
        public const uint WorldNameId = 560916; // Serenity
        public const string BindHost = "127.0.0.1";
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static LocalSettings LoadOrCreate(LocalPaths paths)
    {
        paths.EnsureLayout();
        if (File.Exists(paths.SettingsFile))
        {
            string text = File.ReadAllText(paths.SettingsFile);
            return JsonSerializer.Deserialize<LocalSettings>(text, JsonOptions) ?? new LocalSettings();
        }
        var s = new LocalSettings();
        s.EnsureCredentials();
        s.Save(paths);
        return s;
    }

    public void EnsureCredentials()
    {
        if (string.IsNullOrEmpty(DbPassword))
            DbPassword = RandomPassword();
        if (string.IsNullOrEmpty(DbRootPassword))
            DbRootPassword = RandomPassword();
    }

    public void Save(LocalPaths paths)
    {
        paths.EnsureLayout();
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    public string AdminEntry => string.IsNullOrWhiteSpace(AdminAccountName)
        ? ""
        : "account:" + AdminAccountName.Trim();

    public bool NeedsFirstRun => string.IsNullOrWhiteSpace(LandmarkFolder)
        || string.IsNullOrWhiteSpace(AdminAccountName);

    public bool HasRememberedLogin(string? account = null)
    {
        string name = (account ?? AdminAccountName).Trim();
        return name.Length > 0
               && SavedSrpKey.Length > 0
               && SavedSrpSalt.Length > 0
               && string.Equals(AdminAccountName.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }

    public void RememberLogin(string account, string key, string salt)
    {
        AdminAccountName = account.Trim();
        SavedSrpKey = key;
        SavedSrpSalt = salt;
    }

    public void ForgetLogin()
    {
        SavedSrpKey = "";
        SavedSrpSalt = "";
    }

    public static string RandomPassword(int bytes = 18)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static bool LooksLikeLandmarkFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;
        return File.Exists(Path.Combine(folder, "Landmark64.exe"))
            && Directory.Exists(Path.Combine(folder, "Resources"));
    }

    public static string? TryFindSteamLandmark()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Landmark"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam", "steamapps", "common", "Landmark"),
        ];
        foreach (string c in candidates)
        {
            if (LooksLikeLandmarkFolder(c))
                return c;
        }
        return null;
    }
}
