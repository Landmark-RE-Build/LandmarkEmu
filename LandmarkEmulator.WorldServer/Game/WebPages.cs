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
    /// <item>GET /icons/&lt;image set id&gt;.png: icons exported by re/tools/export_icons.py into GameDataPath/icons.</item>
    /// <item>JSON API, all with the player's token (t=...): GET /api/me, GET /api/catalogue, GET /api/gallery,
    /// POST /api/take (item, count), POST /api/visit (site). Requests that change the game run on the world thread
    /// (<see cref="Update"/>).</item>
    /// <item>GET /uploads/lm/images/templates[N]/(small|large)/(design guid as 24 digits, "/" every 3).jpg: design
    /// screenshots, for clients whose ClientConfig MarketplaceAssets points here (URL built by FUN_143ad69a0, Lua
    /// Templates.GetTemplateUrl).</item>
    /// </list>
    /// Listens on WorldServer.json "WebPagesHost" (default loopback only) port <see cref="Port"/>.
    /// </summary>
    public static class WebPages
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int Port = 19001;
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
                _ = Task.Run(() => Serve(client));
            }
        }

        private record Response(int Status, string Type, byte[] Body, string Cache = "no-cache");

        /// <summary>Reads one HTTP request, answers it and closes the connection.</summary>
        private static async Task Serve(TcpClient client)
        {
            using (client)
            {
                string requestLine = "";
                try
                {
                    client.ReceiveTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    // Headers byte by byte up to the blank line (bodies can be binary: portrait uploads).
                    var headerBytes = new List<byte>();
                    var one = new byte[1];
                    while (headerBytes.Count < MaxHeader)
                    {
                        if (await stream.ReadAsync(one) == 0)
                            break;
                        headerBytes.Add(one[0]);
                        int n = headerBytes.Count;
                        if (n >= 4 && headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 && headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10)
                            break;
                    }
                    string[] headerLines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
                    requestLine = headerLines[0];
                    int length = 0;
                    string contentType = "";
                    foreach (string line in headerLines.Skip(1))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line[15..].Trim(), out length);
                        else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                            contentType = line[13..].Trim();
                    }
                    string[] parts = requestLine.Split(' ');
                    string method = parts.Length > 0 ? parts[0] : "GET";
                    string target = parts.Length > 1 ? parts[1] : "/";
                    int q = target.IndexOf('?');
                    string path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
                    bool upload = path == PortraitUploadPath;
                    byte[] bodyBytes = Array.Empty<byte>();
                    if (length > 0)
                    {
                        bodyBytes = new byte[Math.Min(length, upload ? MaxUpload : MaxBody)];
                        int read = 0;
                        while (read < bodyBytes.Length)
                        {
                            int n = await stream.ReadAsync(bodyBytes.AsMemory(read));
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
                        Response uploaded = PortraitUpload(contentType, bodyBytes);
                        await WriteResponse(stream, uploaded);
                        log.Info($"Portrait upload ({bodyBytes.Length} bytes): {Encoding.UTF8.GetString(uploaded.Body)}");
                        return;
                    }

                    Response response = await Route(method, path, query);
                    await WriteResponse(stream, response);
                    log.Debug($"Web page served: {requestLine} -> {response.Status}");
                }
                catch (Exception e)
                {
                    log.Debug($"Web page request failed ({requestLine}): {e.Message}");
                }
            }
        }

        private static async Task WriteResponse(NetworkStream stream, Response response)
        {
            string head = $"HTTP/1.1 {response.Status} {(response.Status == 200 ? "OK" : response.Status == 404 ? "Not Found" : "Error")}\r\n"
                + $"Content-Type: {response.Type}\r\nContent-Length: {response.Body.Length}\r\n"
                + $"Cache-Control: {response.Cache}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(response.Body);
        }

        /// <summary>Where clients post their own portraits (ClientConfig [Portrait] UploadUrl = this path on the web server).</summary>
        public const string PortraitUploadPath = "/portrait/upload";
        private const int MaxHeader = 16 * 1024, MaxUpload = 6 * 1024 * 1024;

        /// <summary>
        /// The client's portrait upload (ScreenshotManager::PostPortraitToHttp): multipart/form-data with imageType
        /// "portrait", characterId, thumbnailFile (headshot.png) and imageFile (portrait.png). See <see cref="Portraits.Upload"/>.
        /// </summary>
        private static Response PortraitUpload(string contentType, byte[] body)
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
            (bool ok, string message) = Portraits.Upload(character, fields.GetValueOrDefault("imageFile"), fields.GetValueOrDefault("thumbnailFile"));
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

        private static async Task<Response> Route(string method, string path, Dictionary<string, string> query)
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
            if (path == "/admin")
                return Admin.IsAdmin(Store.SessionOf(query.GetValueOrDefault("t")))
                    ? new Response(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Admin.Page))
                    : new Response(403, "text/plain", Encoding.ASCII.GetBytes("Admins only."));
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
                    return Json(Store.Catalogue().Select(i => new { i.Id, i.Name, i.Description, i.Icon, i.Section, i.Group }));
                case "gallery":
                    return Json(await OnWorldThread(Store.Gallery));
                case "me":
                    if (session != null)
                        Store.Confirm(session);
                    return Json(new { signedIn = session != null, name = session?.CharacterName ?? "" });
            }
            if (method != "POST" && name != "myvendors")
                return Json(new { ok = false, message = "Use POST." }, 405);
            if (session == null)
                return Json(new { ok = false, message = "Open the Showcase from the game to sign in." }, 403);
            switch (name)
            {
                case "admin/players":
                    return Admin.IsAdmin(session) ? Json(new { ok = true, players = Admin.Players() })
                        : Json(new { ok = false, message = "Admins only." }, 403);
                case "admin/run":
                {
                    var (ok, message) = Admin.Run(session, query.GetValueOrDefault("target"), query.GetValueOrDefault("cmd"));
                    return Json(new { ok, message }, ok ? 200 : 403);
                }
                case "take":
                {
                    uint.TryParse(query.GetValueOrDefault("item"), out uint item);
                    if (!uint.TryParse(query.GetValueOrDefault("count"), out uint count))
                        count = 1;
                    (bool ok, string message) = await OnWorldThread(() => Store.Take(session, item, count));
                    return Json(new { ok, message });
                }
                case "myvendors":
                    return Json(await OnWorldThread(() => MyVendors(session)));
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
            }
            return NotFound;
        }
    }
}
