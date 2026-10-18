using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Admin web logins (/admin, /admin/monitor, /admin/settings and /api/admin/…). The in-game /admin command opens
    /// /admin?t=&lt;the admin's Showcase token&gt; (<see cref="Admin.Enter"/>); that URL token would stay in the in-game
    /// browser's history and in logs, so it is used once: the world answers with a redirect to the same page without it
    /// and an HttpOnly, SameSite=Strict cookie naming a random admin session (<see cref="Exchange"/>), and every admin page
    /// and request after that is checked by the cookie only (a URL token is refused there). A session lasts
    /// <see cref="Lifetime"/> from its start and only while its admin is online and still an admin (it names their Showcase
    /// token, which ends when they leave the world); typing /admin again in game starts a new one.
    /// </summary>
    public sealed class AdminSessions
    {
        /// <summary>How long one admin web login lasts.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

        /// <summary>Most sessions kept at once (each needs a live admin token to make; old ones go first).</summary>
        public const int MaxSessions = 1000;

        public static AdminSessions Instance { get; } = new();

        private readonly ConcurrentDictionary<string, (string Token, DateTime Expires)> sessions = new(StringComparer.Ordinal);

        /// <summary>The cookie's name for a web port (cookies don't keep ports apart: two worlds on one host each have their own).</summary>
        public static string CookieName(int port) => $"lm_admin_{port}";

        /// <summary>A new session for an admin's Showcase token (the caller has checked it is an admin's).</summary>
        public string Create(string showcaseToken, DateTime now)
        {
            foreach (var (id, entry) in sessions)
                if (entry.Expires <= now)
                    sessions.TryRemove(id, out _);
            while (sessions.Count >= MaxSessions)
            {
                string oldest = sessions.OrderBy(s => s.Value.Expires).Select(s => s.Key).FirstOrDefault();
                if (oldest == null || !sessions.TryRemove(oldest, out _))
                    break;
            }
            string session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            sessions[session] = (showcaseToken, now + Lifetime);
            return session;
        }

        /// <summary>The Showcase token a live session names (null = none, unknown or expired).</summary>
        public string TokenOf(string session, DateTime now)
        {
            if (string.IsNullOrEmpty(session) || !sessions.TryGetValue(session, out var entry))
                return null;
            if (entry.Expires <= now)
            {
                sessions.TryRemove(session, out _);
                return null;
            }
            return entry.Token;
        }

        /// <summary>Ends a session.</summary>
        public void End(string session)
        {
            if (!string.IsNullOrEmpty(session))
                sessions.TryRemove(session, out _);
        }

        /// <summary>One cookie's value from a Cookie header ("a=1; lm_admin_19001=…"), or null.</summary>
        public static string ReadCookie(string header, string name)
        {
            foreach (string part in (header ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part[..eq].Trim() == name)
                    return part[(eq + 1)..].Trim();
            }
            return null;
        }

        /// <summary>The Set-Cookie header for a session: HttpOnly (no script reads it), SameSite=Strict (no other site sends it), Path=/.</summary>
        public static string SetCookie(string name, string session)
            => $"Set-Cookie: {name}={session}; Path=/; Max-Age={(int)Lifetime.TotalSeconds}; HttpOnly; SameSite=Strict";

        /// <summary>
        /// GET of an admin page with ?t=: a session for that token's admin and the redirect that drops the token from the
        /// URL; null when the token isn't an online admin's (<paramref name="isAdminToken"/>).
        /// </summary>
        public (string Session, string Location, string Cookie)? Exchange(string showcaseToken, string path, int port,
            Func<string, bool> isAdminToken, DateTime now)
        {
            if (string.IsNullOrEmpty(showcaseToken) || !isAdminToken(showcaseToken))
                return null;
            string session = Create(showcaseToken, now);
            return (session, path, SetCookie(CookieName(port), session));
        }
    }
}
