using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Showcase (HUD "Showcase" button = the InGameMarketplaceBrowser window, title "Showcase"): the client's
    /// embedded Chromium browser shows ClientConfig [Marketplace] MarketplaceUrl, opened at "#!/marketplace" or
    /// "#!/gallery" (Browser.OpenMarketplace / OpenGallery, FUN_143530290 / FUN_143530100) or at
    /// "/#!/marketplace/search/limit=50&amp;filterEquals=Purchasable:1&amp;SearchTags=..." (OpenMarketplaceWithTags). The
    /// official pages lived on Daybreak's store servers, which also delivered what was bought; the client data has no
    /// catalogue (Resources/Marketing tables are empty). Here the world server serves those pages itself
    /// (<see cref="WebPages"/>) and the store is free for now: every item listed can be taken.
    ///
    /// <para>Who is buying: the page only reaches the web server, so the server gives the player's browser a sign-in
    /// URL with a per-session token: client Lua SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL(url) through
    /// <see cref="ClientLua.RunCode"/> (ShowURL also queues the URL when the browser doesn't exist yet). The browser only exists after the
    /// client's login preload (Browser.Create, which loads MarketplaceUrl#!/gallery), so the sign-in goes out
    /// <see cref="SignInDelay"/> after world entry, and again when the player opens the Showcase (UI event 7) while
    /// the page hasn't confirmed it yet (/api/me with the token). The page keeps the token in its sessionStorage (one
    /// per client process, so two clients on one PC stay apart) and sends it with every request; the client's own
    /// navigations reuse the same browser and keep it. A token dies with the session.</para>
    ///
    /// <para>Catalogue: cosmetic outfits (equipment slot 1), decorations (prop items in the builder's prop categories
    /// PropCategory / PropCategoryPropMap / PropCategoryGroup; uncategorised props such as game markers and crafting
    /// stations stay out) and designs (design items with a client template). Tools, weapons, armour and accessories
    /// (slot 3: combat trinkets) stay out too: they are crafted. Icons are the client's
    /// image sets exported by re/tools/export_icons.py into GameDataPath/icons.</para>
    /// </summary>
    public static class Store
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public record Item(uint Id, string Name, string Description, uint Icon, string Section, string Group);
        public record Site(ulong Id, string Name, string Owner, string Landscape, string Description, ulong Rating, bool Online);

        private static readonly object sync = new();
        private static readonly Dictionary<string, WorldSession> sessions = new();
        private static readonly Dictionary<WorldSession, string> tokens = new();
        private static readonly Dictionary<WorldSession, DateTime> lastGrant = new();
        /// <summary>Sign-ins waiting to be sent (due time), and players whose page confirmed its token.</summary>
        private static readonly Dictionary<WorldSession, DateTime> pendingSignIn = new();
        private static readonly HashSet<WorldSession> confirmed = new();
        private static readonly TimeSpan SignInDelay = TimeSpan.FromSeconds(10);
        public const uint ShowcaseOpenedEvent = 7;
        private static List<Item> catalogue;

        /// <summary>Most of one item a single request may take, and the pause between takes.</summary>
        public const uint MaxCount = 100;
        private static readonly TimeSpan GrantInterval = TimeSpan.FromMilliseconds(400);

        /// <summary>The address players' browsers reach the web pages at (WorldServer.json "WebPagesUrl").</summary>
        public static string BaseUrl
            => (SharedConfiguration.Configuration?.GetValue<string>("WebPagesUrl") ?? $"http://127.0.0.1:{WebPages.Port}").TrimEnd('/');

        /// <summary>World entry: a fresh token; the sign-in URL follows once the client's browser exists.</summary>
        public static void SignIn(WorldSession session)
        {
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            lock (sync)
            {
                if (tokens.Remove(session, out string old))
                    sessions.Remove(old);
                tokens[session] = token;
                sessions[token] = session;
                confirmed.Remove(session);
                pendingSignIn[session] = DateTime.UtcNow + SignInDelay;
            }
        }

        /// <summary>World thread: sends the sign-ins that are due.</summary>
        public static void Update()
        {
            List<WorldSession> due;
            lock (sync)
            {
                if (pendingSignIn.Count == 0)
                    return;
                DateTime now = DateTime.UtcNow;
                due = pendingSignIn.Where(p => p.Value <= now).Select(p => p.Key).ToList();
                foreach (WorldSession session in due)
                    pendingSignIn.Remove(session);
            }
            foreach (WorldSession session in due)
                SendSignIn(session);
        }

        /// <summary>The player opened the Showcase (UI event 7): sign the page in now if it hasn't confirmed yet.</summary>
        public static void UiEvent(WorldSession session, uint uiEvent)
        {
            if (uiEvent != ShowcaseOpenedEvent)
                return;
            lock (sync)
            {
                if (confirmed.Contains(session) || !tokens.ContainsKey(session))
                    return;
                pendingSignIn.Remove(session);
            }
            SendSignIn(session);
        }

        private static void SendSignIn(WorldSession session)
        {
            string token = TokenOf(session);
            if (token == null || session.Disconnected)
                return;
            ClientLua.RunCode(session, $"SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL('{BaseUrl}/?t={token}#!/marketplace')");
            ClientFixes.Apply(session);
        }

        /// <summary>The page loaded with this player's token (/api/me): no more sign-ins needed.</summary>
        public static void Confirm(WorldSession session)
        {
            lock (sync)
            {
                if (tokens.ContainsKey(session) && confirmed.Add(session))
                    log.Info($"Store: {session.CharacterName}'s Showcase page is signed in.");
            }
        }

        /// <summary>The player left the world: the token stops working.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                if (tokens.Remove(session, out string token))
                    sessions.Remove(token);
                lastGrant.Remove(session);
                pendingSignIn.Remove(session);
                confirmed.Remove(session);
            }
        }

        /// <summary>The online player a token belongs to, or null.</summary>
        public static WorldSession SessionOf(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            lock (sync)
                return sessions.TryGetValue(token, out WorldSession session) && !session.Disconnected ? session : null;
        }

        /// <summary>The token of an online player (tests: the harness reads it from the log instead).</summary>
        public static string TokenOf(WorldSession session)
        {
            lock (sync)
                return tokens.GetValueOrDefault(session);
        }

        private static readonly string[] SectionOrder = { "Outfits", "Decorations", "Designs" };

        /// <summary>The store's items (built once).</summary>
        public static List<Item> Catalogue()
        {
            if (catalogue != null)
                return catalogue;
            var items = new List<Item>();
            string Text(uint id) => id == 0 ? "" : TextManager.Instance.GetTextForId(id) ?? "";

            // Prop categories: top-level name for each prop (a child category reads "Parent: Child").
            var categoryName = GameDataFiles.Rows("PropCategory.txt").ToDictionary(r => r.U(0), r => Text(r.U(1)));
            var parentOf = new Dictionary<uint, uint>();
            foreach (string[] r in GameDataFiles.Rows("PropCategoryGroup.txt"))
                parentOf.TryAdd(r.U(1), r.U(0));
            var propCategory = new Dictionary<uint, string>();
            foreach (string[] r in GameDataFiles.Rows("PropCategoryPropMap.txt"))
            {
                uint category = r.U(1), prop = r.U(2);
                string name = categoryName.GetValueOrDefault(category, "");
                if (parentOf.TryGetValue(category, out uint parent) && categoryName.TryGetValue(parent, out string parentName)
                    && parentName.Length > 0)
                    name = name.Length > 0 ? $"{parentName}: {name}" : parentName;
                if (name.Length > 0)
                    propCategory.TryAdd(prop, name);
            }

            foreach (var d in Inventory.ItemDefinitionManager.All)
            {
                string name = Text(d.NameId).Trim();
                if (name.Length == 0 || name.StartsWith("<", StringComparison.Ordinal) || name.Contains("TEST", StringComparison.Ordinal))
                    continue;
                string section, group;
                if (d.EquipSlotId == 1)
                    (section, group) = ("Outfits", "Outfits");
                else if (d.PropId == Vendors.VendorProp)
                    (section, group) = ("Decorations", "Build Site Vendor");
                else if (d.PropId != 0 && d.EquipSlotId == 0 && propCategory.TryGetValue(d.PropId, out string category))
                    (section, group) = ("Decorations", category);
                else if (d.EquipSlotId == 0 && d.Abilities.Count == 0 && ItemUses.IsDesign(d.Id))
                    (section, group) = ("Designs", "Designs");
                else
                    continue;
                items.Add(new Item(d.Id, name, Plain(Text(d.DescriptionId)), d.ImageSetId, section, group));
            }
            catalogue = items.GroupBy(i => (i.Section, i.Name)).Select(g => g.OrderBy(i => i.Id).First())
                .OrderBy(i => Array.IndexOf(SectionOrder, i.Section)).ThenBy(i => i.Group).ThenBy(i => i.Name).ToList();
            log.Info($"Store catalogue: {catalogue.Count} items ({string.Join(", ", catalogue.GroupBy(i => i.Section).Select(g => $"{g.Count()} {g.Key}"))}).");
            return catalogue;
        }

        /// <summary>Client text without its markup (&lt;font color=...&gt;, &lt;br&gt;).</summary>
        private static string Plain(string text)
            => System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(text ?? "", "<br */?>", " "), "<[^>]*>", "").Trim();

        /// <summary>
        /// Gives a catalogue item (free). Runs on the world thread. Returns a short message for the page; the player
        /// also sees the item arrive in their bag.
        /// </summary>
        public static (bool Ok, string Message) Take(WorldSession session, uint itemId, uint count)
        {
            Item item = Catalogue().FirstOrDefault(i => i.Id == itemId);
            if (item == null)
                return (false, "That item isn't in the store.");
            count = Math.Clamp(count, 1, MaxCount);
            lock (sync)
            {
                if (lastGrant.TryGetValue(session, out DateTime last) && DateTime.UtcNow - last < GrantInterval)
                    return (false, "One moment...");
                lastGrant[session] = DateTime.UtcNow;
            }
            long space = session.Inventory.SpaceFor(itemId);
            if (space <= 0)
                return (false, "Your bag is full.");
            count = (uint)Math.Min(count, space);
            session.GiveItem(itemId, count);
            log.Info($"Store: {session.CharacterName} took {count} × {item.Name} ({itemId}).");
            return (true, count == 1 ? $"{item.Name} is in your bag." : $"{count} × {item.Name} are in your bag.");
        }

        /// <summary>
        /// The gallery: the build sites their owners published (Build Site Management › Publish to Gallery, a Player
        /// Studio listing; Commerce), best rated first.
        /// </summary>
        public static List<Site> Gallery()
        {
            var online = PlayerManager.Snapshot().Select(s => s.PlayerGuid).ToHashSet();
            var names = DatabaseManager.Instance.CharacterDatabase.GetAllCharacters().ToDictionary(c => c.Id, c => c.Name);
            // TODO: only published sites (Commerce.IsOnGallery) once Publish to Gallery works in the client.
            return Claims.All().Select(c => new Site(c.Id, c.Name ?? "", names.GetValueOrDefault(c.OwnerGuid, "?"),
                    LandscapeName(c.ZoneId), c.Description ?? "", Ratings.Total(c.Id), online.Contains(c.OwnerGuid)))
                .OrderByDescending(s => s.Rating).ThenBy(s => s.Name).ToList();
        }

        private static string LandscapeName(uint zone)
        {
            uint text = Landscapes.Get(zone)?.NameText ?? 0;
            string name = text == 0 ? "" : TextManager.Instance.GetTextForId(text);
            return string.IsNullOrEmpty(name) ? $"Landscape {zone}" : name;
        }

        /// <summary>The gallery's Visit: travel to a build site (its Starting Location, else its centre). World thread.</summary>
        public static (bool Ok, string Message) Visit(WorldSession session, ulong siteId)
        {
            ClaimModel site = Claims.Get(siteId);
            if (site == null || Claims.ArrivalOf(site.OwnerGuid) is not (Vector3 arrival, uint zone, ulong claimId) || claimId != siteId)
                return (false, "That build site is gone.");
            StoryTools.ExpectArrival(session, siteId, respawn: false);
            Vector3 ground = Terrain.TerrainHeights.OnGround(arrival.X, arrival.Z, zone) ?? arrival;
            Teleport.ToZone(session, ground + new Vector3(0f, 1f, 0f), zone);
            log.Info($"Store: {session.CharacterName} visits build site {siteId} '{site.Name}'.");
            return (true, $"Travelling to {(string.IsNullOrEmpty(site.Name) ? "the build site" : site.Name)}...");
        }
    }
}
