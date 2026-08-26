// The impure transport end of the generation pipeline. QueuePrompt is the single choke point that
// stamps the planned prompt on the event, records endpoint metadata, marks it queued, and enqueues
// the request to LlmClient. ApplyLlmResult dequeues a finished result and writes it back onto the
// DiaryEvent, then kicks off the recipient half of a paired event and the small title follow-up.
// Prompt-test mode (capture the prompt without calling the API) is detected here.
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
        // Max tokens the title follow-up is allowed to emit. A title is only a few words, and this
        // cap is generous for a chat-style subject plus a stray word or two while keeping the call
        // cheap when the title toggle is on. Reused from the same field on the main-entry
        // request — we do NOT add a player setting for it.
        private const int TitleMaxTokens = 40;
        private const string PromptTestEndpointLabel = "prompt-test-mode";

        private delegate DiaryPromptPlan PromptPlanFactory(PromptContextDetailLevel contextDetailLevel);

        private static bool PromptTestModeEnabled()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.promptTestMode;
        }

        /// <summary>
        /// Final impure step before LLM dispatch: stamps the planned prompt on the event, records
        /// endpoint metadata, marks the event queued, and enqueues the request to <see cref="LlmClient"/>.
        /// </summary>
        private void QueuePrompt(DiaryEvent diaryEvent, string povRole, PromptPlanFactory promptPlanFactory,
            ApiEndpointConfig primaryOverride = null, Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null,
            Action<PromptContextDetailLevel, bool> prepareSelectedPlan = null,
            SavedActiveLogicalRequestV1 stagedMemoryRequest = null,
            bool allowMemoryRecall = true)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole) || promptPlanFactory == null)
            {
                return;
            }

            bool suppressRecallV2ForQueue = false;
            PromptPlanFactory effectivePromptPlanFactory = level =>
            {
                if (MemorySystemActivationGate.IsCurrentRelease && allowMemoryRecall)
                {
                    if (suppressRecallV2ForQueue)
                    {
                        PrepareMemoryRecallV2BackgroundOnly(diaryEvent, povRole, level);
                    }
                    else
                    {
                        PrepareMemoryRecallV2Projection(diaryEvent, povRole, level);
                    }
                }
                return promptPlanFactory(level);
            };

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache, livePawnsById))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Fetch settings once into a local so the method operates on one consistent snapshot
            // (matching QueueTitleRequest) instead of reaching the global static at every step.
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoLlmSettings".Translate());
                NotifyEntryStatusChanged(diaryEvent, povRole);
                return;
            }

            // Build a read-only Full plan first to resolve template choice and forced-model routing metadata.
            // First-person factories must not stamp voice state here: no effective API lane is known yet.
            // After lane selection we pre-render one prompt variant per effective context preset so
            // failover lanes can honor their own overrides without touching game state off-thread.
            DiaryPromptPlan routingPlan = effectivePromptPlanFactory(PromptContextDetailLevel.Full);
            if (routingPlan == null)
            {
                return;
            }

            if (PromptTestModeEnabled())
            {
                PromptContextDetailLevel testLevel = PawnDiarySettings.NormalizeContextDetailLevel(settings.contextDetailLevel);
                prepareSelectedPlan?.Invoke(
                    testLevel,
                    PromptContextFeaturePolicy.AllowsPsychotypes(testLevel));
                if (prepareSelectedPlan != null)
                {
                    // The preparation hook may persist a new instruction/tone reroll. Rebuild the
                    // routing copy so prompt-test capture sees the same final event state.
                    routingPlan = effectivePromptPlanFactory(PromptContextDetailLevel.Full);
                    if (routingPlan == null)
                    {
                        return;
                    }
                }

                DiaryPromptPlan testPlan = testLevel == PromptContextDetailLevel.Full
                    ? routingPlan
                    : effectivePromptPlanFactory(testLevel);
                if (testPlan == null)
                {
                    return;
                }

                string testRawText = testPlan.userPrompt ?? string.Empty;
                diaryEvent.SetPrompt(povRole, DiaryPromptCapture.Format(testPlan.systemPrompt, testRawText));
                diaryEvent.SetLlmMeta(povRole, PromptTestEndpointLabel, string.Empty);
                diaryEvent.MarkPromptOnly(povRole, "PawnDiary.Error.PromptTestModeCaptured".Translate());
                NotifyEntryStatusChanged(diaryEvent, povRole);
                LogApiDebug("Captured prompt without generation event=" + diaryEvent.eventId + " role=" + povRole);
                return;
            }

            // Pick which configured API lane handles this request. New events spread across all
            // lanes (parallelism); the recipient half of a paired event reuses the initiator's lane
            // so a sequential pair stays on one model.
            List<ApiEndpointConfig> targets = settings.ActiveEndpoints();
            LogApiLaneConfiguration(settings, targets);
            if (targets.Count == 0)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoApiConfigured".Translate());
                NotifyEntryStatusChanged(diaryEvent, povRole);
                return;
            }

            string selectionReason;
            bool forcePrimaryLane;
            ApiEndpointConfig target = SelectApiTarget(diaryEvent, povRole, targets, primaryOverride,
                routingPlan.forcedModelName, settings.apiRoutingMode, out selectionReason, out forcePrimaryLane);
            List<ApiEndpointConfig> failoverTargets = BuildFailoverTargets(targets, target);
            PromptContextDetailLevel contextDetailLevel = settings.EffectiveContextDetailLevel(target);
            prepareSelectedPlan?.Invoke(
                contextDetailLevel,
                AnyPromptVariantAllowsPsychotypes(settings, target, failoverTargets));
            if (prepareSelectedPlan != null)
            {
                // Anti-repeat preparation can reroll persisted instruction/tone state. Rebuild Full
                // once so the selected and failover variants all share that final event state.
                routingPlan = effectivePromptPlanFactory(PromptContextDetailLevel.Full);
                if (routingPlan == null)
                {
                    return;
                }
            }

            DiaryPromptPlan promptPlan = PromptPlanForContextLevel(
                contextDetailLevel,
                routingPlan,
                effectivePromptPlanFactory);
            if (promptPlan == null)
            {
                return;
            }

            Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants = BuildPromptVariants(
                settings, target, failoverTargets, routingPlan, contextDetailLevel, promptPlan,
                effectivePromptPlanFactory);
            if (promptVariants == null)
            {
                return;
            }

            if (MemorySystemActivationGate.IsCurrentRelease
                && allowMemoryRecall
                && stagedMemoryRequest == null)
            {
                bool hadRecallEvidence;
                SavedActiveLogicalRequestV1 recallRequest;
                bool recallRequestBuilt;
                try
                {
                    recallRequestBuilt = TryBuildNormalMemoryRequestForPromptVariants(
                        diaryEvent, povRole, promptVariants, out recallRequest,
                        out hadRecallEvidence);
                }
                finally
                {
                    // The selected evidence/receipts are now detached in recallRequest. Keeping the
                    // event-bound cache beyond this point would make long sessions grow without bound.
                    ClearMemoryRecallV2EventRole(diaryEvent.eventId, povRole);
                }
                if (!recallRequestBuilt && hadRecallEvidence)
                {
                    // Recall is optional to the primary page. Any identity, receipt, cap, or frozen-
                    // variant refusal rebuilds the exact lane set memory-free before staging.
                    suppressRecallV2ForQueue = true;
                    routingPlan = effectivePromptPlanFactory(PromptContextDetailLevel.Full);
                    if (routingPlan == null) return;
                    promptPlan = PromptPlanForContextLevel(
                        contextDetailLevel,
                        routingPlan,
                        effectivePromptPlanFactory);
                    promptVariants = BuildPromptVariants(
                        settings,
                        target,
                        failoverTargets,
                        routingPlan,
                        contextDetailLevel,
                        promptPlan,
                        effectivePromptPlanFactory);
                    if (promptPlan == null || promptVariants == null) return;
                }
                else
                {
                    stagedMemoryRequest = recallRequest;
                }
            }

            if (stagedMemoryRequest != null
                && !CanAdmitActiveMemoryRequest(stagedMemoryRequest))
            {
                RecordMemoryDiagnostic("other", "owner");
                if (!TryRebuildPromptSetWithoutRecall(
                        ref suppressRecallV2ForQueue,
                        effectivePromptPlanFactory,
                        settings,
                        target,
                        failoverTargets,
                        contextDetailLevel,
                        out routingPlan,
                        out promptPlan,
                        out promptVariants)) return;
                stagedMemoryRequest = null;
            }

            LlmGenerationRequest request = CreateGenerationRequest(
                diaryEvent, povRole, promptPlan, promptVariants, target, failoverTargets,
                forcePrimaryLane, settings);
            if (stagedMemoryRequest != null
                && !TryBindMemoryTransportContext(
                    request, stagedMemoryRequest, promptVariants))
            {
                RecordMemoryDiagnostic("other", "owner");
                if (!TryRebuildPromptSetWithoutRecall(
                        ref suppressRecallV2ForQueue,
                        effectivePromptPlanFactory,
                        settings,
                        target,
                        failoverTargets,
                        contextDetailLevel,
                        out routingPlan,
                        out promptPlan,
                        out promptVariants)) return;
                stagedMemoryRequest = null;
                request = CreateGenerationRequest(
                    diaryEvent, povRole, promptPlan, promptVariants, target, failoverTargets,
                    forcePrimaryLane, settings);
            }
            string rawText = promptPlan.userPrompt ?? string.Empty;

            // Reserve bounded transport capacity and dedup ownership first, but keep the request
            // invisible. Only after the event's matching prompt/lane/pending state is committed do
            // we activate the queue item, so a worker can never outrun its main-thread owner row.
            LlmStagedGenerationRequest staged;
            if (stagedMemoryRequest != null)
            {
                // The allocator burns before TryStage. Queue refusal may leave a harmless gap, but
                // no later request can reuse an identity that transport may already have observed.
                lastIssuedMemoryLogicalRequestSequence = Math.Max(
                    lastIssuedMemoryLogicalRequestSequence,
                    stagedMemoryRequest.logicalRequestSequence);
            }
            LlmRequestStageOutcome stageOutcome = LlmClient.TryStage(request, out staged);
            if (stageOutcome != LlmRequestStageOutcome.Staged)
            {
                if (stagedMemoryRequest != null) RecordMemoryDiagnostic("other", "owner");
                LogApiDebug(
                    "Could not stage request event=" + diaryEvent.eventId
                    + " role=" + povRole
                    + " outcome=" + stageOutcome);
                return;
            }

            bool transportActivated = false;
            try
            {
                diaryEvent.SetPrompt(povRole, rawText);
                diaryEvent.SetLlmMeta(
                    povRole,
                    EndpointUtility.BuildGenerationUrl(target.url, target.model, target.apiMode),
                    target.model);
                diaryEvent.MarkQueued(povRole);

                if (stagedMemoryRequest != null)
                {
                    // The complete detached row is published only after transport capacity is staged.
                    // Its Activated state is committed before the transport handle becomes visible.
                    diaryEvent.SetActiveMemoryLogicalRequestForRole(povRole, stagedMemoryRequest);
                    if (!MemoryDispatchSavedAdapter.TryActivate(stagedMemoryRequest))
                    {
                        RecordMemoryDiagnostic("other", "owner");
                        return;
                    }
                    RebuildMemorySizeIndexes();
                }

                if (!LlmClient.Activate(staged))
                {
                    // Session replacement can race the tiny stage->commit window. No send is claimed.
                    if (stagedMemoryRequest != null) RecordMemoryDiagnostic("other", "owner");
                    return;
                }
                transportActivated = true;
            }
            catch (Exception exception)
            {
                if (stagedMemoryRequest != null) RecordMemoryDiagnostic("other", "owner");
                Log.Error("[Pawn Diary] Failed while activating a staged generation request: "
                    + exception);
            }
            finally
            {
                if (!transportActivated)
                    RollBackStagedGeneration(diaryEvent, povRole, stagedMemoryRequest, staged);
            }
            if (!transportActivated) return;

            LogApiDebug(
                "Queue event=" + diaryEvent.eventId
                + " role=" + povRole
                + " primary=" + LaneLabel(target)
                + " context=" + contextDetailLevel
                + " reason=" + selectionReason
                + " failovers=[" + LaneList(failoverTargets) + "]");
            NotifyEntryStatusChanged(diaryEvent, povRole);
        }

        /// <summary>
        /// Best-effort rollback for every failure or exception after transport staging. Cleanup is
        /// deliberately idempotent so one failing adapter cannot strand the remaining reservations.
        /// </summary>
        private void RollBackStagedGeneration(
            DiaryEvent diaryEvent,
            string povRole,
            SavedActiveLogicalRequestV1 stagedMemoryRequest,
            LlmStagedGenerationRequest staged)
        {
            try { LlmClient.CancelStaged(staged); }
            catch (Exception exception)
            {
                Log.Error("[Pawn Diary] Could not cancel a staged generation request: " + exception);
            }
            try { diaryEvent.RollBackQueuedBeforeActivation(povRole); }
            catch (Exception exception)
            {
                Log.Error("[Pawn Diary] Could not roll back queued diary state: " + exception);
            }
            if (stagedMemoryRequest != null)
            {
                try
                {
                    diaryEvent.SetActiveMemoryLogicalRequestForRole(povRole, null);
                    RebuildMemorySizeIndexes();
                }
                catch (Exception exception)
                {
                    Log.Error("[Pawn Diary] Could not clear a staged memory request: " + exception);
                }
                try
                {
                    MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                        stagedMemoryRequest.logicalRequestId);
                }
                catch (Exception exception)
                {
                    Log.Error("[Pawn Diary] Could not release staged send envelopes: " + exception);
                }
                try { invokedGenerationCutoffs.Settle(stagedMemoryRequest.logicalRequestId); }
                catch (Exception exception)
                {
                    Log.Error("[Pawn Diary] Could not settle a staged generation cutoff: " + exception);
                }
            }
            try { NotifyEntryStatusChanged(diaryEvent, povRole); }
            catch (Exception exception)
            {
                Log.Error("[Pawn Diary] Could not publish staged-request rollback status: " + exception);
            }
        }

        /// <summary>
        /// M2 manual replay: route one already-accepted exact prompt pair through current transport
        /// settings. The factory ignores context detail, so retries/failovers cannot rebuild or alter
        /// historical memory, settings, background, suppression, or wording state.
        /// </summary>
        private bool QueueStaticRegenerationPrompt(
            DiaryEvent diaryEvent,
            string povRole,
            string acceptedSystemPrompt,
            string acceptedUserPrompt)
        {
            if (diaryEvent == null
                || string.IsNullOrWhiteSpace(acceptedSystemPrompt)
                || string.IsNullOrWhiteSpace(acceptedUserPrompt)) return false;
            // Prompt-test mode writes a combined inspection blob into DiaryEvent.prompt. That field is
            // the accepted user half in CurrentRelease, so static replay must leave the exact pair alone.
            if (PromptTestModeEnabled()) return false;

            QueuePrompt(
                diaryEvent,
                povRole,
                ignored => new DiaryPromptPlan
                {
                    systemPrompt = acceptedSystemPrompt,
                    userPrompt = acceptedUserPrompt,
                    responseRules = DiaryResponseRules.ForRequest(
                        diaryEvent.eventId,
                        povRole,
                        false,
                        PawnDiaryMod.Settings?.maxTokens ?? 0)
                },
                allowMemoryRecall: false);
            return diaryEvent.IsPending(povRole);
        }

        /// <summary>
        /// Rebuilds all lane variants after optional Recall-v2 receipt/admission refuses. This is the
        /// normal-diary fail-open path: player background may remain, but episodic evidence is absent.
        /// </summary>
        private bool TryRebuildPromptSetWithoutRecall(
            ref bool suppressRecallV2ForQueue,
            PromptPlanFactory effectivePromptPlanFactory,
            PawnDiarySettings settings,
            ApiEndpointConfig target,
            List<ApiEndpointConfig> failoverTargets,
            PromptContextDetailLevel contextDetailLevel,
            out DiaryPromptPlan routingPlan,
            out DiaryPromptPlan promptPlan,
            out Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants)
        {
            suppressRecallV2ForQueue = true;
            routingPlan = effectivePromptPlanFactory(PromptContextDetailLevel.Full);
            if (routingPlan == null)
            {
                promptPlan = null;
                promptVariants = null;
                return false;
            }
            promptPlan = PromptPlanForContextLevel(
                contextDetailLevel, routingPlan, effectivePromptPlanFactory);
            if (promptPlan == null)
            {
                promptVariants = null;
                return false;
            }
            promptVariants = BuildPromptVariants(
                settings, target, failoverTargets, routingPlan, contextDetailLevel, promptPlan,
                effectivePromptPlanFactory);
            return promptVariants != null;
        }

        private static LlmGenerationRequest CreateGenerationRequest(
            DiaryEvent diaryEvent,
            string povRole,
            DiaryPromptPlan promptPlan,
            Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants,
            ApiEndpointConfig target,
            List<ApiEndpointConfig> failoverTargets,
            bool forcePrimaryLane,
            PawnDiarySettings settings)
        {
            DiaryResponseRules responseRules = promptPlan.responseRules
                ?? DiaryResponseRules.ForRequest(diaryEvent.eventId, povRole, false, settings.maxTokens);
            if (string.IsNullOrWhiteSpace(responseRules.eventId))
                responseRules.eventId = diaryEvent.eventId;
            responseRules.targetRole = povRole;
            responseRules.isTitle = false;
            if (responseRules.maxTokens <= 0) responseRules.maxTokens = settings.maxTokens;
            int requestMaxTokens = responseRules.maxTokens > 0
                ? responseRules.maxTokens
                : settings.maxTokens;
            return new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                // The pure planner already folded persona and XML template policy into this system
                // prompt. Queueing should only attach transport metadata and response rules.
                systemPrompt = promptPlan.systemPrompt,
                rawText = promptPlan.userPrompt ?? string.Empty,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                authMode = target.authMode,
                customAuthHeaderName = target.customAuthHeaderName,
                apiMode = target.apiMode,
                reasoningEffort = target.reasoningEffort,
                reasoningTag = target.reasoningTag,
                providerModelFamily = target.ProviderModelFamilyForCurrentLane(),
                forcePrimaryLane = forcePrimaryLane,
                failoverTargets = failoverTargets,
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = requestMaxTokens,
                lowThinkingHeadroomTokens = DiaryTuning.LowThinkingHeadroomTokens,
                temperature = settings.temperature,
                responseRules = responseRules,
                promptVariants = promptVariants
            };
        }

        private static DiaryPromptPlan PromptPlanForContextLevel(
            PromptContextDetailLevel level,
            DiaryPromptPlan fullPlan,
            PromptPlanFactory promptPlanFactory)
        {
            PromptContextDetailLevel normalized = PawnDiarySettings.NormalizeContextDetailLevel(level);
            if (normalized == PromptContextDetailLevel.Full)
            {
                return fullPlan;
            }

            return promptPlanFactory == null ? null : promptPlanFactory(normalized);
        }

        private static Dictionary<ApiLaneIdentity, LlmPromptVariant> BuildPromptVariants(
            PawnDiarySettings settings,
            ApiEndpointConfig primary,
            List<ApiEndpointConfig> failovers,
            DiaryPromptPlan fullPlan,
            PromptContextDetailLevel primaryLevel,
            DiaryPromptPlan primaryPlan,
            PromptPlanFactory promptPlanFactory)
        {
            Dictionary<ApiLaneIdentity, LlmPromptVariant> variants = new Dictionary<ApiLaneIdentity, LlmPromptVariant>();
            Dictionary<PromptContextDetailLevel, DiaryPromptPlan> plansByLevel = new Dictionary<PromptContextDetailLevel, DiaryPromptPlan>();
            plansByLevel[PromptContextDetailLevel.Full] = fullPlan;
            plansByLevel[PawnDiarySettings.NormalizeContextDetailLevel(primaryLevel)] = primaryPlan;

            if (!AddPromptVariant(variants, plansByLevel, settings, primary, promptPlanFactory))
            {
                return null;
            }

            if (failovers != null)
            {
                for (int i = 0; i < failovers.Count; i++)
                {
                    if (!AddPromptVariant(variants, plansByLevel, settings, failovers[i], promptPlanFactory))
                    {
                        return null;
                    }
                }
            }

            return variants;
        }

        /// <summary>
        /// Returns whether the selected lane or any of its failovers can render the automatic psychotype
        /// layer. Voice staging is deferred until this answer is known so an all-Compact route neither
        /// consumes a roll nor persists a psychotype that no request variant can use.
        /// </summary>
        private static bool AnyPromptVariantAllowsPsychotypes(
            PawnDiarySettings settings,
            ApiEndpointConfig primary,
            List<ApiEndpointConfig> failovers)
        {
            if (settings == null)
            {
                return false;
            }

            if (primary != null
                && PromptContextFeaturePolicy.AllowsPsychotypes(
                    settings.EffectiveContextDetailLevel(primary)))
            {
                return true;
            }

            if (failovers == null)
            {
                return false;
            }

            for (int i = 0; i < failovers.Count; i++)
            {
                ApiEndpointConfig failover = failovers[i];
                if (failover != null
                    && PromptContextFeaturePolicy.AllowsPsychotypes(
                        settings.EffectiveContextDetailLevel(failover)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AddPromptVariant(
            Dictionary<ApiLaneIdentity, LlmPromptVariant> variants,
            Dictionary<PromptContextDetailLevel, DiaryPromptPlan> plansByLevel,
            PawnDiarySettings settings,
            ApiEndpointConfig lane,
            PromptPlanFactory promptPlanFactory)
        {
            if (variants == null || plansByLevel == null || settings == null || lane == null)
            {
                return true;
            }

            ApiLaneIdentity key = ApiLaneIdentity.ForGate(
                lane.url, lane.model, lane.apiMode, lane.authMode, lane.customAuthHeaderName, lane.apiKey);
            if (key.Empty || variants.ContainsKey(key))
            {
                return true;
            }

            PromptContextDetailLevel level = settings.EffectiveContextDetailLevel(lane);
            DiaryPromptPlan plan;
            if (!plansByLevel.TryGetValue(level, out plan))
            {
                plan = PromptPlanForContextLevel(level, plansByLevel[PromptContextDetailLevel.Full], promptPlanFactory);
                if (plan == null)
                {
                    return false;
                }

                plansByLevel[level] = plan;
            }

            variants[key] = new LlmPromptVariant
            {
                systemPrompt = plan.systemPrompt ?? string.Empty,
                rawText = plan.userPrompt ?? string.Empty,
                contextDetailLevel = level
            };
            return true;
        }

        /// <summary>
        /// Dequeues a completed LLM result and applies it to the corresponding DiaryEvent, then kicks
        /// off the recipient side for pairwise entries. Title follow-up results are routed
        /// separately (see the <c>isTitleRequest</c> branch) so they never reach the main-entry
        /// applier.
        /// </summary>
        private DiaryTelemetryOutcome ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.eventId))
            {
                return DiaryTelemetryOutcome.LlmResultInvalid;
            }

            DiaryTelemetryOutcome coordinatorOutcome;
            if (TryApplyMemoryCoordinatorResult(result, out coordinatorOutcome))
            {
                return coordinatorOutcome;
            }

            DiaryEvent diaryEvent = events.FindEvent(result.eventId);
            if (diaryEvent == null)
            {
                return DiaryTelemetryOutcome.LlmResultMissingEvent;
            }

            if (!DiaryEvent.RoleEquals(result.povRole, DiaryEvent.InitiatorRole)
                && !DiaryEvent.RoleEquals(result.povRole, DiaryEvent.RecipientRole)
                && !DiaryEvent.RoleEquals(result.povRole, DiaryEvent.NeutralRole))
            {
                return DiaryTelemetryOutcome.LlmResultInvalid;
            }

            // A dev-history purge can retain a shared master event for its other pawn while severing
            // ownership of only the purged first-person role. Ignore any result that was already in
            // flight for that now-ownerless role: it must not overwrite the terminal tombstone, retry,
            // create unread state, or queue a title. If the detached role was the pair initiator, still
            // re-evaluate the surviving recipient so it can write from the base pair prompt.
            if (DiaryEvent.RoleIsInitiatorOrRecipient(result.povRole)
                && string.IsNullOrWhiteSpace(diaryEvent.PawnIdForRole(result.povRole)))
            {
                if (!result.isTitleRequest
                    && DiaryEvent.RoleEquals(result.povRole, DiaryEvent.InitiatorRole)
                    && !diaryEvent.solo)
                {
                    QueueSequentialPairwiseRewrite(diaryEvent, SuccessfulLaneFromResult(result));
                }

                return DiaryTelemetryOutcome.LlmResultApplied;
            }

            // Title follow-up: never call ApplyLlmResult (which is the main-entry applier) —
            // the title is a separate, smaller request that lives on its own per-POV fields.
            if (result.isTitleRequest)
            {
                // The request may have finished after the player manually replaced this page. Manual
                // Save clears the pending title state, so consuming-but-ignoring this stale completion
                // prevents it from overwriting the player's canonical title or emitting callbacks.
                if (!diaryEvent.IsTitlePending(result.povRole))
                {
                    return DiaryTelemetryOutcome.LlmTitleResultApplied;
                }

                ApplyTitleResult(diaryEvent, result);
                return DiaryTelemetryOutcome.LlmTitleResultApplied;
            }

            // As above for the main body: every accepted generation marks its exact role pending before
            // transport starts. A non-pending completion is obsolete (manual edit, purge, or another
            // terminal transition) and must not restore prose, unread state, retries, or pair follow-ups.
            if (!diaryEvent.IsPending(result.povRole))
            {
                return DiaryTelemetryOutcome.LlmResultApplied;
            }

            SavedActiveLogicalRequestV1 memoryRequest;
            if (!TryBeginMemoryResultApply(diaryEvent, result, out memoryRequest))
            {
                // A late session/request/epoch callback is consumed but cannot restore text or
                // recreate memory state. Brainwipe and replacement requests deliberately land here.
                SavedActiveLogicalRequestV1 invalidExactRequest = diaryEvent
                    .ActiveMemoryLogicalRequestForRole(result.povRole);
                if (!string.IsNullOrWhiteSpace(result.memoryLogicalRequestId)
                    && invalidExactRequest != null
                    && string.Equals(
                        invalidExactRequest.logicalRequestId,
                        result.memoryLogicalRequestId,
                        StringComparison.Ordinal))
                {
                    // Exact live ownership plus failed validation is corruption, not an obsolete
                    // callback. Settle it terminally so orphan recovery cannot turn the fail-closed
                    // decision into an automatic resend.
                    AppendTerminalMemoryAttemptAudits(
                        invalidExactRequest,
                        MemoryDispatchTokens.Invalid,
                        Math.Max(1, Find.TickManager?.TicksGame ?? 0));
                    diaryEvent.SetActiveMemoryLogicalRequestForRole(result.povRole, null);
                    MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                        invalidExactRequest.logicalRequestId);
                    invokedGenerationCutoffs.Settle(invalidExactRequest.logicalRequestId);
                    RebuildMemorySizeIndexes();
                    diaryEvent.MarkSkipped(
                        result.povRole,
                        "PawnDiary.Error.MemoryDispatchStopped".Translate());
                    NotifyEntryStatusChanged(diaryEvent, result.povRole);
                    if (!diaryEvent.solo
                        && DiaryEvent.RoleEquals(
                            result.povRole, DiaryEvent.InitiatorRole)
                        && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                    {
                        diaryEvent.MarkSkipped(
                            DiaryEvent.RecipientRole,
                            "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                        NotifyEntryStatusChanged(
                            diaryEvent, DiaryEvent.RecipientRole);
                    }
                }
                return DiaryTelemetryOutcome.LlmResultApplied;
            }

            if (!result.success)
            {
                if (memoryRequest != null)
                {
                    AppendTerminalMemoryAttemptAudits(
                        memoryRequest,
                        MemoryDispatchTokens.ProviderError,
                        Math.Max(1, Find.TickManager?.TicksGame ?? 0));
                    diaryEvent.SetActiveMemoryLogicalRequestForRole(result.povRole, null);
                    MemoryDispatchRuntimeBridge.ReleaseSendEnvelope(
                        result.memoryInvocationPermit);
                    invokedGenerationCutoffs.Settle(memoryRequest.logicalRequestId);
                    RebuildMemorySizeIndexes();
                }
                HandleFailedMainGeneration(diaryEvent, result);
                return DiaryTelemetryOutcome.LlmResultApplied;
            }

            if (MemorySystemActivationGate.IsCurrentRelease
                && result.sentSystemPrompt != null && result.sentRawText != null)
            {
                diaryEvent.SetAcceptedPromptPair(
                    result.povRole, result.sentSystemPrompt, result.sentRawText);
                ApplyAcceptedPromptRetention();
            }
            else if (result.sentRawText != null)
            {
                diaryEvent.SetPrompt(result.povRole, result.sentRawText);
            }

            diaryEvent.ApplyLlmResult(result);
            if (!string.IsNullOrWhiteSpace(result.generatedText))
            {
                MarkGeneratedEntryUnread(diaryEvent, result.povRole);
            }

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            ApiEndpointConfig successfulLane = SuccessfulLaneFromResult(result);
            if (!string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(
                    result.povRole,
                    EndpointUtility.BuildGenerationUrl(
                        result.endpointUrl,
                        result.modelName,
                        result.apiMode),
                    result.modelName);
            }

            NotifyEntryStatusChanged(diaryEvent, result.povRole);
            if (memoryRequest != null)
            {
                AppendTerminalMemoryAttemptAudits(
                    memoryRequest,
                    MemoryDispatchTokens.Success,
                    Math.Max(1, Find.TickManager?.TicksGame ?? 0));
                diaryEvent.SetActiveMemoryLogicalRequestForRole(result.povRole, null);
                MemoryDispatchRuntimeBridge.ReleaseSendEnvelope(
                    result.memoryInvocationPermit);
                invokedGenerationCutoffs.Settle(memoryRequest.logicalRequestId);
                RebuildMemorySizeIndexes();
            }

            // Generated speech Social-log injection is currently hidden/disabled. RimWorld accepts
            // the synthetic PlayLog row, but it does not reliably appear in the Social tab UI.
            // TryInjectGeneratedSpeechPlayLogEntry(diaryEvent, result);

            QueueRecipientAfterInitiatorResult(diaryEvent, result, successfulLane);

            // Title follow-up: if Generate LLM titles is on and the main entry produced text
            // but the role has no stored title yet, queue a small title call. The title is tiny,
            // and the request is capped to TitleMaxTokens.
            if (PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.generateTitles
                && !string.IsNullOrWhiteSpace(result.generatedText)
                && string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(result.povRole)))
            {
                QueueTitleRequest(diaryEvent, result.povRole, successfulLane);
            }
            return DiaryTelemetryOutcome.LlmResultApplied;
        }

        /// <summary>
        /// Keeps a failed main-entry request out of player UI. A bounded number of fresh requests are
        /// queued through the normal background pipeline; exhaustion becomes a hidden skipped state
        /// plus one warning in the RimWorld log.
        /// </summary>
        private void HandleFailedMainGeneration(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (result.memoryDispatchTerminalFailure)
            {
                string terminalError = string.IsNullOrWhiteSpace(result.error)
                    ? "PawnDiary.Error.MemoryDispatchStopped".Translate().ToString()
                    : result.error;
                diaryEvent.MarkSkipped(
                    result.povRole,
                    terminalError);
                NotifyEntryStatusChanged(diaryEvent, result.povRole);
                if (!diaryEvent.solo
                    && DiaryEvent.RoleEquals(result.povRole, DiaryEvent.InitiatorRole)
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkSkipped(
                        DiaryEvent.RecipientRole,
                        "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                    NotifyEntryStatusChanged(diaryEvent, DiaryEvent.RecipientRole);
                }
                return;
            }

            int retryLimit = DiaryGenerationStatus.NormalizeAutomaticRetryLimit(
                DiaryTuning.Current.automaticGenerationRetryLimit);
            int attemptsAlreadyScheduled =
                diaryEvent.AutomaticGenerationRetryAttemptsForRole(result.povRole);
            if (DiaryGenerationStatus.CanScheduleAutomaticRetry(attemptsAlreadyScheduled, retryLimit))
            {
                string acceptedSystem = diaryEvent.AcceptedSystemPromptForRole(result.povRole);
                string acceptedUser = diaryEvent.PromptForRole(result.povRole);
                bool replayAcceptedPair = MemorySystemActivationGate.IsCurrentRelease
                    && !string.IsNullOrWhiteSpace(acceptedSystem)
                    && !string.IsNullOrWhiteSpace(acceptedUser);
                // Prompt-test capture reuses DiaryEvent.prompt for a combined inspection blob. Do not
                // make a preserved accepted pair queueable when static replay is deliberately disabled,
                // or the recurring normal scan can overwrite only its user half on the next pass.
                if (!replayAcceptedPair || !PromptTestModeEnabled())
                {
                    int retryNumber = diaryEvent.RecordAutomaticGenerationRetry(result.povRole);
                    diaryEvent.PrepareForAutomaticRegeneration(result.povRole);
                    NotifyEntryStatusChanged(diaryEvent, result.povRole);
                    if (replayAcceptedPair)
                    {
                        QueueStaticRegenerationPrompt(
                            diaryEvent, result.povRole, acceptedSystem, acceptedUser);
                    }
                    else
                    {
                        EnsureGenerationQueued(diaryEvent, result.povRole);
                    }
                    LogApiDebug(
                        "Automatically requeued failed generation event=" + diaryEvent.eventId
                        + " role=" + result.povRole
                        + " retry=" + retryNumber + "/" + retryLimit);
                    return;
                }
            }

            string error = string.IsNullOrWhiteSpace(result.error)
                ? "Unknown generation error."
                : result.error;
            diaryEvent.MarkSkipped(result.povRole, error);
            NotifyEntryStatusChanged(diaryEvent, result.povRole);

            // A sequential recipient cannot be written without the initiator page as context. Mark it
            // skipped too so a later load/catch-up scan does not revive half of an exhausted pair.
            if (!diaryEvent.solo
                && DiaryEvent.RoleEquals(result.povRole, DiaryEvent.InitiatorRole)
                && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
            {
                diaryEvent.MarkSkipped(
                    DiaryEvent.RecipientRole,
                    "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                NotifyEntryStatusChanged(diaryEvent, DiaryEvent.RecipientRole);
            }

            string warningError = TextTruncation.SafePrefix(
                error.Replace('\r', ' ').Replace('\n', ' '),
                500);
            Log.Warning(
                "[Pawn Diary] Gave up diary generation after "
                + attemptsAlreadyScheduled + " automatic regeneration attempt(s)"
                + " event=" + diaryEvent.eventId
                + " role=" + result.povRole
                + " lastError=" + warningError);
        }

        /// <summary>
        /// Sets the cheap "new page" badge flag for the pawn whose main POV just finished. This runs
        /// when the generation result is applied, so the inspect command never has to scan history.
        /// </summary>
        private void MarkGeneratedEntryUnread(DiaryEvent diaryEvent, string povRole)
        {
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId) && DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole))
            {
                pawnId = DiaryContextFields.Value(diaryEvent.gameContext, "arrival_pawn_id");
                if (string.IsNullOrWhiteSpace(pawnId))
                {
                    pawnId = DiaryContextFields.Value(diaryEvent.gameContext, "death_victim_id");
                }
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary != null)
            {
                if (diary.unreadGeneratedEntryCount < int.MaxValue)
                {
                    diary.unreadGeneratedEntryCount++;
                }
                diary.hasUnreadGeneratedEntry = true;
                SetCachedCommandUnreadCount(pawnId, diary.unreadGeneratedEntryCount);
            }
        }

        private static ApiEndpointConfig SuccessfulLaneFromResult(LlmGenerationResult result)
        {
            if (result == null
                || !result.success
                || string.IsNullOrWhiteSpace(result.endpointUrl)
                || string.IsNullOrWhiteSpace(result.modelName))
            {
                return null;
            }

            return new ApiEndpointConfig(result.endpointUrl, result.apiKey, result.modelName)
            {
                authMode = result.authMode,
                customAuthHeaderName = result.customAuthHeaderName,
                apiMode = result.apiMode
            };
        }

        /// <summary>
        /// Applies a title-generation result to the event: stores the returned title on success
        /// or records the failure. Uses a separate per-POV status field so the main-entry
        /// recovery scan never touches it. If the title call fails, entries without an older
        /// stored title keep a date-only card header.
        /// </summary>
        private void ApplyTitleResult(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (diaryEvent == null || result == null)
            {
                return;
            }

            if (result.success)
            {
                string title = LlmResponseParser.TitleOrFallback(
                    result.generatedText,
                    diaryEvent.DisplayTextForRole(result.povRole));
                if (string.IsNullOrWhiteSpace(title))
                {
                    diaryEvent.MarkTitleFailed(result.povRole, "PawnDiary.Error.TitleEmptyResponse".Translate());
                }
                else
                {
                    diaryEvent.MarkTitleComplete(result.povRole, title);
                }
            }
            else
            {
                diaryEvent.MarkTitleFailed(result.povRole, result.error);
            }

            NotifyEntryStatusChanged(diaryEvent, result.povRole);
        }

        /// <summary>
        /// Queues the title-generation follow-up for the given POV. Mirrors the
        /// <see cref="QueuePrompt"/> shape: pick a lane (pin to the same lane the main entry
        /// used so a sequential pair stays on one model), mark the title status as pending, and
        /// enqueue an <see cref="LlmGenerationRequest"/> with <c>isTitleRequest = true</c>.
        /// On failure (no API configured, lane unavailable) the per-POV title is left untouched.
        /// </summary>
        private bool QueueTitleRequest(DiaryEvent diaryEvent, string povRole, ApiEndpointConfig primaryOverride,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return false;
            }

            // Don't double-queue: an existing title or an in-flight title request both skip.
            if (!string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(povRole)))
            {
                return false;
            }

            if (diaryEvent.IsTitlePending(povRole))
            {
                return false;
            }

            if (!diaryEvent.CanQueueTitleGeneration(povRole))
            {
                return false;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache, livePawnsById))
            {
                return false;
            }

            if (ShouldSkipFirstPersonGenerationForIncapacitation(
                    diaryEvent,
                    FindLivePawnByLoadId(PawnIdForRole(diaryEvent, povRole), livePawnsById)))
            {
                return false;
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                return false;
            }

            if (PromptTestModeEnabled())
            {
                return false;
            }

            List<ApiEndpointConfig> targets = settings.ActiveEndpoints();
            if (targets == null || targets.Count == 0)
            {
                return false;
            }

            ApiEndpointConfig target = FindMatchingActiveLane(targets, primaryOverride);
            if (target != null && !CanUsePinnedLane(targets, target))
            {
                target = null;
            }

            if (target == null)
            {
                // Pin the title to the same lane the main entry used, when available — keeps a
                // paired event and its title on one model. Reuses the shared pin primitive so the
                // title follows the same lane-selection rules as the main entry's recipient pin
                // (one policy, not two). Falls back to round-robin for first-time titles on a new
                // role or when the main lane is cooling.
                target = FindPinnableLane(targets, diaryEvent.LlmEndpointForRole(povRole), diaryEvent.LlmModelForRole(povRole));

                if (target == null)
                {
                    int index = ApiLaneSelector.SelectPrimaryIndex(
                        targets.Count,
                        settings.apiRoutingMode,
                        LlmClient.NextRoundRobinIndex(),
                        LaneReadiness(targets));
                    target = targets[index];
                }
            }

            DiaryPromptPlan titlePlan = DiaryPromptBuilder.BuildTitlePromptPlan(diaryEvent, povRole, TitleMaxTokens);
            DiaryResponseRules titleRules = titlePlan.responseRules
                ?? DiaryResponseRules.ForRequest(diaryEvent.eventId, povRole, true, TitleMaxTokens);
            if (string.IsNullOrWhiteSpace(titleRules.eventId))
            {
                titleRules.eventId = diaryEvent.eventId;
            }
            titleRules.targetRole = povRole;
            titleRules.isTitle = true;
            titleRules.maxTokens = TitleMaxTokens;
            titleRules.trimIncompleteSentence = false;

            LlmGenerationRequest request = new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                isTitleRequest = true,
                systemPrompt = titlePlan.systemPrompt,
                rawText = titlePlan.userPrompt,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                authMode = target.authMode,
                customAuthHeaderName = target.customAuthHeaderName,
                apiMode = target.apiMode,
                reasoningEffort = target.reasoningEffort,
                reasoningTag = target.reasoningTag,
                providerModelFamily = target.ProviderModelFamilyForCurrentLane(),
                failoverTargets = BuildFailoverTargets(targets, target),
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = TitleMaxTokens,
                lowThinkingHeadroomTokens = DiaryTuning.LowThinkingHeadroomTokens,
                temperature = settings.temperature,
                responseRules = titleRules
            };

            LlmStagedGenerationRequest staged;
            if (LlmClient.TryStage(request, out staged) != LlmRequestStageOutcome.Staged)
            {
                return false;
            }

            diaryEvent.MarkTitleQueued(povRole);
            if (!LlmClient.Activate(staged))
            {
                diaryEvent.RollBackTitleQueuedBeforeActivation(povRole);
                NotifyEntryStatusChanged(diaryEvent, povRole);
                return false;
            }

            NotifyEntryStatusChanged(diaryEvent, povRole);
            return true;
        }

        /// <summary>
        /// After the initiator entry completes, either marks the recipient as failed (if the initiator failed)
        /// or re-evaluates the sequential queue so the recipient can generate with the initiator's text as context.
        /// </summary>
        private void QueueRecipientAfterInitiatorResult(DiaryEvent diaryEvent, LlmGenerationResult result, ApiEndpointConfig successfulLane)
        {
            if (diaryEvent == null
                || diaryEvent.solo
                || result == null
                || !string.Equals(result.povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.success)
            {
                // Do not mark a disabled recipient as failed; they intentionally have no LLM state.
                if (DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole)
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                    NotifyEntryStatusChanged(diaryEvent, DiaryEvent.RecipientRole);
                }

                return;
            }

            QueueSequentialPairwiseRewrite(diaryEvent, successfulLane);
        }
    }
}
