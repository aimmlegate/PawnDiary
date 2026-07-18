// Impure Odyssey landing adapter. The lifecycle component supplies a pure novelty plan plus the
// exact live Pawn references preserved across vanilla LandingEnded; this signal creates one canonical
// solo/pair DiaryEvent, snapshots localized prose and Narrative Continuity evidence, then queues it.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates at most one two-POV page for a novelty-authorized successful landing.</summary>
    internal sealed class GravshipJourneySignal : DiarySignal
    {
        private readonly OdysseyJourneySnapshot journey;
        private readonly OdysseyLocationSnapshot destination;
        private readonly OdysseyLandingPlan plan;
        private readonly OdysseyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly OdysseyWriterCandidate firstCandidate;
        private readonly OdysseyWriterCandidate secondCandidate;
        private readonly Pawn firstPawn;
        private readonly Pawn secondPawn;
        private readonly GravshipJourneyEventData payload;

        /// <summary>The durable event created by Emit, or null when runtime creation did not finish.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        public GravshipJourneySignal(
            OdysseyJourneySnapshot journey,
            OdysseyLocationSnapshot destination,
            OdysseyLandingPlan plan,
            OdysseyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            List<Pawn> livePawns)
        {
            this.journey = journey;
            this.destination = destination ?? journey?.destination;
            this.plan = plan;
            this.policy = policy ?? OdysseyPolicySnapshot.CreateDefault();
            this.group = group;
            firstCandidate = WriterAt(plan?.selectedWriters, 0);
            secondCandidate = WriterAt(plan?.selectedWriters, 1);
            firstPawn = FindLivePawn(livePawns, firstCandidate?.pawnId);
            secondPawn = FindLivePawn(livePawns, secondCandidate?.pawnId);
            bool firstEligible = IsLiveWriter(firstPawn, firstCandidate);
            bool secondEligible = IsLiveWriter(secondPawn, secondCandidate);
            payload = new GravshipJourneyEventData
            {
                PawnId = firstCandidate?.pawnId ?? secondCandidate?.pawnId ?? string.Empty,
                Tick = Math.Max(0, plan?.historyMutation?.landingObservationTick ?? 0),
                JourneyId = journey?.journeyId ?? string.Empty,
                ShipStableId = journey?.shipStableId ?? string.Empty,
                DepartureTick = journey?.departureTick ?? -1,
                HasValidPlan = plan != null && plan.historyMutation != null,
                WritePage = plan?.writePage == true,
                AlreadyRecorded = false,
                FirstWriterId = firstCandidate?.pawnId ?? string.Empty,
                FirstWriterEligible = firstEligible,
                SecondWriterId = secondCandidate?.pawnId ?? string.Empty,
                SecondWriterEligible = secondEligible
            };
            PreserveHistoricalOrdering(payload.Tick);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.FirstWriterEligible || payload.SecondWriterEligible,
                userEnabled: enabled,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => policy.staleJourneyRetentionTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || group == null
                || (decision != CaptureDecision.GenerateSolo && decision != CaptureDecision.GeneratePair))
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Odyssey.Landing.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGravshipJourney(group);
            string shipName = VisibleShipName();
            string placeName = VisibleDestinationName();

            if (decision == CaptureDecision.GeneratePair && firstPawn != null && secondPawn != null)
            {
                string context = OdysseyContextFormatter.FormatLandingPair(
                    journey,
                    destination,
                    plan,
                    firstCandidate?.pawnId,
                    secondCandidate?.pawnId,
                    policy);
                string firstText = PairText(firstPawn, firstCandidate, secondPawn, secondCandidate, shipName, placeName);
                string secondText = PairText(secondPawn, secondCandidate, firstPawn, firstCandidate, shipName, placeName);
                CreatedEvent = CreatePairwiseEvent(
                    sink,
                    firstPawn,
                    secondPawn,
                    GravshipJourneyEventData.DefName,
                    label,
                    firstText,
                    secondText,
                    instruction,
                    context);
                ApplyNarrativeEvidence(sink, CreatedEvent, firstPawn, DiaryEvent.InitiatorRole);
                ApplyNarrativeEvidence(sink, CreatedEvent, secondPawn, DiaryEvent.RecipientRole);
            }
            else
            {
                Pawn writer = firstPawn ?? secondPawn;
                OdysseyWriterCandidate candidate = firstPawn != null ? firstCandidate : secondCandidate;
                if (writer == null || candidate == null) return;
                string context = OdysseyContextFormatter.FormatLanding(
                    journey, destination, plan, candidate.pawnId, policy);
                string text = "PawnDiary.Event.Odyssey.Landing.Fallback"
                    .Translate(writer.LabelShortCap, shipName, placeName, RoleLabel(candidate.roleToken))
                    .Resolve();
                CreatedEvent = CreateSoloEvent(
                    sink,
                    writer,
                    null,
                    GravshipJourneyEventData.DefName,
                    label,
                    text,
                    instruction,
                    context);
                ApplyNarrativeEvidence(sink, CreatedEvent, writer, DiaryEvent.InitiatorRole);
            }

            if (CreatedEvent == null) return;
            try
            {
                if (CreatedEvent.solo)
                {
                    sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
                }
                else
                {
                    sink.QueuePair(CreatedEvent);
                }
            }
            catch (Exception exception)
            {
                // The durable event already owns this landing. Ordinary orphan recovery may retry
                // generation; never release a second landing owner merely because queueing failed.
                Log.ErrorOnce(
                    "[Pawn Diary] Odyssey landing page was created but its initial generation queue failed; "
                    + "normal recovery may retry it: " + exception,
                    "PawnDiary.OdysseyLanding.Queue".GetHashCode());
            }
        }

        private string PairText(
            Pawn writer,
            OdysseyWriterCandidate writerCandidate,
            Pawn other,
            OdysseyWriterCandidate otherCandidate,
            string shipName,
            string placeName)
        {
            return "PawnDiary.Event.Odyssey.Landing.PairFallback".Translate(
                writer.LabelShortCap,
                shipName,
                placeName,
                RoleLabel(writerCandidate?.roleToken),
                other.LabelShortCap,
                RoleLabel(otherCandidate?.roleToken)).Resolve();
        }

        private void ApplyNarrativeEvidence(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole)
        {
            if (sink == null || diaryEvent == null || povPawn == null || journey == null) return;
            try
            {
                string pawnId = povPawn.GetUniqueLoadID();
                bool returned = plan.primaryReason == OdysseyLandingReasonTokens.Homecoming
                    || plan.secondaryReason == OdysseyLandingReasonTokens.Homecoming;
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = povRole,
                        royalty = sink.RoyaltyNarrativeSnapshotFor(povPawn, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        evidence = OdysseyNarrativeEvidenceFactory.Landing(
                            diaryEvent.eventId,
                            diaryEvent.tick,
                            pawnId,
                            povRole,
                            journey.journeyId,
                            journey.shipStableId,
                            journey.shipName,
                            destination?.stableKey,
                            VisibleDestinationName(),
                            string.Empty,
                            GravshipJourneyEventData.DefName,
                            returned,
                            pawnCanKnow: true)
                    });
                if (result.evidence.Count > 0)
                {
                    diaryEvent.ApplyNarrativeContext(povRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Odyssey landing Narrative Continuity evidence failed; the landing page remains: "
                    + exception,
                    "PawnDiary.OdysseyLanding.NarrativeEvidence".GetHashCode());
            }
        }

        private string VisibleShipName()
        {
            string value = DiaryLineCleaner.CleanLine(journey?.shipName);
            return string.IsNullOrWhiteSpace(value)
                ? "PawnDiary.Event.Odyssey.ShipFallback".Translate().Resolve()
                : value;
        }

        private string VisibleDestinationName()
        {
            string value = destination != null && destination.visible
                ? DiaryLineCleaner.CleanLine(destination.visibleLabel)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(value)) value = DiaryLineCleaner.CleanLine(destination?.siteLabel);
            if (string.IsNullOrWhiteSpace(value)) value = DiaryLineCleaner.CleanLine(destination?.biomeLabel);
            return string.IsNullOrWhiteSpace(value)
                ? "PawnDiary.Event.Odyssey.DestinationFallback".Translate().Resolve()
                : value;
        }

        private static string RoleLabel(string roleToken)
        {
            string key = roleToken == OdysseyJourneyRoleTokens.Pilot
                ? "PawnDiary.Event.Odyssey.Role.Pilot"
                : roleToken == OdysseyJourneyRoleTokens.Copilot
                    ? "PawnDiary.Event.Odyssey.Role.Copilot"
                    : "PawnDiary.Event.Odyssey.Role.Crew";
            return key.Translate().Resolve();
        }

        private static OdysseyWriterCandidate WriterAt(List<OdysseyWriterCandidate> writers, int index)
        {
            return writers != null && index >= 0 && index < writers.Count ? writers[index] : null;
        }

        private static Pawn FindLivePawn(List<Pawn> pawns, string pawnId)
        {
            if (pawns == null || string.IsNullOrWhiteSpace(pawnId)) return null;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && string.Equals(pawn.GetUniqueLoadID(), pawnId, StringComparison.Ordinal))
                {
                    return pawn;
                }
            }

            return null;
        }

        private static bool IsLiveWriter(Pawn pawn, OdysseyWriterCandidate candidate)
        {
            return pawn != null && candidate != null && candidate.eligible && candidate.present
                && candidate.hasDiary && DiaryGameComponent.IsDiaryEligible(pawn);
        }
    }
}
