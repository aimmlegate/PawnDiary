// ActiveMemoryPayloadBudget.cs — pure checked active/combined byte-budget deltas
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T17.5 measured-unit definitions).
//
// The budget consumes logical bytes produced ONLY by MemoryLogicalPayloadSizer — never its own
// formula. One owner's combined bytes equal its active plus Imported totals; global active bytes
// equal activeComponentBytes plus the sum of every activeOwnerBytes; global combined adds every
// resolved Imported owner total and the Unknown archive exactly once. All arithmetic is checked;
// negative input or overflow is Invalid and never wraps.
using System;

namespace PawnDiary
{
    /// <summary>One snapshot of the four §T17.5 measured units.</summary>
    internal struct MemoryPayloadBudgetTotals
    {
        public long globalActiveBytes;
        public long globalImportedBytes;

        /// <summary>Checked combined total; -1 signals overflow (callers treat as invalid).</summary>
        public long GlobalCombined()
        {
            try
            {
                return checked(globalActiveBytes + globalImportedBytes);
            }
            catch (OverflowException)
            {
                return -1;
            }
        }
    }

    /// <summary>Configured production/defensive cap values (XML-normalized upstream).</summary>
    internal struct MemoryBudgetLimits
    {
        public long activeOwnerBytes;
        public long combinedOwnerBytes;
        public long activeGlobalBytes;
        public long combinedGlobalBytes;
    }

    internal enum MemoryBudgetOutcome
    {
        Admitted = 0,
        InvalidInput = 1,
        OwnerActiveFull = 2,
        OwnerCombinedFull = 3,
        GlobalActiveFull = 4,
        GlobalCombinedFull = 5
    }

    /// <summary>The result of one planned admission; commit happens only when admitted.</summary>
    internal struct MemoryBudgetDecision
    {
        public MemoryBudgetOutcome outcome;
        public MemoryPayloadBudgetTotals newTotals;
        public long newOwnerActiveBytes;
        public long newOwnerImportedBytes;

        public string OutcomeToken()
        {
            switch (outcome)
            {
                case MemoryBudgetOutcome.Admitted: return "admitted";
                case MemoryBudgetOutcome.InvalidInput: return "invalid";
                case MemoryBudgetOutcome.OwnerActiveFull: return "owner_active_full";
                case MemoryBudgetOutcome.OwnerCombinedFull: return "owner_combined_full";
                case MemoryBudgetOutcome.GlobalActiveFull: return "global_active_full";
                case MemoryBudgetOutcome.GlobalCombinedFull: return "global_combined_full";
                default: return "invalid";
            }
        }
    }

    internal static class ActiveMemoryPayloadBudget
    {
        public const string SchemaToken = "memory-active-payload-budget-v1";

        /// <summary>
        /// Plans one owner-scoped admission delta against the owner and global caps. Pure and
        /// side-effect-free: callers commit their swap only on <see cref="MemoryBudgetOutcome.Admitted"/>.
        /// </summary>
        public static MemoryBudgetDecision TryAdmit(
            MemoryBudgetLimits limits,
            long ownerActiveBytesCurrent,
            long ownerImportedBytesCurrent,
            long ownerDeltaActive,
            long ownerDeltaImported,
            MemoryPayloadBudgetTotals globalCurrent)
        {
            MemoryBudgetDecision refused = new MemoryBudgetDecision
            {
                outcome = MemoryBudgetOutcome.InvalidInput
            };
            if (limits.activeOwnerBytes <= 0 || limits.combinedOwnerBytes <= 0
                || limits.activeGlobalBytes <= 0 || limits.combinedGlobalBytes <= 0)
            {
                return refused;
            }

            if (!IsNonNegative(ownerActiveBytesCurrent) || !IsNonNegative(ownerImportedBytesCurrent)
                || !IsNonNegative(globalCurrent.globalActiveBytes)
                || !IsNonNegative(globalCurrent.globalImportedBytes))
            {
                return refused;
            }

            // Deltas may be negative (removals/expiry); overflow-checked adds everywhere.
            if (!CheckedAdd(ownerActiveBytesCurrent, ownerDeltaActive, out long ownerActiveNew)
                || !CheckedAdd(ownerImportedBytesCurrent, ownerDeltaImported, out long ownerImportedNew)
                || !CheckedAdd(globalCurrent.globalActiveBytes, ownerDeltaActive, out long globalActiveNew)
                || !CheckedAdd(
                    globalCurrent.globalImportedBytes, ownerDeltaImported, out long globalImportedNew))
            {
                return refused;
            }

            // A removal larger than the current totals would produce negative bytes — corrupt
            // input, not a shrink (§T17.5 totals are nonnegative). Refuse before any cap check.
            if (ownerActiveNew < 0 || ownerImportedNew < 0
                || globalActiveNew < 0 || globalImportedNew < 0)
            {
                return refused;
            }

            if (ownerActiveNew > limits.activeOwnerBytes)
            {
                return Full(refused, MemoryBudgetOutcome.OwnerActiveFull);
            }

            if (!CheckedAdd(ownerActiveNew, ownerImportedNew, out long ownerCombinedNew)
                || ownerCombinedNew > limits.combinedOwnerBytes)
            {
                return Full(refused, MemoryBudgetOutcome.OwnerCombinedFull);
            }

            if (globalActiveNew > limits.activeGlobalBytes)
            {
                return Full(refused, MemoryBudgetOutcome.GlobalActiveFull);
            }

            if (!CheckedAdd(globalActiveNew, globalImportedNew, out long globalCombinedNew)
                || globalCombinedNew > limits.combinedGlobalBytes)
            {
                return Full(refused, MemoryBudgetOutcome.GlobalCombinedFull);
            }

            return new MemoryBudgetDecision
            {
                outcome = MemoryBudgetOutcome.Admitted,
                newTotals = new MemoryPayloadBudgetTotals
                {
                    globalActiveBytes = globalActiveNew,
                    globalImportedBytes = globalImportedNew
                },
                newOwnerActiveBytes = ownerActiveNew,
                newOwnerImportedBytes = ownerImportedNew
            };
        }

        private static MemoryBudgetDecision Full(
            MemoryBudgetDecision decision, MemoryBudgetOutcome outcome)
        {
            decision.outcome = outcome;
            return decision;
        }

        private static bool IsNonNegative(long value)
        {
            return value >= 0;
        }

        private static bool CheckedAdd(long left, long right, out long total)
        {
            try
            {
                total = checked(left + right);
                return true;
            }
            catch (OverflowException)
            {
                total = -1;
                return false;
            }
        }
    }
}
