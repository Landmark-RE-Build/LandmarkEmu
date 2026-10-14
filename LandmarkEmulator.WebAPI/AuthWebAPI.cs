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
            string address = "ip:" + ClientAddress(http);
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
        /// The plain-text endpoints (/create, /authenticate, /delete) take the password itself, so they only answer on
        /// this PC (the host's own start.bat and tools). Other PCs use the /srp endpoints (play.bat).
        /// </summary>
        static bool IsLocal(HttpContext http) => http.Connection.RemoteIpAddress is IPAddress a && IPAddress.IsLoopback(a)
            && http.Request.Headers["X-Forwarded-For"].Count == 0;

        static IResult LocalOnly() => Results.Json(new CreateModel
        {
            Message = "Plain-text login is only accepted on the server PC. Use play.bat, which logs in without sending the password."
        }, statusCode: 403);

        /// <summary>An SRP login in progress (between /srp/start and /srp/finish).</summary>
        sealed record Handshake(string Username, BigInteger V, BigInteger B, BigInteger PublicB, DateTime Expires);
        static readonly ConcurrentDictionary<string, Handshake> handshakes = new();
        static readonly TimeSpan HandshakeLifetime = TimeSpan.FromMinutes(2);
        /// <summary>Unknown names get a salt that stays the same for that name, so /srp/start does not reveal which accounts exist.</summary>
        static readonly byte[] fakeSaltSecret = RandomNumberGenerator.GetBytes(32);
        static readonly Regex AccountName = new("^[A-Za-z0-9_]{3,32}$");
        static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> registrations = new();
        static readonly TimeSpan RegistrationWindow = TimeSpan.FromHours(1);

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
            app = builder.Build();

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
                    patches = PatchHost.Packs(settings.PatchFolder).Select(PatchHost.Describe),
                    worlds,
                    news
                });
            });
            PatchHost.Map(app, () => settings.PatchFolder);

            // ---- SRP-6a (Shared/Network/Cryptography/Srp.cs; clients: LaunchPad SrpClient.cs, docker/web/site/srp.js). No password on the wire.

            // Register: the client sends the salt and verifier it computed itself (it also enforces the 6-character
            // minimum, which the server can no longer see).
            app.MapPost("/srp/register", (HttpContext http, string username, string salt, string verifier) =>
            {
                if (!settings.RegistrationOpen && !IsLocal(http))
                    return Results.Json(new CreateModel { Message = "Account creation is closed." }, statusCode: 403);
                string address = "ip:" + ClientAddress(http);
                var r = registrations.AddOrUpdate(address, _ => (1, DateTime.UtcNow),
                    (_, f) => DateTime.UtcNow - f.Since < RegistrationWindow ? (f.Count + 1, f.Since) : (1, DateTime.UtcNow));
                if (r.Count > settings.RegistrationsPerAddressPerHour && !IsLocal(http))
                    return Results.Json(new CreateModel { Message = "Too many new accounts from your address. Try again later." }, statusCode: 429);
                if (!IsLocal(http))
                    lock (registrationLock)
                    {
                        if (DateTime.UtcNow - allRegistrations.Since >= RegistrationWindow)
                            allRegistrations = (0, DateTime.UtcNow);
                        if (allRegistrations.Count >= settings.RegistrationsPerHour)
                            return Results.Json(new CreateModel { Message = "Too many new accounts right now. Try again later." }, statusCode: 429);
                        allRegistrations.Count++;
                    }
                if (username == null || !AccountName.IsMatch(username) || !IsHex(salt, 32) || !Srp.IsVerifier(verifier))
                    return Results.BadRequest(new CreateModel { Message = "Account name (3-32 letters / digits), salt and verifier are required." });
                if (DatabaseManager.Instance.AuthDatabase.AccountExists(username))
                    return Results.Conflict(new CreateModel { Message = "Unable to create account. Username already exists." });
                DatabaseManager.Instance.AuthDatabase.CreateAccount(username, salt.ToUpperInvariant(), verifier.ToUpperInvariant());
                return Results.Ok(new CreateModel { Username = username, Message = "Success." });
            });

            // Start: salt and B. Unknown names get a stable fake salt and a random B (the proof then fails as for a
            // wrong password).
            app.MapPost("/srp/start", async (HttpContext http, string username) =>
            {
                string user = "user:" + (username ?? "").ToLowerInvariant();
                if (LockedOut(user, MaxFailures) || LockedOut("ip:" + ClientAddress(http), MaxFailuresPerAddress))
                    return Results.StatusCode(429);
                foreach (var old in handshakes.Where(h => h.Value.Expires < DateTime.UtcNow).ToList())
                    handshakes.TryRemove(old.Key, out _);
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
                handshakes[id] = new Handshake(username ?? "", v, b, B, DateTime.UtcNow + HandshakeLifetime);
                return Results.Ok(new { handshake = id, salt, b = Srp.Hex(B) });
            });

            // Finish: the client's A and proof M1; on success the server's proof M2 and the session key, encrypted with
            // the shared secret.
            app.MapPost("/srp/finish", async (HttpContext http, string handshake, string a, string m1) =>
            {
                if (handshake == null || !handshakes.TryRemove(handshake, out Handshake? h) || h.Expires < DateTime.UtcNow)
                    return Results.BadRequest(new CreateModel { Message = "Login expired; try again." });
                string user = "user:" + h.Username.ToLowerInvariant();
                string address = "ip:" + ClientAddress(http);
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

            // Creates an account and returns a CreateModel containing the Username if successful.
            app.MapPost("/create", async (HttpContext http, string username, string password) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || password.Length < 6)
                    return Results.BadRequest(new CreateModel { Message = "Username and a password of at least 6 characters are required." });
                if (DatabaseManager.Instance.AuthDatabase.AccountExists(username))
                    return Results.Conflict(new CreateModel
                    {
                        Message  = "Unable to create account. Username already exists."
                    });

                var (s, v) = PasswordProvider.GenerateSaltAndVerifier(password);
                DatabaseManager.Instance.AuthDatabase.CreateAccount(username, s, v);

                return Results.Ok(new CreateModel
                {
                    Username = username,
                    Message = "Success."
                });
            });
            
            // Returns am AuthenticateModel containing a SessionKey upon successful authentication.
            app.MapPost("/authenticate", async (HttpContext http, string username, string password) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                var (account, refusal) = await CheckPassword(http, username, password);
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

            // Deleting an account needs its password (it used to need only the username).
            app.MapDelete("/delete", async (HttpContext http, string username, string password) =>
            {
                if (!IsLocal(http))
                    return LocalOnly();
                var (account, refusal) = await CheckPassword(http, username, password);
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
            
            return Task.CompletedTask;
        }
    }
}
