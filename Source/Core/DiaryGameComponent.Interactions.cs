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
using Verse.Grammar;

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
        /// Returns whether the PlayLog capture hook may render RimWorld's POV text for this
        /// interaction. Most vanilla rows are safe to render. Some compatibility groups intentionally
        /// skip rendering because another mod's grammar renderer can have gameplay side effects.
        /// </summary>
        public bool ShouldRenderInteractionTextFromPlayLog(InteractionDef interactionDef)
        {
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            if (group != null && !group.captureRenderedGameText)
            {
                return false;
            }

            return !HasTaggedLogGrammar(interactionDef)
                || SpeakUpReplySchedulingGuardPatch.CanRenderTaggedGrammarSafely;
        }

        // Conversation-framework mods can attach grammar tags to social-log rules and use those
        // tags to schedule follow-up interactions while RimWorld renders the log text. Pawn Diary is
        // only observing the row, so it avoids rendering tagged grammar here and lets the recorder
        // fall back to neutral interaction text instead.
        private static bool HasTaggedLogGrammar(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return false;
            }

            try
            {
                return HasTaggedLogGrammar(interactionDef.logRulesInitiator, new HashSet<RulePack>());
            }
            catch
            {
                // If a third-party rule pack is malformed, prefer neutral fallback text over risking
                // an exception inside RimWorld's PlayLog.Add flow.
                return true;
            }
        }

        private static bool HasTaggedLogGrammar(RulePack rulePack, HashSet<RulePack> visited)
        {
            if (rulePack == null || visited == null || visited.Contains(rulePack))
            {
                return false;
            }

            visited.Add(rulePack);

            if (HasTaggedRule(rulePack.Rules)
                || HasTaggedRule(rulePack.UntranslatedRules))
            {
                return true;
            }

            if (rulePack.include == null)
            {
                return false;
            }

            for (int i = 0; i < rulePack.include.Count; i++)
            {
                RulePackDef included = rulePack.include[i];
                if (included != null
                    && (HasTaggedRule(included.RulesPlusIncludes)
                        || HasTaggedRule(included.UntranslatedRulesPlusIncludes)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTaggedRule(List<Rule> rules)
        {
            if (rules == null)
            {
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                Rule_String stringRule = rules[i] as Rule_String;
                if (stringRule != null && !string.IsNullOrWhiteSpace(stringRule.tag))
                {
                    return true;
                }
            }

            return false;
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
