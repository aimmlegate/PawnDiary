// Social interactions — the PlayLog.Add hook's diary flow. The Harmony patch
// (DiarySocialLogPatches.cs)
// forwards every social-log row here; RecordInteraction snapshots live RimWorld facts, asks the
// catalog for the final route (solo / pair / batch / ambient), then performs that side effect.
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
            DiaryInteractionGroupDef batchGroup = null;
            bool routeToBatch = false;
            bool routeToAmbient = false;
            if (initiatorEligible && recipientEligible)
            {
                // XML marks low-value groups as delayed batches. The promotion roll is intentionally
                // impure, so the adapter pre-computes the chosen route before calling the catalog.
                batchGroup = BatchGroupFor(interactionDef);
                if (batchGroup != null && !ShouldPromoteInteraction(batchGroup, initiator, recipient))
                {
                    routeToAmbient = batchGroup.batch != null
                        && batchGroup.batch.mode == InteractionBatchMode.AmbientDayNote;
                    routeToBatch = !routeToAmbient;
                }
            }

            // Snapshot for the catalog. The IsSignificant flag reflects the earlier
            // IsInteractionSignificant check, while the route flags capture XML batching policy plus
            // the impure promotion RNG result.
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
                RouteToBatch = routeToBatch,
                RouteToAmbient = routeToAmbient,
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

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryLineCleaner.CleanLine(initiatorGameText);
            string recipientText = DiaryLineCleaner.CleanLine(recipientGameText);

            if (decision == CaptureDecision.GenerateSolo)
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

            if (decision == CaptureDecision.RouteBatch || decision == CaptureDecision.RouteAmbient)
            {
                if (batchGroup != null)
                {
                    RecordBatchedInteraction(batchGroup, initiator, recipient, interactionDef,
                        interactionLabel, initiatorText, recipientText, playLogEntryId);
                }
                return;
            }

            if (decision != CaptureDecision.GeneratePair)
            {
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

        /// <summary>
        /// Cheap preflight used by the PlayLog.Add patch before it renders RimWorld grammar strings.
        /// The full recorder repeats these checks because settings/XML may change between call sites.
        /// </summary>
        public bool ShouldCaptureInteractionFromPlayLog(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            return CanRecordGameplayEventNow()
                && initiator != null
                && recipient != null
                && interactionDef != null
                && IsInteractionSignificant(interactionDef)
                && (IsDiaryEligible(initiator) || IsDiaryEligible(recipient));
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a specific interaction def, or empty string if none.
        /// </summary>
        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return InteractionGroups.InstructionFor(interactionDef);
        }

        /// <summary>
        /// An interaction is recorded only if its group (see InteractionGroups) is enabled in settings.
        /// </summary>
        private static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }
    }
}
