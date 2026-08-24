// ImportedPayloadBudget.cs — pure whole-unit admission planning for the migration-only archive
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T17.5 global Imported admission, §T13.5).
//
// A resolved exact owner's complete Imported candidate is ONE unit; the complete unresolved
// component input is ONE unit. Units sort by earliest authored original tick (ageUnknown last),
// exact owner ID ordinally, then the input-local source index; the unresolved unit places last.
// A unit admits only when its owner row count, the global row count, and aggregate logical bytes
// all fit; otherwise that complete unit stays raw/pending — never a prefix of one owner.
// Byte totals come ONLY from MemoryLogicalPayloadSizer results; no parallel formula here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One detached archive-admission candidate unit.</summary>
    internal sealed class MemoryImportedAdmissionUnit
    {
        /// <summary>Exact owner ID, or empty for the single unresolved component unit.</summary>
        public string ownerPawnId = string.Empty;
        /// <summary>Input-local ordering coordinate; never persisted as identity (§T6.8).</summary>
        public int sourceIndex;
        /// <summary>Earliest authored original tick across the unit's rows; ageUnknown last.</summary>
        public long earliestAuthoredTick;
        public bool anyAgeUnknown;
        public int rowCount;
        /// <summary>Sizer-measured logical bytes for the COMPLETE unit.</summary>
        public long logicalBytes;
    }

    internal enum MemoryImportedAdmissionOutcome
    {
        Admitted = 0,
        Pending = 1,
        Invalid = 2
    }

    internal sealed class MemoryImportedAdmissionDecision
    {
        public MemoryImportedAdmissionOutcome outcome;
        /// <summary>Parallel to the input list: true when that unit admits this round.</summary>
        public List<bool> admitted;
        public long totalRows;
        public long totalBytes;
    }

    internal static class ImportedPayloadBudget
    {
        public const string SchemaToken = "imported-payload-budget-v1";

        /// <summary>
        /// Plans one whole-unit admission round. Pure: rerun over equal inputs is identical.
        /// Caps come from M0-frozen production/defensive values normalized upstream.
        /// </summary>
        public static MemoryImportedAdmissionDecision PlanAdmission(
            List<MemoryImportedAdmissionUnit> units,
            int maxOwnerRows,
            int maxGlobalRows,
            long importedOwnerBytesCap,
            long importedGlobalBytesCap,
            long globalCombinedBytesCurrent,
            long combinedGlobalBytesCap)
        {
            var refused = new MemoryImportedAdmissionDecision
            {
                outcome = MemoryImportedAdmissionOutcome.Invalid,
                admitted = new List<bool>()
            };
            if (units == null || maxOwnerRows <= 0 || maxGlobalRows <= 0
                || importedOwnerBytesCap <= 0 || importedGlobalBytesCap <= 0
                || combinedGlobalBytesCap <= 0 || globalCombinedBytesCurrent < 0)
            {
                return refused;
            }

            // Whole-unit invariants: exactly ONE unresolved unit and at most one unit per owner
            // (§T17.5). Duplicates would otherwise let a prefix of one owner commit.
            var seenOwners = new HashSet<string>(StringComparer.Ordinal);
            int unresolvedCount = 0;
            foreach (MemoryImportedAdmissionUnit unit in units)
            {
                if (!IsValidUnit(unit))
                {
                    return refused;
                }

                if (string.IsNullOrEmpty(unit.ownerPawnId))
                {
                    unresolvedCount++;
                    if (unresolvedCount > 1)
                    {
                        return refused;
                    }
                }
                else if (!seenOwners.Add(unit.ownerPawnId))
                {
                    return refused;
                }

                if (!IsValidUnit(unit))
                {
                    return refused;
                }
            }

            try
            {
                // Deterministic whole-unit order: tick ascending with ageUnknown last, then owner
                // ordinal, then input index; the empty-owner unresolved unit always sorts last
                // (§T17.5). Sorting (index, unit) pairs keeps admitted[] mapping stable even for
                // tuple-equal units.
                var ordered = new List<(int index, MemoryImportedAdmissionUnit unit)>();
                for (int i = 0; i < units.Count; i++)
                {
                    ordered.Add((i, units[i]));
                }

                ordered.Sort((left, right) =>
                {
                    int compare = CompareUnits(left.unit, right.unit);
                    return compare != 0 ? compare : left.index.CompareTo(right.index);
                });

                var decision = new MemoryImportedAdmissionDecision
                {
                    outcome = MemoryImportedAdmissionOutcome.Admitted,
                    admitted = new List<bool>(new bool[units.Count])
                };

                var ownerRows = new Dictionary<string, long>(StringComparer.Ordinal);
                var ownerBytes = new Dictionary<string, long>(StringComparer.Ordinal);
                long totalRows = 0;
                long totalBytes = 0;

                foreach ((int inputIndex, MemoryImportedAdmissionUnit unit) in ordered)
                {
                    bool isUnresolved = string.IsNullOrEmpty(unit.ownerPawnId);

                    long ownerRowTotal = isUnresolved
                        ? 0
                        : (ownerRows.TryGetValue(unit.ownerPawnId, out long rows) ? rows : 0);
                    long ownerByteTotal = isUnresolved
                        ? 0
                        : (ownerBytes.TryGetValue(unit.ownerPawnId, out long bytes) ? bytes : 0);

                    bool fitsRows = totalRows + unit.rowCount <= maxGlobalRows
                        && (isUnresolved || ownerRowTotal + unit.rowCount <= maxOwnerRows);
                    bool fitsBytes =
                        totalBytes + unit.logicalBytes <= importedGlobalBytesCap
                        && (isUnresolved
                            || ownerByteTotal + unit.logicalBytes <= importedOwnerBytesCap)
                        && globalCombinedBytesCurrent + totalBytes + unit.logicalBytes
                            <= combinedGlobalBytesCap;

                    if (!fitsRows || !fitsBytes)
                    {
                        // Whole-unit rule: leave this complete unit raw/pending; earlier unrelated
                        // units stay committed; never a prefix of one owner (§T13.5).
                        decision.outcome = MemoryImportedAdmissionOutcome.Pending;
                        continue;
                    }

                    decision.admitted[inputIndex] = true;
                    totalRows += unit.rowCount;
                    totalBytes += unit.logicalBytes;
                    if (!isUnresolved)
                    {
                        ownerRows[unit.ownerPawnId] = ownerRowTotal + unit.rowCount;
                        ownerBytes[unit.ownerPawnId] = ownerByteTotal + unit.logicalBytes;
                    }
                }

                decision.totalRows = totalRows;
                decision.totalBytes = totalBytes;
                return decision;
            }
            catch (OverflowException)
            {
                // Byte totals never wrap; overflow is Invalid input, never an admission.
                return refused;
            }
        }

        private static int CompareUnits(
            MemoryImportedAdmissionUnit left, MemoryImportedAdmissionUnit right)
        {
            bool leftUnresolved = string.IsNullOrEmpty(left.ownerPawnId);
            bool rightUnresolved = string.IsNullOrEmpty(right.ownerPawnId);
            if (leftUnresolved != rightUnresolved)
            {
                return leftUnresolved ? 1 : -1; // unresolved last
            }

            bool leftUnknownDate = left.anyAgeUnknown;
            bool rightUnknownDate = right.anyAgeUnknown;
            if (leftUnknownDate != rightUnknownDate)
            {
                return leftUnknownDate ? 1 : -1; // ageUnknown last
            }

            int tick = left.earliestAuthoredTick.CompareTo(right.earliestAuthoredTick);
            if (tick != 0)
            {
                return tick;
            }

            int owner = string.CompareOrdinal(left.ownerPawnId, right.ownerPawnId);
            if (owner != 0)
            {
                return owner;
            }

            return left.sourceIndex.CompareTo(right.sourceIndex);
        }

        private static bool IsValidUnit(MemoryImportedAdmissionUnit unit)
        {
            return unit != null
                && unit.rowCount >= 0
                && unit.logicalBytes >= 0
                && unit.sourceIndex >= 0
                && unit.earliestAuthoredTick >= 0;
        }
    }
}
