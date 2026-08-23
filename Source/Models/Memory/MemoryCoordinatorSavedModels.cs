// MemoryCoordinatorSavedModels.cs — component-saved coordinator, diagnostic, audit, reservation,
// and repetition-guard rows (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.9, §T6.10).
//
// These rows are the saved extension of the EXISTING reflection coordinator — never a second
// scheduler. The component holds at most one summary-wording opportunity per owner; terminal
// attempt audit rows are Dev-only bounded metadata; legacy epoch reservations fence old saves so a
// removed owner can never silently reuse an epoch sequence.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>The last memory policy tuple applied to this save (§T6.9). Types/defaults mirror
    /// MemorySettingsPolicyFieldsV1; a missing or malformed row means "apply the full deferred
    /// delta" rather than trusting default Booleans.</summary>
    public partial class SavedMemoryAppliedPolicyStateV1 : IMemoryLogicalSizeSource
    {
        /// <summary>This row's version is exactly 1 by definition (§T6.0).</summary>
        public int schemaVersion = 1;
        public bool saveNewMemories = true;
        public bool useMemoriesInWriting = true;
        public bool usePawnBackground = true;
        public bool allowExtraMemoryAiRequests;
        public bool occasionalMemoryReflections;
        /// <summary>Only the four known category low bits may be set.</summary>
        public int memoryCategoryMask = 15;
        public long captureInvalidationGenerationPersonal = 1;
        public long captureInvalidationGenerationRelationships = 1;
        public long captureInvalidationGenerationFamily = 1;
        public long captureInvalidationGenerationFactions = 1;
        public long optionalRequestInvalidationGeneration = 1;
        public int minorMemoryLifetimeDays = 15;
        public int regularMemoryLifetimeDays = 60;
        public int memoryThreadTarget = 12;
        public int memoryReuseDays = 5;
        public int memoryRevisitEntryCount = 3;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref saveNewMemories, "saveNewMemories", false);
            Scribe_Values.Look(ref useMemoriesInWriting, "useMemoriesInWriting", false);
            Scribe_Values.Look(ref usePawnBackground, "usePawnBackground", false);
            Scribe_Values.Look(ref allowExtraMemoryAiRequests, "allowExtraMemoryAiRequests", false);
            Scribe_Values.Look(ref occasionalMemoryReflections,
                "occasionalMemoryReflections", false);
            Scribe_Values.Look(ref memoryCategoryMask, "memoryCategoryMask", 0);
            Scribe_Values.Look(ref captureInvalidationGenerationPersonal,
                "captureInvalidationGenerationPersonal", 0);
            Scribe_Values.Look(ref captureInvalidationGenerationRelationships,
                "captureInvalidationGenerationRelationships", 0);
            Scribe_Values.Look(ref captureInvalidationGenerationFamily,
                "captureInvalidationGenerationFamily", 0);
            Scribe_Values.Look(ref captureInvalidationGenerationFactions,
                "captureInvalidationGenerationFactions", 0);
            Scribe_Values.Look(ref optionalRequestInvalidationGeneration,
                "optionalRequestInvalidationGeneration", 0);
            Scribe_Values.Look(ref minorMemoryLifetimeDays, "minorMemoryLifetimeDays", 0);
            Scribe_Values.Look(ref regularMemoryLifetimeDays, "regularMemoryLifetimeDays", 0);
            Scribe_Values.Look(ref memoryThreadTarget, "memoryThreadTarget", 0);
            Scribe_Values.Look(ref memoryReuseDays, "memoryReuseDays", 0);
            Scribe_Values.Look(ref memoryRevisitEntryCount, "memoryRevisitEntryCount", 0);
        }

        public void Normalize()
        {
        }
    }

    /// <summary>One bounded Summary-wording opportunity row owned by the component (§T6.9). Rows
    /// serialize by owner ID, epoch, then opportunity key; at most one exists per owner/epoch.</summary>
    public partial class SavedSummaryWordingOpportunityV1 : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public string rootId = string.Empty;
        public string summaryRecordId = string.Empty;
        public long expectedRootStructuralRevision;
        public long expectedSummaryFactsRevision;
        public int expectedReducerRevision;
        public long expectedFormatRevision;
        public int expectedCategoryMask;
        public string projectionFingerprint = string.Empty;
        public long requestedTick;
        public long dueTick;
        public long expiryTick;
        public int configuredPriority;
        public int salience;
        /// <summary>Length-prefixed tuple of every identity/revision/fingerprint field above.</summary>
        public string opportunityKey = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref ownerCancellationGeneration, "ownerCancellationGeneration", 0);
            Scribe_Values.Look(ref globalCancellationGeneration, "globalCancellationGeneration", 0);
            Scribe_Values.Look(ref optionalRequestInvalidationGeneration,
                "optionalRequestInvalidationGeneration", 0);
            Scribe_Values.Look(ref rootId, "rootId", string.Empty);
            Scribe_Values.Look(ref summaryRecordId, "summaryRecordId", string.Empty);
            Scribe_Values.Look(ref expectedRootStructuralRevision,
                "expectedRootStructuralRevision", 0);
            Scribe_Values.Look(ref expectedSummaryFactsRevision,
                "expectedSummaryFactsRevision", 0);
            Scribe_Values.Look(ref expectedReducerRevision, "expectedReducerRevision", 0);
            Scribe_Values.Look(ref expectedFormatRevision, "expectedFormatRevision", 0);
            Scribe_Values.Look(ref expectedCategoryMask, "expectedCategoryMask", 0);
            Scribe_Values.Look(ref projectionFingerprint, "projectionFingerprint", string.Empty);
            Scribe_Values.Look(ref requestedTick, "requestedTick", 0);
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref expiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref configuredPriority, "configuredPriority", 0);
            Scribe_Values.Look(ref salience, "salience", 0);
            Scribe_Values.Look(ref opportunityKey, "opportunityKey", string.Empty);
        }

        public void Normalize()
        {
            ownerPawnId = ownerPawnId ?? string.Empty;
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            rootId = rootId ?? string.Empty;
            summaryRecordId = summaryRecordId ?? string.Empty;
            projectionFingerprint = projectionFingerprint ?? string.Empty;
            opportunityKey = opportunityKey ?? string.Empty;
        }
    }

    /// <summary>One deduplicated diagnostic counter (§T6.9). Unknown bounded tokens fold into one
    /// allowlisted "other" row rather than retaining prose.</summary>
    public partial class SavedMemoryDiagnosticCounter : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string reasonToken = string.Empty;
        public string scopeToken = string.Empty;
        /// <summary>Nonnegative and sticky at long.MaxValue.</summary>
        public long saturatedCount;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref reasonToken, "reasonToken", string.Empty);
            Scribe_Values.Look(ref scopeToken, "scopeToken", string.Empty);
            Scribe_Values.Look(ref saturatedCount, "saturatedCount", 0);
        }

        public void Normalize()
        {
            reasonToken = reasonToken ?? string.Empty;
            scopeToken = scopeToken ?? string.Empty;
        }
    }

    /// <summary>One TERMINAL transport attempt audit row for Dev diagnostics (§T6.9). Contains no
    /// prompt, response, endpoint, model credential, exception body, or live request object; the
    /// tuple (logicalRequestId, attemptOrdinal) is unique.</summary>
    public partial class SavedMemoryAttemptAuditRow : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string logicalRequestId = string.Empty;
        public string requestPurposeToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public int attemptOrdinal;
        public string variantKey = string.Empty;
        public long invocationTick;
        public long terminalTick;
        public string outcomeToken = string.Empty;
        public bool potentialExposure;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref logicalRequestId, "logicalRequestId", string.Empty);
            Scribe_Values.Look(ref requestPurposeToken, "requestPurposeToken", string.Empty);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref attemptOrdinal, "attemptOrdinal", 0);
            Scribe_Values.Look(ref variantKey, "variantKey", string.Empty);
            Scribe_Values.Look(ref invocationTick, "invocationTick", 0);
            Scribe_Values.Look(ref terminalTick, "terminalTick", 0);
            Scribe_Values.Look(ref outcomeToken, "outcomeToken", string.Empty);
            Scribe_Values.Look(ref potentialExposure, "potentialExposure", false);
        }

        public void Normalize()
        {
            logicalRequestId = logicalRequestId ?? string.Empty;
            requestPurposeToken = requestPurposeToken ?? string.Empty;
            ownerPawnId = ownerPawnId ?? string.Empty;
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            variantKey = variantKey ?? string.Empty;
            outcomeToken = outcomeToken ?? string.Empty;
        }
    }

    /// <summary>One pre-current-schema epoch reservation for an exact owner group (§T6.9). The epoch
    /// token itself is derived through the allocator and is deliberately not stored here.</summary>
    public partial class SavedLegacyOwnerEpochReservation : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string ownerPawnId = string.Empty;
        /// <summary>Syntactically valid only when greater than zero.</summary>
        public long reservedEpochSequence;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref reservedEpochSequence, "reservedEpochSequence", 0);
        }

        public void Normalize()
        {
            ownerPawnId = ownerPawnId ?? string.Empty;
        }
    }

    /// <summary>One non-record-level narrative-repetition guard row (§T6.10). Unique by
    /// (ownerEpochToken, guardKind, guardKey); serialization order is that same ordinal tuple.</summary>
    public partial class SavedMemoryRepetitionGuardRow : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string ownerEpochToken = string.Empty;
        /// <summary>Exactly root | subject | pair | novelty.</summary>
        public string guardKind = string.Empty;
        /// <summary>Canonical length-prefixed exact identity.</summary>
        public string guardKey = string.Empty;
        public long lastAutomaticIncludedTick;
        public long lastAutomaticIncludedEntryOrdinal;
        public long automaticInclusionCount;
        public string lastSourceOccurrenceId = string.Empty;
        public string lastCommittedLogicalRequestId = string.Empty;
        public string lastCommittedEvidenceSetFingerprint = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref guardKind, "guardKind", string.Empty);
            Scribe_Values.Look(ref guardKey, "guardKey", string.Empty);
            Scribe_Values.Look(ref lastAutomaticIncludedTick, "lastAutomaticIncludedTick", 0);
            Scribe_Values.Look(
                ref lastAutomaticIncludedEntryOrdinal, "lastAutomaticIncludedEntryOrdinal", 0);
            Scribe_Values.Look(ref automaticInclusionCount, "automaticInclusionCount", 0);
            Scribe_Values.Look(ref lastSourceOccurrenceId, "lastSourceOccurrenceId", string.Empty);
            Scribe_Values.Look(
                ref lastCommittedLogicalRequestId, "lastCommittedLogicalRequestId", string.Empty);
            Scribe_Values.Look(
                ref lastCommittedEvidenceSetFingerprint,
                "lastCommittedEvidenceSetFingerprint",
                string.Empty);
        }

        public void Normalize()
        {
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            guardKind = guardKind ?? string.Empty;
            guardKey = guardKey ?? string.Empty;
            lastSourceOccurrenceId = lastSourceOccurrenceId ?? string.Empty;
            lastCommittedLogicalRequestId = lastCommittedLogicalRequestId ?? string.Empty;
            lastCommittedEvidenceSetFingerprint =
                lastCommittedEvidenceSetFingerprint ?? string.Empty;
        }
    }
}
