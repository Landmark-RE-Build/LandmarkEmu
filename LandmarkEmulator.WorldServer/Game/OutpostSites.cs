using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The outposts' walk-on banners (features audit #60, OPEN-LEADS L102; trace/0695/agent-outposts.md): near an
    /// outpost's edge its name shows in white with its creator and date; to see it again, step back, wait a moment and
    /// come closer (wiki "Outposts"). That is the
    /// client's own build-site walk-on window (as3_695 ClaimWalkOnTextWindow): shown when the player enters a site's box
    /// (0.695 FUN_142dc7540 fills GameData.ClaimDialogs.WalkOnText from the claim entry: ClaimName = entry +0x10,
    /// WelcomeMessage = +0x28), shown again after leaving and coming back (CanAnimate). So each outpost is sent as a site
    /// record (ClaimsBase AOIUpdate, <see cref="Claims.SendDetachedSites"/>) around its ruin.
    /// <para>
    /// The plain banner (DefaultContent: name and welcome message in white), not the contest one: an admin template guid
    /// (record +0x158, the Landmarks of Landmark's "fancy blue header banner") makes the client refuse every claim
    /// permission to non-staff players inside the site (FUN_142d98cc0 returns 1 when entry +0x138 is set and the player's
    /// SendSelf +0x287 admin-bypass flag is clear, for any mask), and the prop interaction pass checks "use" (mask 2,
    /// FUN_1431b47c0 → FUN_142fb1a70 → FUN_142db4b50) at a prop's position — unless the entity's +0x1f8 is the "none"
    /// value, which is not settled for the server's lightweight props — so it could switch off the Lost Crystal and the
    /// Lost Chest. Default Access is Play Around (0x003: use, as on a new player site): the client
    /// then refuses building, props, decorating and copying there by itself (masks 0x10 / 8 / 0x100), and use is allowed.
    /// </para>
    /// <para>
    /// The records go to the client only: the server's own site list (Claims) never holds them, so digging (mining keeps
    /// the open-world rule; the stamped ruin heals back), creature spawning, daily goals, site search and the server's
    /// placement buffer are unchanged. The client's 285 m root-site buffer is three-dimensional (|dy| &lt; 90 + 285 m) and
    /// the caverns lie far below any surface site (MinTravelDepth −650 m vs ground ~1000 m), so it refuses nothing. Sent
    /// only while the outpost is streamed to the player (Outposts ViewRange 160 m), all changes of one tick in one packet,
    /// and removed when it is dropped; a full claims update (login, re-zone) wipes the client's list, so they are sent
    /// again (<see cref="Reset"/>).
    /// </para>
    /// <para>
    /// Name, creator and date: the wiki's "Outposts A-Z (List)" ("Abandoned Bunker by Hadder on 6-20-2016": 30 of the 35
    /// ruins), with the client's own spellings where its texts carry the build's name or creator (the cavern-ruin design
    /// texts, e.g. 567397 "Pausania's Eye", 567064 "Khalimzul", 567664 "Portavar"), and the client's texts for four the wiki
    /// doesn't list (576273 Dark Arrow Skyway, 576293 / 576294 Raid at the Coliseum / Ashlyn, 567060 / 567061 Shining
    /// Shrine / Ginsan, 567066 / 567067 Trapped Soul / Maxximus). [I]: ruin_ahnzul_market is "Longshade Trading Post" (the
    /// wiki's Ahnzul outpost that is not Seat of Power; text 576278); ruin_ahnzul_ancestralshrine's title is its file name;
    /// five dates are not found yet (no date in the record then). The wording of the two lines is ours [I]: the walk-on
    /// window has no date field, so the date goes into the welcome message.
    /// </para>
    /// </summary>
    public static class OutpostSites
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Site ids and their owner: "TS" in the top bytes + outpost index; no database id or character is near.</summary>
        private const ulong SiteIdBase = 0x5453_0000_0000_0000;
        public const ulong OwnerGuid = SiteIdBase;
        /// <summary>Play Around (0x003), like a new site: use allowed, no building, props, decorating or copying.</summary>
        private const uint Access = 0x003;
        /// <summary>
        /// How far the box reaches below the ruin's slot, decimetres [I]: the slot sits 8 m above the cavern's bottom
        /// (ruin-stamping.md §5), so a player walking up on the cave floor meets the banner at the ruin's edge.
        /// </summary>
        private const double Below = 200;

        /// <summary>One ruin's credits: title, creator(s), date shown (M-D-YYYY as the wiki copied it), null when not found yet.</summary>
        private sealed record Credit(string Title, string Creator, string Date);

        private static readonly Dictionary<string, Credit> credits = new(StringComparer.Ordinal)
        {
            ["ruin_adjorr_frozenteleporter"] = new("Frozen Teleporter", "Adjorr", "4-6-2016"),
            ["ruin_ahnzul_ancestralshrine"] = new("Ancestral Shrine", "Ahnzul", null),                 // [I] title from the file name
            ["ruin_ahnzul_market"] = new("Longshade Trading Post", "Ahnzul", "6-24-2016"),             // [I] matched by creator
            ["ruin_ahnzul_seatofpower"] = new("Seat of Power", "Ahnzul", "6-22-2016"),
            ["ruin_ansgar_darkarrowskyway"] = new("Dark Arrow Skyway", "Ansgar", null),               // text 576273
            ["ruin_ashlyn_coliseumraid"] = new("Raid at the Coliseum", "Ashlyn", null),               // texts 576293 / 576294
            ["ruin_basikle_handofbrog"] = new("Hand of Brog", "Basikle", "6-20-2016"),
            ["ruin_butchywuff_shrineofthelotus"] = new("Shrine of the Lotus", "Butchywuff", "4-6-2016"),
            ["ruin_ceijay_fallenpridekeep"] = new("Fallenpride Keep", "Ceijay", "6-20-2016"),
            ["ruin_dhara_ironorder"] = new("The Iron Order Hall", "D'hara and FryCaptain", "6-22-2016"),
            ["ruin_diamondfox_ebonglowtower"] = new("Ebonglow Tower", "Diamondfox", "6-22-2016"),
            ["ruin_durwin_bridgetonowhere"] = new("Bridge to Nowhere", "Durwin", "6-22-2016"),
            ["ruin_endru_genesisstone"] = new("The Genesis Stone", "Endru", "6-22-2016"),
            ["ruin_fazz_ancientorgan"] = new("Ancient Rock Organ", "Fazz", "6-20-2016"),
            ["ruin_felgon_tomb"] = new("Tomb of the Unknown King", "Felgon", "6-20-2016"),
            ["ruin_ginsan_houseofelders"] = new("House of the Elders", "Ginsan", "4-25-2016"),
            ["ruin_ginsan_shiningshrine"] = new("Shining Shrine", "Ginsan", null),                    // texts 567060 / 567061
            ["ruin_hadder_bunker"] = new("Abandoned Bunker", "Hadder", "6-20-2016"),
            ["ruin_hanna_pausaniaseye"] = new("Pausania's Eye", "Hanna", "6-22-2016"),                // wiki "Pausainia's"
            ["ruin_khalimzul_curseofthebetrayer"] = new("Curse of the Betrayer", "Khalimzul", "6-22-2016"),
            ["ruin_khalimzul_iceboundchalice"] = new("The Icebound Chalice", "Khalimzul", "6-22-2016"), // wiki "Khalimazul"
            ["ruin_koda_greathall"] = new("The Great Hall", "Koda", "4-6-2016"),
            ["ruin_maxximus_trappedsoul"] = new("Trapped Soul", "Maxximus", null),                    // texts 567066 / 567067
            ["ruin_osmotar_grimstonehall"] = new("Grimstone Hall", "Osmotar", "6-22-2016"),
            ["ruin_poem_crag"] = new("Crag", "Poem", "6-22-2016"),
            ["ruin_poem_lifesong"] = new("The Lifesong", "Poem", "6-22-2016"),
            ["ruin_portavar_refinery"] = new("Abandoned Refinery", "Portavar", "6-22-2016"),          // wiki "Portavaron"
            ["ruin_raenin_lostmission"] = new("Lost Mission", "Raenin", "4-6-2016"),
            ["ruin_schag_voxelmancerend"] = new("Voxelmancer's End", "Schag", "6-22-2016"),
            ["ruin_shattari_hunterslodge"] = new("Horned Owl Hunting Lodge", "Shattari", "4-28-2016"),
            ["ruin_shattari_magrail"] = new("Forgotten Magrail", "Shattari", "6-22-2016"),
            ["ruin_tenma_chompnstompcathedral"] = new("Chomp and Stomp Cathedral", "Tenma", "6-20-2016"),
            ["ruin_thrift_takingacloserlook"] = new("Taking a Closer Look", "Thrift", "6-22-2016"),
            ["ruin_woodstein_clockworkcalamity"] = new("Clockwork Calamity", "Woodstein", "6-1-2016"),
            ["ruin_xantheos_abandonedraildepot"] = new("Abandoned Rail Depot", "Xantheos", "6-2-2016"),
        };

        /// <summary>The banner's name of a ruin ("Abandoned Bunker"), or null for a ruin not in the list.</summary>
        public static string TitleOf(string ruin) => credits.TryGetValue(ruin ?? "", out Credit c) ? c.Title : null;

        /// <summary>The welcome message under the name [I]: "Created by Hadder on 6-20-2016".</summary>
        public static string LineOf(string ruin)
        {
            if (!credits.TryGetValue(ruin ?? "", out Credit c))
                return "";
            return c.Date == null ? $"Created by {c.Creator}" : $"Created by {c.Creator} on {c.Date}";
        }

        private static readonly ConcurrentDictionary<string, (uint X, uint Y, uint Z)> sizes = new();
        private static readonly ConcurrentDictionary<int, ClaimModel> sites = new();
        private static readonly object sync = new();
        /// <summary>The outpost sites each client holds (indices).</summary>
        private static readonly Dictionary<WorldSession, HashSet<int>> shown = new();

        /// <summary>The site id of an outpost index.</summary>
        public static ulong IdOf(int index) => SiteIdBase | (uint)index;

        /// <summary>Is this one of the outposts' banner sites?</summary>
        public static bool IsSite(ulong id) => (id & 0xFFFF_0000_0000_0000) == SiteIdBase && id != SiteIdBase;

        /// <summary>
        /// An outpost's site record: the ruin's footprint (the slot: corner + its border voxel, the template's size),
        /// from <see cref="Below"/> under its base to the template's top. Null when the ruin's size can't be read.
        /// </summary>
        private static ClaimModel SiteOf(Outposts.Outpost o)
        {
            if (sites.TryGetValue(o.Index, out ClaimModel site))
                return site;
            (uint X, uint Y, uint Z) size;
            try
            {
                size = sizes.GetOrAdd(o.Ruin, r => VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, r + ".otd")));
            }
            catch (Exception e)
            {
                log.Warn($"Outpost {o.Index} ({o.Ruin}): no banner site ({e.Message}).");
                return null;
            }
            double voxel = TerrainEdits.VoxelSize;
            site = new ClaimModel
            {
                Id = IdOf(o.Index),
                OwnerGuid = OwnerGuid,
                Name = TitleOf(o.Ruin) ?? o.Name,
                Description = LineOf(o.Ruin),
                MinX = (o.Corner.X + 1) * voxel,
                MinY = (o.Corner.Y + 1) * voxel - Below,
                MinZ = (o.Corner.Z + 1) * voxel,
                MaxX = (o.Corner.X + 1 + size.X) * voxel,
                MaxY = (o.Corner.Y + 1 + size.Y) * voxel,
                MaxZ = (o.Corner.Z + 1 + size.Z) * voxel,
                ZoneId = o.Zone,
                DefaultPermissions = Access
            };
            return sites.GetOrAdd(o.Index, site);
        }

        /// <summary>
        /// World tick (Outposts.Stream): the client gets the site of every outpost it is streamed (<paramref name="streamed"/>,
        /// outpost indices) and loses those no longer streamed — one AOIUpdate for all of this tick's changes, none when
        /// nothing changed.
        /// </summary>
        public static void Sync(WorldSession session, HashSet<int> streamed)
        {
            HashSet<int> have;
            lock (sync)
                if (!shown.TryGetValue(session, out have))
                    shown[session] = have = new HashSet<int>();
            List<ClaimModel> added = null;
            List<ulong> removed = null;
            foreach (int index in streamed)
                if (!have.Contains(index) && Outposts.ByIndex(index) is Outposts.Outpost o && SiteOf(o) is ClaimModel site)
                {
                    have.Add(index);
                    (added ??= new List<ClaimModel>()).Add(site);
                }
            foreach (int index in have.Where(i => !streamed.Contains(i)).ToList())
            {
                have.Remove(index);
                (removed ??= new List<ulong>()).Add(IdOf(index));
            }
            if (added != null || removed != null)
                Claims.SendDetachedSites(session, (IReadOnlyCollection<ClaimModel>)added ?? Array.Empty<ClaimModel>(),
                    (IReadOnlyCollection<ulong>)removed ?? Array.Empty<ulong>());
        }

        /// <summary>A full claims update replaced the client's list (login, re-zone): the outposts' sites are sent again.</summary>
        public static void Reset(WorldSession session)
        {
            lock (sync)
                shown.Remove(session);
        }

        public static void Forget(WorldSession session) => Reset(session);

        /// <summary>
        /// World thread: an outpost leaves its cavern for another ruin (Outposts.Rotation.cs): its cached record goes (the
        /// index comes back with another ruin's name and box), and any client still holding it loses it.
        /// </summary>
        public static void Drop(int index)
        {
            sites.TryRemove(index, out _);
            List<WorldSession> holders;
            lock (sync)
            {
                holders = shown.Where(s => s.Value.Remove(index)).Select(s => s.Key).ToList();
            }
            foreach (WorldSession session in holders)
                Claims.SendDetachedSites(session, Array.Empty<ClaimModel>(), new[] { IdOf(index) });
        }
    }
}
