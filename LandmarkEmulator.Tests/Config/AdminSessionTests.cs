using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace LandmarkEmulator.Tests.Config
{
    /// <summary>
    /// Admin web logins (Game/AdminSessions.cs, WebPages.AdminRequest): the in-game /admin?t=token URL is exchanged once
    /// for an HttpOnly, SameSite=Strict cookie and a redirect without the token; after that the admin pages and
    /// /api/admin/… answer the cookie only (never a URL token), every admin POST needs the page's anti-forgery token, and
    /// a login ends with its lifetime or when the admin leaves the world.
    /// </summary>
    public class AdminSessionTests
    {
        // ---- AdminSessions on its own ---------------------------------------------------------------------------------

        [Fact]
        public void ASession_NamesItsToken_UntilItsLifetimeEnds()
        {
            var sessions = new AdminSessions();
            DateTime now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            string id = sessions.Create("token-a", now);
            Assert.Equal(64, id.Length);
            Assert.NotEqual(id, sessions.Create("token-a", now));
            Assert.Equal("token-a", sessions.TokenOf(id, now.AddMinutes(59)));
            Assert.Null(sessions.TokenOf(id, now + AdminSessions.Lifetime));
            Assert.Null(sessions.TokenOf(id, now), "an expired session is gone for good");
            Assert.Null(sessions.TokenOf("", now));
            Assert.Null(sessions.TokenOf("not-a-session", now));
            string other = sessions.Create("token-b", now);
            sessions.End(other);
            Assert.Null(sessions.TokenOf(other, now));
        }

        [Fact]
        public void TheExchange_RefusesNonAdminTokens_AndSetsAStrictHttpOnlyCookie()
        {
            var sessions = new AdminSessions();
            DateTime now = DateTime.UtcNow;
            Assert.Null(sessions.Exchange("player", "/admin", 19001, t => false, now));
            Assert.Null(sessions.Exchange("", "/admin", 19001, t => true, now));
            var exchange = sessions.Exchange("admin-token", "/admin/settings", 19001, t => t == "admin-token", now).Value;
            Assert.Equal("/admin/settings", exchange.Location);
            Assert.False(exchange.Location.Contains("admin-token") || exchange.Cookie.Contains("admin-token"), "the token is in the redirect");
            Assert.Contains("Set-Cookie: lm_admin_19001=" + exchange.Session + ";", exchange.Cookie);
            Assert.Contains("HttpOnly", exchange.Cookie);
            Assert.Contains("SameSite=Strict", exchange.Cookie);
            Assert.Contains("Path=/", exchange.Cookie);
            Assert.Contains($"Max-Age={(int)AdminSessions.Lifetime.TotalSeconds}", exchange.Cookie);
            Assert.Equal("admin-token", sessions.TokenOf(exchange.Session, now));
        }

        [Fact]
        public void Cookies_AreReadByName_AndKeptApartPerWorldPort()
        {
            Assert.Equal("abc", AdminSessions.ReadCookie("x=1; lm_admin_19001=abc; y=2", "lm_admin_19001"));
            Assert.Null(AdminSessions.ReadCookie("lm_admin_19002=abc", "lm_admin_19001"));
            Assert.Null(AdminSessions.ReadCookie(null, "lm_admin_19001"));
            Assert.NotEqual(AdminSessions.CookieName(19001), AdminSessions.CookieName(19002));
        }

        [Fact]
        public void Logs_NeverCarryTheToken()
        {
            Assert.Equal("GET /admin?t=… HTTP/1.1", Call<string>("Redact", "GET /admin?t=0123abcd HTTP/1.1"));
            Assert.Equal("GET /api/me?x=1&t=… HTTP/1.1", Call<string>("Redact", "GET /api/me?x=1&t=ffee HTTP/1.1"));
        }

        // ---- Through the web server's routing -------------------------------------------------------------------------

        private static readonly Type Pages = typeof(WebPages);

        private static T Call<T>(string method, params object[] args)
            => (T)Pages.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

        private sealed record Answer(int Status, string Body, string[] Headers)
        {
            public string Header(string name) => Headers?.FirstOrDefault(h => h.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase));
        }

        private static Answer Request(string method, string path, Dictionary<string, string> form, string cookie = null, string origin = null)
        {
            Type info = Pages.GetNestedType("RequestInfo", BindingFlags.NonPublic)!;
            object request = Activator.CreateInstance(info, origin, "127.0.0.1:19001", true, cookie)!;
            Task task = (Task)Pages.GetMethod("Route", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { method, path, form ?? new Dictionary<string, string>(), request })!;
            task.GetAwaiter().GetResult();
            object response = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Type type = response.GetType();
            return new Answer((int)type.GetProperty("Status")!.GetValue(response)!,
                Encoding.UTF8.GetString((byte[])type.GetProperty("Body")!.GetValue(response)!),
                (string[])type.GetProperty("Headers")!.GetValue(response));
        }

        private static string CookieOf(Answer signIn)
        {
            string set = signIn.Header("Set-Cookie")!;
            return set["Set-Cookie:".Length..].Split(';')[0].Trim();
        }

        [Fact]
        public void TheInGameAdminUrl_SignsIn_ThenOnlyTheCookieOpensTheAdminPages()
        {
            using var world = new FakeWorld(c => c.Admins.Add("character:CookieAdmin"));
            WorldSession admin = world.Session(0x7100, "CookieAdmin");
            WorldSession player = world.Session(0x7101, "CookiePlayer");
            Store.SignIn(admin);
            Store.SignIn(player);
            try
            {
                string token = Store.TokenOf(admin);
                string playerToken = Store.TokenOf(player);

                // A player's token opens nothing and gets no cookie.
                Answer refused = Request("GET", "/admin", new() { ["t"] = playerToken });
                Assert.Equal(403, refused.Status);
                Assert.Null(refused.Header("Set-Cookie"));

                // The admin's token: a redirect to the page without it, and the cookie.
                Answer signIn = Request("GET", "/admin", new() { ["t"] = token });
                Assert.Equal(302, signIn.Status);
                Assert.Equal("Location: /admin", signIn.Header("Location"));
                Assert.Contains("HttpOnly", signIn.Header("Set-Cookie"));
                Assert.Contains("SameSite=Strict", signIn.Header("Set-Cookie"));
                string cookie = CookieOf(signIn);

                // The pages open with the cookie, never with a URL token alone.
                foreach (string page in new[] { "/admin", "/admin/monitor", ServerSettingsPage.PagePath })
                {
                    Assert.Equal(200, Request("GET", page, null, cookie).Status);
                    Assert.Equal(403, Request("GET", page, null).Status);
                }
                Assert.Equal(403, Request("POST", "/api/admin/players", new() { ["t"] = token }).Status);
                Assert.Equal(403, Request("GET", ServerSettingsPage.ApiPath, new() { ["t"] = token }).Status);
                Assert.NotEqual(403, Request("GET", ServerSettingsPage.ApiPath, null, cookie).Status);
                // A forged cookie, or another world's, opens nothing.
                Assert.Equal(403, Request("GET", "/admin", null, AdminSessions.CookieName(19001) + "=" + new string('a', 64)).Status);

                // The menu's page carries its anti-forgery token, bound to this login; its POSTs need it.
                Answer menu = Request("GET", "/admin", null, cookie);
                string session = cookie.Split('=')[1];
                string csrf = ServerSettingsPage.CsrfToken(session);
                Assert.Contains("var csrf='" + csrf + "'", menu.Body);
                Assert.False(menu.Body.Contains("__CSRF__"));
                Assert.Equal(403, Request("POST", "/api/admin/players", null, cookie).Status);
                Assert.Equal(403, Request("POST", "/api/admin/players", new() { ["csrf"] = ServerSettingsPage.CsrfToken("other") }, cookie).Status);
                Assert.Equal(403, Request("POST", "/api/admin/players", new() { ["csrf"] = csrf }, cookie, "http://evil.example").Status);
                Answer players = Request("POST", "/api/admin/players", new() { ["csrf"] = csrf }, cookie, "http://127.0.0.1:19001");
                Assert.Equal(200, players.Status);
                Assert.Contains("\"ok\":true", players.Body);

                // Item browse APIs: same cookie + CSRF rules; empty search is ok with no definitions loaded in this harness.
                Assert.Equal(403, Request("POST", "/api/admin/items", new() { ["q"] = "pick" }, cookie).Status);
                Assert.Equal(403, Request("POST", "/api/admin/item-categories", null, cookie).Status);
                Answer items = Request("POST", "/api/admin/items", new() { ["csrf"] = csrf, ["q"] = "pick" }, cookie, "http://127.0.0.1:19001");
                Assert.Equal(200, items.Status);
                Assert.Contains("\"ok\":true", items.Body);
                Assert.Contains("\"items\":", items.Body);
                Answer cats = Request("POST", "/api/admin/item-categories", new() { ["csrf"] = csrf }, cookie, "http://127.0.0.1:19001");
                Assert.Equal(200, cats.Status);
                Assert.Contains("\"ok\":true", cats.Body);
                Assert.Contains("\"categories\":", cats.Body);
                Assert.Contains("lmAdminCmdHistory", menu.Body);
                Assert.Contains("loadCreatures", menu.Body);
                Answer creatures = Request("POST", "/api/admin/creatures", new() { ["csrf"] = csrf }, cookie, "http://127.0.0.1:19001");
                Assert.Equal(200, creatures.Status);
                Assert.Contains("\"ok\":true", creatures.Body);
                Assert.Contains("\"id\":\"kobold\"", creatures.Body);
                Assert.Contains("\"id\":\"wolf\"", creatures.Body);
                Assert.Contains("\"id\":\"fungus\"", creatures.Body);
                Assert.Equal(403, Request("POST", "/api/admin/creatures", null, cookie).Status);

                // The login ends when the admin leaves the world (their Showcase token goes).
                Store.Forget(admin);
                Assert.Equal(403, Request("GET", "/admin", null, cookie).Status);
            }
            finally
            {
                Store.Forget(admin);
                Store.Forget(player);
            }
        }
    }
}
