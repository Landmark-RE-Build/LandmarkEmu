using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One in-game mail message in a character's inbox (Notifications -> Open Inbox). Each recipient gets their own row.
    /// </summary>
    public class MailModel
    {
        public ulong Id { get; set; }
        /// <summary>The character whose inbox it is in.</summary>
        public ulong RecipientGuid { get; set; }
        public ulong SenderGuid { get; set; }
        /// <summary>The sender's name when it was sent (shown even if they are renamed or deleted later).</summary>
        public string SenderName { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        /// <summary>Attached items not yet taken: "definition:count" entries separated by ';' (empty = none).</summary>
        public string Attachments { get; set; } = "";
        /// <summary>
        /// The client's mail kind flags (MailData +0x1d0): 0 = player mail. System kinds make the client write the sender,
        /// subject and body from its own text: 0x10 Player Studio item accepted, 0x20 rejected (the subject is the
        /// product name, the text's "item" token).
        /// </summary>
        public uint Kind { get; set; }
    }
}
