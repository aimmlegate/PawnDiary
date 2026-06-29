// Pure selection rule for which over-cap diary refs retention drops. The impure caller
// (DiaryGameComponent.ArchiveAndRemoveOverflowRefs) decides per ref whether it CAN be removed
// (archiveable, out of bounds, or cold-and-blank) and stages any archive row; this helper owns the
// small "how many, in what order" math so it can be pinned by the test projects without loading
// RimWorld. New to C#? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Pure planner for per-pawn hot-ref overflow trimming. Refs are passed oldest-first (saved order),
    /// so the oldest removable refs are chosen and any ref that must stay hot (title-pending, prompt-only,
    /// still in the scan window) is skipped without consuming the overflow budget.
    /// </summary>
    internal static class DiaryArchiveCompactionPlanner
    {
        /// <summary>
        /// Returns the ascending indexes of the first <paramref name="overflow"/> removable refs.
        /// Empty when nothing should be dropped (non-positive overflow, no removable refs, or a null
        /// list). The result is capped at the overflow budget so a pawn never drops more than it is over.
        /// </summary>
        public static List<int> SelectOverflowRemovals(IReadOnlyList<bool> removable, int overflow)
        {
            List<int> selected = new List<int>();
            if (removable == null || overflow <= 0)
            {
                return selected;
            }

            for (int i = 0; i < removable.Count && selected.Count < overflow; i++)
            {
                if (removable[i])
                {
                    selected.Add(i);
                }
            }

            return selected;
        }
    }
}
