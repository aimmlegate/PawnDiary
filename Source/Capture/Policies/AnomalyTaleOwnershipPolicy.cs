// Pure, fail-open ownership for the generic StudiedEntity Tale. A dedicated Anomaly page may claim
// only its exact researcher/entity pair. The exact vanilla job identity remains authoritative for
// slow work; the short tick window is the fallback when either callback lacks that identity.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Compares one bounded dedicated-page claim with one generic Tale.</summary>
    internal static class AnomalyTaleOwnershipPolicy
    {
        /// <summary>Returns a consume-once decision without mutating the supplied DTOs.</summary>
        public static AnomalyTaleOwnershipDecision Decide(
            AnomalyStudyTaleClaim claim,
            AnomalyStudiedTaleFacts tale,
            int maximumAgeTicks)
        {
            AnomalyTaleOwnershipDecision result = new AnomalyTaleOwnershipDecision
            {
                nextClaim = Clone(claim)
            };
            if (!ValidClaim(claim) || claim.consumed)
            {
                result.removeClaim = true;
                return result;
            }

            // Zero is a valid same-tick-only window; only malformed negative input uses fallback.
            int expiry = maximumAgeTicks < 0
                ? AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks
                : maximumAgeTicks;
            if (tale == null || tale.tick < 0)
            {
                return result;
            }

            long age = (long)tale.tick - claim.acceptedTick;
            string claimedJobId = (claim.studyJobId ?? string.Empty).Trim();
            string taleJobId = (tale.studyJobId ?? string.Empty).Trim();
            bool bothJobsKnown = claimedJobId.Length > 0 && taleJobId.Length > 0;
            bool sameJob = bothJobsKnown && Same(claimedJobId, taleJobId);
            if (age > expiry && !sameJob)
            {
                result.removeClaim = true;
                return result;
            }
            if (age < 0 || !Same((claim.studierPawnId ?? string.Empty).Trim(), tale.studierPawnId))
            {
                return result;
            }

            // When both callbacks know their vanilla job, a different job is never allowed to borrow
            // ownership merely because the same researcher studied the same entity again quickly.
            if (bothJobsKnown && !sameJob)
            {
                return result;
            }

            string claimedEntityId = (claim.studiedEntityId ?? string.Empty).Trim();
            string taleEntityId = (tale.studiedEntityId ?? string.Empty).Trim();
            bool entityMatches;
            if (claimedEntityId.Length > 0 || taleEntityId.Length > 0)
            {
                // A stable ID on only one side is incomplete correlation, not permission to weaken
                // ownership to a defName shared by many entities. Preserve the generic Tale.
                entityMatches = claimedEntityId.Length > 0 && taleEntityId.Length > 0
                    && Same(claimedEntityId, taleEntityId);
            }
            else
            {
                entityMatches = Same(
                    (claim.studiedDefName ?? string.Empty).Trim(), tale.studiedDefName);
            }
            if (!entityMatches)
            {
                return result;
            }

            result.suppress = true;
            result.removeClaim = true;
            result.nextClaim.consumed = true;
            return result;
        }

        private static bool ValidClaim(AnomalyStudyTaleClaim claim)
        {
            return claim != null && claim.acceptedTick >= 0
                && !string.IsNullOrWhiteSpace(claim.studierPawnId)
                && (!string.IsNullOrWhiteSpace(claim.studiedEntityId)
                    || !string.IsNullOrWhiteSpace(claim.studiedDefName));
        }

        private static bool Same(string expected, string actual)
        {
            return expected.Length > 0
                && string.Equals(expected, (actual ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        private static AnomalyStudyTaleClaim Clone(AnomalyStudyTaleClaim source)
        {
            if (source == null)
            {
                return null;
            }

            return new AnomalyStudyTaleClaim
            {
                studierPawnId = source.studierPawnId ?? string.Empty,
                studiedEntityId = source.studiedEntityId ?? string.Empty,
                studiedDefName = source.studiedDefName ?? string.Empty,
                studyJobId = source.studyJobId ?? string.Empty,
                acceptedTick = source.acceptedTick,
                consumed = source.consumed
            };
        }
    }
}
