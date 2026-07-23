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
        // Lookup by (eventId, povRole) for the public integration API entry-snapshot read, which does
        // not always know the pawn id. Keyed by EventAndRoleKey; the value is the newest matching row
        // because rows are appended in archive order and Index overwrites on each insert.
        private readonly Dictionary<string, ArchivedDiaryEntry> entriesByEventAndRole = new Dictionary<string, ArchivedDiaryEntry>(StringComparer.Ordinal);
        // N1 cold-history lookups are scoped by the archived POV pawn. A shared arc or subject is not
        // automatically known by every colonist, so no colony-wide narrative lookup exists here.
        private readonly Dictionary<string, List<ArchivedDiaryEntry>> entriesByNarrativeArc =
            new Dictionary<string, List<ArchivedDiaryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ArchivedDiaryEntry>> entriesByNarrativeSubject =
            new Dictionary<string, List<ArchivedDiaryEntry>>(StringComparer.Ordinal);
        // Cross-arc reflection asks only for pages that carry exact saved references. Keeping that
        // per-pawn subset indexed prevents each rest opportunity from projecting the full archive.
        private readonly Dictionary<string, List<ArchivedDiaryEntry>> narrativeEntriesByPawnId =
            new Dictionary<string, List<ArchivedDiaryEntry>>(StringComparer.Ordinal);

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
            entriesByEventAndRole.Clear();
            entriesByNarrativeArc.Clear();
            entriesByNarrativeSubject.Clear();
            narrativeEntriesByPawnId.Clear();
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

        /// <summary>
        /// Returns only archived POV pages that carry exact narrative references, oldest first. This
        /// transient index is rebuilt from saved rows and lets rest-time selection read a bounded newest
        /// slice without scanning unrelated archive pages.
        /// </summary>
        public IReadOnlyList<ArchivedDiaryEntry> NarrativeEntriesForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return EmptyArchiveEntries.List;
            }

            List<ArchivedDiaryEntry> entries;
            return narrativeEntriesByPawnId.TryGetValue(pawnId, out entries)
                ? entries
                : EmptyArchiveEntries.List;
        }

        /// <summary>
        /// Returns archived pages for one POV pawn that reference an exact narrative arc. The list is
        /// in archive order, oldest first; blank identity parts produce an empty read-only result.
        /// </summary>
        public IReadOnlyList<ArchivedDiaryEntry> EntriesForNarrativeArc(string pawnId, string arcKey)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(arcKey))
            {
                return EmptyArchiveEntries.List;
            }

            List<ArchivedDiaryEntry> entries;
            return entriesByNarrativeArc.TryGetValue(NarrativeArcIndexKey(pawnId, arcKey), out entries)
                ? entries
                : EmptyArchiveEntries.List;
        }

        /// <summary>
        /// Returns archived pages for one POV pawn that reference one exact narrative subject. Both
        /// kind and id are required, so a matching display label cannot join unrelated subjects.
        /// </summary>
        public IReadOnlyList<ArchivedDiaryEntry> EntriesForNarrativeSubject(
            string pawnId,
            string subjectKind,
            string subjectId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)
                || string.IsNullOrWhiteSpace(NarrativePersistencePolicy.SubjectIndexKey(subjectKind, subjectId)))
            {
                return EmptyArchiveEntries.List;
            }

            List<ArchivedDiaryEntry> entries;
            return entriesByNarrativeSubject.TryGetValue(
                NarrativeSubjectIndexKey(pawnId, subjectKind, subjectId), out entries)
                ? entries
                : EmptyArchiveEntries.List;
        }

        public bool Contains(string eventId, string pawnId, string povRole)
        {
            string key = ArchivedDiaryEntry.BuildArchiveKey(eventId, pawnId, povRole);
            return !string.IsNullOrWhiteSpace(eventId)
                && !string.IsNullOrWhiteSpace(pawnId)
                && !string.IsNullOrWhiteSpace(povRole)
                && entriesByArchiveKey.ContainsKey(key);
        }

        public ArchivedDiaryEntry Find(string eventId, string pawnId, string povRole)
        {
            string key = ArchivedDiaryEntry.BuildArchiveKey(eventId, pawnId, povRole);
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(pawnId)
                || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            ArchivedDiaryEntry entry;
            return entriesByArchiveKey.TryGetValue(key, out entry) ? entry : null;
        }

        public ArchivedDiaryEntry FindByEventAndRole(string eventId, string povRole)
        {
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            // O(1) index hit for the common case (the public integration API entry-snapshot read).
            // The index always holds the newest matching row because Index overwrites on each insert
            // and rows are appended in archive order. If the index somehow misses (it is rebuilt in
            // lockstep with the other indexes on load, add, and remove), fall back to the original
            // newest-first scan so correctness never depends on the index alone.
            ArchivedDiaryEntry indexed;
            if (entriesByEventAndRole.TryGetValue(EventAndRoleKey(eventId, povRole), out indexed) && indexed != null)
            {
                return indexed;
            }

            for (int i = archiveEntries.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archiveEntries[i];
                if (entry != null
                    && string.Equals(entry.eventId, eventId, StringComparison.Ordinal)
                    && string.Equals(entry.povRole, povRole, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
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
        /// Removes every compact archive row for one pawn. Used only by dev tooling; hot DiaryEvent
        /// records stay untouched so the button can clear cold display history without changing active
        /// generation/retry state.
        /// </summary>
        public int RemoveForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || archiveEntries.Count == 0)
            {
                return 0;
            }

            int before = archiveEntries.Count;
            archiveEntries.RemoveAll(e => e != null && string.Equals(e.pawnId, pawnId, StringComparison.Ordinal));
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
            entriesByEventAndRole.Clear();
            entriesByNarrativeArc.Clear();
            entriesByNarrativeSubject.Clear();
            narrativeEntriesByPawnId.Clear();
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

            // Newest-wins for the (eventId, povRole) index: rows are appended in archive order, so a
            // later insert for the same key is newer and should win the public lookup. This matches the
            // backward-scan fallback, which returns the last (newest) match.
            entriesByEventAndRole[EventAndRoleKey(entry.eventId, entry.povRole)] = entry;

            List<ArchivedDiaryEntry> entries;
            if (!entriesByPawnId.TryGetValue(entry.pawnId, out entries))
            {
                entries = new List<ArchivedDiaryEntry>();
                entriesByPawnId[entry.pawnId] = entries;
            }

            entries.Add(entry);

            List<NarrativeReference> references = NarrativeStatePersistence.ToReferences(entry.narrativeReferences);
            if (references.Count > 0)
            {
                AddToNarrativePawnIndex(entry);
            }
            for (int i = 0; i < references.Count; i++)
            {
                NarrativeReference reference = references[i];
                if (reference == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(reference.arcKey))
                {
                    AddToNarrativeIndex(entriesByNarrativeArc,
                        NarrativeArcIndexKey(entry.pawnId, reference.arcKey), entry);
                }

                if (!string.IsNullOrWhiteSpace(
                    NarrativePersistencePolicy.SubjectIndexKey(reference.subjectKind, reference.subjectId)))
                {
                    AddToNarrativeIndex(entriesByNarrativeSubject,
                        NarrativeSubjectIndexKey(entry.pawnId, reference.subjectKind, reference.subjectId), entry);
                }
            }
        }

        private static void AddToNarrativeIndex(
            Dictionary<string, List<ArchivedDiaryEntry>> index,
            string key,
            ArchivedDiaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(key) || entry == null)
            {
                return;
            }

            List<ArchivedDiaryEntry> entries;
            if (!index.TryGetValue(key, out entries))
            {
                entries = new List<ArchivedDiaryEntry>();
                index[key] = entries;
            }

            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        private void AddToNarrativePawnIndex(ArchivedDiaryEntry entry)
        {
            List<ArchivedDiaryEntry> entries;
            if (!narrativeEntriesByPawnId.TryGetValue(entry.pawnId, out entries))
            {
                entries = new List<ArchivedDiaryEntry>();
                narrativeEntriesByPawnId[entry.pawnId] = entries;
            }

            int insertAt = entries.Count;
            while (insertAt > 0 && CompareNarrativeArchiveOrder(entry, entries[insertAt - 1]) < 0)
            {
                insertAt--;
            }
            entries.Insert(insertAt, entry);
        }

        private static int CompareNarrativeArchiveOrder(
            ArchivedDiaryEntry left,
            ArchivedDiaryEntry right)
        {
            int tick = left.tick.CompareTo(right.tick);
            if (tick != 0) return tick;
            return string.Compare(left.ArchiveKey, right.ArchiveKey, StringComparison.Ordinal);
        }

        // Stable composite key for the (eventId, povRole) lookup. The unit separator (\x1F) cannot
        // appear in either field, so the pair round-trips unambiguously. povRole is lowercased to
        // match the OrdinalIgnoreCase comparison the fallback scan uses.
        private static string EventAndRoleKey(string eventId, string povRole)
        {
            return eventId + '\x1F' + (povRole ?? string.Empty).ToLowerInvariant();
        }

        private static string NarrativeArcIndexKey(string pawnId, string arcKey)
        {
            return pawnId.Trim() + '\x1F' + arcKey.Trim();
        }

        private static string NarrativeSubjectIndexKey(string pawnId, string subjectKind, string subjectId)
        {
            return pawnId.Trim() + '\x1F' + NarrativePersistencePolicy.SubjectIndexKey(subjectKind, subjectId);
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
