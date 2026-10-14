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
                        (server.SavedKey, server.SavedSalt, server.AcceptedPatches) = ("", "", new());   // a login belongs to its server
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

                // ---- patching from the server, then playing
                case "update":
                {
                    if (!ServerPatches.Enabled)
                        throw new InvalidOperationException("Server patches are switched off in this LaunchPad: install the update from its .lmpatch file (Settings > Patch packs).");
                    ServerEntry server = Server(args);
                    GameStore s = Store();
                    if (Game.IsRunning())
                        throw new InvalidOperationException("Close Landmark first.");
                    var api = new ServerApi(server);
                    JsonElement info = await api.Info();
                    var listed = ServerPatches.FromInfo(info);
                    ServerPatches.Plan plan = ServerPatches.PlanFor(s, listed);
                    if (!plan.Needed)
                        return State();
                    if (plan.Install.Any(p => !server.AcceptedPatches.Contains(p.Sha256)))
                    {
                        if (!Bool(args, "accept"))
                            throw new InvalidOperationException("Accept this server's patches first.");
                        server.AcceptedPatches = listed.Select(p => p.Sha256).ToList();
                        settings.Save();
                    }
                    await Long(cancel => ServerPatches.Sync(s, api, server.Display, server.Name, plan, Report, cancel));
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
                    if (ServerPatches.Enabled && ServerPatches.PlanFor(s, ServerPatches.FromInfo(info)).Needed)
                        throw new InvalidOperationException("This server's patches aren't installed yet: update first.");
                    string required = info.TryGetProperty("requiredClientBuild", out JsonElement r) ? r.GetString() ?? "" : "";
                    string build = Game.Build(Game.Folder);
                    if (required.Length > 0 && required != build)
                        throw new InvalidOperationException($"This server needs game build {required}; yours is {build}. Install its patch pack (Settings > Patch packs).");
                    (string account, string key, string salt) = sessions.TryGetValue(server.Id, out var known) ? known
                        : server.SavedKey.Length > 0 ? (server.Account, server.SavedKey, server.SavedSalt)
                        : throw new InvalidOperationException("Log in first.");
                    // A fresh login for the game's session key (the saved key logs in without the password).
                    var (sessionKey, _, _) = await api.Login(account, null, key, salt);
                    int loginPort = info.TryGetProperty("loginPort", out JsonElement lp) && lp.TryGetInt32(out int p) && p > 0 ? p : 20042;
                    Game.PointAt(Game.Folder, $"{server.Address}:{loginPort}", MarketplaceUrl(server, info));
                    Process started = Game.Start(Game.Folder, sessionKey, settings.Locale);
                    started.EnableRaisingEvents = true;
                    // BeginInvoke, not Invoke: Process raises Exited holding a lock on itself, and waiting here for the
                    // window thread (which then reads the process) deadlocked the launcher.
                    started.Exited += (_, _) => window.Dispatcher.BeginInvoke(() => GameExited(started));
                    game = started;
                    window.WindowState = WindowState.Minimized;
                    return null;
                }
                case "setOption":
                {
                    if (args?["autoPlay"] != null)
                        settings.AutoPlay = Bool(args, "autoPlay");
                    if (args?["locale"] != null)
                        settings.Locale = Str(args, "locale");
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
                    return DescribePack(dialog.FileName);
                }
                case "dropPack":
                    return DescribePack(files.FirstOrDefault() ?? throw new InvalidOperationException("Drop the patch pack (.lmpatch) on the window."));
                case "installPack":
                {
                    GameStore s = Store();
                    string path = Str(args, "path");
                    await Long(cancel => Task.Run(() =>
                    {
                        using var pack = new Patching.Pack(path);
                        return s.Install(pack, "", "", filesChecked: false, Report, cancel);
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

        /// <summary>What playing on a server needs patched, for the page (and whether the player already agreed).</summary>
        private object PatchSummary(ServerEntry server, JsonElement info)
        {
            if (!Game.IsGameFolder(Game.Folder) || !ServerPatches.Enabled)
                return new { needed = false };
            ServerPatches.Plan plan = ServerPatches.PlanFor(Store(), ServerPatches.FromInfo(info));
            return new
            {
                needed = plan.Needed,
                accepted = plan.Install.All(p => server.AcceptedPatches.Contains(p.Sha256)),
                undo = plan.Undo.Select(p => p.Name),
                install = plan.Install.Select(p => new { p.Name, p.Version, p.Description, p.Files, p.Bytes, p.Delete })
            };
        }

        /// <summary>A chosen or dropped pack (an .lmpatch).</summary>
        private static object DescribePack(string path)
        {
            if (!path.EndsWith(PatchFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Patch packs are {PatchFile.Extension} files.");
            return new { path, info = Patching.Describe(path, Game.Folder) };
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
