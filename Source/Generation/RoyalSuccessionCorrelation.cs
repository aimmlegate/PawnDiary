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
        private static int serial;

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
                    if (scope.stagedTitleMutations.Count >= cap) return false;
                    if (!string.Equals(candidate.heirPawnId, mutation.pawnId, StringComparison.Ordinal)
                        || !string.Equals(candidate.factionId, mutation.factionId, StringComparison.Ordinal)
                        || !string.Equals(candidate.inheritedTitleDefName,
                            mutation.newTitle?.titleDefName, StringComparison.Ordinal)) continue;
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
            serial = 0;
        }

        public static int ActiveCountForTests => Active.Count;

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
    }
}
