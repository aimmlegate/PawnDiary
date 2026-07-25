// Pure diary-page text search and display highlighting.
//
// The RimWorld UI supplies only the current page's visible title and body. Linked POV previews are
// deliberately not part of this contract, so a connected pawn's prose can never make a page match.
// Keeping the string decisions here lets the standalone test harness cover matching, minimum-query
// policy, and rich-text-safe highlighting without loading Verse or Unity.
using System;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Stateless, case-insensitive search rules for one diary page.
    /// </summary>
    internal static class DiaryEntrySearch
    {
        /// <summary>
        /// Returns the trimmed query used by both matching and highlighting.
        /// </summary>
        public static string NormalizeQuery(string query)
        {
            return (query ?? string.Empty).Trim();
        }

        /// <summary>
        /// True once a query reaches the configured minimum character count.
        /// </summary>
        public static bool IsActive(string query, int minimumCharacters)
        {
            return NormalizeQuery(query).Length >= SafeMinimum(minimumCharacters);
        }

        /// <summary>
        /// Keeps a page when search is inactive, or when its title or own body contains the query.
        /// Callers intentionally do not pass linked-POV text.
        /// </summary>
        public static bool Matches(
            string title,
            string body,
            string query,
            int minimumCharacters)
        {
            string term = NormalizeQuery(query);
            if (term.Length < SafeMinimum(minimumCharacters))
            {
                return true;
            }

            return Contains(title, term) || Contains(body, term);
        }

        /// <summary>
        /// Highlights every case-insensitive occurrence in plain text while escaping raw rich-text
        /// brackets first. Color-only markup does not change wrapping or measured card height.
        /// </summary>
        public static string HighlightPlainText(
            string text,
            string query,
            string colorHex,
            int minimumCharacters)
        {
            string escaped = EscapeRawRichText(text);
            return HighlightRichText(escaped, query, colorHex, minimumCharacters);
        }

        /// <summary>
        /// Highlights visible text while preserving Unity rich-text tags already present in the input.
        /// Matching never inspects tag names or attributes.
        /// </summary>
        public static string HighlightRichText(
            string richText,
            string query,
            string colorHex,
            int minimumCharacters)
        {
            string text = richText ?? string.Empty;
            string term = NormalizeQuery(query);
            string color = CleanColorHex(colorHex);
            if (text.Length == 0
                || term.Length < SafeMinimum(minimumCharacters)
                || color.Length == 0)
            {
                return text;
            }

            StringBuilder result = new StringBuilder(text.Length + 32);
            for (int i = 0; i < text.Length;)
            {
                if (TryCopyRichTextTag(text, ref i, result))
                {
                    continue;
                }

                // A malformed/unclosed '<' is visible text, not a tag. Consume it explicitly so a
                // damaged rich-text string cannot leave this loop parked on the same character.
                if (text[i] == '<')
                {
                    result.Append(text[i]);
                    i++;
                    continue;
                }

                int segmentStart = i;
                while (i < text.Length && text[i] != '<')
                {
                    i++;
                }

                HighlightVisibleSegment(text, segmentStart, i, term, color, result);
            }

            return result.ToString();
        }

        private static bool Contains(string text, string term)
        {
            return (text ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int SafeMinimum(int minimumCharacters)
        {
            return minimumCharacters < 1 ? 1 : minimumCharacters;
        }

        private static string EscapeRawRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string safeLessThan = ((char)0x2039).ToString();
            string safeGreaterThan = ((char)0x203A).ToString();
            return text.Replace("<", safeLessThan).Replace(">", safeGreaterThan);
        }

        private static bool TryCopyRichTextTag(string text, ref int index, StringBuilder result)
        {
            if (text[index] != '<')
            {
                return false;
            }

            int close = text.IndexOf('>', index + 1);
            if (close < 0)
            {
                return false;
            }

            result.Append(text, index, close - index + 1);
            index = close + 1;
            return true;
        }

        private static void HighlightVisibleSegment(
            string text,
            int start,
            int end,
            string term,
            string colorHex,
            StringBuilder result)
        {
            int cursor = start;
            while (cursor < end)
            {
                int match = text.IndexOf(term, cursor, end - cursor, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                {
                    result.Append(text, cursor, end - cursor);
                    return;
                }

                result.Append(text, cursor, match - cursor);
                result.Append("<color=#");
                result.Append(colorHex);
                result.Append('>');
                result.Append(text, match, term.Length);
                result.Append("</color>");
                cursor = match + term.Length;
            }
        }

        private static string CleanColorHex(string colorHex)
        {
            string value = (colorHex ?? string.Empty).Trim().TrimStart('#');
            if (value.Length != 6)
            {
                return string.Empty;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return string.Empty;
                }
            }

            return value.ToUpperInvariant();
        }
    }
}
