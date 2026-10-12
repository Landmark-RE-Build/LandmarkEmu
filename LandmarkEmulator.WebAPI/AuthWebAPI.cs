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
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WebAPI
{
    public class AuthWebAPI : IHostedService
    {
        bool enabled;
        WebApplication? app;
        string hostname;

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
            string address = "ip:" + http.Connection.RemoteIpAddress;
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

        public AuthWebAPI(WebApiConfig config, DatabaseConfig databaseConfig)
        {
            enabled = config.Enabled;
            if (!enabled)
                return;

            hostname = config.BaseURI ?? "https://0.0.0.0:5000";
            DatabaseManager.Instance.Initialise(databaseConfig);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!enabled)
                return Task.CompletedTask;

            var builder = WebApplication.CreateBuilder();
            app = builder.Build();

            app.MapGet("/", () => "Hello World!");

            // Creates an account and returns a CreateModel containing the Username if successful.
            app.MapPost("/create", async (string username, string password) =>
            {
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
