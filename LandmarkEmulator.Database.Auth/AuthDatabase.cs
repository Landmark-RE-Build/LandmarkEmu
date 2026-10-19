using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.Database.Auth
{
    public class AuthDatabase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IDatabaseConfig config;

        public AuthDatabase(IDatabaseConfig config)
        {
            this.config = config;
        }

        public async Task Save(Action<AuthContext> action)
        {
            using var context = new AuthContext(config);
            action.Invoke(context);
            await context.SaveChangesAsync();
        }

        public void Migrate()
        {
            using var context = new AuthContext(config);

            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count > 0)
            {
                log.Info($"Applying {migrations.Count} authentication database migration(s)...");
                foreach (string migration in migrations)
                    log.Info(migration);

                context.Database.Migrate();
            }
        }

        /// <summary>
        /// Returns if an account with the given username already exists.
        /// </summary>
        public bool AccountExists(string username)
        {
            using var context = new AuthContext(config);
            return context.Account.SingleOrDefault(a => a.Username == username) != null;
        }

        /// <summary>
        /// Create a new account with the supplied email, salt and password verifier that is inserted into the database.
        /// </summary>
        /// <summary>
        /// Serialises account creation in this process (the auth server: its console command, /create and /srp/register).
        /// The account table's username index is not unique (20220111035602_Accounts), so two registrations of one name
        /// at the same moment both passed the AccountExists check and both inserted (2026-10-18). Every lookup by name
        /// (GetAccountByUsername, AccountExists, DeleteAccount) uses SingleOrDefault, which then throws for that name:
        /// the account could never log in again. A unique index would need a migration that fails on a database that
        /// already holds such a pair, so the check and the insert are made one step here instead.
        /// </summary>
        private static readonly object createLock = new();

        /// <summary>Creates an account; throws <see cref="InvalidOperationException"/> when the name is taken.</summary>
        public void CreateAccount(string username, string s, string v)
        {
            lock (createLock)
            {
                if (AccountExists(username))
                    throw new InvalidOperationException($"Account with that username already exists.");

                using var context = new AuthContext(config);
                var model = new AccountModel
                {
                    Username = username,
                    Salt     = s,
                    Verifier = v,
                };
                context.Account.Add(model);

                context.SaveChanges();
            }
        }

        /// <summary>
        /// Delete an existing account with the supplied email.
        /// </summary>
        public bool DeleteAccount(string username)
        {
            using var context = new AuthContext(config);
            AccountModel account = context.Account.SingleOrDefault(a => a.Username == username);
            if (account == null)
                return false;

            context.Account.Remove(account);
            return context.SaveChanges() > 0;
        }

        /// <summary>
        /// Update <see cref="AccountModel"/> with supplied session key asynchronously.
        /// </summary>
        public async Task UpdateAccountSessionKey(AccountModel account, string sessionKey)
        {
            account.SessionKey = sessionKey;
            account.SessionKeyExpiration = DateTime.Now.AddHours(0.5d); // Session Keys expire in 30 minutes

            await using var context = new AuthContext(config);
            EntityEntry<AccountModel> entity = context.Attach(account);
            entity.Property(p => p.SessionKey).IsModified = true;
            entity.Property(p => p.SessionKeyExpiration).IsModified = true;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Selects an <see cref="AccountModel"/> asynchronously that matches the supplied session key.
        /// </summary>
        public async Task<AccountModel> GetAccountBySessionKeyAsync(string sessionKey)
        {
            using var context = new AuthContext(config);
            return await context.Account
                .AsSplitQuery()
                .SingleOrDefaultAsync(a => a.SessionKey == sessionKey && a.SessionKeyExpiration > DateTime.Now);
        }

        /// <summary>The account with this id, or null.</summary>
        public async Task<AccountModel> GetAccountByIdAsync(ulong id)
        {
            using var context = new AuthContext(config);
            return await context.Account
                .AsSplitQuery()
                .SingleOrDefaultAsync(a => a.Id == id);
        }

        /// <summary>
        /// Selects an <see cref="AccountModel"/> asynchronously that matches the supplied session key.
        /// </summary>
        public async Task<AccountModel> GetAccountByUsername(string username)
        {
            using var context = new AuthContext(config);
            return await context.Account
                .AsSplitQuery()
                .SingleOrDefaultAsync(a => a.Username == username);
        }

        /// <summary>Replaces an account's stored password verifier (an old-style value upgraded to an SRP verifier).</summary>
        public async Task UpdateVerifier(AccountModel account, string verifier)
        {
            account.Verifier = verifier;

            await using var context = new AuthContext(config);
            EntityEntry<AccountModel> entity = context.Attach(account);
            entity.Property(p => p.Verifier).IsModified = true;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Update <see cref="AccountModel"/> with supplied server ticket asynchronously.
        /// </summary>
        public async Task UpdateServerTicket(AccountModel account, string serverTicket, uint world = 0)
        {
            account.ServerTicket = serverTicket;
            account.ServerTicketIssuedAt = DateTime.UtcNow;
            account.ServerTicketWorld = world;

            await using var context = new AuthContext(config);
            EntityEntry<AccountModel> entity = context.Attach(account);
            entity.Property(p => p.ServerTicket).IsModified = true;
            entity.Property(p => p.ServerTicketIssuedAt).IsModified = true;
            entity.Property(p => p.ServerTicketWorld).IsModified = true;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// How long a server ticket stays good after Play (ours [I]: live's lifetime is not known). The client connects to
        /// the world right after the login server's answer, so a fresh ticket is used within seconds; a kept one can no
        /// longer skip a login queue later.
        /// </summary>
        public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Selects an <see cref="AccountModel"/> asynchronously that matches the supplied server ticket.
        /// </summary>
        public async Task<AccountModel> GetAccountByServerTicketAsync(string serverTicket)
        {
            using var context = new AuthContext(config);
            return await context.Account
                .AsSplitQuery()
                .SingleOrDefaultAsync(a => a.ServerTicket == serverTicket);
        }

        /// <summary>
        /// Takes a server ticket: returns its account and clears the ticket, so a ticket logs in once (one seen on the
        /// network can't be replayed). The login server issues a fresh one for every character login. Ticket hardening: a
        /// ticket older than <see cref="TicketLifetime"/>, or issued for another world than <paramref name="world"/> (0 =
        /// the caller doesn't know its world), is refused and cleared; a ticket stored for world 0 (written by a local test
        /// login) is good on any world.
        /// </summary>
        public async Task<AccountModel> ConsumeServerTicketAsync(string serverTicket, uint world = 0)
        {
            if (string.IsNullOrEmpty(serverTicket))
                return null;
            await using var context = new AuthContext(config);
            AccountModel account = await context.Account.SingleOrDefaultAsync(a => a.ServerTicket == serverTicket);
            if (account == null)
                return null;
            bool expired = account.ServerTicketIssuedAt is not DateTime issued || DateTime.UtcNow - issued > TicketLifetime;
            bool otherWorld = world != 0 && account.ServerTicketWorld != 0 && account.ServerTicketWorld != world;
            if (expired || otherWorld)
            {
                // Logged, so a refused login has a reason on record (review 2026-10-18 #9: a direct login to a secondary
                // world through a multi-world gateway, which passes its primary world, is refused here with no message).
                log.Warn(otherWorld
                    ? $"Server ticket of account {account.Id} issued for world {account.ServerTicketWorld} used on world {world}: refused."
                    : $"Server ticket of account {account.Id} expired (issued {account.ServerTicketIssuedAt:u}): refused.");
                await context.Account.Where(a => a.Id == account.Id && a.ServerTicket == serverTicket)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ServerTicket, ""));
                return null;
            }
            // Only the first of two simultaneous logins with the same ticket wins.
            int taken = await context.Account.Where(a => a.Id == account.Id && a.ServerTicket == serverTicket)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ServerTicket, ""));
            return taken == 1 ? account : null;
        }

        /// <summary>
        /// Stores an account's membership (type and end, UTC) and whether it has used its trial (TASKS 3.12; world server
        /// Game/Membership). The world keeps the online account's copy and writes it through here.
        /// </summary>
        public async Task UpdateMembership(ulong accountId, byte type, DateTime? until, bool trialRedeemed)
        {
            await using var context = new AuthContext(config);
            await context.Account.Where(a => a.Id == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MemberType, type).SetProperty(a => a.MemberUntil, until)
                    .SetProperty(a => a.TrialRedeemed, trialRedeemed));
        }

        /// <summary>
        /// Marks the account's one trial key as mailed. True only for the call that changed it, so two worlds entered at
        /// the same time can't both mail a key.
        /// </summary>
        /// <summary>
        /// Takes the account's member stipend if it is due (never had, or the last one is older than
        /// <paramref name="interval"/>): one atomic update, so two worlds can't both pay it. True when this call took it.
        /// </summary>
        public async Task<bool> ClaimMemberStipend(ulong accountId, TimeSpan interval)
        {
            using var context = new AuthContext(config);
            DateTime now = DateTime.UtcNow, due = now - interval;
            int changed = await context.Account.Where(a => a.Id == accountId && (a.MemberStipendAt == null || a.MemberStipendAt < due))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MemberStipendAt, now));
            return changed == 1;
        }

        public async Task<bool> MarkTrialKeyMailed(ulong accountId)
        {
            await using var context = new AuthContext(config);
            int changed = await context.Account.Where(a => a.Id == accountId && !a.TrialKeyMailed)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.TrialKeyMailed, true));
            return changed == 1;
        }

        /// <summary>The membership columns of one account (type, end, trial used), or null; read fresh, not cached.</summary>
        public async Task<(byte Type, DateTime? Until, bool TrialRedeemed)?> GetMembership(ulong accountId)
        {
            await using var context = new AuthContext(config);
            var row = await context.Account.AsNoTracking().Where(a => a.Id == accountId)
                .Select(a => new { a.MemberType, a.MemberUntil, a.TrialRedeemed }).SingleOrDefaultAsync();
            return row == null ? null : (row.MemberType, row.MemberUntil, row.TrialRedeemed);
        }

        /// <summary>
        /// The world server is running (heartbeat = now, or null when it stops) with this many players and this limit.
        /// </summary>
        public async Task UpdateZoneServerHeartbeat(ulong zoneServerId, bool running, uint players, uint maxPlayers = 0)
        {
            using var context = new AuthContext(config);
            DateTime? now = running ? DateTime.UtcNow : null;
            await context.ZoneServer.Where(s => s.Id == zoneServerId)
                .ExecuteUpdateAsync(s => s.SetProperty(z => z.Heartbeat, now).SetProperty(z => z.PlayersOnline, players)
                    .SetProperty(z => z.MaxPlayers, maxPlayers));
        }

        /// <summary>
        /// A world server's own entry in the server list (zone_server): created on its first start, then kept up to date
        /// with its name text id and public address when those are configured (0 / null leave the stored value).
        /// </summary>
        public async Task RegisterZoneServer(ulong zoneServerId, uint nameId, string host, ushort port)
        {
            using var context = new AuthContext(config);
            ZoneServerModel row = await context.ZoneServer.SingleOrDefaultAsync(s => s.Id == zoneServerId);
            if (row == null)
            {
                row = new ZoneServerModel { Id = zoneServerId, NameId = nameId != 0 ? nameId : 62147u, Host = host ?? "127.0.0.1",
                    Port = port != 0 ? port : (ushort)19000, Flags = 1 };
                context.ZoneServer.Add(row);
            }
            else
            {
                if (nameId != 0)
                    row.NameId = nameId;
                if (!string.IsNullOrEmpty(host))
                    row.Host = host;
                if (port != 0)
                    row.Port = port;
            }
            await context.SaveChangesAsync();
        }

        /// <summary>One zone server's row as it is now (its heartbeat), or null.</summary>
        public async Task<ZoneServerModel> GetZoneServerAsync(ulong zoneServerId)
        {
            using var context = new AuthContext(config);
            return await context.ZoneServer.AsNoTracking().SingleOrDefaultAsync(s => s.Id == zoneServerId);
        }

        /// <summary>
        /// Selects all Zone Servers and returns a list containing all <see cref="ZoneServerModel"/>.
        /// </summary>
        public ImmutableList<ZoneServerModel> GetZoneServers()
        {
            using var context = new AuthContext(config);
            return context.ZoneServer
                .AsNoTracking()
                .ToImmutableList();
        }

        /// <summary>
        /// Global admin entries from the <c>admin</c> table (same string form as .env ADMIN_*). Cached about a minute so
        /// every <c>Admin.IsAdmin</c> check is cheap; SQL inserts/deletes take effect within that window without a restart.
        /// </summary>
        public IReadOnlyList<string> GetAdminEntries()
        {
            DateTime now = DateTime.UtcNow;
            lock (adminEntriesLock)
            {
                if (adminEntriesCache != null && now - adminEntriesCachedAt < AdminEntriesRecheck)
                    return adminEntriesCache;
            }
            ImmutableList<string> next;
            try
            {
                using var context = new AuthContext(config);
                next = context.Admin.AsNoTracking()
                    .Select(a => a.Entry)
                    .AsEnumerable()
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .ToImmutableList();
            }
            catch (Exception e)
            {
                log.Warn($"Admin entries could not be read ({e.Message}); using the last cache or none.");
                lock (adminEntriesLock)
                    return adminEntriesCache ?? ImmutableList<string>.Empty;
            }
            lock (adminEntriesLock)
            {
                adminEntriesCache = next;
                adminEntriesCachedAt = now;
                return next;
            }
        }

        private static readonly object adminEntriesLock = new();
        private static ImmutableList<string> adminEntriesCache;
        private static DateTime adminEntriesCachedAt;
        private static readonly TimeSpan AdminEntriesRecheck = TimeSpan.FromMinutes(1);
    }
}
