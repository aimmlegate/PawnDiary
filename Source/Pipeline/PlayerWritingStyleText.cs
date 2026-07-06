// Pure cleanup for player-authored per-pawn writing-style prompts. Unlike the external integration
// override, this text is written by the player from the pawn's Diary tab, so it deliberately keeps
// line breaks so the editor stays readable. The same sanitizer runs before storage, before prompt
// use, and after save load.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// Sanitizes the player-authored custom writing-style prompt saved on a pawn's diary record.
    /// Internal implementation detail; <c>DiaryPipelineTests</c> reaches in via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal static class PlayerWritingStyleText
    {
        // Defensive cap for player-authored prompt text. This is a schema safety limit rather than
        // tuning policy: a custom style rule should stay compact enough to fit in the system prompt.
        public const int MaxRuleChars = 2000;

        /// <summary>
        /// Cleans a free-form player-authored prompt rule, preserving line breaks and capping length
        /// without splitting a UTF-16 surrogate pair.
        /// </summary>
        public static string CleanRule(string rule)
        {
            string cleaned = PromptTextSanitizer.Multiline(rule);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Length > MaxRuleChars)
            {
                return TextTruncation.SafePrefix(cleaned, MaxRuleChars).TrimEnd();
            }

            return cleaned;
        }
    }
}
