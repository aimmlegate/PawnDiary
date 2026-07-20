// Pure containment-breach aggregation and writer selection. The later Harmony adapter owns call
// scoping and before/after verification; this file only accepts already-captured primitive facts.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Builds one bounded breach event from one outer escape call tree.</summary>
    internal static class ContainmentBreachPolicy
    {
        /// <summary>Filters verified escapes, de-duplicates identities, and ranks writers.</summary>
        public static ContainmentBreachPlan Plan(
            ContainmentEscapeFacts facts,
            AnomalyPolicySnapshot policy)
        {
            ContainmentBreachPlan plan = new ContainmentBreachPlan();
            if (!ValidScope(facts))
            {
                return plan;
            }

            policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            plan.dedupKey = DedupKey(facts);
            if (plan.dedupKey.Length == 0)
            {
                return plan;
            }

            List<ContainedEntityFact> escaped = VerifiedEscapes(facts.entities);
            if (escaped.Count == 0)
            {
                return plan;
            }

            plan.valid = true;
            plan.escapedCount = escaped.Count;
            plan.sameRoomCascade = facts.sameRoomCascade && escaped.Count > 1;
            plan.preEjectionSetting = facts.preEjectionSetting ?? string.Empty;
            int labelMaximum = NormalizeEntityLabelMaximum(policy.containmentMaxEntityLabelsInContext);
            for (int i = 0; i < escaped.Count && plan.contextEntities.Count < labelMaximum; i++)
            {
                plan.contextEntities.Add(Clone(escaped[i]));
            }
            plan.additionalEscapedCount = escaped.Count - plan.contextEntities.Count;

            if (!policy.containmentEnabled)
            {
                return plan;
            }

            int writerMaximum = NormalizeWriterMaximum(policy.containmentMaxWriters);
            int radius = NormalizeWitnessRadius(policy.containmentWitnessRadius);
            SelectWriters(facts.witnesses, radius, writerMaximum, plan.selectedWriters);
            plan.writePage = plan.selectedWriters.Count > 0;
            return plan;
        }

        /// <summary>Returns the stable outer-call identity used by later duplicate arbitration.</summary>
        public static string DedupKey(ContainmentEscapeFacts facts)
        {
            if (!ValidScope(facts))
            {
                return string.Empty;
            }

            string escapeId = facts.escapeId.Trim();
            if (escapeId.IndexOf('|') >= 0)
            {
                return string.Empty;
            }

            // Include the exact start tick as well as the adapter's outer-call ID. This keeps the
            // call-tree identity useful for later cross-source arbitration without letting a reused
            // or mod-supplied escape ID collapse a genuinely later breach on the same map.
            return "anomaly-breach|" + facts.mapId + "|" + facts.tick + "|" + escapeId;
        }

        /// <summary>Normalizes an XML writer cap to the supported one-or-two-author contract.</summary>
        public static int NormalizeWriterMaximum(int configured)
        {
            return configured < 1 || configured > AnomalyPolicyLimits.MaximumContainmentWriters
                ? AnomalyPolicyLimits.DefaultContainmentWriters
                : configured;
        }

        /// <summary>
        /// Returns at most <paramref name="maximum"/> relevant detached candidates in the same
        /// deterministic order used for final writer selection. The live adapter uses this before it
        /// retains Pawn references, so a defensive cap cannot discard a nearby or recent-studier
        /// candidate merely because a lower-priority colony pawn has an earlier load ID.
        /// </summary>
        public static List<AnomalyWriterCandidate> BoundWriterCandidates(
            List<AnomalyWriterCandidate> source,
            int radius,
            int maximum)
        {
            return BoundCandidates(source, radius, maximum, includeUnqualified: false);
        }

        /// <summary>
        /// Retains a bounded outer-call roster for nested platforms. Current-role candidates stay
        /// first, followed by otherwise-unqualified eligible pawns ordered by distance, so a pawn who
        /// is near only a later same-room escape is not lost on a non-home map.
        /// </summary>
        public static List<AnomalyWriterCandidate> BoundCandidatePool(
            List<AnomalyWriterCandidate> source,
            int radius,
            int maximum)
        {
            return BoundCandidates(source, radius, maximum, includeUnqualified: true);
        }

        private static List<AnomalyWriterCandidate> BoundCandidates(
            List<AnomalyWriterCandidate> source,
            int radius,
            int maximum,
            bool includeUnqualified)
        {
            List<AnomalyWriterCandidate> result = new List<AnomalyWriterCandidate>();
            if (maximum < 1 || source == null) return result;

            int cap = Math.Min(maximum, AnomalyPolicyLimits.MaximumContainmentCandidates);
            List<RankedCandidate> ranked =
                RankCandidates(source, radius, includeUnqualified);
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ranked.Count && result.Count < cap; i++)
            {
                AnomalyWriterCandidate candidate = ranked[i].source;
                string pawnId = candidate.pawnId.Trim();
                if (selectedIds.Add(pawnId)) result.Add(candidate);
            }

            return result;
        }

        private static bool ValidScope(ContainmentEscapeFacts facts)
        {
            return facts != null && facts.outerEscape && facts.tick >= 0 && facts.mapId >= 0
                && !string.IsNullOrWhiteSpace(facts.escapeId);
        }

        private static List<ContainedEntityFact> VerifiedEscapes(List<ContainedEntityFact> source)
        {
            List<ContainedEntityFact> result = new List<ContainedEntityFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                ContainedEntityFact entity = source[i];
                string id = entity == null ? string.Empty : (entity.entityId ?? string.Empty).Trim();
                if (entity == null || !entity.escaped || id.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                result.Add(entity);
                if (result.Count >= AnomalyPolicyLimits.MaximumContainmentEntities)
                {
                    break;
                }
            }

            return result;
        }

        private static void SelectWriters(
            List<AnomalyWriterCandidate> source,
            int radius,
            int maximum,
            List<AnomalyWriterSelection> destination)
        {
            List<RankedCandidate> ranked =
                RankCandidates(source, radius, includeUnqualified: false);
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ranked.Count && destination.Count < maximum; i++)
            {
                RankedCandidate candidate = ranked[i];
                string pawnId = candidate.source.pawnId.Trim();
                if (!selectedIds.Add(pawnId))
                {
                    continue;
                }

                destination.Add(new AnomalyWriterSelection
                {
                    pawnId = pawnId,
                    roleToken = candidate.roleToken
                });
            }
        }

        private static List<RankedCandidate> RankCandidates(
            List<AnomalyWriterCandidate> source,
            int radius,
            bool includeUnqualified)
        {
            List<RankedCandidate> ranked = new List<RankedCandidate>();
            if (source == null) return ranked;

            int normalizedRadius = NormalizeWitnessRadius(radius);
            long radiusSquared = (long)normalizedRadius * normalizedRadius;
            for (int i = 0; i < source.Count; i++)
            {
                AnomalyWriterCandidate candidate = source[i];
                if (candidate == null || !candidate.eligible || !candidate.onAffectedMap
                    || string.IsNullOrWhiteSpace(candidate.pawnId))
                {
                    continue;
                }

                int roleRank;
                string role;
                bool insideRadius = candidate.distanceSquared >= 0
                    && (long)candidate.distanceSquared <= radiusSquared;
                if (candidate.nearbyCandidate && insideRadius)
                {
                    roleRank = 0;
                    role = AnomalyWitnessRoleTokens.Nearby;
                }
                else if (candidate.recentExactStudier)
                {
                    roleRank = 1;
                    role = AnomalyWitnessRoleTokens.RecentStudier;
                }
                else if (candidate.freeColonistOnHomeMap)
                {
                    roleRank = 2;
                    role = AnomalyWitnessRoleTokens.ColonyWitness;
                }
                else if (includeUnqualified)
                {
                    roleRank = 3;
                    role = string.Empty;
                }
                else
                {
                    continue;
                }

                ranked.Add(new RankedCandidate
                {
                    source = candidate,
                    roleRank = roleRank,
                    roleToken = role
                });
            }

            ranked.Sort(Compare);
            return ranked;
        }

        private static int Compare(RankedCandidate left, RankedCandidate right)
        {
            int comparison = left.roleRank.CompareTo(right.roleRank);
            if (comparison != 0) return comparison;
            comparison = NormalizeDistance(left.source.distanceBucket)
                .CompareTo(NormalizeDistance(right.source.distanceBucket));
            if (comparison != 0) return comparison;
            comparison = NormalizeDistance(left.source.distanceSquared)
                .CompareTo(NormalizeDistance(right.source.distanceSquared));
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(TieBreak(left.source), TieBreak(right.source));
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.source.pawnId.Trim(), right.source.pawnId.Trim());
        }

        private static int NormalizeDistance(int value)
        {
            return value < 0 ? int.MaxValue : value;
        }

        private static string TieBreak(AnomalyWriterCandidate candidate)
        {
            string key = (candidate.tieBreakKey ?? string.Empty).Trim();
            return key.Length == 0 ? candidate.pawnId.Trim() : key;
        }

        private static int NormalizeWitnessRadius(int configured)
        {
            return configured < 1 || configured > AnomalyPolicyLimits.MaximumWitnessRadius
                ? AnomalyPolicyLimits.DefaultWitnessRadius
                : configured;
        }

        private static int NormalizeEntityLabelMaximum(int configured)
        {
            return configured < 1 || configured > AnomalyPolicyLimits.MaximumEntityLabels
                ? AnomalyPolicyLimits.DefaultEntityLabels
                : configured;
        }

        private static ContainedEntityFact Clone(ContainedEntityFact source)
        {
            return new ContainedEntityFact
            {
                entityId = (source.entityId ?? string.Empty).Trim(),
                visibleLabel = source.visibleLabel ?? string.Empty,
                defName = (source.defName ?? string.Empty).Trim(),
                mutantDefName = (source.mutantDefName ?? string.Empty).Trim(),
                platformId = (source.platformId ?? string.Empty).Trim(),
                mapId = source.mapId,
                platformX = source.platformX,
                platformZ = source.platformZ,
                escaped = source.escaped
            };
        }

        private sealed class RankedCandidate
        {
            public AnomalyWriterCandidate source;
            public int roleRank;
            public string roleToken;
        }
    }
}
