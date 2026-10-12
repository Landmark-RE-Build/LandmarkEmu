using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A build site that lapses (its owner away longer than the upkeep allows, <see cref="Claims.Update"/>) goes back to
    /// nature like a deleted one: its terrain reverts and its props are packed away (<see cref="Props.StowInBox"/>). The
    /// owner gets a Build Site Contents item (914, "Right-click this item to retrieve your resources and props.") at
    /// their next login; using it returns the props' items and the Build Site Flag. Pending sites are
    /// <see cref="Unlocks.SiteContents"/> rows, so it survives restarts. (Voxel materials of the reverted terrain are not
    /// refunded: the server doesn't know which of them the player paid for.)
    /// </summary>
    public static class SiteContents
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint ContentsItem = 914;

        /// <summary>Reverts a lapsed site and packs its props for its (offline) owner.</summary>
        public static void Pack(ClaimModel claim)
        {
            // "A Design of each expired Build Site has been saved": the owner's build, before the ground comes back.
            SiteDesigns.Save(claim);
            var reverted = Terrain.TerrainEdits.RevertBox(claim.MinX, claim.MinY, claim.MinZ, claim.MaxX, claim.MaxY, claim.MaxZ, claim.ZoneId);
            Terrain.TerrainEdits.Broadcast(reverted, claim.ZoneId);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            int props = Props.StowInBox(claim.OwnerGuid,
                new Vector3((float)((claim.MinX - origin.X) / 10), (float)((claim.MinY - origin.Y) / 10), (float)((claim.MinZ - origin.Z) / 10)),
                new Vector3((float)((claim.MaxX - origin.X) / 10), (float)((claim.MaxY - origin.Y) / 10), (float)((claim.MaxZ - origin.Z) / 10)),
                claim.ZoneId);
            Unlocks.Add(claim.OwnerGuid, Unlocks.SiteContents, (uint)claim.Id);
            string owner = DatabaseManager.Instance.CharacterDatabase.GetCharacterById(claim.OwnerGuid).GetAwaiter().GetResult()?.Name ?? "";
            SiteUpkeep.MailExpired(claim.OwnerGuid, owner);
            log.Info($"Site {claim.Id} lapsed: {reverted.Count} cell(s) reverted, {props} prop(s) packed for 0x{claim.OwnerGuid:X}.");
        }

        /// <summary>
        /// At world entry: a player with packed site contents who has neither the Build Site Contents item nor the expiry
        /// letter carrying it (<see cref="SiteUpkeep.MailExpired"/>) gets the item directly.
        /// </summary>
        public static void Deliver(WorldSession session)
        {
            if (Unlocks.Values(session.PlayerGuid, Unlocks.SiteContents).Count == 0
                || session.Inventory.Items.Any(i => i.ItemDefinitionId == ContentsItem)
                || Mail.HasAttachment(session.PlayerGuid, ContentsItem))
                return;
            session.GiveItem(ContentsItem, 1);
            Chat.System(session, "Your Build Site lapsed while you were away. Use the Build Site Contents in your bag to get your props back.");
        }

        /// <summary>Using Build Site Contents: the packed props' items and one Build Site Flag per lapsed site.</summary>
        public static bool TryUse(WorldSession session, CharacterItemModel item)
        {
            if (item.ItemDefinitionId != ContentsItem)
                return false;
            var sites = Unlocks.Values(session.PlayerGuid, Unlocks.SiteContents);
            if (!session.TakeItem(ContentsItem, 1))
                return true;
            int props = Props.Unstow(session);
            foreach (uint _ in sites)
                session.GiveItem(Claims.BuildSiteFlagItem, 1);
            Unlocks.Clear(session, Unlocks.SiteContents);
            Chat.System(session, sites.Count == 0 && props == 0 ? "There was nothing left to retrieve."
                : $"Retrieved {props} prop(s){(sites.Count > 0 ? " and your Build Site Flag" : "")}.");
            log.Info($"{session.CharacterName} opened Build Site Contents: {props} prop(s), {sites.Count} flag(s).");
            return true;
        }
    }
}
