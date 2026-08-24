// MemoryDispatchPolicy.cs — pure M2 request, attempt, invocation-permit, and settlement rules.
//
// Game-facing code copies saved rows into these detached snapshots, asks this policy for a complete
// plan, and only then mutates Verse-owned state. No Pawn, Scribe, HTTP, endpoint, model, credential,
// response body, or exception crosses this boundary. See design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md
// §T6.11 and AGENTS.md (impure edge -> DTO -> pure policy -> impure adapter).
using System;
using System.Collections.Generic;
using System.Threading;

namespace PawnDiary
{
    /// <summary>Stable M2 request, attempt, and terminal tokens.</summary>
    internal static class MemoryDispatchTokens
    {
        public const string NormalDiary = "normal_diary";
        public const string MemoryReflection = "memory_reflection";
        public const string SummaryWording = "summary_wording";
        public const string ManualRegenerate = "manual_regenerate";

        public const string Initial = "initial";
        public const string Retry = "retry";
        public const string Failover = "failover";

        public const string Success = "success";
        public const string ProviderError = "provider_error";
        public const string Timeout = "timeout";
        public const string Malformed = "malformed";
        public const string Stale = "stale";
        public const string Invalid = "invalid";
        public const string QueueRefused = "queue_refused";
        public const string ActivationFailed = "activation_failed";
        public const string SequenceSaturated = "sequence_saturated";
        public const string CancelledPreInvocation = "cancelled_pre_invocation";
        public const string CancelledPostInvocation = "cancelled_post_invocation";
        public const string LoadInterruptedBeforeInvocation = "load_interrupted_before_invocation";
        public const string LoadInterruptedAfterInvocation = "load_interrupted_after_invocation";

        public static bool IsPurpose(string value)
        {
            return value == NormalDiary || value == MemoryReflection
                || value == SummaryWording || value == ManualRegenerate;
        }

        public static bool IsOptionalPurpose(string value)
        {
            return value == MemoryReflection || value == SummaryWording;
        }

        public static bool IsNarrativeUsePurpose(string value)
        {
            return value == NormalDiary || value == MemoryReflection;
        }

        public static bool IsAttemptOrigin(string value)
        {
            return value == Initial || value == Retry || value == Failover;
        }

        public static bool IsTerminalOutcome(string value)
        {
            return value == Success || value == ProviderError || value == Timeout
                || value == Malformed || value == Stale || value == Invalid
                || value == QueueRefused || value == ActivationFailed
                || value == SequenceSaturated || value == CancelledPreInvocation
                || value == CancelledPostInvocation
                || value == LoadInterruptedBeforeInvocation
                || value == LoadInterruptedAfterInvocation;
        }
    }

    /// <summary>Detached exact evidence/guard receipt plan for one frozen prompt variant.</summary>
    internal sealed class MemoryFrozenReceiptSnapshot
    {
        public string evidenceSetFingerprint = string.Empty;
        public string receiptPlanFingerprint = string.Empty;
        public List<MemoryEvidenceIdentity> evidence = new List<MemoryEvidenceIdentity>();
        public List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>();
    }

    /// <summary>Detached immutable prompt variant. Transport lane details are intentionally absent.</summary>
    internal sealed class MemoryFrozenPromptVariantSnapshot
    {
        public int variantOrdinal;
        public string variantKey = string.Empty;
        public string templateIdentity = string.Empty;
        public string contextDetailIdentity = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public MemoryFrozenReceiptSnapshot receipt = new MemoryFrozenReceiptSnapshot();
        public List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>();
    }

    /// <summary>Detached state of one saved physical attempt.</summary>
    internal sealed class MemoryLogicalAttemptSnapshot
    {
        public int attemptOrdinal;
        public string variantKey = string.Empty;
        public string attemptOriginToken = string.Empty;
        public int predecessorAttemptOrdinal;
        public string attemptStateToken = string.Empty;
        public long invocationSequence;
        public long invocationTick;
        public long terminalTick;
        public string terminalOutcomeToken = string.Empty;
        public bool potentialExposureApplied;
        public bool narrativeUseApplied;
        public bool resultApplied;
    }

    /// <summary>Detached complete logical request used by pure M2 validation and planning.</summary>
    internal sealed class MemoryLogicalRequestSnapshot
    {
        public long logicalRequestSequence;
        public string logicalRequestId = string.Empty;
        public string logicalRequestKey = string.Empty;
        public string requestPurposeToken = string.Empty;
        public long sessionId;
        public string eventIdOrOpportunityKey = string.Empty;
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string evidenceEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public string requestStateToken = string.Empty;
        public int lastIssuedAttemptOrdinal;
        public int narrativeUseWinnerAttemptOrdinal;
        public string narrativeUseWinnerVariantKey = string.Empty;
        public List<MemoryFrozenPromptVariantSnapshot> variants =
            new List<MemoryFrozenPromptVariantSnapshot>();
        public List<MemoryLogicalAttemptSnapshot> attempts =
            new List<MemoryLogicalAttemptSnapshot>();
        public List<MemoryEvidenceIdentity> reservedEvidence =
            new List<MemoryEvidenceIdentity>();
        public List<MemoryGuardIdentity> reservedGuards =
            new List<MemoryGuardIdentity>();
    }

    /// <summary>Current main-thread cancellation/epoch tuple checked immediately before invocation.</summary>
    internal sealed class MemoryDispatchFenceSnapshot
    {
        public long sessionId;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
    }

    /// <summary>
    /// Transient declaration-order permit echoed by every transport receipt/result. It is never
    /// Scribed; <see cref="ToIdentity"/> feeds the one canonical fingerprint implementation.
    /// </summary>
    internal sealed class MemoryInvocationCommitPermitV1
    {
        public string logicalRequestId = string.Empty;
        public string logicalRequestKey = string.Empty;
        public string requestPurposeToken = string.Empty;
        public long sessionId;
        public string eventIdOrOpportunityKey = string.Empty;
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string evidenceEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public int attemptOrdinal;
        public string variantKey = string.Empty;
        public string receiptPlanFingerprint = string.Empty;
        public long invocationSequence;
        public long invocationTick;
        public int narrativeUseWinnerAttemptOrdinal;
        public string permitFingerprint = string.Empty;

        public MemoryInvocationPermitIdentity ToIdentity()
        {
            return new MemoryInvocationPermitIdentity
            {
                logicalRequestId = logicalRequestId,
                logicalRequestKey = logicalRequestKey,
                requestPurposeToken = requestPurposeToken,
                sessionId = sessionId,
                eventIdOrOpportunityKey = eventIdOrOpportunityKey,
                povRoleToken = povRoleToken,
                ownerPawnId = ownerPawnId,
                ownerEpochToken = ownerEpochToken,
                evidenceEpochToken = evidenceEpochToken,
                ownerCancellationGeneration = ownerCancellationGeneration,
                globalCancellationGeneration = globalCancellationGeneration,
                optionalRequestInvalidationGeneration = optionalRequestInvalidationGeneration,
                attemptOrdinal = attemptOrdinal,
                variantKey = variantKey,
                receiptPlanFingerprint = receiptPlanFingerprint,
                invocationSequence = invocationSequence,
                invocationTick = invocationTick,
                narrativeUseWinnerAttemptOrdinal = narrativeUseWinnerAttemptOrdinal
            };
        }
    }

    /// <summary>Pure plan for atomically committing one invocation receipt before physical send.</summary>
    internal sealed class MemoryInvocationCommitPlan
    {
        public bool canCommit;
        public string outcomeToken = MemoryDispatchTokens.Invalid;
        public long nextInvocationSequence;
        public int narrativeUseWinnerAttemptOrdinal;
        public string narrativeUseWinnerVariantKey = string.Empty;
        public bool applyPotentialExposure;
        public bool applyNarrativeUse;
        public MemoryInvocationCommitPermitV1 permit;
    }

    /// <summary>Pure receipt-before-result decision for one terminal callback.</summary>
    internal sealed class MemoryTerminalCallbackPlan
    {
        public bool accepted;
        public bool duplicate;
        public string outcomeToken = MemoryDispatchTokens.Invalid;
        public bool applyConfirmedExposure;
        public bool applyResult;
        public List<string> orderedOperations = new List<string>();
    }

    /// <summary>Pure load settlement for an active row that must never be reactivated or resent.</summary>
    internal sealed class MemoryLoadSettlementPlan
    {
        public bool valid;
        public bool hadCommittedInvocation;
        public string outcomeToken = MemoryDispatchTokens.Invalid;
        public bool restoreNormalPovRetryable;
        public int repairedNarrativeUseWinnerAttemptOrdinal;
        public string repairedNarrativeUseWinnerVariantKey = string.Empty;
        public List<int> potentialExposureAttemptOrdinals = new List<int>();
    }

    /// <summary>
    /// One bounded runtime send envelope. Only the compare-exchange winner may cross into SendAsync,
    /// even if a worker or an equal permit is scheduled twice.
    /// </summary>
    internal sealed class MemoryRuntimeSendEnvelope
    {
        public readonly MemoryInvocationCommitPermitV1 permit;
        private int sendClaimed;

        public MemoryRuntimeSendEnvelope(MemoryInvocationCommitPermitV1 permit)
        {
            this.permit = permit;
        }

        public bool TryClaimPhysicalSend()
        {
            return MemoryDispatchPolicy.PermitFingerprintIsValid(permit)
                && Interlocked.CompareExchange(ref sendClaimed, 1, 0) == 0;
        }
    }

    /// <summary>Pure, fail-closed M2 validation and transition planning.</summary>
    internal static class MemoryDispatchPolicy
    {
        // Code-owned defensive ceilings from §T17.1. Production settings may only lower them.
        public const int MaximumVariants = 16;
        public const int MaximumAttempts = 16;
        public const int MaximumEvidencePerVariant = 2;
        public const int MaximumGuardsPerVariant = 32;
        public const int MaximumDiagnosticsPerVariant = 64;

        public static bool ValidateRequest(MemoryLogicalRequestSnapshot request)
        {
            if (request == null
                || request.logicalRequestSequence <= 0
                || request.sessionId <= 0
                || !MemoryDispatchTokens.IsPurpose(request.requestPurposeToken)
                || request.ownerCancellationGeneration < 0
                || request.globalCancellationGeneration < 0
                || !ValidOptionalGeneration(request)
                || !IsRequestState(request.requestStateToken)
                || request.variants == null || request.variants.Count == 0
                || request.variants.Count > MaximumVariants
                || request.attempts == null || request.attempts.Count > MaximumAttempts
                || request.reservedEvidence == null || request.reservedGuards == null)
            {
                return false;
            }

            long parsedSequence;
            string expectedRequestId;
            if (!MemoryIdentityCodec.TryParseLogicalRequestId(
                    request.logicalRequestId, out parsedSequence)
                || parsedSequence != request.logicalRequestSequence
                || !MemoryIdentityCodec.TryCreateLogicalRequestId(
                    request.logicalRequestSequence, out expectedRequestId)
                || !Equal(expectedRequestId, request.logicalRequestId))
            {
                return false;
            }

            List<MemoryEvidenceIdentity> unionEvidence = new List<MemoryEvidenceIdentity>();
            List<MemoryGuardIdentity> unionGuards = new List<MemoryGuardIdentity>();
            HashSet<string> variantKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < request.variants.Count; index++)
            {
                MemoryFrozenPromptVariantSnapshot variant = request.variants[index];
                if (!ValidateVariant(request, variant, index)
                    || !variantKeys.Add(variant.variantKey))
                {
                    return false;
                }

                unionEvidence.AddRange(variant.receipt.evidence);
                unionGuards.AddRange(variant.receipt.guards);
            }

            List<MemoryEvidenceIdentity> canonicalEvidence = CanonicalEvidence(unionEvidence);
            List<MemoryGuardIdentity> canonicalGuards = CanonicalGuards(unionGuards);
            if (canonicalEvidence == null || canonicalGuards == null
                || !EvidenceListsEqual(canonicalEvidence, request.reservedEvidence)
                || !GuardListsEqual(canonicalGuards, request.reservedGuards))
            {
                return false;
            }

            string expectedEpoch;
            string expectedKey;
            if (!MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    canonicalEvidence,
                    canonicalGuards,
                    out expectedEpoch)
                || !Equal(expectedEpoch, request.evidenceEpochToken)
                || !MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.evidenceEpochToken,
                    out expectedKey)
                || !Equal(expectedKey, request.logicalRequestKey)
                || !ValidatePurposeReceiptShape(request))
            {
                return false;
            }

            if (!ValidateAttempts(request))
            {
                return false;
            }

            if (request.narrativeUseWinnerAttemptOrdinal == 0)
            {
                return string.IsNullOrEmpty(request.narrativeUseWinnerVariantKey);
            }

            MemoryLogicalAttemptSnapshot winner = Attempt(
                request, request.narrativeUseWinnerAttemptOrdinal);
            return MemoryDispatchTokens.IsNarrativeUsePurpose(request.requestPurposeToken)
                && winner != null
                && winner.invocationSequence > 0
                && winner.narrativeUseApplied
                && Equal(winner.variantKey, request.narrativeUseWinnerVariantKey);
        }

        public static bool TryPlanPreparedAttempt(
            MemoryLogicalRequestSnapshot request,
            string variantKey,
            string originToken,
            int predecessorAttemptOrdinal,
            out MemoryLogicalAttemptSnapshot attempt)
        {
            attempt = null;
            if (!ValidateRequest(request)
                || (request.requestStateToken != MemoryRequestStateMachineContracts.Activated
                    && request.requestStateToken
                        != MemoryRequestStateMachineContracts.InvocationCommitted)
                || !MemoryDispatchTokens.IsAttemptOrigin(originToken)
                || request.lastIssuedAttemptOrdinal >= MaximumAttempts
                || Variant(request, variantKey) == null)
            {
                return false;
            }

            int nextOrdinal;
            try
            {
                nextOrdinal = checked(request.lastIssuedAttemptOrdinal + 1);
            }
            catch (OverflowException)
            {
                return false;
            }

            MemoryLogicalAttemptSnapshot predecessor = Attempt(
                request, predecessorAttemptOrdinal);
            if (originToken == MemoryDispatchTokens.Initial)
            {
                if (nextOrdinal != 1 || predecessorAttemptOrdinal != 0) return false;
            }
            else if (predecessor == null || predecessor.attemptOrdinal >= nextOrdinal)
            {
                return false;
            }
            else if (originToken == MemoryDispatchTokens.Retry
                ? !Equal(predecessor.variantKey, variantKey)
                : Equal(predecessor.variantKey, variantKey))
            {
                return false;
            }

            attempt = new MemoryLogicalAttemptSnapshot
            {
                attemptOrdinal = nextOrdinal,
                variantKey = variantKey,
                attemptOriginToken = originToken,
                predecessorAttemptOrdinal = predecessorAttemptOrdinal,
                attemptStateToken = MemoryRequestStateMachineContracts.AttemptPrepared
            };
            return true;
        }

        public static MemoryInvocationCommitPlan PlanInvocationCommit(
            MemoryLogicalRequestSnapshot request,
            int attemptOrdinal,
            MemoryDispatchFenceSnapshot currentFence,
            long currentInvocationSequence,
            long invocationTick)
        {
            MemoryInvocationCommitPlan plan = new MemoryInvocationCommitPlan();
            MemoryLogicalAttemptSnapshot attempt = Attempt(request, attemptOrdinal);
            MemoryFrozenPromptVariantSnapshot variant = attempt == null
                ? null
                : Variant(request, attempt.variantKey);
            if (!ValidateRequest(request)
                || (request.requestStateToken != MemoryRequestStateMachineContracts.Activated
                    && request.requestStateToken
                        != MemoryRequestStateMachineContracts.InvocationCommitted)
                || attempt == null || variant == null
                || attempt.attemptStateToken
                    != MemoryRequestStateMachineContracts.AttemptPrepared
                || attempt.invocationSequence != 0 || attempt.invocationTick != 0
                || currentInvocationSequence < 0 || invocationTick <= 0
                || !FenceMatches(request, currentFence))
            {
                return plan;
            }

            if (currentInvocationSequence == long.MaxValue)
            {
                plan.outcomeToken = MemoryDispatchTokens.SequenceSaturated;
                return plan;
            }

            long nextSequence = currentInvocationSequence + 1;
            int winnerOrdinal = request.narrativeUseWinnerAttemptOrdinal;
            string winnerKey = request.narrativeUseWinnerVariantKey ?? string.Empty;
            bool narrativeUse = MemoryDispatchTokens.IsNarrativeUsePurpose(
                    request.requestPurposeToken)
                && variant.receipt.evidence.Count > 0
                && winnerOrdinal == 0;
            if (narrativeUse)
            {
                winnerOrdinal = attempt.attemptOrdinal;
                winnerKey = attempt.variantKey;
            }

            MemoryInvocationCommitPermitV1 permit = new MemoryInvocationCommitPermitV1
            {
                logicalRequestId = request.logicalRequestId,
                logicalRequestKey = request.logicalRequestKey,
                requestPurposeToken = request.requestPurposeToken,
                sessionId = request.sessionId,
                eventIdOrOpportunityKey = request.eventIdOrOpportunityKey,
                povRoleToken = request.povRoleToken,
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                evidenceEpochToken = request.evidenceEpochToken,
                ownerCancellationGeneration = request.ownerCancellationGeneration,
                globalCancellationGeneration = request.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    request.optionalRequestInvalidationGeneration,
                attemptOrdinal = attempt.attemptOrdinal,
                variantKey = attempt.variantKey,
                receiptPlanFingerprint = variant.receipt.receiptPlanFingerprint,
                invocationSequence = nextSequence,
                invocationTick = invocationTick,
                narrativeUseWinnerAttemptOrdinal = winnerOrdinal
            };
            if (!MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                    permit.ToIdentity(), out permit.permitFingerprint))
            {
                return plan;
            }

            plan.canCommit = true;
            plan.outcomeToken = MemoryDispatchTokens.Success;
            plan.nextInvocationSequence = nextSequence;
            plan.narrativeUseWinnerAttemptOrdinal = winnerOrdinal;
            plan.narrativeUseWinnerVariantKey = winnerKey;
            plan.applyPotentialExposure = variant.receipt.evidence.Count > 0;
            plan.applyNarrativeUse = narrativeUse;
            plan.permit = permit;
            return plan;
        }

        public static MemoryTerminalCallbackPlan PlanTerminalCallback(
            MemoryLogicalRequestSnapshot request,
            MemoryInvocationCommitPermitV1 permit,
            MemoryDispatchFenceSnapshot currentFence,
            string terminalOutcomeToken,
            bool providerReturnedUsableResult)
        {
            MemoryTerminalCallbackPlan plan = new MemoryTerminalCallbackPlan();
            if (!MemoryDispatchTokens.IsTerminalOutcome(terminalOutcomeToken))
            {
                return plan;
            }

            if (!FenceMatches(request, currentFence))
            {
                plan.outcomeToken = MemoryDispatchTokens.Stale;
                return plan;
            }

            MemoryLogicalAttemptSnapshot attempt;
            MemoryFrozenPromptVariantSnapshot variant;
            if (!PermitMatchesCommittedRequest(request, permit, out attempt, out variant))
            {
                return plan;
            }

            if (attempt.resultApplied)
            {
                plan.duplicate = true;
                plan.outcomeToken = terminalOutcomeToken;
                return plan;
            }

            plan.accepted = true;
            plan.outcomeToken = terminalOutcomeToken;
            if (providerReturnedUsableResult && variant.receipt.evidence.Count > 0)
            {
                plan.applyConfirmedExposure = true;
                plan.orderedOperations.Add("confirmed_exposure_receipt");
            }
            if (providerReturnedUsableResult)
            {
                plan.applyResult = true;
                plan.orderedOperations.Add("result_publication");
            }
            plan.orderedOperations.Add("terminal_audit_and_remove");
            return plan;
        }

        public static MemoryLoadSettlementPlan PlanLoadedRequestSettlement(
            MemoryLogicalRequestSnapshot request)
        {
            MemoryLoadSettlementPlan plan = new MemoryLoadSettlementPlan();
            if (!ValidateRequest(request))
            {
                return plan;
            }

            plan.valid = true;
            MemoryLogicalAttemptSnapshot earliestNarrative = null;
            for (int index = 0; index < request.attempts.Count; index++)
            {
                MemoryLogicalAttemptSnapshot attempt = request.attempts[index];
                if (attempt.invocationSequence <= 0) continue;
                plan.hadCommittedInvocation = true;
                if (!attempt.potentialExposureApplied)
                {
                    plan.potentialExposureAttemptOrdinals.Add(attempt.attemptOrdinal);
                }

                MemoryFrozenPromptVariantSnapshot variant = Variant(request, attempt.variantKey);
                if (MemoryDispatchTokens.IsNarrativeUsePurpose(request.requestPurposeToken)
                    && variant != null && variant.receipt.evidence.Count > 0
                    && (earliestNarrative == null
                        || attempt.invocationSequence < earliestNarrative.invocationSequence
                        || (attempt.invocationSequence == earliestNarrative.invocationSequence
                            && attempt.attemptOrdinal < earliestNarrative.attemptOrdinal)))
                {
                    earliestNarrative = attempt;
                }
            }

            if (!plan.hadCommittedInvocation)
            {
                plan.outcomeToken = MemoryDispatchTokens.LoadInterruptedBeforeInvocation;
                plan.restoreNormalPovRetryable =
                    request.requestPurposeToken == MemoryDispatchTokens.NormalDiary;
                return plan;
            }

            plan.outcomeToken = MemoryDispatchTokens.LoadInterruptedAfterInvocation;
            if (earliestNarrative != null)
            {
                plan.repairedNarrativeUseWinnerAttemptOrdinal = earliestNarrative.attemptOrdinal;
                plan.repairedNarrativeUseWinnerVariantKey = earliestNarrative.variantKey;
            }
            return plan;
        }

        public static bool PermitFingerprintIsValid(MemoryInvocationCommitPermitV1 permit)
        {
            string expected;
            return permit != null
                && MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                    permit.ToIdentity(), out expected)
                && Equal(expected, permit.permitFingerprint);
        }

        private static bool ValidateVariant(
            MemoryLogicalRequestSnapshot request,
            MemoryFrozenPromptVariantSnapshot variant,
            int expectedOrdinal)
        {
            if (variant == null || variant.variantOrdinal != expectedOrdinal
                || variant.receipt == null
                || variant.receipt.evidence == null
                || variant.receipt.guards == null
                || variant.diagnostics == null
                || variant.receipt.evidence.Count > MaximumEvidencePerVariant
                || variant.receipt.guards.Count > MaximumGuardsPerVariant
                || variant.diagnostics.Count > MaximumDiagnosticsPerVariant)
            {
                return false;
            }

            string evidenceFingerprint;
            string receiptFingerprint;
            string diagnosticFingerprint;
            string variantKey;
            return MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    variant.receipt.evidence, out evidenceFingerprint)
                && Equal(evidenceFingerprint, variant.receipt.evidenceSetFingerprint)
                && MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    variant.receipt.evidence,
                    variant.receipt.guards,
                    out receiptFingerprint)
                && Equal(receiptFingerprint, variant.receipt.receiptPlanFingerprint)
                && MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    variant.diagnostics, out diagnosticFingerprint)
                && MemoryIdentityCodec.TryCreatePromptVariantKey(
                    request.logicalRequestId,
                    variant.variantOrdinal,
                    request.requestPurposeToken,
                    variant.templateIdentity,
                    variant.contextDetailIdentity,
                    variant.systemPrompt,
                    variant.userPrompt,
                    receiptFingerprint,
                    diagnosticFingerprint,
                    out variantKey)
                && Equal(variantKey, variant.variantKey);
        }

        private static bool ValidatePurposeReceiptShape(MemoryLogicalRequestSnapshot request)
        {
            if (request.requestPurposeToken == MemoryDispatchTokens.ManualRegenerate)
            {
                return request.reservedEvidence.Count == 0
                    && request.reservedGuards.Count == 0;
            }
            if (request.requestPurposeToken == MemoryDispatchTokens.SummaryWording)
            {
                return request.reservedEvidence.Count == 1
                    && request.reservedGuards.Count == 0;
            }
            return true;
        }

        private static bool ValidateAttempts(MemoryLogicalRequestSnapshot request)
        {
            if (request.lastIssuedAttemptOrdinal != request.attempts.Count)
            {
                return false;
            }

            long priorInvocationSequence = 0;
            for (int index = 0; index < request.attempts.Count; index++)
            {
                MemoryLogicalAttemptSnapshot attempt = request.attempts[index];
                int expectedOrdinal = index + 1;
                if (attempt == null || attempt.attemptOrdinal != expectedOrdinal
                    || Variant(request, attempt.variantKey) == null
                    || !MemoryDispatchTokens.IsAttemptOrigin(attempt.attemptOriginToken)
                    || !IsAttemptState(attempt.attemptStateToken)
                    || attempt.terminalTick != 0
                    || !string.IsNullOrEmpty(attempt.terminalOutcomeToken))
                {
                    return false;
                }

                if (expectedOrdinal == 1)
                {
                    if (attempt.attemptOriginToken != MemoryDispatchTokens.Initial
                        || attempt.predecessorAttemptOrdinal != 0) return false;
                }
                else
                {
                    MemoryLogicalAttemptSnapshot predecessor = Attempt(
                        request, attempt.predecessorAttemptOrdinal);
                    if (predecessor == null
                        || predecessor.attemptOrdinal >= attempt.attemptOrdinal
                        || (attempt.attemptOriginToken == MemoryDispatchTokens.Initial)
                        || (attempt.attemptOriginToken == MemoryDispatchTokens.Retry
                            && !Equal(predecessor.variantKey, attempt.variantKey))
                        || (attempt.attemptOriginToken == MemoryDispatchTokens.Failover
                            && Equal(predecessor.variantKey, attempt.variantKey)))
                    {
                        return false;
                    }
                }

                bool invoked = attempt.attemptStateToken
                    != MemoryRequestStateMachineContracts.AttemptPrepared;
                if (invoked
                    ? attempt.invocationSequence <= 0 || attempt.invocationTick <= 0
                    : attempt.invocationSequence != 0 || attempt.invocationTick != 0
                        || attempt.potentialExposureApplied
                        || attempt.narrativeUseApplied
                        || attempt.resultApplied)
                {
                    return false;
                }
                if (invoked && attempt.invocationSequence <= priorInvocationSequence)
                {
                    return false;
                }
                if (invoked) priorInvocationSequence = attempt.invocationSequence;
            }
            return true;
        }

        private static bool PermitMatchesCommittedRequest(
            MemoryLogicalRequestSnapshot request,
            MemoryInvocationCommitPermitV1 permit,
            out MemoryLogicalAttemptSnapshot attempt,
            out MemoryFrozenPromptVariantSnapshot variant)
        {
            attempt = permit == null ? null : Attempt(request, permit.attemptOrdinal);
            variant = attempt == null ? null : Variant(request, attempt.variantKey);
            if (!ValidateRequest(request) || !PermitFingerprintIsValid(permit)
                || attempt == null || variant == null
                || attempt.attemptStateToken
                    == MemoryRequestStateMachineContracts.AttemptPrepared
                || attempt.invocationSequence != permit.invocationSequence
                || attempt.invocationTick != permit.invocationTick
                || !Equal(variant.receipt.receiptPlanFingerprint,
                    permit.receiptPlanFingerprint)
                || permit.narrativeUseWinnerAttemptOrdinal
                    != request.narrativeUseWinnerAttemptOrdinal)
            {
                return false;
            }

            return Equal(request.logicalRequestId, permit.logicalRequestId)
                && Equal(request.logicalRequestKey, permit.logicalRequestKey)
                && Equal(request.requestPurposeToken, permit.requestPurposeToken)
                && request.sessionId == permit.sessionId
                && Equal(request.eventIdOrOpportunityKey, permit.eventIdOrOpportunityKey)
                && Equal(request.povRoleToken, permit.povRoleToken)
                && Equal(request.ownerPawnId, permit.ownerPawnId)
                && Equal(request.ownerEpochToken, permit.ownerEpochToken)
                && Equal(request.evidenceEpochToken, permit.evidenceEpochToken)
                && request.ownerCancellationGeneration
                    == permit.ownerCancellationGeneration
                && request.globalCancellationGeneration
                    == permit.globalCancellationGeneration
                && request.optionalRequestInvalidationGeneration
                    == permit.optionalRequestInvalidationGeneration
                && Equal(attempt.variantKey, permit.variantKey);
        }

        private static bool FenceMatches(
            MemoryLogicalRequestSnapshot request,
            MemoryDispatchFenceSnapshot fence)
        {
            return request != null && fence != null
                && request.sessionId == fence.sessionId
                && Equal(request.ownerPawnId, fence.ownerPawnId)
                && Equal(request.ownerEpochToken, fence.ownerEpochToken)
                && request.ownerCancellationGeneration
                    == fence.ownerCancellationGeneration
                && request.globalCancellationGeneration
                    == fence.globalCancellationGeneration
                && request.optionalRequestInvalidationGeneration
                    == fence.optionalRequestInvalidationGeneration;
        }

        private static bool ValidOptionalGeneration(MemoryLogicalRequestSnapshot request)
        {
            return MemoryDispatchTokens.IsOptionalPurpose(request.requestPurposeToken)
                ? request.optionalRequestInvalidationGeneration > 0
                    && request.optionalRequestInvalidationGeneration < long.MaxValue
                : request.optionalRequestInvalidationGeneration == 0;
        }

        private static bool IsRequestState(string value)
        {
            return value == MemoryRequestStateMachineContracts.Staged
                || value == MemoryRequestStateMachineContracts.Activated
                || value == MemoryRequestStateMachineContracts.InvocationCommitted
                || value == MemoryRequestStateMachineContracts.SettlementPending;
        }

        private static bool IsAttemptState(string value)
        {
            return value == MemoryRequestStateMachineContracts.AttemptPrepared
                || value == MemoryRequestStateMachineContracts.AttemptInvocationCommitted
                || value == MemoryRequestStateMachineContracts.AttemptReceiptApplied
                || value == MemoryRequestStateMachineContracts.AttemptTerminalPending;
        }

        private static MemoryFrozenPromptVariantSnapshot Variant(
            MemoryLogicalRequestSnapshot request,
            string key)
        {
            if (request?.variants == null) return null;
            for (int index = 0; index < request.variants.Count; index++)
            {
                MemoryFrozenPromptVariantSnapshot variant = request.variants[index];
                if (variant != null && Equal(variant.variantKey, key)) return variant;
            }
            return null;
        }

        private static MemoryLogicalAttemptSnapshot Attempt(
            MemoryLogicalRequestSnapshot request,
            int ordinal)
        {
            if (request?.attempts == null || ordinal <= 0 || ordinal > request.attempts.Count)
                return null;
            MemoryLogicalAttemptSnapshot attempt = request.attempts[ordinal - 1];
            return attempt != null && attempt.attemptOrdinal == ordinal ? attempt : null;
        }

        private static List<MemoryEvidenceIdentity> CanonicalEvidence(
            List<MemoryEvidenceIdentity> source)
        {
            List<MemoryEvidenceIdentity> result = source == null
                ? new List<MemoryEvidenceIdentity>()
                : new List<MemoryEvidenceIdentity>(source);
            result.Sort(CompareEvidence);
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (result[index] == null) return null;
                if (index > 0 && CompareEvidence(result[index - 1], result[index]) == 0)
                    result.RemoveAt(index);
            }
            return result;
        }

        private static List<MemoryGuardIdentity> CanonicalGuards(List<MemoryGuardIdentity> source)
        {
            List<MemoryGuardIdentity> result = source == null
                ? new List<MemoryGuardIdentity>()
                : new List<MemoryGuardIdentity>(source);
            result.Sort(CompareGuard);
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (result[index] == null) return null;
                if (index > 0 && CompareGuard(result[index - 1], result[index]) == 0)
                    result.RemoveAt(index);
            }
            return result;
        }

        private static bool EvidenceListsEqual(
            List<MemoryEvidenceIdentity> left,
            List<MemoryEvidenceIdentity> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (CompareEvidence(left[index], right[index]) != 0) return false;
            return true;
        }

        private static bool GuardListsEqual(
            List<MemoryGuardIdentity> left,
            List<MemoryGuardIdentity> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (CompareGuard(left[index], right[index]) != 0) return false;
            return true;
        }

        private static int CompareEvidence(MemoryEvidenceIdentity left, MemoryEvidenceIdentity right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int byRecord = string.CompareOrdinal(left.recordId, right.recordId);
            if (byRecord != 0) return byRecord;
            int bySource = string.CompareOrdinal(
                left.sourceOccurrenceId, right.sourceOccurrenceId);
            return bySource != 0
                ? bySource
                : string.CompareOrdinal(left.rootIdOrEmpty, right.rootIdOrEmpty);
        }

        private static int CompareGuard(MemoryGuardIdentity left, MemoryGuardIdentity right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int byKind = string.CompareOrdinal(left.guardKind, right.guardKind);
            return byKind != 0
                ? byKind
                : string.CompareOrdinal(left.guardKey, right.guardKey);
        }

        private static bool Equal(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty,
                StringComparison.Ordinal);
        }
    }
}
