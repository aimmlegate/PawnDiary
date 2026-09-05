// DiaryGameComponent.MemoryDispatch.cs — main-thread M2 permit, receipt, and result transactions.
//
// LlmClient workers exchange detached messages through MemoryDispatchRuntimeBridge. This component
// is the sole adapter allowed to compare those messages with Scribed request/epoch state. No live
// Pawn or provider credential enters the pure policy or crosses to the worker.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Session-local and deliberately unscribed: saved invoked rows settle without resend on load.
        private long memoryInvocationSequenceForSession;

        /// <summary>
        /// Binds a detached staged row to the already-rendered lane variants. Every lane must map to
        /// one exact saved system/user pair; credentials and endpoint names remain outside the row.
        /// </summary>
        private static bool TryBindMemoryTransportContext(
            LlmGenerationRequest request,
            SavedActiveLogicalRequestV1 saved,
            Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants)
        {
            if (request == null || saved == null
                || saved.requestStateToken != MemoryRequestStateMachineContracts.Staged)
                return false;
            saved.sessionId = LlmClient.CurrentSessionId;
            MemoryLogicalRequestSnapshot snapshot = MemoryDispatchSavedAdapter.ToSnapshot(saved);
            if (!MemoryDispatchPolicy.ValidateRequest(snapshot)
                || !string.Equals(saved.eventIdOrOpportunityKey, request.eventId,
                    StringComparison.Ordinal)
                || !DiaryEvent.RoleEquals(saved.povRoleToken, request.povRole)) return false;

            MemoryDispatchTransportContext context = new MemoryDispatchTransportContext
            {
                logicalRequestId = saved.logicalRequestId,
                logicalRequestKey = saved.logicalRequestKey,
                requestPurposeToken = saved.requestPurposeToken,
                eventIdOrOpportunityKey = saved.eventIdOrOpportunityKey,
                povRoleToken = saved.povRoleToken,
                ownerPawnId = saved.ownerPawnId,
                ownerEpochToken = saved.ownerEpochToken,
                evidenceEpochToken = saved.evidenceEpochToken,
                ownerCancellationGeneration = saved.ownerCancellationGeneration,
                globalCancellationGeneration = saved.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    saved.optionalRequestInvalidationGeneration,
                primaryVariantKey = VariantKeyForExactPrompt(
                    saved, request.systemPrompt, request.rawText)
            };
            if (string.IsNullOrEmpty(context.primaryVariantKey)) return false;
            foreach (KeyValuePair<ApiLaneIdentity, LlmPromptVariant> pair
                in promptVariants ?? new Dictionary<ApiLaneIdentity, LlmPromptVariant>())
            {
                string key = VariantKeyForExactPrompt(
                    saved, pair.Value?.systemPrompt, pair.Value?.rawText);
                if (string.IsNullOrEmpty(key)) return false;
                context.laneVariantKeys[pair.Key] = key;
            }
            request.memoryDispatch = context;
            return true;
        }

        private static string VariantKeyForExactPrompt(
            SavedActiveLogicalRequestV1 saved,
            string systemPrompt,
            string userPrompt)
        {
            string match = string.Empty;
            for (int index = 0; saved?.frozenVariants != null
                && index < saved.frozenVariants.Count; index++)
            {
                SavedFrozenPromptVariantV1 variant = saved.frozenVariants[index];
                if (variant == null
                    || !string.Equals(variant.systemPrompt, systemPrompt ?? string.Empty,
                        StringComparison.Ordinal)
                    || !string.Equals(variant.userPrompt, userPrompt ?? string.Empty,
                        StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(match)
                    && !string.Equals(match, variant.variantKey, StringComparison.Ordinal))
                    return string.Empty;
                match = variant.variantKey;
            }
            return match;
        }

        /// <summary>
        /// Settles every Scribed active row at load without reactivation. Pre-invocation normal pages
        /// become retryable; any row with a committed exposure becomes terminal so a process restart
        /// can never create an ambiguous duplicate provider send.
        /// </summary>
        private void SettleLoadedMemoryDispatchRows()
        {
            memoryInvocationSequenceForSession = 0;
            invokedGenerationCutoffs.Reset();
            RepairLoadedSummaryWordingOpportunities();
            bool changed = false;
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                changed = SettleLoadedMemoryDispatchRole(
                    diaryEvent, DiaryEvent.InitiatorRole) || changed;
                changed = SettleLoadedMemoryDispatchRole(
                    diaryEvent, DiaryEvent.RecipientRole) || changed;
                changed = SettleLoadedMemoryDispatchRole(
                    diaryEvent, DiaryEvent.NeutralRole) || changed;
            }

            // Optional coordinator work has no player page to restore. Deterministic fallback state
            // already exists, so every loaded row settles/drops and none enters the transport queue.
            long terminalTick = Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0);
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 saved = activeMemoryCoordinatorRequests[index];
                MemoryLoadSettlementPlan plan = MemoryDispatchPolicy.PlanLoadedRequestSettlement(
                    MemoryDispatchSavedAdapter.ToSnapshot(saved));
                if (plan.valid) ApplyLoadedMemorySettlementAccounting(saved, plan);
                if (saved?.requestPurposeToken == MemoryDispatchTokens.SummaryWording)
                {
                    SummaryWordingOpportunitySnapshot opportunity;
                    if (MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                            saved.eventIdOrOpportunityKey, out opportunity))
                        ApplySummaryTerminal(
                            opportunity, MemoryOptionalWordingDispositionTokens.Failed);
                }
                AppendTerminalMemoryAttemptAudits(
                    saved,
                    plan.valid
                        ? plan.outcomeToken
                        : MemoryDispatchTokens.Invalid,
                    terminalTick);
                changed = true;
            }
            activeMemoryCoordinatorRequests?.Clear();
            RebuildMemorySizeIndexes();
            if (changed) DiaryStateVersion.Bump();
        }

        private bool SettleLoadedMemoryDispatchRole(
            DiaryEvent diaryEvent,
            string povRole)
        {
            SavedActiveLogicalRequestV1 saved =
                diaryEvent?.ActiveMemoryLogicalRequestForRole(povRole);
            if (saved == null) return false;
            MemoryLoadSettlementPlan plan = MemoryDispatchPolicy.PlanLoadedRequestSettlement(
                MemoryDispatchSavedAdapter.ToSnapshot(saved));
            bool mayHaveBeenInvoked = plan.valid
                ? plan.hadCommittedInvocation
                : MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(saved);
            if (plan.valid) ApplyLoadedMemorySettlementAccounting(saved, plan);
            AppendTerminalMemoryAttemptAudits(
                saved,
                plan.valid
                    ? plan.outcomeToken
                    : MemoryDispatchTokens.Invalid,
                Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0));
            if (mayHaveBeenInvoked)
            {
                diaryEvent.MarkSkipped(
                    povRole,
                    "PawnDiary.Error.GenerationInterruptedAfterSend".Translate());
                if (!diaryEvent.solo
                    && DiaryEvent.RoleEquals(povRole, DiaryEvent.InitiatorRole)
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkSkipped(
                        DiaryEvent.RecipientRole,
                        "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }
            }
            diaryEvent.SetActiveMemoryLogicalRequestForRole(povRole, null);
            return true;
        }

        /// <summary>
        /// Applies only the accounting deltas explicitly planned from a valid loaded request. Rows
        /// are never reactivated; the repaired exposure/winner state is committed before terminal
        /// audit and removal so load cannot forget a prior provider boundary.
        /// </summary>
        private void ApplyLoadedMemorySettlementAccounting(
            SavedActiveLogicalRequestV1 saved,
            MemoryLoadSettlementPlan plan)
        {
            if (saved == null || plan == null || !plan.valid || !plan.hadCommittedInvocation)
                return;
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            SavedActiveLogicalAttemptV1 winner = FindSavedAttempt(
                saved, plan.repairedNarrativeUseWinnerAttemptOrdinal);
            for (int index = 0; index < plan.potentialExposureAttemptOrdinals.Count; index++)
            {
                SavedActiveLogicalAttemptV1 attempt = FindSavedAttempt(
                    saved, plan.potentialExposureAttemptOrdinals[index]);
                if (attempt == null)
                {
                    RecordMemoryDiagnostic("other", "owner");
                    continue;
                }
                SavedFrozenPromptVariantV1 variant = FindVariant(saved, attempt?.variantKey);
                MemoryInvocationCommitPlan mutation = new MemoryInvocationCommitPlan
                {
                    applyPotentialExposure = true,
                    applyNarrativeUse = ReferenceEquals(attempt, winner)
                        && !attempt.narrativeUseApplied
                };
                if (!CanApplyInvocationAccounting(
                        state,
                        variant,
                        mutation,
                        saved,
                        Math.Max(1, attempt.invocationTick)))
                {
                    RecordMemoryDiagnostic("other", "owner");
                    continue;
                }
                if (ApplyInvocationAccounting(
                        state, variant, mutation, saved, Math.Max(1, attempt.invocationTick)))
                    MarkMemoryLibraryStatusProjectionDirty();
                attempt.potentialExposureApplied = true;
                if (mutation.applyNarrativeUse) attempt.narrativeUseApplied = true;
            }

            if (winner != null && !winner.narrativeUseApplied)
            {
                SavedFrozenPromptVariantV1 variant = FindVariant(saved, winner.variantKey);
                MemoryInvocationCommitPlan mutation = new MemoryInvocationCommitPlan
                {
                    applyNarrativeUse = true
                };
                if (CanApplyInvocationAccounting(
                        state,
                        variant,
                        mutation,
                        saved,
                        Math.Max(1, winner.invocationTick)))
                {
                    if (ApplyInvocationAccounting(
                            state, variant, mutation, saved, Math.Max(1, winner.invocationTick)))
                        MarkMemoryLibraryStatusProjectionDirty();
                    winner.narrativeUseApplied = true;
                }
                else
                {
                    RecordMemoryDiagnostic("other", "owner");
                }
            }
            if (winner != null)
            {
                saved.narrativeUseWinnerAttemptOrdinal = winner.attemptOrdinal;
                saved.narrativeUseWinnerVariantKey = winner.variantKey ?? string.Empty;
            }
        }

        private static SavedActiveLogicalAttemptV1 FindSavedAttempt(
            SavedActiveLogicalRequestV1 saved,
            int attemptOrdinal)
        {
            for (int index = 0; saved?.activeAttempts != null
                && index < saved.activeAttempts.Count; index++)
            {
                SavedActiveLogicalAttemptV1 attempt = saved.activeAttempts[index];
                if (attempt != null && attempt.attemptOrdinal == attemptOrdinal) return attempt;
            }
            return null;
        }

        /// <summary>Drains permits before receipts; workers await each main-thread decision.</summary>
        private void DrainMemoryDispatchHandoffs()
        {
            MemoryInvocationPermitRequest permitRequest;
            while (MemoryDispatchRuntimeBridge.TryDequeuePermit(out permitRequest))
            {
                MemoryInvocationCommitPermitV1 permit = null;
                try
                {
                    permit = TryCommitMemoryInvocation(permitRequest);
                }
                finally
                {
                    MemoryDispatchRuntimeBridge.ResolvePermit(permitRequest, permit);
                }
            }

            MemoryInvocationReceiptRequest receiptRequest;
            while (MemoryDispatchRuntimeBridge.TryDequeueReceipt(out receiptRequest))
            {
                bool accepted = false;
                try
                {
                    accepted = TryApplyMemoryInvocationReceipt(receiptRequest);
                }
                finally
                {
                    MemoryDispatchRuntimeBridge.ResolveReceipt(receiptRequest, accepted);
                }
            }
        }

        private MemoryInvocationCommitPermitV1 TryCommitMemoryInvocation(
            MemoryInvocationPermitRequest pending)
        {
            if (pending == null || pending.context == null
                || pending.sessionId != LlmClient.CurrentSessionId
                || memoryInvocationSequenceForSession == long.MaxValue)
            {
                return null;
            }

            SavedActiveLogicalRequestV1 saved = FindActiveMemoryDispatchRequest(
                pending.context.eventIdOrOpportunityKey,
                pending.context.povRoleToken);
            if (!TransportIdentityMatches(saved, pending.context)) return null;

            SavedFrozenPromptVariantV1 variant = FindVariant(saved, pending.variantKey);
            if (variant == null
                || !string.Equals(variant.systemPrompt, pending.systemPrompt,
                    StringComparison.Ordinal)
                || !string.Equals(variant.userPrompt, pending.userPrompt,
                    StringComparison.Ordinal))
            {
                return null;
            }

            MemoryDispatchFenceSnapshot fence = CurrentFence(saved, false);
            if (fence == null) return null;
            long invocationTick = Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0);

            // Plan against a detached copy first. Failed accounting or sequence validation therefore
            // cannot leave a half-prepared saved attempt behind.
            MemoryLogicalRequestSnapshot detached = MemoryDispatchSavedAdapter.ToSnapshot(saved);
            MemoryLogicalAttemptSnapshot prepared;
            if (!MemoryDispatchPolicy.TryPlanPreparedAttempt(
                    detached,
                    pending.variantKey,
                    pending.attemptOriginToken,
                    pending.predecessorAttemptOrdinal,
                    out prepared))
            {
                return null;
            }
            detached.attempts.Add(prepared);
            detached.lastIssuedAttemptOrdinal = prepared.attemptOrdinal;
            MemoryInvocationCommitPlan planned = MemoryDispatchPolicy.PlanInvocationCommit(
                detached,
                prepared.attemptOrdinal,
                fence,
                memoryInvocationSequenceForSession,
                invocationTick);
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            if (!planned.canCommit || !CanApplyInvocationAccounting(
                    state, variant, planned, saved, invocationTick))
            {
                return null;
            }
            bool optionalMemoryRequest = MemoryDispatchTokens.IsOptionalPurpose(
                saved.requestPurposeToken);
            if (optionalMemoryRequest
                && !invokedGenerationCutoffs.CanRegister(
                    saved.sessionId,
                    saved.ownerPawnId,
                    saved.ownerEpochToken,
                    saved.ownerCancellationGeneration,
                    saved.globalCancellationGeneration,
                    saved.logicalRequestId,
                    planned.permit.invocationSequence,
                    LlmClient.MaxQueuedRequests)) return null;

            SavedActiveLogicalAttemptV1 persistedAttempt;
            MemoryInvocationCommitPlan committed;
            if (!MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    pending.variantKey,
                    pending.attemptOriginToken,
                    pending.predecessorAttemptOrdinal,
                    out persistedAttempt)
                || !MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved,
                    persistedAttempt.attemptOrdinal,
                    fence,
                    memoryInvocationSequenceForSession,
                    invocationTick,
                    out committed))
            {
                RollBackPreparedAttempt(saved, persistedAttempt);
                return null;
            }

            if (ApplyInvocationAccounting(state, variant, committed, saved, invocationTick))
                MarkMemoryLibraryStatusProjectionDirty();
            if (optionalMemoryRequest
                && !invokedGenerationCutoffs.TryRegister(
                    saved.sessionId,
                    saved.ownerPawnId,
                    saved.ownerEpochToken,
                    saved.ownerCancellationGeneration,
                    saved.globalCancellationGeneration,
                    saved.logicalRequestId,
                    committed.permit.invocationSequence,
                    LlmClient.MaxQueuedRequests))
            {
                RecordMemoryDiagnosticOnce("other", "dispatch");
                return null;
            }
            memoryInvocationSequenceForSession = committed.nextInvocationSequence;
            RefreshMemoryDispatchSizeIndex(state);
            return committed.permit;
        }

        private bool TryApplyMemoryInvocationReceipt(MemoryInvocationReceiptRequest pending)
        {
            MemoryInvocationCommitPermitV1 permit = pending?.permit;
            if (pending == null || permit == null
                || pending.sessionId != LlmClient.CurrentSessionId)
            {
                return false;
            }

            SavedActiveLogicalRequestV1 saved = FindActiveMemoryDispatchRequest(
                permit.eventIdOrOpportunityKey,
                permit.povRoleToken);
            MemoryDispatchFenceSnapshot fence = CurrentFence(saved, true);
            MemoryTerminalCallbackPlan plan = fence == null
                ? new MemoryTerminalCallbackPlan()
                : MemoryDispatchSavedAdapter.PlanTerminalCallback(
                    saved,
                    permit,
                    fence,
                    pending.outcomeToken,
                    pending.providerReturnedUsableResult);
            if (!plan.accepted) return plan.duplicate;

            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            bool accountingChanged = false;
            if (plan.applyConfirmedExposure)
            {
                SavedFrozenPromptVariantV1 variant = FindVariant(saved, permit.variantKey);
                if (ApplyConfirmedExposure(
                    owner,
                    variant,
                    permit.invocationTick))
                {
                    MarkMemoryLibraryStatusProjectionDirty();
                    accountingChanged = true;
                }
            }
            bool applied = MemoryDispatchSavedAdapter.MarkReceiptApplied(
                saved,
                permit.attemptOrdinal,
                pending.outcomeToken,
                Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0));
            if (applied || accountingChanged) RefreshMemoryDispatchSizeIndex(owner);
            return applied;
        }

        /// <summary>
        /// Validates a terminal result after its receipt and marks publication once. The caller clears
        /// the active row only after the existing page adapter finishes successfully.
        /// </summary>
        private bool TryBeginMemoryResultApply(
            DiaryEvent diaryEvent,
            LlmGenerationResult result,
            out SavedActiveLogicalRequestV1 saved)
        {
            saved = null;
            saved = diaryEvent?.ActiveMemoryLogicalRequestForRole(result.povRole);
            if (saved == null)
            {
                return string.IsNullOrWhiteSpace(result?.memoryLogicalRequestId)
                    && result?.memoryInvocationPermit == null;
            }
            return TryBeginMemoryResultApply(saved, result);
        }

        /// <summary>Shared result gate for DiaryEvent-owned and component-owned logical requests.</summary>
        private bool TryBeginMemoryResultApply(
            SavedActiveLogicalRequestV1 saved,
            LlmGenerationResult result)
        {
            if (saved == null || result == null
                || result.sessionId != LlmClient.CurrentSessionId
                || saved.sessionId != result.sessionId
                || !string.Equals(saved.eventIdOrOpportunityKey,
                    result.eventId, StringComparison.Ordinal)
                || !DiaryEvent.RoleEquals(saved.povRoleToken, result.povRole)) return false;
            MemoryInvocationCommitPermitV1 permit = result?.memoryInvocationPermit;
            bool hasMemoryIdentity = !string.IsNullOrWhiteSpace(
                result?.memoryLogicalRequestId);
            if (permit == null)
            {
                if (!hasMemoryIdentity) return true;
                // A live pre-permit lane/queue failure may return without a physical-attempt row.
                // It owns the exact active logical request and may settle/retry only when that saved
                // row is valid and proves no invocation was committed.
                return saved != null
                    && string.Equals(
                        saved.logicalRequestId,
                        result.memoryLogicalRequestId,
                        StringComparison.Ordinal)
                    && MemoryDispatchPolicy.ValidateRequest(
                        MemoryDispatchSavedAdapter.ToSnapshot(saved))
                    && !MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(saved);
            }
            if (!hasMemoryIdentity
                || !string.Equals(
                    saved?.logicalRequestId,
                    result.memoryLogicalRequestId,
                    StringComparison.Ordinal)) return false;
            string terminalOutcome = result.memoryDispatchTerminalOutcomeToken;
            if (!MemoryDispatchTokens.IsTerminalOutcome(terminalOutcome)
                || result.success != string.Equals(
                    terminalOutcome,
                    MemoryDispatchTokens.Success,
                    StringComparison.Ordinal)) return false;
            MemoryDispatchFenceSnapshot fence = CurrentFence(saved, true);
            MemoryTerminalCallbackPlan plan = fence == null
                ? new MemoryTerminalCallbackPlan()
                : MemoryDispatchSavedAdapter.PlanTerminalCallback(
                    saved,
                    permit,
                    fence,
                    terminalOutcome,
                    result.success);
            bool applied = plan.accepted && MemoryDispatchSavedAdapter.MarkResultApplied(
                saved, permit.attemptOrdinal);
            if (applied) RefreshMemoryDispatchSizeIndex(
                FindCurrentMemoryEnvelope(saved.ownerPawnId));
            return applied;
        }

        /// <summary>
        /// Settles one active M2 owner before player-authored or integration-authored prose replaces
        /// its page. The replacement itself remains authoritative; late provider callbacks then find
        /// neither saved ownership nor a live physical-send claim to restore.
        /// </summary>
        internal void SettleActiveMemoryRequestForPageReplacement(
            DiaryEvent diaryEvent,
            string povRole)
        {
            SavedActiveLogicalRequestV1 saved = diaryEvent?
                .ActiveMemoryLogicalRequestForRole(povRole);
            if (saved == null) return;

            AppendTerminalMemoryAttemptAudits(
                saved,
                MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(saved)
                    ? MemoryDispatchTokens.CancelledPostInvocation
                    : MemoryDispatchTokens.CancelledPreInvocation,
                Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0));
            diaryEvent.SetActiveMemoryLogicalRequestForRole(povRole, null);
            MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                saved.logicalRequestId);
            invokedGenerationCutoffs.Settle(saved.logicalRequestId);
            RefreshMemoryDispatchSizeIndex(
                FindCurrentMemoryEnvelope(saved.ownerPawnId));
        }

        /// <summary>
        /// Refreshes only the request owner plus the bounded component/request subtotal. A corrupt
        /// or stale transient index falls back to the full rebuild, but ordinary permit, receipt,
        /// result, and settlement mutations never rescan unrelated owner payloads.
        /// </summary>
        private void RefreshMemoryDispatchSizeIndex(PawnKnowledgeState owner)
        {
            if (!RefreshMemorySizeIndexForOwner(owner)) RebuildMemorySizeIndexes();
        }

        private static bool TransportIdentityMatches(
            SavedActiveLogicalRequestV1 saved,
            MemoryDispatchTransportContext context)
        {
            return saved != null && context != null
                && string.Equals(saved.logicalRequestId, context.logicalRequestId,
                    StringComparison.Ordinal)
                && string.Equals(saved.logicalRequestKey, context.logicalRequestKey,
                    StringComparison.Ordinal)
                && string.Equals(saved.requestPurposeToken, context.requestPurposeToken,
                    StringComparison.Ordinal)
                && string.Equals(saved.eventIdOrOpportunityKey,
                    context.eventIdOrOpportunityKey, StringComparison.Ordinal)
                && DiaryEvent.RoleEquals(saved.povRoleToken, context.povRoleToken)
                && string.Equals(saved.ownerPawnId, context.ownerPawnId,
                    StringComparison.Ordinal)
                && string.Equals(saved.ownerEpochToken, context.ownerEpochToken,
                    StringComparison.Ordinal)
                && string.Equals(saved.evidenceEpochToken, context.evidenceEpochToken,
                    StringComparison.Ordinal)
                && saved.ownerCancellationGeneration
                    == context.ownerCancellationGeneration
                && saved.globalCancellationGeneration
                    == context.globalCancellationGeneration
                && saved.optionalRequestInvalidationGeneration
                    == context.optionalRequestInvalidationGeneration;
        }

        private MemoryDispatchFenceSnapshot CurrentFence(
            SavedActiveLogicalRequestV1 saved,
            bool allowInvocationWinner)
        {
            if (saved == null || saved.sessionId != LlmClient.CurrentSessionId) return null;
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            if (state == null || !string.Equals(
                    state.autobiographicalEpochToken,
                    saved.ownerEpochToken,
                    StringComparison.Ordinal)) return null;
            bool optional = MemoryDispatchTokens.IsOptionalPurpose(saved.requestPurposeToken);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            // A newly published policy is effective immediately. Until its component transaction
            // reconciles, no unsent optional request may obtain an invocation permit under stale
            // saved generations. Already-invoked receipt/result settlement remains eligible below.
            if (optional && !allowInvocationWinner && !MemoryPolicyIsReconciled()) return null;
            long currentGlobal = optional
                ? globalOptionalRequestCancellationGeneration
                : saved.globalCancellationGeneration;
            long currentOptional = optional
                ? policy?.optionalRequestInvalidationGeneration ?? 0
                : saved.optionalRequestInvalidationGeneration;
            bool generationsCurrent = state.requestCancellationGeneration
                    == saved.ownerCancellationGeneration
                && currentGlobal == saved.globalCancellationGeneration
                && currentOptional == saved.optionalRequestInvalidationGeneration;
            long invokedSequence = GreatestCommittedInvocationSequence(saved);
            bool invocationWinner = optional && allowInvocationWinner && !generationsCurrent
                && invokedGenerationCutoffs.AllowsInvocationWinner(
                    saved.sessionId,
                    saved.ownerPawnId,
                    saved.ownerEpochToken,
                    saved.ownerCancellationGeneration,
                    saved.globalCancellationGeneration,
                    saved.logicalRequestId,
                    invokedSequence);
            if (!generationsCurrent && !invocationWinner) return null;
            return new MemoryDispatchFenceSnapshot
            {
                sessionId = LlmClient.CurrentSessionId,
                ownerPawnId = saved.ownerPawnId,
                ownerEpochToken = state.autobiographicalEpochToken,
                ownerCancellationGeneration = invocationWinner
                    ? saved.ownerCancellationGeneration
                    : state.requestCancellationGeneration,
                globalCancellationGeneration = invocationWinner
                    ? saved.globalCancellationGeneration
                    : currentGlobal,
                optionalRequestInvalidationGeneration = invocationWinner
                    ? saved.optionalRequestInvalidationGeneration
                    : currentOptional
            };
        }

        private SavedActiveLogicalRequestV1 FindActiveMemoryDispatchRequest(
            string eventOrOpportunityKey,
            string povRole)
        {
            DiaryEvent diaryEvent = events?.FindEvent(eventOrOpportunityKey);
            SavedActiveLogicalRequestV1 eventOwned = diaryEvent?
                .ActiveMemoryLogicalRequestForRole(povRole);
            if (eventOwned != null) return eventOwned;
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 candidate = activeMemoryCoordinatorRequests[index];
                if (candidate != null
                    && string.Equals(candidate.eventIdOrOpportunityKey,
                        eventOrOpportunityKey, StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(candidate.povRoleToken, povRole)) return candidate;
            }
            return null;
        }

        private static long GreatestCommittedInvocationSequence(
            SavedActiveLogicalRequestV1 saved)
        {
            long greatest = 0;
            for (int index = 0; saved?.activeAttempts != null
                && index < saved.activeAttempts.Count; index++)
                greatest = Math.Max(greatest,
                    saved.activeAttempts[index]?.invocationSequence ?? 0);
            return greatest;
        }

        private static SavedFrozenPromptVariantV1 FindVariant(
            SavedActiveLogicalRequestV1 saved,
            string variantKey)
        {
            for (int index = 0; saved?.frozenVariants != null
                && index < saved.frozenVariants.Count; index++)
            {
                SavedFrozenPromptVariantV1 row = saved.frozenVariants[index];
                if (row != null && string.Equals(row.variantKey, variantKey,
                    StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private static void RollBackPreparedAttempt(
            SavedActiveLogicalRequestV1 saved,
            SavedActiveLogicalAttemptV1 prepared)
        {
            if (saved == null || prepared == null || saved.activeAttempts == null) return;
            int last = saved.activeAttempts.Count - 1;
            if (last >= 0 && ReferenceEquals(saved.activeAttempts[last], prepared))
            {
                saved.activeAttempts.RemoveAt(last);
                saved.lastIssuedAttemptOrdinal = last;
            }
        }

        private bool CanApplyInvocationAccounting(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            MemoryInvocationCommitPlan plan,
            SavedActiveLogicalRequestV1 request,
            long invocationTick)
        {
            if (variant?.receiptPlan == null || plan == null) return false;
            if (!plan.applyPotentialExposure && !plan.applyNarrativeUse) return true;
            if (state == null || state.statusRevision == long.MaxValue) return false;
            List<SavedMemoryThreadRoot> roots = FindEvidenceRoots(state, variant.receiptPlan);
            for (int index = 0; index < roots.Count; index++)
                if (roots[index].statusRevision == long.MaxValue) return false;
            if (!plan.applyNarrativeUse) return true;

            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            for (int index = 0; index < blocks.Count; index++)
                if (blocks[index].automaticInclusionCount == long.MaxValue) return false;
            List<SavedMemoryRepetitionGuardRow> guardRows;
            return TryCollectRequiredNarrativeGuardRows(
                    state, variant.receiptPlan, out guardRows)
                && CanAdmitNarrativeGuardMutation(
                    state,
                    variant.receiptPlan,
                    guardRows,
                    request,
                    invocationTick);
        }

        private bool ApplyInvocationAccounting(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            MemoryInvocationCommitPlan plan,
            SavedActiveLogicalRequestV1 request,
            long invocationTick)
        {
            if (state == null || variant?.receiptPlan == null
                || (!plan.applyPotentialExposure && !plan.applyNarrativeUse)) return false;
            List<SavedMemoryRepetitionGuardRow> requiredGuardRows = null;
            if (plan.applyNarrativeUse
                && !TryCollectRequiredNarrativeGuardRows(
                    state, variant.receiptPlan, out requiredGuardRows)) return false;
            if (plan.applyNarrativeUse
                && !CanAdmitNarrativeGuardMutation(
                    state,
                    variant.receiptPlan,
                    requiredGuardRows,
                    request,
                    invocationTick)) return false;
            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            List<SavedMemoryThreadRoot> roots = FindEvidenceRoots(state, variant.receiptPlan);
            bool changed = false;
            for (int index = 0; index < blocks.Count; index++)
            {
                SavedMemoryBlock block = blocks[index];
                long nextExposureTick = Math.Max(block.lastProviderExposureTick, invocationTick);
                if (block.providerExposureState != "confirmed_sent"
                    && block.providerExposureState != "potentially_sent")
                {
                    block.providerExposureState = "potentially_sent";
                    changed = true;
                }
                if (block.lastProviderExposureTick != nextExposureTick)
                {
                    block.lastProviderExposureTick = nextExposureTick;
                    changed = true;
                }
                if (plan.applyNarrativeUse)
                {
                    block.automaticInclusionCount++;
                    block.lastAutomaticIncludedTick = invocationTick;
                    block.lastAutomaticIncludedEntryOrdinal =
                        state.completedDiaryEntryOrdinal;
                    changed = true;
                }
            }

            if (plan.applyNarrativeUse && requiredGuardRows != null)
            {
                string sourceOccurrence = variant.receiptPlan.evidenceEntries.Count > 0
                    ? variant.receiptPlan.evidenceEntries[0]?.sourceOccurrenceId ?? string.Empty
                    : string.Empty;
                if (state.repetitionGuardRows == null)
                    state.repetitionGuardRows = new List<SavedMemoryRepetitionGuardRow>();
                for (int index = 0; index < requiredGuardRows.Count; index++)
                {
                    SavedMemoryRepetitionGuardRow guard = requiredGuardRows[index];
                    if (!state.repetitionGuardRows.Contains(guard))
                        state.repetitionGuardRows.Add(guard);
                    guard.automaticInclusionCount++;
                    guard.lastAutomaticIncludedTick = invocationTick;
                    guard.lastAutomaticIncludedEntryOrdinal = state.completedDiaryEntryOrdinal;
                    guard.lastSourceOccurrenceId = sourceOccurrence;
                    guard.lastCommittedLogicalRequestId = request.logicalRequestId;
                    guard.lastCommittedEvidenceSetFingerprint =
                        variant.receiptPlan.evidenceSetFingerprint;
                    changed = true;
                }
                // The saved contract orders these rows by their complete unique identity. Keeping the
                // order canonical at creation time also makes a second use find and update the same row
                // instead of materializing a duplicate.
                state.repetitionGuardRows.Sort(CompareRepetitionGuardRows);
            }
            if (!changed) return false;
            for (int index = 0; index < roots.Count; index++) roots[index].statusRevision++;
            state.statusRevision++;
            return true;
        }

        /// <summary>
        /// Refuses a first-use guard mutation before touching saved truth when its complete live rows
        /// would exceed the XML owner/global table cap or the shared logical-byte budget. Existing
        /// rows may still update at a full row cap, but their exact string-size delta must fit too.
        /// </summary>
        private bool CanAdmitNarrativeGuardMutation(
            PawnKnowledgeState state,
            SavedFrozenEvidenceReceiptPlanV1 receipt,
            List<SavedMemoryRepetitionGuardRow> requiredRows,
            SavedActiveLogicalRequestV1 request,
            long invocationTick)
        {
            if (state == null || receipt == null || requiredRows == null || request == null
                || invocationTick <= 0) return false;

            int ownerCurrentRows = NonNullGuardRowCount(state.repetitionGuardRows);
            int missingRows = 0;
            for (int index = 0; index < requiredRows.Count; index++)
            {
                SavedMemoryRepetitionGuardRow row = requiredRows[index];
                if (row == null) return false;
                if (state.repetitionGuardRows == null
                    || !state.repetitionGuardRows.Contains(row)) missingRows++;
            }

            int ownerCap;
            int globalCap;
            ReadCapacityPair(
                "guardRowsOwnerGlobal",
                512,
                10000,
                2048,
                40000,
                out ownerCap,
                out globalCap);
            int globalCurrentRows;
            if (ownerCap <= 0 || globalCap <= 0
                || !TryCountCurrentGlobalGuardRows(state, out globalCurrentRows)
                || (long)ownerCurrentRows + missingRows > ownerCap
                || (long)globalCurrentRows + missingRows > globalCap) return false;

            string sourceOccurrence = receipt.evidenceEntries != null
                    && receipt.evidenceEntries.Count > 0
                ? receipt.evidenceEntries[0]?.sourceOccurrenceId ?? string.Empty
                : string.Empty;
            long activeByteDelta = 0;
            for (int index = 0; index < requiredRows.Count; index++)
            {
                SavedMemoryRepetitionGuardRow current = requiredRows[index];
                bool alreadySaved = state.repetitionGuardRows != null
                    && state.repetitionGuardRows.Contains(current);
                long beforeBytes = 0;
                if (alreadySaved)
                {
                    MemoryLogicalSizeResult before = MemoryLogicalPayloadSizer.Size(current);
                    if (!before.valid || before.totalBytes < 0) return false;
                    beforeBytes = before.totalBytes;
                }

                SavedMemoryRepetitionGuardRow projected = ProjectNarrativeGuardRow(
                    current,
                    sourceOccurrence,
                    request.logicalRequestId,
                    receipt.evidenceSetFingerprint,
                    invocationTick,
                    state.completedDiaryEntryOrdinal);
                MemoryLogicalSizeResult after = MemoryLogicalPayloadSizer.Size(projected);
                if (!after.valid || after.totalBytes < 0) return false;
                try
                {
                    activeByteDelta = checked(
                        activeByteDelta + checked(after.totalBytes - beforeBytes));
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            MemoryOwnerByteTotals owner = GetOwnerByteTotals(state.pawnId);
            MemoryPayloadBudgetTotals global = GetGlobalBudgetTotals();
            if (!owner.valid || global.globalActiveBytes < 0
                || global.globalImportedBytes < 0) return false;
            MemoryBudgetDecision decision = ActiveMemoryPayloadBudget.TryAdmit(
                CurrentMemoryBudgetLimits(),
                owner.activeBytes,
                owner.importedBytes,
                activeByteDelta,
                0,
                global);
            return decision.outcome == MemoryBudgetOutcome.Admitted;
        }

        private bool TryCountCurrentGlobalGuardRows(
            PawnKnowledgeState target,
            out int count)
        {
            count = 0;
            bool targetCounted = false;
            var owners = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                PawnKnowledgeState owner = diary?.knowledgeState;
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || owner == null || !owner.IsCurrentSchema()
                    || !owners.Add(diary.pawnId)) continue;
                if (ReferenceEquals(owner, target)) targetCounted = true;
                int ownerRows = NonNullGuardRowCount(owner.repetitionGuardRows);
                try
                {
                    count = checked(count + ownerRows);
                }
                catch (OverflowException)
                {
                    count = 0;
                    return false;
                }
            }
            return targetCounted;
        }

        private static int NonNullGuardRowCount(
            List<SavedMemoryRepetitionGuardRow> rows)
        {
            int count = 0;
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index] != null) count++;
            return count;
        }

        private static SavedMemoryRepetitionGuardRow ProjectNarrativeGuardRow(
            SavedMemoryRepetitionGuardRow current,
            string sourceOccurrence,
            string logicalRequestId,
            string evidenceSetFingerprint,
            long invocationTick,
            long completedEntryOrdinal)
        {
            return current == null ? null : new SavedMemoryRepetitionGuardRow
            {
                schemaVersion = current.schemaVersion,
                ownerEpochToken = current.ownerEpochToken,
                guardKind = current.guardKind,
                guardKey = current.guardKey,
                lastAutomaticIncludedTick = invocationTick,
                lastAutomaticIncludedEntryOrdinal = completedEntryOrdinal,
                automaticInclusionCount = current.automaticInclusionCount + 1,
                lastSourceOccurrenceId = sourceOccurrence ?? string.Empty,
                lastCommittedLogicalRequestId = logicalRequestId ?? string.Empty,
                lastCommittedEvidenceSetFingerprint = evidenceSetFingerprint ?? string.Empty
            };
        }

        /// <summary>
        /// Resolves every non-record guard frozen into one narrative receipt. Missing rows are planned
        /// as detached current-epoch rows; no saved state changes until the complete receipt has been
        /// validated, so malformed, duplicate, or saturated guard sets cannot partially spend cooldown.
        /// </summary>
        private static bool TryCollectRequiredNarrativeGuardRows(
            PawnKnowledgeState state,
            SavedFrozenEvidenceReceiptPlanV1 receipt,
            out List<SavedMemoryRepetitionGuardRow> requiredRows)
        {
            requiredRows = new List<SavedMemoryRepetitionGuardRow>();
            if (state == null || receipt?.guardEntries == null
                || string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)) return false;

            var requested = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < receipt.guardEntries.Count; index++)
            {
                SavedFrozenGuardEntryV1 frozen = receipt.guardEntries[index];
                if (frozen == null) return false;
                if (string.Equals(
                    frozen.guardKind,
                    MemoryRepetitionGuardKinds.Record,
                    StringComparison.Ordinal)) continue;
                if (!MemoryRepetitionGuardKinds.IsSavedRowKind(frozen.guardKind)
                    || !MemoryRepetitionGuardPolicy.IsCanonicalIdentity(
                        frozen.guardKind, frozen.guardKey)) return false;

                string tuple = frozen.guardKind + "\n" + frozen.guardKey;
                if (!requested.Add(tuple)) return false;

                SavedMemoryRepetitionGuardRow match = null;
                for (int rowIndex = 0; state.repetitionGuardRows != null
                    && rowIndex < state.repetitionGuardRows.Count; rowIndex++)
                {
                    SavedMemoryRepetitionGuardRow current = state.repetitionGuardRows[rowIndex];
                    if (current == null
                        || !string.Equals(
                            current.guardKind, frozen.guardKind, StringComparison.Ordinal)
                        || !string.Equals(
                            current.guardKey, frozen.guardKey, StringComparison.Ordinal)) continue;

                    // A tuple from another epoch is not the requested row and cannot safely coexist:
                    // recall indexes by guard tuple after Brainwipe has removed all old-epoch rows.
                    if (!string.Equals(
                            current.ownerEpochToken,
                            state.autobiographicalEpochToken,
                            StringComparison.Ordinal)
                        || match != null) return false;
                    match = current;
                }

                if (match != null)
                {
                    if (match.automaticInclusionCount < 0
                        || match.automaticInclusionCount == long.MaxValue) return false;
                    requiredRows.Add(match);
                    continue;
                }

                requiredRows.Add(new SavedMemoryRepetitionGuardRow
                {
                    schemaVersion = 1,
                    ownerEpochToken = state.autobiographicalEpochToken,
                    guardKind = frozen.guardKind,
                    guardKey = frozen.guardKey
                });
            }
            return true;
        }

        private static int CompareRepetitionGuardRows(
            SavedMemoryRepetitionGuardRow left,
            SavedMemoryRepetitionGuardRow right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int epoch = string.CompareOrdinal(left.ownerEpochToken, right.ownerEpochToken);
            if (epoch != 0) return epoch;
            int kind = string.CompareOrdinal(left.guardKind, right.guardKind);
            return kind != 0 ? kind : string.CompareOrdinal(left.guardKey, right.guardKey);
        }

        internal static bool ApplyConfirmedExposure(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            long invocationTick)
        {
            if (state == null || variant?.receiptPlan == null
                || state.statusRevision == long.MaxValue) return false;
            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            List<SavedMemoryThreadRoot> roots = FindEvidenceRoots(state, variant.receiptPlan);
            for (int index = 0; index < roots.Count; index++)
                if (roots[index].statusRevision == long.MaxValue) return false;
            bool changed = false;
            for (int index = 0; index < blocks.Count; index++)
            {
                SavedMemoryBlock block = blocks[index];
                long nextTick = Math.Max(block.lastProviderExposureTick, invocationTick);
                if (block.providerExposureState == "confirmed_sent"
                    && block.lastProviderExposureTick == nextTick) continue;
                block.providerExposureState = "confirmed_sent";
                block.lastProviderExposureTick = nextTick;
                changed = true;
            }
            if (!changed) return false;
            for (int index = 0; index < roots.Count; index++) roots[index].statusRevision++;
            state.statusRevision++;
            return true;
        }

        private static List<SavedMemoryBlock> FindEvidenceBlocks(
            PawnKnowledgeState state,
            SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            List<SavedMemoryBlock> result = new List<SavedMemoryBlock>();
            AddMatchingBlocks(result, state?.standaloneBlocks, receipt);
            for (int index = 0; state?.threadRoots != null && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (root == null) continue;
                AddMatchingBlocks(result, root.visibleBlocks, receipt);
                if (MatchesEvidence(root.rollingSummaryBlock, receipt)
                    && !result.Contains(root.rollingSummaryBlock))
                    result.Add(root.rollingSummaryBlock);
            }
            return result;
        }

        /// <summary>Finds each affected root once so status-only overlays receive their own revision.</summary>
        private static List<SavedMemoryThreadRoot> FindEvidenceRoots(
            PawnKnowledgeState state,
            SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            List<SavedMemoryThreadRoot> result = new List<SavedMemoryThreadRoot>();
            for (int index = 0; state?.threadRoots != null && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (root == null) continue;
                bool matched = false;
                for (int blockIndex = 0; root.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                {
                    if (MatchesEvidence(root.visibleBlocks[blockIndex], receipt))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) matched = MatchesEvidence(root.rollingSummaryBlock, receipt);
                if (matched) result.Add(root);
            }
            return result;
        }

        private static void AddMatchingBlocks(
            List<SavedMemoryBlock> result,
            List<SavedMemoryBlock> source,
            SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                SavedMemoryBlock block = source[index];
                if (MatchesEvidence(block, receipt) && !result.Contains(block)) result.Add(block);
            }
        }

        private static bool MatchesEvidence(
            SavedMemoryBlock block,
            SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            for (int index = 0; block != null && receipt?.evidenceEntries != null
                && index < receipt.evidenceEntries.Count; index++)
            {
                SavedFrozenEvidenceEntryV1 evidence = receipt.evidenceEntries[index];
                if (evidence != null
                    && string.Equals(block.recordId, evidence.recordId, StringComparison.Ordinal)
                    && string.Equals(block.sourceOccurrenceId, evidence.sourceOccurrenceId,
                        StringComparison.Ordinal)
                    && string.Equals(block.rootId ?? string.Empty,
                        evidence.rootIdOrEmpty ?? string.Empty, StringComparison.Ordinal)) return true;
            }
            return false;
        }

    }
}
