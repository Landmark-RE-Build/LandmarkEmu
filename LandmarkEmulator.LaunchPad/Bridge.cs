using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// The page's native side, as LaunchPad's page called its shell and patcher plug-in (window control, login, patch,
    /// launch). Requests are JSON messages { id, method, args }; answers are { id, ok, result } or { id, ok: false,
    /// error }; the page also gets events { event, data } (progress, game exit).
    /// </summary>
    public sealed class Bridge
    {
        private readonly MainWindow window;
        private readonly CoreWebView2 core;
        private readonly Settings settings = Settings.Load();
        private GameStore? store;
        /// <summary>A message for the player from opening the game folder (an interrupted update was undone).</summary>
        private string? notice;
        /// <summary>SRP keys of accounts logged in this session (only kept on disk when "remember me" is on).</summary>
        private readonly Dictionary<string, (string Account, string Key, string Salt)> sessions = new();
        private CancellationTokenSource? busy;
        /// <summary>The game LaunchPad started, while it runs.</summary>
        private Process? game;
        /// <summary>Each server's last /info this session (the Client settings panel shows what PLAY will write for it).</summary>
        private readonly Dictionary<string, JsonElement> lastInfo = new();

        public bool GameRunning => game is { HasExited: false };

        private static readonly JsonSerializerOptions camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public Bridge(MainWindow window, CoreWebView2 core)
        {
            this.window = window;
            this.core = core;
            core.WebMessageReceived += async (_, e) =>
            {
                // A file dropped on the page arrives as a CoreWebView2File next to the message (only its path is used).
                string[] files = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
                await Handle(e.WebMessageAsJson, files);
            };
        }

        public static void OpenInBrowser(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }

        private void Send(object message) => core.PostWebMessageAsJson(JsonSerializer.Serialize(message, camel));

        private void Event(string name, object? data) => window.Dispatcher.Invoke(() => Send(new { @event = name, data }));

        private async Task Handle(string json, string[] files)
        {
            JsonNode? request = JsonNode.Parse(json);
            string id = request?["id"]?.ToString() ?? "";
            string method = request?["method"]?.GetValue<string>() ?? "";
            JsonNode? args = request?["args"];
            try
            {
                object? result = await Call(method, args, files);
                Send(new { id, ok = true, result });
            }
            catch (Exception e)
            {
                string message = e is ServerApi.Failure or InvalidOperationException or InvalidDataException or IOException
                    ? e.Message : $"{e.GetType().Name}: {e.Message}";
                if (e is OperationCanceledException)
                    message = "Stopped.";
                Send(new { id, ok = false, error = message });
            }
        }

        private static string Str(JsonNode? args, string name) => args?[name]?.ToString() ?? "";
        private static bool Bool(JsonNode? args, string name) => args?[name]?.GetValue<bool>() == true;

        private ServerEntry Server(JsonNode? args)
            => settings.Servers.FirstOrDefault(s => s.Id == Str(args, "server")) ?? throw new InvalidOperationException("Choose a server first.");

        /// <summary>The game folder's store; only when LaunchPad.exe is in a Landmark folder.</summary>
        private GameStore Store()
        {
            if (!Game.IsGameFolder(Game.Folder))
                throw new InvalidOperationException("LaunchPad.exe must be in your Landmark folder (next to Landmark64.exe).");
            if (store == null)
            {
                store = new GameStore(Game.Folder);
                notice = store.RecoverInterrupted();
            }
            return store;
        }

        private async Task<object?> Call(string method, JsonNode? args, string[] files)
        {
            switch (method)
            {
                // ---- window (LaunchPad: AppMinimizeByUser, AppCloseByUser, StartDrag)
                case "minimize": window.WindowState = WindowState.Minimized; return null;
                case "close": busy?.Cancel(); window.Close(); return null;
                case "drag": window.BeginDrag(); return null;
                case "openUrl": OpenInBrowser(Str(args, "url")); return null;
                case "openFolder": Process.Start(new ProcessStartInfo(Game.Folder) { UseShellExecute = true }); return null;

                case "state": return State();

                // ---- servers: the player types an address; the rest comes from the server's /info
                case "saveServer":
                {
                    var (host, port) = ParseAddress(Str(args, "address"));
                    ServerEntry server = settings.Servers.FirstOrDefault(s => s.Id == Str(args, "server")) ?? new ServerEntry();
                    if (server.Address != host || server.ApiPort != port)
                        (server.SavedKey, server.SavedSalt) = ("", "");   // a login belongs to its server
                    (server.Address, server.ApiPort) = (host, port);
                    string name = Str(args, "name").Trim();
                    if (name.Length == 0)
                    {
                        try
                        {
                            JsonElement info = await new ServerApi(server).Info();
                            name = info.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                        }
                        catch (ServerApi.Failure)
                        {
                            // Offline right now: saved anyway, named after its address.
                        }
                    }
                    server.Name = name.Length > 0 ? name : server.Display;
                    if (!settings.Servers.Contains(server))
                        settings.Servers.Add(server);
                    settings.SelectedServer = server.Id;
                    settings.Save();
                    return State();
                }
                case "removeServer":
                    settings.Servers.RemoveAll(s => s.Id == Str(args, "server"));
                    settings.Save();
                    return State();
                case "selectServer":
                    settings.SelectedServer = Str(args, "server");
                    settings.Save();
                    return State();
                case "serverInfo":
                {
                    ServerEntry server = Server(args);
                    JsonElement info = await new ServerApi(server).Info();
                    lastInfo[server.Id] = info;
                    return new { info, patch = PatchSummary(server, info) };
                }

                // ---- accounts
                case "register":
                {
                    ServerEntry server = Server(args);
                    string account = Str(args, "account").Trim(), password = Str(args, "password");
                    CheckAccount(account, password);
                    await new ServerApi(server).Register(account, password);
                    return await LogIn(server, account, password, Bool(args, "remember"));
                }
                case "login":
                {
                    ServerEntry server = Server(args);
                    string account = Str(args, "account").Trim(), password = Str(args, "password");
                    CheckAccount(account, password.Length > 0 ? password : "xxxxxx");
                    return await LogIn(server, account, password, Bool(args, "remember"));
                }
                case "logout":
                {
                    ServerEntry server = Server(args);
                    sessions.Remove(server.Id);
                    (server.SavedKey, server.SavedSalt) = ("", "");
                    settings.Save();
                    return State();
                }

                // ---- the update from the server (ServerPatches), then playing. Nothing is downloaded until the player has
                //      seen what it is (updateOffer: server, build, size; only the server's list is fetched and checked)
                //      and clicked to install it (update, with the list's SHA-256 they were shown).
                case "updateOffer":
                {
                    if (!settings.AllowServerUpdates)
                        throw new InvalidOperationException("Installing the update from servers is switched off (Settings > Options).");
                    ServerEntry server = Server(args);
                    GameStore s = Store();
                    var api = new ServerApi(server);
                    JsonElement info = await api.Info();
                    ServerPatches.Listed listed = ServerPatches.Needed(s, ServerPatches.FromInfo(info))
                        ?? throw new InvalidOperationException("Your game needs nothing from this server.");
                    ServerPatches.Offer offer = await Long(cancel => ServerPatches.Check(api, listed, cancel));
                    return new
                    {
                        server = server.Name, address = server.Display, id = listed.Sha256, name = offer.Manifest.Name,
                        build = offer.Build.Label, packBytes = listed.PackBytes, files = offer.Manifest.Files.Count,
                        bytes = offer.Manifest.Files.Sum(f => f.Size), deletes = offer.Manifest.Delete.Count
                    };
                }
                case "update":
                {
                    if (!settings.AllowServerUpdates)
                        throw new InvalidOperationException("Installing the update from servers is switched off (Settings > Options).");
                    ServerEntry server = Server(args);
                    GameStore s = Store();
                    if (Game.IsRunning())
                        throw new InvalidOperationException("Close Landmark first.");
                    string agreed = Str(args, "id");
                    if (agreed.Length == 0)
                        throw new InvalidOperationException("Agree to the update first.");
                    var api = new ServerApi(server);
                    JsonElement info = await api.Info();
                    ServerPatches.Listed listed = ServerPatches.Needed(s, ServerPatches.FromInfo(info))
                        ?? throw new InvalidOperationException("Your game needs nothing from this server.");
                    await Long(cancel => ServerPatches.Install(s, api, server.Name, listed, agreed, Report, cancel));
                    return State();
                }
                case "play":
                {
                    ServerEntry server = Server(args);
                    GameStore s = Store();
                    if (Game.IsRunning())
                        throw new InvalidOperationException("Landmark is already running.");
                    var api = new ServerApi(server);
                    JsonElement info = await api.Info();
                    lastInfo[server.Id] = info;
                    // Only a genuine Daybreak exe is started (Steam's 0.589 one or a known 0.695 build's: GameStore.CheckExe).
                    if (await Task.Run(() => s.CheckExe(settings.AllowFebruaryWeak)) is string notGenuine)
                        throw new InvalidOperationException(notGenuine);
                    string required = info.TryGetProperty("requiredClientBuild", out JsonElement r) ? r.GetString() ?? "" : "";
                    string build = Game.Build(Game.Folder);
                    if (required.Length > 0 && required != build)
                        throw new InvalidOperationException($"This server needs game build {required}; yours is {build}. Install its patch pack (Settings > Patch packs).");
                    (string account, string key, string salt) = sessions.TryGetValue(server.Id, out var known) ? known
                        : server.SavedKey.Length > 0 ? (server.Account, server.SavedKey, server.SavedSalt)
                        : throw new InvalidOperationException("Log in first.");
                    // A fresh login for the game's session key (the saved key logs in without the password).
                    var (sessionKey, _, _) = await api.Login(account, null, key, salt);
                    // ClientConfig.ini: this server, and the asset threads by the player's choice (advanced mode; else 2).
                    ClientConfigOptions? config = settings.ConfigFor(Game.Folder);
                    Game.PointAt(Game.Folder, LoginAddress(server, info), MarketplaceUrl(server, info), config);
                    // Experimental expands only count while advanced mode is on (choices are kept while off).
                    AllocatorSlotExpand allocMode = config is { Advanced: true }
                        ? config.AllocatorSlotExpand
                        : AllocatorSlotExpand.Off;
                    ContourThreadExpand contourMode = config is { Advanced: true }
                        ? ContourThreadPatch.ClampToAllocator(config.ContourThreadExpand, allocMode)
                        : ContourThreadExpand.Off;
                    var launch = new ClientLaunchOptions
                    {
                        AllocatorSlotExpand = allocMode,
                        ContourThreadExpand = contourMode,
                    };
                    Process started = Game.Start(Game.Folder, sessionKey, settings.Locale, launch, out ClientLaunchPatches patches);
                    started.EnableRaisingEvents = true;
                    // BeginInvoke, not Invoke: Process raises Exited holding a lock on itself, and waiting here for the
                    // window thread (which then reads the process) deadlocked the launcher.
                    started.Exited += (_, _) => window.Dispatcher.BeginInvoke(() => GameExited(started));
                    game = started;
                    window.WindowState = WindowState.Minimized;
                    // Fail-open: game is running; surface loud alerts when experimental patches could not be applied.
                    bool allocFail = patches.Allocator.Status == AllocatorPatchStatus.Failed;
                    bool contourFail = patches.Contour.Status == AllocatorPatchStatus.Failed;
                    if (!allocFail && !contourFail)
                        return null;
                    return new
                    {
                        patchAlert = allocFail ? patches.Allocator.UserMessage : null,
                        contourPatchAlert = contourFail ? patches.Contour.UserMessage : null,
                    };
                }
                case "setOption":
                {
                    if (args?["autoPlay"] != null)
                        settings.AutoPlay = Bool(args, "autoPlay");
                    if (args?["locale"] != null)
                        settings.Locale = Str(args, "locale");
                    if (args?["allowServerUpdates"] != null)
                        settings.AllowServerUpdates = Bool(args, "allowServerUpdates");
                    if (args?["allowFebruaryWeak"] != null)
                        settings.AllowFebruaryWeak = Bool(args, "allowFebruaryWeak");
                    settings.Save();
                    return State();
                }

                // ---- the player's own patch packs
                case "choosePack":
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Choose the patch pack (.lmpatch)",
                        Filter = "Patch packs (*.lmpatch)|*.lmpatch"
                    };
                    if (dialog.ShowDialog(window) != true)
                        return null;
                    return DescribePack(dialog.FileName, settings.AllowFebruaryWeak);
                }
                case "dropPack":
                    return DescribePack(files.FirstOrDefault() ?? throw new InvalidOperationException("Drop the patch pack (.lmpatch) on the window."),
                        settings.AllowFebruaryWeak);
                case "installPack":
                {
                    GameStore s = Store();
                    string path = Str(args, "path");
                    await Long(cancel => Task.Run(() =>
                    {
                        // A pack the player chose: their February opt-in applies (never to a server's pack).
                        using var pack = new Patching.Pack(path);
                        return s.Install(pack, "", "", Report, cancel, settings.AllowFebruaryWeak);
                    }, cancel));
                    return State();
                }
                case "undoPack":
                {
                    GameStore s = Store();
                    await Long(cancel => Task.Run(() => s.UndoLast(Report, cancel), cancel));
                    return State();
                }
                case "revertAll":
                {
                    GameStore s = Store();
                    await Long(cancel => Task.Run(() =>
                    {
                        while (s.Stack.Count > 0)
                            s.UndoLast(Report, cancel);
                        return 0;
                    }, cancel));
                    return State();
                }
                // ---- making the update pack from the player's own 0.695 copy (Settings > Patch packs > Create update pack)
                case "chooseFolder":
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Str(args, "title") is { Length: > 0 } t ? t : "Choose a folder" };
                    return dialog.ShowDialog(window) == true ? dialog.FolderName : null;
                }
                case "chooseSave":
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save the update pack as", Filter = "Patch packs (*.lmpatch)|*.lmpatch",
                        FileName = "update-0695" + PatchFile.Extension, DefaultExt = PatchFile.Extension, AddExtension = true
                    };
                    return dialog.ShowDialog(window) == true ? dialog.FileName : null;
                }
                case "checkMakeSource":
                {
                    string from = Str(args, "from");
                    if (!Directory.Exists(from))
                        throw new InvalidOperationException("Choose your 0.695 game folder first.");
                    Patching.FolderCheck check = await Long(cancel => Task.Run(() => Patching.CheckFolder(from, Report, cancel, settings.AllowFebruaryWeak), cancel));
                    return FolderCheckResult(check);
                }
                case "makePack":
                    return await Long(cancel => Task.Run(() => MakePack(Str(args, "from"), Str(args, "steam"), Str(args, "out"), cancel), cancel));

                case "clearCache":
                    Store().ClearCache();
                    return State();

                // ---- checks
                case "verifySteam":
                {
                    GameStore s = Store();
                    var problems = await Long(cancel => Task.Run(() => s.VerifySteam(Report, cancel), cancel));
                    return new { problems = problems.Take(200), total = problems.Count, steamBuild = Patching.SteamBuild };
                }
                case "validate":
                {
                    GameStore s = Store();
                    var (problems, count) = await Long(cancel => Task.Run(() => s.Validate(Report, cancel), cancel));
                    return new { checkedFiles = count, problems = problems.Take(200), total = problems.Count };
                }
                case "cancel":
                    busy?.Cancel();
                    return null;

                // ---- advanced mode (Settings > Options): the ClientConfig.ini values PLAY writes, and the player's choice
                //      for each one that may be chosen (ClientConfigFile in LandmarkEmulator.PatchPacks)
                case "setAdvanced":
                    Store();
                    settings.ConfigToChange(Game.Folder).Advanced = Bool(args, "on");
                    settings.Save();
                    return State();
                case "clientConfig":
                    return ClientConfigState();
                case "setAllocatorSlotExpand":
                {
                    ClientConfigOptions options = Advanced();
                    if (!Enum.TryParse(Str(args, "mode"), true, out AllocatorSlotExpand mode) || !Enum.IsDefined(mode))
                        throw new InvalidOperationException("Choose Off, Auto, or On.");
                    options.AllocatorSlotExpand = mode;
                    if (mode == AllocatorSlotExpand.Off)
                        options.ContourThreadExpand = ContourThreadExpand.Off;
                    settings.Save();
                    return ClientConfigState();
                }
                case "setContourThreadExpand":
                {
                    ClientConfigOptions options = Advanced();
                    if (!Enum.TryParse(Str(args, "mode"), true, out ContourThreadExpand mode) || !Enum.IsDefined(mode))
                        throw new InvalidOperationException("Choose Off, Auto, or On.");
                    if (mode != ContourThreadExpand.Off && options.AllocatorSlotExpand == AllocatorSlotExpand.Off)
                        throw new InvalidOperationException(
                            "Turn on Allocator slot expand (Auto or On) before enabling Contour threads.");
                    options.ContourThreadExpand = ContourThreadPatch.ClampToAllocator(mode, options.AllocatorSlotExpand);
                    settings.Save();
                    return ClientConfigState();
                }
                case "setConfigChoice":
                {
                    ClientConfigOptions options = Advanced();
                    string key = Str(args, "key");
                    ManagedSetting setting = ClientConfigFile.Managed.FirstOrDefault(m => m.Key == key)
                        ?? throw new InvalidOperationException($"LaunchPad doesn't manage {key}.");
                    if (!setting.Editable)
                        throw new InvalidOperationException(setting.Fixed);
                    if (!Enum.TryParse(Str(args, "mode"), true, out ConfigChoice mode) || !Enum.IsDefined(mode))
                        throw new InvalidOperationException("Choose what LaunchPad does with it.");
                    SettingChoice choice = options.Choices.TryGetValue(key, out SettingChoice? known) && known != null ? known : new SettingChoice();
                    string value = Str(args, "value").Trim();
                    if (mode == ConfigChoice.Custom && setting.Validate(value) is string bad)
                        throw new InvalidOperationException(bad);
                    if (setting.Validate(value) == null)
                        choice.Value = int.Parse(value).ToString();   // kept for the box also when another choice is picked
                    choice.Mode = mode;
                    options.Choices[key] = choice;
                    settings.Save();
                    return ClientConfigState();
                }
            }
            throw new InvalidOperationException($"Unknown request: {method}");
        }

        /// <summary>
        /// The game closed: LaunchPad comes back (also when the player had closed it: it was only hidden). Exit Game
        /// ends the process with 0; on a crash the client's handler (SetUnhandledExceptionFilter → wws_crashreport) runs
        /// its uploader and then TerminateProcess(self, -1), or Windows ends it with the exception code.
        /// </summary>
        private void GameExited(Process exited)
        {
            int code = 0;
            try { code = exited.ExitCode; } catch (InvalidOperationException) { }
            if (game == exited)
                game = null;
            window.BringBack();
            // A crash: the handler's -1, the uploader's 0xFF, or a Windows exception code (0xC0000005 ...). Anything
            // else is a normal close.
            bool crashed = code == 0xFF || (uint)code >= 0x80000000;
            Send(new { @event = "gameExited", data = new { code, crashed, hex = $"0x{code:X8}" } });
        }

        /// <summary>This folder's advanced-mode settings; refused unless advanced mode is on (the choices are advanced only).</summary>
        private ClientConfigOptions Advanced()
        {
            Store();
            ClientConfigOptions? options = settings.ConfigFor(Game.Folder);
            if (options is not { Advanced: true })
                throw new InvalidOperationException("Switch on advanced mode first (Settings > Options).");
            return options;
        }

        /// <summary>
        /// The Client settings panel: each value LaunchPad writes (what it does, the game's original, LaunchPad's
        /// recommendation, what the file says now, the player's choice, what the next PLAY writes). Reads only.
        /// </summary>
        private object ClientConfigState()
        {
            Store();
            ClientConfigOptions? options = settings.ConfigFor(Game.Folder);
            string path = Path.Combine(Game.Folder, ClientConfigFile.FileName);
            string text = File.Exists(path) ? File.ReadAllText(path, System.Text.Encoding.Latin1) : "";
            ServerEntry? server = settings.Selected;
            JsonElement? info = server != null && lastInfo.TryGetValue(server.Id, out JsonElement i) ? i : null;
            return new
            {
                advanced = options?.Advanced == true,
                exists = File.Exists(path),
                server = server?.Name ?? "",
                allocatorSlotExpand = (options?.AllocatorSlotExpand ?? AllocatorSlotExpand.Off).ToString(),
                contourThreadExpand = ContourThreadPatch.ClampToAllocator(
                    options?.ContourThreadExpand ?? ContourThreadExpand.Off,
                    options?.AllocatorSlotExpand ?? AllocatorSlotExpand.Off).ToString(),
                settings = ClientConfigFile.Managed.Select(m =>
                {
                    var (original, source) = ClientConfigFile.Original(Game.Folder, m);
                    SettingChoice choice = options != null && options.Choices.TryGetValue(m.Key, out SettingChoice? c) && c != null ? c : new SettingChoice();
                    string recommended = m.Editable ? m.Recommended
                        : server == null || info == null ? ""
                        : m == ClientConfigFile.Server ? LoginAddress(server, info.Value) : MarketplaceUrl(server, info.Value);
                    return new
                    {
                        key = m.Key, section = m.Section, title = m.Title, what = m.What, @fixed = m.Fixed, editable = m.Editable,
                        min = m.Min, max = m.Max, shipped = m.Shipped, codeDefault = m.CodeDefault,
                        original, originalSource = source, recommended,
                        current = ClientConfigFile.Value(text, m.Section, m.Key),
                        mode = choice.Mode.ToString(), value = choice.Value,
                        // What the next PLAY writes (null: the line is left as it is).
                        writes = m.Editable ? ClientConfigFile.ThreadsToWrite(options) : recommended
                    };
                }).ToList()
            };
        }

        /// <summary>The login server for ClientConfig.ini Server=: the server's address at the login port its /info gives.</summary>
        private static string LoginAddress(ServerEntry server, JsonElement info)
        {
            int loginPort = info.TryGetProperty("loginPort", out JsonElement lp) && lp.TryGetInt32(out int p) && p > 0 ? p : 20042;
            return $"{server.Address}:{loginPort}";
        }

        /// <summary>"host", "host:port" or "[v6]:port" -> host, account-service port (5000 when not given).</summary>
        private static (string Host, int Port) ParseAddress(string text)
        {
            string a = text.Trim();
            if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                a = new Uri(a).Authority;
            a = a.TrimEnd('/');
            if (a.Length > 0 && Uri.TryCreate("http://" + a, UriKind.Absolute, out Uri? uri) && uri.AbsolutePath == "/"
                && uri.Host.Length > 0 && Uri.CheckHostName(uri.IdnHost) != UriHostNameType.Unknown)
                return (uri.Host, uri.IsDefaultPort && !a.EndsWith(":80") ? 5000 : uri.Port);
            throw new InvalidOperationException("Enter the server's address: a host name or IP address, with :port if it isn't 5000.");
        }

        /// <summary>
        /// The in-game web pages (ClientConfig.ini MarketplaceUrl): the first world's, as it registered itself. A world
        /// that registered a loopback or any-address host is reached at the server's own address.
        /// </summary>
        private static string MarketplaceUrl(ServerEntry server, JsonElement info)
        {
            if (info.TryGetProperty("worlds", out JsonElement worlds) && worlds.ValueKind == JsonValueKind.Array)
                foreach (JsonElement w in worlds.EnumerateArray())
                    if (w.TryGetProperty("webPagesUrl", out JsonElement u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out Uri? url))
                    {
                        bool local = url.Host is "0.0.0.0" or "[::]" || (IPAddress.TryParse(url.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip));
                        return local ? $"{url.Scheme}://{server.Address}:{url.Port}" : url.ToString().TrimEnd('/');
                    }
            return $"http://{server.Address}:19001";
        }

        /// <summary>
        /// Does this server host the update and does the game need it, for the page (UPDATE). Nothing is fetched beyond
        /// /info; what it is exactly is checked when the player asks (updateOffer).
        /// </summary>
        private object PatchSummary(ServerEntry server, JsonElement info)
        {
            if (!Game.IsGameFolder(Game.Folder) || !settings.AllowServerUpdates)
                return new { needed = false };
            ServerPatches.Listed? listed = ServerPatches.Needed(Store(), ServerPatches.FromInfo(info));
            return listed == null ? new { needed = false }
                : new { needed = true, name = listed.Name, version = listed.Version, packBytes = listed.PackBytes };
        }

        private static object FolderCheckResult(Patching.FolderCheck check) => new
        {
            ok = check.Build != null, build = check.Build?.Label ?? "", closest = check.Closest, checkedFiles = check.Checked,
            problems = check.Problems.Take(50), total = check.Problems.Count, weak = check.Weak
        };

        /// <summary>
        /// Makes the update pack from the player's 0.695 folder (<see cref="Patching.MakeUpdate"/>, as --make-update): checks
        /// the folder against the strong list first (refused unless it is a known build, file for file), and the Steam
        /// copy if one is used ("own" = LaunchPad's own game folder, a path, or "" for none: every file whole). The made
        /// pack is checked with the same rules a server and every player use (<see cref="PackCheck.Verify"/>, replaying
        /// each edit and patch on the Steam copy when there is one) before it is reported done; one that fails is deleted.
        /// </summary>
        private object MakePack(string from, string steam, string output, CancellationToken cancel)
        {
            if (!Directory.Exists(from))
                throw new InvalidOperationException("Choose your 0.695 game folder first.");
            if (output.Length == 0 || !output.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Choose where to save the pack (a {PatchFile.Extension} file).");
            string? steamFolder = steam == "own" ? Game.Folder : steam.Length > 0 ? steam : null;
            if (steamFolder != null && Same(steamFolder, from))
                throw new InvalidOperationException("The Steam copy and the 0.695 folder are the same folder.");
            if (Path.GetFullPath(output).StartsWith(Path.GetFullPath(from).TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the pack somewhere outside your 0.695 game folder.");

            // 1. The 0.695 folder: every file the update carries must be a known build's genuine file.
            bool february = settings.AllowFebruaryWeak;
            Patching.FolderCheck check = Patching.CheckFolder(from, Report, cancel, february);
            if (check.Build == null)
                throw new InvalidOperationException($"Your 0.695 folder isn't a known 0.695 build file for file ({check.Problems.Count} file(s) " +
                    $"missing, different or unverifiable for the {check.Closest}; first: " +
                    string.Join("; ", check.Problems.Take(5).Select(p => $"{p.Path}: {p.Issue}")) + "). No pack was made.");
            // 2. The Steam copy the edits and patches are made against: complete and unchanged (Steam's SHA-1s).
            if (steamFolder != null)
            {
                GameStore steamStore = steam == "own" ? Store() : new GameStore(steamFolder);
                if (steamStore.Stack.Count > 0)
                    throw new InvalidOperationException("That game folder has the update installed, so it isn't a Steam copy now. Choose " +
                        "\"No Steam copy\"" + " or another copy.");
                steamStore.RequireSteamCopy(Report, cancel);
            }
            // 3. The pack, then 4. the same check a server and every player run on it.
            var (edits, patches, whole, bytes) = Patching.MakeUpdate(steamFolder, [from], output,
                $"Update to {check.Build.Label}", null, Report, cancel, february);
            PackCheck.Result verified;
            try
            {
                using var pack = new Patching.Pack(output);
                verified = PackCheck.Verify(pack, steamFolder, Report, cancel, february);
            }
            catch (Exception)
            {
                try { File.Delete(output); } catch (IOException) { }
                throw;
            }
            return new { path = output, bytes, edits, patches, whole, build = verified.Build.Label, replayed = verified.Replayed, weak = check.Weak };
        }

        private static bool Same(string a, string b)
            => Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        /// <summary>A chosen or dropped pack (an .lmpatch).</summary>
        private static object DescribePack(string path, bool allowFebruary)
        {
            if (!path.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Patch packs are {PatchFile.Extension} files.");
            return new { path, info = Patching.Describe(path, Game.Folder, allowFebruary) };
        }

        private static void CheckAccount(string account, string password)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(account, "^[A-Za-z0-9_]{3,32}$"))
                throw new InvalidOperationException("Account name: 3 to 32 letters, digits or _.");
            if (password.Length < 6)
                throw new InvalidOperationException("The password needs at least 6 characters.");
        }

        private async Task<object> LogIn(ServerEntry server, string account, string password, bool remember)
        {
            bool sameAccount = account.Equals(server.Account, StringComparison.OrdinalIgnoreCase);
            var (_, key, salt) = await new ServerApi(server).Login(account, password.Length > 0 ? password : null,
                sameAccount ? server.SavedKey : null, sameAccount ? server.SavedSalt : null);
            sessions[server.Id] = (account, key, salt);
            server.Account = account;
            (server.SavedKey, server.SavedSalt) = remember ? (key, salt) : ("", "");
            settings.Save();
            return State();
        }

        /// <summary>One long job at a time (verify, update, install, undo, validate), cancellable.</summary>
        private async Task<T> Long<T>(Func<CancellationToken, Task<T>> work)
        {
            if (busy != null)
                throw new InvalidOperationException("Another task is still running.");
            busy = new CancellationTokenSource();
            try
            {
                return await work(busy.Token);
            }
            finally
            {
                busy.Dispose();
                busy = null;
                Event("progress", new { phase = "idle", done = 0, total = 0, file = "" });
            }
        }

        private async Task Long(Func<CancellationToken, Task> work) => await Long(async c => { await work(c); return 0; });

        private DateTime lastReport;

        private void Report(string phase, long done, long total, string file)
        {
            // At most ~10 updates a second to the page.
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 100)
                return;
            lastReport = DateTime.UtcNow;
            Event("progress", new { phase, done, total, file });
        }

        private string? TakeNotice()
        {
            string? n = notice;
            notice = null;
            return n;
        }

        /// <summary>Everything the page shows: the game folder check and build, servers (without saved keys), packs.</summary>
        private object State()
        {
            bool folderOk = Game.IsGameFolder(Game.Folder);
            GameStore? s = folderOk ? Store() : null;
            return new
            {
                folder = Game.Folder,
                folderOk,
                build = folderOk ? Game.Build(Game.Folder) : "",
                steamBuild = Patching.SteamBuild,
                finalBuild = Patching.FinalBuild,
                selected = settings.Selected?.Id ?? "",
                servers = settings.Servers.Select(v => new
                {
                    id = v.Id, name = v.Name, address = v.Display, account = v.Account,
                    loggedIn = sessions.ContainsKey(v.Id) || v.SavedKey.Length > 0,
                    remembered = v.SavedKey.Length > 0
                }),
                autoPlay = settings.AutoPlay,
                locale = settings.Locale,
                allowServerUpdates = settings.AllowServerUpdates,
                allowFebruaryWeak = settings.AllowFebruaryWeak,
                advanced = settings.ConfigFor(Game.Folder)?.Advanced == true,
                // Can this game folder be the Steam copy a made pack's edits and patches start from (checked when used)?
                ownSteamCopy = s != null && s.Stack.Count == 0 && Game.Build(Game.Folder) == Patching.SteamBuild,
                busy = busy != null,
                packs = s?.Stack.Select(p => new
                {
                    p.Name, p.Version, p.Official, installedAt = p.InstalledAt.ToString("yyyy-MM-dd HH:mm"), p.Files, p.Deletes,
                    p.BackupBytes, p.Build, source = p.Source.Length > 0 ? p.SourceName : ""
                }).ToList() ?? [],
                cacheBytes = s?.CacheBytes() ?? 0,
                notice = TakeNotice()
            };
        }
    }
}
