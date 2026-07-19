// Main-thread transient bridge for the exact Notify_PawnKilled inheritance boundary. It stores only
// detached DTOs, is bounded, and is reset on every game/load transition.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Tracks active death scopes and stages title callbacks until the outer commit resolves.</summary>
    internal static class RoyalSuccessionCorrelation
    {
        private static readonly List<RoyalSuccessionDeathScope> Active =
            new List<RoyalSuccessionDeathScope>();
        private static readonly List<RecentTitleClaim> RecentClaims =
            new List<RecentTitleClaim>();
        private static int serial;

        private sealed class RecentTitleClaim
        {
            public string pawnId = string.Empty;
            public string factionId = string.Empty;
            public string previousTitleDefName = string.Empty;
            public string newTitleDefName = string.Empty;
            public int mutationTick;
            public int expiresTick;
        }

        public static RoyalSuccessionDeathScope Open(string deceasedPawnId, int tick, int maximumActive)
        {
            string pawnId = CleanId(deceasedPawnId);
            int cap = maximumActive < 1 || maximumActive > 256 ? 64 : maximumActive;
            if (pawnId.Length == 0 || tick < 0 || Active.Count >= cap) return null;
            RoyalSuccessionDeathScope scope = new RoyalSuccessionDeathScope
            {
                scopeId = "succession|" + pawnId + "|" + tick + "|" + (++serial),
                deceasedPawnId = pawnId,
                openedTick = tick
            };
            Active.Add(scope);
            return scope;
        }

        public static bool AddCandidate(RoyalSuccessionCandidateSnapshot candidate, int maximumCandidates)
        {
            RoyalSuccessionDeathScope scope = Find(candidate?.deceasedPawnId);
            int cap = maximumCandidates < 1 || maximumCandidates > 256 ? 64 : maximumCandidates;
            if (scope == null || candidate == null || scope.candidates.Count >= cap) return false;
            candidate.correlationId = scope.scopeId + "|edge|" + scope.candidates.Count;
            scope.candidates.Add(candidate);
            return true;
        }

        public static bool StageTitle(RoyalTitleMutationSnapshot mutation, int maximumMutations)
        {
            if (mutation == null) return false;
            int cap = maximumMutations < 1 || maximumMutations > 256 ? 64 : maximumMutations;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                RoyalSuccessionDeathScope scope = Active[i];
                for (int j = scope.candidates.Count - 1; j >= 0; j--)
                {
                    RoyalSuccessionCandidateSnapshot candidate = scope.candidates[j];
                    if (!RoyalSuccessionPolicy.MatchesCandidateMutation(candidate, mutation)) continue;
                    for (int k = 0; k < scope.stagedTitleMutations.Count; k++)
                    {
                        RoyalSuccessionStagedTitleMutation existing = scope.stagedTitleMutations[k];
                        if (string.Equals(existing?.correlationId, candidate.correlationId,
                                StringComparison.Ordinal)
                            && RoyalSuccessionPolicy.SameMutation(existing?.mutation, mutation))
                            return true;
                    }
                    if (scope.stagedTitleMutations.Count >= cap) return false;
                    scope.stagedTitleMutations.Add(new RoyalSuccessionStagedTitleMutation
                    {
                        correlationId = candidate.correlationId,
                        mutation = mutation
                    });
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Remembers one already-owned physical title edge. The saved fact may advance or terminate
        /// after the title hook, while a surrounding bestowing callback reports the same edge later
        /// in the same game tick. This transient cache suppresses only that exact duplicate.
        /// </summary>
        public static void RememberClaim(
            RoyalTitleMutationSnapshot mutation,
            int now,
            int lifetimeTicks,
            int maximumClaims)
        {
            if (mutation?.newTitle == null) return;
            PruneRecent(now);
            int cap = maximumClaims < 1 || maximumClaims > 256 ? 64 : maximumClaims;
            int lifetime = lifetimeTicks > 0 ? lifetimeTicks : 2500;
            for (int i = 0; i < RecentClaims.Count; i++)
            {
                if (!SameRecent(RecentClaims[i], mutation)) continue;
                RecentClaims[i].expiresTick = SafeAdd(now, lifetime);
                return;
            }
            if (RecentClaims.Count >= cap) RecentClaims.RemoveAt(0);
            RecentClaims.Add(new RecentTitleClaim
            {
                pawnId = CleanId(mutation.pawnId),
                factionId = CleanId(mutation.factionId),
                previousTitleDefName = CleanId(mutation.previousTitle?.titleDefName),
                newTitleDefName = CleanId(mutation.newTitle.titleDefName),
                mutationTick = mutation.tick,
                expiresTick = SafeAdd(now, lifetime)
            });
        }

        /// <summary>Returns true only for a second report of the exact already-owned same-tick edge.</summary>
        public static bool WasClaimedRecently(RoyalTitleMutationSnapshot mutation, int now)
        {
            if (mutation == null) return false;
            PruneRecent(now);
            for (int i = RecentClaims.Count - 1; i >= 0; i--)
                if (SameRecent(RecentClaims[i], mutation)) return true;
            return false;
        }

        public static RoyalSuccessionDeathScope Close(RoyalSuccessionDeathScope scope)
        {
            if (scope == null || scope.completed || !Active.Remove(scope)) return null;
            scope.completed = true;
            return scope;
        }

        public static void Cancel(RoyalSuccessionDeathScope scope)
        {
            if (scope != null) Active.Remove(scope);
        }

        public static void Clear()
        {
            Active.Clear();
            RecentClaims.Clear();
            serial = 0;
        }

        public static int ActiveCountForTests => Active.Count;
        public static int RecentClaimCountForTests => RecentClaims.Count;

        private static RoyalSuccessionDeathScope Find(string deceasedPawnId)
        {
            string pawnId = CleanId(deceasedPawnId);
            for (int i = Active.Count - 1; i >= 0; i--)
                if (string.Equals(Active[i].deceasedPawnId, pawnId, StringComparison.Ordinal)) return Active[i];
            return null;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }

        private static bool SameRecent(RecentTitleClaim claim, RoyalTitleMutationSnapshot mutation)
        {
            return claim != null && mutation != null
                && claim.mutationTick == mutation.tick
                && string.Equals(claim.pawnId, CleanId(mutation.pawnId), StringComparison.Ordinal)
                && string.Equals(claim.factionId, CleanId(mutation.factionId), StringComparison.Ordinal)
                && string.Equals(claim.previousTitleDefName,
                    CleanId(mutation.previousTitle?.titleDefName), StringComparison.Ordinal)
                && string.Equals(claim.newTitleDefName,
                    CleanId(mutation.newTitle?.titleDefName), StringComparison.Ordinal);
        }

        private static void PruneRecent(int now)
        {
            for (int i = RecentClaims.Count - 1; i >= 0; i--)
                if (now > RecentClaims[i].expiresTick) RecentClaims.RemoveAt(i);
        }

        private static int SafeAdd(int value, int amount)
        {
            long result = (long)Math.Max(0, value) + Math.Max(1, amount);
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
