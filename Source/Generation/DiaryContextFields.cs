// Small parser for the semicolon-delimited context strings stored on DiaryEvent.gameContext.
// Many runtime systems add fields like "death_victim_id=Pawn_12; death_description=true".
// Keeping exact key/value lookup in one place avoids brittle substring checks such as Pawn_1
// matching Pawn_12. The format stays intentionally simple because these strings are saved data.
//
// Hot path: the Diary tab's sliced history indexer and the per-entry view builder call these
// helpers several times PER event (arrival/death bounds checks, status reads, and domain
// recovery, which probes up to ~13 markers). The previous implementation did context.Split(';')
// on every call, allocating a string[] plus a substring per field each time — so indexing a few
// thousand entries allocated millions of strings and blew the per-frame time budget after only a
// couple of entries, making large histories take many seconds to load. The implementation below
// scans the context in place and allocates ONLY when a value is actually returned, so the common
// "key absent" path (the vast majority of the classifier's probes) is allocation-free.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Reads stable key/value fields from a saved diary context string.
    /// </summary>
    internal static class DiaryContextFields
    {
        /// <summary>
        /// Returns the trimmed value for an exact context key, or an empty string when absent.
        /// Keys are matched case-insensitively; values are returned as saved (trimmed).
        /// </summary>
        public static string Value(string context, string key)
        {
            int valueStart;
            int valueEnd;
            if (!TryFindValueRange(context, key, out valueStart, out valueEnd))
            {
                return string.Empty;
            }

            return context.Substring(valueStart, valueEnd - valueStart);
        }

        /// <summary>
        /// Returns true when the context contains a non-empty value for the exact key.
        /// </summary>
        public static bool HasField(string context, string key)
        {
            int valueStart;
            int valueEnd;
            return TryFindValueRange(context, key, out valueStart, out valueEnd) && valueEnd > valueStart;
        }

        /// <summary>
        /// Returns true when the exact context key has the exact expected value. The expected value
        /// is compared as-is (not trimmed) against the key's saved trimmed value, case-insensitively.
        /// </summary>
        public static bool FieldEquals(string context, string key, string expectedValue)
        {
            int valueStart;
            int valueEnd;
            if (!TryFindValueRange(context, key, out valueStart, out valueEnd))
            {
                return false;
            }

            string expected = expectedValue ?? string.Empty;
            int valueLength = valueEnd - valueStart;
            return valueLength == expected.Length
                && string.Compare(context, valueStart, expected, 0, valueLength, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Convenience for boolean context fields saved as "key=true".
        /// </summary>
        public static bool IsTrue(string context, string key)
        {
            return FieldEquals(context, key, "true");
        }

        /// <summary>
        /// Backward-compatible marker lookup for existing callers. Markers in "key=" or
        /// "key=value" form use exact field parsing; other markers fall back to a substring scan.
        /// </summary>
        public static bool HasMarker(string context, string marker)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(marker))
            {
                return false;
            }

            string trimmed = marker.Trim();
            int equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex == trimmed.Length - 1)
            {
                return HasField(context, trimmed.Substring(0, equalsIndex));
            }

            if (equalsIndex > 0)
            {
                return FieldEquals(
                    context,
                    trimmed.Substring(0, equalsIndex),
                    trimmed.Substring(equalsIndex + 1));
            }

            return context.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Locates the trimmed value character range for an exact key without allocating. Returns
        /// false (and zero range) when the context or key is blank, or the key is absent. The range
        /// is trimmed the same way the legacy split-and-Trim implementation trimmed, so observable
        /// results are identical; only the per-call allocations are eliminated.
        /// </summary>
        private static bool TryFindValueRange(string context, string key, out int valueStart, out int valueEnd)
        {
            valueStart = 0;
            valueEnd = 0;

            if (string.IsNullOrEmpty(context) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            // Trim the lookup key by range so a key with no surrounding whitespace (the common case:
            // "tale", "death_victim_id") does not allocate a trimmed copy.
            int keyStart = 0;
            int keyEnd = key.Length;
            while (keyStart < keyEnd && char.IsWhiteSpace(key[keyStart]))
            {
                keyStart++;
            }

            while (keyEnd > keyStart && char.IsWhiteSpace(key[keyEnd - 1]))
            {
                keyEnd--;
            }

            if (keyStart >= keyEnd)
            {
                return false;
            }

            int keyLength = keyEnd - keyStart;
            int contextLength = context.Length;
            int scan = 0;

            while (scan < contextLength)
            {
                // Skip semicolons and any surrounding whitespace between segments. The legacy
                // Split(';', RemoveEmptyEntries) followed by Trim() treated such runs as skipped
                // empty segments; consuming them here is equivalent and avoids per-segment work.
                while (scan < contextLength && (context[scan] == ';' || char.IsWhiteSpace(context[scan])))
                {
                    scan++;
                }

                int segmentStart = scan;
                while (scan < contextLength && context[scan] != ';')
                {
                    scan++;
                }

                int segmentEnd = scan; // exclusive; context[segmentEnd] is ';' or past the end

                // Trim the segment in place.
                int trimmedStart = segmentStart;
                int trimmedEnd = segmentEnd;
                while (trimmedStart < trimmedEnd && char.IsWhiteSpace(context[trimmedStart]))
                {
                    trimmedStart++;
                }

                while (trimmedEnd > trimmedStart && char.IsWhiteSpace(context[trimmedEnd - 1]))
                {
                    trimmedEnd--;
                }

                if (trimmedStart >= trimmedEnd)
                {
                    continue; // empty segment
                }

                // Find the first '=' inside the trimmed segment. The legacy code used
                // IndexOf('=') on the trimmed part and skipped segments with no key (equalsIndex <= 0).
                int equalsAt = -1;
                for (int i = trimmedStart; i < trimmedEnd; i++)
                {
                    if (context[i] == '=')
                    {
                        equalsAt = i;
                        break;
                    }
                }

                if (equalsAt < 0 || equalsAt == trimmedStart)
                {
                    continue; // no '=' or a zero-length key
                }

                // Trim the key range [trimmedStart, equalsAt), then compare to the lookup key.
                int keyRangeStart = trimmedStart;
                int keyRangeEnd = equalsAt;
                while (keyRangeStart < keyRangeEnd && char.IsWhiteSpace(context[keyRangeStart]))
                {
                    keyRangeStart++;
                }

                while (keyRangeEnd > keyRangeStart && char.IsWhiteSpace(context[keyRangeEnd - 1]))
                {
                    keyRangeEnd--;
                }

                if (keyRangeEnd - keyRangeStart == keyLength
                    && string.Compare(context, keyRangeStart, key, keyStart, keyLength, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // Match: trim the value range [equalsAt + 1, trimmedEnd) and return it.
                    valueStart = equalsAt + 1;
                    valueEnd = trimmedEnd;
                    while (valueStart < valueEnd && char.IsWhiteSpace(context[valueStart]))
                    {
                        valueStart++;
                    }

                    while (valueEnd > valueStart && char.IsWhiteSpace(context[valueEnd - 1]))
                    {
                        valueEnd--;
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
