using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// A server's account service (the auth server's web API, LandmarkEmulator.WebAPI/AuthWebAPI.cs): /info for the
    /// launcher page, /srp/register to create an account, /srp/start + /srp/finish to log in.
    /// </summary>
    public sealed class ServerApi : IPatchSource
    {
        // Release servers answer on plain HTTP; a dev server may use HTTPS with a self-signed certificate. The certificate
        // isn't checked: logging in is SRP, so the password never crosses the wire and the server must prove it knows the
        // account's verifier (M2) - an impostor gets nothing. Which one a server speaks is remembered per address.
        private static readonly HttpClient http = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 8 << 20 };   // /info and logins are small
        // Downloads have no overall time limit (a pack can be gigabytes); a stalled one is cut off by Download itself.
        private static readonly HttpClient downloads = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { Timeout = Timeout.InfiniteTimeSpan };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> schemes = new();
        private readonly string hostPort;

        public ServerApi(ServerEntry server) => hostPort = $"{server.Address}:{server.ApiPort}";

        public string Display => hostPort;

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

        // ---- Hosted patches (LandmarkEmulator.WebAPI/PatchHost.cs; checked by ServerPatches) -------------------------------
        //
        // Plain HTTP is fine here: nothing downloaded is trusted for coming from this server. The list and the pack are
        // checked byte for byte against the lists built into LaunchPad (ServerPatches, PackCheck, GameStore.Install), so a
        // server, or anyone between it and the player, can only make the download fail, never get another file installed.

        /// <summary>A hosted pack's pack.json, as bytes (its SHA-256 is checked against /info's). Never more than <paramref name="maxBytes"/>.</summary>
        public async Task<byte[]> Manifest(string id, long maxBytes, CancellationToken cancel)
        {
            // Read as it arrives (never more than maxBytes kept), within a minute.
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                using HttpResponseMessage response = await Send(b => new HttpRequestMessage(HttpMethod.Get,
                    $"{b}/patches/{Uri.EscapeDataString(id)}/pack.json"), downloads, limit.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    throw new ServerPatches.ServerUnavailable("The server is checking its patch again. Try again in a few minutes.");
                if (!response.IsSuccessStatusCode)
                    throw new Failure($"The server has no patch \"{id}\" (error {(int)response.StatusCode}).", (int)response.StatusCode);
                await using Stream from = await response.Content.ReadAsStreamAsync(limit.Token);
                using var to = new MemoryStream();
                byte[] buffer = new byte[1 << 16];
                int n;
                while ((n = await from.ReadAsync(buffer, limit.Token)) > 0)
                {
                    if (to.Length + n > maxBytes)
                        throw new InvalidDataException("The server's patch list is far too big. Nothing was downloaded.");
                    to.Write(buffer, 0, n);
                }
                return to.ToArray();
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException && !cancel.IsCancellationRequested)
            {
                throw new Failure($"No answer from {hostPort}.");
            }
        }

        /// <summary>
        /// Downloads a hosted pack's .lmpatch to <paramref name="partFile"/>, continuing a broken-off download where it stopped
        /// (HTTP range). Never writes past <paramref name="maxBytes"/> (the size /info announced): a server that sends more
        /// is cut off and the download deleted. <paramref name="read"/> gets the bytes on disk so far. A download that
        /// stalls for 30 s fails (and continues next time).
        /// </summary>
        public async Task Download(string id, string partFile, long maxBytes, Action<long> read, CancellationToken cancel)
        {
            const string path = "the patch";
            long have = File.Exists(partFile) ? new FileInfo(partFile).Length : 0;
            if (have >= maxBytes)
            {
                // Already complete (or more than announced): the check that follows decides.
                if (have > maxBytes)
                    File.Delete(partFile);
                else
                    return;
                have = 0;
            }
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
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    throw new ServerPatches.ServerUnavailable("The server is checking its patch again. Try again in a few minutes.");
                if (!response.IsSuccessStatusCode)
                    throw new Failure($"The server could not send {path} (error {(int)response.StatusCode}).", (int)response.StatusCode);
                bool resumed = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (resumed && response.Content.Headers.ContentRange?.From is long from0 && from0 != have)
                {
                    File.Delete(partFile);
                    throw new Failure($"{path}: the server continued the download at the wrong place. Try again.");
                }
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
                    if (total + n > maxBytes)
                    {
                        to.Close();
                        File.Delete(partFile);
                        throw new InvalidDataException($"The server sent more than the {maxBytes:N0} bytes it announced for {path}. The download was deleted.");
                    }
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
