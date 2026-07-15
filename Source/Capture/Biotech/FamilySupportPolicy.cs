// Pure family-support selection for a growth moment. Candidates are built from exact saved parent
// relations and observed lesson/BabyPlay facts; proximity, generated prose, and the teaching page
// setting never create a role. Selection is deterministic across save/load and prompt previews.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Possible writer shapes for one canonical growth moment.</summary>
    internal enum GrowthWriterShape
    {
        Drop,
        ChildSolo,
        SupporterSolo,
        Pair
    }

    /// <summary>Selects at most one eligible parent/birth-parent/observed teacher.</summary>
    internal static class FamilySupportPolicy
    {
        /// <summary>
        /// Selects one supporter by relationship certainty, observed engagement, recency, map, and ID.
        /// Duplicate rows remain candidates so their best evidence wins regardless of input order.
        /// </summary>
        public static FamilySupportSelection Select(
            List<FamilySupportCandidate> candidates,
            BiotechPolicySnapshot policy)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            RankedCandidate best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                FamilySupportCandidate candidate = candidates[i];
                RankedCandidate ranked = Rank(candidate, policy);
                if (ranked == null)
                {
                    continue;
                }

                if (best == null || Compare(ranked, best) < 0)
                {
                    best = ranked;
                }
            }

            if (best == null)
            {
                return null;
            }

            return new FamilySupportSelection
            {
                adultId = best.candidate.adultId,
                displayName = best.candidate.displayName ?? string.Empty,
                roleToken = best.roleToken,
                observationBand = best.observationBand?.token ?? string.Empty,
                evidenceCount = best.evidenceCount
            };
        }

        /// <summary>Maps raw saved counts to a qualitative XML band; raw counts stay out of prompts.</summary>
        public static BiotechObservationBandRule ObservationBandFor(
            int evidenceCount,
            BiotechPolicySnapshot policy)
        {
            BiotechObservationBandRule best = null;
            List<BiotechObservationBandRule> rules = policy?.observationBands;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    BiotechObservationBandRule rule = rules[i];
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.token)
                        && rule.minimumEvidence > 0 && evidenceCount >= rule.minimumEvidence
                        && (best == null || rule.minimumEvidence > best.minimumEvidence))
                    {
                        best = rule;
                    }
                }
            }

            return best;
        }

        private static RankedCandidate Rank(FamilySupportCandidate candidate, BiotechPolicySnapshot policy)
        {
            if (candidate == null || !candidate.eligible || string.IsNullOrWhiteSpace(candidate.adultId))
            {
                return null;
            }

            int evidence = SafeSum(candidate.lessonCount, candidate.babyPlayCount, candidate.careCount);
            bool parent = candidate.relationToken == BiotechFamilyRoleTokens.Parent
                || candidate.relationToken == BiotechFamilyRoleTokens.BirthParent;
            int tier;
            string role;
            if (parent && evidence > 0)
            {
                tier = 0;
                role = candidate.relationToken;
            }
            else if (parent)
            {
                tier = 1;
                role = candidate.relationToken;
            }
            else if (evidence >= Math.Max(1, policy?.supporterMinimumEvidence ?? 2))
            {
                tier = 2;
                role = BiotechFamilyRoleTokens.Teacher;
            }
            else
            {
                return null;
            }

            return new RankedCandidate
            {
                candidate = candidate,
                roleToken = role,
                tier = tier,
                evidenceCount = evidence,
                observationBand = ObservationBandFor(evidence, policy)
            };
        }

        private static int Compare(RankedCandidate left, RankedCandidate right)
        {
            int value = left.tier.CompareTo(right.tier);
            if (value != 0) return value;

            int leftBand = left.observationBand?.minimumEvidence ?? 0;
            int rightBand = right.observationBand?.minimumEvidence ?? 0;
            value = rightBand.CompareTo(leftBand);
            if (value != 0) return value;

            value = right.candidate.lastObservedTick.CompareTo(left.candidate.lastObservedTick);
            if (value != 0) return value;

            value = right.candidate.sameMap.CompareTo(left.candidate.sameMap);
            if (value != 0) return value;

            return string.CompareOrdinal(left.candidate.adultId, right.candidate.adultId);
        }

        private static int SafeSum(int first, int second, int third)
        {
            long total = Math.Max(0, first) + (long)Math.Max(0, second) + Math.Max(0, third);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private sealed class RankedCandidate
        {
            public FamilySupportCandidate candidate;
            public string roleToken;
            public int tier;
            public int evidenceCount;
            public BiotechObservationBandRule observationBand;
        }
    }

    /// <summary>Chooses child solo, supporter solo, pair, or no page from already-proven eligibility.</summary>
    internal static class GrowthWriterPolicy
    {
        /// <summary>Returns the truthful writer shape after independently checking both stable IDs.</summary>
        public static GrowthWriterShape Decide(
            string childId,
            bool childEligible,
            FamilySupportSelection supporter)
        {
            bool hasChild = childEligible && !string.IsNullOrWhiteSpace(childId);
            bool hasSupporter = supporter != null && !string.IsNullOrWhiteSpace(supporter.adultId)
                && (!hasChild || !string.Equals(childId, supporter.adultId, StringComparison.Ordinal));
            if (hasChild && hasSupporter) return GrowthWriterShape.Pair;
            if (hasChild) return GrowthWriterShape.ChildSolo;
            if (hasSupporter) return GrowthWriterShape.SupporterSolo;
            return GrowthWriterShape.Drop;
        }
    }
}
