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
    internal static class PromptTextSanitizer
    {
        public const int DefaultMaxLocalizedSentences = 2;

        private static readonly Regex RichTextTagRegex = new Regex("<.*?>");
        // Player-authored free-form prompt text may legitimately contain angle brackets, e.g.
        // "write <short> sentences" or "keep it < 3 lines". The one-line path strips any "<...>" run
        // because it cleans controlled localized guidance, but the multiline player path must strip
        // ONLY known Unity/RimWorld rich-text tags so it never eats real instructions.
        private static readonly Regex KnownRichTextTagRegex = new Regex(
            "</?(?:b|i|size|color|material|quad)\\b[^>]*>", RegexOptions.IgnoreCase);
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
        /// Returns prompt-safe multiline text: rich-text tags are removed and control characters
        /// (except line breaks) become spaces, runs of blank lines collapse, trailing whitespace on
        /// each line is trimmed, and surrounding whitespace is trimmed. Unlike <see cref="OneLine"/>
        /// it deliberately keeps newlines so player-authored prompt rules stay readable in the editor.
        /// </summary>
        public static string Multiline(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string withoutTags = KnownRichTextTagRegex.Replace(value, string.Empty);
            StringBuilder builder = new StringBuilder(withoutTags.Length);
            for (int i = 0; i < withoutTags.Length; i++)
            {
                char ch = withoutTags[i];
                // Keep CR/LF line breaks; convert every other control char to a space so we do not
                // inject stray vertical tabs, form feeds, or NULs into the prompt.
                if (ch == '\n' || ch == '\r')
                {
                    builder.Append(ch);
                }
                else if (char.IsControl(ch))
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            // Normalize line endings to LF without merging blank lines across paragraph breaks:
            // CRLF -> LF, then any lone CR -> LF. Runs of blank lines are collapsed to a single blank
            // line below, after per-line trailing whitespace is trimmed.
            string normalized = builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = WhitespaceRegex.Replace(lines[i], " ").Trim();
            }

            string joined = string.Join("\n", lines);
            // Three or more consecutive LFs (i.e. one or more blank lines between text) collapse to
            // exactly two (a single blank line), keeping paragraph spacing readable without huge gaps.
            return MultilineBlankRunRegex.Replace(joined, "\n\n").Trim();
        }

        // Collapses three or more consecutive LFs (one or more blank lines between text) to exactly
        // two (a single blank line), keeping paragraph spacing readable without huge gaps.
        private static readonly Regex MultilineBlankRunRegex = new Regex("\n{3,}");

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
