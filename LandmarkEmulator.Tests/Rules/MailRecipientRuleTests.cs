using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// PacketMailComposeRequest's two senders (Game/Mail.cs ReadCompose / ResolveRecipients, docs/request-variants.md):
    /// Mail.SendMessage (the mail window) names the recipients in the "To" list with ToGuid 0; Mail.SendMessageForClaimFeedback
    /// (a site's Feedback button, ClaimFeedback.as) sets ToGuid to the site owner and leaves "To" empty, which the client
    /// writes as one empty name block. Before, the server skipped ToGuid and answered the feedback mail "user not found".
    /// </summary>
    public class MailRecipientRuleTests
    {
        /// <summary>A compose body as FUN_14363b170 fills it: u64 ToGuid, u32 n × name block, u32 0, subject, body, u32 0.</summary>
        private static byte[] Compose(ulong toGuid, params string[] to)
        {
            var bytes = new List<byte>();
            var w = new GamePacketWriter(bytes);
            w.Write(toGuid);
            w.Write((uint)to.Length);
            foreach (string name in to)
            {
                w.Write(0u); w.Write(0u); w.Write(0u);
                w.Write(name);
                w.Write("");
            }
            w.Write(0u);
            w.Write("Feedback: Castle");
            w.Write("Lovely site!");
            w.Write(0u);
            return bytes.ToArray();
        }

        private static Mail.ComposeDraft Read(byte[] body)
        {
            Assert.Equal(Mail.Success, Mail.ReadCompose(new GamePacketReader(body), out Mail.ComposeDraft draft));
            return draft;
        }

        private static string NameOf(ulong guid) => guid == 0x77 ? "Owner" : null;

        [Fact]
        public void TheMailWindow_NamesRecipients_AndToGuidStaysUnused()
        {
            Mail.ComposeDraft draft = Read(Compose(0, "Alice", "Bob"));
            Assert.Equal(0ul, draft.ToGuid);
            Mail.ResolveRecipients(draft, NameOf);
            Assert.Equal(new List<string> { "Alice", "Bob" }, draft.Names);
            Assert.Equal("Lovely site!", draft.Body);
        }

        [Fact]
        public void TheFeedbackMail_GoesToTheCharacterToGuidNames()
        {
            Mail.ComposeDraft draft = Read(Compose(0x77, ""));
            Assert.Equal(0x77ul, draft.ToGuid);
            Assert.Empty(draft.Names);
            Mail.ResolveRecipients(draft, NameOf);
            Assert.Equal(new List<string> { "Owner" }, draft.Names);
            Assert.Equal("Feedback: Castle", draft.Subject);
        }

        [Fact]
        public void TypedNames_WinOverToGuid()
        {
            Mail.ComposeDraft draft = Read(Compose(0x77, "Alice"));
            Mail.ResolveRecipients(draft, NameOf);
            Assert.Equal(new List<string> { "Alice" }, draft.Names);
        }

        [Fact]
        public void AnUnknownToGuid_LeavesNoRecipient()
        {
            Mail.ComposeDraft draft = Read(Compose(0x99, ""));
            Mail.ResolveRecipients(draft, NameOf);
            Assert.Empty(draft.Names);
        }

        [Fact]
        public void BlankNameBlocks_AreNoRecipients_AndTheRestStaysAligned()
        {
            Mail.ComposeDraft draft = Read(Compose(0, " ", "Alice", ""));
            Assert.Equal(new List<string> { "Alice" }, draft.Names);
            Assert.Equal("Feedback: Castle", draft.Subject);
        }
    }
}
