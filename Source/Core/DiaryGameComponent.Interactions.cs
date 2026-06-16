// Social interactions — the PlayLog.Add hook's diary flow. The Harmony patch (DiaryPatches.cs)
// forwards every social-log row here; these RecordInteraction overloads classify it (skip
// insignificant groups, check per-pawn eligibility), build the fallback text, and hand off to a
// pairwise/solo DiaryEvent — or, when the classified group has an XML <batch> policy, into the
// interaction batcher (RecordBatchedInteraction lives in DiaryGameComponent.InteractionBatching.cs).
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
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
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(initiator);
            bool recipientEligible = IsDiaryEligible(recipient);

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
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            QueuePairwiseGeneration(diaryEvent);
        }
    }
}
