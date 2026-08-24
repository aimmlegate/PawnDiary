// MemoryDispatchSavedModels.cs — saved logical-request, frozen-variant, and attempt rows
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.11).
//
// One logical request exists in exactly one location: a memory-bearing DiaryEvent POV or the
// component's activeMemoryCoordinatorRequests list. The detached stage commit saves the complete
// row before activation; workers never own canonical state. These rows contain no live transport
// object, endpoint, model, credential, response, or exception body.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One frozen prompt-memory evidence identity in exact rendered line order (§T6.11).</summary>
    public partial class SavedFrozenEvidenceEntryV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string recordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string rootIdOrEmpty = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref recordId, "recordId", string.Empty);
            Scribe_Values.Look(ref sourceOccurrenceId, "sourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref rootIdOrEmpty, "rootIdOrEmpty", string.Empty);
        }

        public void Normalize()
        {
            recordId = recordId ?? string.Empty;
            sourceOccurrenceId = sourceOccurrenceId ?? string.Empty;
            rootIdOrEmpty = rootIdOrEmpty ?? string.Empty;
        }
    }

    /// <summary>One canonical repetition-guard identity reserved by a frozen variant (§T6.11).</summary>
    public partial class SavedFrozenGuardEntryV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string guardKind = string.Empty;
        public string guardKey = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref guardKind, "guardKind", string.Empty);
            Scribe_Values.Look(ref guardKey, "guardKey", string.Empty);
        }

        public void Normalize()
        {
            guardKind = guardKind ?? string.Empty;
            guardKey = guardKey ?? string.Empty;
        }
    }

    /// <summary>One bounded diagnostic-provenance row in canonical line-first order (§T6.11).</summary>
    public partial class SavedFrozenDiagnosticProvenanceV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string provenanceKindToken = string.Empty;
        public string sourceId = string.Empty;
        public string recordIdOrEmpty = string.Empty;
        public string sourceOccurrenceIdOrEmpty = string.Empty;
        public string rootIdOrEmpty = string.Empty;
        /// <summary>Zero-based line ordinal.</summary>
        public int lineOrdinal;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref provenanceKindToken, "provenanceKindToken", string.Empty);
            Scribe_Values.Look(ref sourceId, "sourceId", string.Empty);
            Scribe_Values.Look(ref recordIdOrEmpty, "recordIdOrEmpty", string.Empty);
            Scribe_Values.Look(ref sourceOccurrenceIdOrEmpty,
                "sourceOccurrenceIdOrEmpty", string.Empty);
            Scribe_Values.Look(ref rootIdOrEmpty, "rootIdOrEmpty", string.Empty);
            Scribe_Values.Look(ref lineOrdinal, "lineOrdinal", 0);
        }

        public void Normalize()
        {
            provenanceKindToken = provenanceKindToken ?? string.Empty;
            sourceId = sourceId ?? string.Empty;
            recordIdOrEmpty = recordIdOrEmpty ?? string.Empty;
            sourceOccurrenceIdOrEmpty = sourceOccurrenceIdOrEmpty ?? string.Empty;
            rootIdOrEmpty = rootIdOrEmpty ?? string.Empty;
        }
    }

    /// <summary>The frozen receipt plan of one variant: exact evidence set plus every required
    /// repetition guard (§T6.11).</summary>
    public partial class SavedFrozenEvidenceReceiptPlanV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string evidenceSetFingerprint = string.Empty;
        public List<SavedFrozenEvidenceEntryV1> evidenceEntries =
            new List<SavedFrozenEvidenceEntryV1>();
        public List<SavedFrozenGuardEntryV1> guardEntries = new List<SavedFrozenGuardEntryV1>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref evidenceSetFingerprint, "evidenceSetFingerprint", string.Empty);
            Scribe_Collections.Look(ref evidenceEntries, "evidenceEntries", LookMode.Deep);
            Scribe_Collections.Look(ref guardEntries, "guardEntries", LookMode.Deep);
        }

        public void Normalize()
        {
            evidenceSetFingerprint = evidenceSetFingerprint ?? string.Empty;
            evidenceEntries = evidenceEntries ?? new List<SavedFrozenEvidenceEntryV1>();
            for (int i = evidenceEntries.Count - 1; i >= 0; i--)
            {
                if (evidenceEntries[i] == null)
                {
                    evidenceEntries.RemoveAt(i);
                    continue;
                }

                evidenceEntries[i].Normalize();
            }

            guardEntries = guardEntries ?? new List<SavedFrozenGuardEntryV1>();
            for (int i = guardEntries.Count - 1; i >= 0; i--)
            {
                if (guardEntries[i] == null)
                {
                    guardEntries.RemoveAt(i);
                    continue;
                }

                guardEntries[i].Normalize();
            }
        }
    }

    /// <summary>One immutable frozen prompt variant (§T6.11). Retries reuse byte-identical strings
    /// and therefore the same variant key; lane/endpoint/model/credentials are deliberately absent.</summary>
    public partial class SavedFrozenPromptVariantV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        /// <summary>Zero-based.</summary>
        public int variantOrdinal;
        public string variantKey = string.Empty;
        public string templateIdentity = string.Empty;
        public string contextDetailIdentity = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public SavedFrozenEvidenceReceiptPlanV1 receiptPlan;
        public List<SavedFrozenDiagnosticProvenanceV1> diagnosticProvenance =
            new List<SavedFrozenDiagnosticProvenanceV1>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref variantOrdinal, "variantOrdinal", 0);
            Scribe_Values.Look(ref variantKey, "variantKey", string.Empty);
            Scribe_Values.Look(ref templateIdentity, "templateIdentity", string.Empty);
            Scribe_Values.Look(ref contextDetailIdentity, "contextDetailIdentity", string.Empty);
            Scribe_Values.Look(ref systemPrompt, "systemPrompt", string.Empty);
            Scribe_Values.Look(ref userPrompt, "userPrompt", string.Empty);
            Scribe_Deep.Look(ref receiptPlan, "receiptPlan");
            Scribe_Collections.Look(ref diagnosticProvenance, "diagnosticProvenance", LookMode.Deep);
        }

        public void Normalize()
        {
            variantKey = variantKey ?? string.Empty;
            templateIdentity = templateIdentity ?? string.Empty;
            contextDetailIdentity = contextDetailIdentity ?? string.Empty;
            systemPrompt = systemPrompt ?? string.Empty;
            userPrompt = userPrompt ?? string.Empty;
            diagnosticProvenance = diagnosticProvenance
                ?? new List<SavedFrozenDiagnosticProvenanceV1>();
            for (int i = diagnosticProvenance.Count - 1; i >= 0; i--)
            {
                if (diagnosticProvenance[i] == null)
                {
                    diagnosticProvenance.RemoveAt(i);
                    continue;
                }

                diagnosticProvenance[i].Normalize();
            }

            if (receiptPlan != null)
            {
                receiptPlan.Normalize();
            }
        }
    }

    /// <summary>One physical send attempt against one frozen variant (§T6.11). Attempt ordinal 0 is
    /// never a row; invocation/tick zeros are the pre-transition sentinels.</summary>
    public partial class SavedActiveLogicalAttemptV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public int attemptOrdinal;
        public string variantKey = string.Empty;
        /// <summary>Exactly initial | retry | failover.</summary>
        public string attemptOriginToken = string.Empty;
        public int predecessorAttemptOrdinal;
        /// <summary>prepared | invocation_committed | receipt_applied | terminal_pending.</summary>
        public string attemptStateToken = string.Empty;
        public long invocationSequence;
        public long invocationTick;
        public long terminalTick;
        /// <summary>Empty while nonterminal; only the §T6.11 outcome vocabulary when terminal.</summary>
        public string terminalOutcomeToken = string.Empty;
        public bool potentialExposureApplied;
        public bool narrativeUseApplied;
        public bool resultApplied;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref attemptOrdinal, "attemptOrdinal", 0);
            Scribe_Values.Look(ref variantKey, "variantKey", string.Empty);
            Scribe_Values.Look(ref attemptOriginToken, "attemptOriginToken", string.Empty);
            Scribe_Values.Look(ref predecessorAttemptOrdinal, "predecessorAttemptOrdinal", 0);
            Scribe_Values.Look(ref attemptStateToken, "attemptStateToken", string.Empty);
            Scribe_Values.Look(ref invocationSequence, "invocationSequence", 0);
            Scribe_Values.Look(ref invocationTick, "invocationTick", 0);
            Scribe_Values.Look(ref terminalTick, "terminalTick", 0);
            Scribe_Values.Look(ref terminalOutcomeToken, "terminalOutcomeToken", string.Empty);
            Scribe_Values.Look(ref potentialExposureApplied, "potentialExposureApplied", false);
            Scribe_Values.Look(ref narrativeUseApplied, "narrativeUseApplied", false);
            Scribe_Values.Look(ref resultApplied, "resultApplied", false);
        }

        public void Normalize()
        {
            variantKey = variantKey ?? string.Empty;
            attemptOriginToken = attemptOriginToken ?? string.Empty;
            attemptStateToken = attemptStateToken ?? string.Empty;
            terminalOutcomeToken = terminalOutcomeToken ?? string.Empty;
        }
    }

    /// <summary>
    /// One active memory logical request (§T6.11). A loaded session never reactivates or resends a
    /// saved row; load settlement normalizes it before coordinator/capture/dispatch eligibility.
    /// </summary>
    public partial class SavedActiveLogicalRequestV1 : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public long logicalRequestSequence;
        public string logicalRequestId = string.Empty;
        public string logicalRequestKey = string.Empty;
        /// <summary>normal_diary | memory_reflection | summary_wording | manual_regenerate.</summary>
        public string requestPurposeToken = string.Empty;
        public long sessionId;
        public string eventIdOrOpportunityKey = string.Empty;
        /// <summary>initiator | recipient | neutral.</summary>
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string evidenceEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        /// <summary>Positive for optional purposes; exactly 0 for normal_diary/manual_regenerate.</summary>
        public long optionalRequestInvalidationGeneration;
        /// <summary>staged | activated | invocation_committed | settlement_pending.</summary>
        public string requestStateToken = string.Empty;
        public int lastIssuedAttemptOrdinal;
        public int narrativeUseWinnerAttemptOrdinal;
        public string narrativeUseWinnerVariantKey = string.Empty;
        public List<SavedFrozenPromptVariantV1> frozenVariants =
            new List<SavedFrozenPromptVariantV1>();
        public List<SavedActiveLogicalAttemptV1> activeAttempts =
            new List<SavedActiveLogicalAttemptV1>();
        public List<SavedFrozenEvidenceEntryV1> reservedEvidenceEntries =
            new List<SavedFrozenEvidenceEntryV1>();
        public List<SavedFrozenGuardEntryV1> reservedGuardEntries =
            new List<SavedFrozenGuardEntryV1>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref logicalRequestSequence, "logicalRequestSequence", 0);
            Scribe_Values.Look(ref logicalRequestId, "logicalRequestId", string.Empty);
            Scribe_Values.Look(ref logicalRequestKey, "logicalRequestKey", string.Empty);
            Scribe_Values.Look(ref requestPurposeToken, "requestPurposeToken", string.Empty);
            Scribe_Values.Look(ref sessionId, "sessionId", 0);
            Scribe_Values.Look(ref eventIdOrOpportunityKey, "eventIdOrOpportunityKey", string.Empty);
            Scribe_Values.Look(ref povRoleToken, "povRoleToken", string.Empty);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref evidenceEpochToken, "evidenceEpochToken", string.Empty);
            Scribe_Values.Look(ref ownerCancellationGeneration,
                "ownerCancellationGeneration", 0);
            Scribe_Values.Look(ref globalCancellationGeneration,
                "globalCancellationGeneration", 0);
            Scribe_Values.Look(ref optionalRequestInvalidationGeneration,
                "optionalRequestInvalidationGeneration", 0);
            Scribe_Values.Look(ref requestStateToken, "requestStateToken", string.Empty);
            Scribe_Values.Look(ref lastIssuedAttemptOrdinal, "lastIssuedAttemptOrdinal", 0);
            Scribe_Values.Look(ref narrativeUseWinnerAttemptOrdinal,
                "narrativeUseWinnerAttemptOrdinal", 0);
            Scribe_Values.Look(ref narrativeUseWinnerVariantKey,
                "narrativeUseWinnerVariantKey", string.Empty);
            Scribe_Collections.Look(ref frozenVariants, "frozenVariants", LookMode.Deep);
            Scribe_Collections.Look(ref activeAttempts, "activeAttempts", LookMode.Deep);
            Scribe_Collections.Look(
                ref reservedEvidenceEntries, "reservedEvidenceEntries", LookMode.Deep);
            Scribe_Collections.Look(
                ref reservedGuardEntries, "reservedGuardEntries", LookMode.Deep);
        }

        public void Normalize()
        {
            logicalRequestId = logicalRequestId ?? string.Empty;
            logicalRequestKey = logicalRequestKey ?? string.Empty;
            requestPurposeToken = requestPurposeToken ?? string.Empty;
            eventIdOrOpportunityKey = eventIdOrOpportunityKey ?? string.Empty;
            povRoleToken = povRoleToken ?? string.Empty;
            ownerPawnId = ownerPawnId ?? string.Empty;
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            evidenceEpochToken = evidenceEpochToken ?? string.Empty;
            requestStateToken = requestStateToken ?? string.Empty;
            narrativeUseWinnerVariantKey = narrativeUseWinnerVariantKey ?? string.Empty;
            frozenVariants = frozenVariants ?? new List<SavedFrozenPromptVariantV1>();
            for (int i = frozenVariants.Count - 1; i >= 0; i--)
            {
                if (frozenVariants[i] == null)
                {
                    frozenVariants.RemoveAt(i);
                    continue;
                }

                frozenVariants[i].Normalize();
            }

            activeAttempts = activeAttempts ?? new List<SavedActiveLogicalAttemptV1>();
            for (int i = activeAttempts.Count - 1; i >= 0; i--)
            {
                if (activeAttempts[i] == null)
                {
                    activeAttempts.RemoveAt(i);
                    continue;
                }

                activeAttempts[i].Normalize();
            }

            reservedEvidenceEntries = reservedEvidenceEntries
                ?? new List<SavedFrozenEvidenceEntryV1>();
            for (int i = reservedEvidenceEntries.Count - 1; i >= 0; i--)
            {
                if (reservedEvidenceEntries[i] == null)
                {
                    reservedEvidenceEntries.RemoveAt(i);
                    continue;
                }

                reservedEvidenceEntries[i].Normalize();
            }

            reservedGuardEntries = reservedGuardEntries
                ?? new List<SavedFrozenGuardEntryV1>();
            for (int i = reservedGuardEntries.Count - 1; i >= 0; i--)
            {
                if (reservedGuardEntries[i] == null)
                {
                    reservedGuardEntries.RemoveAt(i);
                    continue;
                }

                reservedGuardEntries[i].Normalize();
            }
        }
    }
}
