using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.WebAPI.Configuration;
using LandmarkEmulator.WebAPI.Models.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WebAPI
{
    public class AuthWebAPI : IHostedService
    {
        bool enabled;
        WebApplication? app;
        string hostname;
        static WebApiConfig settings = new();
        /// <summary>The patch folder, checked (PatchHost.cs).</summary>
        static PatchLibrary patches = new("", null, _ => { });

        /// <summary>
        /// The player's address: the connecting address, or, for requests from the trusted proxy (the account website,
        /// <see cref="WebApiConfig.TrustedProxy"/>), the last address in its X-Forwarded-For (the one the proxy saw).
        /// </summary>
        static string ClientAddress(HttpContext http)
        {
            IPAddress? remote = http.Connection.RemoteIpAddress;
            if (remote != null && remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();
            if (remote != null && !string.IsNullOrEmpty(settings.TrustedProxy) && IPAddress.TryParse(settings.TrustedProxy, out IPAddress? proxy)
                && remote.Equals(proxy))
            {
                string forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
                string last = forwarded.Split(',').Select(p => p.Trim()).LastOrDefault(p => p.Length > 0) ?? "";
                if (IPAddress.TryParse(last, out IPAddress? client))
                    return client.ToString();
            }
            return remote?.ToString() ?? "";
        }

        /// <summary>All registrations in the current hour (<see cref="WebApiConfig.RegistrationsPerHour"/>).</summary>
        static (int Count, DateTime Since) allRegistrations = (0, DateTime.UtcNow);
        static readonly object registrationLock = new();

        /// <summary>
        /// Failed password checks per username and per remote address: after <see cref="MaxFailures"/> within
        /// <see cref="FailureWindow"/> that key is locked out for the rest of the window (stops password guessing).
        /// </summary>
        static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> failures = new();
        const int MaxFailures = 5, MaxFailuresPerAddress = 20;
        static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

        static bool LockedOut(string key, int max)
            => failures.TryGetValue(key, out var f) && DateTime.UtcNow - f.Since < FailureWindow && f.Count >= max;

        static void Failed(string key)
            => failures.AddOrUpdate(key, _ => (1, DateTime.UtcNow),
                (_, f) => DateTime.UtcNow - f.Since < FailureWindow ? (f.Count + 1, f.Since) : (1, DateTime.UtcNow));

        /// <summary>Username + password check with the lockout above, for every endpoint that acts on an account.</summary>
        static async Task<(AccountModel? Account, IResult? Refusal)> CheckPassword(HttpContext http, string username, string password)
        {
            string user = "user:" + (username ?? "").ToLowerInvariant();
            string address = "ip:" + AddressKey(ClientAddress(http));
            // Per address the limit is higher: several players may share one (home router, NAT).
            if (LockedOut(user, MaxFailures) || LockedOut(address, MaxFailuresPerAddress))
                return (null, Results.StatusCode(429));
            AccountModel? account = string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) ? null
                : await DatabaseManager.Instance.AuthDatabase.GetAccountByUsername(username);
            if (account == null || !PasswordProvider.VerifyPassword(account.Salt, account.Verifier, password))
            {
                Failed(user);
                Failed(address);
                return (null, Results.StatusCode(403));
            }
            failures.TryRemove(user, out _);
            return (account, null);
        }

        /// <summary>
        /// The plain-text endpoints (/create, /authenticate, /delete) take the password itself (in the request body,
        /// <see cref="ReadCredentials"/>), so they only answer on this PC (the host's own scripts and tools). Other PCs
        /// use the /srp endpoints (LaunchPad).
        /// </summary>
        static bool IsLocal(HttpContext http) => http.Connection.RemoteIpAddress is IPAddress a && IPAddress.IsLoopback(a)
            && http.Request.Headers["X-Forwarded-For"].Count == 0;

        static IResult LocalOnly() => Results.Json(new CreateModel
        {
            Message = "Plain-text login is only accepted on the server PC. Use LandmarkEmu LaunchPad, which logs in without sending the password."
        }, statusCode: 403);

        /// <summary>The body of a plain-text endpoint: JSON {"username": "...", "password": "..."} (names in any case).</summary>
        internal sealed record Credentials(string? Username, string? Password);

        /// <summary>A plain-text endpoint's body is a user name and a password: anything bigger is refused unread.</summary>
        const long MaxCredentialsBody = 4096;

        /// <summary>
        /// The user name and password of a plain-text endpoint (/create, /authenticate, /delete), from the request BODY
        /// only: a form (application/x-www-form-urlencoded or multipart, fields "username" and "password") or JSON
        /// (<see cref="Credentials"/>). Until 2026-10-18 they were query parameters, so every password stood in the
        /// request URL, which ends up in logs (ASP.NET's own "Request starting" lines, proxies, shell history). A request
        /// that still puts a password in the address is refused (400) without being looked at, so an old script fails
        /// loudly instead of leaking. The body itself is never logged: nothing here writes it, and ASP.NET's request
        /// logging is held at Warning (<see cref="StartAsync"/>).
        /// </summary>
        internal static async Task<(string? Username, string? Password, IResult? Refusal)> ReadCredentials(HttpContext http)
        {
            if (http.Request.Query.ContainsKey("password"))
                return (null, null, Results.BadRequest(new CreateModel
                {
                    Message = "Send the user name and password in the request body (a form or JSON), not in the address."
                }));
            if (http.Request.ContentLength is long length && length > MaxCredentialsBody)
                return (null, null, Results.StatusCode(413));
            try
            {
                if (http.Request.HasFormContentType)
                {
                    IFormCollection form = await http.Request.ReadFormAsync();
                    return (form["username"].ToString(), form["password"].ToString(), null);
                }
                if (http.Request.HasJsonContentType())
                {
                    Credentials? body = await http.Request.ReadFromJsonAsync<Credentials>();
                    return (body?.Username, body?.Password, null);
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is System.IO.InvalidDataException || e is BadHttpRequestException)
            {
                // A malformed body: the same answer as a missing one (the exception text is not passed on: it can quote the body).
            }
            return (null, null, Results.BadRequest(new CreateModel
            {
                Message = "A form or JSON body with username and password is required."
            }));
        }

        /// <summary>An SRP login in progress (between /srp/start and /srp/finish).</summary>
        sealed record Handshake(string Username, BigInteger V, BigInteger B, BigInteger PublicB, DateTime Expires, string Address = "");
        static readonly ConcurrentDictionary<string, Handshake> handshakes = new();
        /// <summary>
        /// Logins one address may have in progress at once. The table holds at most 10,000 and answers 503 to everyone
        /// beyond that; until 2026-10-18 one address could fill it with /srp/start (no account needed) and so block every
        /// login on the server for the 2-minute handshake lifetime. A player needs one at a time.
        /// </summary>
        const int HandshakesPerAddress = 16;

        /// <summary>
        /// The key an address is counted by: IPv4 as is, IPv6 by its /64 (one home or host usually holds a whole /64, so
        /// counting single IPv6 addresses would let one machine bypass every per-address limit).
        /// </summary>
        static string AddressKey(string address)
        {
            if (IPAddress.TryParse(address, out IPAddress? ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] bytes = ip.GetAddressBytes();
                Array.Clear(bytes, 8, 8);
                return new IPAddress(bytes) + "/64";
            }
            return address;
        }

        /// <summary>
        /// Drops the counters whose window is over (failures, registrations). They were never pruned, so every user name
        /// and address ever tried stayed in memory; an unauthenticated client trying random names grew them without
        /// bound. Swept at most once a minute, from the endpoints that add to them.
        /// </summary>
        static void SweepCounters()
        {
            DateTime now = DateTime.UtcNow;
            lock (sweepLock)
            {
                if (now < nextSweep)
                    return;
                nextSweep = now.AddMinutes(1);
            }
            foreach (var f in failures.Where(f => now - f.Value.Since >= FailureWindow).ToList())
                failures.TryRemove(f);
            foreach (var r in registrations.Where(r => now - r.Value.Since >= RegistrationWindow).ToList())
                registrations.TryRemove(r);
        }
        static readonly object sweepLock = new();
        static DateTime nextSweep;
        static readonly TimeSpan HandshakeLifetime = TimeSpan.FromMinutes(2);
        /// <summary>Unknown names get a salt that stays the same for that name, so /srp/start does not reveal which accounts exist.</summary>
        static readonly byte[] fakeSaltSecret = RandomNumberGenerator.GetBytes(32);
        static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> registrations = new();
        static readonly TimeSpan RegistrationWindow = TimeSpan.FromHours(1);

        /// <summary>
        /// The sign-up gate shared by every web route that creates an account: REGISTRATION_OPEN, then the per-address and
        /// all-addresses hourly limits (<see cref="WebApiConfig.RegistrationsPerAddressPerHour"/>,
        /// <see cref="WebApiConfig.RegistrationsPerHour"/>); null = go ahead (the attempt is counted). With
        /// <paramref name="localExempt"/> a request from this PC passes all three, as /srp/register always did (the owner's
        /// own LaunchPad on the server PC). /create passes false: it answers only this PC, so an exemption would make it a
        /// way round all three (until 2026-10-18 it checked none); the owner's unrestricted tool is the login server
        /// console's "account create".
        /// </summary>
        static IResult? RegistrationRefused(HttpContext http, bool localExempt)
        {
            bool exempt = localExempt && IsLocal(http);
            if (!settings.RegistrationOpen && !exempt)
                return Results.Json(new CreateModel { Message = "Account creation is closed." }, statusCode: 403);
            string address = "ip:" + AddressKey(ClientAddress(http));
            SweepCounters();
            var r = registrations.AddOrUpdate(address, _ => (1, DateTime.UtcNow),
                (_, f) => DateTime.UtcNow - f.Since < RegistrationWindow ? (f.Count + 1, f.Since) : (1, DateTime.UtcNow));
            if (r.Count > settings.RegistrationsPerAddressPerHour && !exempt)
                return Results.Json(new CreateModel { Message = "Too many new accounts from your address. Try again later." }, statusCode: 429);
            if (!exempt)
                lock (registrationLock)
                {
                    if (DateTime.UtcNow - allRegistrations.Since >= RegistrationWindow)
                        allRegistrations = (0, DateTime.UtcNow);
                    if (allRegistrations.Count >= settings.RegistrationsPerHour)
                        return Results.Json(new CreateModel { Message = "Too many new accounts right now. Try again later." }, statusCode: 429);
                    allRegistrations.Count++;
                }
            return null;
        }

        static bool IsHex(string value, int length)
            => value != null && value.Length == length && value.All(Uri.IsHexDigit);

        public AuthWebAPI(WebApiConfig config, DatabaseConfig databaseConfig)
        {
            enabled = config.Enabled;
            if (!enabled)
                return;

            hostname = config.BaseURI ?? "https://0.0.0.0:5000";
            settings = config;
            DatabaseManager.Instance.Initialise(databaseConfig);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!enabled)
                return Task.CompletedTask;

            var builder = WebApplication.CreateBuilder();
            // ASP.NET logs every request's full address at Information ("Request starting POST http://.../path?query");
            // query strings here can carry login material (SRP verifiers and proofs), so request lines stay out of the log.
            // Warnings and errors still show. Request bodies are never logged (no HTTP logging middleware is added).
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            app = builder.Build();

            patches.Dispose();
            patches = new PatchLibrary(settings.PatchFolder, settings.PatchVerifySteamDir, Console.WriteLine);
            patches.Start();

            app.MapGet("/", () => "Hello World!");

            // ---- For the launcher: this server's name, news, worlds and requirements. Public; no account data.
            app.MapGet("/info", () =>
            {
                object[] news = Array.Empty<object>();
                try
                {
                    if (!string.IsNullOrWhiteSpace(settings.NewsFile) && System.IO.File.Exists(settings.NewsFile))
                        news = System.Text.Json.JsonSerializer.Deserialize<object[]>(System.IO.File.ReadAllText(settings.NewsFile)) ?? news;
                }
                catch (Exception)
                {
                    // A half-edited news file shows no news rather than breaking the launcher.
                }
                var worlds = DatabaseManager.Instance.AuthDatabase.GetZoneServers().Select(z => new
                {
                    id = z.Id,
                    name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(z.NameId) ?? $"World {z.Id}",
                    up = z.Heartbeat is DateTime beat && DateTime.UtcNow - beat < TimeSpan.FromSeconds(15),
                    players = z.PlayersOnline,
                    webPagesUrl = $"http://{z.Host}:{z.Port + 1}"
                });
                return Results.Ok(new
                {
                    name = settings.ServerName,
                    websiteUrl = settings.WebsiteUrl,
                    requiredClientBuild = settings.RequiredClientBuild,
                    registrationOpen = settings.RegistrationOpen,
                    // Everything else the launcher needs, so players only type the address.
                    loginPort = settings.LoginPort,
                    patches = patches.Packs.Select(PatchLibrary.Describe),
                    worlds,
                    news
                });
            });
            PatchHost.Map(app, () => patches);

            // ---- SRP-6a (Shared/Network/Cryptography/Srp.cs; clients: LaunchPad SrpClient.cs, docker/web/site/srp.js). No password on the wire.

            // Register: the client sends the salt and verifier it computed itself (it also enforces the 6-character
            // minimum, which the server can no longer see).
            app.MapPost("/srp/register", (HttpContext http, string username, string salt, string verifier) =>
            {
                if (RegistrationRefused(http, localExempt: true) is IResult refused)
                    return refused;
                if (AccountRules.CheckName(username) != null || !IsHex(salt, 32) || !Srp.IsVerifier(verifier))
                    return Results.BadRequest(new CreateModel { Message = "Account name (3-32 letters / digits), salt and verifier are required." });
                if (DatabaseManager.Instance.AuthDatabase.AccountExists(username))
                    return Results.Conflict(new CreateModel { Message = "Unable to create account. Username already exists." });
                try
                {
                    DatabaseManager.Instance.AuthDatabase.CreateAccount(username, salt.ToUpperInvariant(), verifier.ToUpperInvariant());
                }
                catch (InvalidOperationException)
                {
                    // Registered by a request that arrived at the same moment (AuthDatabase.CreateAccount checks again).
                    return Results.Conflict(new CreateModel { Message = "Unable to create account. Username already exists." });
                }
                return Results.Ok(new CreateModel { Username = username, Message = "Success." });
            });

            // Start: salt and B. Unknown names get a stable fake salt and a random B (the proof then fails as for a
            // wrong password).
            app.MapPost("/srp/start", async (HttpContext http, string username) =>
            {
                string user = "user:" + (username ?? "").ToLowerInvariant();
                string address = AddressKey(ClientAddress(http));
                if (LockedOut(user, MaxFailures) || LockedOut("ip:" + address, MaxFailuresPerAddress))
                    return Results.StatusCode(429);
                SweepCounters();
                foreach (var old in handshakes.Where(h => h.Value.Expires < DateTime.UtcNow).ToList())
                    handshakes.TryRemove(old.Key, out _);
                if (handshakes.Values.Count(h => h.Address == address) >= HandshakesPerAddress)
                    return Results.StatusCode(429);
                if (handshakes.Count > 10000)
                    return Results.StatusCode(503);

                AccountModel? account = string.IsNullOrEmpty(username) ? null
                    : await DatabaseManager.Instance.AuthDatabase.GetAccountByUsername(username);
                string salt;
                BigInteger v;
                if (account != null)
                {
                    if (PasswordProvider.IsLegacy(account.Verifier))
                        await DatabaseManager.Instance.AuthDatabase.UpdateVerifier(account, PasswordProvider.SrpVerifier(account.Verifier));
                    salt = account.Salt;
                    v = Srp.Number(account.Verifier);
                }
                else
                {
                    salt = Convert.ToHexString(HMACSHA256.HashData(fakeSaltSecret, Encoding.UTF8.GetBytes((username ?? "").ToLowerInvariant()))[..16]);
                    v = Srp.Number(RandomNumberGenerator.GetBytes(Srp.Length)) % Srp.N;
                }
                var (b, B) = Srp.ServerStart(v);
                string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                handshakes[id] = new Handshake(username ?? "", v, b, B, DateTime.UtcNow + HandshakeLifetime, address);
                return Results.Ok(new { handshake = id, salt, b = Srp.Hex(B) });
            });

            // Finish: the client's A and proof M1; on success the server's proof M2 and the session key, encrypted with
            // the shared secret.
            app.MapPost("/srp/finish", async (HttpContext http, string handshake, string a, string m1) =>
            {
                if (handshake == null || !handshakes.TryRemove(handshake, out Handshake? h) || h.Expires < DateTime.UtcNow)
                    return Results.BadRequest(new CreateModel { Message = "Login expired; try again." });
                string user = "user:" + h.Username.ToLowerInvariant();
                string address = "ip:" + AddressKey(ClientAddress(http));
                if (LockedOut(user, MaxFailures) || LockedOut(address, MaxFailuresPerAddress))
                    return Results.StatusCode(429);
                BigInteger? S = null;
                byte[]? proof = null;
                if (IsHex(a, Srp.Length * 2) && IsHex(m1, 64))
                {
                    proof = Convert.FromHexString(m1);
                    S = Srp.ServerVerify(h.V, h.B, h.PublicB, Srp.Number(a), proof);
                }
                AccountModel? account = S == null ? null : await DatabaseManager.Instance.AuthDatabase.GetAccountByUsername(h.Username);
                if (account == null)
                {
                    Failed(user);
                    Failed(address);
                    return Results.StatusCode(403);
                }
                failures.TryRemove(user, out _);
                string sessionKey = RandomProvider.GetBytes(16u).ToHexString();
                await DatabaseManager.Instance.AuthDatabase.UpdateAccountSessionKey(account, sessionKey);
                return Results.Ok(new
                {
                    m2 = Convert.ToHexString(Srp.ServerProof(Srp.Number(a), proof!, S!.Value)),
                    sessionKey = Srp.SealSessionKey(sessionKey, S.Value)
                });
            });

            // ---- Plain-text endpoints (server PC only): user name and password in the POST body (ReadCredentials).

            // Creates an account and returns a CreateModel containing the Username if successful. The sign-up's own rules
            // (AccountRules: name, password length) and its gate (REGISTRATION_OPEN, the hourly limits; no exemption for
            // this PC, RegistrationRefused) apply as for /srp/register.
            app.MapPost("/create", async (HttpContext http) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                var (username, password, unreadable) = await ReadCredentials(http);
                if (unreadable != null)
                    return unreadable;
                if (AccountRules.Check(username, password) is string broken)
                    return Results.BadRequest(new CreateModel { Message = broken });
                if (RegistrationRefused(http, localExempt: false) is IResult refused)
                    return refused;
                if (DatabaseManager.Instance.AuthDatabase.AccountExists(username!))
                    return Results.Conflict(new CreateModel
                    {
                        Message  = "Unable to create account. Username already exists."
                    });

                var (s, v) = PasswordProvider.GenerateSaltAndVerifier(password!);
                try
                {
                    DatabaseManager.Instance.AuthDatabase.CreateAccount(username!, s, v);
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new CreateModel { Message = "Unable to create account. Username already exists." });
                }

                return Results.Ok(new CreateModel
                {
                    Username = username!,
                    Message = "Success."
                });
            });
            
            // Returns am AuthenticateModel containing a SessionKey upon successful authentication.
            app.MapPost("/authenticate", async (HttpContext http) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                var (username, password, unreadable) = await ReadCredentials(http);
                if (unreadable != null)
                    return unreadable;
                var (account, refusal) = await CheckPassword(http, username!, password!);
                if (account == null)
                    return refusal!;

                string sessionKey = RandomProvider.GetBytes(16u).ToHexString();
                await DatabaseManager.Instance.AuthDatabase.UpdateAccountSessionKey(account, sessionKey);

                return Results.Ok(new AuthenticateModel
                {
                    SessionKey = sessionKey,
                    SessionKeyExpiration = DateTime.Now.AddMinutes(30d),
                    Message = "Success."
                });
            });

            // Deleting an account needs its password (it used to need only the username). DELETE as before, or POST (some
            // HTTP tools send no body with DELETE); either way the credentials come from the body.
            app.MapMethods("/delete", new[] { "DELETE", "POST" }, async (HttpContext http) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                var (username, password, unreadable) = await ReadCredentials(http);
                if (unreadable != null)
                    return unreadable;
                var (account, refusal) = await CheckPassword(http, username!, password!);
                if (account == null)
                    return refusal!;
                bool removed = DatabaseManager.Instance.AuthDatabase.DeleteAccount(account.Username);
                return removed ? Results.Ok() : Results.NotFound();
            });

            app.Urls.Clear();
            app.Urls.Add(hostname);

            app.StartAsync(cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            app?.StopAsync(cancellationToken);
            patches.Dispose();
            
            return Task.CompletedTask;
        }
    }
}
