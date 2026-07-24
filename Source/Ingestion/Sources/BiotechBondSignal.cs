// Biotech psychic-bond ingestion signal. The Harmony owner supplies a canonical sorted pair and
// verified before/after truth; this adapter chooses pair/solo writers and creates one catalog page.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Carries one verified psychic-bond formation or rupture through shared dispatch.</summary>
    internal sealed class BiotechBondSignal : DiarySignal
    {
        private const string GroupKey = "biotechPsychicBondLifecycle";
        private readonly BiotechBondEventData payload;
        private readonly PsychicBondMutationSnapshot mutation;
        private readonly Pawn firstPawn;
        private readonly Pawn secondPawn;

        internal BiotechBondSignal(
            BiotechBondEventData payload,
            PsychicBondMutationSnapshot mutation,
            Pawn firstPawn,
            Pawn secondPawn)
        {
            this.payload = payload;
            this.mutation = mutation;
            this.firstPawn = firstPawn;
            this.secondPawn = secondPawn;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(GroupKey);
            bool enabled = group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload != null
                    && (payload.FirstPawnEligible || payload.SecondPawnEligible),
                userEnabled: enabled,
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression),
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload?.DedupKey() ?? string.Empty;

        public override int DedupWindowTicks =>
            DiaryBiotechPolicy.Snapshot().bondDeathrest.psychicBondCorrelationExpiryTicks;

        public override void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
            if (payload != null && mutation != null)
            {
                sink.CaptureEventKnowledgeWithoutPage(
                    firstPawn, secondPawn, payload.DefName, BuildGameContext(), payload.Tick);
            }
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || mutation == null || payload == null
                || firstPawn == null || secondPawn == null
                || (decision != CaptureDecision.GeneratePair
                    && decision != CaptureDecision.GenerateSolo))
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(GroupKey);
            string label = group == null || string.IsNullOrWhiteSpace(group.label)
                ? LabelKey().Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForProgression(group);
            string firstText = TextKey().Translate(firstPawn.LabelShortCap, secondPawn.LabelShortCap)
                .Resolve();
            string secondText = TextKey().Translate(secondPawn.LabelShortCap, firstPawn.LabelShortCap)
                .Resolve();
            string gameContext = BuildGameContext();

            DiaryEvent diaryEvent;
            if (decision == CaptureDecision.GeneratePair)
            {
                diaryEvent = sink.AddPairwiseEvent(
                    firstPawn,
                    secondPawn,
                    payload.DefName,
                    label,
                    firstText,
                    secondText,
                    instruction,
                    gameContext,
                    mutation.observedTick);
                ApplyNarrativeEvidence(
                    sink,
                    diaryEvent,
                    firstPawn,
                    DiaryEvent.InitiatorRole,
                    secondPawn);
                ApplyNarrativeEvidence(
                    sink,
                    diaryEvent,
                    secondPawn,
                    DiaryEvent.RecipientRole,
                    firstPawn);
                sink.QueuePair(diaryEvent);
                return;
            }

            bool firstEligible = payload.FirstPawnEligible;
            Pawn writer = firstEligible ? firstPawn : secondPawn;
            Pawn partner = firstEligible ? secondPawn : firstPawn;
            string writerText = firstEligible ? firstText : secondText;
            diaryEvent = sink.AddSoloEvent(
                writer,
                partner,
                payload.DefName,
                label,
                writerText,
                instruction,
                gameContext,
                mutation.observedTick);
            ApplyNarrativeEvidence(
                sink,
                diaryEvent,
                writer,
                DiaryEvent.InitiatorRole,
                partner);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>Adds one exact pair/epoch N3-B lens for this POV without inferring relationship.</summary>
        private void ApplyNarrativeEvidence(
            DiaryGameComponent sink,
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            Pawn partner)
        {
            if (sink == null || diaryEvent == null || povPawn == null || partner == null) return;
            PsychicBondPair pair = mutation.Pair;
            string arcKey = PsychicBondPairPolicy.ArcKey(pair, mutation.bondEpoch);
            if (arcKey.Length == 0) return;
            try
            {
                string povPawnId = povPawn.GetUniqueLoadID();
                string partnerId = partner.GetUniqueLoadID();
                string phaseText = (mutation.phase == PsychicBondPhaseTokens.Formed
                    ? "PawnDiary.Event.Biotech.Bond.Formed.Phase"
                    : "PawnDiary.Event.Biotech.Bond.Ruptured.Phase").Translate().Resolve();
                string narrativeText = FormatNarrative(
                    DiaryBiotechPolicy.Snapshot().psychicBondNarrativeFormat,
                    mutation.firstPawnName,
                    mutation.secondPawnName,
                    phaseText);
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = povPawnId,
                        povRole = povRole,
                        recentSelectedCandidateKeys =
                            sink.RecentNarrativeSelectedCandidateKeys(povPawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel
                                ?? PromptContextDetailLevel.Full),
                        royalty = sink.RoyaltyNarrativeSnapshotFor(povPawn, diaryEvent.tick),
                        biotech = new BiotechNarrativeSnapshot
                        {
                            providerAvailable = ModsConfig.BiotechActive,
                            povPawnId = povPawnId,
                            bondPartnerId = partnerId,
                            bondArcKey = arcKey,
                            bondPhase = mutation.phase,
                            bondText = narrativeText,
                            sourceTick = diaryEvent.tick,
                            pawnCanKnow = true,
                            hasVerifiedPovConnection = true
                        },
                        odyssey = sink.OdysseyNarrativeSnapshotFor(povPawn, diaryEvent.tick),
                        evidence = new List<NarrativeEvidence>
                        {
                            new NarrativeEvidence
                            {
                                facet = NarrativeFacetTokens.BondLifecycle,
                                phase = mutation.phase,
                                subjectKind = NarrativeSubjectKindTokens.Pawn,
                                subjectId = partnerId,
                                subjectLabel = partner.LabelShortCap,
                                arcKey = arcKey,
                                beliefTopics = new List<string> { "bonding", "psychic" },
                                salience = NarrativeSalienceTokens.Meaningful,
                                pawnCanKnow = true,
                                sourceDomain = "biotech_psychic_bond",
                                sourceDefName = payload.DefName
                            }
                        }
                    });
                if (result.evidence.Count > 0)
                    diaryEvent.ApplyNarrativeContext(povRole, result);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Biotech psychic-bond Narrative Continuity evidence failed; "
                    + "the bond page remains: " + exception,
                    "PawnDiary.BiotechPsychicBond.NarrativeEvidence".GetHashCode());
            }
        }

        private static string FormatNarrative(string format, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;
            try
            {
                return DiaryLineCleaner.CleanLine(string.Format(format, values));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private string BuildGameContext()
        {
            StringBuilder context = new StringBuilder();
            Append(context, BiotechBondDeathrestContextKeys.BondPhase, mutation.phase);
            Append(context, BiotechBondDeathrestContextKeys.BondFirstPawnId, mutation.firstPawnId);
            Append(context, BiotechBondDeathrestContextKeys.BondFirstPawnName, mutation.firstPawnName);
            Append(context, BiotechBondDeathrestContextKeys.BondSecondPawnId, mutation.secondPawnId);
            Append(context, BiotechBondDeathrestContextKeys.BondSecondPawnName, mutation.secondPawnName);
            Append(context, BiotechBondDeathrestContextKeys.BondEpoch, mutation.bondEpoch.ToString());
            if (PsychicBondCauseTokens.IsPromptSafe(mutation.cause))
            {
                Append(context, BiotechBondDeathrestContextKeys.Cause, mutation.cause);
            }
            return context.ToString();
        }

        private string LabelKey()
        {
            return mutation.phase == PsychicBondPhaseTokens.Formed
                ? "PawnDiary.Event.Biotech.Bond.Formed.Label"
                : "PawnDiary.Event.Biotech.Bond.Ruptured.Label";
        }

        private string TextKey()
        {
            return mutation.phase == PsychicBondPhaseTokens.Formed
                ? "PawnDiary.Event.Biotech.Bond.Formed.Text"
                : "PawnDiary.Event.Biotech.Bond.Ruptured.Text";
        }

        private static void Append(StringBuilder context, string key, string value)
        {
            if (context == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;
            if (context.Length > 0) context.Append("; ");
            context.Append(key).Append('=').Append(DiaryLineCleaner.CleanLine(value));
        }
    }
}
