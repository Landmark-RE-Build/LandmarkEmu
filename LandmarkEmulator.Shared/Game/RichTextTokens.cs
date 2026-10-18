namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// The 0.695 client's rich-text tokens: the chat window, the notification window and the speech bubble turn every
    /// line they show into HTML with <c>~[~</c> → <c>&lt;</c>, <c>~]~</c> → <c>&gt;</c>, <c>~a~</c> → <c>'</c>,
    /// <c>~q~</c> → <c>"</c> (re/as3_695/Chat/scripts/ChatDocumentClass.as detokenizeText; OPEN-LEADS L54). The only
    /// legitimate producer is the prop property editor (LargeTextInput.tokenizeText) for story text. Text a player types
    /// elsewhere (chat, guild message of the day / description, build site names) reaches other players' windows the same
    /// way, so a typed token would inject markup (colours, sizes, fake system lines, links) into their chat — verified
    /// 2026-10-17: a /say with the tokens reached the client's chat layer unchanged. Player text is therefore
    /// neutralised before it is stored or relayed: each token becomes the plain character it stands for, minus the
    /// markup ones (<c>[</c> and <c>]</c>), so nothing is lost but nothing becomes HTML.
    /// </summary>
    public static class RichTextTokens
    {
        /// <summary>Player-typed text with the client's markup tokens made harmless; null stays null.</summary>
        public static string Neutralize(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('~') < 0)
                return text;
            return text.Replace("~[~", "[").Replace("~]~", "]").Replace("~a~", "'").Replace("~q~", "\"");
        }
    }
}
