using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Database.Social.Model
{
    /// <summary>
    /// social_mail: one message in one inbox (a mail to several players is one row per recipient), shared by every world
    /// (social-service.md §5.2). From lmemu_character.mail (<see cref="SocialImport"/>), keeping the ids.
    /// </summary>
    public class SocialMailModel
    {
        public ulong Id { get; set; }
        /// <summary>The character whose inbox it is in.</summary>
        public ulong RecipientGuid { get; set; }
        /// <summary>The sending character; 0 for mail from the game.</summary>
        public ulong SenderGuid { get; set; }
        /// <summary>The sender's name when it was sent (shown even if they are renamed or deleted later).</summary>
        public string SenderName { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        /// <summary>The client's mail kind flags (MailData +0x1d0): 0 = player mail; system kinds make the client write
        /// its own sender / subject / body text.</summary>
        public uint Kind { get; set; }
        /// <summary>The guild of a guild mail (kind 0x100, L64); 0 otherwise.</summary>
        public ulong GuildId { get; set; }
        /// <summary>
        /// The zone's compose token or a system mail's token (0 = none): a message sent again after a lost link is
        /// recognised by it and stored once.
        /// </summary>
        public ulong Token { get; set; }

        public List<SocialMailAttachmentModel> Attachments { get; set; } = new();
    }

    /// <summary>
    /// social_mail_attachment: one attachment slot of a mail. Taken slots stay (Taken = true) so the client's attachment
    /// ids — slot + 1 — keep pointing at the same slot.
    /// </summary>
    public class SocialMailAttachmentModel
    {
        public ulong MailId { get; set; }
        public uint Slot { get; set; }
        /// <summary>0 items, 1 coins to take, 2 coins to pay on delivery (Internal.Social.MailAttachmentType).</summary>
        public byte Type { get; set; }
        public uint ItemDefinition { get; set; }
        /// <summary>Items, or coins.</summary>
        public uint Count { get; set; }
        public bool Taken { get; set; }
        /// <summary>The attached item's wear (durability points lost), kept on the way (audit-wiki-gaps #1); 0 otherwise.</summary>
        public float Wear { get; set; }
    }
}
