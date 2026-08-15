// Pure mapping from a diary page's semantic color cue to its rarer atmosphere decoration.
// Hot DiaryEvent views and compact ArchivedDiaryEntry rows both call this helper so changing a
// player category cannot leave the two storage shapes rendering differently. This file deliberately
// depends only on System, which keeps the decision testable without RimWorld/Verse assemblies.
using System;

namespace PawnDiary
{
    /// <summary>Maps stable saved color-cue tokens to stable display-atmosphere tokens.</summary>
    internal static class DiaryAtmosphereCuePolicy
    {
        // These are stable schema/save tokens, not player-facing or tunable prose. DiaryEvent and
        // DiaryEntryView retain their historical public constant names as aliases for compatibility.
        internal const string MentalBreakColorCue = "mentalBreak";
        internal const string StrangeChatColorCue = "strangeChat";
        internal const string ExtremeDarkColorCue = "extremeDark";
        internal const string BodyPartAnomalousColorCue = "bodyPartAnomalous";
        internal const string FracturedAtmosphereCue = "fractured";
        internal const string UnsettledAtmosphereCue = "unsettled";

        /// <summary>
        /// Returns the atmosphere implied by one color cue. Empty means ordinary prose layout.
        /// Matching is case-insensitive because old/custom Def values may differ only by casing.
        /// </summary>
        internal static string ForColorCue(string colorCue)
        {
            if (string.Equals(colorCue, StrangeChatColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(colorCue, ExtremeDarkColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(colorCue, BodyPartAnomalousColorCue, StringComparison.OrdinalIgnoreCase))
            {
                return UnsettledAtmosphereCue;
            }

            if (string.Equals(colorCue, MentalBreakColorCue, StringComparison.OrdinalIgnoreCase))
            {
                return FracturedAtmosphereCue;
            }

            return string.Empty;
        }
    }
}
