// Pure Phase-A2.0 creepjoiner continuity and writer arbitration. Live tracker/Pawn/Map reads stay
// in DlcContext; this file accepts only detached visible facts and returns one terminal state mutation
// plus, when a truthful POV exists, one deterministic page plan.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Plans canonical arrival ownership and exact visible creepjoiner outcomes.</summary>
    internal static class CreepJoinerOutcomePolicy
    {
        /// <summary>True only for an exact semicolon-delimited <c>creepjoiner=true</c> field.</summary>
        public static bool IsCreepJoinerArrivalContext(string context)
        {
            if (string.IsNullOrWhiteSpace(context)) return false;
            string[] fields = context.Split(';');
            for (int i = 0; i < fields.Length; i++)
            {
                string field = (fields[i] ?? string.Empty).Trim();
                int equals = field.IndexOf('=');
                if (equals <= 0) continue;
                string key = field.Substring(0, equals).Trim();
                string value = field.Substring(equals + 1).Trim();
                if (string.Equals(key, "creepjoiner", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates or enriches the state-only joined row owned by the existing arrival route. A
        /// terminal outcome can gain a previously missing canonical arrival ID, but is never downgraded.
        /// </summary>
        public static CreepJoinerArcSnapshot UpsertArrival(
            CreepJoinerArcSnapshot existing,
            string pawnId,
            int joinedTick,
            string createdArrivalEventId)
        {
            string id = CleanStable(pawnId);
            if (id.Length == 0 || joinedTick < 0) return Clone(existing);

            CreepJoinerArcSnapshot result = Clone(existing) ?? new CreepJoinerArcSnapshot
            {
                pawnId = id,
                joinedTick = joinedTick,
                lastVisiblePhase = CreepJoinerPhaseTokens.Joined,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            if (!string.Equals(CleanStable(result.pawnId), id, StringComparison.Ordinal))
                return result;

            result.pawnId = id;
            // Tick zero is both a valid game-start join and the safe pre-join terminal sentinel. A
            // later observation must not rewrite either historical claim; only repair corrupt negatives.
            if (result.joinedTick < 0)
                result.joinedTick = joinedTick;
            string eventId = CleanStable(createdArrivalEventId);
            if (result.arrivalEventId.Length == 0 && eventId.Length > 0)
                result.arrivalEventId = eventId;
            if (!result.terminal && !CreepJoinerPhaseTokens.IsOutcome(result.lastVisiblePhase))
                result.lastVisiblePhase = CreepJoinerPhaseTokens.Joined;
            if (result.schemaVersion <= AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion)
                result.schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion;
            return result;
        }

        /// <summary>
        /// Accepts one committed transition, advances terminal history independently of output settings,
        /// and selects at most one exact event-time writer only when visibility is also verified.
        /// </summary>
        public static CreepJoinerOutcomePlan Plan(
            CreepJoinerOutcomeFacts facts,
            CreepJoinerArcSnapshot existing,
            AnomalyPolicySnapshot policy)
        {
            CreepJoinerOutcomePlan plan = new CreepJoinerOutcomePlan();
            if (!ValidCommittedTransition(facts) || facts.nestedOutcomeOwnedByRejection)
                return plan;

            plan.valid = true;
            plan.phase = facts.phase.Trim();
            plan.visibleResultToken = facts.visibleResultToken.Trim();
            plan.sourceKey = CleanStable(facts.pawnId) + "|" + plan.phase + "|" + facts.tick;

            CreepJoinerArcSnapshot current = AnomalyPersistencePolicy.NormalizeCreepJoinerArc(existing);
            if (current != null && (current.terminal
                || current.schemaVersion > AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion))
            {
                plan.replaySuppressed = true;
                plan.nextArc = current;
                return plan;
            }

            plan.advanceArc = true;
            plan.nextArc = current ?? new CreepJoinerArcSnapshot
            {
                pawnId = CleanStable(facts.pawnId),
                // Zero is the explicit "never joined / joined at game start" safe sentinel. A
                // pre-acceptance rejection/departure must not invent an arrival tick.
                joinedTick = 0,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            bool visibleOutcomeVerified = facts.transitionVerified && facts.playerVisible;
            // The private marker is still a durable replay boundary when a modded response is silent
            // or another mod changes vanilla's expected post-state. Do not claim a visible phase or
            // page without the stronger evidence, but also do not leave a committed transition open.
            plan.nextArc.lastVisiblePhase = visibleOutcomeVerified ? plan.phase : string.Empty;
            plan.nextArc.lastVisibleEventId = string.Empty;
            plan.nextArc.terminal = true;
            plan.nextArc.schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion;

            if (!visibleOutcomeVerified) return plan;

            AnomalyPolicySnapshot effective = AnomalyPolicyNormalization.Normalize(policy);
            plan.selectedWriter = SelectWriter(facts, effective);
            plan.writePage = effective.creepJoinerEnabled && plan.selectedWriter != null;
            return plan;
        }

        private static bool ValidCommittedTransition(CreepJoinerOutcomeFacts facts)
        {
            if (facts == null || facts.tick < 0 || !facts.transitioned
                || !CreepJoinerPhaseTokens.IsOutcome(facts.phase))
            {
                return false;
            }

            string expectedResult = CreepJoinerVisibleResultTokens.ForPhase(facts.phase);
            return CleanStable(facts.pawnId).Length > 0
                && string.Equals(expectedResult, (facts.visibleResultToken ?? string.Empty).Trim(),
                    StringComparison.Ordinal);
        }

        private static AnomalyWriterSelection SelectWriter(
            CreepJoinerOutcomeFacts facts,
            AnomalyPolicySnapshot policy)
        {
            List<CreepJoinerWriterCandidate> candidates = NormalizeCandidates(facts.writers);
            bool beforeJoining = !facts.joinedBefore;
            if (beforeJoining && (facts.phase == AnomalyOutcomeTokens.Rejected
                || facts.phase == AnomalyOutcomeTokens.Departed
                || facts.aggressionFollowedRejection))
            {
                CreepJoinerWriterCandidate speaker = First(
                    candidates, row => row.exactSpeaker && row.onSubjectMap);
                if (speaker != null) return Selection(speaker, AnomalyWitnessRoleTokens.Speaker);
            }

            if (facts.phase == AnomalyOutcomeTokens.Departed && facts.joinedBefore
                && facts.subjectEligibleBefore)
            {
                CreepJoinerWriterCandidate subject = First(candidates, row => row.subject);
                if (subject != null) return Selection(subject, AnomalyWitnessRoleTokens.Subject);
            }

            long radiusSquared = (long)policy.creepJoinerWitnessRadius
                * policy.creepJoinerWitnessRadius;
            List<CreepJoinerWriterCandidate> nearby = new List<CreepJoinerWriterCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                CreepJoinerWriterCandidate row = candidates[i];
                if (!row.subject && row.onSubjectMap && row.nearby
                    && row.distanceSquared >= 0 && row.distanceSquared <= radiusSquared)
                {
                    nearby.Add(row);
                }
            }

            nearby.Sort(CompareNearby);
            int cap = Math.Min(policy.creepJoinerMaxWitnesses, nearby.Count);
            return cap > 0 ? Selection(nearby[0], AnomalyWitnessRoleTokens.Nearby) : null;
        }

        private static List<CreepJoinerWriterCandidate> NormalizeCandidates(
            List<CreepJoinerWriterCandidate> source)
        {
            Dictionary<string, CreepJoinerWriterCandidate> byId =
                new Dictionary<string, CreepJoinerWriterCandidate>(StringComparer.Ordinal);
            int count = Math.Min(source?.Count ?? 0, AnomalyPolicyLimits.MaximumCreepJoinerCandidateInputs);
            for (int i = 0; i < count; i++)
            {
                CreepJoinerWriterCandidate row = source[i];
                string id = CleanStable(row?.pawnId);
                if (id.Length == 0 || row == null || !row.eligible) continue;
                CreepJoinerWriterCandidate copy;
                if (!byId.TryGetValue(id, out copy))
                {
                    copy = new CreepJoinerWriterCandidate
                    {
                        pawnId = id,
                        eligible = true,
                        distanceSquared = row.distanceSquared,
                        tieBreakKey = CleanStable(row.tieBreakKey)
                    };
                    byId.Add(id, copy);
                }

                copy.exactSpeaker |= row.exactSpeaker;
                copy.subject |= row.subject;
                copy.onSubjectMap |= row.onSubjectMap;
                copy.nearby |= row.nearby;
                if (row.distanceSquared >= 0
                    && (copy.distanceSquared < 0 || row.distanceSquared < copy.distanceSquared))
                    copy.distanceSquared = row.distanceSquared;
                string tie = CleanStable(row.tieBreakKey);
                if (tie.Length > 0 && (copy.tieBreakKey.Length == 0
                    || string.CompareOrdinal(tie, copy.tieBreakKey) < 0))
                    copy.tieBreakKey = tie;
            }

            List<CreepJoinerWriterCandidate> result = new List<CreepJoinerWriterCandidate>(byId.Values);
            // Preserve the strongest evidence when an extreme modded roster exceeds the hard cap.
            // Exact speaker/subject rows cannot be displaced by a large set of ordinary witnesses;
            // nearby rows then keep the closest deterministic candidates.
            result.Sort(CompareForCandidateCap);
            if (result.Count > AnomalyPolicyLimits.MaximumCreepJoinerCandidates)
                result.RemoveRange(
                    AnomalyPolicyLimits.MaximumCreepJoinerCandidates,
                    result.Count - AnomalyPolicyLimits.MaximumCreepJoinerCandidates);
            result.Sort((left, right) => string.CompareOrdinal(left.pawnId, right.pawnId));
            return result;
        }

        private static int CompareForCandidateCap(
            CreepJoinerWriterCandidate left,
            CreepJoinerWriterCandidate right)
        {
            bool leftSpeaker = left.exactSpeaker && left.onSubjectMap;
            bool rightSpeaker = right.exactSpeaker && right.onSubjectMap;
            int speaker = rightSpeaker.CompareTo(leftSpeaker);
            if (speaker != 0) return speaker;
            int subject = right.subject.CompareTo(left.subject);
            if (subject != 0) return subject;
            bool leftNearby = left.onSubjectMap && left.nearby && left.distanceSquared >= 0;
            bool rightNearby = right.onSubjectMap && right.nearby && right.distanceSquared >= 0;
            int nearby = rightNearby.CompareTo(leftNearby);
            if (nearby != 0) return nearby;
            if (leftNearby)
            {
                int distance = left.distanceSquared.CompareTo(right.distanceSquared);
                if (distance != 0) return distance;
            }
            return string.CompareOrdinal(left.pawnId, right.pawnId);
        }

        private static CreepJoinerWriterCandidate First(
            List<CreepJoinerWriterCandidate> candidates,
            Predicate<CreepJoinerWriterCandidate> predicate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (predicate(candidates[i])) return candidates[i];
            }
            return null;
        }

        private static int CompareNearby(
            CreepJoinerWriterCandidate left,
            CreepJoinerWriterCandidate right)
        {
            int distance = left.distanceSquared.CompareTo(right.distanceSquared);
            if (distance != 0) return distance;
            string leftTie = left.tieBreakKey.Length == 0 ? left.pawnId : left.tieBreakKey;
            string rightTie = right.tieBreakKey.Length == 0 ? right.pawnId : right.tieBreakKey;
            int tie = string.CompareOrdinal(leftTie, rightTie);
            return tie != 0 ? tie : string.CompareOrdinal(left.pawnId, right.pawnId);
        }

        private static AnomalyWriterSelection Selection(
            CreepJoinerWriterCandidate candidate,
            string role)
        {
            return candidate == null ? null : new AnomalyWriterSelection
            {
                pawnId = candidate.pawnId,
                roleToken = role
            };
        }

        private static CreepJoinerArcSnapshot Clone(CreepJoinerArcSnapshot source)
        {
            if (source == null) return null;
            return new CreepJoinerArcSnapshot
            {
                pawnId = source.pawnId ?? string.Empty,
                arrivalEventId = source.arrivalEventId ?? string.Empty,
                joinedTick = source.joinedTick,
                lastVisiblePhase = source.lastVisiblePhase ?? string.Empty,
                lastVisibleEventId = source.lastVisibleEventId ?? string.Empty,
                terminal = source.terminal,
                schemaVersion = source.schemaVersion
            };
        }

        private static string CleanStable(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length > 0 && clean.Length <= 200
                && clean.IndexOf('|') < 0 && clean.IndexOf(';') < 0 && clean.IndexOf('=') < 0
                && clean.IndexOf('\r') < 0 && clean.IndexOf('\n') < 0
                ? clean
                : string.Empty;
        }
    }
}
