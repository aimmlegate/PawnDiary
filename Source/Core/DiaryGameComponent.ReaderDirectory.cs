// Saved-data directory projection for the standalone diary reader.
// This partial stays at the persistence edge: it reads hot diary records and compact archive rows,
// then returns plain counts/names for the UI adapter to combine with live RimWorld pawns.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // The reader draws every pawn row several times per frame. Re-scan hot event references only
        // when DiaryEvent setters bump the shared render-state version, then answer each row in O(1).
        private readonly HashSet<string> readerWritingPawnIds =
            new HashSet<string>(StringComparer.Ordinal);
        private int readerWritingStateVersion = -1;

        /// <summary>
        /// Compact saved-data row for one pawn known to diary storage.
        /// </summary>
        internal struct DiaryReaderPawnInfo
        {
            public string pawnId;
            public string cachedName;
            public int hotEntryCount;
            public int archivedEntryCount;

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

        /// <summary>
        /// Returns whether this pawn has a main page or follow-up title currently being generated.
        /// </summary>
        internal bool IsDiaryWritingFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            RefreshReaderWritingPawnIds();
            return readerWritingPawnIds.Contains(pawnId);
        }

        /// <summary>
        /// Rebuilds the standalone reader's pending-pawn snapshot after any rendered event state changes.
        /// </summary>
        private void RefreshReaderWritingPawnIds()
        {
            int stateVersion = DiaryStateVersion.Current;
            if (readerWritingStateVersion == stateVersion)
            {
                return;
            }

            readerWritingPawnIds.Clear();
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
                        if (diaryEvent.TryGetTabStateForPawn(
                                diary.pawnId,
                                archivedForScans,
                                out povRole,
                                out hasGeneratedText,
                                out archivedGenerationStale,
                                out generating,
                                out promptOnly,
                                out titlePending)
                            && (generating || titlePending))
                        {
                            readerWritingPawnIds.Add(diary.pawnId);
                            break;
                        }
                    }
                }
            }

            readerWritingStateVersion = stateVersion;
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

                    output.Add(new DiaryReaderPawnInfo
                    {
                        pawnId = record.pawnId,
                        cachedName = record.pawnName ?? string.Empty,
                        hotEntryCount = record.eventIds?.Count ?? 0,
                        archivedEntryCount = archive.CountForPawn(record.pawnId)
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

                output.Add(new DiaryReaderPawnInfo
                {
                    pawnId = pawnId,
                    cachedName = string.Empty,
                    hotEntryCount = 0,
                    archivedEntryCount = archive.CountForPawn(pawnId)
                });
            }
        }
    }
}
