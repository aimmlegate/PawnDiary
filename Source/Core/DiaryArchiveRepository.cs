// Compact archived diary-page store. The hot DiaryEventRepository owns active generation records;
// this repository owns old display-only ArchivedDiaryEntry rows that no longer participate in LLM
// scans. DiaryGameComponent remains the lifecycle owner and calls ExposeArchive from ExposeData.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Saved archive of compact, per-pawn diary page views plus transient lookup indexes rebuilt after
    /// load. One row represents one displayed POV, not a full multi-role event.
    /// </summary>
    internal sealed class DiaryArchiveRepository
    {
        private List<ArchivedDiaryEntry> archiveEntries = new List<ArchivedDiaryEntry>();
        private readonly Dictionary<string, List<ArchivedDiaryEntry>> entriesByPawnId = new Dictionary<string, List<ArchivedDiaryEntry>>();
        private readonly Dictionary<string, ArchivedDiaryEntry> entriesByArchiveKey = new Dictionary<string, ArchivedDiaryEntry>();

        public int Count
        {
            get { return archiveEntries.Count; }
        }

        public IReadOnlyList<ArchivedDiaryEntry> AllEntries
        {
            get { return archiveEntries; }
        }

        public void Clear()
        {
            archiveEntries.Clear();
            entriesByPawnId.Clear();
            entriesByArchiveKey.Clear();
        }

        public IReadOnlyList<ArchivedDiaryEntry> EntriesForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return EmptyArchiveEntries.List;
            }

            List<ArchivedDiaryEntry> entries;
            return entriesByPawnId.TryGetValue(pawnId, out entries) ? entries : EmptyArchiveEntries.List;
        }

        public int CountForPawn(string pawnId)
        {
            return EntriesForPawn(pawnId).Count;
        }

        public bool Contains(string eventId, string pawnId, string povRole)
        {
            string key = ArchivedDiaryEntry.BuildArchiveKey(eventId, pawnId, povRole);
            return !string.IsNullOrWhiteSpace(eventId)
                && !string.IsNullOrWhiteSpace(pawnId)
                && !string.IsNullOrWhiteSpace(povRole)
                && entriesByArchiveKey.ContainsKey(key);
        }

        /// <summary>
        /// True when <paramref name="entry"/> is a well-formed archive row this store would accept (the
        /// same gate <see cref="AddOrKeep"/> applies). Lets retention decide a ref is safe to drop while
        /// only committing the write for refs it actually removes.
        /// </summary>
        public bool WouldAccept(ArchivedDiaryEntry entry)
        {
            return IsValid(entry);
        }

        /// <summary>
        /// Adds an archive row unless an equivalent row already exists. Returns true when the caller may
        /// safely remove the hot pawn ref: either the new row was stored, or a duplicate was already there.
        /// </summary>
        public bool AddOrKeep(ArchivedDiaryEntry entry)
        {
            if (!IsValid(entry))
            {
                return false;
            }

            string key = entry.ArchiveKey;
            if (entriesByArchiveKey.ContainsKey(key))
            {
                return true;
            }

            archiveEntries.Add(entry);
            Index(entry);
            return true;
        }

        public int RemoveForEventIds(HashSet<string> eventIds)
        {
            if (eventIds == null || eventIds.Count == 0)
            {
                return 0;
            }

            int before = archiveEntries.Count;
            archiveEntries.RemoveAll(e => e != null && eventIds.Contains(e.eventId));
            int removed = before - archiveEntries.Count;
            if (removed > 0)
            {
                RebuildIndex();
            }

            return removed;
        }

        /// <summary>
        /// Caps each pawn's compact archive to its newest <paramref name="perPawnLimit"/> rows. The
        /// pawn indexes are already in saved/archive order, oldest first, so the shared retention plan can
        /// decide the survivor keys without this repository mutating while it is planning.
        /// </summary>
        public bool TrimPerPawnLimit(int perPawnLimit)
        {
            if (perPawnLimit < 0 || archiveEntries.Count == 0 || entriesByPawnId.Count == 0)
            {
                return false;
            }

            bool anyOver = false;
            foreach (KeyValuePair<string, List<ArchivedDiaryEntry>> pair in entriesByPawnId)
            {
                List<ArchivedDiaryEntry> entries = pair.Value;
                if (entries != null && entries.Count > perPawnLimit)
                {
                    anyOver = true;
                    break;
                }
            }

            if (!anyOver)
            {
                return false;
            }

            List<IReadOnlyList<string>> perPawnArchiveKeys = new List<IReadOnlyList<string>>();
            foreach (KeyValuePair<string, List<ArchivedDiaryEntry>> pair in entriesByPawnId)
            {
                List<ArchivedDiaryEntry> entries = pair.Value;
                List<string> keys = new List<string>();
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        ArchivedDiaryEntry entry = entries[i];
                        keys.Add(entry == null ? string.Empty : entry.ArchiveKey);
                    }
                }

                perPawnArchiveKeys.Add(keys);
            }

            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(perPawnArchiveKeys, perPawnLimit);
            if (!plan.TrimmedAny)
            {
                return false;
            }

            int before = archiveEntries.Count;
            archiveEntries.RemoveAll(e => e == null || !plan.Referenced.Contains(e.ArchiveKey));
            if (archiveEntries.Count == before)
            {
                return false;
            }

            RebuildIndex();
            return true;
        }

        public int? FirstArrivalTickForPawn(string pawnId)
        {
            IReadOnlyList<ArchivedDiaryEntry> entries = EntriesForPawn(pawnId);
            int? first = null;
            for (int i = 0; i < entries.Count; i++)
            {
                ArchivedDiaryEntry entry = entries[i];
                if (entry == null || !entry.IsArrivalDescriptionFor(pawnId))
                {
                    continue;
                }

                if (!first.HasValue || entry.tick < first.Value)
                {
                    first = entry.tick;
                }
            }

            return first;
        }

        public int? FinalDeathTickForPawn(string pawnId)
        {
            IReadOnlyList<ArchivedDiaryEntry> entries = EntriesForPawn(pawnId);
            int? last = null;
            for (int i = 0; i < entries.Count; i++)
            {
                ArchivedDiaryEntry entry = entries[i];
                if (entry == null || !entry.IsDeathDescriptionFor(pawnId))
                {
                    continue;
                }

                if (!last.HasValue || entry.tick > last.Value)
                {
                    last = entry.tick;
                }
            }

            return last;
        }

        public void RebuildIndex()
        {
            entriesByPawnId.Clear();
            entriesByArchiveKey.Clear();
            for (int i = 0; i < archiveEntries.Count; i++)
            {
                ArchivedDiaryEntry entry = archiveEntries[i];
                if (!IsValid(entry))
                {
                    continue;
                }

                string key = entry.ArchiveKey;
                if (entriesByArchiveKey.ContainsKey(key))
                {
                    continue;
                }

                Index(entry);
            }
        }

        public void ExposeArchive(string label)
        {
            Scribe_Collections.Look(ref archiveEntries, label, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RepairLoadedEntries();
                RebuildIndex();
            }
        }

        private void RepairLoadedEntries()
        {
            if (archiveEntries == null)
            {
                archiveEntries = new List<ArchivedDiaryEntry>();
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < archiveEntries.Count; i++)
            {
                ArchivedDiaryEntry entry = archiveEntries[i];
                if (!IsValid(entry) || !seen.Add(entry.ArchiveKey))
                {
                    archiveEntries.RemoveAt(i);
                    i--;
                }
            }
        }

        private void Index(ArchivedDiaryEntry entry)
        {
            entriesByArchiveKey[entry.ArchiveKey] = entry;

            List<ArchivedDiaryEntry> entries;
            if (!entriesByPawnId.TryGetValue(entry.pawnId, out entries))
            {
                entries = new List<ArchivedDiaryEntry>();
                entriesByPawnId[entry.pawnId] = entries;
            }

            entries.Add(entry);
        }

        private static bool IsValid(ArchivedDiaryEntry entry)
        {
            return entry != null
                && !string.IsNullOrWhiteSpace(entry.eventId)
                && !string.IsNullOrWhiteSpace(entry.pawnId)
                && !string.IsNullOrWhiteSpace(entry.povRole);
        }

        private static class EmptyArchiveEntries
        {
            public static readonly IReadOnlyList<ArchivedDiaryEntry> List = new ArchivedDiaryEntry[0];
        }
    }
}
