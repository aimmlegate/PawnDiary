// MemoryBrainwipeHeadroomPolicy.cs — pure union-directory displacement for mandatory epoch fences.
//
// A Brainwipe may never evict autobiography. When the shared active+fence directory is full, the
// sole cross-owner exception is replacing one completely empty epoch-only metadata row.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    internal sealed class MemoryBrainwipeFenceCandidate
    {
        public string ownerPawnId = string.Empty;
        public bool currentSchema;
        public bool archiveOnly;
        public bool epochFenceOnly;
        public bool hasEpoch;
        public bool hasAutobiographicalPayload;
        public bool hasActiveRequestOrOpportunity;
        public bool hasPageOrNonMemoryState;
    }

    internal sealed class MemoryBrainwipeDirectoryPlan
    {
        public bool valid;
        public bool requiresDisplacement;
        public string displacedOwnerPawnId = string.Empty;
    }

    internal static class MemoryBrainwipeHeadroomPolicy
    {
        /// <summary>
        /// Derives the catalog-owned worst-case empty-fence transaction reserve. The terms mirror
        /// the registered logical-size framing for a maximum owner/epoch identity, allocator-chain
        /// delta, and one permanently reserved coalescing diagnostic row.
        /// </summary>
        public static bool TryComputeMetadataReserveBytes(
            long rawIdentityUnits,
            long completeCompositeKeyUnits,
            long diagnosticTextUnits,
            out long reserveBytes)
        {
            reserveBytes = 0;
            if (rawIdentityUnits < 0 || completeCompositeKeyUnits < 0
                || diagnosticTextUnits < 0) return false;
            try
            {
                const long rowFraming = 64;
                long ownerAndEpochStrings = checked(
                    4 + 3 * rawIdentityUnits + 4 + 3 * completeCompositeKeyUnits);
                const long ownerScalars = 4 + 2 + (5 * 8) + (7 * 4);
                long diagnostic = checked(
                    rowFraming + 4 + 4 + 3 * diagnosticTextUnits + 8);
                const long fallbackChain = 4 + 64;
                reserveBytes = checked(
                    rowFraming + ownerAndEpochStrings + ownerScalars
                    + diagnostic + fallbackChain);
                return reserveBytes > 0;
            }
            catch (OverflowException)
            {
                reserveBytes = 0;
                return false;
            }
        }

        /// <summary>
        /// True when a projected optional admission leaves enough active and combined global bytes
        /// for a worst-case Brainwipe, counting at most one already-reclaimable empty fence.
        /// </summary>
        public static bool RetainsMetadataReserve(
            long projectedGlobalActiveBytes,
            long projectedGlobalImportedBytes,
            long activeGlobalCap,
            long combinedGlobalCap,
            long reserveBytes,
            long reclaimableFenceBytes)
        {
            if (projectedGlobalActiveBytes < 0 || projectedGlobalImportedBytes < 0
                || activeGlobalCap <= 0 || combinedGlobalCap <= 0
                || reserveBytes < 0 || reclaimableFenceBytes < 0) return false;
            try
            {
                long combined = checked(
                    projectedGlobalActiveBytes + projectedGlobalImportedBytes);
                long activeAvailable = checked(
                    activeGlobalCap - projectedGlobalActiveBytes + reclaimableFenceBytes);
                long combinedAvailable = checked(
                    combinedGlobalCap - combined + reclaimableFenceBytes);
                return activeAvailable >= reserveBytes && combinedAvailable >= reserveBytes;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public static MemoryBrainwipeDirectoryPlan PlanDirectoryAdmission(
            string targetOwnerPawnId,
            bool targetAlreadyUsesUnionSlot,
            int unionOwnerCount,
            int epochFenceDirectoryCap,
            IEnumerable<MemoryBrainwipeFenceCandidate> candidates)
        {
            MemoryBrainwipeDirectoryPlan plan = new MemoryBrainwipeDirectoryPlan();
            if (string.IsNullOrWhiteSpace(targetOwnerPawnId) || unionOwnerCount < 0
                || epochFenceDirectoryCap <= 0) return plan;
            plan.valid = true;
            if (targetAlreadyUsesUnionSlot || unionOwnerCount < epochFenceDirectoryCap) return plan;
            plan.requiresDisplacement = true;
            string selected = string.Empty;
            foreach (MemoryBrainwipeFenceCandidate candidate
                in candidates ?? new MemoryBrainwipeFenceCandidate[0])
            {
                if (!Eligible(candidate)
                    || string.Equals(candidate.ownerPawnId, targetOwnerPawnId,
                        StringComparison.Ordinal)) continue;
                if (selected.Length == 0 || string.CompareOrdinal(
                        candidate.ownerPawnId, selected) < 0)
                    selected = candidate.ownerPawnId;
            }
            plan.displacedOwnerPawnId = selected;
            return plan;
        }

        public static bool Eligible(MemoryBrainwipeFenceCandidate candidate)
        {
            return candidate != null
                && candidate.currentSchema
                && !candidate.archiveOnly
                && candidate.epochFenceOnly
                && candidate.hasEpoch
                && !candidate.hasAutobiographicalPayload
                && !candidate.hasActiveRequestOrOpportunity
                && !candidate.hasPageOrNonMemoryState
                && !string.IsNullOrWhiteSpace(candidate.ownerPawnId);
        }
    }
}
