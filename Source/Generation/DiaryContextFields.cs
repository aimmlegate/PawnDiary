// Small parser for the semicolon-delimited context strings stored on DiaryEvent.gameContext.
// Many runtime systems add fields like "death_victim_id=Pawn_12; death_description=true".
// Keeping exact key/value lookup in one place avoids brittle substring checks such as Pawn_1
// matching Pawn_12. The format stays intentionally simple because these strings are saved data.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Reads stable key/value fields from a saved diary context string.
    /// </summary>
    public static class DiaryContextFields
    {
        /// <summary>
        /// Returns the trimmed value for an exact context key, or an empty string when absent.
        /// Keys are matched case-insensitively; values are returned as saved.
        /// </summary>
        public static string Value(string context, string key)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string expectedKey = key.Trim();
            string[] parts = context.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int equalsIndex = part.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string partKey = part.Substring(0, equalsIndex).Trim();
                if (string.Equals(partKey, expectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(equalsIndex + 1).Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns true when the context contains a non-empty value for the exact key.
        /// </summary>
        public static bool HasField(string context, string key)
        {
            return !string.IsNullOrWhiteSpace(Value(context, key));
        }

        /// <summary>
        /// Returns true when the exact context key has the exact expected value.
        /// </summary>
        public static bool FieldEquals(string context, string key, string expectedValue)
        {
            return string.Equals(Value(context, key), expectedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
    }
}
