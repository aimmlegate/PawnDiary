// Read-only snapshots for the public integration API. This partial keeps adapter-facing reads close
// to the saved diary stores while still returning only plain DTOs from PawnDiary.Integration.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const int IntegrationRecentTitleHardCap = 20;

        /// <summary>
        /// Builds newest-first diary title snapshots for one pawn. Called only by PawnDiaryApi, which
        /// already checks main-thread/game-readiness rules and catches exceptions for adapter safety.
        /// </summary>
        internal List<DiaryEntryTitleSnapshot> RecentEntryTitleSnapshotsFor(Pawn pawn, int maxCount)
        {
            List<DiaryEntryTitleSnapshot> snapshots = new List<DiaryEntryTitleSnapshot>();
            if (pawn == null || maxCount <= 0)
            {
                return snapshots;
            }

            int limit = maxCount > IntegrationRecentTitleHardCap
                ? IntegrationRecentTitleHardCap
                : maxCount;
            string pawnId = pawn.GetUniqueLoadID();
            HashSet<string> emittedKeys = new HashSet<string>();

            AppendRecentHotTitleSnapshots(pawn, pawnId, limit, emittedKeys, snapshots);
            if (snapshots.Count < limit)
            {
                AppendRecentArchivedTitleSnapshots(pawnId, limit, emittedKeys, snapshots);
            }

            return snapshots;
        }

        private void AppendRecentHotTitleSnapshots(
            Pawn pawn,
            string pawnId,
            int limit,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            HashSet<string> activeEventIds = ActiveScanEventIds();
            for (int i = diary.eventIds.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                DiaryEntryView view = diaryEvent?.ToViewFor(pawnId, EventIsArchivedForScans(diaryEvent, activeEventIds));
                TryAppendTitleSnapshot(view, false, emittedKeys, snapshots);
            }
        }

        private void AppendRecentArchivedTitleSnapshots(
            string pawnId,
            int limit,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.EntriesForPawn(pawnId);
            if (archivedEntries == null)
            {
                return;
            }

            for (int i = archivedEntries.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                ArchivedDiaryEntry archivedEntry = archivedEntries[i];
                TryAppendTitleSnapshot(archivedEntry?.ToView(), true, emittedKeys, snapshots);
            }
        }

        private static void TryAppendTitleSnapshot(
            DiaryEntryView view,
            bool archived,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            if (view == null || snapshots == null || !ViewHasCompletedDiaryPage(view))
            {
                return;
            }

            string key = view.EntryKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && emittedKeys != null && !emittedKeys.Add(key))
            {
                return;
            }

            snapshots.Add(new DiaryEntryTitleSnapshot
            {
                tick = view.Tick,
                date = view.Date ?? string.Empty,
                eventId = view.EventId ?? string.Empty,
                povRole = view.PovRole ?? string.Empty,
                title = view.Title ?? string.Empty,
                groupLabel = view.GroupLabel ?? string.Empty,
                archived = archived
            });
        }

        private static bool ViewHasCompletedDiaryPage(DiaryEntryView view)
        {
            return view != null
                && (!string.IsNullOrWhiteSpace(view.GeneratedText)
                    || !string.IsNullOrWhiteSpace(view.Title));
        }
    }
}
