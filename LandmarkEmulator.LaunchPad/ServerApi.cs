using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// A server's account service (the auth server's web API, LandmarkEmulator.WebAPI/AuthWebAPI.cs): /info for the
    /// launcher page, /srp/register to create an account, /srp/start + /srp/finish to log in.
    /// </summary>
    public sealed class ServerApi
    {
        // Release servers answer on plain HTTP; a dev server may use HTTPS with a self-signed certificate. The certificate
        // isn't checked: logging in is SRP, so the password never crosses the wire and the server must prove it knows the
        // account's verifier (M2) - an impostor gets nothing. Which one a server speaks is remembered per address.
        private static readonly HttpClient http = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { Timeout = TimeSpan.FromSeconds(10) };
        // Downloads have no overall time limit (a pack can be gigabytes); a stalled one is cut off by Download itself.
        private static readonly HttpClient downloads = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { Timeout = Timeout.InfiniteTimeSpan };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> schemes = new();
        private readonly string hostPort;

        public ServerApi(ServerEntry server) => hostPort = $"{server.Address}:{server.ApiPort}";

        /// <summary>Sends a request with the scheme this server answered on before; if it fails to connect, tries the other.</summary>
        private async Task<HttpResponseMessage> Send(Func<string, HttpRequestMessage> request, HttpClient? client = null,
            CancellationToken cancel = default)
        {
            client ??= http;
            var completion = client == downloads ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;
            string first = schemes.TryGetValue(hostPort, out string? known) ? known : "http";
            try
            {
                return await client.SendAsync(request($"{first}://{hostPort}"), completion, cancel);
            }
            catch (HttpRequestException)
            {
                string other = first == "http" ? "https" : "http";
                HttpResponseMessage response = await client.SendAsync(request($"{other}://{hostPort}"), completion, cancel);
                schemes[hostPort] = other;
                return response;
            }
        }

        public sealed class Failure : Exception
        {
            public int Status { get; }
            public Failure(string message, int status = 0) : base(message) => Status = status;
        }

        private async Task<(int Status, JsonElement Body)> Post(string path, IDictionary<string, string> query)
        {
            string q = string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            HttpResponseMessage response;
            try
            {
                response = await Send(b => new HttpRequestMessage(HttpMethod.Post, $"{b}/{path}?{q}"));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                throw new Failure($"No answer from {hostPort}. Is the server running, and is that port open?");
            }
            string text = await response.Content.ReadAsStringAsync();
            JsonElement body = default;
            try { if (text.Length > 0) body = JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException) { }
            return ((int)response.StatusCode, body);
        }

        private static string Message(JsonElement body)
            => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

        /// <summary>The server's name, news, worlds and requirements (GET /info), as raw JSON for the page.</summary>
        public async Task<JsonElement> Info()
        {
            try
            {
                using HttpResponseMessage response = await Send(b => new HttpRequestMessage(HttpMethod.Get, $"{b}/info"));
                response.EnsureSuccessStatusCode();
                string text = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<JsonElement>(text);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new Failure($"No answer from {hostPort} (is it a LandmarkEmu server, and up to date?).");
            }
        }

        /// <summary>Creates an account; returns the SRP key for "remember me".</summary>
        public async Task<(string Key, string Salt)> Register(string account, string password)
        {
            var (salt, verifier, key) = SrpClient.NewAccount(password);
            var (status, body) = await Post("srp/register", new Dictionary<string, string>
            {
                ["username"] = account, ["salt"] = salt, ["verifier"] = verifier
            });
            return status switch
            {
                200 => (key, salt),
                409 => throw new Failure("That account name is taken.", status),
                429 => throw new Failure(Message(body) is { Length: > 0 } m ? m : "Too many new accounts. Try again later.", status),
                _ => throw new Failure(Message(body) is { Length: > 0 } m2 ? m2 : $"The account could not be created (error {status}).", status)
            };
        }

        /// <summary>
        /// Logs in with a password or a saved key (when its salt still matches the account's). Returns the session key the
        /// game is started with, plus the key and salt to remember.
        /// </summary>
        public async Task<(string SessionKey, string Key, string Salt)> Login(string account, string? password, string? savedKey, string? savedSalt)
        {
            var (status, start) = await Post("srp/start", new Dictionary<string, string> { ["username"] = account });
            if (status == 429)
                throw new Failure("Too many wrong passwords. Wait 5 minutes and try again.", status);
            if (status != 200)
                throw new Failure($"Login failed (error {status}).", status);
            string salt = start.GetProperty("salt").GetString() ?? "";
            string key;
            if (!string.IsNullOrEmpty(savedKey) && string.Equals(savedSalt, salt, StringComparison.OrdinalIgnoreCase))
                key = savedKey;
            else if (!string.IsNullOrEmpty(password))
                key = SrpClient.Key(password, salt);
            else
                throw new Failure("Enter your password.", 401);

            SrpClient.Login login = SrpClient.Start(key, start.GetProperty("b").GetString() ?? "");
            var (finishStatus, finish) = await Post("srp/finish", new Dictionary<string, string>
            {
                ["handshake"] = start.GetProperty("handshake").GetString() ?? "", ["a"] = login.A, ["m1"] = login.M1
            });
            if (finishStatus == 403)
                throw new Failure("Wrong account name or password.", 403);
            if (finishStatus == 429)
                throw new Failure("Too many wrong passwords. Wait 5 minutes and try again.", 429);
            if (finishStatus != 200)
                throw new Failure($"Login failed (error {finishStatus}).", finishStatus);
            if (!login.ServerProofOk(finish.GetProperty("m2").GetString() ?? ""))
                throw new Failure("The server could not prove it is the real server. Check the address.", 0);
            return (login.OpenSessionKey(finish.GetProperty("sessionKey").GetString() ?? ""), key, salt);
        }

        // ---- Hosted patches (LandmarkEmulator.WebAPI/PatchHost.cs) ----------------------------------------------------

        /// <summary>A hosted pack's pack.json, as bytes (its SHA-256 is checked against /info's).</summary>
        public async Task<byte[]> PackManifest(string id, CancellationToken cancel)
        {
            try
            {
                using HttpResponseMessage response = await Send(b => new HttpRequestMessage(HttpMethod.Get,
                    $"{b}/patches/{Uri.EscapeDataString(id)}/pack.json"), cancel: cancel);
                if (!response.IsSuccessStatusCode)
                    throw new Failure($"The server has no patch \"{id}\" (error {(int)response.StatusCode}).", (int)response.StatusCode);
                return await response.Content.ReadAsByteArrayAsync(cancel);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
                throw new Failure($"No answer from {hostPort}.");
            }
        }

        /// <summary>
        /// Downloads a hosted pack's .lmpatch to <paramref name="partFile"/>, continuing a broken-off download where it stopped
        /// (HTTP range). <paramref name="read"/> gets the bytes on disk so far. A download that stalls for 30 s fails.
        /// </summary>
        public async Task DownloadPack(string id, string partFile, Action<long> read, CancellationToken cancel)
        {
            const string path = "the patch";
            long have = File.Exists(partFile) ? new FileInfo(partFile).Length : 0;
            string url(string b) => $"{b}/patches/{Uri.EscapeDataString(id)}/pack{PatchFile.Extension}";
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            try
            {
                using HttpResponseMessage response = await Send(b =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url(b));
                    if (have > 0)
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                    return request;
                }, downloads, stall.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(partFile);              // the .part is longer than the file: start over next time
                    throw new Failure($"{path}: the download has to start over. Try again.");
                }
                if (!response.IsSuccessStatusCode)
                    throw new Failure($"The server could not send {path} (error {(int)response.StatusCode}).", (int)response.StatusCode);
                bool resumed = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                await using Stream from = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var to = new FileStream(partFile, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write);
                long total = resumed ? have : 0;
                read(total);
                byte[] buffer = new byte[1 << 20];
                while (true)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(30));
                    int n = await from.ReadAsync(buffer, stall.Token);
                    if (n == 0)
                        break;
                    await to.WriteAsync(buffer.AsMemory(0, n), cancel);
                    total += n;
                    read(total);
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException && !cancel.IsCancellationRequested)
            {
                throw new Failure($"The download of {path} from {hostPort} broke off. Try again: it continues where it stopped.");
            }
        }
    }
}
