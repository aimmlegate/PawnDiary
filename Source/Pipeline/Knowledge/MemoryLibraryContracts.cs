// MemoryLibraryContracts.cs — detached M5 handles, queries, results, and immutable-view DTO shapes.
//
// These plain objects contain no Pawn, Def, Verse, Unity, saved-list, or UI references. The component
// adapter copies canonical state into them on the main thread; pure policy/index code may therefore be
// exercised by the standalone MemoryThreadTests executable.
using System.Collections.Generic;

namespace PawnDiary
{
    internal static class MemoryLibraryStatuses
    {
        public const string Ready = "Ready";
        public const string Preparing = "Preparing";
        public const string Stale = "Stale";
        public const string Missing = "Missing";
        public const string Invalid = "Invalid";
    }

    internal static class MemoryLibraryScopes
    {
        public const string Active = "active";
        public const string ArchiveOnly = "archiveOnly";
        public const string UnresolvedImported = "unresolvedImported";
        public const string LegacyRawExact = "legacyRawExact";
        public const string LegacyRawUnknown = "legacyRawUnknown";
        public const string InertCurrentExact = "inertCurrentExact";
    }

    internal static class MemoryLibraryViews
    {
        public const string Threads = "Threads";
        public const string Standalone = "Standalone";
        public const string Imported = "Imported";
    }

    internal static class MemoryLibraryRowTags
    {
        public const string Thread = "Thread";
        public const string Standalone = "Standalone";
        public const string Imported = "Imported";
    }

    internal static class MemoryLibraryActions
    {
        public const string SetSuppressed = "SetSuppressed";
        public const string SaveWording = "SaveWording";
        public const string UseOriginalWording = "UseOriginalWording";
        public const string ForgetPermanent = "ForgetPermanent";
    }

    internal static class MemoryLibraryCommandStatuses
    {
        public const string Success = "Success";
        public const string Stale = "Stale";
        public const string Missing = "Missing";
        public const string Unauthorized = "Unauthorized";
        public const string CapFull = "CapFull";
        public const string RevisionSaturated = "RevisionSaturated";
        public const string Invalid = "Invalid";
        public const string Ineligible = "Ineligible";
    }

    internal sealed class MemoryLibraryOwnerHandle
    {
        public string scopeToken = string.Empty;
        public string exactOwnerPawnIdOrEmpty = string.Empty;
        public string epochTokenOrEmpty = string.Empty;

        public MemoryLibraryOwnerHandle() { }
        public MemoryLibraryOwnerHandle(string scope, string owner, string epoch)
        {
            scopeToken = scope ?? string.Empty;
            exactOwnerPawnIdOrEmpty = owner ?? string.Empty;
            epochTokenOrEmpty = epoch ?? string.Empty;
        }
    }

    internal sealed class MemoryOwnerEpochKey
    {
        public string ownerPawnId = string.Empty;
        public string epochToken = string.Empty;
    }

    internal sealed class MemoryRootHandle
    {
        public string ownerPawnId = string.Empty;
        public string epochToken = string.Empty;
        public string rootId = string.Empty;
    }

    internal sealed class MemoryRecordHandle
    {
        public string ownerPawnId = string.Empty;
        public string epochToken = string.Empty;
        public string recordId = string.Empty;
    }

    internal sealed class MemoryArchiveHandle
    {
        public string archiveScopeToken = string.Empty;
        public string exactOwnerPawnIdOrEmpty = string.Empty;
        public string archiveRecordId = string.Empty;
    }

    internal sealed class MemoryOwnerCultureDto
    {
        public string originStateToken = "none";
        public string originDisplayLabel = string.Empty;
        public string originProvenanceToken = "none";
        public string adoptedStateToken = "none";
        public string adoptedDisplayLabel = string.Empty;
    }

    internal sealed class MemoryLibraryOwnerRow
    {
        public MemoryLibraryOwnerHandle primaryHandle;
        public MemoryOwnerEpochKey activeOwnerEpochKey;
        public MemoryLibraryOwnerHandle compatibilityHandle;
        public string displayName = string.Empty;
        public string lifecycleToken = string.Empty;
        public MemoryOwnerCultureDto culture;
        public int threadCount;
        public int standaloneCount;
        public int importedCount;
        public long latestActivityTick;
        public bool hasArchive;
        public bool legacyRawPending;
        public long structuralRevision;
        public long statusRevision;
        public long compatibilitySourcePayloadRevision;
        public string normalizedSearch = string.Empty;
    }

    internal sealed class MemoryLibraryOwnerQuery
    {
        public string search = string.Empty;
        public string sortToken = "name";
        public int start;
        public int count = 64;
        public long expectedDirectoryRevision;
    }

    internal sealed class MemoryLibraryOwnerResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public int directoryRowCount;
        public int totalMatchedRows;
        public long additionalLegacyRawOwnersNotShown;
        public long additionalZeroMemoryOwnersNotShown;
        public int returnedStart;
        public int returnedCount;
        public int nextStart;
        public bool hasPrevious;
        public bool hasMore;
        public long directoryRevision;
        public string ownerEmptyStateToken = "none";
        public List<MemoryLibraryOwnerRow> rows = new List<MemoryLibraryOwnerRow>();
    }

    internal sealed class MemoryLibraryFilters
    {
        /// <summary>Zero means every importance/category.</summary>
        public int importanceMask;
        public int categoryMask;
        /// <summary>all | edited | suppressed | unsuppressed.</summary>
        public string stateToken = "all";
    }

    internal sealed class MemoryLibraryListQuery
    {
        public MemoryLibraryOwnerHandle primaryHandle;
        public MemoryOwnerEpochKey activeOwnerEpochKey;
        public string viewTag = MemoryLibraryViews.Threads;
        public MemoryLibraryFilters filters = new MemoryLibraryFilters();
        public string search = string.Empty;
        public string sortToken = "newest";
        public int listStart;
        public int listCount = 64;
        public long expectedDirectoryRevision;
        public long expectedListSnapshotRevision;
    }

    internal sealed class MemoryThreadHeaderRow
    {
        public MemoryRootHandle rootHandle;
        public string subjectLabel = string.Empty;
        public string subjectTypeToken = string.Empty;
        public long latestActivityTick;
        public int chapterCount;
        public int targetCountedVisibleBlockCount;
        public int manageableMemoryCount;
        public int highestImportanceMask;
        public int editedCount;
        public int suppressedCount;
        public string normalizedSearch = string.Empty;
        public long structuralRevision;
        public long statusRevision;
    }

    internal sealed class MemoryBlockRow
    {
        public MemoryRecordHandle recordHandle;
        public MemoryRootHandle rootHandle;
        public string chapterId = string.Empty;
        public long targetStructuralRevision;
        public string kind = string.Empty;
        public string summaryRole = string.Empty;
        public int projectedCategoryMask;
        /// <summary>Union of every importance represented by this exact returned projection.</summary>
        public int projectedImportanceMask;
        public int projectedHighestImportanceMask;
        public long originalTick;
        public long activityTick;
        public long projectedNextExpiryTick = long.MaxValue;
        public string displayWording = string.Empty;
        public string primarySubjectLabel = string.Empty;
        public bool playerEdited;
        public bool suppressed;
        public bool canSuppress;
        public bool canSaveWording;
        public bool canUseOriginal;
        public bool canDevForget;
        public long lastAutomaticIncludedTick;
        public long automaticInclusionCount;
        public string providerExposureState = string.Empty;
        public string normalizedSearch = string.Empty;
        /// <summary>Whole-container fields only; Summary contribution fields remain scratch-only.</summary>
        public string normalizedWholeSearch = string.Empty;
        public List<MemorySummaryContributionDescriptor> summaryContributions =
            new List<MemorySummaryContributionDescriptor>();
        public bool rollingSummary;
        public bool closedSummary;
        public bool ageUnknown;
    }

    /// <summary>
    /// Bounded detached facts for one Summary contribution. Search fields remain unnormalized so a
    /// query normalizes only one field into scratch at a time instead of retaining N normalized copies.
    /// </summary>
    internal sealed class MemorySummaryContributionDescriptor
    {
        public int sourceOrdinal;
        public int categoryMask;
        public int importanceMask;
        public long originalTick;
        public long nextExpiryTick = long.MaxValue;
        public bool ageUnknown;
        public string browsePreview = string.Empty;
        public List<string> searchFields = new List<string>();
        public List<string> factDescriptors = new List<string>();
        public List<string> subjectDescriptors = new List<string>();
        public List<string> provenanceDescriptors = new List<string>();
    }

    internal sealed class MemoryImportedRow
    {
        public MemoryArchiveHandle archiveHandle;
        public string preview = string.Empty;
        public long originalTick;
        public bool ageUnknown;
        public string migrationReasonToken = string.Empty;
        public long targetStructuralRevision;
    }

    /// <summary>
    /// Internal Imported index facts. Complete preserved wording stays separate from the bounded row
    /// returned to callers and is normalized into one transient query scratch at a time.
    /// </summary>
    internal sealed class MemoryImportedSearchDescriptor
    {
        public MemoryImportedRow row;
        public string rawSearchText = string.Empty;
    }

    /// <summary>
    /// Incremental pure Imported matcher. It retains only bounded returned-row DTO references and
    /// never stores a normalized copy of complete Imported wording.
    /// </summary>
    internal sealed class MemoryImportedListSelectionJob
    {
        public List<MemoryImportedSearchDescriptor> source =
            new List<MemoryImportedSearchDescriptor>();
        public string normalizedSearch = string.Empty;
        public string sortToken = string.Empty;
        public int cursor;
        public MemoryLibraryListSelection selection = new MemoryLibraryListSelection();
    }

    /// <summary>Complete immutable-domain list selection reused by pinned cursor pages.</summary>
    internal sealed class MemoryLibraryListSelection
    {
        public int totalEligibleRows;
        public bool sorted;
        public List<MemoryLibraryListRow> matched = new List<MemoryLibraryListRow>();
    }

    internal sealed class MemoryLibraryListRow
    {
        public string tag = string.Empty;
        public MemoryThreadHeaderRow thread;
        public MemoryBlockRow standalone;
        public MemoryImportedRow imported;
    }

    internal sealed class MemoryLibraryListResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public int totalEligibleRows;
        public int totalMatchedRows;
        public long ttlValidUntilTickExclusive = long.MaxValue;
        public int returnedStart;
        public int returnedCount;
        public int nextStart;
        public bool hasPrevious;
        public bool hasMore;
        public long directoryRevision;
        public long listSnapshotRevision;
        public long ownerStructuralRevision;
        public long ownerStatusRevision;
        public long committedSettingsRevision;
        public long languageDisplayRevision;
        public long ttlDayRevision;
        public string emptyStateToken = "none";
        public List<MemoryLibraryListRow> rows = new List<MemoryLibraryListRow>();
    }

    internal sealed class MemoryThreadDetailQuery
    {
        public MemoryRootHandle rootHandle;
        public MemoryLibraryFilters filters = new MemoryLibraryFilters();
        public string search = string.Empty;
        public int detailStart;
        public int detailCount = 64;
        public long expectedDetailSnapshotRevision;
    }

    internal sealed class MemoryChapterRow
    {
        public string chapterId = string.Empty;
        public long ordinal;
        public string phaseToken = string.Empty;
        public long openedTick;
        public long lastActivityTick;
        public long closedTick;
        public string closureReasonToken = string.Empty;
        public bool closed;
        public int returnedChildStart;
        public int returnedChildCount;
        public bool continuedFromPrevious;
        public bool continuesInNext;
    }

    internal sealed class MemoryCurrentStatusDto
    {
        public string statusToken = "Unknown";
        public List<string> frozenDisplayFields = new List<string>();
        public string knownnessEvidenceToken = string.Empty;
        public long sourceCaptureGeneration;
        public long capturedTick;
        public long statusSnapshotRevision;
    }

    internal sealed class MemoryThreadDetailResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public MemoryThreadHeaderRow header;
        public MemoryCurrentStatusDto currentStatus;
        public int shownManageableCount;
        public int totalManageableCount;
        public bool allBlocksSuppressedForWriting;
        public int returnedStart;
        public int returnedCount;
        public int nextStart;
        public bool hasPrevious;
        public bool hasMore;
        public long detailSnapshotRevision;
        public long ttlValidUntilTickExclusive = long.MaxValue;
        public List<MemoryChapterRow> chapters = new List<MemoryChapterRow>();
        public List<MemoryBlockRow> blocks = new List<MemoryBlockRow>();
    }

    internal sealed class MemoryBlockDetailQuery
    {
        public MemoryRecordHandle recordHandle;
        public MemoryRootHandle rootHandle;
        public string placementToken = string.Empty;
        public long targetStructuralRevision;
        public string projectionToken = "full";
        public MemoryLibraryFilters filters = new MemoryLibraryFilters();
        public string search = string.Empty;
    }

    internal sealed class MemoryBlockDetail
    {
        public List<string> factDescriptors = new List<string>();
        public List<string> subjectDescriptors = new List<string>();
        public List<string> provenanceDescriptors = new List<string>();
        public string sourcePageLinkToken = string.Empty;
        public string automaticWording = string.Empty;
        public string playerWording = string.Empty;
        public List<string> devIdentifiersAndReasons = new List<string>();
    }

    internal sealed class MemoryBlockDetailResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public MemoryBlockRow row;
        public MemoryBlockDetail detail;
        public long targetStructuralRevision;
        public long targetStatusRevision;
        public long ttlValidUntilTickExclusive = long.MaxValue;
    }

    internal sealed class MemoryImportedDetailQuery
    {
        public MemoryArchiveHandle archiveHandle;
        public int textStart;
        public int textCount = 240;
        public long expectedArchiveTextSnapshotRevision;
        public long targetStructuralRevision;
    }

    internal sealed class MemoryImportedDetailResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public MemoryArchiveHandle archiveHandle;
        public string textChunk = string.Empty;
        public int returnedTextStart;
        public int previousTextStart;
        public int nextTextStart;
        public int totalTextLength;
        public bool hasPrevious;
        public bool hasMore;
        public long archiveTextSnapshotRevision;
        public long targetStructuralRevision;
    }

    internal sealed class MemoryLegacyPendingDto
    {
        public MemoryLibraryOwnerHandle handle;
        public string stateToken = string.Empty;
        public string reasonToken = string.Empty;
        public long rowCount;
        public long logicalByteCount;
        public bool countWasClamped;
        public long sourcePayloadRevision;
        public string safePreview = string.Empty;
    }

    internal sealed class MemoryCompatibilityQuery
    {
        public MemoryLibraryOwnerHandle compatibilityHandle;
        public long sourcePayloadRevision;
    }

    internal sealed class MemoryCompatibilityResult
    {
        public string status = MemoryLibraryStatuses.Invalid;
        public string reasonToken = string.Empty;
        public long sourcePayloadRevision;
        public MemoryLegacyPendingDto pending;
    }

    internal sealed class MemoryLibraryCommand
    {
        public string libraryClientToken = string.Empty;
        public long commandId;
        public string actionToken = string.Empty;
        public MemoryRecordHandle recordHandle;
        public MemoryRootHandle rootHandle;
        public MemoryArchiveHandle archiveHandle;
        public string placementToken = string.Empty;
        public long targetStructuralRevision;
        public bool hasDesiredSuppressed;
        public bool desiredSuppressed;
        public string wordingDraft = string.Empty;
    }

    internal sealed class MemoryLibraryCommandResult
    {
        public string libraryClientToken = string.Empty;
        public long commandId;
        public string status = MemoryLibraryCommandStatuses.Invalid;
        public string reasonToken = string.Empty;
        public long resultingStructuralRevision;
    }

    /// <summary>Detached repository facts used to fixture command precedence without saved models.</summary>
    internal sealed class MemoryLibraryCommandTargetState
    {
        public bool commandShapeValid;
        public bool exists;
        public long currentStructuralRevision;
        public bool imported;
        public bool devAuthorized;
        public MemoryBlockRow activeRow;
    }

    /// <summary>Detached source row consumed by the pure owner-index builder.</summary>
    internal sealed class MemoryLibraryOwnerIndexInput
    {
        public MemoryLibraryOwnerHandle primaryHandle;
        public MemoryOwnerEpochKey ownerEpochKey;
        public MemoryLibraryOwnerHandle compatibilityHandle;
        public string displayName = string.Empty;
        public string lifecycleToken = string.Empty;
        public MemoryOwnerCultureDto culture;
        public long structuralRevision;
        public long statusRevision;
        public long compatibilitySourcePayloadRevision;
        public long snapshotNowTick;
        public long nextLocalizedDayBoundary = long.MaxValue;
        public List<MemoryLibraryRootIndexInput> roots = new List<MemoryLibraryRootIndexInput>();
        public List<MemoryBlockRow> standalone = new List<MemoryBlockRow>();
        public List<MemoryImportedSearchDescriptor> imported =
            new List<MemoryImportedSearchDescriptor>();
    }

    internal sealed class MemoryLibraryRootIndexInput
    {
        public MemoryThreadHeaderRow header;
        public MemoryCurrentStatusDto currentStatus;
        public List<MemoryBlockRow> children = new List<MemoryBlockRow>();
        public List<MemoryChapterRow> chapters = new List<MemoryChapterRow>();
        public long rootEarliestFiniteExpiryTickExclusive = long.MaxValue;
    }

    internal sealed class MemoryLibraryOwnerIndexSnapshot
    {
        public MemoryLibraryOwnerRow ownerRow;
        /// <summary>Directory-only raw saved-culture identity; never returned or used by list caches.</summary>
        public string directoryCultureSourceFingerprint = string.Empty;
        public long ownerEarliestFiniteExpiryTickExclusive = long.MaxValue;
        public List<MemoryLibraryRootIndexInput> roots = new List<MemoryLibraryRootIndexInput>();
        public List<MemoryBlockRow> standalone = new List<MemoryBlockRow>();
        public List<MemoryImportedSearchDescriptor> imported =
            new List<MemoryImportedSearchDescriptor>();
    }
}
