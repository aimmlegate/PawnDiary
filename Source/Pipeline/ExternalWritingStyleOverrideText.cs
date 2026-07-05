// Pure cleanup for external writing-style overrides. Other mods can save prompt-facing rule text,
// so the same sanitizer must run before storage, before prompt use, and after save load.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// Sanitizes external writing-style override text supplied through the public integration API.
    /// Internal implementation detail; the public contract lives in <c>PawnDiary.Integration</c>,
    /// and <c>DiaryPipelineTests</c> reaches in via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal static class ExternalWritingStyleOverrideText
    {
        // Defensive caps for untrusted external prompt text. These are schema safety limits rather
        // than tuning policy: style rules should stay compact enough to fit in the system prompt.
        public const int MaxRuleChars = 1200;
        public const int MaxSourceIdChars = 128;

        /// <summary>
        /// Cleans a free-form prompt rule into one capped line.
        /// </summary>
        public static string CleanRule(string rule)
        {
            return CleanOneLine(rule, MaxRuleChars);
        }

        /// <summary>
        /// Cleans the adapter source id saved as the override owner.
        /// </summary>
        public static string CleanSourceId(string sourceId)
        {
            return CleanOneLine(sourceId, MaxSourceIdChars);
        }

        private static string CleanOneLine(string value, int maxChars)
        {
            string cleaned = PromptTextSanitizer.OneLine(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (maxChars > 0 && cleaned.Length > maxChars)
            {
                return TextTruncation.SafePrefix(cleaned, maxChars).TrimEnd();
            }

            return cleaned;
        }
    }
}
