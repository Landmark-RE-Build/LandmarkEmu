using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Linq;

using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Designs as items: the Designs window's "export to item" (TemplateBase 0x1B ExportToItemRequest {u64 design}, Lua
    /// ExportToItem) makes a Player Design item (2433, "An item that contains a player made Design."). The item carries a
    /// copy of the design: a template row owned by the item's guid instead of a character. Using the item adds that copy
    /// to the user's designs (and uses the item up), so designs can be handed over in a trade. A trade gives the
    /// receiver a new item, so the copy moves to it (<see cref="Moved"/>); so does taking one out of a chest (a chest keeps
    /// the item's guid while it is stored; Chests.GiveOut, audit-wiki-gaps #3). Mail keeps only item kinds and counts, so
    /// Player Design items can't be mailed yet.
    /// </summary>
    public static class DesignItems
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint PlayerDesignItem = 2433;

        /// <summary>Client text 572728 "We ran into troubles trying to export your Design!" (its own export failure line).</summary>
        public const uint ExportFailedText = 572728;

        /// <summary>
        /// ExportToItemRequest (TemplateBase 0x1B). A design bought in Player Studio can't be exported: an exported item is
        /// tradeable, so one purchase could be handed around for free and its creator would lose the sales (wiki
        /// Trading: purchased templates are the exception among tradeable items; wiki Player Studio: purchased Designs
        /// can't be mailed to other players; audit-wiki-gaps #4). The Designs window shows "Send to
        /// Inventory" on every design (TemplateDocumentClass.as ITEM_ROLL_OVER: no marketplace check; Template.lua
        /// SendToInventory → Templates.ExportToItem), so the server refuses it, with the client's own export-failure text
        /// [I: which text live showed is not known; this request has no reply packet].
        /// </summary>
        public static void Export(WorldSession session, ulong designGuid)
        {
            TemplateModel design = Templates.FindDesign(session.PlayerGuid, designGuid);
            if (design == null)
                return;
            if (Templates.IsPlayerStudioCopy(session.PlayerGuid, designGuid))
            {
                SystemMessages.Show(session, ExportFailedText);
                log.Info($"{session.CharacterName} tried to export Player Studio design {designGuid} to an item: refused.");
                return;
            }
            CharacterItemModel item = session.Inventory.AddSingle(PlayerDesignItem);
            if (item == null)
            {
                Chat.System(session, "Your bag is full.");
                return;
            }
            var copy = new TemplateModel
            {
                OwnerGuid = item.Id,
                AuthorName = design.AuthorName,
                Name = design.Name,
                Tags = design.Tags,
                SizeX = design.SizeX,
                SizeY = design.SizeY,
                SizeZ = design.SizeZ,
                Materials = design.Materials,
                Blocks = design.Blocks,
                CreatedAt = design.CreatedAt
            };
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                context.Template.Add(copy);
                context.SaveChanges();
            }
            // Sent after the copy exists: the item record names the design it holds (ItemBlock).
            session.SendItem(item, replace: false);
            Chat.System(session, $"\"{design.Name}\" is now a Player Design item in your bag.");
            Achievements.DesignItemCreated(session);
            log.Info($"{session.CharacterName} exported design {design.Id} to item 0x{item.Id:X} (copy {copy.Id}).");
        }

        /// <summary>Using a Player Design item: its design joins the user's designs.</summary>
        public static bool TryUse(WorldSession session, CharacterItemModel item)
        {
            if (item.ItemDefinitionId != PlayerDesignItem)
                return false;
            TemplateModel design = Templates.OwnedBy(item.Id).FirstOrDefault();
            if (design == null)
            {
                Chat.System(session, "This Player Design is empty.");
                return true;
            }
            if (!Templates.HasRoom(session, design))
            {
                Chat.System(session, "Your design storage is full.");
                return true;
            }
            ulong itemGuid = item.Id;
            if (session.TakeFromStack(itemGuid, 1) == null)
                return true;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                TemplateModel row = context.Template.First(t => t.Id == design.Id);
                row.OwnerGuid = session.PlayerGuid;
                context.SaveChanges();
                design = row;
            }
            Templates.SendAdded(session, design);
            Templates.SendLearned(session, design.Id, design.Name);
            log.Info($"{session.CharacterName} unpacked Player Design 0x{itemGuid:X}: design {design.Id}.");
            return true;
        }

        /// <summary>
        /// The item record's design block (ClientItem +0x128) for a Player Design item: {design id, name, author}, as
        /// the live server sent it ("1 - EW Section" by "Zhang"). Null when the item holds no design.
        /// </summary>
        public static InventoryItem.DesignData ItemBlock(ulong itemGuid)
        {
            TemplateModel design = Templates.OwnedBy(itemGuid).FirstOrDefault();
            return design == null ? null : new InventoryItem.DesignData { DesignId = design.Id, DesignName = design.Name, AuthorName = design.AuthorName };
        }

        /// <summary>A Player Design item changed hands as a new item: its design goes with it.</summary>
        public static void Moved(ulong oldItem, ulong newItem)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            foreach (TemplateModel row in context.Template.Where(t => t.OwnerGuid == oldItem))
                row.OwnerGuid = newItem;
            context.SaveChanges();
        }
    }
}
