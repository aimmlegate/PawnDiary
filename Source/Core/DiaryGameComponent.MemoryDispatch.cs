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
                if (!CanApplyInvocationAccounting(state, variant, mutation))
                {
                    RecordMemoryDiagnostic("other", "owner");
                    continue;
                }
                ApplyInvocationAccounting(
                    state, variant, mutation, saved, Math.Max(1, attempt.invocationTick));
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
                if (CanApplyInvocationAccounting(state, variant, mutation))
                {
                    ApplyInvocationAccounting(
                        state, variant, mutation, saved, Math.Max(1, winner.invocationTick));
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

            DiaryEvent diaryEvent = events?.FindEvent(pending.context.eventIdOrOpportunityKey);
            SavedActiveLogicalRequestV1 saved = diaryEvent?
                .ActiveMemoryLogicalRequestForRole(pending.context.povRoleToken);
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

            MemoryDispatchFenceSnapshot fence = CurrentFence(saved);
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
            if (!planned.canCommit || !CanApplyInvocationAccounting(state, variant, planned))
            {
                return null;
            }

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

            ApplyInvocationAccounting(state, variant, committed, saved, invocationTick);
            memoryInvocationSequenceForSession = committed.nextInvocationSequence;
            RebuildMemorySizeIndexes();
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

            DiaryEvent diaryEvent = events?.FindEvent(permit.eventIdOrOpportunityKey);
            SavedActiveLogicalRequestV1 saved = diaryEvent?
                .ActiveMemoryLogicalRequestForRole(permit.povRoleToken);
            MemoryDispatchFenceSnapshot fence = CurrentFence(saved);
            MemoryTerminalCallbackPlan plan = fence == null
                ? new MemoryTerminalCallbackPlan()
                : MemoryDispatchSavedAdapter.PlanTerminalCallback(
                    saved,
                    permit,
                    fence,
                    pending.outcomeToken,
                    pending.providerReturnedUsableResult);
            if (!plan.accepted) return plan.duplicate;

            bool accountingChanged = false;
            if (plan.applyConfirmedExposure)
            {
                SavedFrozenPromptVariantV1 variant = FindVariant(saved, permit.variantKey);
                ApplyConfirmedExposure(
                    FindCurrentMemoryEnvelope(saved.ownerPawnId),
                    variant,
                    permit.invocationTick);
                accountingChanged = true;
            }
            bool applied = MemoryDispatchSavedAdapter.MarkReceiptApplied(
                saved,
                permit.attemptOrdinal,
                pending.outcomeToken,
                Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0));
            if (applied || accountingChanged) RebuildMemorySizeIndexes();
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
            MemoryInvocationCommitPermitV1 permit = result?.memoryInvocationPermit;
            saved = diaryEvent?.ActiveMemoryLogicalRequestForRole(result.povRole);
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
            MemoryDispatchFenceSnapshot fence = CurrentFence(saved);
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
            if (applied) RebuildMemorySizeIndexes();
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
            RebuildMemorySizeIndexes();
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

        private MemoryDispatchFenceSnapshot CurrentFence(SavedActiveLogicalRequestV1 saved)
        {
            if (saved == null || saved.sessionId != LlmClient.CurrentSessionId) return null;
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            if (state == null) return null;
            return new MemoryDispatchFenceSnapshot
            {
                sessionId = LlmClient.CurrentSessionId,
                ownerPawnId = saved.ownerPawnId,
                ownerEpochToken = state.autobiographicalEpochToken,
                ownerCancellationGeneration = state.requestCancellationGeneration,
                // M2 ships dormant under LegacyShadow. The settings-generation owners arrive with
                // the M10 coordinator; until then the frozen values are the current values.
                globalCancellationGeneration = saved.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    saved.optionalRequestInvalidationGeneration
            };
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

        private static bool CanApplyInvocationAccounting(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            MemoryInvocationCommitPlan plan)
        {
            if (variant?.receiptPlan == null || plan == null) return false;
            if (!plan.applyPotentialExposure && !plan.applyNarrativeUse) return true;
            if (state == null || state.statusRevision == long.MaxValue) return false;
            if (!plan.applyNarrativeUse) return true;

            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            for (int index = 0; index < blocks.Count; index++)
                if (blocks[index].automaticInclusionCount == long.MaxValue) return false;
            for (int index = 0; state.repetitionGuardRows != null
                && index < state.repetitionGuardRows.Count; index++)
            {
                SavedMemoryRepetitionGuardRow guard = state.repetitionGuardRows[index];
                if (guard != null && ReceiptContainsGuard(variant.receiptPlan, guard)
                    && guard.automaticInclusionCount == long.MaxValue) return false;
            }
            return true;
        }

        private static void ApplyInvocationAccounting(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            MemoryInvocationCommitPlan plan,
            SavedActiveLogicalRequestV1 request,
            long invocationTick)
        {
            if (state == null || variant?.receiptPlan == null
                || (!plan.applyPotentialExposure && !plan.applyNarrativeUse)) return;
            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            for (int index = 0; index < blocks.Count; index++)
            {
                SavedMemoryBlock block = blocks[index];
                block.providerExposureState = "potentially_sent";
                block.lastProviderExposureTick = Math.Max(
                    block.lastProviderExposureTick, invocationTick);
                if (plan.applyNarrativeUse)
                {
                    block.automaticInclusionCount++;
                    block.lastAutomaticIncludedTick = invocationTick;
                    block.lastAutomaticIncludedEntryOrdinal =
                        state.completedDiaryEntryOrdinal;
                }
            }

            if (plan.applyNarrativeUse && state.repetitionGuardRows != null)
            {
                string sourceOccurrence = variant.receiptPlan.evidenceEntries.Count > 0
                    ? variant.receiptPlan.evidenceEntries[0]?.sourceOccurrenceId ?? string.Empty
                    : string.Empty;
                for (int index = 0; index < state.repetitionGuardRows.Count; index++)
                {
                    SavedMemoryRepetitionGuardRow guard = state.repetitionGuardRows[index];
                    if (guard == null || !ReceiptContainsGuard(variant.receiptPlan, guard)) continue;
                    guard.automaticInclusionCount++;
                    guard.lastAutomaticIncludedTick = invocationTick;
                    guard.lastAutomaticIncludedEntryOrdinal = state.completedDiaryEntryOrdinal;
                    guard.lastSourceOccurrenceId = sourceOccurrence;
                    guard.lastCommittedLogicalRequestId = request.logicalRequestId;
                    guard.lastCommittedEvidenceSetFingerprint =
                        variant.receiptPlan.evidenceSetFingerprint;
                }
            }
            state.statusRevision++;
        }

        private static void ApplyConfirmedExposure(
            PawnKnowledgeState state,
            SavedFrozenPromptVariantV1 variant,
            long invocationTick)
        {
            if (state == null || variant?.receiptPlan == null) return;
            List<SavedMemoryBlock> blocks = FindEvidenceBlocks(state, variant.receiptPlan);
            for (int index = 0; index < blocks.Count; index++)
            {
                blocks[index].providerExposureState = "confirmed_sent";
                blocks[index].lastProviderExposureTick = Math.Max(
                    blocks[index].lastProviderExposureTick, invocationTick);
            }
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

        private static bool ReceiptContainsGuard(
            SavedFrozenEvidenceReceiptPlanV1 receipt,
            SavedMemoryRepetitionGuardRow guard)
        {
            for (int index = 0; receipt?.guardEntries != null
                && index < receipt.guardEntries.Count; index++)
            {
                SavedFrozenGuardEntryV1 row = receipt.guardEntries[index];
                if (row != null
                    && string.Equals(row.guardKind, guard.guardKind, StringComparison.Ordinal)
                    && string.Equals(row.guardKey, guard.guardKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
