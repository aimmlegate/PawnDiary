// The queue-orchestration half of the generation pipeline: deciding what to (re)queue. The tick
// scan (QueueAllPendingGenerations) and the per-pawn re-enable scan (QueuePendingGenerationsForPawn)
// funnel through EnsureGenerationQueued, which routes each event to the right prompt (neutral
// death/arrival, sequential dual-POV pair, or a single POV rewrite). Orphan recovery re-queues
// entries stranded on "Generating" after a session restart. The actual prompt build + LLM dispatch
// lives in DiaryGameComponent.GenerationDispatch.cs; lane selection in DiaryGameComponent.ApiLanes.cs;
// eligibility/rule resolution in DiaryGameComponent.GenerationEligibility.cs.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (IsGenerationDelayed(diaryEvent, povRole))
            {
                return;
            }

            if (TryMarkIncapacitatedPovSkipped(diaryEvent, povRole, livePawnsById))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache))
            {
                return;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole) && diaryEvent.HasDeathDescription())
            {
                QueueDeathDescription(diaryEvent);
                return;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole) && diaryEvent.HasArrivalDescription())
            {
                QueueArrivalDescription(diaryEvent);
                return;
            }

            if (DiaryEvent.RoleIsInitiatorOrRecipient(povRole) && !diaryEvent.solo)
            {
                QueueSequentialPairwiseRewrite(diaryEvent, null, boundsCache, livePawnsById);
                return;
            }

            if (diaryEvent.CanQueueGeneration(povRole))
            {
                QueueLlmRewrite(diaryEvent, povRole, boundsCache, livePawnsById);
            }
        }

        private void QueueAllPendingGenerations()
        {
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = new Dictionary<string, DiaryBoundsCacheEntry>();
            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
                    }

                    continue;
                }

                if (diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.initiatorPawnId, boundsCache))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole, boundsCache, livePawnsById);
                }

                if (!diaryEvent.solo
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.recipientPawnId, boundsCache))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole, boundsCache, livePawnsById);
                }

            }

            QueueMissingTitles(boundsCache, livePawnsById);
        }

        /// <summary>
        /// Title requests are not persisted as live work; after load or after enabling title
        /// generation later, sweep completed entries and queue any missing titles once.
        /// </summary>
        private void QueueMissingTitles(Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null || !settings.generateTitles)
            {
                return;
            }

            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription() || diaryEvent.HasArrivalDescription())
                {
                    QueueMissingTitleForRole(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
                    continue;
                }

                QueueMissingTitleForRole(diaryEvent, DiaryEvent.InitiatorRole, boundsCache, livePawnsById);
                if (!diaryEvent.solo)
                {
                    QueueMissingTitleForRole(diaryEvent, DiaryEvent.RecipientRole, boundsCache, livePawnsById);
                }
            }
        }

        private void QueueMissingTitleForRole(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null
                || string.IsNullOrWhiteSpace(povRole)
                || !diaryEvent.HasGeneratedTextForRole(povRole)
                || !string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(povRole))
                || !diaryEvent.CanQueueTitleGeneration(povRole))
            {
                return;
            }

            QueueTitleRequest(diaryEvent, povRole, null, boundsCache, livePawnsById);
        }

        /// <summary>
        /// Re-queues diary entries stranded on "Generating": a POV marked pending whose background
        /// request is no longer in flight (e.g. it was cancelled by a session restart). Such an entry
        /// never recovers on its own, because CanQueueGeneration rejects the pending status, so
        /// QueueAllPendingGenerations skips it. We reset it to NotGenerated so the queue pass that runs
        /// right after re-drives it. Two guards keep this from ever double-sending real work:
        ///   * anything still in flight (its session-keyed request key is present) is left alone, and
        ///   * an entry must look orphaned on two consecutive scans before we touch it, so a request
        ///     that merely finished between scans — its result still waiting in the main-thread drain —
        ///     is never mistaken for an orphan.
        /// </summary>
        private void RecoverOrphanedPendingGenerations()
        {
            HashSet<string> orphansThisScan = new HashSet<string>();
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                CollectOrphanedPendingRole(diaryEvent, DiaryEvent.InitiatorRole, orphansThisScan);
                CollectOrphanedPendingRole(diaryEvent, DiaryEvent.RecipientRole, orphansThisScan);
                CollectOrphanedPendingRole(diaryEvent, DiaryEvent.NeutralRole, orphansThisScan);
            }

            orphanCandidatesLastScan = orphansThisScan;
        }

        /// <summary>
        /// Helper for <see cref="RecoverOrphanedPendingGenerations"/>: when the role looks orphaned
        /// (pending, not in flight), recover it if we also saw it orphaned on the previous scan,
        /// otherwise remember it as a candidate so a second sighting next scan can recover it.
        /// </summary>
        private void CollectOrphanedPendingRole(DiaryEvent diaryEvent, string povRole, HashSet<string> orphansThisScan)
        {
            if (!diaryEvent.IsPending(povRole) || LlmClient.IsInFlight(diaryEvent.eventId, povRole))
            {
                return;
            }

            string key = diaryEvent.eventId + "|" + povRole;
            if (orphanCandidatesLastScan.Contains(key))
            {
                diaryEvent.ResetPendingToNotGenerated(povRole);
                LogApiDebug("Recovered orphaned pending generation event=" + diaryEvent.eventId + " role=" + povRole);
            }
            else
            {
                orphansThisScan.Add(key);
            }
        }

        private void QueuePendingGenerationsForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null || diary.eventIds == null)
            {
                return;
            }

            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = new Dictionary<string, DiaryBoundsCacheEntry>();
            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent == null)
                {
                    continue;
                }

                if (EventFallsOutsideDiaryBoundsForPawn(diaryEvent, pawnId, boundsCache))
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.IsDeathDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
                    }

                    continue;
                }

                string povRole = diaryEvent.RoleForPawn(pawnId);
                EnsureGenerationQueued(diaryEvent, povRole, boundsCache, livePawnsById);
            }
        }

        /// <summary>
        /// Dispatches a pairwise event for LLM generation through the supported sequential POV flow.
        /// </summary>
        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            QueueSequentialPairwiseRewrite(diaryEvent);
        }

        /// <summary>
        /// Builds the prompt for a single POV role and enqueues the LLM request if generation is still allowed.
        /// </summary>
        private void QueueLlmRewrite(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (IsGenerationDelayed(diaryEvent, povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Persona and prompt enchantment are resolved at queue time so changing a pawn or XML
            // weights affects future generations without rewriting prompts already sent or saved
            // for debugging. The hidden per-entry humor cue is resolved the same way.
            DiaryPromptPlan promptPlan = DiaryPromptBuilder.BuildInteractionPromptPlan(
                diaryEvent,
                povRole,
                PersonaRuleFor(diaryEvent, povRole),
                PromptEnchantmentRuleFor(diaryEvent, povRole, livePawnsById),
                0,
                HumorCueFor(diaryEvent));
            QueuePrompt(diaryEvent, povRole, promptPlan, null, boundsCache);
        }

        /// <summary>
        /// Returns true while a freshly spawned ordinary raid is still in its XML-tuned anticipation
        /// window. The marker is transient: saved/reloaded games recover by queuing any unfinished
        /// generation normally, just like other not-yet-generated entries.
        /// </summary>
        private bool IsGenerationDelayed(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return false;
            }

            int readyTick;
            string key = DelayedGenerationKey(diaryEvent, povRole);
            if (!delayedRaidGenerationReadyTicks.TryGetValue(key, out readyTick))
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            if (now < readyTick)
            {
                return true;
            }

            delayedRaidGenerationReadyTicks.Remove(key);
            return false;
        }

        /// <summary>Stores a transient "do not queue this role until tick X" marker.</summary>
        private void DelayGenerationUntil(DiaryEvent diaryEvent, string povRole, int readyTick)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            delayedRaidGenerationReadyTicks[DelayedGenerationKey(diaryEvent, povRole)] = Math.Max(0, readyTick);
        }

        private static string DelayedGenerationKey(DiaryEvent diaryEvent, string povRole)
        {
            return diaryEvent.eventId + "|" + povRole;
        }

        /// <summary>
        /// Queues the persona-independent neutral description used for colonist deaths. This is not
        /// a first-person diary entry; it is a concise record of how the pawn died.
        /// </summary>
        private void QueueDeathDescription(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || !diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
            {
                return;
            }

            DiaryPromptPlan promptPlan = DiaryPromptBuilder.BuildDeathDescriptionPromptPlan(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, promptPlan);
        }

        /// <summary>
        /// Queues the persona-independent neutral description used for colony arrivals. This is a
        /// factual first page for the pawn's diary, not a first-person entry.
        /// </summary>
        private void QueueArrivalDescription(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || !diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
            {
                return;
            }

            DiaryPromptPlan promptPlan = DiaryPromptBuilder.BuildArrivalDescriptionPromptPlan(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, promptPlan);
        }

        /// <summary>
        /// Dual-POV flow: queues the initiator first, then the recipient once the initiator result arrives
        /// (so the recipient prompt can include the initiator's generated text as hidden continuity context).
        /// </summary>
        private void QueueSequentialPairwiseRewrite(DiaryEvent diaryEvent, ApiEndpointConfig recipientPrimaryOverride = null,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || diaryEvent.solo)
            {
                return;
            }

            TryMarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.InitiatorRole, livePawnsById);
            TryMarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.RecipientRole, livePawnsById);

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole, boundsCache);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole, boundsCache);
            bool initiatorSkipped = diaryEvent.IsSkipped(DiaryEvent.InitiatorRole);
            bool initiatorContextExpected = initiatorEnabled && !initiatorSkipped;

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                DiaryPromptPlan promptPlan = DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    PersonaRuleFor(diaryEvent, DiaryEvent.InitiatorRole),
                    PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.InitiatorRole, livePawnsById),
                    0,
                    HumorCueFor(diaryEvent));
                QueuePrompt(diaryEvent, DiaryEvent.InitiatorRole, promptPlan, null, boundsCache);
                return;
            }

            // If the recipient is disabled, stop here even if the initiator completed.
            if (!recipientEnabled)
            {
                return;
            }

            // Keep the old paired behavior when the initiator was supposed to generate but failed.
            if (initiatorContextExpected && string.Equals(diaryEvent.initiatorStatus, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }

                return;
            }

            // Wait for initiator context only when the initiator is enabled. If the initiator is
            // disabled, the recipient can still generate from the base event prompt.
            if (initiatorContextExpected
                && (!string.Equals(diaryEvent.initiatorStatus, DiaryEvent.CompleteStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(diaryEvent.initiatorGeneratedText)))
            {
                return;
            }

            if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
            {
                // Recipient prompt includes hidden initiator context only when that context exists.
                DiaryPromptPlan promptPlan = initiatorContextExpected
                    ? DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                        diaryEvent,
                        DiaryEvent.RecipientRole,
                        PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole),
                        PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.RecipientRole, livePawnsById),
                        0,
                        HumorCueFor(diaryEvent))
                    : DiaryPromptBuilder.BuildInteractionPromptPlan(
                        diaryEvent,
                        DiaryEvent.RecipientRole,
                        PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole),
                        PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.RecipientRole, livePawnsById),
                        0,
                        HumorCueFor(diaryEvent));
                QueuePrompt(diaryEvent, DiaryEvent.RecipientRole, promptPlan, recipientPrimaryOverride, boundsCache);
            }
        }
    }
}
