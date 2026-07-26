// Pure planning for rebuilding the saved hot-event list after load.
//
// RimWorld/Scribe owns deserialization and DiaryEvent owns the mutable save model. This file knows
// neither: the repository projects only identity facts here, receives source indexes to retain, and
// commits the resulting event list/index atomically at the impure persistence boundary.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Plain identity facts for one row in the just-loaded hot-event list.</summary>
    internal sealed class LoadedEventIdentity
    {
        public int sourceIndex;
        public int tick;
        public string eventId = string.Empty;
        public bool eventIdWasRepairedOnLoad;
    }

    /// <summary>Ordered source indexes selected by the pure loaded-event repair policy.</summary>
    internal sealed class LoadedEventRepairPlan
    {
        public readonly List<int> retainedSourceIndexes = new List<int>();
        public readonly List<int> repairedIdSourceIndexes = new List<int>();

        /// <summary>Null rows, out-of-range source indexes, and blank event ids discarded.</summary>
        public int invalidRowCount;

        /// <summary>Rows discarded because an earlier stable-tick row already owned that event id.</summary>
        public int duplicateEventIdCount;

        /// <summary>Rows discarded because the same source index appeared more than once.</summary>
        public int duplicateSourceIndexCount;

        /// <summary>Total rows rejected by the repair plan.</summary>
        public int DiscardedRowCount
        {
            get
            {
                return invalidRowCount + duplicateEventIdCount + duplicateSourceIndexCount;
            }
        }
    }

    /// <summary>
    /// Selects the valid, uniquely identified loaded rows in stable tick order. Duplicate IDs keep the
    /// earliest row in that order because a saved diary reference cannot identify which duplicate it
    /// originally meant; inventing a replacement ID would silently guess ownership.
    /// </summary>
    internal static class LoadedEventRepairPolicy
    {
        /// <summary>Builds a deterministic repair plan without reading or mutating save models.</summary>
        public static LoadedEventRepairPlan Plan(IList<LoadedEventIdentity> identities)
        {
            LoadedEventRepairPlan plan = new LoadedEventRepairPlan();
            if (identities == null || identities.Count == 0)
            {
                return plan;
            }

            List<LoadedEventIdentity> ordered = new List<LoadedEventIdentity>(identities.Count);
            for (int i = 0; i < identities.Count; i++)
            {
                LoadedEventIdentity identity = identities[i];
                if (identity != null)
                {
                    ordered.Add(identity);
                }
                else
                {
                    plan.invalidRowCount++;
                }
            }

            // List<T>.Sort is not stable. The source index is the explicit tie-breaker that preserves
            // original equal-tick order across runtimes.
            ordered.Sort(CompareStableTickOrder);

            HashSet<int> seenSourceIndexes = new HashSet<int>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                LoadedEventIdentity identity = ordered[i];
                if (identity.sourceIndex < 0
                    || identity.sourceIndex >= identities.Count
                    || string.IsNullOrWhiteSpace(identity.eventId))
                {
                    plan.invalidRowCount++;
                    continue;
                }

                if (seenSourceIndexes.Contains(identity.sourceIndex))
                {
                    plan.duplicateSourceIndexCount++;
                    continue;
                }

                if (seenIds.Contains(identity.eventId))
                {
                    plan.duplicateEventIdCount++;
                    continue;
                }

                seenSourceIndexes.Add(identity.sourceIndex);
                seenIds.Add(identity.eventId);
                plan.retainedSourceIndexes.Add(identity.sourceIndex);
                if (identity.eventIdWasRepairedOnLoad)
                {
                    plan.repairedIdSourceIndexes.Add(identity.sourceIndex);
                }
            }

            return plan;
        }

        private static int CompareStableTickOrder(
            LoadedEventIdentity left,
            LoadedEventIdentity right)
        {
            int tickOrder = left.tick.CompareTo(right.tick);
            return tickOrder != 0 ? tickOrder : left.sourceIndex.CompareTo(right.sourceIndex);
        }
    }
}
