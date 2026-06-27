// Active event retention for Pawn Diary. This is the temporary hard cap that keeps the live
// DiaryEvent store small until old-event compaction exists. The repository owns the master list and
// lookup index; this component owns the saved per-pawn diary references, so the trim-and-scrub pass
// lives here where both sides can be kept consistent.
using System;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Public settings hook: applies the configured active-event cap to the currently loaded game.
        /// Safe to call from the mod settings window; errors are logged once instead of escaping into
        /// RimWorld's settings UI.
        /// </summary>
        public void ApplyActiveEventLimitFromSettings()
        {
            try
            {
                ApplyActiveEventLimit();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Active diary event limit failed: " + e,
                    "DiaryGameComponent.ApplyActiveEventLimitFromSettings".GetHashCode());
            }
        }

        /// <summary>
        /// Drops oldest events beyond the configured active cap, then removes dangling event ids from
        /// pawn diary records so UI and background scans only walk retained events.
        /// </summary>
        private void ApplyActiveEventLimit()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            int limit = settings == null
                ? PawnDiarySettings.DefaultMaxActiveDiaryEvents
                : PawnDiarySettings.ClampActiveDiaryEventLimit(settings.maxActiveDiaryEvents);
            if (HasDevMockStressHistory(limit))
            {
                return;
            }

            int removed = events.TrimToMostRecent(limit);
            if (removed <= 0)
            {
                return;
            }

            PruneDiaryEventRefs();
            orphanCandidatesLastScan.Clear();
            DiaryStateVersion.Bump();
        }
    }
}
