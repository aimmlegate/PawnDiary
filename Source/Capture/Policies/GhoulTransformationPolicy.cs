// Pure A2.2 policy for one successful ghoul infusion. Live Pawns, recipe Defs, Tales, settings,
// Harmony state, and DLC trackers stay outside this file; it accepts detached pre/post truth and
// selects only the exact surgeon and subject in deterministic order.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Authorizes exactly one verified non-ghoul-to-ghoul transformation.</summary>
    internal static class GhoulTransformationPolicy
    {
        /// <summary>Builds a source-owned page plan from detached recipe facts.</summary>
        public static GhoulTransformationPlan Plan(
            GhoulTransformationFacts facts,
            AnomalyPolicySnapshot policy)
        {
            GhoulTransformationPlan plan = new GhoulTransformationPlan();
            if (!ValidTransition(facts)) return plan;

            string subjectId = CleanStable(facts.subjectPawnId);
            string surgeonId = CleanStable(facts.surgeonPawnId);
            plan.valid = true;
            plan.transitionVerified = true;
            plan.transformationToken = AnomalyOutcomeTokens.Ghoul;
            plan.sourceKey = subjectId + "|" + AnomalyKindTokens.GhoulTransformation
                + "|" + facts.tick;

            AnomalyPolicySnapshot effective = AnomalyPolicyNormalization.Normalize(policy);
            if (facts.surgeonEligible)
            {
                plan.selectedWriters.Add(new AnomalyWriterSelection
                {
                    pawnId = surgeonId,
                    roleToken = AnomalyWitnessRoleTokens.Surgeon
                });
            }
            if (facts.subjectEligible
                && plan.selectedWriters.Count < effective.ghoulTransformationMaxWriters
                && !string.Equals(subjectId, surgeonId, StringComparison.Ordinal))
            {
                plan.selectedWriters.Add(new AnomalyWriterSelection
                {
                    pawnId = subjectId,
                    roleToken = AnomalyWitnessRoleTokens.Subject
                });
            }

            plan.writePage = effective.ghoulTransformationEnabled
                && plan.selectedWriters.Count > 0;
            return plan;
        }

        /// <summary>True only when this plan proves the dedicated event owns DidSurgery.</summary>
        public static bool OwnsDidSurgery(GhoulTransformationPlan plan)
        {
            return plan != null && plan.valid && plan.transitionVerified
                && plan.transformationToken == AnomalyOutcomeTokens.Ghoul;
        }

        private static bool ValidTransition(GhoulTransformationFacts facts)
        {
            return facts != null && facts.tick >= 0 && facts.methodReturnedNormally
                && !facts.wasGhoul && facts.isGhoul && facts.playerVisible
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
