using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Network;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A small web server for the client's embedded browser (ClientConfig [Marketplace] MarketplaceUrl, e.g.
    /// http://127.0.0.1:19001). Without an answer the browser reloads forever with backoff and logs in to the retired
    /// official store each time, which has frozen the client.
    /// <list type="bullet">
    /// <item>GET / (any path without an extension): the Showcase page (Resources/Web/index.html: the store and the
    /// build site gallery, see <see cref="Store"/>). Routes are the client's own hash routes (#!/marketplace,
    /// #!/gallery, #!/marketplace/search/...SearchTags=...).</item>
    /// <item>GET /web/&lt;file&gt;: the page's script and style sheet.</item>
    /// <item>GET /icons/&lt;image set id&gt;.png: icons exported from the owner's game folder into GameDataPath/icons (Game/IconExport.cs, the Docker "icons" step).</item>
    /// <item>Admins only (<see cref="AdminRequest"/>: opened as /admin?t=token from the game, then by an HttpOnly cookie,
    /// <see cref="AdminSessions"/>): GET /admin (the admin menu, <see cref="Admin"/>), GET /admin/monitor and GET
    /// /api/admin/monitor (the server monitor, <see cref="AdminMonitor"/>: read-only, built on the world thread), GET
    /// /admin/settings and /api/admin/settings… (<see cref="ServerSettingsPage"/>), POST /api/admin/players, psqueue, run.</item>
    /// <item>JSON API, all with the player's token (t=...): GET /api/me, GET /api/catalogue, GET /api/gallery (published
    /// + tagged sites with tags, likes, Play Mode, listing time, screenshot, comment count, contests; /api/me also gives the
    /// player's Lumens and whether the store is free, /api/catalogue each item's price in Lumens), GET
    /// /api/gallery/tags (the three drop-down lists), GET /api/gallery/comments (site), POST /api/gallery/comment (site,
    /// text), POST /api/gallery/uncomment (id), GET /api/follows (the Following tab: followed creators and tag groups,
    /// <see cref="ShowcaseFollow"/>), POST /api/follow (creator name, or tags) and /api/unfollow (creator id, or tags), POST /api/take (item, count), GET /api/studio (listed Player Studio designs
    /// with their price in Lumens), POST /api/studio/take (product: buys one), POST /api/visit (site). Requests that
    /// change the game run on the world thread (<see cref="Update"/>).</item>
    /// <item>GET /uploads/lm/images/templates[N]/(small|large)/(design guid as 24 digits, "/" every 3).jpg: design
    /// screenshots, for clients whose ClientConfig MarketplaceAssets points here (URL built by FUN_143ad69a0, Lua
    /// Templates.GetTemplateUrl).</item>
    /// </list>
    /// Listens on WorldServer.json "WebPagesHost" (default loopback only) port <see cref="Port"/>.
    /// </summary>
    public static class WebPages
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The web pages' TCP port (WorldServer.json WebPagesPort, default 19001; each world container its own).</summary>
        public static int Port => Shared.Configuration.SharedConfiguration.Configuration?.GetValue("WebPagesPort", 19001) ?? 19001;
        private const int MaxBody = 4096;
        private static TcpListener listener;

        private static readonly Regex TemplateImage = new(@"^/uploads/lm/images/templates(\d*)/(small|large)/([0-9/]+)\.jpg$");
        private static readonly Regex SiteImage = new(@"^/uploads/lm/images/claims(\d*)/(small|large)/([0-9/]+)\.jpg$");
        private static readonly Regex IconPath = new(@"^/icons/(\d+)\.png$");
        private static readonly Regex WebFile = new(@"^/web/([A-Za-z0-9_\-]+\.(js|css|png))$");

        /// <summary>Work the web threads hand to the world thread (game state is only changed there).</summary>
        private static readonly ConcurrentQueue<Action> worldQueue = new();

        private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>
        /// Starts listening; a failure (port in use) is logged and the server carries on.
        /// </summary>
        public static void Start()
        {
            try
            {
                string host = SharedConfiguration.Configuration?.GetValue<string>("WebPagesHost");
                IPAddress address = string.IsNullOrEmpty(host) ? IPAddress.Loopback : IPAddress.Parse(host);
                listener = new TcpListener(address, Port);
                listener.Start();
                Task.Run(AcceptLoop);
                log.Info($"Web pages on http://{address}:{Port}/ (players' browsers use {Store.BaseUrl}).");
            }
            catch (Exception e)
            {
                log.Warn($"Could not start the web page listener on port {Port}: {e.Message}");
            }
        }

        /// <summary>World thread: runs the work web requests queued.</summary>
        public static void Update()
        {
            while (worldQueue.TryDequeue(out Action action))
                action();
        }

        /// <summary>Runs <paramref name="work"/> on the world thread and waits (up to 10 s) for its result.</summary>
        private static async Task<T> OnWorldThread<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            worldQueue.Enqueue(() =>
            {
                try
                {
                    done.SetResult(work());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            });
            Task finished = await Task.WhenAny(done.Task, Task.Delay(10000));
            if (finished != done.Task)
                throw new TimeoutException("The world thread did not answer.");
            return await done.Task;
        }

        private static async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }
                // Beyond MaxConnections at once the connection is closed at once (the port is public in Docker); a page
                // load makes a handful, so only a flood reaches it.
                if (System.Threading.Interlocked.Increment(ref openConnections) > MaxConnections)
                {
                    System.Threading.Interlocked.Decrement(ref openConnections);
                    client.Dispose();
                    continue;
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Serve(client);
                    }
                    finally
                    {
                        System.Threading.Interlocked.Decrement(ref openConnections);
                    }
                });
            }
        }

        /// <summary>Connections being served now, and the most served at once (<see cref="AcceptLoop"/>).</summary>
        private static int openConnections;
        private const int MaxConnections = 256;

        /// <summary>
        /// Portrait uploads read at once. The body buffer is allocated from the Content-Length header before any of the
        /// body arrives (up to <see cref="MaxUpload"/>, 6 MB), and the request may take <see cref="RequestDeadline"/> to
        /// come in. Until 2026-10-18 any number of connections could each send just that header and hold 6 MB for 30 s
        /// (a thousand of them: 6 GB). Clients post one portrait when they take it, so a few at a time is plenty; the
        /// rest are answered 503 (the client's upload fails as when the server is away).
        /// </summary>
        private static readonly System.Threading.SemaphoreSlim uploadSlots = new(MaxUploadsAtOnce);
        private const int MaxUploadsAtOnce = 4;

        private record Response(int Status, string Type, byte[] Body, string Cache = "no-cache", string[] Headers = null);

        /// <summary>Reads one HTTP request, answers it and closes the connection.</summary>
        private static async Task Serve(TcpClient client)
        {
            using (client)
            {
                string requestLine = "";
                bool uploading = false;
                try
                {
                    // The whole request must arrive within RequestDeadline. ReceiveTimeout (set before) only applies to
                    // blocking reads, not to these ReadAsync calls: a connection that sent nothing (or a byte now and
                    // then) was held open forever, and the port is public (Docker WebPagesHost 0.0.0.0) — enough idle
                    // connections would use up the process's sockets, its database connections included.
                    using var deadline = new System.Threading.CancellationTokenSource(RequestDeadline);
                    System.Threading.CancellationToken cancel = deadline.Token;
                    NetworkStream stream = client.GetStream();
                    // Headers byte by byte up to the blank line (bodies can be binary: portrait uploads).
                    var headerBytes = new List<byte>();
                    var one = new byte[1];
                    while (headerBytes.Count < MaxHeader)
                    {
                        if (await stream.ReadAsync(one, cancel) == 0)
                            break;
                        headerBytes.Add(one[0]);
                        int n = headerBytes.Count;
                        if (n >= 4 && headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 && headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10)
                            break;
                    }
                    string[] headerLines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
                    requestLine = headerLines[0];
                    int length = 0;
                    string contentType = "", origin = null, host = null, cookie = null;
                    foreach (string line in headerLines.Skip(1))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line[15..].Trim(), out length);
                        else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                            contentType = line[13..].Trim();
                        else if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                            origin = line[7..].Trim();
                        else if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                            host = line[5..].Trim();
                        else if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
                            cookie = line[7..].Trim();
                    }
                    string[] parts = requestLine.Split(' ');
                    string method = parts.Length > 0 ? parts[0] : "GET";
                    string target = parts.Length > 1 ? parts[1] : "/";
                    int q = target.IndexOf('?');
                    string path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
                    bool upload = path == PortraitUploadPath;
                    if (upload)
                    {
                        if (!uploadSlots.Wait(0))
                        {
                            await WriteResponse(stream, new Response(503, "text/plain", Encoding.ASCII.GetBytes("busy")));
                            log.Debug($"Portrait upload refused: {MaxUploadsAtOnce} already being read.");
                            return;
                        }
                        uploading = true;
                    }
                    // The settings page's save takes more than the 4 KB of the other requests; a body over its limit is
                    // refused rather than cut (a cut save would apply only part of the changes).
                    bool settingsSave = method == "POST" && path == ServerSettingsPage.SavePath;
                    if (settingsSave && length > ServerSettingsPage.MaxSaveBody)
                    {
                        await WriteResponse(stream, Json(new { ok = false, message = $"Request too large (at most {ServerSettingsPage.MaxSaveBody / 1024} KB)." }, 413));
                        return;
                    }
                    byte[] bodyBytes = Array.Empty<byte>();
                    if (length > 0)
                    {
                        bodyBytes = new byte[Math.Min(length, upload ? MaxUpload : settingsSave ? ServerSettingsPage.MaxSaveBody : MaxBody)];
                        int read = 0;
                        while (read < bodyBytes.Length)
                        {
                            int n = await stream.ReadAsync(bodyBytes.AsMemory(read), cancel);
                            if (n == 0)
                                break;
                            read += n;
                        }
                        if (read < bodyBytes.Length)
                            Array.Resize(ref bodyBytes, read);
                    }
                    var query = ParseForm(q < 0 ? "" : target[(q + 1)..]);
                    if (method == "POST" && !upload)
                        foreach (var kv in ParseForm(Encoding.UTF8.GetString(bodyBytes)))
                            query[kv.Key] = kv.Value;
                    if (upload)
                    {
                        Response uploaded = await PortraitUpload(contentType, bodyBytes);
                        await WriteResponse(stream, uploaded);
                        log.Info($"Portrait upload ({bodyBytes.Length} bytes): {Encoding.UTF8.GetString(uploaded.Body)}");
                        return;
                    }

                    Response response = await Route(method, path, query,
                        new RequestInfo(origin, host, length <= 0 || length == bodyBytes.Length, cookie));
                    await WriteResponse(stream, response);
                    log.Debug($"Web page served: {Redact(requestLine)} -> {response.Status}");
                }
                catch (Exception e)
                {
                    log.Debug($"Web page request failed ({Redact(requestLine)}): {e.Message}");
                }
                finally
                {
                    if (uploading)
                        uploadSlots.Release();
                }
            }
        }

        /// <summary>A request line for the log, without the player's token (t=… names a signed-in player).</summary>
        internal static string Redact(string requestLine) => Regex.Replace(requestLine ?? "", @"([?&]t=)[^&\s]*", "$1…");

        private static async Task WriteResponse(NetworkStream stream, Response response)
        {
            string status = response.Status switch
            {
                200 => "OK", 302 => "Found", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed",
                409 => "Conflict", 413 => "Payload Too Large", 503 => "Service Unavailable", _ => "Error"
            };
            string extra = response.Headers == null ? "" : string.Concat(response.Headers.Select(h => h + "\r\n"));
            string head = $"HTTP/1.1 {response.Status} {status}\r\n"
                + $"Content-Type: {response.Type}\r\nContent-Length: {response.Body.Length}\r\n"
                + $"Cache-Control: {response.Cache}\r\n{extra}Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(response.Body);
        }

        /// <summary>Where clients post their own portraits (ClientConfig [Portrait] UploadUrl = this path on the web server).</summary>
        public const string PortraitUploadPath = "/portrait/upload";
        private const int MaxHeader = 16 * 1024, MaxUpload = 6 * 1024 * 1024;
        /// <summary>How long a client may take to send its whole request (a 6 MB portrait upload included).</summary>
        private static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The client's portrait upload (ScreenshotManager::PostPortraitToHttp): multipart/form-data with imageType
        /// "portrait", characterId, thumbnailFile (headshot.png) and imageFile (portrait.png). See <see cref="Portraits.Upload"/>;
        /// checked and kept by the commerce back end (in-process, or the commerce service: ICommerceService.PortraitUpload).
        /// </summary>
        private static async Task<Response> PortraitUpload(string contentType, byte[] body)
        {
            int b = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (b < 0)
                return new Response(400, "text/plain", Encoding.ASCII.GetBytes("no boundary"));
            byte[] boundary = Encoding.ASCII.GetBytes("--" + contentType[(b + 9)..].Trim('"', ' '));
            var fields = new Dictionary<string, byte[]>();
            int pos = IndexOf(body, boundary, 0);
            while (pos >= 0)
            {
                int start = pos + boundary.Length;
                int next = IndexOf(body, boundary, start);
                if (next < 0)
                    break;
                int headerEnd = IndexOf(body, new byte[] { 13, 10, 13, 10 }, start);
                if (headerEnd > 0 && headerEnd < next)
                {
                    string headers = Encoding.ASCII.GetString(body, start, headerEnd - start);
                    var name = Regex.Match(headers, "name=\"([^\"]+)\"");
                    int dataStart = headerEnd + 4, dataEnd = next - 2;   // the part ends with CRLF before the boundary
                    if (name.Success && dataEnd >= dataStart)
                        fields[name.Groups[1].Value] = body[dataStart..dataEnd];
                }
                pos = next;
            }
            ulong.TryParse(Encoding.ASCII.GetString(fields.GetValueOrDefault("characterId") ?? Array.Empty<byte>()), out ulong character);
            (bool ok, string message) = await WorldChannel.WorldChannelServices.Commerce.PortraitUpload(character,
                fields.GetValueOrDefault("imageFile"), fields.GetValueOrDefault("thumbnailFile"));
            return new Response(ok ? 200 : 400, "text/plain", Encoding.ASCII.GetBytes(message));
        }

        private static int IndexOf(byte[] data, byte[] pattern, int from)
        {
            for (int i = Math.Max(0, from); i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }

        private static Dictionary<string, string> ParseForm(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (string pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                string value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                result[key] = value;
            }
            return result;
        }

        private static Response Json(object value, int status = 200)
            => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, json));

        private static readonly Response NotFound = new(404, "text/plain", Encoding.ASCII.GetBytes("Not found"));

        private static readonly object monitorSync = new();
        private static DateTime monitorAt;
        private static byte[] monitorBody;

        /// <summary>
        /// GET /api/admin/monitor (admins only, by the admin cookie): the admin monitor snapshot (<see cref="AdminMonitor.Snapshot"/>), built
        /// on the world thread and shared for 2 s, so several open pages cost one build per interval. Read-only.
        /// </summary>
        private static async Task<Response> MonitorJson()
        {
            byte[] body;
            lock (monitorSync)
                body = DateTime.UtcNow - monitorAt < TimeSpan.FromSeconds(2) ? monitorBody : null;
            if (body == null)
            {
                body = JsonSerializer.SerializeToUtf8Bytes(await OnWorldThread(AdminMonitor.Snapshot), json);
                lock (monitorSync)
                {
                    monitorBody = body;
                    monitorAt = DateTime.UtcNow;
                }
            }
            return new Response(200, "application/json; charset=utf-8", body);
        }

        /// <summary>What a request carries beyond its form: the Origin and Host headers, and whether its whole body arrived.</summary>
        private sealed record RequestInfo(string Origin, string Host, bool BodyComplete, string Cookie = null);

        /// <summary>The admin pages: opened from the game as page?t=token once, then by the admin cookie (<see cref="AdminSessions"/>).</summary>
        private static readonly string[] AdminPages = { "/admin", "/admin/monitor", ServerSettingsPage.PagePath };

        /// <summary>
        /// The admin pages and /api/admin/… (<see cref="AdminSessions"/>). A GET of a page with ?t= (the in-game /admin
        /// command's URL) exchanges the admin's Showcase token for an HttpOnly, SameSite=Strict cookie and redirects to the
        /// page without the token; everything else is checked by that cookie only (a URL token is not accepted here). Every
        /// POST also needs the page's anti-forgery token (bound to the cookie's session) and, when the browser sends one,
        /// this server's Origin. Admins only throughout; game work runs on the world thread.
        /// </summary>
        private static async Task<Response> AdminRequest(string method, string path, Dictionary<string, string> query, RequestInfo info)
        {
            DateTime now = DateTime.UtcNow;
            string token = query.GetValueOrDefault("t");
            bool page = AdminPages.Contains(path);
            if (page && method == "GET" && !string.IsNullOrEmpty(token))
            {
                var exchange = AdminSessions.Instance.Exchange(token, path, Port, t => Admin.IsAdmin(Store.SessionOf(t)), now);
                if (exchange is not { } signedIn)
                    return new Response(403, "text/plain", Encoding.ASCII.GetBytes("Admins only."));
                log.Info($"Admin {Store.SessionOf(token)?.CharacterName} opened the admin pages (web login for {AdminSessions.Lifetime.TotalMinutes:0} min).");
                return new Response(302, "text/plain", Encoding.ASCII.GetBytes("Signed in."), "no-store",
                    new[] { "Location: " + signedIn.Location, signedIn.Cookie });
            }
            string session = AdminSessions.ReadCookie(info.Cookie, AdminSessions.CookieName(Port));
            WorldSession admin = Store.SessionOf(AdminSessions.Instance.TokenOf(session, now));
            if (!Admin.IsAdmin(admin))
                admin = null;
            if (page)
            {
                if (admin == null)
                    return new Response(403, "text/plain", Encoding.UTF8.GetBytes("Admins only: type /admin in the game to open the admin pages "
                        + $"(the login lasts {AdminSessions.Lifetime.TotalMinutes:0} minutes while you are in the world; the browser must keep cookies)."));
                string html = path == "/admin" ? Admin.Page : path == "/admin/monitor" ? AdminMonitorPage.Html : ServerSettingsPage.Html;
                return new Response(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html.Replace("__CSRF__", ServerSettingsPage.CsrfToken(session))),
                    "no-store");
            }
            if (admin == null)
                return Json(new { ok = false, message = "Admins only: type /admin in the game to sign in again." }, 403);
            if (path == "/api/admin/monitor")
                return method == "GET" ? await MonitorJson() : Json(new { ok = false, message = "Use GET." }, 405);
            if (path.StartsWith(ServerSettingsPage.ApiPath, StringComparison.Ordinal))
            {
                ServerSettingsPage.Result result = ServerSettingsPage.Handle(method, path, query, info.Origin, info.Host, info.BodyComplete,
                    session, admin.CharacterName, ServerSettings.Store);
                if (result.WorldWork != null)
                    await OnWorldThread(() =>
                    {
                        result.WorldWork();
                        return true;
                    });
                return result.Html != null
                    ? new Response(result.Status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(result.Html))
                    : Json(result.Body, result.Status);
            }
            if (method != "POST")
                return Json(new { ok = false, message = "Use POST." }, 405);
            if (!ServerSettingsPage.OriginAllowed(info.Origin, info.Host) || !ServerSettingsPage.CsrfValid(session, query.GetValueOrDefault("csrf")))
            {
                log.Warn($"Admin page request {path} by {admin.CharacterName} refused: wrong anti-forgery token or Origin {info.Origin}.");
                return Json(new { ok = false, message = "Refused: the page's security token is missing or old. Reload the page." }, 403);
            }
            switch (path)
            {
                case "/api/admin/players":
                    return Json(new { ok = true, players = Admin.Players() });
                case "/api/admin/psqueue":
                {
                    // Player Studio products the peers rejected, waiting for staff (Commerce "Live's review rules"; P1210).
                    var queue = await await OnWorldThread(() => WorldChannel.WorldChannelServices.Commerce.StaffQueue());
                    return Json(new { ok = true, products = queue.Select(p => new { id = p.Product.ToString(), name = p.Name,
                        description = p.Description, tags = p.Tags, creator = p.Creator, price = p.Price, approvals = p.Approvals,
                        rejections = p.Rejections, created = p.Created.ToString("yyyy-MM-dd HH:mm"), reasons = p.Reasons ?? "" }).ToList() });
                }
                case "/api/admin/run":
                {
                    var (ok, message) = Admin.Run(admin, query.GetValueOrDefault("target"), query.GetValueOrDefault("cmd"));
                    return Json(new { ok, message }, ok ? 200 : 403);
                }
            }
            return Json(new { ok = false, message = "Not found." }, 404);
        }

        private static async Task<Response> Route(string method, string path, Dictionary<string, string> query, RequestInfo info)
        {
            Match match;
            if ((match = TemplateImage.Match(path)).Success)
            {
                int index = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 0;
                ulong.TryParse(match.Groups[3].Value.Replace("/", ""), out ulong design);
                byte[] image = Templates.Image(design, index, match.Groups[2].Value);
                return image == null ? NotFound : new Response(200, "image/jpeg", image, "max-age=86400");
            }
            if ((match = SiteImage.Match(path)).Success)
            {
                int index = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 0;
                ulong.TryParse(match.Groups[3].Value.Replace("/", ""), out ulong site);
                byte[] image = SiteScreenshots.Image(site, index, match.Groups[2].Value);
                return image == null ? NotFound : new Response(200, "image/jpeg", image, "max-age=300");
            }
            if ((match = IconPath.Match(path)).Success)
            {
                string file = GameDataFiles.PathOf(Path.Combine("icons", match.Groups[1].Value + ".png"));
                return File.Exists(file) ? new Response(200, "image/png", await File.ReadAllBytesAsync(file), "max-age=604800") : NotFound;
            }
            if ((match = WebFile.Match(path)).Success)
            {
                string file = Path.Combine(AppContext.BaseDirectory, "Resources", "Web", match.Groups[1].Value);
                string type = match.Groups[2].Value switch { "js" => "text/javascript; charset=utf-8", "css" => "text/css; charset=utf-8", _ => "image/png" };
                return File.Exists(file) ? new Response(200, type, await File.ReadAllBytesAsync(file)) : NotFound;
            }
            if (AdminPages.Contains(path) || path.StartsWith("/api/admin/", StringComparison.Ordinal))
                return await AdminRequest(method, path, query, info);
            if (path.StartsWith("/api/", StringComparison.Ordinal))
                return await Api(method, path[5..], query);
            if (Path.HasExtension(path))
                return NotFound;
            string page = Path.Combine(AppContext.BaseDirectory, "Resources", "Web", "index.html");
            return File.Exists(page)
                ? new Response(200, "text/html; charset=utf-8", await File.ReadAllBytesAsync(page))
                : new Response(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes("<!doctype html><title>Landmark</title><p>Showcase page missing (Resources/Web/index.html).</p>"));
        }

        /// <summary>
        /// "My vendors": the player's placed vendors with their stock, and the bag items that could be listed. Guids
        /// travel as strings (JavaScript numbers lose digits above 2^53).
        /// </summary>
        private static object MyVendors(WorldSession session)
        {
            var defs = Inventory.ItemDefinitionManager.Get;
            object Item(uint definition) => new { item = definition, name = Vendors.ItemName(definition), icon = defs(definition)?.ImageSetId ?? 0u };
            var vendors = Vendors.VendorsOf(session).Select(v => new
            {
                id = v.Vendor.ToString(),
                landscape = Shared.GameTable.Text.TextManager.Instance.GetTextForId(Landscapes.Get(v.Zone)?.NameText ?? 0),
                stock = v.Stock.Select(l => new { id = l.Id.ToString(), info = Item(l.ItemDefinitionId), count = l.Count, price = l.Price, fee = l.FeePaid })
            }).ToList();
            var bag = session.Inventory.Items
                .Where(i => i.Container == Inventory.Inventory.BagContainer && i.ItemDefinitionId != Inventory.Inventory.CurrencyItem)
                .OrderBy(i => i.Slot)
                .Select(i => new { guid = i.Id.ToString(), info = Item(i.ItemDefinitionId), count = i.Count }).ToList();
            return new { coins = session.Coins, fee = Vendors.FeeRate, vendors, bag };
        }

        private static async Task<Response> Api(string method, string name, Dictionary<string, string> query)
        {
            WorldSession session = Store.SessionOf(query.GetValueOrDefault("t"));
            switch (name)
            {
                case "catalogue":
                    // price: Lumens (0 = free); priceNote: where the price comes from (Store.MarketplaceProducts).
                    return Json(Store.Catalogue().Select(i => new { i.Id, i.Name, i.Description, i.Icon, i.Section, i.Group, i.Price, i.PriceNote }));
                case "gallery":
                    return Json(await OnWorldThread(Store.Gallery));
                case "gallery/tags":
                {
                    // The three drop-downs of Build Site Management, for the page's tag search (TagDefinitions.txt).
                    List<string>[] lists = Gallery.Categories();
                    return Json(new { genre = lists[0], built = lists[1], activity = lists[2] });
                }
                case "gallery/comments":
                {
                    ulong.TryParse(query.GetValueOrDefault("site"), out ulong site);
                    return Json(Gallery.Comments(site).Select(c => new
                    {
                        id = c.Id.ToString(), c.Author, c.Text, c.At,
                        mine = session != null && c.AuthorGuid == session.PlayerGuid,
                        removable = session != null && (c.AuthorGuid == session.PlayerGuid || Claims.OwnerOf(site) == session.PlayerGuid || Admin.IsAdmin(session))
                    }));
                }
                case "studio":
                    // The commerce back end's list (in-process, or the commerce service's answer; asked from the world thread).
                    // price: the Lumens a buyer pays (Commerce.StudioPrice; 0 = free); creatorPrice: the price as the creator set
                    // it in the client's submission window (Daybreak Cash on live).
                    return Json((await await OnWorldThread(() => WorldChannel.WorldChannelServices.Commerce.ListedDesigns())).Select(d => new
                    {
                        id = d.Product.ToString(), d.Name, d.Description, d.Tags, d.Author, d.Voxels,
                        image = Templates.HasImage(d.Template) ? Templates.ImagePath(d.Template) : "",
                        price = Commerce.StudioPrice(d.Price), creatorPrice = d.Price
                    }));
                case "follows":
                {
                    // The Following tab (live: My Store → Start Following, Update Notes 27 Aug 2014; Game.ShowcaseFollow): the
                    // creators (ids as strings) and tag groups this player follows, and whether the daily mail is on — from
                    // the commerce back end (in-process: this world's database; with COMMERCE_ADDRESS: the commerce service).
                    if (session == null)
                        return Json(new { available = false, digest = false, creators = Array.Empty<object>(), tags = Array.Empty<string>() });
                    ShowcaseFollow.FollowList list = await WorldChannel.WorldChannelServices.Commerce.FollowList(session);
                    return Json(new
                    {
                        available = list.Available, digest = list.DigestOn, message = list.Message,
                        creators = list.Creators.Select(c => new { id = c.Id.ToString(), name = c.Name }),
                        tags = list.Tags,
                        maxCreators = ShowcaseFollow.MaxCreators, maxTags = ShowcaseFollow.MaxTagGroups,
                        maxWords = ShowcaseFollow.MaxWordsPerGroup
                    });
                }
                case "me":
                {
                    if (session != null)
                        Store.Confirm(session);
                    // The player's Lumens (read on the world thread, where the bag changes) and whether the store is free.
                    uint lumens = session == null ? 0 : await OnWorldThread(() => session.Coins);
                    // memberDiscount: the percent off this player's prices (members and trials; Membership.PriceFor).
                    uint memberDiscount = session != null && Membership.IsMember(session) ? Membership.DiscountPercent : 0;
                    return Json(new { signedIn = session != null, name = session?.CharacterName ?? "", lumens, freeStore = Store.IsFree,
                        memberDiscount, vendorShops = Vendors.Enabled });
                }
            }
            if (method != "POST" && name != "myvendors")
                return Json(new { ok = false, message = "Use POST." }, 405);
            if (session == null)
                return Json(new { ok = false, message = "Open the Showcase from the game to sign in." }, 403);
            switch (name)
            {
                case "take":
                {
                    uint.TryParse(query.GetValueOrDefault("item"), out uint item);
                    if (!uint.TryParse(query.GetValueOrDefault("count"), out uint count))
                        count = 1;
                    (bool ok, string message, uint lumens) = await OnWorldThread(() =>
                    {
                        (bool taken, string text) = Store.Take(session, item, count);
                        return (taken, text, session.Coins);
                    });
                    return Json(new { ok, message, lumens });
                }
                case "myvendors":
                    if (!Vendors.Enabled)
                        return Json(new { vendors = Array.Empty<object>(), bag = Array.Empty<object>(), coins = 0, fee = Vendors.FeeRate, disabled = true });
                    return Json(await OnWorldThread(() => MyVendors(session)));
                case "studio/take":
                {
                    ulong.TryParse(query.GetValueOrDefault("product"), out ulong product);
                    (bool ok, string message) = await await OnWorldThread(() => WorldChannel.WorldChannelServices.Commerce.TakeDesign(session, product));
                    // The player's Lumens after the purchase (read on the world thread, where the bag changes).
                    uint lumens = await OnWorldThread(() => session.Coins);
                    return Json(new { ok, message, lumens });
                }
                case "vendor/list":
                {
                    ulong.TryParse(query.GetValueOrDefault("vendor"), out ulong vendor);
                    ulong.TryParse(query.GetValueOrDefault("item"), out ulong item);
                    uint.TryParse(query.GetValueOrDefault("count"), out uint count);
                    uint.TryParse(query.GetValueOrDefault("price"), out uint price);
                    (uint result, uint fee, uint listed) = await OnWorldThread(() => Vendors.List(session, vendor, item, count, price));
                    string message = result switch
                    {
                        Vendors.Success => $"Listed {listed} at {price} coins each (fee {fee} coins).",
                        Vendors.NotEnoughCoins => $"The listing fee is {fee} coins: not enough coins.",
                        Vendors.NoSuchItem => "That item isn't in your bag any more.",
                        _ => "That isn't your vendor."
                    };
                    return Json(new { ok = result == Vendors.Success, message });
                }
                case "vendor/price":
                {
                    ulong.TryParse(query.GetValueOrDefault("listing"), out ulong listing);
                    uint.TryParse(query.GetValueOrDefault("price"), out uint price);
                    (uint result, _) = await OnWorldThread(() => Vendors.SetPrice(session, listing, price));
                    return Json(new { ok = result == Vendors.Success, message = result == Vendors.Success ? $"Price set to {price} coins." : "That listing is gone." });
                }
                case "vendor/unlist":
                {
                    ulong.TryParse(query.GetValueOrDefault("listing"), out ulong listing);
                    (bool ok, string message) = await OnWorldThread(() => Vendors.Unlist(session, listing));
                    return Json(new { ok, message });
                }
                case "visit":
                {
                    ulong.TryParse(query.GetValueOrDefault("site"), out ulong site);
                    (bool ok, string message) = await OnWorldThread(() => Store.Visit(session, site));
                    return Json(new { ok, message });
                }
                case "follow":
                case "unfollow":
                {
                    // POST follow (creator = a character name, or tags = words) / unfollow (creator = id, or tags = the group).
                    // Database / commerce service only (no game state), so on this thread.
                    string creator = query.GetValueOrDefault("creator"), tags = query.GetValueOrDefault("tags");
                    if (name == "follow")
                    {
                        (bool ok, string message) = await WorldChannel.WorldChannelServices.Commerce.Follow(session, creator, tags);
                        return Json(new { ok, message });
                    }
                    if (!string.IsNullOrWhiteSpace(creator) && (!ulong.TryParse(creator, out ulong id) || id == 0))
                        return Json(new { ok = false, message = "That wasn't followed." });
                    ulong.TryParse(creator, out ulong creatorId);
                    (bool done, string text) = await WorldChannel.WorldChannelServices.Commerce.Unfollow(session, creatorId, tags);
                    return Json(new { ok = done, message = text });
                }
                case "gallery/comment":
                {
                    ulong.TryParse(query.GetValueOrDefault("site"), out ulong site);
                    (bool ok, string message) = await OnWorldThread(() => Gallery.AddComment(session, site, query.GetValueOrDefault("text")));
                    return Json(new { ok, message });
                }
                case "gallery/uncomment":
                {
                    ulong.TryParse(query.GetValueOrDefault("id"), out ulong id);
                    (bool ok, string message) = await OnWorldThread(() => Gallery.DeleteComment(session, id));
                    return Json(new { ok, message });
                }
            }
            return NotFound;
        }
    }
}
