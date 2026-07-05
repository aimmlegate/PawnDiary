// Active/archive retention for Pawn Diary. The active cap limits each pawn's HOT full DiaryEvent
// references; old displayable pages compact into the archive before their hot refs are dropped. The
// archive cap then limits each pawn's compact display-only rows. The component owns the saved per-pawn
// diary references, so the archive-and-sweep pass lives here where the hot repository, archive, and
// pawn records can be kept consistent.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Public settings hook: applies the configured active and archive caps to the loaded game.
        /// Safe to call from the mod settings window; errors are logged once instead of escaping into
        /// RimWorld's settings UI.
        /// </summary>
        internal void ApplyDiaryEventLimitsFromSettings()
        {
            try
            {
                ApplyDiaryEventLimits();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Diary event retention limits failed: " + e,
                    "DiaryGameComponent.ApplyDiaryEventLimitsFromSettings".GetHashCode());
            }
        }

        /// <summary>
        /// Compatibility wrapper for older call sites and dev tools that only knew about the active cap.
        /// </summary>
        internal void ApplyActiveEventLimitFromSettings()
        {
            ApplyDiaryEventLimitsFromSettings();
        }

        /// <summary>
        /// Applies both configured retention caps and invalidates cached Diary-tab views once if either
        /// the hot event refs or compact archive rows changed.
        /// </summary>
        private void ApplyDiaryEventLimits()
        {
            bool changed = ApplyActiveEventLimit();
            changed = ApplyArchivedEventLimit() || changed;
            if (!changed)
            {
                return;
            }

            orphanCandidatesLastScan.Clear();
            DiaryStateVersion.Bump();
        }

        /// <summary>
        /// Caps each pawn's diary to its newest configured number of hot pages, archiving old
        /// displayable pages before dropping their hot refs, then drops master-list events that no pawn
        /// references anymore. Runs after new event creation, before save, after load, and when settings
        /// are saved.
        /// </summary>
        private bool ApplyActiveEventLimit()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            int perPawnLimit = settings == null
                ? PawnDiarySettings.DefaultMaxActiveDiaryEvents
                : PawnDiarySettings.ClampActiveDiaryEventLimit(settings.maxActiveDiaryEvents);
            if (HasDevMockStressHistory(perPawnLimit))
            {
                return false;
            }

            return TrimDiariesToPerPawnLimit(perPawnLimit);
        }

        /// <summary>
        /// Caps each pawn's compact archive to the configured number of display-only rows.
        /// </summary>
        private bool ApplyArchivedEventLimit()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            int perPawnLimit = settings == null
                ? PawnDiarySettings.DefaultMaxArchivedDiaryEvents
                : PawnDiarySettings.ClampArchivedDiaryEventLimit(settings.maxArchivedDiaryEvents);
            return archive.TrimPerPawnLimit(perPawnLimit);
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
            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            bool changed = false;
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                List<string> eventIds = diary?.eventIds;
                if (diary == null || eventIds == null || eventIds.Count <= perPawnLimit)
                {
                    continue;
                }

                if (ArchiveAndRemoveOverflowRefs(diary, perPawnLimit, activeEventIds, livePawnsById))
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

        private bool ArchiveAndRemoveOverflowRefs(PawnDiaryRecord diary, int perPawnLimit, HashSet<string> activeEventIds,
            Dictionary<string, Pawn> livePawnsById)
        {
            if (diary == null || diary.eventIds == null || diary.eventIds.Count <= perPawnLimit)
            {
                return false;
            }

            int overflow = diary.eventIds.Count - perPawnLimit;
            string pawnId = diary.pawnId;
            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary, PawnAliveForDiaryBounds(pawnId, livePawnsById));

            // Phase 1 (impure, oldest-first): mark which old refs can leave the hot list and stage the
            // archive row each archiveable drop needs. The scan stops as soon as it has found `overflow`
            // removable refs (the original early-out), and archive WRITES are deferred to phase 2 so a
            // ref is only ever archived if it is actually dropped. removable/staged stay index-aligned.
            List<bool> removable = new List<bool>();
            List<ArchivedDiaryEntry> staged = new List<ArchivedDiaryEntry>();
            int removableCount = 0;
            for (int eventIndex = 0; eventIndex < diary.eventIds.Count && removableCount < overflow; eventIndex++)
            {
                ArchivedDiaryEntry stagedEntry;
                bool canRemove = CanRemoveOverflowRef(
                    pawnId, diary.eventIds[eventIndex], eventIndex, bounds, activeEventIds, out stagedEntry);
                removable.Add(canRemove);
                staged.Add(stagedEntry);
                if (canRemove)
                {
                    removableCount++;
                }
            }

            // Pure rule owns "which indexes, capped at the budget"; phase 1 owns the per-ref yes/no.
            List<int> removeIndexes = DiaryArchiveCompactionPlanner.SelectOverflowRemovals(removable, overflow);
            if (removeIndexes.Count == 0)
            {
                return false;
            }

            // Phase 2: commit the staged archive rows for the chosen refs, then drop those hot refs
            // back-to-front so earlier indexes stay valid as we remove.
            for (int i = 0; i < removeIndexes.Count; i++)
            {
                ArchivedDiaryEntry entry = staged[removeIndexes[i]];
                if (entry != null)
                {
                    archive.AddOrKeep(entry);
                }
            }

            for (int i = removeIndexes.Count - 1; i >= 0; i--)
            {
                diary.eventIds.RemoveAt(removeIndexes[i]);
            }

            return true;
        }

        /// <summary>
        /// Decides whether one over-cap ref can leave the hot list, staging its archive row via
        /// <paramref name="staged"/> when the drop is an archive conversion. A ref is removable when its
        /// event is missing/blank, it sits outside the pawn's arrival/death bounds, it converts cleanly
        /// into an archive row, or it is a cold blank row the UI would never display. Pure write-free:
        /// the caller commits any staged archive row only for refs it actually drops.
        /// </summary>
        private bool CanRemoveOverflowRef(
            string pawnId, string eventId, int eventIndex, DiaryBounds bounds, HashSet<string> activeEventIds,
            out ArchivedDiaryEntry staged)
        {
            staged = null;
            DiaryEvent diaryEvent = events.FindEvent(eventId);
            if (string.IsNullOrWhiteSpace(eventId) || diaryEvent == null)
            {
                return true;
            }

            if (EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, bounds))
            {
                return true;
            }

            if (TryStageArchiveEntry(pawnId, diaryEvent, activeEventIds, out staged))
            {
                return true;
            }

            return ShouldDropUnarchiveableRef(pawnId, diaryEvent, activeEventIds);
        }

        /// <summary>
        /// Builds (but does NOT yet store) the compact archive row for one displayable old POV. Returns
        /// true with a staged row when the page is archiveable and well-formed; false when the POV has no
        /// view or the archive policy keeps it hot. Deferring the actual write keeps archive contents in
        /// lock-step with the refs retention really removes.
        /// </summary>
        private bool TryStageArchiveEntry(
            string pawnId, DiaryEvent diaryEvent, HashSet<string> activeEventIds, out ArchivedDiaryEntry staged)
        {
            staged = null;
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
            if (!archive.WouldAccept(archivedEntry))
            {
                return false;
            }

            staged = archivedEntry;
            return true;
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
