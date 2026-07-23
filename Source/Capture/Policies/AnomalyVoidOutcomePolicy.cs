// Pure A3.0 policy for one terminal void answer. Live Pawns, utility Defs, quests, Tales, settings,
// Harmony state, and Anomaly component state stay outside this file; it accepts detached pre/post
// truth and authorizes exactly one verified terminal commit plus the single choosing-pawn author.
// A method/level pair that disagrees drops and fails open so the vanilla Tale is never lost.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Authorizes one verified terminal void outcome and selects the sole choosing author.</summary>
    internal static class AnomalyVoidOutcomePolicy
    {
        /// <summary>Terminal MonolithLevelDef.defName reached by an embrace.</summary>
        internal const string EmbracedLevelDefName = "Embraced";

        /// <summary>Terminal MonolithLevelDef.defName reached by a disruption.</summary>
        internal const string DisruptedLevelDefName = "Disrupted";

        /// <summary>Returns the exact expected terminal level defName for a branch token, or empty.</summary>
        public static string ExpectedLevelDefName(string outcome)
        {
            string token = NormalizeOutcome(outcome);
            if (token == AnomalyOutcomeTokens.Embraced) return EmbracedLevelDefName;
            if (token == AnomalyOutcomeTokens.Disrupted) return DisruptedLevelDefName;
            return string.Empty;
        }

        /// <summary>Returns the exact single-pawn Tale defName vanilla records for a branch, or empty.</summary>
        public static string ExpectedTaleDefName(string outcome)
        {
            string token = NormalizeOutcome(outcome);
            if (token == AnomalyOutcomeTokens.Embraced)
                return AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName;
            if (token == AnomalyOutcomeTokens.Disrupted)
                return AnomalyVoidTaleOwnershipPolicy.ClosedTheVoidDefName;
            return string.Empty;
        }

        /// <summary>Returns "embraced"/"disrupted" for a supported branch token, or empty.</summary>
        public static string NormalizeOutcome(string outcome)
        {
            string token = (outcome ?? string.Empty).Trim();
            return token == AnomalyOutcomeTokens.Embraced || token == AnomalyOutcomeTokens.Disrupted
                ? token
                : string.Empty;
        }

        /// <summary>Builds a terminal-commit plan and optional single-author page from detached facts.</summary>
        public static VoidOutcomePlan Plan(VoidOutcomeFacts facts, AnomalyPolicySnapshot policy)
        {
            VoidOutcomePlan plan = new VoidOutcomePlan();
            if (!ValidTerminal(facts)) return plan;

            string actorId = CleanStable(facts.actorPawnId);
            string outcome = NormalizeOutcome(facts.outcome);
            plan.valid = true;
            plan.commitOutcome = true;
            plan.outcomeToken = outcome;
            plan.reachedLevelDefName = ExpectedLevelDefName(outcome);
            plan.sourceKey = actorId + "|" + AnomalyKindTokens.VoidOutcome + "|" + outcome
                + "|" + facts.tick;

            AnomalyPolicySnapshot effective = AnomalyPolicyNormalization.Normalize(policy);
            if (facts.actorEligible)
            {
                plan.selectedWriter = new AnomalyWriterSelection
                {
                    pawnId = actorId,
                    roleToken = AnomalyWitnessRoleTokens.Actor
                };
            }

            // The terminal outcome commits to saved state whenever it verifies, even when output is
            // disabled or the sole author cannot write; only the optional page depends on both.
            plan.writePage = effective.voidOutcomeEnabled && plan.selectedWriter != null;
            return plan;
        }

        /// <summary>
        /// True only when this plan proves ownership of the terminal single-pawn Tale. Final
        /// suppression additionally requires a deferred Tale and an actually created replacement page.
        /// </summary>
        public static bool OwnsTerminalTale(VoidOutcomePlan plan)
        {
            return plan != null && plan.valid && plan.commitOutcome
                && NormalizeOutcome(plan.outcomeToken).Length > 0;
        }

        private static bool ValidTerminal(VoidOutcomeFacts facts)
        {
            if (facts == null || facts.tick < 0 || !facts.methodReturnedNormally
                || !facts.playerVisible) return false;
            string outcome = NormalizeOutcome(facts.outcome);
            string actorId = CleanStable(facts.actorPawnId);
            if (outcome.Length == 0 || actorId.Length == 0) return false;

            // The branch, the level the branch should reach, and the level actually observed after
            // vanilla returned must all agree. A contradictory method/level pair (for example an
            // embrace that somehow left the monolith at Disrupted) drops and fails open.
            string expected = ExpectedLevelDefName(outcome);
            return expected.Length > 0
                && string.Equals(
                    expected, (facts.expectedLevelDefName ?? string.Empty).Trim(),
                    StringComparison.Ordinal)
                && string.Equals(
                    expected, (facts.reachedLevelDefName ?? string.Empty).Trim(),
                    StringComparison.Ordinal);
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
