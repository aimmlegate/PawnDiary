// Main-thread adapter between the transient detached mutation cache and pure event correlation.
// It runs only after an existing source has already authorized a page. The adapter reads no live
// Ideology object and cannot authorize, create, queue, or persist a DiaryEvent.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>Peeks exact event-relative mechanical evidence for existing authorized adapters.</summary>
    internal static class BeliefMutationEvidenceAdapter
    {
        /// <summary>
        /// Returns conversion/reassurance evidence for one exact authorized PlayLog interaction, or
        /// null when DLC, policy, XML mapping, group ownership, pawn identity, time, or mechanics differ.
        /// </summary>
        public static BeliefEventEvidence ForInteraction(
            string interactionDefName,
            string effectiveGroupDefName,
            string initiatorPawnId,
            string recipientPawnId,
            int eventTick,
            string initiatorLabel = null,
            string recipientLabel = null)
        {
            try
            {
                if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying) return null;
                BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
                BeliefMutationEventRule rule = BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction,
                    interactionDefName,
                    effectiveGroupDefName,
                    ideologyActive: true,
                    policyEnabled: policy.enabled,
                    policy.mutationEventRules);
                if (rule == null) return null;

                string subjectPawnId = BeliefMutationEventSelector.SubjectPawnId(
                    rule, initiatorPawnId, recipientPawnId);
                if (subjectPawnId.Length == 0) return null;
                string subjectLabel = rule.subjectRole == BeliefMutationSubjectRoleTokens.Initiator
                    ? initiatorLabel
                    : recipientLabel;
                BeliefMutationSnapshot newest = BeliefMutationCache.PeekLatest(
                    subjectPawnId, eventTick, policy);
                return BeliefMutationEventSelector.Select(
                    rule, subjectPawnId, eventTick, policy.mutationCorrelationWindowTicks, newest,
                    subjectLabel);
            }
            catch (Exception exception)
            {
                WarnAndKeepOrdinaryPage("interaction", exception);
                return null;
            }
        }

        /// <summary>
        /// Returns exact IdeoChange crisis evidence for an already-authorized solo mental-state page.
        /// The cache read is a peek: both mutation-backed and current-only results remain detached facts,
        /// and this adapter can neither consume evidence nor create another page.
        /// </summary>
        public static BeliefEventEvidence ForMentalState(
            string mentalStateDefName,
            string effectiveGroupDefName,
            string pawnId,
            int eventTick,
            string pawnLabel = null)
        {
            try
            {
                if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying) return null;
                BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
                BeliefMutationEventRule rule = BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.MentalState,
                    mentalStateDefName,
                    effectiveGroupDefName,
                    ideologyActive: true,
                    policyEnabled: policy.enabled,
                    policy.mutationEventRules);
                if (rule == null) return null;

                string subjectPawnId = BeliefMutationEventSelector.SubjectPawnId(
                    rule, pawnId, null);
                if (subjectPawnId.Length == 0) return null;
                BeliefMutationSnapshot newest = BeliefMutationCache.PeekLatest(
                    subjectPawnId, eventTick, policy);
                return BeliefMutationEventSelector.SelectCrisisOrCurrent(
                    rule, subjectPawnId, eventTick, policy.mutationCorrelationWindowTicks,
                    newest, pawnLabel);
            }
            catch (Exception exception)
            {
                WarnAndKeepOrdinaryPage("mental-state", exception);
                return null;
            }
        }

        /// <summary>
        /// Optional mutation evidence must never unwind past the source which already authorized the
        /// ordinary page. Log each source/exception kind once and let that page continue un-enriched.
        /// </summary>
        private static void WarnAndKeepOrdinaryPage(string sourceKind, Exception exception)
        {
            Type type = exception.GetType();
            Log.WarningOnce(
                "[Pawn Diary] Ideology mutation-evidence enrichment failed for " + sourceKind
                    + "; this page keeps ordinary context: " + type.FullName + ": " + exception.Message,
                ("PawnDiary.BeliefMutationEvidenceAdapter." + sourceKind + "." + type.FullName)
                    .GetHashCode());
        }
    }
}
