// Pure formatting and defensive output policy for persona synchronization in either direction.
namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>Builds short, single-line, Unicode-safe persona text without game dependencies.</summary>
    internal static class PersonaTransferText
    {
        /// <summary>Prompt target. Shorter valid replies are accepted; local code never pads prose.</summary>
        public const int TargetMinimumCharacters = 200;

        /// <summary>Hard local maximum, counted as user-perceived Unicode characters.</summary>
        public const int MaximumCharacters = 300;

        /// <summary>
        /// Builds transfer text from psychotype only. Writing-style prose is intentionally not an
        /// argument: style identity may select a prompt policy, but its instructions never cross.
        /// </summary>
        public static string FromPsychotype(string psychotype)
        {
            return Clean(psychotype);
        }

        /// <summary>Normalizes an LLM/direct result and enforces the hard 300-character contract.</summary>
        public static string Clean(string value)
        {
            string text = UnicodeText.CleanOneLine(value);
            if (text.Length >= 2
                && ((text[0] == '"' && text[text.Length - 1] == '"')
                    || (text[0] == '\'' && text[text.Length - 1] == '\'')))
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            if (UnicodeText.TextElementCount(text) <= MaximumCharacters)
            {
                return text;
            }

            string capped = UnicodeText.CapTextElements(text, MaximumCharacters);
            int lastSpace = capped.LastIndexOf(' ');
            // Prefer a complete final word when that does not discard more than a quarter of the budget.
            if (lastSpace >= MaximumCharacters * 3 / 4)
            {
                capped = capped.Substring(0, lastSpace).TrimEnd();
            }

            return capped;
        }
    }
}
