// Interaction ingestion signal — the impure capture+emit half of the "social interaction" source
// (PlayLog.Add). Replaces the old DiaryGameComponent.RecordInteraction. Routes per the catalog:
// GenerateSolo (one eligible pawn), GeneratePair (both pawns, both POV), or RouteBatch/RouteAmbient
// (low-value groups merged into a delayed/day batch). Carries the originating PlayLog id so a click in
// RimWorld's Social tab can jump back to the generated entry, and stamps playLogInteractionDefName on
// pair entries so the generated-speech row override can find them.
//
// The patch-side preflight (ShouldCaptureInteractionFromPlayLog / ShouldRenderInteractionTextFromPlayLog)
// stays on the component. Pure decision + game-context format live in
// Source/Capture/Events/InteractionEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one social interaction row and emits it via the route the catalog chose. Built by
    /// <see cref="PlayLogAddPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class InteractionSignal : DiarySignal
    {
        private readonly Pawn initiator;
        private readonly Pawn recipient;
        private readonly InteractionDef interactionDef;
        private readonly string initiatorGameText;
        private readonly string recipientGameText;
        private readonly int playLogEntryId;
        private readonly bool initiatorEligible;
        private readonly bool recipientEligible;
        private readonly DiaryInteractionGroupDef batchGroup;
        private readonly InteractionEventData payload;

        public InteractionSignal(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string initiatorGameText, string recipientGameText, int playLogEntryId)
        {
            this.initiator = initiator;
            this.recipient = recipient;
            this.interactionDef = interactionDef;
            this.initiatorGameText = initiatorGameText;
            this.recipientGameText = recipientGameText;
            this.playLogEntryId = playLogEntryId;

            if (!DiaryGameComponent.GamePlaying || initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!DiaryGameComponent.IsInteractionSignificant(interactionDef))
            {
                return;
            }

            initiatorEligible = DiaryGameComponent.IsDiaryEligible(initiator);
            recipientEligible = DiaryGameComponent.IsDiaryEligible(recipient);

            bool routeToBatch = false;
            bool routeToAmbient = false;
            if (initiatorEligible && recipientEligible)
            {
                // XML marks low-value groups as delayed batches. The promotion roll is intentionally
                // impure, so the chosen route is pre-computed here before the catalog decides.
                batchGroup = DiaryGameComponent.BatchGroupFor(interactionDef);
                if (batchGroup != null && !DiaryGameComponent.ShouldPromoteInteraction(batchGroup, initiator, recipient))
                {
                    routeToAmbient = batchGroup.batch != null
                        && batchGroup.batch.mode == InteractionBatchMode.AmbientDayNote;
                    routeToBatch = !routeToAmbient;
                }
            }

            payload = new InteractionEventData
            {
                PawnId = initiatorEligible ? initiator.GetUniqueLoadID() : recipient.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = interactionDef.defName,
                Label = interactionDef.LabelCap.Resolve(),
                InitiatorPawnId = initiator.GetUniqueLoadID(),
                RecipientPawnId = recipient.GetUniqueLoadID(),
                InitiatorEligible = initiatorEligible,
                RecipientEligible = recipientEligible,
                IsSignificant = true,
                RouteToBatch = routeToBatch,
                RouteToAmbient = routeToAmbient,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: initiatorEligible || recipientEligible,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        // Interaction has no dedup (DedupKey empty → dispatcher skips the dedup gate), matching the old
        // RecordInteraction.

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            // Pure routing (unit-tested in DiaryCapturePolicyTests); this method renders the live text
            // and drives the sink for the chosen shape.
            InteractionEventData.InteractionEmitShape shape = InteractionEventData.PlanEmit(decision);
            if (shape == InteractionEventData.InteractionEmitShape.Drop)
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryLineCleaner.CleanLine(initiatorGameText);
            string recipientText = DiaryLineCleaner.CleanLine(recipientGameText);

            if (shape == InteractionEventData.InteractionEmitShape.Solo)
            {
                Pawn eligiblePawn = initiatorEligible ? initiator : recipient;
                Pawn otherPawn = initiatorEligible ? recipient : initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction".Translate(eligiblePawn.LabelShortCap, interactionLabel, otherPawn.LabelShortCap);
                }

                string gameContext = DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel);
                DiaryEvent soloEvent = sink.AddSoloEvent(eligiblePawn, otherPawn, interactionDef.defName, interactionLabel,
                    eligibleText, InteractionGroups.InstructionFor(interactionDef), gameContext);
                soloEvent.AddPlayLogEntryId(playLogEntryId);
                sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionLabel, recipient.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            if (shape == InteractionEventData.InteractionEmitShape.Batch)
            {
                if (batchGroup != null)
                {
                    sink.RecordBatchedInteraction(batchGroup, initiator, recipient, interactionDef,
                        interactionLabel, initiatorText, recipientText, playLogEntryId);
                }
                return;
            }

            // Pair.
            DiaryEvent diaryEvent = sink.AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionGroups.InstructionFor(interactionDef),
                DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel));
            diaryEvent.playLogInteractionDefName = interactionDef.defName;
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            sink.QueuePair(diaryEvent);
        }
    }
}
