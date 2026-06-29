// Pure archive-compaction decisions for old diary pages. Runtime code owns the actual
// DiaryEvent/DiaryEntryView objects; this helper owns the small yes/no policy so tests can cover it
// without loading RimWorld assemblies.
namespace PawnDiary
{
    /// <summary>
    /// Result of deciding whether one old diary POV can move from the hot event store into the compact
    /// display archive.
    /// </summary>
    internal struct DiaryArchiveEligibilityDecision
    {
        public readonly bool CanArchive;
        public readonly bool ForceFallback;

        public DiaryArchiveEligibilityDecision(bool canArchive, bool forceFallback)
        {
            CanArchive = canArchive;
            ForceFallback = forceFallback;
        }
    }

    /// <summary>
    /// Pure policy for archive compaction. Keeping this free of Verse/Pawn/GUI types lets the small test
    /// projects verify the safety rules that protect pending titles, prompt-only dev rows, and cold
    /// undisplayable overflow rows.
    /// </summary>
    internal static class DiaryArchiveEligibility
    {
        public static DiaryArchiveEligibilityDecision Evaluate(
            bool titlePending,
            string generatedText,
            bool archivedGenerationStale,
            string status,
            string fallbackText)
        {
            // A separate title request may still be queued/running. Once a row is compacted, raw title
            // state is gone, so keep it hot until that follow-up finishes or clears.
            if (titlePending)
            {
                return new DiaryArchiveEligibilityDecision(false, false);
            }

            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                return new DiaryArchiveEligibilityDecision(true, false);
            }

            if (archivedGenerationStale)
            {
                return new DiaryArchiveEligibilityDecision(true, true);
            }

            if (DiaryGenerationStatus.StatusEquals(status, DiaryGenerationStatus.Failed)
                && !string.IsNullOrWhiteSpace(fallbackText))
            {
                return new DiaryArchiveEligibilityDecision(true, true);
            }

            return new DiaryArchiveEligibilityDecision(false, false);
        }

        public static bool ShouldDropColdUndisplayableRef(
            bool archivedForScans,
            bool titlePending,
            string status,
            string generatedText,
            bool archivedGenerationStale)
        {
            // Inside the active scan window, pending work may still retry, title-backfill, or be inspected
            // by dev tools. Only cold rows outside that window are eligible for silent hot-ref removal.
            if (!archivedForScans)
            {
                return false;
            }

            if (titlePending || DiaryGenerationStatus.StatusEquals(status, DiaryGenerationStatus.PromptOnly))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(generatedText) && !archivedGenerationStale;
        }
    }
}
