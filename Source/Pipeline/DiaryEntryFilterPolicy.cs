// Pure journal-filter and favorite-key rules for the Diary tab. The tab supplies plain facts it
// observed from the entry views ("is this page starred?", "which group label does it carry?", "which
// tags are active?"); saved records also pass their raw favorite keys here for bounded normalization.
// Keeping both decisions plain lets tests pin them without loading Verse/RimWorld assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Decides which diary pages survive the filter panel's favorites/tag selections.
    /// </summary>
    internal static class DiaryEntryFilterPolicy
    {
        /// <summary>
        /// Defensive per-diary favorite bound shared by save normalization, component writes, and the
        /// UI mirror. Normal play cannot approach it; it protects load from corrupt or hand-edited saves.
        /// </summary>
        public const int MaximumFavoriteEntryKeys = 4096;

        /// <summary>
        /// An entry passes when both independent dimensions accept it: the favorites dimension
        /// (a "favorites only" filter rejects unstarred pages) AND the tag dimension (one or more
        /// active tag chips reject pages whose group label matches none of them — union/OR semantics,
        /// so tapping several chips widens the shown set rather than narrowing it). Untagged pages
        /// match no chip, so they drop out whenever any tag filter is active.
        /// </summary>
        public static bool Passes(
            bool favoritesOnly,
            bool isFavorite,
            string groupLabel,
            IReadOnlyCollection<string> activeTags)
        {
            if (favoritesOnly && !isFavorite)
            {
                return false;
            }

            if (activeTags == null || activeTags.Count == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(groupLabel))
            {
                return false;
            }

            // Manual scan instead of LINQ Contains: IReadOnlyCollection has no Contains, and the tab
            // calls this per visible card, so no extension-method dependency or allocation is wanted.
            foreach (string tag in activeTags)
            {
                if (string.Equals(tag, groupLabel, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the first bounded set of non-empty favorite keys in saved order, removing exact
        /// duplicates in O(n) time. Ordinal comparison matches the stable eventId|povRole key contract.
        /// </summary>
        public static List<string> NormalizeFavoriteEntryKeys(IEnumerable<string> savedKeys)
        {
            List<string> normalized = new List<string>();
            if (savedKeys == null)
            {
                return normalized;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in savedKeys)
            {
                if (normalized.Count >= MaximumFavoriteEntryKeys)
                {
                    break;
                }

                if (!string.IsNullOrEmpty(key) && seen.Add(key))
                {
                    normalized.Add(key);
                }
            }

            return normalized;
        }
    }
}
