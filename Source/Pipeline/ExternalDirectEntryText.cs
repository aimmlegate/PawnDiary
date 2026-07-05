// Pure cleanup for external direct-text injection. The API accepts prose that another mod already
// authored, so this helper preserves paragraph breaks while still trimming unsafe control characters
// and applying XML-backed defensive length caps before the text enters the save.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Sanitizes direct-entry prose and titles supplied through the public integration API.
    /// </summary>
    public static class ExternalDirectEntryText
    {
        /// <summary>
        /// Cleans final diary prose while preserving paragraph breaks. Returns empty for blank input.
        /// </summary>
        public static string CleanProse(string text, int maxChars)
        {
            string normalized = NormalizeMultiline(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = normalized.Trim();
            if (maxChars > 0 && normalized.Length > maxChars)
            {
                return TextTruncation.SafePrefix(normalized, maxChars).TrimEnd();
            }

            return normalized;
        }

        /// <summary>
        /// Cleans a caller-supplied title into one display-safe line.
        /// </summary>
        public static string CleanTitle(string title, int maxChars)
        {
            string cleaned = PromptTextSanitizer.OneLine(title);
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

        /// <summary>
        /// Cleans a short factual fallback summary. Unlike prose, this is always one line.
        /// </summary>
        public static string CleanSummary(string summary, int maxChars)
        {
            return CleanTitle(summary, maxChars);
        }

        private static string NormalizeMultiline(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool previousBlank = false;
            bool lineHasVisible = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    AppendNewline(builder, ref previousBlank, ref lineHasVisible);
                    continue;
                }

                if (c == '\n')
                {
                    AppendNewline(builder, ref previousBlank, ref lineHasVisible);
                    continue;
                }

                if (c == '\t')
                {
                    if (lineHasVisible)
                    {
                        builder.Append(' ');
                    }

                    continue;
                }

                if (char.IsControl(c))
                {
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (lineHasVisible)
                    {
                        builder.Append(' ');
                    }

                    continue;
                }

                builder.Append(c);
                lineHasVisible = true;
                previousBlank = false;
            }

            return builder.ToString();
        }

        private static void AppendNewline(StringBuilder builder, ref bool previousBlank, ref bool lineHasVisible)
        {
            if (!lineHasVisible)
            {
                if (!previousBlank && builder.Length > 0)
                {
                    builder.Append('\n');
                    previousBlank = true;
                }

                return;
            }

            TrimTrailingSpaces(builder);
            builder.Append('\n');
            lineHasVisible = false;
        }

        private static void TrimTrailingSpaces(StringBuilder builder)
        {
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ')
            {
                builder.Length--;
            }
        }
    }
}
