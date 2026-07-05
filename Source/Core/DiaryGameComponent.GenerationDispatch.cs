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

        private static bool PromptTestModeEnabled()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.promptTestMode;
        }

        /// <summary>
        /// Final impure step before LLM dispatch: stamps the planned prompt on the event, records
        /// endpoint metadata, marks the event queued, and enqueues the request to <see cref="LlmClient"/>.
        /// </summary>
        private void QueuePrompt(DiaryEvent diaryEvent, string povRole, DiaryPromptPlan promptPlan,
            ApiEndpointConfig primaryOverride = null, Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole) || promptPlan == null)
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

            string rawText = promptPlan.userPrompt ?? string.Empty;
            string capturedPrompt = DiaryPromptCapture.Format(promptPlan.systemPrompt, rawText);
            diaryEvent.SetPrompt(povRole, PromptTestModeEnabled() ? capturedPrompt : rawText);

            // Fetch settings once into a local so the method operates on one consistent snapshot
            // (matching QueueTitleRequest) instead of reaching the global static at every step.
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoLlmSettings".Translate());
                NotifyEntryStatusChanged(diaryEvent, povRole);
                return;
            }

            if (PromptTestModeEnabled())
            {
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
                promptPlan.forcedModelName, settings.apiRoutingMode, out selectionReason, out forcePrimaryLane);
            List<ApiEndpointConfig> failoverTargets = BuildFailoverTargets(targets, target);
            LogApiDebug(
                "Queue event=" + diaryEvent.eventId
                + " role=" + povRole
                + " primary=" + LaneLabel(target)
                + " reason=" + selectionReason
                + " failovers=[" + LaneList(failoverTargets) + "]");

            diaryEvent.SetLlmMeta(povRole, EndpointUtility.BuildGenerationUrl(target.url, target.apiMode), target.model);
            diaryEvent.MarkQueued(povRole);

            DiaryResponseRules responseRules = promptPlan.responseRules
                ?? DiaryResponseRules.ForRequest(diaryEvent.eventId, povRole, false, settings.maxTokens);
            if (string.IsNullOrWhiteSpace(responseRules.eventId))
            {
                responseRules.eventId = diaryEvent.eventId;
            }
            responseRules.targetRole = povRole;
            responseRules.isTitle = false;
            if (responseRules.maxTokens <= 0)
            {
                responseRules.maxTokens = settings.maxTokens;
            }

            int requestMaxTokens = responseRules.maxTokens > 0 ? responseRules.maxTokens : settings.maxTokens;
            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                // The pure planner already folded persona and XML template policy into this system
                // prompt. Queueing should only attach transport metadata and response rules.
                systemPrompt = promptPlan.systemPrompt,
                rawText = rawText,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                authMode = target.authMode,
                customAuthHeaderName = target.customAuthHeaderName,
                apiMode = target.apiMode,
                reasoningEffort = target.reasoningEffort,
                reasoningTag = target.reasoningTag,
                forcePrimaryLane = forcePrimaryLane,
                // The other configured lanes, tried in order if this one errors ("use next model").
                failoverTargets = failoverTargets,
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = requestMaxTokens,
                temperature = settings.temperature,
                responseRules = responseRules
            });
            NotifyEntryStatusChanged(diaryEvent, povRole);
        }

        /// <summary>
        /// Dequeues a completed LLM result and applies it to the corresponding DiaryEvent, then kicks
        /// off the recipient side for pairwise entries. Title follow-up results are routed
        /// separately (see the <c>isTitleRequest</c> branch) so they never reach the main-entry
        /// applier.
        /// </summary>
        private void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.eventId))
            {
                return;
            }

            DiaryEvent diaryEvent = events.FindEvent(result.eventId);
            if (diaryEvent == null)
            {
                return;
            }

            // Title follow-up: never call ApplyLlmResult (which is the main-entry applier) —
            // the title is a separate, smaller request that lives on its own per-POV fields.
            if (result.isTitleRequest)
            {
                ApplyTitleResult(diaryEvent, result);
                return;
            }

            diaryEvent.ApplyLlmResult(result);
            if (result.success && !string.IsNullOrWhiteSpace(result.generatedText))
            {
                MarkGeneratedEntryUnread(diaryEvent, result.povRole);
            }

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            ApiEndpointConfig successfulLane = SuccessfulLaneFromResult(result);
            if (result.success && !string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(result.povRole, EndpointUtility.BuildGenerationUrl(result.endpointUrl, result.apiMode), result.modelName);
            }

            NotifyEntryStatusChanged(diaryEvent, result.povRole);

            // Generated speech Social-log injection is currently hidden/disabled. RimWorld accepts
            // the synthetic PlayLog row, but it does not reliably appear in the Social tab UI.
            // TryInjectGeneratedSpeechPlayLogEntry(diaryEvent, result);

            QueueRecipientAfterInitiatorResult(diaryEvent, result, successfulLane);

            // Title follow-up: if Generate LLM titles is on and the main entry produced text
            // but the role has no stored title yet, queue a small title call. The title is tiny,
            // and the request is capped to TitleMaxTokens.
            if (result.success
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.generateTitles
                && !string.IsNullOrWhiteSpace(result.generatedText)
                && string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(result.povRole)))
            {
                QueueTitleRequest(diaryEvent, result.povRole, successfulLane);
            }
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
                diary.hasUnreadGeneratedEntry = true;
                SetCachedCommandUnreadFlag(pawnId, true);
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

            if (ShouldSkipFirstPersonGenerationForIncapacitation(FindLivePawnByLoadId(PawnIdForRole(diaryEvent, povRole), livePawnsById)))
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

            diaryEvent.MarkTitleQueued(povRole);

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

            LlmClient.Enqueue(new LlmGenerationRequest
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
                failoverTargets = BuildFailoverTargets(targets, target),
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = TitleMaxTokens,
                temperature = settings.temperature,
                responseRules = titleRules
            });
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
