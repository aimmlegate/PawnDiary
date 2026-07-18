// Main-thread, resettable ownership bridge for Royalty title/psylink causes. Exact hook adapters
// open a detached scope, capture before/after facts, and briefly retain only plain DTOs while a richer
// ritual owner arrives. Nothing here is saved or holds a Pawn, Def, Thing, ritual, or settings object.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Bounds, matches, claims, and expires exact Royalty mutation batches.</summary>
    internal static class RoyalMutationCorrelation
    {
        private static readonly List<RoyalMutationBatchSnapshot> Active =
            new List<RoyalMutationBatchSnapshot>();
        private static readonly List<RoyalMutationBatchSnapshot> Pending =
            new List<RoyalMutationBatchSnapshot>();
        private static int nextCorrelationSerial;

        public static RoyalMutationBatchSnapshot Open(
            string pawnId,
            string pawnName,
            string factionId,
            string causeToken,
            int tick,
            RoyalTitleSnapshot previousTitle,
            int previousPsylinkLevel,
            int maximumActive)
        {
            string id = CleanId(pawnId);
            string cause = Clean(causeToken);
            int cap = Clamp(maximumActive, 1, 64, 16);
            if (id.Length == 0 || !RoyalMutationCauseTokens.IsKnown(cause)
                || cause == RoyalMutationCauseTokens.Unknown || tick < 0 || Active.Count >= cap)
                return null;

            string correlationId = cause + "|" + id + "|" + tick + "|" + (++nextCorrelationSerial);
            RoyalMutationBatchSnapshot batch = new RoyalMutationBatchSnapshot
            {
                batchId = correlationId,
                pawnId = id,
                pawnName = SafeText(pawnName),
                causeToken = cause,
                openedTick = tick,
                scope = new RoyalMutationCauseScope
                {
                    causeToken = cause,
                    pawnId = id,
                    factionId = CleanId(factionId),
                    previousTitleDefName = previousTitle?.titleDefName ?? string.Empty,
                    previousPsylinkLevel = Math.Max(0, previousPsylinkLevel),
                    openedTick = tick,
                    correlationId = correlationId
                },
                titleMutation = CleanId(factionId).Length == 0 ? null : new RoyalTitleMutationSnapshot
                {
                    pawnId = id,
                    factionId = CleanId(factionId),
                    previousTitle = previousTitle,
                    causeToken = cause,
                    tick = tick,
                    correlationId = correlationId
                },
                psylinkMutation = new RoyalPsychicMutationSnapshot
                {
                    pawnId = id,
                    previousPsylinkLevel = Math.Max(0, previousPsylinkLevel),
                    newPsylinkLevel = Math.Max(0, previousPsylinkLevel),
                    causeToken = cause,
                    tick = tick,
                    correlationId = correlationId
                }
            };
            Active.Add(batch);
            return batch;
        }

        /// <summary>True only inside the exact bestowing boundary for this pawn and faction.</summary>
        public static bool HasRicherTitleOwner(
            string pawnId,
            string factionId,
            int tick,
            int correlationTicks)
        {
            string pawn = CleanId(pawnId);
            string faction = CleanId(factionId);
            int window = Math.Max(1, correlationTicks);
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                RoyalMutationBatchSnapshot batch = Active[i];
                RoyalMutationCauseScope scope = batch?.scope;
                if (scope != null
                    && scope.causeToken == RoyalMutationCauseTokens.ImperialBestowing
                    && string.Equals(scope.pawnId, pawn, StringComparison.Ordinal)
                    && string.Equals(scope.factionId, faction, StringComparison.Ordinal)
                    && tick >= scope.openedTick && (long)tick - scope.openedTick <= window)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Closes an active scope. Ritual causes enter the bounded pending queue; a caller handles a
        /// false result as an immediate truthful progression fallback.
        /// </summary>
        public static bool Complete(
            RoyalMutationBatchSnapshot batch,
            RoyalTitleMutationSnapshot title,
            RoyalPsychicMutationSnapshot psylink,
            int maximumPending)
        {
            if (batch == null || !Active.Remove(batch)) return false;
            batch.titleMutation = Meaningful(title) ? title : null;
            batch.psylinkMutation = Meaningful(psylink) ? psylink : null;
            if (batch.titleMutation == null && batch.psylinkMutation == null) return true;

            if (batch.scope != null)
            {
                batch.scope.newTitleDefName = batch.titleMutation?.newTitle?.titleDefName ?? string.Empty;
                batch.scope.newPsylinkLevel = batch.psylinkMutation?.newPsylinkLevel
                    ?? batch.scope.previousPsylinkLevel;
            }

            bool ritualCause = batch.causeToken == RoyalMutationCauseTokens.ImperialBestowing
                || batch.causeToken == RoyalMutationCauseTokens.AnimaLinking;
            if (!ritualCause) return true;
            int cap = Clamp(maximumPending, 1, 256, 64);
            if (Pending.Count >= cap) return false;
            Pending.Add(batch);
            return true;
        }

        public static void Cancel(RoyalMutationBatchSnapshot batch)
        {
            if (batch == null) return;
            Active.Remove(batch);
        }

        /// <summary>Finds, but does not yet claim, the exact mutation batch for a completed ritual.</summary>
        public static RoyalMutationBatchSnapshot PrepareRitualOwner(
            string expectedCauseToken,
            IList<string> candidatePawnIds,
            int nowTick,
            RoyaltyPolicySnapshot policy)
        {
            string cause = Clean(expectedCauseToken);
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if ((cause != RoyalMutationCauseTokens.ImperialBestowing
                    && cause != RoyalMutationCauseTokens.AnimaLinking)
                || candidatePawnIds == null) return null;

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                RoyalMutationBatchSnapshot batch = Pending[i];
                if (batch == null || batch.claimed || batch.causeToken != cause
                    || !Contains(candidatePawnIds, batch.pawnId)) continue;
                RoyalMutationBatchPlan plan = RoyalMutationOwnershipPolicy.Plan(
                    batch.MutationFacts(), batch.scope, nowTick, true, true,
                    batch.fallbackConsumed, effective);
                if (plan.ownerToken == RoyalMutationOwnerTokens.Ritual
                    && plan.mutations.Count > 0) return batch;
            }
            return null;
        }

        /// <summary>Claims only after a ritual child page is durably created.</summary>
        public static bool ClaimRitual(RoyalMutationBatchSnapshot batch)
        {
            if (batch == null || !Pending.Remove(batch)) return false;
            batch.claimed = true;
            return true;
        }

        /// <summary>
        /// Consumes one expired unclaimed batch for this exact pawn. Disabled output consumes truth
        /// without returning a page, so re-enabling Progression cannot replay it.
        /// </summary>
        public static RoyalMutationBatchSnapshot TakeExpiredFallback(
            string pawnId,
            int nowTick,
            bool outputEnabled,
            RoyaltyPolicySnapshot policy)
        {
            string pawn = CleanId(pawnId);
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            for (int i = 0; i < Pending.Count; i++)
            {
                RoyalMutationBatchSnapshot batch = Pending[i];
                if (batch == null || batch.claimed
                    || !string.Equals(batch.pawnId, pawn, StringComparison.Ordinal)) continue;
                RoyalMutationBatchPlan plan = RoyalMutationOwnershipPolicy.Plan(
                    batch.MutationFacts(), batch.scope, nowTick, false, outputEnabled,
                    batch.fallbackConsumed, effective);
                if (!plan.fallbackConsumed) continue;
                batch.fallbackConsumed = true;
                Pending.RemoveAt(i);
                return plan.shouldEmitFallbackPage ? batch : null;
            }
            return null;
        }

        public static int PendingCountForTests => Pending.Count;
        public static int ActiveCountForTests => Active.Count;

        public static void Clear()
        {
            Active.Clear();
            Pending.Clear();
            nextCorrelationSerial = 0;
        }

        private static bool Meaningful(RoyalTitleMutationSnapshot value)
        {
            RoyalMutationFact fact = RoyalMutationOwnershipPolicy.FromTitle(value);
            return fact != null && fact.previousValue != fact.newValue;
        }

        private static bool Meaningful(RoyalPsychicMutationSnapshot value)
        {
            return value != null && value.previousPsylinkLevel >= 0 && value.newPsylinkLevel >= 0
                && value.previousPsylinkLevel != value.newPsylinkLevel;
        }

        private static bool Contains(IList<string> values, string expected)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(CleanId(values[i]), expected, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }

        private static string SafeText(string value)
        {
            return (value ?? string.Empty).Trim().Replace(";", ",");
        }

        private static string CleanId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.IndexOf(';') >= 0 || cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
