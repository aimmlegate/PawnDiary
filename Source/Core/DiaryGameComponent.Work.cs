// Work diary events — the periodic sampler. RimWorld work is long-running and repetitive, so there
// is no clean one-shot hook; this scan picks WHO to sample every few in-game hours and submits a
// WorkSignal for each colonist. The per-pawn snapshot, mood classification, weighted roll, and emit
// live in Source/Ingestion/Sources/WorkSignal.cs. What stays here is the scan entry point and the
// persistent same/different-work cooldown lookup over saved diary history (so loading a save does not
// reset "recently wrote about this work" memory) — the signal reads it through Current.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Periodically samples eligible colonists' current work and submits a WorkSignal for each.
        /// The signal decides (via the catalog) whether the sample becomes a solo work entry.
        /// </summary>
        private void ScanPawnWorkForDiaryEvents()
        {
            if (PawnDiaryMod.Settings == null || !DiarySignalPolicies.Enabled(DiarySignalPolicies.Work))
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                DiaryEvents.Submit(new WorkSignal(colonists[i]));
            }
        }

        /// <summary>
        /// Returns whether the pawn already has a work diary entry within the window — same work type
        /// only, or different work type only, per <paramref name="sameWorkOnly"/>. Reads saved diary
        /// history so cooldowns survive save/load.
        /// internal: the WorkSignal capture reads work cooldowns through DiaryGameComponent.Current.
        /// </summary>
        internal bool HasRecentWorkEvent(Pawn pawn, string currentWorkTypeDefName, int windowTicks, bool sameWorkOnly)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(currentWorkTypeDefName) || windowTicks <= 0)
            {
                return false;
            }

            int minTick = Find.TickManager.TicksGame - windowTicks;
            string pawnId = pawn.GetUniqueLoadID();
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
            for (int i = allEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.tick < minTick)
                {
                    break;
                }

                if (diaryEvent.initiatorPawnId != pawnId || !IsWorkContext(diaryEvent.gameContext))
                {
                    continue;
                }

                string recordedWork = WorkTypeFromContext(diaryEvent.gameContext);
                bool sameWork = string.Equals(recordedWork, currentWorkTypeDefName, StringComparison.OrdinalIgnoreCase);
                if ((sameWorkOnly && sameWork) || (!sameWorkOnly && !sameWork))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWorkContext(string gameContext)
        {
            return DiaryContextFields.HasField(gameContext, "work");
        }

        private static string WorkTypeFromContext(string gameContext)
        {
            return DiaryContextFields.Value(gameContext, "work");
        }
    }
}
