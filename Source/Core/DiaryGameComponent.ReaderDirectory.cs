// Saved-data directory projection for the standalone diary reader.
// This partial stays at the persistence edge: it reads hot diary records and compact archive rows,
// then returns plain counts/names for the UI adapter to combine with live RimWorld pawns.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // One cached, render-ready activity row per diary subject. The bottom-menu button and reader
        // directory draw several times per frame, so saved events are scanned only when their shared
        // render-state version or structural count changes; all per-row reads after that are O(1).
        private struct DiaryReaderActivity
        {
            public int pendingCount;
            public int failedCount;
            public bool hasLatestEntry;
            public int latestEntryTick;
            public string latestEntryDate;
        }

        private readonly Dictionary<string, DiaryReaderActivity> readerActivityByPawnId =
            new Dictionary<string, DiaryReaderActivity>(StringComparer.Ordinal);
        private int readerActivityStateVersion = -1;
        private int readerActivityDataCount = -1;
        private int readerGlobalPendingCount;
        private int readerGlobalFailedCount;
        // Unread acknowledgement does not mutate a DiaryEvent, so it has its own cheap change token.
        // DiaryReaderPawnDirectory watches it in addition to DiaryStateVersion when unread sorting is on.
        private int readerUnreadStateVersion;

        /// <summary>
        /// Compact saved-data row for one pawn known to diary storage.
        /// </summary>
        internal struct DiaryReaderPawnInfo
        {
            public string pawnId;
            public string cachedName;
            public int hotEntryCount;
            public int archivedEntryCount;
            public int unreadCount;
            public bool hasLatestEntry;
            public int latestEntryTick;
            public string latestEntryDate;

            public int EntryCount
            {
                get { return Math.Max(0, hotEntryCount) + Math.Max(0, archivedEntryCount); }
            }
        }

        /// <summary>
        /// Cheap change token used by the reader directory's throttled world snapshot.
        /// </summary>
        internal int DiaryReaderDirectoryDataCount
        {
            get { return (diaries?.Count ?? 0) + archive.Count; }
        }

        /// <summary>Unread-only change token for the throttled pawn-directory projection.</summary>
        internal int DiaryReaderUnreadStateVersion
        {
            get { return readerUnreadStateVersion; }
        }

        /// <summary>
        /// Returns the cached unread/pending/failure status for one reader subject.
        /// </summary>
        internal DiaryCommandStatus ReaderStatusForId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return default(DiaryCommandStatus);
            }

            RefreshReaderActivity();
            DiaryCommandStatus status;
            commandStatusByPawnId.TryGetValue(pawnId, out status);
            DiaryReaderActivity activity;
            if (readerActivityByPawnId.TryGetValue(pawnId, out activity))
            {
                status.pendingCount = activity.pendingCount;
                status.failedCount = activity.failedCount;
            }
            else
            {
                status.pendingCount = 0;
                status.failedCount = 0;
            }

            return status;
        }

        /// <summary>
        /// Aggregates the cached activity snapshot for the standalone Diary bottom-menu button.
        /// </summary>
        internal DiaryCommandStatus GlobalReaderStatus()
        {
            RefreshReaderActivity();
            DiaryCommandStatus status = new DiaryCommandStatus
            {
                pendingCount = readerGlobalPendingCount,
                failedCount = readerGlobalFailedCount
            };

            foreach (KeyValuePair<string, DiaryCommandStatus> pair in commandStatusByPawnId)
            {
                int unread = Math.Max(0, pair.Value.unacknowledgedCount);
                status.unacknowledgedCount = unread > int.MaxValue - status.unacknowledgedCount
                    ? int.MaxValue
                    : status.unacknowledgedCount + unread;
            }

            return status;
        }

        /// <summary>Marks the exact-unread projection as changed without invalidating entry-card caches.</summary>
        private void NotifyReaderUnreadStateChanged()
        {
            readerUnreadStateVersion++;
        }

        /// <summary>
        /// Rebuilds pending/failure/latest-page metadata after rendered event state changes. Archive rows
        /// participate only in latest-page metadata; archived failures are not retryable and therefore do
        /// not leave a permanent global error badge.
        /// </summary>
        private void RefreshReaderActivity()
        {
            int stateVersion = DiaryStateVersion.Current;
            int dataCount = DiaryReaderDirectoryDataCount;
            if (readerActivityStateVersion == stateVersion && readerActivityDataCount == dataCount)
            {
                return;
            }

            readerActivityByPawnId.Clear();
            readerGlobalPendingCount = 0;
            readerGlobalFailedCount = 0;
            if (diaries != null)
            {
                HashSet<string> activeEventIds = ActiveScanEventIds();
                for (int diaryIndex = 0; diaryIndex < diaries.Count; diaryIndex++)
                {
                    PawnDiaryRecord diary = diaries[diaryIndex];
                    if (diary == null
                        || string.IsNullOrWhiteSpace(diary.pawnId)
                        || diary.eventIds == null)
                    {
                        continue;
                    }

                    for (int eventIndex = 0; eventIndex < diary.eventIds.Count; eventIndex++)
                    {
                        DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[eventIndex]);
                        if (diaryEvent == null)
                        {
                            continue;
                        }

                        bool archivedForScans = EventIsArchivedForScans(diaryEvent, activeEventIds);
                        string povRole;
                        bool hasGeneratedText;
                        bool archivedGenerationStale;
                        bool generating;
                        bool promptOnly;
                        bool titlePending;
                        bool failed;
                        if (diaryEvent.TryGetTabStateForPawn(
                                diary.pawnId,
                                archivedForScans,
                                out povRole,
                                out hasGeneratedText,
                                out archivedGenerationStale,
                                out generating,
                                out promptOnly,
                                out titlePending,
                                out failed))
                        {
                            DiaryReaderActivity activity;
                            readerActivityByPawnId.TryGetValue(diary.pawnId, out activity);
                            if (generating || titlePending)
                            {
                                activity.pendingCount++;
                                readerGlobalPendingCount++;
                            }
                            if (failed)
                            {
                                activity.failedCount++;
                                readerGlobalFailedCount++;
                            }
                            if (hasGeneratedText || archivedGenerationStale || failed)
                            {
                                UpdateLatestEntry(
                                    ref activity,
                                    diaryEvent.tick,
                                    diaryEvent.date);
                            }
                            readerActivityByPawnId[diary.pawnId] = activity;
                        }
                    }
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.AllEntries;
            for (int i = 0; i < archivedEntries.Count; i++)
            {
                ArchivedDiaryEntry entry = archivedEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.pawnId))
                {
                    continue;
                }

                DiaryReaderActivity activity;
                readerActivityByPawnId.TryGetValue(entry.pawnId, out activity);
                UpdateLatestEntry(ref activity, entry.tick, entry.date);
                readerActivityByPawnId[entry.pawnId] = activity;
            }

            readerActivityStateVersion = stateVersion;
            readerActivityDataCount = dataCount;
        }

        private static void UpdateLatestEntry(
            ref DiaryReaderActivity activity,
            int tick,
            string date)
        {
            if (activity.hasLatestEntry && tick <= activity.latestEntryTick)
            {
                return;
            }

            activity.hasLatestEntry = true;
            activity.latestEntryTick = tick;
            activity.latestEntryDate = date ?? string.Empty;
        }

        /// <summary>
        /// Collects saved diary subjects, including archive-only pawn IDs whose full record was pruned.
        /// </summary>
        internal void CollectDiaryReaderPawns(List<DiaryReaderPawnInfo> output)
        {
            if (output == null)
            {
                return;
            }

            output.Clear();
            RefreshReaderActivity();
            HashSet<string> coveredPawnIds = new HashSet<string>(StringComparer.Ordinal);
            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord record = diaries[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.pawnId)
                        || !coveredPawnIds.Add(record.pawnId))
                    {
                        continue;
                    }

                    DiaryReaderActivity activity;
                    readerActivityByPawnId.TryGetValue(record.pawnId, out activity);
                    DiaryCommandStatus status = ReaderStatusForId(record.pawnId);
                    output.Add(new DiaryReaderPawnInfo
                    {
                        pawnId = record.pawnId,
                        cachedName = record.pawnName ?? string.Empty,
                        hotEntryCount = record.eventIds?.Count ?? 0,
                        archivedEntryCount = archive.CountForPawn(record.pawnId),
                        unreadCount = status.unacknowledgedCount,
                        hasLatestEntry = activity.hasLatestEntry,
                        latestEntryTick = activity.latestEntryTick,
                        latestEntryDate = activity.latestEntryDate ?? string.Empty
                    });
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = 0; i < archived.Count; i++)
            {
                string pawnId = archived[i]?.pawnId;
                if (string.IsNullOrWhiteSpace(pawnId) || !coveredPawnIds.Add(pawnId))
                {
                    continue;
                }

                DiaryReaderActivity activity;
                readerActivityByPawnId.TryGetValue(pawnId, out activity);
                output.Add(new DiaryReaderPawnInfo
                {
                    pawnId = pawnId,
                    cachedName = string.Empty,
                    hotEntryCount = 0,
                    archivedEntryCount = archive.CountForPawn(pawnId),
                    unreadCount = ReaderStatusForId(pawnId).unacknowledgedCount,
                    hasLatestEntry = activity.hasLatestEntry,
                    latestEntryTick = activity.latestEntryTick,
                    latestEntryDate = activity.latestEntryDate ?? string.Empty
                });
            }
        }
    }
}
