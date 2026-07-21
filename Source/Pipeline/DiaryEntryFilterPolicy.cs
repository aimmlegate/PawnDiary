// Pure journal-filter rules for the Diary tab. The tab supplies plain facts it observed from the
// entry views ("is this page starred?", "which group label does it carry?", "which tags are active?");
// this helper owns the small decision so tests can pin it without loading Verse/RimWorld assemblies.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Decides which diary pages survive the filter panel's favorites/tag selections.
    /// </summary>
    internal static class DiaryEntryFilterPolicy
    {
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
    }
}
