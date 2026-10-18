using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A build site that lapses (its owner away longer than the upkeep allows, <see cref="Claims.Update"/>) goes back to
    /// nature like a deleted one (TASKS 6.39):
    /// <list type="bullet">
    /// <item>a design of the owner's build is saved, with the site's screenshots (<see cref="SiteDesigns.Save"/>; hotfix
    /// 1 Apr 2016, P4365);</item>
    /// <item>its terrain reverts;</item>
    /// <item>a design of the site and all its resources are mailed to the owner (21 Mar 2016,
    /// P4133; client text 562109 "a Design of each Build Site is created, all resources and props are returned to you via
    /// mail attachment"), and the props come back as materials, not as overflow items (26 Apr 2016, P4870):
    /// the materials of the owner's voxels (<see cref="SiteDesigns.RefundItems"/>, by Voxel Vision ownership) and of
    /// their props' recipes (<see cref="Props.StowInBox"/>) are mailed (<see cref="MailResources"/>);</item>
    /// <item>chests that still hold items are packed away (<see cref="Props.StowInBox"/>); the expiry letter carries the
    /// Build Site Contents item (914, "Right-click this item to retrieve your resources and props."), and using it
    /// returns those chests as materials with their contents in the overflow tab, plus the Build Site Flag (0.589).</item>
    /// </list>
    /// Pending sites are <see cref="Unlocks.SiteContents"/> rows, so it survives restarts.
    /// <para>
    /// <b>Inferred</b>: live's own split between letter attachments and the Contents item is not known (its letter text
    /// 562150 / 563062 is cut off in the client after "Right-click the attachment below to retrieve you…"). Resources
    /// travel as plain attachments, four to a letter (the mail window's limit, Mail.MaxAttachments), because nothing
    /// else here can hold an offline player's item counts without a new table; the chests need their owner online.
    /// </para>
    /// </summary>
    public static class SiteContents
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint ContentsItem = 914;

        /// <summary>Client text 562148 "Build Site Management": the sender of the site letters (as SiteUpkeep's).</summary>
        private const uint TextFrom = 562148;

        /// <summary>Attachments per letter: the client mail window's four slots (Mail.MaxAttachments).</summary>
        private const int AttachmentsPerLetter = 4;

        /// <summary>
        /// Reverts a lapsed site, mails its resources and packs its full chests for its (offline) owner. The letter that
        /// carries the Build Site Contents item is the upkeep one (<see cref="SiteUpkeep.MailExpired"/>) unless the caller
        /// names another (<paramref name="letter"/>: owner guid, owner name; e.g. an event island that closed,
        /// <see cref="SiteUpkeep.MailIslandClosed"/>); it must attach <see cref="ContentsItem"/> (<see cref="Deliver"/>).
        /// </summary>
        public static void Pack(ClaimModel claim, System.Action<ulong, string> letter = null)
        {
            // "A Design of each expired Build Site has been saved": the owner's build, before the ground comes back.
            var (_, built) = SiteDesigns.Save(claim);
            var resources = SiteDesigns.RefundItems(built);
            var reverted = Terrain.EnvironmentServer.Instance.RevertBox(claim.MinX, claim.MinY, claim.MinZ, claim.MaxX, claim.MaxY, claim.MaxZ, claim.ZoneId);
            Terrain.ZoneTerrain.Broadcast(reverted, claim.ZoneId);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            int props = Props.StowInBox(claim.OwnerGuid,
                new Vector3((float)((claim.MinX - origin.X) / 10), (float)((claim.MinY - origin.Y) / 10), (float)((claim.MinZ - origin.Z) / 10)),
                new Vector3((float)((claim.MaxX - origin.X) / 10), (float)((claim.MaxY - origin.Y) / 10), (float)((claim.MaxZ - origin.Z) / 10)),
                claim.ZoneId, resources);
            // The site's unplaced inventory items (ClaimInventory, L146) go in the same letters, as a delete returns them.
            resources.AddRange(ClaimInventory.TakeUnplaced(claim));
            Unlocks.Add(claim.OwnerGuid, Unlocks.SiteContents, (uint)claim.Id);
            string owner = DatabaseManager.Instance.CharacterDatabase.GetCharacterById(claim.OwnerGuid).GetAwaiter().GetResult()?.Name ?? "";
            (letter ?? SiteUpkeep.MailExpired)(claim.OwnerGuid, owner);
            int letters = MailResources(claim.OwnerGuid, owner, claim.Name, resources);
            log.Info($"Site {claim.Id} lapsed: {reverted.Count} cell(s) reverted, {props} prop(s) taken off, "
                + $"{resources.Sum(r => (long)r.Count)} resource item(s) mailed in {letters} letter(s) to 0x{claim.OwnerGuid:X}.");
        }

        /// <summary>
        /// Mails a lapsed site's resources to its owner: the same item merged, at most <see cref="AttachmentsPerLetter"/>
        /// stacks per letter, from "Build Site Management". The letter's wording is ours (the client has no text for it).
        /// Returns how many letters.
        /// </summary>
        public static int MailResources(ulong ownerGuid, string ownerName, string siteName, List<(uint Item, uint Count)> resources)
        {
            var merged = resources.Where(r => r.Item != 0 && r.Count > 0).GroupBy(r => r.Item)
                .Select(g => (Item: g.Key, Count: (uint)System.Math.Min(g.Sum(r => (long)r.Count), uint.MaxValue))).ToList();
            if (merged.Count == 0)
                return 0;
            string from = TextManager.Instance.GetTextForId(TextFrom) ?? "Build Site Management";
            int letters = (merged.Count + AttachmentsPerLetter - 1) / AttachmentsPerLetter;
            string site = string.IsNullOrWhiteSpace(siteName) ? "your Build Site" : $"\"{siteName}\"";
            for (int i = 0; i < letters; i++)
            {
                var part = merged.Skip(i * AttachmentsPerLetter).Take(AttachmentsPerLetter).Select(r => (r.Item, r.Count)).ToList();
                Social.SocialServices.Service.SendSystemMail(ownerGuid, from,
                    letters == 1 ? "Your Build Site resources" : $"Your Build Site resources ({i + 1}/{letters})",
                    $"Dear {ownerName},<br><br>Here are the resources from {site}: the materials of everything you built and "
                    + "of your props. Take the attachments to put them in your inventory.", 0, part);
            }
            return letters;
        }

        /// <summary>
        /// At world entry: a player with packed site contents who has neither the Build Site Contents item nor the expiry
        /// letter carrying it (<see cref="SiteUpkeep.MailExpired"/>) gets the item directly.
        /// </summary>
        public static void Deliver(WorldSession session)
        {
            if (Unlocks.Values(session.PlayerGuid, Unlocks.SiteContents).Count == 0
                || session.Inventory.Items.Any(i => i.ItemDefinitionId == ContentsItem)
                || Social.SocialServices.Service.HasMailAttachment(session.PlayerGuid, ContentsItem))
                return;
            session.GiveItem(ContentsItem, 1);
            Chat.System(session, "Your Build Site lapsed while you were away. Its resources were mailed to you; use the Build Site Contents in your bag for the rest.");
        }

        /// <summary>
        /// Using Build Site Contents: the packed chests as materials (their contents to overflow, <see cref="Props.Unstow"/>).
        /// No Build Site Flag comes back: sites are slots, and the lapsed site's slot is already free (0.589 gave one flag
        /// per lapsed site).
        /// </summary>
        public static bool TryUse(WorldSession session, CharacterItemModel item)
        {
            if (item.ItemDefinitionId != ContentsItem)
                return false;
            var sites = Unlocks.Values(session.PlayerGuid, Unlocks.SiteContents);
            if (!session.TakeItem(ContentsItem, 1))
                return true;
            int props = Props.Unstow(session);
            Unlocks.Clear(session, Unlocks.SiteContents);
            Chat.System(session, sites.Count == 0 && props == 0 ? "There was nothing left to retrieve."
                : $"Retrieved {props} prop(s) as materials (chest contents are in your overflow tab).");
            log.Info($"{session.CharacterName} opened Build Site Contents: {props} prop(s), {sites.Count} flag(s).");
            return true;
        }
    }
}
