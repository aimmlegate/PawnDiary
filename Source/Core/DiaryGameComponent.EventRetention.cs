// Active event retention for Pawn Diary. This is the per-pawn HOT history cap: each pawn keeps only
// its newest configured number of full DiaryEvent references. When an older page is safe to keep only
// for display, retention copies that POV into the compact archive before dropping the hot ref. The
// repository owns the hot master list and lookup index; the archive owns cold display rows; this
// component owns the saved per-pawn diary references, so the archive-and-sweep pass lives here where
// all three can be kept consistent.
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
        /// Caps each pawn's diary to its newest configured number of hot pages, archiving old
        /// displayable pages before dropping their hot refs, then drops master-list events that no pawn
        /// references anymore. Runs on every new event, on save/load, and when settings are saved.
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
        /// Caps every pawn's hot event-id list to its newest <paramref name="perPawnLimit"/> references
        /// (the oldest sit at the front, since refs are appended in tick order). Old completed/stale
        /// displayable pages are first copied into the compact archive; only then is the hot ref removed.
        /// A hot master-list event survives until no pawn still references it. Returns true when anything
        /// changed, so callers can skip the version bump on the common no-op path.
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

            HashSet<string> activeEventIds = ActiveScanEventIds();
            bool changed = false;
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                List<string> eventIds = diary?.eventIds;
                if (diary == null || eventIds == null || eventIds.Count <= perPawnLimit)
                {
                    continue;
                }

                if (ArchiveAndRemoveOverflowRefs(diary, perPawnLimit, activeEventIds))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return false;
            }

            // Sweep the master list down to events still referenced by at least one HOT pawn diary ref.
            // Compact archive rows are display-only and deliberately do not keep full DiaryEvent records
            // alive for generation/title/orphan scans.
            events.RetainOnly(CollectHotReferencedEventIds());
            return true;
        }

        private bool ArchiveAndRemoveOverflowRefs(PawnDiaryRecord diary, int perPawnLimit, HashSet<string> activeEventIds)
        {
            if (diary == null || diary.eventIds == null || diary.eventIds.Count <= perPawnLimit)
            {
                return false;
            }

            int overflow = diary.eventIds.Count - perPawnLimit;
            string pawnId = diary.pawnId;
            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary);
            List<int> removeIndexes = null;

            for (int eventIndex = 0; eventIndex < diary.eventIds.Count && (removeIndexes == null || removeIndexes.Count < overflow); eventIndex++)
            {
                string eventId = diary.eventIds[eventIndex];
                DiaryEvent diaryEvent = events.FindEvent(eventId);
                bool remove = string.IsNullOrWhiteSpace(eventId) || diaryEvent == null;
                if (!remove && EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, bounds))
                {
                    remove = true;
                }
                else if (!remove && TryArchiveDiaryRef(pawnId, diaryEvent, activeEventIds))
                {
                    remove = true;
                }
                else if (!remove && ShouldDropUnarchiveableRef(pawnId, diaryEvent, activeEventIds))
                {
                    remove = true;
                }

                if (remove)
                {
                    if (removeIndexes == null)
                    {
                        removeIndexes = new List<int>();
                    }

                    removeIndexes.Add(eventIndex);
                }
            }

            if (removeIndexes == null || removeIndexes.Count == 0)
            {
                return false;
            }

            for (int i = removeIndexes.Count - 1; i >= 0; i--)
            {
                diary.eventIds.RemoveAt(removeIndexes[i]);
            }

            return true;
        }

        private bool TryArchiveDiaryRef(string pawnId, DiaryEvent diaryEvent, HashSet<string> activeEventIds)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvent == null)
            {
                return false;
            }

            bool archivedForScans = EventIsArchivedForScans(diaryEvent, activeEventIds);
            DiaryEntryView view = diaryEvent.ToViewFor(pawnId, archivedForScans);
            if (view == null)
            {
                return false;
            }

            bool forceFallback;
            if (!CanArchiveView(view, out forceFallback))
            {
                return false;
            }

            ArchivedDiaryEntry archivedEntry = ArchivedDiaryEntry.FromEvent(diaryEvent, pawnId, view, forceFallback);
            return archive.AddOrKeep(archivedEntry);
        }

        private static bool CanArchiveView(DiaryEntryView view, out bool forceFallback)
        {
            if (view == null)
            {
                forceFallback = false;
                return false;
            }

            DiaryArchiveEligibilityDecision decision = DiaryArchiveEligibility.Evaluate(
                view.TitlePending,
                view.GeneratedText,
                view.ArchivedGenerationStale,
                view.LlmStatus,
                view.Text);
            forceFallback = decision.ForceFallback;
            return decision.CanArchive;
        }

        private bool ShouldDropUnarchiveableRef(string pawnId, DiaryEvent diaryEvent, HashSet<string> activeEventIds)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvent == null)
            {
                return true;
            }

            // Inside the hot scan window, an unarchiveable row may still be retried, title-backfilled, or
            // inspected by dev tools. Outside it, rows that have no displayable archive representation are
            // cold noise and should not keep the pawn permanently over the hot cap.
            bool archivedForScans = EventIsArchivedForScans(diaryEvent, activeEventIds);
            if (!archivedForScans)
            {
                return false;
            }

            DiaryEntryView view = diaryEvent.ToViewFor(pawnId, true);
            if (view == null)
            {
                return true;
            }

            return DiaryArchiveEligibility.ShouldDropColdUndisplayableRef(
                archivedForScans,
                view.TitlePending,
                view.LlmStatus,
                view.GeneratedText,
                view.ArchivedGenerationStale);
        }

        private HashSet<string> CollectHotReferencedEventIds()
        {
            HashSet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (diaries == null)
            {
                return referenced;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                List<string> eventIds = diaries[i]?.eventIds;
                if (eventIds == null)
                {
                    continue;
                }

                for (int j = 0; j < eventIds.Count; j++)
                {
                    string eventId = eventIds[j];
                    if (!string.IsNullOrWhiteSpace(eventId))
                    {
                        referenced.Add(eventId);
                    }
                }
            }

            return referenced;
        }
    }
}
