// Main-thread adapter between existing authorized pages and pure Ideology event evidence. Mutation
// consumers read the transient detached cache; exact Counsel uses only already-visible PlayLog facts.
// The adapter reads no live Ideology object and cannot authorize, create, queue, or persist a page.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>Peeks exact event-relative mechanical evidence for existing authorized adapters.</summary>
    internal static class BeliefMutationEvidenceAdapter
    {
        /// <summary>
        /// Returns exact Counsel or conversion/reassurance evidence for one authorized PlayLog
        /// interaction, or null when DLC, policy, XML mapping, ownership, identity, time, or mechanics differ.
        /// </summary>
        public static BeliefEventEvidence ForInteraction(
            string interactionDefName,
            string effectiveGroupDefName,
            string initiatorPawnId,
            string recipientPawnId,
            int eventTick,
            string initiatorLabel = null,
            string recipientLabel = null,
            string interactionLabel = null)
        {
            try
            {
                if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying) return null;

                // Counsel's verified PlayLog result is itself the mechanical boundary: vanilla has
                // already applied the listener's mood thought, and no ideology mutation exists to peek.
                BeliefEventEvidence counsel = CounselEventPolicy.ForInteraction(
                    interactionDefName,
                    effectiveGroupDefName,
                    ideologyActive: true,
                    recipientPawnId,
                    eventTick,
                    recipientLabel,
                    interactionLabel);
                if (counsel != null) return counsel;

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
        /// Optional belief evidence must never unwind past the source which already authorized the
        /// ordinary page. Log each source/exception kind once and let that page continue un-enriched.
        /// </summary>
        private static void WarnAndKeepOrdinaryPage(string sourceKind, Exception exception)
        {
            Type type = exception.GetType();
            Log.WarningOnce(
                "[Pawn Diary] Ideology event-evidence enrichment failed for " + sourceKind
                    + "; this page keeps ordinary context: " + type.FullName + ": " + exception.Message,
                ("PawnDiary.BeliefMutationEvidenceAdapter." + sourceKind + "." + type.FullName)
                    .GetHashCode());
        }
    }
}
