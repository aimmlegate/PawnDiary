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
using PawnDiary.Capture;
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

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache, livePawnsById))
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
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
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

                TryRestorePermanentBodyChangeIncapacitationSkip(
                    diaryEvent, DiaryEvent.InitiatorRole);
                if (diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.initiatorPawnId, boundsCache, livePawnsById))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole, boundsCache, livePawnsById);
                }

                TryRestorePermanentBodyChangeIncapacitationSkip(
                    diaryEvent, DiaryEvent.RecipientRole);
                if (!diaryEvent.solo
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.recipientPawnId, boundsCache, livePawnsById))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole, boundsCache, livePawnsById);
                }
            }

        }

        /// <summary>
        /// Title requests are not persisted as live work. Normal successful main-entry results queue
        /// their own title immediately; this one-shot sweep is only for load/settings catch-up, not
        /// the recurring generation scanner.
        /// </summary>
        private void QueueMissingTitles(Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null || !settings.generateTitles)
            {
                return;
            }

            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
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
        /// Public settings hook: when the player enables title generation later, queue titles for
        /// completed active entries once instead of scanning every generation tick forever.
        /// </summary>
        internal void QueueMissingTitlesFromSettings()
        {
            try
            {
                QueueMissingTitles();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Missing-title catch-up failed: " + e,
                    "DiaryGameComponent.QueueMissingTitlesFromSettings".GetHashCode());
            }
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
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
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
                NotifyEntryStatusChanged(diaryEvent, povRole);
                RequestGenerationScan();
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.LlmPendingRecovered,
                    "llm.orphan_recovery",
                    povRole,
                    diaryEvent.interactionDefName,
                    diaryEvent.tick);
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
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            for (int i = 0; i < activeEvents.Count; i++)
            {
                DiaryEvent diaryEvent = activeEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (EventFallsOutsideDiaryBoundsForPawn(diaryEvent, pawnId, boundsCache, livePawnsById))
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

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache, livePawnsById))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Quality Wave H1. This is the ONE funnel every solo page passes through: the periodic
            // scanner reaches it via EnsureGenerationQueued, and a signal's own emit reaches it via
            // QueueSolo. The raid source uses BOTH — an ordinary walk-in raid takes the delayed path,
            // while a drop-pod raid or infestation queues straight from its emit tick — so the beats
            // gate has to live here or those immediate-threat pages would never wait for their fight.
            if (!TryPrepareBattleBeats(diaryEvent, povRole, livePawnsById))
            {
                return;
            }

            // The routing probe is read-only. After dispatch knows the selected/failover presets, its
            // preparation callback stages writing style and only rolls a psychotype when some real variant
            // can render one. Prompt enchantment and humor remain stable across all rendered variants.
            string personaRule = PersonaRuleFor(diaryEvent, povRole, livePawnsById, false);
            string promptEnchantment = PromptEnchantmentRuleFor(
                diaryEvent,
                povRole,
                livePawnsById,
                PromptContextDetailLevel.Full);
            string humorCue = HumorCueFor(diaryEvent, povRole, livePawnsById);
            QueuePrompt(
                diaryEvent,
                povRole,
                level => DiaryPromptBuilder.BuildInteractionPromptPlan(
                    diaryEvent,
                    povRole,
                    personaRule,
                    PsychotypeRuleFor(diaryEvent, povRole, livePawnsById, false, level),
                    promptEnchantment,
                    0,
                    humorCue,
                    level),
                null,
                boundsCache,
                livePawnsById,
                (level, anyVariantAllowsPsychotypes) =>
                {
                    PrepareVoiceStageForPromptVariants(
                        diaryEvent,
                        povRole,
                        livePawnsById,
                        anyVariantAllowsPsychotypes);
                    personaRule = PersonaRuleFor(diaryEvent, povRole, livePawnsById, false);
                    ApplyPromptAntiRepeatGuard(
                        diaryEvent,
                        povRole,
                        level,
                        (enchantment, humor) => DiaryPromptBuilder.BuildInteractionPromptPlan(
                            diaryEvent,
                            povRole,
                            personaRule,
                            PsychotypeRuleFor(diaryEvent, povRole, livePawnsById, false, level),
                            enchantment,
                            0,
                            humor,
                            level),
                        livePawnsById,
                        ref promptEnchantment,
                        ref humorCue);
                });
        }

        /// <summary>
        /// Quality Wave H1. Before a raid page is queued, mine RimWorld's combat log for the strongest
        /// moment or two of the POV pawn's own fight and freeze them onto the saved context. Returns
        /// false when the caller must NOT queue yet, because the fight is still unresolved; a retry
        /// tick is stamped so the generation scanner comes back. Every other event returns true
        /// immediately, so this costs one context-marker probe on the normal path.
        ///
        /// The "checked" marker is written into the SAVED context rather than a session set on
        /// purpose: without it, loading a game would restart mining on a raid whose combat log has
        /// long since been pruned, and the page would retry until its deadline every single load.
        /// </summary>
        private bool TryPrepareBattleBeats(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, Pawn> livePawnsById)
        {
            // Raid pages are solo, so only the initiator can own this work.
            if (!DiaryEvent.RoleEquals(povRole, DiaryEvent.InitiatorRole)
                || !DiaryContextFields.HasField(diaryEvent.gameContext, RaidEventData.RaidContextKey)
                || !diaryEvent.IsImportant()
                || BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext))
            {
                // Non-important friendly arrivals use SoloDefault, which does not project
                // battle_beats. Do not delay or mine context the selected prompt cannot consume.
                return true;
            }

            DiaryTuningDef tuning = DiaryTuning.Current;
            if (tuning == null || !tuning.battleBeatsEnabled)
            {
                // An off switch is not a completed scan. Leave the saved marker absent so enabling the
                // feature later in the same save can still mine an eligible pending raid page.
                return true;
            }

            Pawn pov = FindLivePawnByLoadId(diaryEvent.initiatorPawnId, livePawnsById);
            if (pov == null)
            {
                // The pawn is no longer available and no POV text could be rendered. Record that mining
                // ran so the page never re-enters this path, and queue normally.
                diaryEvent.gameContext = BattleBeatsPolicy.ApplyToContext(diaryEvent.gameContext, string.Empty);
                return true;
            }

            int now = Find.TickManager.TicksGame;
            BattleBeatsInspection inspection;
            try
            {
                inspection = BattleBeatsBuilder.Inspect(
                    pov,
                    DiaryContextFields.Value(diaryEvent.gameContext, RaidEventData.FactionContextKey),
                    diaryEvent.tick,
                    now,
                    tuning);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Battle-beats mining failed: " + e,
                    "DiaryGameComponent.TryPrepareBattleBeats".GetHashCode());
                diaryEvent.gameContext = BattleBeatsPolicy.ApplyToContext(diaryEvent.gameContext, string.Empty);
                return true;
            }

            BattleBeatsDecision decision = BattleBeatsPolicy.Decide(
                inspection.battleFound,
                inspection.latestRelevantGameTick,
                diaryEvent.tick,
                now,
                tuning.battleBeatsQuietTicks,
                tuning.battleBeatsMaxAgeTicks);
            if (decision == BattleBeatsDecision.Retry)
            {
                // Reuses the raid anticipation delay's transient marker, so the existing scan-request
                // plumbing brings us back without a second scheduling mechanism.
                DelayGenerationUntil(diaryEvent, povRole,
                    now + Math.Max(1, tuning.battleBeatsRetryIntervalTicks));
                return false;
            }

            string beats = BattleBeatsPolicy.FormatBeats(
                BattleBeatsPolicy.Select(
                    inspection.candidates,
                    tuning.battleBeatsMaxCount,
                    tuning.battleBeatsScores,
                    0),
                tuning.battleBeatsMaxChars);
            diaryEvent.gameContext = BattleBeatsPolicy.ApplyToContext(diaryEvent.gameContext, beats);
            return true;
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
                RequestGenerationScan();
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
            RequestGenerationScan();
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

            QueuePrompt(
                diaryEvent,
                DiaryEvent.NeutralRole,
                level => DiaryPromptBuilder.BuildDeathDescriptionPromptPlan(diaryEvent, 0, level));
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

            QueuePrompt(
                diaryEvent,
                DiaryEvent.NeutralRole,
                level => DiaryPromptBuilder.BuildArrivalDescriptionPromptPlan(diaryEvent, 0, level));
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

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole, boundsCache, livePawnsById);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole, boundsCache, livePawnsById);
            bool initiatorSkipped = diaryEvent.IsSkipped(DiaryEvent.InitiatorRole);
            bool initiatorContextExpected = initiatorEnabled && !initiatorSkipped;

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                string personaRule = PersonaRuleFor(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    livePawnsById,
                    false);
                string promptEnchantment = PromptEnchantmentRuleFor(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    livePawnsById,
                    PromptContextDetailLevel.Full);
                string humorCue = HumorCueFor(diaryEvent, DiaryEvent.InitiatorRole, livePawnsById);
                QueuePrompt(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    level => DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                        diaryEvent,
                        DiaryEvent.InitiatorRole,
                        personaRule,
                        PsychotypeRuleFor(
                            diaryEvent,
                            DiaryEvent.InitiatorRole,
                            livePawnsById,
                            false,
                            level),
                        promptEnchantment,
                        0,
                        humorCue,
                        level),
                    null,
                    boundsCache,
                    livePawnsById,
                    (level, anyVariantAllowsPsychotypes) =>
                    {
                        PrepareVoiceStageForPromptVariants(
                            diaryEvent,
                            DiaryEvent.InitiatorRole,
                            livePawnsById,
                            anyVariantAllowsPsychotypes);
                        personaRule = PersonaRuleFor(
                            diaryEvent,
                            DiaryEvent.InitiatorRole,
                            livePawnsById,
                            false);
                        ApplyPromptAntiRepeatGuard(
                            diaryEvent,
                            DiaryEvent.InitiatorRole,
                            level,
                            (enchantment, humor) => DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                                diaryEvent,
                                DiaryEvent.InitiatorRole,
                                personaRule,
                                PsychotypeRuleFor(
                                    diaryEvent,
                                    DiaryEvent.InitiatorRole,
                                    livePawnsById,
                                    false,
                                    level),
                                enchantment,
                                0,
                                humor,
                                level),
                            livePawnsById,
                            ref promptEnchantment,
                            ref humorCue);
                    });
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
                    NotifyEntryStatusChanged(diaryEvent, DiaryEvent.RecipientRole);
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
                string personaRule = PersonaRuleFor(
                    diaryEvent,
                    DiaryEvent.RecipientRole,
                    livePawnsById,
                    false);
                string promptEnchantment = PromptEnchantmentRuleFor(
                    diaryEvent,
                    DiaryEvent.RecipientRole,
                    livePawnsById,
                    PromptContextDetailLevel.Full);
                string humorCue = HumorCueFor(diaryEvent, DiaryEvent.RecipientRole, livePawnsById);
                QueuePrompt(
                    diaryEvent,
                    DiaryEvent.RecipientRole,
                    level => initiatorContextExpected
                        ? DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            personaRule,
                            PsychotypeRuleFor(
                                diaryEvent,
                                DiaryEvent.RecipientRole,
                                livePawnsById,
                                false,
                                level),
                            promptEnchantment,
                            0,
                            humorCue,
                            level)
                        : DiaryPromptBuilder.BuildInteractionPromptPlan(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            personaRule,
                            PsychotypeRuleFor(
                                diaryEvent,
                                DiaryEvent.RecipientRole,
                                livePawnsById,
                                false,
                                level),
                            promptEnchantment,
                            0,
                            humorCue,
                            level),
                    recipientPrimaryOverride,
                    boundsCache,
                    livePawnsById,
                    (level, anyVariantAllowsPsychotypes) =>
                    {
                        PrepareVoiceStageForPromptVariants(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            livePawnsById,
                            anyVariantAllowsPsychotypes);
                        personaRule = PersonaRuleFor(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            livePawnsById,
                            false);
                        ApplyPromptAntiRepeatGuard(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            level,
                            (enchantment, humor) => initiatorContextExpected
                                ? DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                                    diaryEvent,
                                    DiaryEvent.RecipientRole,
                                    personaRule,
                                    PsychotypeRuleFor(
                                        diaryEvent,
                                        DiaryEvent.RecipientRole,
                                        livePawnsById,
                                        false,
                                        level),
                                    enchantment,
                                    0,
                                    humor,
                                    level)
                                : DiaryPromptBuilder.BuildInteractionPromptPlan(
                                    diaryEvent,
                                    DiaryEvent.RecipientRole,
                                    personaRule,
                                    PsychotypeRuleFor(
                                        diaryEvent,
                                        DiaryEvent.RecipientRole,
                                        livePawnsById,
                                        false,
                                        level),
                                    enchantment,
                                    0,
                                    humor,
                                    level),
                            livePawnsById,
                            ref promptEnchantment,
                            ref humorCue);
                    });
            }
        }
    }
}
