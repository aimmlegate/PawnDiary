// Pure Odyssey O3 policy for the verified Cerebrex-core ending. Runtime adapters supply only
// detached primitives: the exact quest root/id, operator identity, destroy-versus-scavenge choice,
// normal-return proof, and player visibility. No RimWorld, Verse, Harmony, or Unity type crosses this
// boundary, so choice validation, page prediction, and save normalization stay standalone-testable.
using System;

namespace PawnDiary
{
    /// <summary>Stable synthetic identity for Odyssey's verified Mechhive ending page.</summary>
    internal static class OdysseyMechhiveEventDefNames
    {
        public const string Outcome = "PawnDiary_OdysseyMechhiveOutcome";
    }

    /// <summary>Stable exact QuestScriptDef and quest-part identities proven from RimWorld 1.6.</summary>
    internal static class OdysseyMechhiveSourceTokens
    {
        public const string QuestRootDefName = "Gravcore_Mechhive";
        public const string QuestPartTypeName = "RimWorld.QuestGen.QuestPart_CerebrexCore";
    }

    /// <summary>Visible choices committed by CompCerebrexCore.DeactivateCore(bool scavenging).</summary>
    internal static class OdysseyMechhiveOutcomeTokens
    {
        public const string Destroyed = "destroyed";
        public const string Scavenged = "scavenged";

        /// <summary>Returns one canonical terminal token or an empty string for unsafe input.</summary>
        public static string Normalize(string value)
        {
            string token = (value ?? string.Empty).Trim();
            if (string.Equals(token, Destroyed, StringComparison.OrdinalIgnoreCase)) return Destroyed;
            if (string.Equals(token, Scavenged, StringComparison.OrdinalIgnoreCase)) return Scavenged;
            return string.Empty;
        }
    }

    /// <summary>Detached facts from one exact normally-returned Cerebrex-core resolution.</summary>
    internal sealed class OdysseyMechhiveOutcomeFacts
    {
        public string actorPawnId = string.Empty;
        public string actorLabel = string.Empty;
        public string actorSummary = string.Empty;
        public string setting = string.Empty;
        public string outcomeToken = string.Empty;
        public string questRootDefName = string.Empty;
        public int questId;
        public int tick = -1;
        public bool methodReturnedNormally;
        public bool questSuccessObserved;
        public bool playerVisible;
        public bool actorEligible;
    }

    /// <summary>Pure result for one verified Mechhive resolution.</summary>
    internal sealed class OdysseyMechhiveOutcomePlan
    {
        public bool valid;
        public bool commitOutcome;
        public bool writePage;
        public string outcomeToken = string.Empty;
        public string sourceKey = string.Empty;
        public string actorPawnId = string.Empty;
    }

    /// <summary>Additive saved replay barrier and canonical event identity for the Mechhive ending.</summary>
    internal sealed class OdysseyMechhiveOutcomeSnapshot
    {
        public int schemaVersion = 1;
        public string outcomeToken = string.Empty;
        public string actorPawnId = string.Empty;
        public int questId;
        public int committedTick = -1;
        public string eventId = string.Empty;
    }

    /// <summary>Validates the exact committed source and decides whether one actor-authored page exists.</summary>
    internal static class OdysseyMechhiveOutcomePolicy
    {
        /// <summary>Stable diagnostics domain for the dedicated O3 owner.</summary>
        internal const string NarrativeSourceDomain = "odyssey";

        /// <summary>
        /// Prefix-time prediction for claiming vanilla's synchronous Quest completion. Every downstream
        /// page gate is explicit because the Quest fan-out runs before the postfix verifies the outcome.
        /// </summary>
        public static bool PredictsDedicatedPage(
            bool actorEligible,
            bool policyEnabled,
            bool outcomePageEnabled,
            bool interactionGroupEnabled)
        {
            return actorEligible && policyEnabled && outcomePageEnabled && interactionGroupEnabled;
        }

        /// <summary>Plans only exact, visible, normally-returned destroy/scavenge quest resolutions.</summary>
        public static OdysseyMechhiveOutcomePlan Plan(
            OdysseyMechhiveOutcomeFacts facts,
            OdysseyPolicySnapshot policy)
        {
            OdysseyMechhiveOutcomePlan result = new OdysseyMechhiveOutcomePlan();
            string outcome = OdysseyMechhiveOutcomeTokens.Normalize(facts?.outcomeToken);
            if (facts == null
                || outcome.Length == 0
                || string.IsNullOrWhiteSpace(facts.actorPawnId)
                || facts.questId <= 0
                || facts.tick < 0
                || !facts.methodReturnedNormally
                || !facts.questSuccessObserved
                || !facts.playerVisible
                || !string.Equals(
                    (facts.questRootDefName ?? string.Empty).Trim(),
                    OdysseyMechhiveSourceTokens.QuestRootDefName,
                    StringComparison.Ordinal))
            {
                return result;
            }

            result.valid = true;
            result.commitOutcome = true;
            result.outcomeToken = outcome;
            result.sourceKey = OdysseyArcKeys.MechhiveOutcome(facts.questId);
            result.actorPawnId = facts.actorPawnId.Trim();
            result.writePage = result.sourceKey.Length > 0
                && facts.actorEligible
                && policy != null
                && policy.enabled
                && policy.mechhiveOutcomePageEnabled;
            return result;
        }

        /// <summary>True only when the plan can replace its exact generic Quest success page.</summary>
        public static bool OwnsQuestSuccess(OdysseyMechhiveOutcomePlan plan)
        {
            return plan != null && plan.valid && plan.commitOutcome && plan.writePage
                && !string.IsNullOrWhiteSpace(plan.sourceKey)
                && !string.IsNullOrWhiteSpace(plan.actorPawnId);
        }
    }

    /// <summary>Normalizes saved Mechhive terminal state; malformed partial rows collapse completely.</summary>
    internal static class OdysseyMechhivePersistencePolicy
    {
        private const int MaximumIdentityCharacters = 200;

        /// <summary>
        /// Returns a detached canonical row, or null when any required identity/outcome field is corrupt.
        /// The runtime commit path calls this same method before accepting in-session state.
        /// </summary>
        public static OdysseyMechhiveOutcomeSnapshot Normalize(
            OdysseyMechhiveOutcomeSnapshot source)
        {
            if (source == null) return null;
            string outcome = OdysseyMechhiveOutcomeTokens.Normalize(source.outcomeToken);
            string actor = CleanIdentity(source.actorPawnId);
            if (outcome.Length == 0 || actor.Length == 0
                || source.questId <= 0 || source.committedTick < 0)
            {
                return null;
            }

            return new OdysseyMechhiveOutcomeSnapshot
            {
                schemaVersion = Math.Max(1, source.schemaVersion),
                outcomeToken = outcome,
                actorPawnId = actor,
                questId = source.questId,
                committedTick = source.committedTick,
                eventId = CleanIdentity(source.eventId)
            };
        }

        private static string CleanIdentity(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            if (clean.IndexOf('|') >= 0 || clean.IndexOf(';') >= 0
                || clean.IndexOf('\r') >= 0 || clean.IndexOf('\n') >= 0)
            {
                return string.Empty;
            }
            return clean.Length <= MaximumIdentityCharacters
                ? clean
                : clean.Substring(0, MaximumIdentityCharacters);
        }
    }
}
