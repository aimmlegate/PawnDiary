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
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private static bool DualPovEnabled
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.dualPovGeneration;
            }
        }

        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
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

            if (DualPovEnabled && DiaryEvent.RoleIsInitiatorOrRecipient(povRole) && !diaryEvent.solo)
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
                    && !EventFallsAfterFinalDeathForPawn(diaryEvent, diaryEvent.initiatorPawnId))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole);
                }

                if (!diaryEvent.solo
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole)
                    && !EventFallsAfterFinalDeathForPawn(diaryEvent, diaryEvent.recipientPawnId))
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

                if (EventFallsAfterFinalDeathForPawn(diaryEvent, pawnId))
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
        /// Dispatches a pairwise event for LLM generation: sequential if dual-POV is on, else initiator-only.
        /// </summary>
        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            if (DualPovEnabled)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
            }
            else
            {
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
                QueueLlmRewrite(diaryEvent, DiaryEvent.RecipientRole);
            }
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

            // Persona is resolved at queue time so changing a pawn affects future generations
            // without rewriting prompts that were already sent or saved for debugging.
            string rawText = DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, povRole, PersonaRuleFor(diaryEvent, povRole));
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

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole);

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                string rawText = DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.InitiatorRole, PersonaRuleFor(diaryEvent, DiaryEvent.InitiatorRole));
                QueuePrompt(diaryEvent, DiaryEvent.InitiatorRole, rawText);
                return;
            }

            // If the recipient is disabled, stop here even if the initiator completed.
            if (!recipientEnabled)
            {
                return;
            }

            // Keep the old paired behavior when the initiator was supposed to generate but failed.
            if (initiatorEnabled && string.Equals(diaryEvent.initiatorStatus, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }

                return;
            }

            // Wait for initiator context only when the initiator is enabled. If the initiator is
            // disabled, the recipient can still generate from the base event prompt.
            if (initiatorEnabled
                && (!string.Equals(diaryEvent.initiatorStatus, DiaryEvent.CompleteStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(diaryEvent.initiatorGeneratedText)))
            {
                return;
            }

            if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
            {
                // Recipient prompt includes hidden initiator context only when that context exists.
                string rawText = initiatorEnabled
                    ? DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole))
                    : DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole));
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
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
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
        /// Chooses the primary API lane for a request from the given active lanes (non-empty).
        /// The recipient of a paired (dual-POV) event reuses the lane the initiator used — matched
        /// by the endpoint+model recorded on the event — so the sequential pair shares one model.
        /// Everything else takes the next lane in round-robin order to spread load across APIs.
        /// </summary>
        private static ApiEndpointConfig SelectApiTarget(DiaryEvent diaryEvent, string povRole, List<ApiEndpointConfig> targets, out string reason)
        {
            // Sequential pinning: keep the recipient on the initiator's lane when paired mode ran
            // the initiator first and recorded which endpoint+model it used (after failover, the
            // recorded meta is the lane that actually produced the initiator's entry).
            if (DualPovEnabled && DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
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
        /// Returns true when the interaction belongs to the "smalltalk" group and should be batched rather
        /// than recorded individually.
        /// </summary>
        private static bool IsSmallTalkInteraction(InteractionDef interactionDef)
        {
            // Only the low-stakes Small talk group batches. Heartfelt events such as DeepTalk
            // stay immediate because their group key is "heartfelt", not "smalltalk".
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && string.Equals(group.defName, SmallTalkGroupKey, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Dequeues a completed LLM result and applies it to the corresponding DiaryEvent, then kicks
        /// off the recipient side if dual-POV is active.
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

            diaryEvent.ApplyLlmResult(result);

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            if (result.success && !string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(result.povRole, EndpointUtility.BuildChatCompletionsUrl(result.endpointUrl), result.modelName);
            }

            QueueRecipientAfterInitiatorResult(diaryEvent, result);
        }

        /// <summary>
        /// After the initiator entry completes, either marks the recipient as failed (if the initiator failed)
        /// or re-evaluates the sequential queue so the recipient can generate with the initiator's text as context.
        /// </summary>
        private void QueueRecipientAfterInitiatorResult(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (!DualPovEnabled
                || diaryEvent == null
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

            if (EventFallsAfterFinalDeathForPawn(diaryEvent, pawnId))
            {
                return false;
            }

            return FindDiaryByPawnId(pawnId)?.diaryGenerationEnabled ?? true;
        }

        /// <summary>
        /// Resolves the LLM persona rule string for a given POV in an event, falling back to the XML default.
        /// </summary>
        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            // Missing records fall back to the XML default persona, which also covers old saves.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return DiaryPersonas.RuleFor(diary?.personaDefName);
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
