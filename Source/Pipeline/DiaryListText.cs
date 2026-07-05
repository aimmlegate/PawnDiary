// Small pure helpers for lists that are displayed as comma-separated prompt text.
// Keeping the list and formatting steps separate prevents localized labels that contain commas from
// being split into bogus DTO entries. New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Utilities for prompt-context lists that must stay structured until the final string format.
    /// </summary>
    internal static class DiaryListText
    {
        /// <summary>
        /// Adds a non-blank value to a list. The value is kept exactly as provided.
        /// </summary>
        public static void AddNonBlank(List<string> values, string value)
        {
            if (values != null && !string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        /// <summary>
        /// Formats already-structured entries as the prompt's comma-separated list.
        /// </summary>
        public static string JoinComma(IList<string> values)
        {
            List<string> kept = NonBlankList(values);
            return kept.Count == 0 ? string.Empty : string.Join(", ", kept.ToArray());
        }

        /// <summary>
        /// Returns an independent copy with null entries removed.
        /// </summary>
        public static List<string> CopyNonNull(IList<string> values)
        {
            List<string> copy = new List<string>(values?.Count ?? 0);
            if (values == null)
            {
                return copy;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (value != null)
                {
                    copy.Add(value);
                }
            }

            return copy;
        }

        private static List<string> NonBlankList(IList<string> values)
        {
            List<string> kept = new List<string>(values?.Count ?? 0);
            if (values == null)
            {
                return kept;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    kept.Add(value);
                }
            }

            return kept;
        }
    }
}
