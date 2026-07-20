// Pure A2.1 policy for one verified creepjoiner surgical disclosure. The live recipe, Pawn,
// tracker, StringBuilder, Tale, settings Def, and save adapter stay outside this file; this policy
// advances a non-terminal visible arc and orders only the exact surgeon and subject POVs.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Plans one non-terminal surgical reveal without exposing what the inspection found.</summary>
    internal static class CreepJoinerSurgicalDisclosurePolicy
    {
        /// <summary>
        /// Commits verified disclosure history before optional output and never converts it into a
        /// terminal barrier, so a later rejection, aggression, or departure can still close the arc.
        /// </summary>
        public static CreepJoinerSurgicalDisclosurePlan Plan(
            CreepJoinerSurgicalDisclosureFacts facts,
            CreepJoinerArcSnapshot existing,
            AnomalyPolicySnapshot policy)
        {
            CreepJoinerSurgicalDisclosurePlan plan = new CreepJoinerSurgicalDisclosurePlan();
            if (!ValidDisclosure(facts)) return plan;

            plan.valid = true;
            plan.phase = AnomalyOutcomeTokens.SurgicalReveal;
            plan.visibleResultToken = CreepJoinerVisibleResultTokens.Disclosed;
            string subjectId = CleanStable(facts.subjectPawnId);
            plan.sourceKey = subjectId + "|" + plan.phase + "|" + facts.tick;

            CreepJoinerArcSnapshot current = AnomalyPersistencePolicy.NormalizeCreepJoinerArc(existing);
            if (current != null && !string.Equals(
                    current.pawnId, subjectId, StringComparison.Ordinal))
            {
                // A caller supplied continuity for another pawn. Preserve that normalized row and
                // fail open to the generic Tale instead of mutating unrelated history.
                plan.replaySuppressed = true;
                plan.nextArc = current;
                return plan;
            }
            if (current != null && (current.terminal
                || current.schemaVersion > AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
                || current.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal))
            {
                plan.replaySuppressed = true;
                plan.nextArc = current;
                return plan;
            }

            plan.advanceArc = true;
            plan.nextArc = current ?? new CreepJoinerArcSnapshot
            {
                pawnId = subjectId,
                // A pre-acceptance inspection has no canonical join tick. Zero is the existing safe
                // sentinel and can later be enriched with an arrival event without rewriting history.
                joinedTick = 0,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            plan.nextArc.lastVisiblePhase = plan.phase;
            plan.nextArc.lastVisibleEventId = string.Empty;
            plan.nextArc.terminal = false;
            plan.nextArc.schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion;

            AnomalyPolicySnapshot effective = AnomalyPolicyNormalization.Normalize(policy);
            string surgeonId = CleanStable(facts.surgeonPawnId);
            if (facts.surgeonEligible && surgeonId.Length > 0)
            {
                plan.selectedWriters.Add(new AnomalyWriterSelection
                {
                    pawnId = surgeonId,
                    roleToken = AnomalyWitnessRoleTokens.Surgeon
                });
            }
            if (facts.subjectEligible && subjectId.Length > 0
                && plan.selectedWriters.Count < effective.creepJoinerMaxWitnesses
                && !string.Equals(subjectId, surgeonId, StringComparison.Ordinal))
            {
                plan.selectedWriters.Add(new AnomalyWriterSelection
                {
                    pawnId = subjectId,
                    roleToken = AnomalyWitnessRoleTokens.Subject
                });
            }

            plan.writePage = effective.creepJoinerEnabled && plan.selectedWriters.Count > 0;
            return plan;
        }

        private static bool ValidDisclosure(CreepJoinerSurgicalDisclosureFacts facts)
        {
            return facts != null && facts.tick >= 0 && facts.surgeryCompleted
                && facts.trackerDisclosureAppended && facts.playerVisible
                && CleanStable(facts.subjectPawnId).Length > 0
                && CleanStable(facts.surgeonPawnId).Length > 0;
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
