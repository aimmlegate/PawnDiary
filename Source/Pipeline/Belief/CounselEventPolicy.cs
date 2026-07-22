// Pure exact-policy seam for Ideology's Counsel ability. Vanilla applies a mood thought and then
// emits one Counsel_Success or Counsel_Failure PlayLog interaction. This helper classifies only
// those verified names, builds detached visible evidence for the existing pair page, and appends
// stable prompt-schema facts without reading Pawn, Ideo, Thought, Verse, or Unity state.
using System;

namespace PawnDiary
{
    /// <summary>The two outcomes proved by vanilla's exact downstream Counsel interactions.</summary>
    internal enum CounselEventOutcome
    {
        None,
        Success,
        Failure
    }

    /// <summary>Pure exact matching, evidence construction, and context formatting for Counsel.</summary>
    internal static class CounselEventPolicy
    {
        public const string GroupDefName = "counsel";
        public const string SuccessDefName = "Counsel_Success";
        public const string FailureDefName = "Counsel_Failure";
        public const string SuccessContext =
            "counsel_result=success; counsel_mood_effect=relief_or_boost";
        public const string FailureContext =
            "counsel_result=failure; counsel_mood_effect=penalty";

        /// <summary>
        /// Classifies only the installed vanilla interaction names under the exact Counsel group.
        /// Ordinal matching keeps case variants and similarly named modded events silent.
        /// </summary>
        public static CounselEventOutcome Classify(
            string interactionDefName,
            string effectiveGroupDefName,
            bool ideologyActive)
        {
            if (!ideologyActive
                || !string.Equals(effectiveGroupDefName, GroupDefName, StringComparison.Ordinal))
                return CounselEventOutcome.None;
            if (string.Equals(interactionDefName, SuccessDefName, StringComparison.Ordinal))
                return CounselEventOutcome.Success;
            if (string.Equals(interactionDefName, FailureDefName, StringComparison.Ordinal))
                return CounselEventOutcome.Failure;
            return CounselEventOutcome.None;
        }

        /// <summary>
        /// Builds detached event evidence for the listener only after an existing PlayLog page has
        /// already been authorized. No mutation or current-certainty request is attached.
        /// </summary>
        public static BeliefEventEvidence ForInteraction(
            string interactionDefName,
            string effectiveGroupDefName,
            bool ideologyActive,
            string recipientPawnId,
            int eventTick,
            string recipientLabel,
            string interactionLabel)
        {
            CounselEventOutcome outcome = Classify(
                interactionDefName, effectiveGroupDefName, ideologyActive);
            if (outcome == CounselEventOutcome.None) return null;
            return BeliefEventEvidenceFactory.ForCounsel(
                recipientPawnId,
                eventTick,
                interactionDefName,
                recipientLabel,
                interactionLabel,
                outcome == CounselEventOutcome.Success);
        }

        /// <summary>
        /// Appends the exact mood outcome once. It never emits conversion, certainty, or ideology facts.
        /// </summary>
        public static string AppendGameContext(string gameContext, BeliefEventEvidence evidence)
        {
            string current = gameContext ?? string.Empty;
            CounselEventOutcome outcome = Classify(
                evidence?.narrative?.sourceDefName,
                evidence?.groupKey,
                ideologyActive: true);
            if (outcome == CounselEventOutcome.None
                || current.IndexOf("counsel_result=", StringComparison.Ordinal) >= 0)
                return current;

            string marker = outcome == CounselEventOutcome.Success
                ? SuccessContext
                : FailureContext;
            string prefix = current.Trim().TrimEnd(';');
            return prefix.Length == 0 ? marker : prefix + "; " + marker;
        }
    }
}
