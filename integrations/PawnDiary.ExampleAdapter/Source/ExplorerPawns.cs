// Eligible-pawn pool for the explorer's subject/partner pickers. This is the one impure lookup the
// window needs every frame: a cached, stable list of humanlike player colonists. Mirrors the
// pattern in the core mod's Dialog_PawnDiaryEventTestPanel.EligiblePawns, kept here so the adapter
// depends only on the public PawnDiaryApi surface plus vanilla RimWorld helpers.
//
// New to C#? See AGENTS.md.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Builds the list of pawns the explorer can target: free colonists on any player home map, plus
    /// any player colonists on the current visible map when no home map exists. Cached per-frame.
    /// </summary>
    internal static class ExplorerPawns
    {
        // Reused per call; rebuilt each invocation so newly spawned/gone pawns are picked up. The
        // window calls this once per DoWindowContents frame, never in a hot loop.
        private static readonly List<Pawn> buffer = new List<Pawn>(64);

        /// <summary>
        /// Returns the cached eligible pawn pool. The returned list is owned by this helper — callers
        /// must not retain or mutate it across frames. May be empty (no colonists). Never null.
        /// </summary>
        public static List<Pawn> EligiblePawns()
        {
            buffer.Clear();

            // Prefer free colonists across all home maps (covers multi-map colonies).
            Map anyHome = Find.AnyPlayerHomeMap;
            if (anyHome != null && anyHome.mapPawns?.FreeColonists != null)
            {
                AddAll(buffer, anyHome.mapPawns.FreeColonists);
            }

            if (buffer.Count == 0)
            {
                // Fall back to the current map so the explorer still works before a base is built.
                Map current = Find.CurrentMap;
                if (current != null && current.mapPawns?.FreeColonists != null)
                {
                    AddAll(buffer, current.mapPawns.FreeColonists);
                }
            }

            return buffer;
        }

        private static void AddAll(List<Pawn> target, List<Pawn> source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                Pawn pawn = source[i];
                if (pawn != null && !target.Contains(pawn))
                {
                    target.Add(pawn);
                }
            }
        }

        /// <summary>
        /// Safe label for log lines: never throws on a null pawn. Returns "(null)" so a missing
        /// subject is visible in the result panel instead of crashing the formatter.
        /// </summary>
        public static string LabelOrEmpty(Pawn pawn)
        {
            if (pawn == null)
            {
                return "(null pawn)";
            }

            try
            {
                return pawn.LabelShortCap;
            }
            catch
            {
                return "(unknown pawn)";
            }
        }
    }
}
