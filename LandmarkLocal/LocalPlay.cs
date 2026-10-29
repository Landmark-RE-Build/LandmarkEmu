using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LandmarkEmulator.ClientLaunch;
using LandmarkEmulator.PatchPacks;

namespace LandmarkLocal;

public sealed class LocalPlayResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public bool AccountCreated { get; init; }
    public bool Patched { get; init; }
    /// <summary>User-visible alert when experimental in-memory patches could not be applied (game still started).</summary>
    public string PatchAlert { get; init; } = "";
}

/// <summary>
/// In-app Play: register/login via Auth SRP (LaunchPad <see cref="ServerApi"/>), optional hosted update
/// (<see cref="ServerPatches"/>), <see cref="ClientConfigFile.PointAt"/>, start Landmark64.exe.
/// </summary>
public static class LocalPlay
{
    public static async Task<LocalPlayResult> PlayAsync(
        LocalPaths paths,
        LocalSettings settings,
        string? password,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        settings.EnsureCredentials();
        if (!LocalSettings.LooksLikeLandmarkFolder(settings.LandmarkFolder))
            throw new InvalidOperationException("Set your Landmark folder (the one with Landmark64.exe) first.");

        string account = settings.AdminAccountName.Trim();
        bool haveSaved = settings.HasRememberedLogin(account);
        AccountRules.Check(account, password, passwordRequired: !haveSaved);

        if (GameFolder.IsRunning(settings.LandmarkFolder))
            throw new InvalidOperationException("Landmark is already running. Close it first.");

        ReadinessSnapshot ready = await ReadinessProbe.ProbeAsync(settings, ct);
        if (ready.Phase != HostPhase.Ready)
            throw new InvalidOperationException("Servers are not ready yet. Click Start and wait for Ready.");

        var server = new ServerEntry
        {
            Address = LocalSettings.Defaults.BindHost,
            ApiPort = settings.AccountPort,
            Account = account,
            SavedKey = haveSaved ? settings.SavedSrpKey : "",
            SavedSalt = haveSaved ? settings.SavedSrpSalt : "",
        };
        var api = new ServerApi(server);
        var store = new GameStore(settings.LandmarkFolder);

        progress?.Report("Talking to the local server…");
        JsonElement info = await api.Info();

        if (await Task.Run(() => store.CheckExe(settings.AllowFebruaryWeak), ct) is string notGenuine)
            throw new InvalidOperationException(notGenuine);

        bool patched = await EnsureClientUpdatedAsync(api, store, info, progress, ct);

        string required = info.TryGetProperty("requiredClientBuild", out JsonElement r) ? r.GetString() ?? "" : "";
        string build = GameFolder.Build(settings.LandmarkFolder);
        if (required.Length > 0 && required != build)
        {
            throw new InvalidOperationException(
                $"This server needs game build {required}; yours is {build}. " +
                "Put the .lmpatch update pack in the patches folder next to LandmarkLocal.exe, click Start again, then Play.");
        }

        (bool created, string sessionKey, string key, string salt) =
            await EnsureAccountAndSessionAsync(api, account, password, haveSaved, settings, progress);

        settings.RememberLogin(account, key, salt);
        settings.Save(paths);

        // Bootstrap Admins in WorldServer.json (next restart) + auth DB row (live, ~1 min cache).
        ConfigWriter.WriteWorld(paths, settings);
        try
        {
            using var db = new MariaDbHost(paths, settings);
            db.EnsureAdminEntry(settings.AdminEntry);
            progress?.Report("Admin rights set for your account.");
        }
        catch (Exception ex)
        {
            progress?.Report(
                "Note: could not write admin DB row (" + ex.Message
                + "). WorldServer.json still lists you as admin after Stop → Start.");
        }

        string login = LoginAddress(server, info);
        string marketplace = MarketplaceUrl(server, info);
        progress?.Report("Pointing the game at this PC…");
        // PointAt returns false when ClientConfig.ini already matches (nothing to write) — that is OK.
        try
        {
            if (!ClientConfigFile.PointAt(settings.LandmarkFolder, login, marketplace, settings.ClientConfigForPlay()))
                progress?.Report("ClientConfig.ini already points at this PC.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not update ClientConfig.ini in your Landmark folder: " + ex.Message, ex);
        }

        progress?.Report("Starting Landmark…");
        AllocatorSlotExpand alloc = settings.AllocatorSlotExpand;
        ContourThreadExpand contour = ContourThreadPatch.ClampToAllocator(settings.ContourThreadExpand, alloc);
        var launch = new ClientLaunchOptions
        {
            AllocatorSlotExpand = alloc,
            ContourThreadExpand = contour,
        };
        Process started = GameSession.Start(settings.LandmarkFolder, sessionKey, settings.Locale, launch, out ClientLaunchPatches patches);
        progress?.Report($"Landmark started (pid {started.Id}).");
        string alert = "";
        if (patches.Allocator.Status == AllocatorPatchStatus.Failed)
        {
            progress?.Report(patches.Allocator.UserMessage);
            alert = patches.Allocator.UserMessage;
        }
        if (patches.Contour.Status == AllocatorPatchStatus.Failed)
        {
            progress?.Report(patches.Contour.UserMessage);
            alert = string.IsNullOrEmpty(alert)
                ? patches.Contour.UserMessage
                : alert + "\n\n" + patches.Contour.UserMessage;
        }

        return new LocalPlayResult
        {
            Ok = true,
            AccountCreated = created,
            Patched = patched,
            PatchAlert = alert,
            Message = created
                ? "Account created and Landmark started. You are admin on this local world."
                : "Landmark started. You are admin on this local world.",
        };
    }

    static async Task<bool> EnsureClientUpdatedAsync(
        ServerApi api,
        GameStore store,
        JsonElement info,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ServerPatches.Listed? needed = ServerPatches.Needed(store, ServerPatches.FromInfo(info));
        if (needed == null)
            return false;

        progress?.Report($"Updating Landmark ({needed.Name})… this can take a while.");
        Patching.Progress report = (phase, done, total, file) =>
        {
            if (total > 0 && phase is "download" or "install")
                progress?.Report($"{phase}: {done:N0} / {total:N0}");
            else if (!string.IsNullOrEmpty(file))
                progress?.Report($"{phase}: {file}");
            else
                progress?.Report(phase);
        };
        await ServerPatches.Install(store, api, "LandmarkLocal", needed, needed.Sha256, report, ct);
        progress?.Report("Update installed.");
        return true;
    }

    static async Task<(bool Created, string SessionKey, string Key, string Salt)> EnsureAccountAndSessionAsync(
        ServerApi api,
        string account,
        string? password,
        bool haveSaved,
        LocalSettings settings,
        IProgress<string>? progress)
    {
        try
        {
            if (haveSaved)
            {
                progress?.Report("Logging in…");
                var (sessionKey, key, salt) = await api.Login(account, password, settings.SavedSrpKey, settings.SavedSrpSalt);
                return (false, sessionKey, key, salt);
            }

            progress?.Report("Creating your account…");
            try
            {
                var (regKey, regSalt) = await api.Register(account, password!);
                progress?.Report("Account created — logging in…");
                var (sessionKey, key, salt) = await api.Login(account, null, regKey, regSalt);
                return (true, sessionKey, key, salt);
            }
            catch (ServerApi.Failure ex) when (ex.Status == 409)
            {
                progress?.Report("Account already exists — logging in…");
                var (sessionKey, key, salt) = await api.Login(account, password, null, null);
                return (false, sessionKey, key, salt);
            }
        }
        catch (ServerApi.Failure ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    static string LoginAddress(ServerEntry server, JsonElement info)
    {
        int loginPort = info.TryGetProperty("loginPort", out JsonElement lp) && lp.TryGetInt32(out int p) && p > 0
            ? p
            : 20042;
        return $"{server.Address}:{loginPort}";
    }

    static string MarketplaceUrl(ServerEntry server, JsonElement info)
    {
        if (info.TryGetProperty("worlds", out JsonElement worlds) && worlds.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement w in worlds.EnumerateArray())
            {
                if (w.TryGetProperty("webPagesUrl", out JsonElement u)
                    && Uri.TryCreate(u.GetString(), UriKind.Absolute, out Uri? url))
                {
                    bool local = url.Host is "0.0.0.0" or "[::]"
                        || (IPAddress.TryParse(url.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip));
                    return local ? $"{url.Scheme}://{server.Address}:{url.Port}" : url.ToString().TrimEnd('/');
                }
            }
        }
        return $"http://{server.Address}:19001";
    }
}
