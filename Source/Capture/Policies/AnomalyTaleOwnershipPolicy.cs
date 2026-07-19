// Pure, fail-open ownership for the generic StudiedEntity Tale. A dedicated Anomaly page may claim
// only its exact researcher/entity pair for a short window; mismatches always preserve fallback.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Compares one short-lived dedicated-page claim with one generic Tale.</summary>
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
            int expiry = maximumAgeTicks < 0 ? 2500 : maximumAgeTicks;
            if (tale == null || tale.tick < 0)
            {
                return result;
            }

            long age = (long)tale.tick - claim.acceptedTick;
            if (age > expiry)
            {
                result.removeClaim = true;
                return result;
            }
            if (age < 0 || !Same((claim.studierPawnId ?? string.Empty).Trim(), tale.studierPawnId))
            {
                return result;
            }

            string claimedEntityId = (claim.studiedEntityId ?? string.Empty).Trim();
            bool entityMatches = claimedEntityId.Length > 0
                ? Same(claimedEntityId, tale.studiedEntityId)
                : Same((claim.studiedDefName ?? string.Empty).Trim(), tale.studiedDefName);
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
                acceptedTick = source.acceptedTick,
                consumed = source.consumed
            };
        }
    }
}
