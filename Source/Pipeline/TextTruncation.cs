// Surrogate-safe text truncation. Several places cap a string to a maximum number of UTF-16 chars
// (prompt context, evidence labels, card titles, persisted raw responses). A plain `Substring(0, n)`
// can slice between the two halves of a surrogate pair — an astral character such as an emoji or a
// rare CJK glyph — leaving a lone surrogate that renders as a broken glyph. This pure helper backs
// off one position when the cut would split such a pair, so the prefix always ends on a whole code
// point. No RimWorld/Verse dependency, so it is covered by the pure test harness.
namespace PawnDiary
{
    /// <summary>
    /// Length-capping helpers that never split a UTF-16 surrogate pair.
    /// </summary>
    internal static class TextTruncation
    {
        /// <summary>
        /// Returns the first <paramref name="maxChars"/> characters of <paramref name="value"/> without
        /// slicing through a surrogate pair. Returns the whole string when it already fits, and an empty
        /// string for null/empty input or a non-positive cap. Callers append their own ellipsis/suffix.
        /// </summary>
        public static string SafePrefix(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || maxChars <= 0)
            {
                return string.Empty;
            }

            if (value.Length <= maxChars)
            {
                return value;
            }

            int cut = maxChars;
            // If the last kept char is a high surrogate, its low surrogate sits at index `cut` and would
            // be orphaned by the slice — drop the high surrogate too so the prefix ends on a whole pair.
            if (char.IsHighSurrogate(value[cut - 1]))
            {
                cut--;
            }

            return value.Substring(0, cut);
        }

        /// <summary>
        /// Returns the last <paramref name="maxChars"/> characters of <paramref name="value"/> without
        /// slicing through a surrogate pair. Returns the whole string when it already fits, and an empty
        /// string for null/empty input or a non-positive cap. Callers add their own leading ellipsis.
        /// </summary>
        public static string SafeSuffix(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || maxChars <= 0)
            {
                return string.Empty;
            }

            if (value.Length <= maxChars)
            {
                return value;
            }

            int start = value.Length - maxChars;
            // If the first kept char is a low surrogate, its high surrogate sits just before the cut
            // and would be orphaned by the slice. Move forward one char so the suffix starts cleanly.
            if (char.IsLowSurrogate(value[start]))
            {
                start++;
            }

            return value.Substring(start);
        }
    }
}
