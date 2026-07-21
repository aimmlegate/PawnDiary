// Main-thread adapter between the transient detached mutation cache and pure event correlation.
// It runs only after an existing source has already authorized a page. The adapter reads no live
// Ideology object and cannot authorize, create, queue, or persist a DiaryEvent.
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
    }
}
