// Pure resolution of the "fact" a failed/stale diary page falls back to when it has no generated
// prose. The hot Diary card derives this fact from the saved POV prompt; archive compaction must
// derive the IDENTICAL fact at archive time (while that prompt is still available) and bake it in,
// because the compact archive deliberately drops the raw prompt. Keeping this pure (no Verse/Unity)
// lets both the UI and the archive builder share one code path, and lets the small test projects pin
// the field priority order. New to C#? See AGENTS.md.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Shared, pure picker for the human-readable fact behind an archived fallback page. The prompt
    /// schema labels are intentionally English (structured prompt schema — see AGENTS.md localization
    /// carve-outs), so matching them here is safe in any language.
    /// </summary>
    internal static class DiaryArchiveFallback
    {
        // Prompt fact fields, in display priority order. The first populated one wins, mirroring the
        // order the prompt planner emits them and the order the Diary card has always shown.
        private static readonly string[] FactLabels =
        {
            "what you saw",
            "what happened",
            "death facts",
            "arrival facts",
            "entry"
        };

        /// <summary>
        /// Resolves the raw (untrimmed) fallback fact for one page from its saved prompt and raw event
        /// text: the first populated prompt fact field, else the raw event text, else the first
        /// non-empty prompt line. Callers apply their own whitespace collapse / length trimming so the
        /// hot card and an archived row produce the same final string.
        /// </summary>
        public static string ResolveFact(string prompt, string rawText)
        {
            for (int i = 0; i < FactLabels.Length; i++)
            {
                string value = PromptFieldValue(prompt, FactLabels[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                return rawText;
            }

            return FirstPromptLine(prompt);
        }

        /// <summary>Reads the value after "<paramref name="label"/>:" from a newline-delimited prompt.</summary>
        public static string PromptFieldValue(string prompt, string label)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            string normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            string prefix = label + ":";
            int start = 0;
            while (start < normalized.Length)
            {
                int end = normalized.IndexOf('\n', start);
                if (end < 0)
                {
                    end = normalized.Length;
                }

                string line = normalized.Substring(start, end - start).Trim();
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(prefix.Length).Trim();
                }

                start = end + 1;
            }

            return string.Empty;
        }

        /// <summary>Returns the first non-blank line of a prompt, used as a last-resort fact.</summary>
        public static string FirstPromptLine(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            string normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            int start = 0;
            while (start < normalized.Length)
            {
                int end = normalized.IndexOf('\n', start);
                if (end < 0)
                {
                    end = normalized.Length;
                }

                string line = normalized.Substring(start, end - start).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                start = end + 1;
            }

            return string.Empty;
        }
    }
}
