// MemoryDispatchSavedAdapter.cs — M2 boundary between Verse-scribed request rows and the detached
// MemoryDispatchPolicy snapshots. The adapter copies exact values, asks the pure policy to decide,
// then applies only the returned transition. It never reads Pawns, Defs, settings, or transport.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Copies and transitions the saved §T6.11 request graph without duplicating policy.</summary>
    internal static class MemoryDispatchSavedAdapter
    {
        public static MemoryLogicalRequestSnapshot ToSnapshot(SavedActiveLogicalRequestV1 saved)
        {
            if (saved == null) return null;
            MemoryLogicalRequestSnapshot result = new MemoryLogicalRequestSnapshot
            {
                logicalRequestSequence = saved.logicalRequestSequence,
                logicalRequestId = saved.logicalRequestId ?? string.Empty,
                logicalRequestKey = saved.logicalRequestKey ?? string.Empty,
                requestPurposeToken = saved.requestPurposeToken ?? string.Empty,
                sessionId = saved.sessionId,
                eventIdOrOpportunityKey = saved.eventIdOrOpportunityKey ?? string.Empty,
                povRoleToken = saved.povRoleToken ?? string.Empty,
                ownerPawnId = saved.ownerPawnId ?? string.Empty,
                ownerEpochToken = saved.ownerEpochToken ?? string.Empty,
                evidenceEpochToken = saved.evidenceEpochToken ?? string.Empty,
                ownerCancellationGeneration = saved.ownerCancellationGeneration,
                globalCancellationGeneration = saved.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    saved.optionalRequestInvalidationGeneration,
                requestStateToken = saved.requestStateToken ?? string.Empty,
                lastIssuedAttemptOrdinal = saved.lastIssuedAttemptOrdinal,
                narrativeUseWinnerAttemptOrdinal = saved.narrativeUseWinnerAttemptOrdinal,
                narrativeUseWinnerVariantKey = saved.narrativeUseWinnerVariantKey ?? string.Empty
            };

            CopyVariants(saved.frozenVariants, result.variants);
            CopyAttempts(saved.activeAttempts, result.attempts);
            CopyEvidence(saved.reservedEvidenceEntries, result.reservedEvidence);
            CopyGuards(saved.reservedGuardEntries, result.reservedGuards);
            return result;
        }

        /// <summary>Builds a complete saved row only when the detached frozen graph is valid.</summary>
        public static bool TryCreateSavedRequest(
            MemoryLogicalRequestSnapshot snapshot,
            out SavedActiveLogicalRequestV1 saved)
        {
            saved = null;
            if (!MemoryDispatchPolicy.ValidateRequest(snapshot)) return false;

            SavedActiveLogicalRequestV1 candidate = new SavedActiveLogicalRequestV1
            {
                schemaVersion = 1,
                logicalRequestSequence = snapshot.logicalRequestSequence,
                logicalRequestId = snapshot.logicalRequestId,
                logicalRequestKey = snapshot.logicalRequestKey,
                requestPurposeToken = snapshot.requestPurposeToken,
                sessionId = snapshot.sessionId,
                eventIdOrOpportunityKey = snapshot.eventIdOrOpportunityKey,
                povRoleToken = snapshot.povRoleToken,
                ownerPawnId = snapshot.ownerPawnId,
                ownerEpochToken = snapshot.ownerEpochToken,
                evidenceEpochToken = snapshot.evidenceEpochToken,
                ownerCancellationGeneration = snapshot.ownerCancellationGeneration,
                globalCancellationGeneration = snapshot.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    snapshot.optionalRequestInvalidationGeneration,
                requestStateToken = snapshot.requestStateToken,
                lastIssuedAttemptOrdinal = snapshot.lastIssuedAttemptOrdinal,
                narrativeUseWinnerAttemptOrdinal = snapshot.narrativeUseWinnerAttemptOrdinal,
                narrativeUseWinnerVariantKey = snapshot.narrativeUseWinnerVariantKey
            };
            for (int index = 0; index < snapshot.variants.Count; index++)
                candidate.frozenVariants.Add(ToSavedVariant(snapshot.variants[index]));
            for (int index = 0; index < snapshot.attempts.Count; index++)
                candidate.activeAttempts.Add(ToSavedAttempt(snapshot.attempts[index]));
            for (int index = 0; index < snapshot.reservedEvidence.Count; index++)
                candidate.reservedEvidenceEntries.Add(ToSavedEvidence(
                    snapshot.reservedEvidence[index]));
            for (int index = 0; index < snapshot.reservedGuards.Count; index++)
                candidate.reservedGuardEntries.Add(ToSavedGuard(snapshot.reservedGuards[index]));
            saved = candidate;
            return true;
        }

        /// <summary>Publishes one fully committed staged row to the worker-visible lifecycle.</summary>
        public static bool TryActivate(SavedActiveLogicalRequestV1 saved)
        {
            MemoryLogicalRequestSnapshot snapshot = ToSnapshot(saved);
            if (!MemoryDispatchPolicy.ValidateRequest(snapshot)
                || saved.requestStateToken != MemoryRequestStateMachineContracts.Staged
                || !MemoryRequestStateMachineContracts.CanTransition(
                    saved.requestStateToken,
                    MemoryRequestStateMachineContracts.Activated))
            {
                return false;
            }

            saved.requestStateToken = MemoryRequestStateMachineContracts.Activated;
            return true;
        }

        /// <summary>Appends one prepared attempt only after pure ordinal/origin validation.</summary>
        public static bool TryPrepareAttempt(
            SavedActiveLogicalRequestV1 saved,
            string variantKey,
            string originToken,
            int predecessorAttemptOrdinal,
            out SavedActiveLogicalAttemptV1 prepared)
        {
            prepared = null;
            MemoryLogicalAttemptSnapshot plan;
            if (!MemoryDispatchPolicy.TryPlanPreparedAttempt(
                    ToSnapshot(saved),
                    variantKey,
                    originToken,
                    predecessorAttemptOrdinal,
                    out plan))
            {
                return false;
            }

            prepared = ToSavedAttempt(plan);
            saved.activeAttempts.Add(prepared);
            saved.lastIssuedAttemptOrdinal = plan.attemptOrdinal;
            return true;
        }

        /// <summary>
        /// Applies the row-local half of an invocation transaction. The owning component must apply
        /// evidence/guard receipts in the same main-thread mutation before returning the permit.
        /// </summary>
        public static bool TryCommitInvocation(
            SavedActiveLogicalRequestV1 saved,
            int attemptOrdinal,
            MemoryDispatchFenceSnapshot fence,
            long currentInvocationSequence,
            long invocationTick,
            out MemoryInvocationCommitPlan plan)
        {
            plan = MemoryDispatchPolicy.PlanInvocationCommit(
                ToSnapshot(saved),
                attemptOrdinal,
                fence,
                currentInvocationSequence,
                invocationTick);
            if (!plan.canCommit) return false;

            SavedActiveLogicalAttemptV1 attempt = FindAttempt(saved, attemptOrdinal);
            if (attempt == null) return false;
            attempt.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptInvocationCommitted;
            attempt.invocationSequence = plan.nextInvocationSequence;
            attempt.invocationTick = invocationTick;
            attempt.potentialExposureApplied = plan.applyPotentialExposure;
            attempt.narrativeUseApplied = plan.applyNarrativeUse;
            saved.requestStateToken = MemoryRequestStateMachineContracts.InvocationCommitted;
            saved.narrativeUseWinnerAttemptOrdinal =
                plan.narrativeUseWinnerAttemptOrdinal;
            saved.narrativeUseWinnerVariantKey = plan.narrativeUseWinnerVariantKey;
            return true;
        }

        /// <summary>Plans a callback against the exact currently committed saved row.</summary>
        public static MemoryTerminalCallbackPlan PlanTerminalCallback(
            SavedActiveLogicalRequestV1 saved,
            MemoryInvocationCommitPermitV1 permit,
            MemoryDispatchFenceSnapshot fence,
            string outcomeToken,
            bool providerReturnedUsableResult)
        {
            return MemoryDispatchPolicy.PlanTerminalCallback(
                ToSnapshot(saved),
                permit,
                fence,
                outcomeToken,
                providerReturnedUsableResult);
        }

        /// <summary>
        /// Advances the saved attempt only after the owning component has applied the corresponding
        /// receipt. Result publication is a separate final step so ordering cannot be inverted.
        /// </summary>
        public static bool MarkReceiptApplied(
            SavedActiveLogicalRequestV1 saved,
            int attemptOrdinal,
            string outcomeToken,
            long terminalTick)
        {
            SavedActiveLogicalAttemptV1 attempt = FindAttempt(saved, attemptOrdinal);
            if (attempt == null
                || !MemoryDispatchTokens.IsTerminalOutcome(outcomeToken)
                || terminalTick <= 0)
            {
                return false;
            }
            if (attempt.attemptStateToken
                    == MemoryRequestStateMachineContracts.AttemptReceiptApplied
                || attempt.attemptStateToken
                    == MemoryRequestStateMachineContracts.AttemptTerminalPending)
            {
                // The first receipt owns terminal time. A duplicate main-thread drain can occur on a
                // later game tick; it is idempotent only when the stable outcome token still matches.
                return attempt.terminalTick > 0
                    && string.Equals(
                        attempt.terminalOutcomeToken,
                        outcomeToken,
                        System.StringComparison.Ordinal);
            }
            if (attempt.attemptStateToken
                != MemoryRequestStateMachineContracts.AttemptInvocationCommitted) return false;
            attempt.attemptStateToken = MemoryRequestStateMachineContracts.AttemptReceiptApplied;
            attempt.terminalTick = terminalTick;
            attempt.terminalOutcomeToken = outcomeToken;
            return true;
        }

        /// <summary>
        /// Returns true unless a loaded saved row proves that no invocation permit was committed.
        /// Invalid or incomplete rows fail closed so load recovery cannot automatically resend work
        /// that may already have crossed the conservative exposure boundary.
        /// </summary>
        public static bool LoadedRequestMayHaveBeenInvoked(SavedActiveLogicalRequestV1 saved)
        {
            if (saved == null || saved.activeAttempts == null
                || !MemoryDispatchPolicy.ValidateRequest(ToSnapshot(saved))) return true;
            for (int index = 0; index < saved.activeAttempts.Count; index++)
            {
                SavedActiveLogicalAttemptV1 attempt = saved.activeAttempts[index];
                if (attempt == null
                    || attempt.invocationSequence > 0
                    || attempt.invocationTick > 0
                    || attempt.potentialExposureApplied
                    || attempt.narrativeUseApplied
                    || attempt.resultApplied
                    || attempt.terminalTick > 0
                    || !string.IsNullOrEmpty(attempt.terminalOutcomeToken)
                    || attempt.attemptStateToken
                        != MemoryRequestStateMachineContracts.AttemptPrepared)
                {
                    return true;
                }
            }

            return saved.requestStateToken != MemoryRequestStateMachineContracts.Staged
                && saved.requestStateToken != MemoryRequestStateMachineContracts.Activated;
        }

        /// <summary>Marks result publication once, only after the receipt-applied state.</summary>
        public static bool MarkResultApplied(
            SavedActiveLogicalRequestV1 saved,
            int attemptOrdinal)
        {
            SavedActiveLogicalAttemptV1 attempt = FindAttempt(saved, attemptOrdinal);
            if (attempt == null || attempt.resultApplied
                || attempt.attemptStateToken
                    != MemoryRequestStateMachineContracts.AttemptReceiptApplied)
            {
                return false;
            }
            attempt.resultApplied = true;
            attempt.attemptStateToken = MemoryRequestStateMachineContracts.AttemptTerminalPending;
            saved.requestStateToken = MemoryRequestStateMachineContracts.SettlementPending;
            return true;
        }

        private static void CopyVariants(
            List<SavedFrozenPromptVariantV1> source,
            List<MemoryFrozenPromptVariantSnapshot> target)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                SavedFrozenPromptVariantV1 saved = source[index];
                if (saved == null)
                {
                    target.Add(null);
                    continue;
                }
                MemoryFrozenPromptVariantSnapshot variant =
                    new MemoryFrozenPromptVariantSnapshot
                    {
                        variantOrdinal = saved.variantOrdinal,
                        variantKey = saved.variantKey ?? string.Empty,
                        templateIdentity = saved.templateIdentity ?? string.Empty,
                        contextDetailIdentity = saved.contextDetailIdentity ?? string.Empty,
                        systemPrompt = saved.systemPrompt ?? string.Empty,
                        userPrompt = saved.userPrompt ?? string.Empty
                    };
                if (saved.receiptPlan == null)
                {
                    variant.receipt = null;
                }
                else
                {
                    variant.receipt.evidenceSetFingerprint =
                        saved.receiptPlan.evidenceSetFingerprint ?? string.Empty;
                    CopyEvidence(saved.receiptPlan.evidenceEntries, variant.receipt.evidence);
                    CopyGuards(saved.receiptPlan.guardEntries, variant.receipt.guards);
                    MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                        variant.receipt.evidence,
                        variant.receipt.guards,
                        out variant.receipt.receiptPlanFingerprint);
                }
                for (int rowIndex = 0;
                    saved.diagnosticProvenance != null
                    && rowIndex < saved.diagnosticProvenance.Count;
                    rowIndex++)
                {
                    SavedFrozenDiagnosticProvenanceV1 row =
                        saved.diagnosticProvenance[rowIndex];
                    variant.diagnostics.Add(row == null ? null : new MemoryDiagnosticIdentity
                    {
                        provenanceKindToken = row.provenanceKindToken ?? string.Empty,
                        sourceId = row.sourceId ?? string.Empty,
                        recordIdOrEmpty = row.recordIdOrEmpty ?? string.Empty,
                        sourceOccurrenceIdOrEmpty = row.sourceOccurrenceIdOrEmpty ?? string.Empty,
                        rootIdOrEmpty = row.rootIdOrEmpty ?? string.Empty,
                        lineOrdinal = row.lineOrdinal
                    });
                }
                target.Add(variant);
            }
        }

        private static void CopyAttempts(
            List<SavedActiveLogicalAttemptV1> source,
            List<MemoryLogicalAttemptSnapshot> target)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                SavedActiveLogicalAttemptV1 row = source[index];
                target.Add(row == null ? null : new MemoryLogicalAttemptSnapshot
                {
                    attemptOrdinal = row.attemptOrdinal,
                    variantKey = row.variantKey ?? string.Empty,
                    attemptOriginToken = row.attemptOriginToken ?? string.Empty,
                    predecessorAttemptOrdinal = row.predecessorAttemptOrdinal,
                    attemptStateToken = row.attemptStateToken ?? string.Empty,
                    invocationSequence = row.invocationSequence,
                    invocationTick = row.invocationTick,
                    terminalTick = row.terminalTick,
                    terminalOutcomeToken = row.terminalOutcomeToken ?? string.Empty,
                    potentialExposureApplied = row.potentialExposureApplied,
                    narrativeUseApplied = row.narrativeUseApplied,
                    resultApplied = row.resultApplied
                });
            }
        }

        private static void CopyEvidence(
            List<SavedFrozenEvidenceEntryV1> source,
            List<MemoryEvidenceIdentity> target)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                SavedFrozenEvidenceEntryV1 row = source[index];
                target.Add(row == null ? null : new MemoryEvidenceIdentity
                {
                    recordId = row.recordId ?? string.Empty,
                    sourceOccurrenceId = row.sourceOccurrenceId ?? string.Empty,
                    rootIdOrEmpty = row.rootIdOrEmpty ?? string.Empty
                });
            }
        }

        private static void CopyGuards(
            List<SavedFrozenGuardEntryV1> source,
            List<MemoryGuardIdentity> target)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                SavedFrozenGuardEntryV1 row = source[index];
                target.Add(row == null ? null : new MemoryGuardIdentity
                {
                    guardKind = row.guardKind ?? string.Empty,
                    guardKey = row.guardKey ?? string.Empty
                });
            }
        }

        private static SavedFrozenPromptVariantV1 ToSavedVariant(
            MemoryFrozenPromptVariantSnapshot variant)
        {
            SavedFrozenPromptVariantV1 saved = new SavedFrozenPromptVariantV1
            {
                schemaVersion = 1,
                variantOrdinal = variant.variantOrdinal,
                variantKey = variant.variantKey,
                templateIdentity = variant.templateIdentity,
                contextDetailIdentity = variant.contextDetailIdentity,
                systemPrompt = variant.systemPrompt,
                userPrompt = variant.userPrompt,
                receiptPlan = new SavedFrozenEvidenceReceiptPlanV1
                {
                    schemaVersion = 1,
                    evidenceSetFingerprint = variant.receipt.evidenceSetFingerprint
                }
            };
            for (int index = 0; index < variant.receipt.evidence.Count; index++)
                saved.receiptPlan.evidenceEntries.Add(ToSavedEvidence(
                    variant.receipt.evidence[index]));
            for (int index = 0; index < variant.receipt.guards.Count; index++)
                saved.receiptPlan.guardEntries.Add(ToSavedGuard(variant.receipt.guards[index]));
            for (int index = 0; index < variant.diagnostics.Count; index++)
            {
                MemoryDiagnosticIdentity row = variant.diagnostics[index];
                saved.diagnosticProvenance.Add(new SavedFrozenDiagnosticProvenanceV1
                {
                    schemaVersion = 1,
                    provenanceKindToken = row.provenanceKindToken,
                    sourceId = row.sourceId,
                    recordIdOrEmpty = row.recordIdOrEmpty,
                    sourceOccurrenceIdOrEmpty = row.sourceOccurrenceIdOrEmpty,
                    rootIdOrEmpty = row.rootIdOrEmpty,
                    lineOrdinal = row.lineOrdinal
                });
            }
            return saved;
        }

        private static SavedActiveLogicalAttemptV1 ToSavedAttempt(
            MemoryLogicalAttemptSnapshot row)
        {
            return new SavedActiveLogicalAttemptV1
            {
                schemaVersion = 1,
                attemptOrdinal = row.attemptOrdinal,
                variantKey = row.variantKey,
                attemptOriginToken = row.attemptOriginToken,
                predecessorAttemptOrdinal = row.predecessorAttemptOrdinal,
                attemptStateToken = row.attemptStateToken,
                invocationSequence = row.invocationSequence,
                invocationTick = row.invocationTick,
                terminalTick = row.terminalTick,
                terminalOutcomeToken = row.terminalOutcomeToken,
                potentialExposureApplied = row.potentialExposureApplied,
                narrativeUseApplied = row.narrativeUseApplied,
                resultApplied = row.resultApplied
            };
        }

        private static SavedFrozenEvidenceEntryV1 ToSavedEvidence(MemoryEvidenceIdentity row)
        {
            return new SavedFrozenEvidenceEntryV1
            {
                schemaVersion = 1,
                recordId = row.recordId,
                sourceOccurrenceId = row.sourceOccurrenceId,
                rootIdOrEmpty = row.rootIdOrEmpty
            };
        }

        private static SavedFrozenGuardEntryV1 ToSavedGuard(MemoryGuardIdentity row)
        {
            return new SavedFrozenGuardEntryV1
            {
                schemaVersion = 1,
                guardKind = row.guardKind,
                guardKey = row.guardKey
            };
        }

        private static SavedActiveLogicalAttemptV1 FindAttempt(
            SavedActiveLogicalRequestV1 request,
            int ordinal)
        {
            if (request?.activeAttempts == null || ordinal <= 0
                || ordinal > request.activeAttempts.Count) return null;
            SavedActiveLogicalAttemptV1 row = request.activeAttempts[ordinal - 1];
            return row != null && row.attemptOrdinal == ordinal ? row : null;
        }
    }
}
