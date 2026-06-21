// Social interactions — the PlayLog.Add hook's diary flow, partially built on the Event Catalog
// pattern (see Source/Capture/). The Harmony patch (DiaryPatches.cs) forwards every social-log row
// here; RecordInteraction asks the catalog for the basic drop-gate (significance / no eligible
// pawn / user-disabled), then performs the solo-vs-pair-vs-batched dispatch locally.
//
// TODO(catalog): the shape dispatch (Solo when only one eligible / Pair when both eligible /
// Batched when the classified group has a batch policy and no promotion roll wins) reads impure
// state (group classification, RNG) and does not fit CaptureDecision cleanly. When per-source
// outcome enums or a batched value lands, move the dispatch into InteractionEventData.Decide and
// shrink this file.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Convenience overload that auto-generates a fallback description from the pawns and interaction label.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            string fallbackText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionDef.LabelCap.Resolve(), recipient.LabelShortCap);
            RecordInteraction(initiator, recipient, interactionDef, fallbackText);
        }

        /// <summary>
        /// Convenience overload that uses the same game text for both initiator and recipient.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string gameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, gameText, gameText);
        }

        /// <summary>
        /// Records one social interaction (the full entry point used by the Harmony patch). Skips
        /// it unless its group is enabled, then builds a pairwise <see cref="DiaryEvent"/> and
        /// queues generation. The shorter overloads above just fill in default text.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string initiatorGameText, string recipientGameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, initiatorGameText, recipientGameText, -1);
        }

        /// <summary>
        /// Records one social interaction and remembers the originating PlayLog id, so a later
        /// click in RimWorld's Social tab can jump back to the generated diary entry.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string initiatorGameText, string recipientGameText, int playLogEntryId)
        {
            if (!CanRecordGameplayEventNow() || initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(initiator);
            bool recipientEligible = IsDiaryEligible(recipient);

            // Snapshot for the catalog drop-gate. The IsSignificant flag and UserEnabled both
            // reflect the IsInteractionSignificant check above (which already gated on the user's
            // per-interaction toggle) — kept explicit here so the pure Decider exercises the same
            // gates with synthetic inputs in tests.
            InteractionEventData data = new InteractionEventData
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
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: initiatorEligible || recipientEligible,
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Interaction);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision == CaptureDecision.Drop)
            {
                return;
            }

            // catalog returned GenerateSolo as a "continue processing" signal (see file header
            // TODO). The Solo/Pair/Batched dispatch below is the impure part that still lives here.

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryContextBuilder.CleanLine(initiatorGameText);
            string recipientText = DiaryContextBuilder.CleanLine(recipientGameText);

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? initiator : recipient;
                Pawn otherPawn = initiatorEligible ? recipient : initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction".Translate(eligiblePawn.LabelShortCap, interactionLabel, otherPawn.LabelShortCap);
                }

                string gameContext = DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel);
                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, interactionDef.defName, interactionLabel,
                    eligibleText, InteractionInstruction(interactionDef), gameContext);
                soloEvent.AddPlayLogEntryId(playLogEntryId);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
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

            // Low-value groups normally batch. A promotion policy can let an individually-interesting
            // moment win a weighted-random roll and skip batching — falling through to the immediate
            // pairwise path below so it becomes its own diary event instead of daily filler.
            DiaryInteractionGroupDef batchGroup = BatchGroupFor(interactionDef);
            if (batchGroup != null && !ShouldPromoteInteraction(batchGroup, initiator, recipient))
            {
                RecordBatchedInteraction(batchGroup, initiator, recipient, interactionDef,
                    interactionLabel, initiatorText, recipientText, playLogEntryId);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionInstruction(interactionDef),
                DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel));
            diaryEvent.playLogInteractionDefName = interactionDef.defName;
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            QueuePairwiseGeneration(diaryEvent);
        }
    }
}
