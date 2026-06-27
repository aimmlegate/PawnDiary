// Active event retention for Pawn Diary. This is the per-pawn history cap: each pawn keeps only its
// newest configured number of diary pages, and a master-list event survives until no pawn references
// it (paired/neutral events are shared between two pawns). The repository owns the master list and
// lookup index; this component owns the saved per-pawn diary references, so the trim-and-sweep pass
// lives here where both sides can be kept consistent.
using System;
using System.Collections.Generic;
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
        /// Caps each pawn's diary to its newest configured number of pages, then drops master-list
        /// events that no pawn references anymore. Runs on every new event, on save/load, and when
        /// settings are saved.
        /// </summary>
        private void ApplyActiveEventLimit()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            int perPawnLimit = settings == null
                ? PawnDiarySettings.DefaultMaxActiveDiaryEvents
                : PawnDiarySettings.ClampActiveDiaryEventLimit(settings.maxActiveDiaryEvents);
            if (HasDevMockStressHistory(perPawnLimit))
            {
                return;
            }

            if (!TrimDiariesToPerPawnLimit(perPawnLimit))
            {
                return;
            }

            orphanCandidatesLastScan.Clear();
            DiaryStateVersion.Bump();
        }

        /// <summary>
        /// Caps every pawn's event-id list to its newest <paramref name="perPawnLimit"/> references
        /// (the oldest sit at the front, since refs are appended in tick order), then sweeps the master
        /// list down to the union of all surviving references. A page shared by two pawns survives
        /// until both drop it. The trim/keep decision is the pure <see cref="DiaryRetentionPlan"/>; this
        /// method just supplies the live lists and applies the plan. Returns true when anything changed,
        /// so callers can skip the version bump on the common no-op path.
        /// </summary>
        private bool TrimDiariesToPerPawnLimit(int perPawnLimit)
        {
            if (diaries == null || perPawnLimit < 0)
            {
                return false;
            }

            // Cheap zero-alloc precheck on the common path: only build a plan when some pawn is over the
            // cap. A colony under the cap pays just one Count comparison per pawn per new event.
            bool anyOver = false;
            for (int i = 0; i < diaries.Count; i++)
            {
                List<string> eventIds = diaries[i]?.eventIds;
                if (eventIds != null && eventIds.Count > perPawnLimit)
                {
                    anyOver = true;
                    break;
                }
            }

            if (!anyOver)
            {
                return false;
            }

            // Over the cap: hand the live per-pawn lists to the pure planner. The view aligns
            // index-for-index with `diaries` (null entries included) so DropCounts line up by position.
            IReadOnlyList<string>[] perPawnView = new IReadOnlyList<string>[diaries.Count];
            for (int i = 0; i < diaries.Count; i++)
            {
                perPawnView[i] = diaries[i]?.eventIds;
            }

            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(perPawnView, perPawnLimit);
            if (!plan.TrimmedAny)
            {
                return false;
            }

            // Apply the drops to each pawn's newest-at-the-end list (front = oldest).
            for (int i = 0; i < diaries.Count; i++)
            {
                List<string> eventIds = diaries[i]?.eventIds;
                int drop = i < plan.DropCounts.Length ? plan.DropCounts[i] : 0;
                if (eventIds != null && drop > 0)
                {
                    eventIds.RemoveRange(0, drop);
                }
            }

            // Sweep the master list down to events still referenced by some pawn. The dropped pages are
            // the oldest colony-wide, so they sit below the activeScanEventWindow hot set and were not
            // being scanned anyway; this just reclaims their memory.
            events.RetainOnly(plan.Referenced);
            return true;
        }
    }
}
