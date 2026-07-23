// Observed-condition selection policies that do not read RimWorld state. The live component keeps
// every lifecycle row, then projects detached rows here to decide (a) which exclusive-family member
// may influence a prompt and (b) which bounded map witnesses may receive a transition page.
// Keeping both decisions pure makes ordering, caps, and old-save baselines testable without Verse.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Pure threshold matching for the detached world-tile pollution fraction.</summary>
    internal static class ObservedConditionPollutionPolicy
    {
        public static bool IsActive(float pollutionFraction, float minimumFraction, float maximumFraction)
        {
            if (float.IsNaN(pollutionFraction) || float.IsInfinity(pollutionFraction))
            {
                return false;
            }

            float pollution = Clamp01(pollutionFraction);
            float minimum = Clamp01(minimumFraction);
            bool belowMaximum = maximumFraction < 0f || pollution < Clamp01(maximumFraction);
            return pollution >= minimum && belowMaximum;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }

    /// <summary>Detached prompt-influence metadata for one active observed-condition row.</summary>
    internal sealed class ObservedConditionInfluenceRow
    {
        public int sourceIndex;
        public string conditionKey;
        public string exclusiveFamilyKey;
        public int severityRank;
        public int mapUniqueId = -1;
    }

    /// <summary>Keeps ordinary rows and only the highest active severity in each family/map.</summary>
    internal static class ObservedConditionExclusiveFamilyPolicy
    {
        public static List<int> SelectSourceIndexes(IList<ObservedConditionInfluenceRow> rows)
        {
            List<int> selected = new List<int>();
            Dictionary<string, ObservedConditionInfluenceRow> winnerByFamily =
                new Dictionary<string, ObservedConditionInfluenceRow>(StringComparer.OrdinalIgnoreCase);
            if (rows == null)
            {
                return selected;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                ObservedConditionInfluenceRow row = rows[i];
                if (row == null)
                {
                    continue;
                }

                string family = (row.exclusiveFamilyKey ?? string.Empty).Trim();
                if (family.Length == 0)
                {
                    selected.Add(row.sourceIndex);
                    continue;
                }

                string familyMap = family + "|" + row.mapUniqueId;
                ObservedConditionInfluenceRow winner;
                if (!winnerByFamily.TryGetValue(familyMap, out winner)
                    || IsStronger(row, winner))
                {
                    winnerByFamily[familyMap] = row;
                }
            }

            foreach (KeyValuePair<string, ObservedConditionInfluenceRow> pair in winnerByFamily)
            {
                selected.Add(pair.Value.sourceIndex);
            }

            selected.Sort();
            return selected;
        }

        private static bool IsStronger(ObservedConditionInfluenceRow candidate,
            ObservedConditionInfluenceRow current)
        {
            if (candidate.severityRank != current.severityRank)
            {
                return candidate.severityRank > current.severityRank;
            }

            int byKey = string.Compare(candidate.conditionKey ?? string.Empty,
                current.conditionKey ?? string.Empty, StringComparison.Ordinal);
            return byKey < 0 || byKey == 0 && candidate.sourceIndex < current.sourceIndex;
        }
    }

    /// <summary>Detached candidate for one bounded observed-condition transition page.</summary>
    internal sealed class ObservedConditionWitnessCandidate
    {
        public string pawnId;
        public bool hasVisibleRelevantHealth;
    }

    /// <summary>
    /// Selects a stable capped subset. A zero cap deliberately preserves the caller's complete order,
    /// matching every pre-pollution observed-condition Def.
    /// </summary>
    internal static class ObservedConditionWitnessPolicy
    {
        public static List<string> SelectPawnIds(string conditionKey, int mapUniqueId, int transitionTick,
            IList<ObservedConditionWitnessCandidate> candidates, int maxPagePawns)
        {
            List<RankedWitness> ranked = new List<RankedWitness>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (candidates == null)
            {
                return new List<string>();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                ObservedConditionWitnessCandidate candidate = candidates[i];
                string pawnId = (candidate?.pawnId ?? string.Empty).Trim();
                if (pawnId.Length == 0 || !seen.Add(pawnId))
                {
                    continue;
                }

                ranked.Add(new RankedWitness
                {
                    pawnId = pawnId,
                    relevant = candidate.hasVisibleRelevantHealth,
                    inputIndex = i,
                    hash = StableHash(conditionKey, mapUniqueId, transitionTick, pawnId)
                });
            }

            if (maxPagePawns <= 0)
            {
                List<string> uncapped = new List<string>();
                for (int i = 0; i < ranked.Count; i++)
                {
                    uncapped.Add(ranked[i].pawnId);
                }

                return uncapped;
            }

            ranked.Sort(delegate(RankedWitness left, RankedWitness right)
            {
                if (left.relevant != right.relevant)
                {
                    return left.relevant ? -1 : 1;
                }

                int byHash = left.hash.CompareTo(right.hash);
                if (byHash != 0)
                {
                    return byHash;
                }

                int byId = string.Compare(left.pawnId, right.pawnId, StringComparison.Ordinal);
                return byId != 0 ? byId : left.inputIndex.CompareTo(right.inputIndex);
            });

            int count = Math.Min(maxPagePawns, ranked.Count);
            List<string> selected = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                selected.Add(ranked[i].pawnId);
            }

            return selected;
        }

        private static ulong StableHash(string conditionKey, int mapUniqueId, int transitionTick,
            string pawnId)
        {
            string value = (conditionKey ?? string.Empty) + "|" + mapUniqueId + "|"
                + transitionTick + "|" + (pawnId ?? string.Empty);
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }

        private sealed class RankedWitness
        {
            public string pawnId;
            public bool relevant;
            public int inputIndex;
            public ulong hash;
        }
    }

    /// <summary>
    /// Reconciles a one-time old-save baseline without rewriting rows already owned by the lifecycle.
    /// </summary>
    internal static class ObservedConditionBaselinePolicy
    {
        /// <summary>
        /// Keeps every distinct saved row exactly as it was, then silently starts only live identities
        /// that the old save did not know about. Preserving saved ticks and recorded flags is essential:
        /// replacing those rows on every load would restart decay and erase pending start/end debounce.
        /// </summary>
        public static List<ObservedConditionStateSnapshot> MergeStartedRows(int now,
            IList<ObservedConditionStateSnapshot> savedRows,
            IList<ObservedConditionObservation> observations)
        {
            List<ObservedConditionStateSnapshot> rows = new List<ObservedConditionStateSnapshot>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (savedRows != null)
            {
                for (int i = 0; i < savedRows.Count; i++)
                {
                    ObservedConditionStateSnapshot saved = savedRows[i];
                    if (saved == null || string.IsNullOrWhiteSpace(saved.conditionKey))
                    {
                        continue;
                    }

                    if (seen.Add(saved.IdentityKey()))
                    {
                        rows.Add(saved.Clone());
                    }
                }
            }

            List<ObservedConditionStateSnapshot> baseline = BuildStartedRows(now, observations);
            for (int i = 0; i < baseline.Count; i++)
            {
                ObservedConditionStateSnapshot row = baseline[i];
                if (row != null && seen.Add(row.IdentityKey()))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>Builds silently-started rows from live observations at an old-save boundary.</summary>
        public static List<ObservedConditionStateSnapshot> BuildStartedRows(int now,
            IList<ObservedConditionObservation> observations)
        {
            List<ObservedConditionStateSnapshot> rows = new List<ObservedConditionStateSnapshot>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (observations == null)
            {
                return rows;
            }

            for (int i = 0; i < observations.Count; i++)
            {
                ObservedConditionObservation observation = observations[i];
                if (observation == null || string.IsNullOrWhiteSpace(observation.conditionKey))
                {
                    continue;
                }

                string identity = ObservedConditionStateSnapshot.Identity(
                    observation.conditionKey, observation.scope, observation.mapUniqueId,
                    observation.subjectPawnId);
                if (!seen.Add(identity))
                {
                    continue;
                }

                rows.Add(new ObservedConditionStateSnapshot
                {
                    conditionDefName = observation.conditionDefName ?? string.Empty,
                    conditionKey = observation.conditionKey,
                    scope = observation.scope,
                    mapUniqueId = observation.mapUniqueId,
                    subjectPawnId = observation.subjectPawnId ?? string.Empty,
                    firstObservedTick = Math.Max(0, now),
                    lastObservedTick = Math.Max(0, now),
                    firstMissingTick = -1,
                    lastSeenEvidenceDefName = observation.evidenceDefName ?? string.Empty,
                    lastSeenEvidenceLabel = observation.evidenceLabel ?? string.Empty,
                    lastSeenEvidenceCount = Math.Max(0, observation.evidenceCount),
                    startRecorded = true,
                    endRecorded = false
                });
            }

            return rows;
        }
    }
}
