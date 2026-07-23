// Impure adapter for one verified Odyssey O3 Mechhive resolution. Pure policy has already proven the
// exact quest, normal return, destroy-versus-scavenge branch, and choosing operator. This class only
// applies XML/settings gates, creates one localized solo fallback page, restores the preverified diary
// reference, and queues generation. No witness or colony fan-out is allowed.
using System;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one operator-authored page for the verified Mechhive ending.</summary>
    internal sealed class OdysseyMechhiveOutcomeSignal : DiarySignal
    {
        private readonly OdysseyMechhiveOutcomeFacts facts;
        private readonly OdysseyMechhiveOutcomePlan plan;
        private readonly OdysseyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly Pawn actor;
        private readonly OdysseyEventData payload;

        /// <summary>Non-null only after the dedicated terminal event actually entered the repository.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        internal OdysseyMechhiveOutcomeSignal(
            OdysseyMechhiveOutcomeFacts facts,
            OdysseyMechhiveOutcomePlan plan,
            OdysseyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            Pawn actor)
        {
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? OdysseyPolicySnapshot.CreateDefault();
            this.group = group;
            this.actor = actor;
            payload = new OdysseyEventData
            {
                PawnId = plan?.actorPawnId ?? string.Empty,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = OdysseyMechhiveEventDefNames.Outcome,
                SourceKey = plan?.sourceKey ?? string.Empty,
                HasVerifiedSource = OdysseyMechhiveOutcomePolicy.OwnsQuestSuccess(plan),
                PlayerVisible = facts?.playerVisible == true,
                AlreadyRecorded = false,
                WriterId = plan?.actorPawnId ?? string.Empty,
                WriterEligible = ActorMatches(plan?.actorPawnId)
            };
            PreserveHistoricalOrdering(payload.Tick);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.WriterEligible,
                userEnabled: enabled,
                signalEnabled: ModsConfig.OdysseyActive
                    && policy.enabled
                    && policy.mechhiveOutcomePageEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.landingCorrelationTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || facts == null || plan == null || group == null || actor == null
                || decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Odyssey.Mechhive.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            CreatedEvent = CreateSoloEvent(
                sink,
                actor,
                null,
                payload.DefName,
                label,
                Fallback(),
                InteractionGroups.InstructionForGroup(group),
                OdysseyMechhiveContextFormatter.Format(facts, plan, policy));
            if (CreatedEvent != null) FinishCreatedEvent(sink);
        }

        private void FinishCreatedEvent(DiaryGameComponent sink)
        {
            try
            {
                sink.AddPreverifiedEventRef(actor, CreatedEvent.eventId, true);
                sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
            }
            catch (Exception exception)
            {
                // The repository already owns the fallback page. Letting this escape would release
                // the deferred generic Quest success and create a duplicate terminal chapter.
                Log.ErrorOnce(
                    "[Pawn Diary] Odyssey Mechhive page was created but its exact reference or "
                        + "generation handoff failed; the dedicated page remains authoritative: "
                        + exception,
                    "PawnDiary.Odyssey.Mechhive.PostCreate".GetHashCode());
            }
        }

        private string Fallback()
        {
            return plan.outcomeToken == OdysseyMechhiveOutcomeTokens.Scavenged
                ? "PawnDiary.Event.Odyssey.Mechhive.ScavengedFallback"
                    .Translate(actor.LabelShortCap).Resolve()
                : "PawnDiary.Event.Odyssey.Mechhive.DestroyedFallback"
                    .Translate(actor.LabelShortCap).Resolve();
        }

        private bool ActorMatches(string actorId)
        {
            return actor != null && !string.IsNullOrWhiteSpace(actorId)
                && string.Equals(actor.GetUniqueLoadID(), actorId, StringComparison.Ordinal);
        }
    }
}
