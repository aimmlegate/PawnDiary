// Pure cleanup for psychotype rule text. Two shapes, mirroring the writing-style pair:
//   - CleanRule: player-authored per-pawn custom psychotype, keeps line breaks (editor stays readable),
//     like PlayerWritingStyleText.
//   - CleanExternalRule / CleanSourceId: one-line sanitizers for integration-API psychotype overrides,
//     like ExternalWritingStyleOverrideText.
// The same sanitizers run before storage, before prompt use, and after save load.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// Sanitizes psychotype rule text. Internal implementation detail; <c>DiaryPipelineTests</c> reaches
    /// in via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal static class PsychotypeText
    {
        // Player-authored custom rules keep line breaks and can be a little longer than an external one-line
        // override, matching the writing-style caps so both layers feel the same in the editor.
        public const int MaxCustomRuleChars = 2000;
        // External override rules and source ids are one-line and defensively capped (untrusted input).
        public const int MaxExternalRuleChars = 1200;
        public const int MaxSourceIdChars = 128;

        /// <summary>
        /// Cleans a free-form player-authored psychotype rule, preserving line breaks and capping length
        /// without splitting a UTF-16 surrogate pair.
        /// </summary>
        public static string CleanRule(string rule)
        {
            string cleaned = PromptTextSanitizer.Multiline(rule);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Length > MaxCustomRuleChars)
            {
                return TextTruncation.SafePrefix(cleaned, MaxCustomRuleChars).TrimEnd();
            }

            return cleaned;
        }

        /// <summary>Cleans an external integration psychotype override into one capped line.</summary>
        public static string CleanExternalRule(string rule)
        {
            return CleanOneLine(rule, MaxExternalRuleChars);
        }

        /// <summary>Cleans the adapter source id saved as the override owner.</summary>
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
