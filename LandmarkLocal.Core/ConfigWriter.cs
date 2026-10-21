using System.Text.Json;
using System.Text.Json.Nodes;

namespace LandmarkLocal;

/// <summary>Writes AuthServer.json / WorldServer.json for loopback solo play.</summary>
public static class ConfigWriter
{
    public static void WriteAll(LocalPaths paths, LocalSettings settings)
    {
        paths.EnsureLayout();
        Directory.CreateDirectory(paths.WorldDataDir);
        WriteAuth(paths, settings);
        WriteWorld(paths, settings);
    }

    public static void WriteAuth(LocalPaths paths, LocalSettings settings)
    {
        string conn = Conn(settings, "lmemu_auth");
        string charConn = Conn(settings, "lmemu_character");
        var root = new JsonObject
        {
            ["Network"] = new JsonObject
            {
                ["Host"] = LocalSettings.Defaults.BindHost,
                ["Port"] = settings.LoginPort,
            },
            ["Database"] = new JsonObject
            {
                ["Auth"] = Db(conn),
                ["Character"] = Db(charConn),
            },
            ["WebApi"] = new JsonObject
            {
                ["Enabled"] = true,
                ["BaseURI"] = $"http://{LocalSettings.Defaults.BindHost}:{settings.AccountPort}",
                ["RegistrationOpen"] = true,
                ["ServerName"] = "LandmarkLocal",
                ["WebsiteUrl"] = "",
                // Empty = any genuine client (same as Docker's default REQUIRED_CLIENT_BUILD). A fixed
                // "0.695.1.132647" makes February installs (exe build 0.699.1.132863) show PATCH in LaunchPad.
                ["RequiredClientBuild"] = "",
                ["PatchFolder"] = paths.PatchesDir.Replace('\\', '/'),
                ["LoginPort"] = settings.LoginPort,
            },
            ["CreationRules"] = new JsonObject { ["StrictName"] = false },
            ["GameDataPath"] = paths.GameDataDir.Replace('\\', '/'),
            ["WorldAddress"] = "",
            ["CharacterSelectFriends"] = true,
            ["SocialAddress"] = "",
            ["Internal"] = new JsonObject
            {
                ["Host"] = LocalSettings.Defaults.BindHost,
                ["Port"] = 20210,
            },
        };
        WriteJson(paths.AuthServerJson, root);
    }

    public static void WriteWorld(LocalPaths paths, LocalSettings settings)
    {
        string conn = Conn(settings, "lmemu_auth");
        string charConn = Conn(settings, "lmemu_character");
        string zoneConn = Conn(settings, "lmemu_world_" + settings.WorldKey);
        string landmark = settings.LandmarkFolder.Replace('\\', '/');
        string overrides = Path.Combine(paths.WorldDataDir, "settings-overrides.json").Replace('\\', '/');

        var admins = new JsonArray();
        if (!string.IsNullOrWhiteSpace(settings.AdminEntry))
            admins.Add(settings.AdminEntry);

        var root = new JsonObject
        {
            ["Network"] = new JsonObject
            {
                ["Host"] = LocalSettings.Defaults.BindHost,
                ["Port"] = settings.WorldPort,
            },
            ["Database"] = new JsonObject
            {
                ["Auth"] = Db(conn),
                ["Character"] = Db(charConn),
                ["Zone"] = Db(zoneConn),
            },
            ["GameDataPath"] = paths.GameDataDir.Replace('\\', '/'),
            ["GameDirectory"] = landmark,
            ["ZoneServerId"] = 1,
            ["WorldNameId"] = settings.WorldNameId,
            ["ClientWorldId"] = 0,
            ["ClientWorldNameText"] = 0,
            ["PublicAddress"] = $"{LocalSettings.Defaults.BindHost}:{settings.WorldPort}",
            ["MaxPlayers"] = 0,
            ["Admins"] = admins,
            ["AdminList"] = "",
            ["MotdSubject"] = "Welcome to Landmark",
            ["MotdBody"] = "This world is running on LandmarkLocal on this PC. Have fun building!",
            ["HealthFile"] = Path.Combine(paths.LogsDir, "world-alive").Replace('\\', '/'),
            ["SettingsOverridesFile"] = overrides,
            ["WebPagesPort"] = settings.WebPagesPort,
            ["WebPagesHost"] = LocalSettings.Defaults.BindHost,
            ["WebPagesUrl"] = $"http://{LocalSettings.Defaults.BindHost}:{settings.WebPagesPort}",
            ["SocialAddress"] = "",
            ["CommerceAddress"] = "",
            ["EnvServerAddress"] = "",
            ["GatewayInternalAddress"] = "",
        };
        WriteJson(paths.WorldServerJson, root);
    }

    static JsonObject Db(string connectionString) => new()
    {
        ["ConnectionString"] = connectionString,
        ["Provider"] = "MySql",
    };

    static string Conn(LocalSettings settings, string database)
        => $"server={LocalSettings.Defaults.BindHost};port={settings.DbPort};user=landmarkemu;password={settings.DbPassword};database={database}";

    static void WriteJson(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        string text = root.ToJsonString(options);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Copy(temp, path, overwrite: true);
        try { File.Delete(temp); } catch { /* ignore */ }
    }
}
