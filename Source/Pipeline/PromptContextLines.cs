// Pure helpers for "key=value" context lines that external adapters or provider hooks contribute
// to an LLM prompt. Runtime code owns where the lines come from; this helper only enforces the
// shared prompt framing rules so tests can pin them without RimWorld assemblies.
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Cleans and joins short context lines for semicolon-separated game-context fields.
    /// </summary>
    internal static class PromptContextLines
    {
        /// <summary>
        /// Returns one prompt-safe context line: one physical line, no semicolon field separators,
        /// and no more than <paramref name="maxChars"/> UTF-16 characters.
        /// </summary>
        public static string CleanLine(string value, int maxChars)
        {
            if (maxChars <= 0)
            {
                return string.Empty;
            }

            string line = PromptTextSanitizer.OneLine(value).Replace(';', ',');
            if (line.Length <= maxChars)
            {
                return line;
            }

            return TextTruncation.SafePrefix(line, maxChars).TrimEnd();
        }

        /// <summary>
        /// Cleans, skips blanks, caps the number of kept lines, and joins them as game-context fields.
        /// </summary>
        public static string Join(IEnumerable<string> lines, int maxLines, int maxLineChars)
        {
            if (lines == null || maxLines <= 0 || maxLineChars <= 0)
            {
                return string.Empty;
            }

            StringBuilder joined = new StringBuilder();
            int kept = 0;
            foreach (string rawLine in lines)
            {
                if (kept >= maxLines)
                {
                    break;
                }

                string line = CleanLine(rawLine, maxLineChars);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (kept > 0)
                {
                    joined.Append("; ");
                }

                joined.Append(line);
                kept++;
            }

            return joined.ToString();
        }
    }
}
