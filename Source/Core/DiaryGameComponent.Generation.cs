// The generation pipeline: deciding what to (re)queue, building the prompt for each POV, choosing
// which configured API lane handles the request, dispatching to LlmClient, and applying finished
// results back onto the DiaryEvent. The tick scan (QueueAllPendingGenerations) and the per-pawn
// re-enable scan funnel through EnsureGenerationQueued, which routes each event to the right prompt
// (neutral death/arrival, sequential dual-POV pair, or a single POV rewrite). QueuePrompt is the
// single choke point that records endpoint metadata and enqueues the LLM request.
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
        // Pawns below 11% Consciousness should not write first-person entries. Events still record,
        // and neutral death/arrival descriptions still generate, but non-neutral LLM work waits
        // until the pawn is conscious enough again.
        private const float MinimumConsciousnessForFirstPersonGeneration = 0.11f;

        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (TryMarkIncapacitatedPovSkipped(diaryEvent, povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
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
                QueueSequentialPairwiseRewrite(diaryEvent);
                return;
            }

            if (diaryEvent.CanQueueGeneration(povRole))
            {
                QueueLlmRewrite(diaryEvent, povRole);
            }
        }

        private void QueueAllPendingGenerations()
        {
            if (diaryEvents == null)
            {
                return;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.CanQueueGeneration(DiaryEvent.NeutralRole))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.initiatorPawnId))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole);
                }

                if (!diaryEvent.solo
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole)
                    && !EventFallsOutsideDiaryBoundsForPawn(diaryEvent, diaryEvent.recipientPawnId))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole);
                }

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
            if (diaryEvents == null)
            {
                orphanCandidatesLastScan.Clear();
                return;
            }

            HashSet<string> orphansThisScan = new HashSet<string>();
            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
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

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null)
                {
                    continue;
                }

                if (EventFallsOutsideDiaryBoundsForPawn(diaryEvent, pawnId))
                {
                    continue;
                }

                if (diaryEvent.HasDeathDescription())
                {
                    if (diaryEvent.IsDeathDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                if (diaryEvent.HasArrivalDescription())
                {
                    if (diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        EnsureGenerationQueued(diaryEvent, DiaryEvent.NeutralRole);
                    }

                    continue;
                }

                string povRole = diaryEvent.RoleForPawn(pawnId);
                EnsureGenerationQueued(diaryEvent, povRole);
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
        private void QueueLlmRewrite(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Persona and prompt enchantment are resolved at queue time so changing a pawn or XML
            // weights affects future generations without rewriting prompts already sent or saved
            // for debugging.
            string rawText = DiaryPromptBuilder.BuildInteractionPrompt(
                diaryEvent,
                povRole,
                PersonaRuleFor(diaryEvent, povRole),
                PromptEnchantmentRuleFor(diaryEvent, povRole));
            QueuePrompt(diaryEvent, povRole, rawText);
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

            string rawText = DiaryPromptBuilder.BuildDeathDescriptionPrompt(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, rawText);
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

            string rawText = DiaryPromptBuilder.BuildArrivalDescriptionPrompt(diaryEvent);
            QueuePrompt(diaryEvent, DiaryEvent.NeutralRole, rawText);
        }

        /// <summary>
        /// Dual-POV flow: queues the initiator first, then the recipient once the initiator result arrives
        /// (so the recipient prompt can include the initiator's generated text as hidden continuity context).
        /// </summary>
        private void QueueSequentialPairwiseRewrite(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || diaryEvent.solo)
            {
                return;
            }

            TryMarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.InitiatorRole);
            TryMarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.RecipientRole);

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole);
            bool initiatorSkipped = diaryEvent.IsSkipped(DiaryEvent.InitiatorRole);
            bool initiatorContextExpected = initiatorEnabled && !initiatorSkipped;

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                string rawText = DiaryPromptBuilder.BuildSequentialInteractionPrompt(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    PersonaRuleFor(diaryEvent, DiaryEvent.InitiatorRole),
                    PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.InitiatorRole));
                QueuePrompt(diaryEvent, DiaryEvent.InitiatorRole, rawText);
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
                string rawText = initiatorContextExpected
                    ? DiaryPromptBuilder.BuildSequentialInteractionPrompt(
                        diaryEvent,
                        DiaryEvent.RecipientRole,
                        PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole),
                        PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.RecipientRole))
                    : DiaryPromptBuilder.BuildInteractionPrompt(
                        diaryEvent,
                        DiaryEvent.RecipientRole,
                        PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole),
                        PromptEnchantmentRuleFor(diaryEvent, DiaryEvent.RecipientRole));
                QueuePrompt(diaryEvent, DiaryEvent.RecipientRole, rawText);
            }
        }

        /// <summary>
        /// Final step before LLM dispatch: stamps the prompt on the event, records endpoint metadata, marks
        /// the event queued, and enqueues the request to <see cref="LlmClient"/>.
        /// </summary>
        private void QueuePrompt(DiaryEvent diaryEvent, string povRole, string rawText)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            diaryEvent.SetPrompt(povRole, rawText);

            if (PawnDiaryMod.Settings == null)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoLlmSettings".Translate());
                return;
            }

            // Pick which configured API lane handles this request. New events spread across all
            // lanes (parallelism); the recipient half of a paired event reuses the initiator's lane
            // so a sequential pair stays on one model.
            List<ApiEndpointConfig> targets = PawnDiaryMod.Settings.ActiveEndpoints();
            LogApiLaneConfiguration(PawnDiaryMod.Settings, targets);
            if (targets.Count == 0)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoApiConfigured".Translate());
                return;
            }

            string selectionReason;
            ApiEndpointConfig target = SelectApiTarget(diaryEvent, povRole, targets, out selectionReason);
            List<ApiEndpointConfig> failoverTargets = BuildFailoverTargets(targets, target);
            LogApiDebug(
                "Queue event=" + diaryEvent.eventId
                + " role=" + povRole
                + " primary=" + LaneLabel(target)
                + " reason=" + selectionReason
                + " failovers=[" + LaneList(failoverTargets) + "]");

            diaryEvent.SetLlmMeta(povRole, EndpointUtility.BuildChatCompletionsUrl(target.url), target.model);
            diaryEvent.MarkQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                systemPrompt = SystemPromptForEvent(diaryEvent),
                rawText = rawText,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                // The other configured lanes, tried in order if this one errors ("use next model").
                failoverTargets = failoverTargets,
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = PawnDiaryMod.Settings.maxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }

        /// <summary>
        /// Picks the system prompt by narrative mode: a neutral third-person chronicle for colonist
        /// death/arrival descriptions, the end-of-day reflection prompt for day summaries, and the
        /// first-person diary voice for everything else. Each is player-editable in settings.
        /// </summary>
        private static string SystemPromptForEvent(DiaryEvent diaryEvent)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (diaryEvent.HasDeathDescription() || diaryEvent.HasArrivalDescription())
            {
                return settings.systemPromptNeutral;
            }

            if (diaryEvent.IsDayReflection())
            {
                return settings.systemPromptReflection;
            }

            return settings.systemPrompt;
        }

        /// <summary>
        /// Chooses the primary API lane for a request from the given active lanes (non-empty).
        /// The recipient of a paired event reuses the lane the initiator used — matched
        /// by the endpoint+model recorded on the event — so the sequential pair shares one model.
        /// Everything else takes the next lane in round-robin order to spread load across APIs.
        /// </summary>
        private static ApiEndpointConfig SelectApiTarget(DiaryEvent diaryEvent, string povRole, List<ApiEndpointConfig> targets, out string reason)
        {
            // Sequential pinning: keep the recipient on the initiator's lane when paired mode ran
            // the initiator first and recorded which endpoint+model it used (after failover, the
            // recorded meta is the lane that actually produced the initiator's entry).
            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
            {
                string initiatorEndpoint = diaryEvent.initiatorLlmEndpoint;
                string initiatorModel = diaryEvent.initiatorLlmModel;
                if (!string.IsNullOrWhiteSpace(initiatorEndpoint) && !string.IsNullOrWhiteSpace(initiatorModel))
                {
                    foreach (ApiEndpointConfig candidate in targets)
                    {
                        if (string.Equals(EndpointUtility.BuildChatCompletionsUrl(candidate.url), initiatorEndpoint, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(candidate.model, initiatorModel, StringComparison.Ordinal))
                        {
                            reason = "recipient pinned to initiator lane";
                            return candidate;
                        }
                    }

                    LogApiDebug(
                        "Recipient pin missed for event=" + diaryEvent.eventId
                        + " initiatorLane=" + initiatorModel + " @ " + initiatorEndpoint
                        + "; falling back to round-robin");
                }
            }

            int index = LlmClient.NextRoundRobinIndex() % targets.Count;
            reason = "round-robin index " + index + " of " + targets.Count;
            return targets[index];
        }

        /// <summary>
        /// Returns the lanes to fall back to (every active lane except the chosen primary), in order,
        /// so a request that errors on its primary lane can retry on the others.
        /// </summary>
        private static List<ApiEndpointConfig> BuildFailoverTargets(List<ApiEndpointConfig> targets, ApiEndpointConfig primary)
        {
            List<ApiEndpointConfig> failovers = new List<ApiEndpointConfig>();
            foreach (ApiEndpointConfig candidate in targets)
            {
                if (!ReferenceEquals(candidate, primary))
                {
                    failovers.Add(candidate);
                }
            }

            return failovers;
        }

        /// <summary>
        /// Writes one-line API lane diagnostics to the RimWorld log. These are intentionally
        /// English debug logs, not player-facing UI strings; never include API keys here.
        /// </summary>
        private static void LogApiLaneConfiguration(PawnDiarySettings settings, List<ApiEndpointConfig> active)
        {
            int configuredCount = settings?.apiEndpoints?.Count ?? 0;
            int activeCount = active?.Count ?? 0;
            LogApiDebug(
                "Configured API lanes=" + configuredCount
                + ", active lanes=" + activeCount
                + (configuredCount == activeCount ? string.Empty : " (disabled rows or rows with blank url/model are skipped)")
                + "; active=[" + LaneList(active) + "]");
        }

        private static void LogApiDebug(string message)
        {
            if (!LlmClient.DebugLoggingEnabled())
            {
                return;
            }

            Log.Message("[PawnDiary debug] " + message);
        }

        private static string LaneList(List<ApiEndpointConfig> lanes)
        {
            if (lanes == null || lanes.Count == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            foreach (ApiEndpointConfig lane in lanes)
            {
                labels.Add(LaneLabel(lane));
            }

            return string.Join(" | ", labels.ToArray());
        }

        private static string LaneLabel(ApiEndpointConfig lane)
        {
            if (lane == null)
            {
                return "<null>";
            }

            return (string.IsNullOrWhiteSpace(lane.model) ? "<blank-model>" : lane.model)
                + " @ "
                + (string.IsNullOrWhiteSpace(lane.url) ? "<blank-url>" : EndpointUtility.BuildChatCompletionsUrl(lane.url));
        }


        /// <summary>
        /// Returns the user-defined instruction for a specific interaction def, or empty string if none.
        /// </summary>
        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return PawnDiaryMod.Settings?.InstructionFor(interactionDef) ?? string.Empty;
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

            DiaryEvent diaryEvent = FindEvent(result.eventId);
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

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            if (result.success && !string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(result.povRole, EndpointUtility.BuildChatCompletionsUrl(result.endpointUrl), result.modelName);
            }

            QueueRecipientAfterInitiatorResult(diaryEvent, result);

            // Title follow-up: if Generate LLM titles is on and the main entry produced text
            // but the role has no stored title yet, queue a small title call. The title is tiny,
            // and the request is capped to TitleMaxTokens.
            if (result.success
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.generateTitles
                && !string.IsNullOrWhiteSpace(result.generatedText)
                && string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(result.povRole)))
            {
                QueueTitleRequest(diaryEvent, result.povRole, null);
            }
        }

        /// <summary>
        /// Applies a title-generation result to the event: stores the returned title on success
        /// or records the failure. Uses a separate per-POV status field so the main-entry
        /// recovery scan never touches it. If the title call fails, entries without an older
        /// stored title keep a date-only card header.
        /// </summary>
        private static void ApplyTitleResult(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (diaryEvent == null || result == null)
            {
                return;
            }

            if (result.success)
            {
                string title = result.generatedText;
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
        }

        /// <summary>
        /// Queues the title-generation follow-up for the given POV. Mirrors the
        /// <see cref="QueuePrompt"/> shape: pick a lane (pin to the same lane the main entry
        /// used so a sequential pair stays on one model), mark the title status as pending, and
        /// enqueue an <see cref="LlmGenerationRequest"/> with <c>isTitleRequest = true</c>.
        /// On failure (no API configured, lane unavailable) the per-POV title is left untouched.
        /// </summary>
        private void QueueTitleRequest(DiaryEvent diaryEvent, string povRole, ApiEndpointConfig primaryOverride)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            // Don't double-queue: an existing title or an in-flight title request both skip.
            if (!string.IsNullOrWhiteSpace(diaryEvent.TitleForRole(povRole)))
            {
                return;
            }

            if (diaryEvent.IsTitlePending(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (ShouldSkipFirstPersonGenerationForIncapacitation(FindLivePawnByLoadId(PawnIdForRole(diaryEvent, povRole))))
            {
                return;
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                return;
            }

            List<ApiEndpointConfig> targets = settings.ActiveEndpoints();
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            ApiEndpointConfig target = primaryOverride;
            if (target == null)
            {
                // Pin the title to the same lane the main entry used, when available — keeps a
                // paired event on one model (recipient title and initiator title both come from
                // the same lane). Falls back to round-robin for first-time titles on a new role.
                string mainEndpoint = diaryEvent.LlmEndpointForRole(povRole);
                string mainModel = diaryEvent.LlmModelForRole(povRole);
                if (!string.IsNullOrWhiteSpace(mainEndpoint) && !string.IsNullOrWhiteSpace(mainModel))
                {
                    foreach (ApiEndpointConfig candidate in targets)
                    {
                        if (string.Equals(EndpointUtility.BuildChatCompletionsUrl(candidate.url), mainEndpoint, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(candidate.model, mainModel, StringComparison.Ordinal))
                        {
                            target = candidate;
                            break;
                        }
                    }
                }

                if (target == null)
                {
                    int index = LlmClient.NextRoundRobinIndex() % targets.Count;
                    target = targets[index];
                }
            }

            diaryEvent.MarkTitleQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                isTitleRequest = true,
                systemPrompt = settings.systemPromptTitle,
                rawText = DiaryPromptBuilder.BuildTitlePrompt(diaryEvent, povRole),
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                failoverTargets = BuildFailoverTargets(targets, target),
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = TitleMaxTokens,
                temperature = settings.temperature
            });
        }

        /// <summary>
        /// After the initiator entry completes, either marks the recipient as failed (if the initiator failed)
        /// or re-evaluates the sequential queue so the recipient can generate with the initiator's text as context.
        /// </summary>
        private void QueueRecipientAfterInitiatorResult(DiaryEvent diaryEvent, LlmGenerationResult result)
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
                }

                return;
            }

            QueueSequentialPairwiseRewrite(diaryEvent);
        }

        /// <summary>
        /// Checks whether the pawn for a given POV role in an event has diary generation enabled,
        /// resolving the pawn via its saved diary record.
        /// </summary>
        private bool DiaryGenerationEnabledFor(DiaryEvent diaryEvent, string povRole)
        {
            // DiaryEvents store pawn IDs, not Pawn instances, so queue-time checks resolve through
            // the saved diary record for that POV.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return true;
            }

            if (initialArrivalScanPending && !diaryEvent.HasArrivalDescription())
            {
                return false;
            }

            if (EventFallsOutsideDiaryBoundsForPawn(diaryEvent, pawnId))
            {
                return false;
            }

            return FindDiaryByPawnId(pawnId)?.diaryGenerationEnabled ?? true;
        }

        /// <summary>
        /// Returns false only for a live pawn whose Consciousness capacity is below the hard
        /// first-person generation floor. Missing pawn/capacity data is treated as allowed so
        /// off-map or unusual saves do not permanently strand queued work.
        /// </summary>
        private static bool PawnConsciousEnoughForGeneration(Pawn pawn)
        {
            if (pawn?.health?.capacities == null || PawnCapacityDefOf.Consciousness == null)
            {
                return true;
            }

            return pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)
                >= MinimumConsciousnessForFirstPersonGeneration;
        }

        /// <summary>
        /// Marks a non-neutral POV as skipped when its live pawn is below the Consciousness floor.
        /// Returns true when generation should stop for this POV.
        /// </summary>
        private bool TryMarkIncapacitatedPovSkipped(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null
                || !DiaryEvent.RoleIsInitiatorOrRecipient(povRole)
                || !diaryEvent.CanQueueGeneration(povRole))
            {
                return false;
            }

            Pawn pawn = FindLivePawnByLoadId(PawnIdForRole(diaryEvent, povRole));
            if (!ShouldSkipFirstPersonGenerationForIncapacitation(pawn))
            {
                return false;
            }

            diaryEvent.MarkSkipped(povRole, IncapacitatedSkipReason());
            return true;
        }

        /// <summary>
        /// Shared event-factory hook: skips first-person generation immediately when the event is
        /// recorded for a pawn who is already too incapacitated to write.
        /// </summary>
        private static void MarkIncapacitatedPovSkipped(DiaryEvent diaryEvent, string povRole, Pawn pawn)
        {
            if (diaryEvent == null || !DiaryEvent.RoleIsInitiatorOrRecipient(povRole))
            {
                return;
            }

            if (ShouldSkipFirstPersonGenerationForIncapacitation(pawn))
            {
                diaryEvent.MarkSkipped(povRole, IncapacitatedSkipReason());
            }
        }

        private static bool ShouldSkipFirstPersonGenerationForIncapacitation(Pawn pawn)
        {
            return pawn != null && !PawnConsciousEnoughForGeneration(pawn);
        }

        private static string IncapacitatedSkipReason()
        {
            return "PawnDiary.Error.SkippedIncapacitated".Translate(
                Mathf.RoundToInt(MinimumConsciousnessForFirstPersonGeneration * 100f)).Resolve();
        }

        /// <summary>
        /// Resolves the LLM persona rule string for a given POV in an event, falling back to the XML default.
        /// </summary>
        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            // Missing records fall back to the XML default persona.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            Pawn pawn = FindLivePawnByLoadId(pawnId);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return DiaryPersonas.RuleFor(diary?.personaDefName, PromptEnchantments.ConsciousnessPersonaStateFor(pawn));
        }

        /// <summary>
        /// Resolves the optional hediff-based prompt enchantment for the POV pawn. Missing live pawn
        /// data simply means no enchantment, preserving neutral death/arrival and title flows.
        /// </summary>
        private string PromptEnchantmentRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            if (!DiaryPromptBuilder.ShouldResolvePromptEnchantment(diaryEvent))
            {
                return string.Empty;
            }

            string pawnId = PawnIdForRole(diaryEvent, povRole);
            Pawn pawn = FindLivePawnByLoadId(pawnId);
            return PromptEnchantments.RuleFor(pawn);
        }

        /// <summary>
        /// Finds a currently loaded pawn by RimWorld's stable unique load ID. Diary events save IDs,
        /// not Pawn references, so prompt-time state checks need this small lookup.
        /// </summary>
        private static Pawn FindLivePawnByLoadId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            if (Find.Maps != null)
            {
                for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
                {
                    Map map = Find.Maps[mapIndex];
                    if (map?.mapPawns?.AllPawns == null)
                    {
                        continue;
                    }

                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn != null && pawn.GetUniqueLoadID() == pawnId)
                        {
                            return pawn;
                        }
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && pawn.GetUniqueLoadID() == pawnId)
                    {
                        return pawn;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the pawn ID for a given POV role in a DiaryEvent (initiator or recipient).
        /// </summary>
        private static string PawnIdForRole(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            if (string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnId;
            }

            if (string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.initiatorPawnId;
            }

            return null;
        }
    }
}
