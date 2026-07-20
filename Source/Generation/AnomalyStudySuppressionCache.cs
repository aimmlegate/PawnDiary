// Bounded, unsaved ownership for a dedicated Anomaly study page and its exact generic
// StudiedEntity Tale. A1.2 registers only after page creation and lets TaleSignal consume a matching
// claim once. Every entry is a detached pawn/entity/job ID plus tick, never a Pawn/Thing/Job/Def.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;

namespace PawnDiary
{
    /// <summary>Consume-once exact study claims, cleared at every game/load boundary.</summary>
    internal static class AnomalyStudySuppressionCache
    {
        private const int DefaultMaximumClaims = 64;
        private const int HardMaximumClaims = 256;
        private static readonly List<AnomalyStudyTaleClaim> Claims =
            new List<AnomalyStudyTaleClaim>();

        /// <summary>Adds one detached valid claim after pruning expiry and duplicate identity.</summary>
        internal static bool Register(
            AnomalyStudyTaleClaim claim,
            int nowTick,
            int maximumAgeTicks,
            int maximumClaims = DefaultMaximumClaims)
        {
            if (!Valid(claim) || nowTick < claim.acceptedTick) return false;
            int age = NonNegativeWindow(maximumAgeTicks);
            Prune(nowTick, age);
            string identity = Identity(claim);
            for (int i = Claims.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Identity(Claims[i]), identity, StringComparison.Ordinal))
                    Claims.RemoveAt(i);
            }

            Claims.Add(Clone(claim));
            int cap = maximumClaims < 1 || maximumClaims > HardMaximumClaims
                ? DefaultMaximumClaims
                : maximumClaims;
            if (Claims.Count > cap) Claims.RemoveRange(0, Claims.Count - cap);
            return true;
        }

        /// <summary>
        /// Applies exact pure ownership to the pending rows. Mismatches remain available for their own
        /// later Tale; expired/malformed rows are removed without suppressing the supplied fallback.
        /// </summary>
        internal static bool TryConsume(
            AnomalyStudiedTaleFacts tale,
            int maximumAgeTicks)
        {
            if (tale == null || tale.tick < 0) return false;
            int age = NonNegativeWindow(maximumAgeTicks);
            for (int i = Claims.Count - 1; i >= 0; i--)
            {
                AnomalyTaleOwnershipDecision decision =
                    AnomalyTaleOwnershipPolicy.Decide(Claims[i], tale, age);
                if (decision.removeClaim) Claims.RemoveAt(i);
                else Claims[i] = Clone(decision.nextClaim);
                if (decision.suppress) return true;
            }

            return false;
        }

        /// <summary>Clears every claim so stable IDs cannot leak into another colony.</summary>
        internal static void Clear()
        {
            Claims.Clear();
        }

        internal static int CountForTests => Claims.Count;

        /// <summary>Detached test seam so an in-game fixture can preserve a player's transient rows.</summary>
        internal static List<AnomalyStudyTaleClaim> SnapshotForTests()
        {
            List<AnomalyStudyTaleClaim> result = new List<AnomalyStudyTaleClaim>();
            for (int i = 0; i < Claims.Count; i++) result.Add(Clone(Claims[i]));
            return result;
        }

        /// <summary>Restores only valid detached rows after an isolated in-game fixture.</summary>
        internal static void RestoreForTests(List<AnomalyStudyTaleClaim> snapshot)
        {
            Claims.Clear();
            if (snapshot == null) return;
            for (int i = 0; i < snapshot.Count && Claims.Count < HardMaximumClaims; i++)
            {
                if (Valid(snapshot[i])) Claims.Add(Clone(snapshot[i]));
            }
        }

        private static void Prune(int nowTick, int maximumAgeTicks)
        {
            for (int i = Claims.Count - 1; i >= 0; i--)
            {
                AnomalyStudyTaleClaim claim = Claims[i];
                long age = claim == null ? long.MaxValue : (long)nowTick - claim.acceptedTick;
                // A known vanilla job is stronger correlation than elapsed ticks. Keep those rows
                // until the exact job's Tale consumes them, the bounded cache evicts them, or colony
                // lifecycle reset clears them. Job-less compatibility claims still use tick expiry.
                bool hasExactJob = claim != null && !string.IsNullOrWhiteSpace(claim.studyJobId);
                if (!Valid(claim) || age < 0 || (age > maximumAgeTicks && !hasExactJob))
                    Claims.RemoveAt(i);
            }
        }

        private static bool Valid(AnomalyStudyTaleClaim claim)
        {
            return claim != null && !claim.consumed && claim.acceptedTick >= 0
                && !string.IsNullOrWhiteSpace(claim.studierPawnId)
                && (!string.IsNullOrWhiteSpace(claim.studiedEntityId)
                    || !string.IsNullOrWhiteSpace(claim.studiedDefName));
        }

        private static int NonNegativeWindow(int value)
        {
            return value < 0 ? AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks : value;
        }

        private static string Identity(AnomalyStudyTaleClaim claim)
        {
            return (claim?.studierPawnId ?? string.Empty).Trim() + "|"
                + (claim?.studiedEntityId ?? string.Empty).Trim() + "|"
                + (claim?.studiedDefName ?? string.Empty).Trim() + "|"
                + (claim?.studyJobId ?? string.Empty).Trim();
        }

        private static AnomalyStudyTaleClaim Clone(AnomalyStudyTaleClaim source)
        {
            if (source == null) return null;
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

    /// <summary>One reset owner for all current and future process-static Anomaly correlation state.</summary>
    internal static class AnomalyTransientState
    {
        internal static void Reset()
        {
            AnomalyStudySuppressionCache.Clear();
        }
    }
}
