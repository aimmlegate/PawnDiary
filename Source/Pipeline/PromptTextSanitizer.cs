// Pure guardrails for text that is about to become part of an LLM prompt. Runtime adapters resolve
// XML/Keyed localization on the main thread, then use these helpers to keep prompt guidance compact
// and safe for the structured "label: value" format.
using System.Text;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Stateless prompt-text cleanup. It has no RimWorld/Verse dependency, so tests can cover the
    /// exact sentence-capping and line-guard behavior used by the runtime prompt adapters.
    /// </summary>
    public static class PromptTextSanitizer
    {
        public const int DefaultMaxLocalizedSentences = 2;

        private static readonly Regex RichTextTagRegex = new Regex("<.*?>");
        private static readonly Regex WhitespaceRegex = new Regex("\\s+");

        /// <summary>
        /// Returns one safe prompt line: rich-text tags are removed, control characters become
        /// spaces, whitespace is collapsed, and surrounding whitespace is trimmed.
        /// </summary>
        public static string OneLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string withoutTags = RichTextTagRegex.Replace(value, string.Empty);
            StringBuilder builder = new StringBuilder(withoutTags.Length);
            for (int i = 0; i < withoutTags.Length; i++)
            {
                char ch = withoutTags[i];
                builder.Append(char.IsControl(ch) ? ' ' : ch);
            }

            return WhitespaceRegex.Replace(builder.ToString(), " ").Trim();
        }

        /// <summary>
        /// Cleans localized prompt guidance to one line and keeps at most the first two sentences.
        /// </summary>
        public static string LocalizedPromptText(string value)
        {
            return FirstSentences(OneLine(value), DefaultMaxLocalizedSentences);
        }

        /// <summary>
        /// Returns the first <paramref name="maxSentences"/> sentence-like chunks from an already
        /// prompt-bound string. If fewer sentence terminators are present, the full line is returned.
        /// </summary>
        public static string FirstSentences(string value, int maxSentences)
        {
            string line = OneLine(value);
            if (string.IsNullOrWhiteSpace(line) || maxSentences <= 0)
            {
                return string.Empty;
            }

            int sentenceCount = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (!IsSentenceTerminator(line[i]) || !IsBoundary(line, i))
                {
                    continue;
                }

                sentenceCount++;
                if (sentenceCount >= maxSentences)
                {
                    return line.Substring(0, IncludeClosingPunctuation(line, i + 1)).Trim();
                }
            }

            return line;
        }

        private static bool IsSentenceTerminator(char ch)
        {
            return ch == '.' || ch == '!' || ch == '?'
                || ch == '\u3002' || ch == '\uFF01' || ch == '\uFF1F';
        }

        private static bool IsBoundary(string value, int terminatorIndex)
        {
            int next = terminatorIndex + 1;
            if (next >= value.Length)
            {
                return true;
            }

            char ch = value[next];
            return char.IsWhiteSpace(ch) || IsClosingPunctuation(ch);
        }

        private static int IncludeClosingPunctuation(string value, int index)
        {
            int end = index;
            while (end < value.Length && IsClosingPunctuation(value[end]))
            {
                end++;
            }

            return end;
        }

        private static bool IsClosingPunctuation(char ch)
        {
            return ch == '"' || ch == '\'' || ch == ')' || ch == ']' || ch == '}'
                || ch == '\u2019' || ch == '\u201D' || ch == '\u300D' || ch == '\u300F';
        }
    }
}
