// DiaryGameComponent.MemoryLibrary.cs — main-thread M5 no-create Library repository adapter.
//
// Saved models never escape this partial. Ordinary update slices requested directory work, builds only
// selected complete owner snapshots into a bounded LRU, publishes immutable query views through one
// loaded-session revision clock, and drains commands outside IMGUI draw. Queries never create an owner,
// allocate an epoch, resolve culture from live pawn state, or normalize saved rows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class MemoryLibraryPublication
        {
            internal string fingerprint = string.Empty;
            internal long revision;
            internal string ownerKey = string.Empty;
            internal MemoryLibraryOwnerIndexSnapshot ownerSnapshot;
            internal long directoryRevision;
            internal long committedSettingsRevision;
            internal long languageDisplayRevision;
            internal long ttlDayRevision;
            internal long nextDayBoundary;
            internal long ttlValidUntilTickExclusive = long.MaxValue;
            internal string textContent = string.Empty;
            internal MemoryLibraryListSelection listSelection;
        }

        private sealed class MemoryLibraryOwnerSource
        {
            internal string kind = string.Empty;
            internal string ownerKey = string.Empty;
            internal string sourceFingerprint = string.Empty;
            internal PawnDiaryRecord diary;
            internal PawnKnowledgeState state;
            internal string displayName = string.Empty;
            internal bool active;
            internal MemoryLibraryOwnerIndexSnapshot headerSnapshot;
            internal MemoryCompatibilityCandidate compatibilityCandidate;
        }

        private sealed class MemoryCompatibilityCandidate
        {
            internal MemoryLibraryOwnerHandle handle;
            internal string stateToken = string.Empty;
            internal string reasonToken = string.Empty;
            internal long rowCount;
            internal long logicalByteCount;
            internal bool countWasClamped;
            internal string safePreview = string.Empty;
            internal string sourceFingerprint = string.Empty;
        }

        private sealed class MemoryCompatibilitySourcePublication
        {
            internal string fingerprint = string.Empty;
            internal long revision;
            internal MemoryLegacyPendingDto pending;
        }

        private sealed class MemoryLibraryDirectoryBuildJob
        {
            internal int diaryStateVersion;
            internal long observationPublicationRevision;
            internal long settingsRevision;
            internal long ttlDayRevision;
            internal LoadedLanguage language;
            internal int diaryCount;
            internal int unresolvedCount;
            internal int rawUnresolvedCount;
            internal int cursor;
            internal List<MemoryLibraryOwnerSource> work = new List<MemoryLibraryOwnerSource>();
            internal List<MemoryLibraryOwnerIndexSnapshot> data =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            internal List<MemoryLibraryOwnerIndexSnapshot> raw =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            internal List<MemoryLibraryOwnerIndexSnapshot> zero =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            internal Dictionary<string, MemoryLibraryOwnerSource> sources =
                new Dictionary<string, MemoryLibraryOwnerSource>(StringComparer.Ordinal);
        }

        /// <summary>
        /// One most-recent complete Imported search for a cached owner. All inputs are detached and
        /// revision-fenced before the update loop advances one full-row scratch at a time.
        /// </summary>
        private sealed class MemoryLibraryListBuildJob
        {
            internal string ownerKey = string.Empty;
            internal string ownerSourceFingerprint = string.Empty;
            internal string streamFingerprint = string.Empty;
            internal string contentFingerprint = string.Empty;
            internal MemoryLibraryOwnerIndexSnapshot ownerSnapshot;
            internal MemoryLibraryListQuery query;
            internal MemoryLibraryLimits limits;
            internal MemoryImportedListSelectionJob selectionJob;
            internal int diaryStateVersion;
            internal long observationPublicationRevision;
            internal long directoryRevision;
            internal long committedSettingsRevision;
            internal long languageDisplayRevision;
            internal long ttlDayRevision;
            internal long nextDayBoundary;
            internal long ttlValidUntilTickExclusive = long.MaxValue;
        }

        private readonly MemoryLibraryPublicationClock memoryLibraryClock =
            new MemoryLibraryPublicationClock();
        private readonly Dictionary<string, MemoryLibraryOwnerIndexSnapshot> memoryLibraryOwners =
            new Dictionary<string, MemoryLibraryOwnerIndexSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryOwnerSource> memoryLibraryOwnerSources =
            new Dictionary<string, MemoryLibraryOwnerSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> memoryLibraryOwnerCacheFingerprints =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly LinkedList<string> memoryLibraryOwnerLru = new LinkedList<string>();
        private readonly List<MemoryLibraryOwnerRow> memoryLibraryDirectory =
            new List<MemoryLibraryOwnerRow>();
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryListPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryDetailPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryTextPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryListBuildJob> memoryLibraryListBuildJobs =
            new Dictionary<string, MemoryLibraryListBuildJob>(StringComparer.Ordinal);
        private readonly LinkedList<string> memoryLibraryListBuildOrder = new LinkedList<string>();
        private readonly Dictionary<string, MemoryCompatibilitySourcePublication>
            memoryLibraryCompatibilityPublications =
                new Dictionary<string, MemoryCompatibilitySourcePublication>(StringComparer.Ordinal);
        private readonly List<MemoryLibraryCommand> memoryLibraryPendingCommands =
            new List<MemoryLibraryCommand>();
        private readonly Dictionary<string, MemoryLibraryCommandResult> memoryLibraryCommandResults =
            new Dictionary<string, MemoryLibraryCommandResult>(StringComparer.Ordinal);
        /// <summary>
        /// Exact transient UI clients. The singleton window normally contributes one token; the set
        /// keeps lifecycle cleanup correct if a stale close races a replacement during game changes.
        /// </summary>
        private readonly HashSet<string> memoryLibraryActiveClients =
            new HashSet<string>(StringComparer.Ordinal);
        private long memoryLibraryDirectoryRevision;
        private string memoryLibraryDirectoryFingerprint = string.Empty;
        private long memoryLibraryAdditionalLegacyRawOwners;
        private long memoryLibraryAdditionalZeroOwners;
        private MemoryLibraryDirectoryBuildJob memoryLibraryDirectoryBuildJob;
        private bool memoryLibraryDirectoryBuildRequested;
        private string memoryLibraryPendingOwnerBuildKey = string.Empty;
        private int memoryLibraryObservedDiaryStateVersion = int.MinValue;
        private long memoryLibraryObservedObservationPublicationRevision = -1;
        private long memoryLibraryObservedSettingsRevision = -1;
        private long memoryLibraryObservedTtlDayRevision = -1;
        private LoadedLanguage memoryLibraryObservedLanguage;
        private int memoryLibraryObservedDiaryCount = -1;
        private int memoryLibraryObservedUnresolvedCount = -1;
        private int memoryLibraryObservedRawUnresolvedCount = -1;
        private long memoryLibraryLanguageDisplayRevision = 1;
        // Loaded RimTests query the detached Library synchronously while the developer's real
        // colony may still have a legitimate observation reconciliation queued. The trusted test
        // seam below bypasses only this publication fence for one call; it never advances or clears
        // the real observation queue.
        private bool memoryLibraryObservationFenceBypassedForTests;

        /// <summary>Clears every loaded-session publication/cache/command identity.</summary>
        private void ResetMemoryLibraryTransient()
        {
            memoryLibraryClock.Reset();
            memoryLibraryOwners.Clear();
            memoryLibraryOwnerSources.Clear();
            memoryLibraryOwnerCacheFingerprints.Clear();
            memoryLibraryOwnerLru.Clear();
            memoryLibraryDirectory.Clear();
            memoryLibraryListPublications.Clear();
            memoryLibraryDetailPublications.Clear();
            memoryLibraryTextPublications.Clear();
            memoryLibraryListBuildJobs.Clear();
            memoryLibraryListBuildOrder.Clear();
            memoryLibraryCompatibilityPublications.Clear();
            memoryLibraryPendingCommands.Clear();
            memoryLibraryCommandResults.Clear();
            memoryLibraryActiveClients.Clear();
            memoryLibraryDirectoryRevision = 0;
            memoryLibraryDirectoryFingerprint = string.Empty;
            memoryLibraryAdditionalLegacyRawOwners = 0;
            memoryLibraryAdditionalZeroOwners = 0;
            memoryLibraryDirectoryBuildJob = null;
            memoryLibraryDirectoryBuildRequested = false;
            memoryLibraryPendingOwnerBuildKey = string.Empty;
            memoryLibraryObservedDiaryStateVersion = int.MinValue;
            memoryLibraryObservedObservationPublicationRevision = -1;
            memoryLibraryObservedSettingsRevision = -1;
            memoryLibraryObservedTtlDayRevision = -1;
            memoryLibraryObservedLanguage = null;
            memoryLibraryObservedDiaryCount = -1;
            memoryLibraryObservedUnresolvedCount = -1;
            memoryLibraryObservedRawUnresolvedCount = -1;
            memoryLibraryLanguageDisplayRevision = 1;
            memoryLibraryObservationFenceBypassedForTests = false;
        }

        /// <summary>
        /// Advances only requested dirty Library work outside draw. Directory headers are sliced and
        /// selected owner indexes are built lazily into the bounded LRU.
        /// </summary>
        private void RefreshMemoryLibraryPublications()
        {
            if (!MemoryPolicyIsReconciled()) return;
            // Source observation is useful only to a live Library. Explicit direct queries (including
            // loaded fixtures) still set a build request and are allowed to drain below.
            if (memoryLibraryActiveClients.Count > 0
                && memoryLibraryDirectoryRevision > 0 && MemoryLibrarySourceTupleChanged())
                memoryLibraryDirectoryBuildRequested = true;
            if (memoryLibraryDirectoryBuildRequested && memoryLibraryDirectoryBuildJob == null
                && MemoryLibraryObservationFenceSatisfied)
                memoryLibraryDirectoryBuildJob = StartMemoryLibraryDirectoryBuild();
            if (memoryLibraryDirectoryBuildJob != null)
                AdvanceMemoryLibraryDirectoryBuild();
            if (!string.IsNullOrEmpty(memoryLibraryPendingOwnerBuildKey))
                BuildPendingMemoryLibraryOwnerIndex();
            if (memoryLibraryListBuildOrder.Count > 0)
                AdvanceMemoryLibraryListBuild();
        }

        /// <summary>
        /// Advances one deterministic loaded-test slice without consuming the player's pending
        /// observation reconciliation. Production callers always use the fenced method above.
        /// </summary>
        internal void RefreshMemoryLibraryPublicationsForTests()
        {
            bool previous = memoryLibraryObservationFenceBypassedForTests;
            memoryLibraryObservationFenceBypassedForTests = true;
            try
            {
                RefreshMemoryLibraryPublications();
            }
            finally
            {
                memoryLibraryObservationFenceBypassedForTests = previous;
            }
        }

        private bool MemoryLibraryObservationFenceSatisfied =>
            memoryLibraryObservationFenceBypassedForTests
            || MemoryObservationPublicationIsStable;

        /// <summary>
        /// Returns a small value stamp for the UI's warm-query gate. No saved object or detached row is
        /// copied, normalized, or created by this read.
        /// </summary>
        internal MemoryLibraryUiRepositoryStamp MemoryLibraryRepositoryStampForUi()
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return new MemoryLibraryUiRepositoryStamp
            {
                diaryStateRevision = DiaryStateVersion.Current,
                observationPublicationRevision = memoryObservationPublicationRevision,
                settingsRevision = MemoryEffectivePolicyProvider.PublicationRevision,
                ttlDayRevision = now / 60000L,
                directoryRevision = memoryLibraryDirectoryRevision,
                publicationRevision = memoryLibraryClock.LastIssuedRevision,
                languageDisplayRevision = memoryLibraryLanguageDisplayRevision,
                diaryCount = diaries?.Count ?? 0,
                unresolvedCount = unresolvedOwnerArchiveRows?.Count ?? 0,
                rawUnresolvedCount = rawUnresolvedOwnerArchiveInput?.Count ?? 0
            };
        }

        /// <summary>Registers one open Library window for paused observation and sliced publications.</summary>
        internal void BeginMemoryLibraryClient(string clientToken)
        {
            if (string.IsNullOrWhiteSpace(clientToken)) return;
            memoryLibraryActiveClients.Add(clientToken);
            if (memoryLibraryDirectoryRevision <= 0 || MemoryLibrarySourceTupleChanged())
                memoryLibraryDirectoryBuildRequested = true;
        }

        /// <summary>
        /// Advances only the unstable observation batch blocking an open Library while game ticks are
        /// paused. The ordinary elapsed-game-time observation schedule remains on GameComponentTick.
        /// </summary>
        private void AdvancePausedMemoryLibraryObservation()
        {
            bool paused = Find.TickManager?.Paused == true;
            if (!MemoryLibraryUiPollPolicy.ShouldAdvancePausedObservation(
                    paused,
                    memoryLibraryActiveClients.Count > 0,
                    MemoryObservationPublicationIsStable)) return;
            TickMemoryObservation(Math.Max(0, Find.TickManager?.TicksGame ?? 0));
        }

        /// <summary>Returns one detached paged owner directory; never creates saved state.</summary>
        internal MemoryLibraryOwnerResult QueryMemoryLibraryOwners(MemoryLibraryOwnerQuery query)
        {
            if (memoryLibraryDirectoryRevision <= 0 || MemoryLibrarySourceTupleChanged())
            {
                memoryLibraryDirectoryBuildRequested = true;
                return new MemoryLibraryOwnerResult { status = MemoryLibraryStatuses.Preparing };
            }
            return MemoryLibraryIndexPolicy.QueryOwners(
                memoryLibraryDirectory,
                query,
                memoryLibraryDirectoryRevision,
                BuildMemoryLibraryLimits(),
                memoryLibraryAdditionalLegacyRawOwners,
                memoryLibraryAdditionalZeroOwners);
        }

        /// <summary>Returns one owner/view window pinned to a transient list publication.</summary>
        internal MemoryLibraryListResult QueryMemoryLibraryList(MemoryLibraryListQuery query)
        {
            if (query?.primaryHandle == null || !MemoryLibraryPolicy.ValidOwnerHandle(
                    query.primaryHandle))
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Invalid };
            if (query.expectedDirectoryRevision < 0
                || query.expectedListSnapshotRevision < 0)
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Invalid };
            if (memoryLibraryDirectoryRevision <= 0)
            {
                memoryLibraryDirectoryBuildRequested = true;
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Preparing };
            }
            MemoryLibraryOwnerRow directoryOwner = FindPrimaryDirectoryRow(query.primaryHandle);
            if (directoryOwner == null)
                return new MemoryLibraryListResult
                {
                    status = MemoryLibraryStatuses.Missing
                };
            string ownerKey = OwnerIndexKey(query.primaryHandle);
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);

            // The sole no-envelope active-owner form is proven by the current directory revision.
            bool zeroNoEpoch = query.primaryHandle.scopeToken == MemoryLibraryScopes.Active
                && string.IsNullOrEmpty(query.primaryHandle.epochTokenOrEmpty)
                && query.activeOwnerEpochKey == null
                && directoryOwner.threadCount == 0
                && directoryOwner.standaloneCount == 0
                && directoryOwner.importedCount == 0;
            if (zeroNoEpoch)
            {
                if (query.listStart != 0 || query.expectedDirectoryRevision <= 0
                    || (query.viewTag != MemoryLibraryViews.Threads
                        && query.viewTag != MemoryLibraryViews.Standalone))
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Invalid };
                if (query.expectedDirectoryRevision != memoryLibraryDirectoryRevision)
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                string zeroStream = ListQueryFingerprint(query, directoryOwner);
                long zeroBoundary = NextMemoryLibraryDayBoundary();
                string zeroContent = MemoryLibraryPolicy.StreamFingerprint(
                    zeroStream,
                    MemoryEffectivePolicyProvider.PublicationRevision
                        .ToString(CultureInfo.InvariantCulture),
                    memoryLibraryLanguageDisplayRevision.ToString(CultureInfo.InvariantCulture),
                    (Math.Max(0, Find.TickManager?.TicksGame ?? 0) / 60000L)
                        .ToString(CultureInfo.InvariantCulture),
                    zeroBoundary.ToString(CultureInfo.InvariantCulture));
                MemoryLibraryPublication zeroPublication = ResolveCompleteLibraryPublication(
                    memoryLibraryListPublications,
                    zeroStream,
                    zeroContent,
                    ownerKey,
                    null,
                    query.expectedListSnapshotRevision,
                    zeroBoundary,
                    zeroBoundary,
                    string.Empty);
                if (zeroPublication == null)
                    return query.expectedListSnapshotRevision > 0
                        ? new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale }
                        : InvalidList("library_revision_saturated");
                if (query.expectedListSnapshotRevision > 0
                    && LibraryPublicationExpiredOrSuperseded(zeroPublication))
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                return new MemoryLibraryListResult
                {
                    status = MemoryLibraryStatuses.Ready,
                    directoryRevision = zeroPublication.directoryRevision,
                    listSnapshotRevision = zeroPublication.revision,
                    committedSettingsRevision = zeroPublication.committedSettingsRevision,
                    languageDisplayRevision = zeroPublication.languageDisplayRevision,
                    ttlDayRevision = zeroPublication.ttlDayRevision,
                    emptyStateToken = "no_memories",
                    ttlValidUntilTickExclusive = zeroPublication.nextDayBoundary
                };
            }

            if (!memoryLibraryOwners.TryGetValue(ownerKey, out MemoryLibraryOwnerIndexSnapshot owner))
            {
                if (query.expectedListSnapshotRevision > 0)
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                RequestMemoryLibraryOwnerBuild(ownerKey);
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Preparing };
            }
            if (MemoryLibraryPolicy.OwnerSnapshotReachedExpiry(
                    now, owner.ownerEarliestFiniteExpiryTickExclusive))
            {
                ExpireMemoryLibraryOwnerSnapshot(ownerKey);
                if (query.expectedListSnapshotRevision > 0)
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                RequestMemoryLibraryOwnerBuild(ownerKey);
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Preparing };
            }
            TouchMemoryLibraryOwner(ownerKey);

            string fingerprint = ListQueryFingerprint(query, owner.ownerRow);
            if (query.expectedListSnapshotRevision > 0)
            {
                if (!memoryLibraryListPublications.TryGetValue(
                        fingerprint, out MemoryLibraryPublication pinned)
                    || pinned.revision != query.expectedListSnapshotRevision
                    || pinned.ownerSnapshot == null
                    || LibraryPublicationExpiredOrSuperseded(pinned))
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                if (query.viewTag == MemoryLibraryViews.Imported)
                {
                    if (pinned.listSelection == null)
                        return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                    return MemoryLibraryIndexPolicy.QueryListSelection(
                        pinned.ownerSnapshot,
                        query,
                        pinned.listSelection,
                        pinned.directoryRevision,
                        pinned.revision,
                        pinned.committedSettingsRevision,
                        pinned.languageDisplayRevision,
                        pinned.ttlDayRevision,
                        pinned.nextDayBoundary,
                        BuildMemoryLibraryLimits());
                }
                return MemoryLibraryIndexPolicy.QueryList(
                    pinned.ownerSnapshot,
                    query,
                    pinned.directoryRevision,
                    pinned.revision,
                    pinned.committedSettingsRevision,
                    pinned.languageDisplayRevision,
                    pinned.ttlDayRevision,
                    pinned.nextDayBoundary,
                    BuildMemoryLibraryLimits());
            }
            long nextBoundary = NextMemoryLibraryDayBoundary();
            long settingsRevision = MemoryEffectivePolicyProvider.PublicationRevision;
            long ttlDayRevision = now / 60000L;
            if (query.viewTag == MemoryLibraryViews.Imported)
            {
                if (query.expectedDirectoryRevision > 0
                    && query.expectedDirectoryRevision != memoryLibraryDirectoryRevision)
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                if (!memoryLibraryOwnerCacheFingerprints.TryGetValue(
                        ownerKey, out string ownerSourceFingerprint))
                {
                    RequestMemoryLibraryOwnerBuild(ownerKey);
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Preparing };
                }
                long ttlValidUntil = MemoryLibraryPolicy.TtlValidUntil(
                    nextBoundary, owner.ownerEarliestFiniteExpiryTickExclusive);
                string importedContentFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                    fingerprint,
                    ownerSourceFingerprint,
                    settingsRevision.ToString(CultureInfo.InvariantCulture),
                    memoryLibraryLanguageDisplayRevision.ToString(CultureInfo.InvariantCulture),
                    ttlDayRevision.ToString(CultureInfo.InvariantCulture),
                    ttlValidUntil.ToString(CultureInfo.InvariantCulture));
                if (memoryLibraryListPublications.TryGetValue(
                        fingerprint, out MemoryLibraryPublication importedPublished)
                    && importedPublished.ownerSnapshot != null
                    && importedPublished.listSelection != null
                    && string.Equals(importedPublished.fingerprint,
                        importedContentFingerprint, StringComparison.Ordinal)
                    && !LibraryPublicationExpiredOrSuperseded(importedPublished))
                {
                    CancelMemoryLibraryListBuild(ownerKey);
                    MemoryLibraryListResult publishedResult =
                        MemoryLibraryIndexPolicy.QueryListSelection(
                            importedPublished.ownerSnapshot,
                            query,
                            importedPublished.listSelection,
                            memoryLibraryDirectoryRevision,
                            importedPublished.revision,
                            importedPublished.committedSettingsRevision,
                            importedPublished.languageDisplayRevision,
                            importedPublished.ttlDayRevision,
                            importedPublished.nextDayBoundary,
                            BuildMemoryLibraryLimits());
                    if (publishedResult.status == MemoryLibraryStatuses.Ready)
                        publishedResult.directoryRevision = importedPublished.directoryRevision;
                    return publishedResult;
                }
                if (memoryLibraryClock.LastIssuedRevision == long.MaxValue)
                    return InvalidList("library_revision_saturated");
                if (!RequestMemoryLibraryListBuild(
                        ownerKey, ownerSourceFingerprint, fingerprint,
                        importedContentFingerprint, owner, query, nextBoundary,
                        ttlValidUntil, settingsRevision, ttlDayRevision))
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Invalid };
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Preparing };
            }
            MemoryLibraryListResult candidate = MemoryLibraryIndexPolicy.QueryList(
                owner,
                query,
                memoryLibraryDirectoryRevision,
                1,
                settingsRevision,
                memoryLibraryLanguageDisplayRevision,
                ttlDayRevision,
                nextBoundary,
                BuildMemoryLibraryLimits());
            if (candidate.status != MemoryLibraryStatuses.Ready) return candidate;
            string contentFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                fingerprint,
                OwnerSnapshotFingerprint(owner),
                settingsRevision.ToString(CultureInfo.InvariantCulture),
                memoryLibraryLanguageDisplayRevision.ToString(CultureInfo.InvariantCulture),
                ttlDayRevision.ToString(CultureInfo.InvariantCulture),
                candidate.ttlValidUntilTickExclusive.ToString(CultureInfo.InvariantCulture));
            MemoryLibraryPublication publication = ResolveCompleteLibraryPublication(
                memoryLibraryListPublications,
                fingerprint,
                contentFingerprint,
                ownerKey,
                owner,
                0,
                nextBoundary,
                candidate.ttlValidUntilTickExclusive,
                string.Empty);
            if (publication == null) return InvalidList("library_revision_saturated");
            candidate.listSnapshotRevision = publication.revision;
            candidate.directoryRevision = publication.directoryRevision;
            return candidate;
        }

        /// <summary>Returns an independently paged selected-root detail stream.</summary>
        internal MemoryThreadDetailResult QueryMemoryThreadDetail(MemoryThreadDetailQuery query)
        {
            if (query?.rootHandle == null)
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Invalid };
            string ownerKey = OwnerIndexKey(new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.Active,
                query.rootHandle.ownerPawnId,
                query.rootHandle.epochToken));
            if (!memoryLibraryOwnerSources.ContainsKey(ownerKey))
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Missing };
            if (!memoryLibraryOwners.TryGetValue(ownerKey, out MemoryLibraryOwnerIndexSnapshot owner))
            {
                if (query.expectedDetailSnapshotRevision > 0)
                    return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Stale };
                RequestMemoryLibraryOwnerBuild(ownerKey);
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Preparing };
            }
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            if (MemoryLibraryPolicy.OwnerSnapshotReachedExpiry(
                    now, owner.ownerEarliestFiniteExpiryTickExclusive))
            {
                ExpireMemoryLibraryOwnerSnapshot(ownerKey);
                if (query.expectedDetailSnapshotRevision > 0)
                    return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Stale };
                RequestMemoryLibraryOwnerBuild(ownerKey);
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Preparing };
            }
            TouchMemoryLibraryOwner(ownerKey);
            string fingerprint = DetailQueryFingerprint(query);
            if (query.expectedDetailSnapshotRevision > 0)
            {
                if (!memoryLibraryDetailPublications.TryGetValue(
                        fingerprint, out MemoryLibraryPublication pinned)
                    || pinned.revision != query.expectedDetailSnapshotRevision
                    || pinned.ownerSnapshot == null
                    || LibraryPublicationExpiredOrSuperseded(pinned))
                    return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Stale };
                return MemoryLibraryIndexPolicy.QueryThreadDetail(
                    pinned.ownerSnapshot, query, pinned.revision,
                    pinned.nextDayBoundary, BuildMemoryLibraryLimits());
            }
            long nextBoundary = NextMemoryLibraryDayBoundary();
            MemoryThreadDetailResult candidate = MemoryLibraryIndexPolicy.QueryThreadDetail(
                owner, query, 1, nextBoundary, BuildMemoryLibraryLimits());
            if (candidate.status != MemoryLibraryStatuses.Ready) return candidate;
            string contentFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                fingerprint,
                OwnerSnapshotFingerprint(owner),
                MemoryEffectivePolicyProvider.PublicationRevision
                    .ToString(CultureInfo.InvariantCulture),
                memoryLibraryLanguageDisplayRevision.ToString(CultureInfo.InvariantCulture),
                (Math.Max(0, Find.TickManager?.TicksGame ?? 0) / 60000L)
                    .ToString(CultureInfo.InvariantCulture),
                candidate.ttlValidUntilTickExclusive.ToString(CultureInfo.InvariantCulture));
            MemoryLibraryPublication publication = ResolveCompleteLibraryPublication(
                memoryLibraryDetailPublications,
                fingerprint,
                contentFingerprint,
                ownerKey,
                owner,
                0,
                nextBoundary,
                candidate.ttlValidUntilTickExclusive,
                string.Empty);
            if (publication == null)
                return new MemoryThreadDetailResult
                {
                    status = MemoryLibraryStatuses.Invalid,
                    reasonToken = "library_revision_saturated"
                };
            candidate.detailSnapshotRevision = publication.revision;
            return candidate;
        }

        /// <summary>Returns one bounded active-block detail after exact placement/revision checks.</summary>
        internal MemoryBlockDetailResult QueryMemoryBlockDetail(MemoryBlockDetailQuery query)
        {
            MemoryBlockDetailResult result = new MemoryBlockDetailResult();
            if (query?.recordHandle == null || string.IsNullOrWhiteSpace(query.recordHandle.ownerPawnId)
                || string.IsNullOrWhiteSpace(query.recordHandle.epochToken)
                || string.IsNullOrWhiteSpace(query.recordHandle.recordId)
                || query.targetStructuralRevision <= 0
                || (query.projectionToken != "full" && query.projectionToken != "filtered"))
                return result;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(query.recordHandle.ownerPawnId);
            if (owner == null || owner.autobiographicalEpochToken != query.recordHandle.epochToken)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            SavedMemoryThreadRoot root = null;
            SavedMemoryBlock block = null;
            long structural;
            long status;
            if (string.Equals(query.placementToken, "standalone", StringComparison.Ordinal))
            {
                if (query.rootHandle != null) return result;
                block = FindSavedBlock(owner.standaloneBlocks, query.recordHandle.recordId);
                structural = MemoryLibraryPolicy.TargetStructuralRevision(
                    false, owner.structuralRevision, 0);
                status = owner.statusRevision;
            }
            else
            {
                if (!RootAndRecordHandlesMatch(query.rootHandle, query.recordHandle)) return result;
                root = FindSavedRoot(owner.threadRoots, query.rootHandle.rootId);
                if (root == null)
                {
                    result.status = MemoryLibraryStatuses.Missing;
                    return result;
                }
                block = FindSavedBlock(root.visibleBlocks, query.recordHandle.recordId)
                    ?? (root.rollingSummaryBlock?.recordId == query.recordHandle.recordId
                        ? root.rollingSummaryBlock : null);
                structural = MemoryLibraryPolicy.TargetStructuralRevision(
                    true, owner.structuralRevision, root.structuralRevision);
                status = root.statusRevision;
            }
            if (block == null)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            long snapshotNow = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            if (!ShouldProjectSavedBlock(block, policy, snapshotNow))
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (structural != query.targetStructuralRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            MemoryBlockRow row = BuildMemoryBlockRow(
                block, root, structural, PawnDiaryRecordName(owner.pawnId), limits);
            if (query.projectionToken == "filtered")
            {
                string normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                    query.search, limits.searchScalars, limits.searchUtf16Units);
                if (!MemoryLibraryPolicy.TryProjectRow(
                    row, query.filters, normalizedSearch, false, limits,
                    out MemoryBlockRow projected))
                    return result;
                row = projected;
            }
            result.status = MemoryLibraryStatuses.Ready;
            result.row = row;
            result.detail = BuildMemoryBlockDetail(block, limits, row);
            result.targetStructuralRevision = structural;
            result.targetStatusRevision = status;
            result.ttlValidUntilTickExclusive = MemoryLibraryPolicy.TtlValidUntil(
                NextMemoryLibraryDayBoundary(), row.projectedNextExpiryTick);
            return result;
        }

        /// <summary>Pages the complete bounded preserved Imported wording without copying it into rows.</summary>
        internal MemoryImportedDetailResult QueryMemoryImportedDetail(
            MemoryImportedDetailQuery query)
        {
            MemoryImportedDetailResult result = new MemoryImportedDetailResult();
            if (!MemoryLibraryPolicy.ValidArchiveHandle(query?.archiveHandle)) return result;
            SavedImportedMemoryRow row;
            long structural;
            string resolveStatus = ResolveImportedRow(
                query.archiveHandle, out row, out structural);
            if (resolveStatus != MemoryLibraryStatuses.Ready)
            {
                result.status = resolveStatus;
                return result;
            }
            if (query.targetStructuralRevision <= 0) return result;
            if (query.targetStructuralRevision != structural)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            MemoryLibraryLimits textLimits = BuildMemoryLibraryLimits();
            int normalizedCount = MemoryLibraryPolicy.NormalizeTextCount(
                query.textCount, textLimits.importedTextChunkUtf16Units);
            if (normalizedCount <= 0) return result;
            string streamFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                ArchiveHandleKey(query.archiveHandle),
                normalizedCount.ToString(CultureInfo.InvariantCulture));
            string contentFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                row.importedWording ?? string.Empty,
                structural.ToString(CultureInfo.InvariantCulture));
            string ownerKey = (query.archiveHandle.archiveScopeToken ?? string.Empty) + "|"
                + (query.archiveHandle.exactOwnerPawnIdOrEmpty ?? string.Empty);
            long boundary = NextMemoryLibraryDayBoundary();
            MemoryLibraryPublication publication = ResolveCompleteLibraryPublication(
                memoryLibraryTextPublications,
                streamFingerprint,
                contentFingerprint,
                ownerKey,
                null,
                query.expectedArchiveTextSnapshotRevision,
                boundary,
                boundary,
                row.importedWording ?? string.Empty);
            if (publication == null
                || (query.expectedArchiveTextSnapshotRevision > 0
                    && (!string.Equals(publication.fingerprint, contentFingerprint,
                            StringComparison.Ordinal)
                        || LibraryPublicationExpiredOrSuperseded(publication))))
            {
                result.status = query.expectedArchiveTextSnapshotRevision > 0
                    ? MemoryLibraryStatuses.Stale : MemoryLibraryStatuses.Invalid;
                result.reasonToken = query.expectedArchiveTextSnapshotRevision > 0
                    ? string.Empty : "library_revision_saturated";
                return result;
            }
            string text = publication.textContent;
            MemoryLibraryTextCursorPlan cursor = MemoryLibraryPolicy.NormalizeTextCursor(
                text,
                query.textStart,
                query.textCount,
                textLimits.importedTextChunkUtf16Units,
                query.expectedArchiveTextSnapshotRevision);
            if (!cursor.valid) return result;
            result.status = MemoryLibraryStatuses.Ready;
            result.archiveHandle = query.archiveHandle;
            result.textChunk = text.Substring(cursor.start, cursor.count);
            result.returnedTextStart = cursor.start;
            result.previousTextStart = cursor.previousStart;
            result.nextTextStart = cursor.end;
            result.totalTextLength = text.Length;
            result.hasPrevious = cursor.hasPrevious;
            result.hasMore = cursor.hasMore;
            result.archiveTextSnapshotRevision = publication.revision;
            result.targetStructuralRevision = structural;
            return result;
        }

        /// <summary>Returns one actionless compatibility panel for an exact directory handle/revision.</summary>
        internal MemoryCompatibilityResult QueryMemoryCompatibility(MemoryCompatibilityQuery query)
        {
            MemoryCompatibilityResult result = new MemoryCompatibilityResult();
            if (!MemoryLibraryPolicy.ValidOwnerHandle(query?.compatibilityHandle)
                || (query.compatibilityHandle.scopeToken != MemoryLibraryScopes.LegacyRawExact
                    && query.compatibilityHandle.scopeToken != MemoryLibraryScopes.LegacyRawUnknown
                    && query.compatibilityHandle.scopeToken != MemoryLibraryScopes.InertCurrentExact))
                return result;
            if (query.sourcePayloadRevision <= 0) return result;
            MemoryLibraryOwnerRow row = FindCompatibilityDirectoryRow(query.compatibilityHandle);
            if (row == null)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (query.sourcePayloadRevision != row.compatibilitySourcePayloadRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            string key = OwnerIndexKey(query.compatibilityHandle);
            if (!memoryLibraryCompatibilityPublications.TryGetValue(
                    key, out MemoryCompatibilitySourcePublication publication)
                || publication?.pending == null)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (publication.revision != row.compatibilitySourcePayloadRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            result.status = MemoryLibraryStatuses.Ready;
            result.sourcePayloadRevision = row.compatibilitySourcePayloadRevision;
            MemoryLegacyPendingDto pending = publication.pending;
            result.pending = new MemoryLegacyPendingDto
            {
                handle = new MemoryLibraryOwnerHandle(
                    pending.handle?.scopeToken,
                    pending.handle?.exactOwnerPawnIdOrEmpty,
                    pending.handle?.epochTokenOrEmpty),
                stateToken = pending.stateToken,
                reasonToken = pending.reasonToken,
                rowCount = pending.rowCount,
                logicalByteCount = pending.logicalByteCount,
                countWasClamped = pending.countWasClamped,
                sourcePayloadRevision = pending.sourcePayloadRevision,
                safePreview = pending.safePreview
            };
            return result;
        }

        /// <summary>Queues one detached command for the next safe component-update drain.</summary>
        internal bool TryEnqueueMemoryLibraryCommand(MemoryLibraryCommand command)
        {
            if (!ValidLibraryCommandEnvelope(command)) return false;
            string key = LibraryCommandKey(command.libraryClientToken, command.commandId);
            if (memoryLibraryCommandResults.ContainsKey(key)) return true;
            for (int index = 0; index < memoryLibraryPendingCommands.Count; index++)
            {
                MemoryLibraryCommand pending = memoryLibraryPendingCommands[index];
                if (pending != null && LibraryCommandKey(
                    pending.libraryClientToken, pending.commandId) == key) return true;
            }
            int cap = BuildMemoryLibraryLimits().commandEntries;
            if (memoryLibraryPendingCommands.Count + memoryLibraryCommandResults.Count >= cap)
                return false;
            memoryLibraryPendingCommands.Add(command);
            return true;
        }

        /// <summary>Consumes one terminal result; later replay re-resolves Missing/Stale precedence.</summary>
        internal bool TryTakeMemoryLibraryCommandResult(
            string clientToken,
            long commandId,
            out MemoryLibraryCommandResult result)
        {
            string key = LibraryCommandKey(clientToken, commandId);
            if (!memoryLibraryCommandResults.TryGetValue(key, out result)) return false;
            memoryLibraryCommandResults.Remove(key);
            return true;
        }

        private void DrainMemoryLibraryCommands()
        {
            if (memoryLibraryPendingCommands.Count == 0) return;
            List<MemoryLibraryCommand> pending =
                new List<MemoryLibraryCommand>(memoryLibraryPendingCommands);
            memoryLibraryPendingCommands.Clear();
            for (int index = 0; index < pending.Count; index++)
            {
                MemoryLibraryCommand command = pending[index];
                string key = LibraryCommandKey(command.libraryClientToken, command.commandId);
                if (memoryLibraryCommandResults.ContainsKey(key)) continue;
                try
                {
                    memoryLibraryCommandResults[key] = ApplyMemoryLibraryCommand(command);
                }
                catch (Exception exception)
                {
                    memoryLibraryCommandResults[key] = NewLibraryCommandResult(command);
                    Log.ErrorOnce(
                        "[Pawn Diary] One Memory Library command failed without replay: " + exception,
                        ("PawnDiary.Memory.Library.Command." + key).GetHashCode());
                }
            }
        }

        /// <summary>Prunes one closed UI client's pending intents and unconsumed terminal results.</summary>
        internal void AbandonMemoryLibraryClient(string clientToken)
        {
            if (string.IsNullOrWhiteSpace(clientToken)) return;
            memoryLibraryActiveClients.Remove(clientToken);
            memoryLibraryPendingCommands.RemoveAll(command => command != null
                && string.Equals(command.libraryClientToken, clientToken, StringComparison.Ordinal));
            List<string> resultKeys = new List<string>();
            foreach (KeyValuePair<string, MemoryLibraryCommandResult> pair
                in memoryLibraryCommandResults)
                if (string.Equals(pair.Value?.libraryClientToken,
                    clientToken, StringComparison.Ordinal)) resultKeys.Add(pair.Key);
            for (int index = 0; index < resultKeys.Count; index++)
                memoryLibraryCommandResults.Remove(resultKeys[index]);
            if (memoryLibraryActiveClients.Count == 0)
            {
                // Keep completed detached caches warm, but release every producer that no window can
                // consume. A later client revalidates the source tuple and requests fresh work.
                memoryLibraryDirectoryBuildJob = null;
                memoryLibraryDirectoryBuildRequested = false;
                memoryLibraryPendingOwnerBuildKey = string.Empty;
                memoryLibraryListBuildJobs.Clear();
                memoryLibraryListBuildOrder.Clear();
            }
        }

        private MemoryLibraryCommandResult ApplyMemoryLibraryCommand(MemoryLibraryCommand command)
        {
            MemoryLibraryCommandResult result = NewLibraryCommandResult(command);
            if (!ValidLibraryCommandEnvelope(command)) return result;
            bool imported = command.archiveHandle != null;
            if (imported) return ForgetImportedMemory(command, result);
            return MutateActiveMemory(command, result);
        }

        private MemoryLibraryCommandResult MutateActiveMemory(
            MemoryLibraryCommand command,
            MemoryLibraryCommandResult result)
        {
            MemoryRecordHandle handle = command.recordHandle;
            if (handle == null || string.IsNullOrWhiteSpace(handle.ownerPawnId)
                || string.IsNullOrWhiteSpace(handle.epochToken)
                || string.IsNullOrWhiteSpace(handle.recordId)) return result;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(handle.ownerPawnId);
            if (owner == null || owner.autobiographicalEpochToken != handle.epochToken)
            {
                result.status = PlanMemoryLibraryCommandStatus(
                    command, false, 0, false, null);
                return result;
            }

            bool standalone = string.Equals(command.placementToken, "standalone",
                StringComparison.Ordinal);
            List<SavedMemoryBlock> detachedStandalone = CloneSavedBlocks(owner.standaloneBlocks);
            List<SavedMemoryThreadRoot> detachedRoots = CloneSavedRoots(owner.threadRoots);
            SavedMemoryThreadRoot root = null;
            SavedMemoryBlock block;
            long targetRevision;
            if (standalone)
            {
                if (command.rootHandle != null) return result;
                block = FindSavedBlock(detachedStandalone, handle.recordId);
                targetRevision = MemoryLibraryPolicy.TargetStructuralRevision(
                    false, owner.structuralRevision, 0);
            }
            else
            {
                if (!RootAndRecordHandlesMatch(command.rootHandle, handle)) return result;
                root = FindSavedRoot(detachedRoots, command.rootHandle.rootId);
                if (root == null)
                {
                    result.status = PlanMemoryLibraryCommandStatus(
                        command, false, 0, false, null);
                    return result;
                }
                block = FindSavedBlock(root.visibleBlocks, handle.recordId)
                    ?? (root.rollingSummaryBlock?.recordId == handle.recordId
                        ? root.rollingSummaryBlock : null);
                targetRevision = MemoryLibraryPolicy.TargetStructuralRevision(
                    true, owner.structuralRevision, root.structuralRevision);
            }
            if (block == null)
            {
                result.status = PlanMemoryLibraryCommandStatus(
                    command, false, targetRevision, false, null);
                return result;
            }
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            long nowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            if (!ShouldProjectSavedBlock(block, policy, nowTick))
            {
                // Commands are queued from detached UI state and drain later on the main thread.
                // Recheck exact TTL at the mutation boundary so a row that expires between those
                // moments cannot be edited into a permanent player-protected resurrection.
                result.status = PlanMemoryLibraryCommandStatus(
                    command, false, targetRevision, false, null);
                return result;
            }
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            MemoryBlockRow dto = BuildMemoryBlockRow(
                block, root, targetRevision, PawnDiaryRecordName(owner.pawnId), limits);
            string plannedStatus = PlanMemoryLibraryCommandStatus(
                command, true, targetRevision, false, dto, limits);
            if (plannedStatus != MemoryLibraryCommandStatuses.Success)
            {
                result.status = plannedStatus;
                return result;
            }
            if (targetRevision == long.MaxValue
                || ((command.actionToken == MemoryLibraryActions.SaveWording
                        || command.actionToken == MemoryLibraryActions.UseOriginalWording)
                    && block.formatRevision == long.MaxValue))
            {
                result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                return result;
            }

            if (command.actionToken == MemoryLibraryActions.SetSuppressed)
            {
                if (block.suppressed == command.desiredSuppressed)
                {
                    result.status = MemoryLibraryCommandStatuses.Success;
                    result.resultingStructuralRevision = targetRevision;
                    return result;
                }
                block.suppressed = command.desiredSuppressed;
            }
            else if (command.actionToken == MemoryLibraryActions.SaveWording)
            {
                block.playerWording = command.wordingDraft;
                block.playerEdited = true;
                block.formatRevision++;
                MemoryStoreMutationOutcome capacity = ValidateDetachedCapacity(
                    owner, detachedStandalone, detachedRoots, false);
                if (capacity != MemoryStoreMutationOutcome.Admitted)
                {
                    result.status = MemoryLibraryCommandStatuses.CapFull;
                    return result;
                }
            }
            else if (command.actionToken == MemoryLibraryActions.UseOriginalWording)
            {
                block.playerWording = string.Empty;
                block.playerEdited = false;
                block.formatRevision++;
            }
            else if (command.actionToken == MemoryLibraryActions.ForgetPermanent)
            {
                if (standalone) detachedStandalone.Remove(block);
                else ForgetRootBlock(detachedRoots, root, block, owner);
            }

            long nextRevision;
            if (standalone)
            {
                nextRevision = owner.structuralRevision + 1;
                owner.structuralRevision = nextRevision;
            }
            else
            {
                root.structuralRevision++;
                nextRevision = root.structuralRevision;
                if (!detachedRoots.Contains(root))
                {
                    if (owner.structuralRevision == long.MaxValue)
                    {
                        result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                        return result;
                    }
                    owner.structuralRevision++;
                }
            }
            owner.standaloneBlocks = detachedStandalone;
            owner.threadRoots = detachedRoots;
            MarkMemoryLibraryMutationCommitted();
            result.status = MemoryLibraryCommandStatuses.Success;
            result.resultingStructuralRevision = nextRevision;
            return result;
        }

        private MemoryLibraryCommandResult ForgetImportedMemory(
            MemoryLibraryCommand command,
            MemoryLibraryCommandResult result)
        {
            SavedImportedMemoryRow row;
            long structural;
            string resolveStatus = ResolveImportedRow(
                command.archiveHandle, out row, out structural);
            if (resolveStatus != MemoryLibraryStatuses.Ready)
            {
                result.status = resolveStatus == MemoryLibraryStatuses.Invalid
                    ? MemoryLibraryCommandStatuses.Invalid
                    : PlanMemoryLibraryCommandStatus(command, false, 0, true, null);
                return result;
            }
            string plannedStatus = PlanMemoryLibraryCommandStatus(
                command, true, structural, true, null);
            if (plannedStatus != MemoryLibraryCommandStatuses.Success)
            {
                result.status = plannedStatus;
                return result;
            }
            if (structural == long.MaxValue)
            {
                result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                return result;
            }
            if (command.archiveHandle.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported)
            {
                List<SavedImportedMemoryRow> retained = new List<SavedImportedMemoryRow>();
                for (int index = 0; unresolvedOwnerArchiveRows != null
                    && index < unresolvedOwnerArchiveRows.Count; index++)
                    if (!ReferenceEquals(unresolvedOwnerArchiveRows[index], row))
                        retained.Add(unresolvedOwnerArchiveRows[index]);
                unresolvedOwnerArchiveRows = retained;
                unresolvedArchiveStructuralRevision++;
                result.resultingStructuralRevision = unresolvedArchiveStructuralRevision;
            }
            else
            {
                PawnKnowledgeState owner = FindCurrentMemoryEnvelope(
                    command.archiveHandle.exactOwnerPawnIdOrEmpty);
                if (owner == null)
                {
                    result.status = MemoryLibraryCommandStatuses.Missing;
                    return result;
                }
                List<SavedImportedMemoryRow> retained = new List<SavedImportedMemoryRow>();
                for (int index = 0; owner.importedArchiveRows != null
                    && index < owner.importedArchiveRows.Count; index++)
                    if (!ReferenceEquals(owner.importedArchiveRows[index], row))
                        retained.Add(owner.importedArchiveRows[index]);
                owner.importedArchiveRows = retained;
                owner.structuralRevision++;
                result.resultingStructuralRevision = owner.structuralRevision;
            }
            MarkMemoryLibraryMutationCommitted();
            result.status = MemoryLibraryCommandStatuses.Success;
            return result;
        }

        private string PlanMemoryLibraryCommandStatus(
            MemoryLibraryCommand command,
            bool exists,
            long structuralRevision,
            bool imported,
            MemoryBlockRow activeRow,
            MemoryLibraryLimits limits = null)
        {
            MemoryLibraryLimits cap = limits ?? BuildMemoryLibraryLimits();
            return MemoryLibraryPolicy.PlanCommandStatus(
                command,
                new MemoryLibraryCommandTargetState
                {
                    commandShapeValid = ValidLibraryCommandEnvelope(command),
                    exists = exists,
                    currentStructuralRevision = structuralRevision,
                    imported = imported,
                    devAuthorized = Prefs.DevMode,
                    activeRow = activeRow
                },
                cap.blockTextUtf16Units);
        }

        private void MarkMemoryLibraryMutationCommitted()
        {
            memoryLibraryDirectoryFingerprint = string.Empty;
            memoryLibraryDirectoryBuildRequested = memoryLibraryActiveClients.Count > 0;
            // Direct repository readers are valid without a registered Window client. Revision zero
            // makes their next query request a rebuild instead of returning Ready over this cleared
            // directory forever.
            memoryLibraryDirectoryRevision = 0;
            memoryLibraryAdditionalLegacyRawOwners = 0;
            memoryLibraryAdditionalZeroOwners = 0;
            memoryLibraryOwners.Clear();
            memoryLibraryOwnerCacheFingerprints.Clear();
            memoryLibraryOwnerLru.Clear();
            memoryLibraryListPublications.Clear();
            memoryLibraryDetailPublications.Clear();
            memoryLibraryTextPublications.Clear();
            memoryM4IndexesDirty = true;
            memoryMaintenanceDirty = true;
            try
            {
                RebuildMemorySizeIndexes();
            }
            catch (Exception exception)
            {
                // Saved mutation is already complete. Keep every derivative dirty for the next bounded
                // rebuild and never turn a committed command into a missing terminal result.
                memoryM4IndexesDirty = true;
                memoryMaintenanceDirty = true;
                Log.ErrorOnce(
                    "[Pawn Diary] Memory Library indexes will rebuild after a committed mutation: "
                        + exception,
                    "PawnDiary.Memory.Library.IndexRebuild".GetHashCode());
            }
            DiaryStateVersion.Bump();
        }

        /// <summary>
        /// Invalidates detached Library snapshots after inclusion/cooldown/exposure changes. These
        /// mutations deliberately leave structural command fences intact, but every status overlay
        /// and its source fingerprint must be rebuilt before the next published read.
        /// </summary>
        private void MarkMemoryLibraryStatusProjectionDirty()
        {
            MarkMemoryLibrarySavedProjectionDirty();
        }

        /// <summary>
        /// Invalidates every detached saved-memory projection without scheduling another maintenance
        /// cycle. Reducer/TTL commits use this path after their own bounded work has completed.
        /// </summary>
        private void MarkMemoryLibrarySavedProjectionDirty()
        {
            memoryLibraryDirectoryFingerprint = string.Empty;
            memoryLibraryDirectoryBuildRequested = memoryLibraryActiveClients.Count > 0;
            // Direct repository readers are valid without a registered Window client. Revision zero
            // makes their next query request a rebuild instead of returning Ready over this cleared
            // directory forever.
            memoryLibraryDirectoryRevision = 0;
            memoryLibraryAdditionalLegacyRawOwners = 0;
            memoryLibraryAdditionalZeroOwners = 0;
            memoryLibraryOwners.Clear();
            memoryLibraryOwnerSources.Clear();
            memoryLibraryOwnerCacheFingerprints.Clear();
            memoryLibraryOwnerLru.Clear();
            memoryLibraryDirectory.Clear();
            memoryLibraryListPublications.Clear();
            memoryLibraryDetailPublications.Clear();
            memoryLibraryTextPublications.Clear();
            memoryLibraryListBuildJobs.Clear();
            memoryLibraryListBuildOrder.Clear();
            // Keep bounded compatibility publications privately until the directory rebuild. Their
            // prior per-owner revisions are needed to fence stale compatibility payloads; the cleared
            // directory makes them unreachable meanwhile, and the rebuild removes owners no longer seen.
            memoryLibraryDirectoryBuildJob = null;
            memoryLibraryPendingOwnerBuildKey = string.Empty;
            DiaryStateVersion.Bump();
        }

        /// <summary>
        /// Invalidates only the saved culture/directory display projection. Owner memory snapshots and
        /// their list/detail publications remain valid because culture is not memory/search identity.
        /// </summary>
        private void MarkMemoryLibraryCultureProjectionDirty()
        {
            memoryLibraryDirectoryBuildRequested = memoryLibraryActiveClients.Count > 0;
            // The global detached-state fence aborts an in-flight directory header build. Owner-cache
            // fingerprints deliberately exclude it, so byte-equivalent memory publications survive.
            DiaryStateVersion.Bump();
        }

        private bool MemoryLibrarySourceTupleChanged()
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return memoryLibraryObservedDiaryStateVersion != DiaryStateVersion.Current
                || memoryLibraryObservedObservationPublicationRevision
                    != memoryObservationPublicationRevision
                || memoryLibraryObservedSettingsRevision
                    != MemoryEffectivePolicyProvider.PublicationRevision
                || memoryLibraryObservedTtlDayRevision != now / 60000L
                || !ReferenceEquals(memoryLibraryObservedLanguage, LanguageDatabase.activeLanguage)
                || memoryLibraryObservedDiaryCount != (diaries?.Count ?? 0)
                || memoryLibraryObservedUnresolvedCount
                    != (unresolvedOwnerArchiveRows?.Count ?? 0)
                || memoryLibraryObservedRawUnresolvedCount
                    != (rawUnresolvedOwnerArchiveInput?.Count ?? 0);
        }

        private MemoryLibraryDirectoryBuildJob StartMemoryLibraryDirectoryBuild()
        {
            MemoryLibraryDirectoryBuildJob job = new MemoryLibraryDirectoryBuildJob
            {
                diaryStateVersion = DiaryStateVersion.Current,
                observationPublicationRevision = memoryObservationPublicationRevision,
                settingsRevision = MemoryEffectivePolicyProvider.PublicationRevision,
                ttlDayRevision = Math.Max(0, Find.TickManager?.TicksGame ?? 0) / 60000L,
                language = LanguageDatabase.activeLanguage,
                diaryCount = diaries?.Count ?? 0,
                unresolvedCount = unresolvedOwnerArchiveRows?.Count ?? 0,
                rawUnresolvedCount = rawUnresolvedOwnerArchiveInput?.Count ?? 0
            };
            Dictionary<string, PawnDiaryRecord> firstDiary =
                new Dictionary<string, PawnDiaryRecord>(StringComparer.Ordinal);
            List<PawnDiaryRecord> orderedDiaries = new List<PawnDiaryRecord>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary != null && !string.IsNullOrWhiteSpace(diary.pawnId)
                    && !firstDiary.ContainsKey(diary.pawnId))
                {
                    firstDiary.Add(diary.pawnId, diary);
                    orderedDiaries.Add(diary);
                }
            }
            Dictionary<string, Pawn> active = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            foreach (Pawn pawn in PawnsFinder
                .AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn == null) continue;
                string id = pawn.GetUniqueLoadID();
                if (!string.IsNullOrWhiteSpace(id) && !active.ContainsKey(id)) active.Add(id, pawn);
            }
            for (int index = 0; index < orderedDiaries.Count; index++)
            {
                PawnDiaryRecord diary = orderedDiaries[index];
                active.TryGetValue(diary.pawnId, out Pawn live);
                PawnKnowledgeState state = diary.knowledgeState;
                job.work.Add(new MemoryLibraryOwnerSource
                {
                    kind = state == null ? "zero" : state.IsCurrentSchema() ? "current" : "legacy",
                    diary = diary,
                    state = state,
                    displayName = live?.LabelShortCap ?? diary.pawnName ?? diary.pawnId,
                    active = live != null
                });
            }
            foreach (KeyValuePair<string, Pawn> pair in active)
            {
                if (firstDiary.ContainsKey(pair.Key)) continue;
                job.work.Add(new MemoryLibraryOwnerSource
                {
                    kind = "zero",
                    displayName = pair.Value.LabelShortCap,
                    active = true,
                    diary = new PawnDiaryRecord { pawnId = pair.Key, pawnName = pair.Value.LabelShortCap }
                });
            }
            if (job.unresolvedCount > 0 || job.rawUnresolvedCount > 0)
                job.work.Add(new MemoryLibraryOwnerSource { kind = "unknown" });
            return job;
        }

        private void AdvanceMemoryLibraryDirectoryBuild()
        {
            MemoryLibraryDirectoryBuildJob job = memoryLibraryDirectoryBuildJob;
            if (job == null) return;
            if (!MemoryLibraryBuildStillCurrent(job))
            {
                memoryLibraryDirectoryBuildJob = null;
                memoryLibraryDirectoryBuildRequested = true;
                return;
            }
            int workCap = Math.Max(1, (int)ReadCapacityLong(
                "sliceWorkItems", 60, MemoryLibraryLimitCeilings.SliceWorkItems));
            long microseconds = Math.Max(1, ReadCapacityLong(
                "sliceTargetMicroseconds", 750,
                MemoryLibraryLimitCeilings.SliceTargetMicroseconds));
            Stopwatch timer = Stopwatch.StartNew();
            int completed = 0;
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            while (job.cursor < job.work.Count && completed < workCap)
            {
                MemoryLibraryOwnerSource source = job.work[job.cursor++];
                MemoryLibraryOwnerIndexSnapshot header = BuildMemoryLibraryDirectoryHeader(
                    source, limits);
                source.headerSnapshot = header;
                if (header?.ownerRow != null)
                {
                    bool hasData = header.ownerRow.threadCount > 0
                        || header.ownerRow.standaloneCount > 0
                        || header.ownerRow.importedCount > 0;
                    bool compatibilityOnly = !hasData
                        && header.ownerRow.compatibilityHandle != null;
                    bool inactiveCurrentWithSavedCulture = source.kind == "current"
                        && !source.active
                        && (!string.IsNullOrWhiteSpace(source.state?.originCultureDefName)
                            || !string.IsNullOrWhiteSpace(source.state?.adoptedCultureDefName));
                    // Unknown is always in the guaranteed first tier, even when it currently carries
                    // only unresolved raw status. Saved culture keeps inactive current/archive owners
                    // discoverable; exact-owner compatibility-only rows otherwise use tier two.
                    int tier = MemoryLibraryPolicy.DirectoryTier(
                        source.kind == "unknown",
                        hasData,
                        inactiveCurrentWithSavedCulture,
                        compatibilityOnly,
                        source.active);
                    if (tier == MemoryLibraryDirectoryTiers.Data) job.data.Add(header);
                    else if (tier == MemoryLibraryDirectoryTiers.CompatibilityRaw) job.raw.Add(header);
                    else if (tier == MemoryLibraryDirectoryTiers.ActiveZero) job.zero.Add(header);
                    source.ownerKey = OwnerIndexKey(header.ownerRow.primaryHandle);
                    source.sourceFingerprint = MemoryLibraryOwnerSourceFingerprint(source, job);
                    if (!string.IsNullOrEmpty(source.ownerKey))
                        job.sources[source.ownerKey] = source;
                }
                completed++;
                if (timer.ElapsedTicks * 1000000L / Stopwatch.Frequency >= microseconds) break;
            }
            if (job.cursor < job.work.Count) return;
            if (!MemoryLibraryBuildStillCurrent(job))
            {
                memoryLibraryDirectoryBuildJob = null;
                memoryLibraryDirectoryBuildRequested = true;
                return;
            }
            PublishMemoryLibraryDirectory(job);
        }

        private bool MemoryLibraryBuildStillCurrent(MemoryLibraryDirectoryBuildJob job)
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return job != null
                && MemoryLibraryObservationFenceSatisfied
                && job.diaryStateVersion == DiaryStateVersion.Current
                && job.observationPublicationRevision
                    == memoryObservationPublicationRevision
                && job.settingsRevision == MemoryEffectivePolicyProvider.PublicationRevision
                && job.ttlDayRevision == now / 60000L
                && ReferenceEquals(job.language, LanguageDatabase.activeLanguage)
                && job.diaryCount == (diaries?.Count ?? 0)
                && job.unresolvedCount == (unresolvedOwnerArchiveRows?.Count ?? 0)
                && job.rawUnresolvedCount == (rawUnresolvedOwnerArchiveInput?.Count ?? 0);
        }

        private void PublishMemoryLibraryDirectory(MemoryLibraryDirectoryBuildJob job)
        {
            job.data.Sort(CompareOwnerSnapshots);
            job.zero.Sort(CompareOwnerSnapshots);
            int cap = Math.Max(1, (int)ReadCapacityLong(
                "libraryOwnerEntries", 2048, MemoryLibraryLimitCeilings.OwnerEntries));
            MemoryLibraryDirectoryCapPlan capPlan = MemoryLibraryPolicy.PlanDirectoryCap(
                job.data.Count, job.raw.Count, job.zero.Count, cap);
            List<MemoryLibraryOwnerIndexSnapshot> indexed =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            for (int index = 0; index < capPlan.includedData; index++)
                indexed.Add(job.data[index]);
            // Raw rows remain in source order: earliest raw source index, then the already-deduplicated
            // exact owner. Zero-memory rows are sorted by their exact owner handle above.
            for (int index = 0; index < capPlan.includedRaw; index++)
                indexed.Add(job.raw[index]);
            for (int index = 0; index < capPlan.includedZero; index++)
                indexed.Add(job.zero[index]);
            memoryLibraryAdditionalLegacyRawOwners = capPlan.omittedRaw;
            memoryLibraryAdditionalZeroOwners = capPlan.omittedZero;
            ApplyMemoryCompatibilityPublications(job, indexed);
            string fingerprint = DirectoryFingerprint(indexed);
            bool changed = memoryLibraryDirectoryRevision <= 0
                || !string.Equals(fingerprint, memoryLibraryDirectoryFingerprint,
                    StringComparison.Ordinal);
            long revision = memoryLibraryDirectoryRevision;
            if (changed && !memoryLibraryClock.TryAllocate(out revision))
            {
                memoryLibraryDirectoryBuildJob = null;
                memoryLibraryDirectoryBuildRequested = false;
                return;
            }
            memoryLibraryDirectory.Clear();
            Dictionary<string, MemoryLibraryOwnerSource> includedSources =
                new Dictionary<string, MemoryLibraryOwnerSource>(StringComparer.Ordinal);
            for (int index = 0; index < indexed.Count; index++)
            {
                MemoryLibraryOwnerRow row = indexed[index]?.ownerRow;
                if (row == null) continue;
                memoryLibraryDirectory.Add(row);
                string key = OwnerIndexKey(row.primaryHandle);
                if (!string.IsNullOrEmpty(key) && job.sources.TryGetValue(
                    key, out MemoryLibraryOwnerSource source)) includedSources[key] = source;
            }
            memoryLibraryOwnerSources.Clear();
            foreach (KeyValuePair<string, MemoryLibraryOwnerSource> pair in includedSources)
                memoryLibraryOwnerSources[pair.Key] = pair.Value;
            PruneMemoryLibraryOwnerCache();
            memoryLibraryDirectoryRevision = revision;
            memoryLibraryDirectoryFingerprint = fingerprint;
            if (memoryLibraryObservedLanguage != null
                && !ReferenceEquals(memoryLibraryObservedLanguage, job.language)
                && memoryLibraryLanguageDisplayRevision < long.MaxValue)
                memoryLibraryLanguageDisplayRevision++;
            memoryLibraryObservedDiaryStateVersion = job.diaryStateVersion;
            memoryLibraryObservedObservationPublicationRevision =
                job.observationPublicationRevision;
            memoryLibraryObservedSettingsRevision = job.settingsRevision;
            memoryLibraryObservedTtlDayRevision = job.ttlDayRevision;
            memoryLibraryObservedLanguage = job.language;
            memoryLibraryObservedDiaryCount = job.diaryCount;
            memoryLibraryObservedUnresolvedCount = job.unresolvedCount;
            memoryLibraryObservedRawUnresolvedCount = job.rawUnresolvedCount;
            memoryLibraryDirectoryBuildJob = null;
            memoryLibraryDirectoryBuildRequested = false;
        }

        private MemoryLibraryOwnerIndexSnapshot BuildMemoryLibraryDirectoryHeader(
            MemoryLibraryOwnerSource source,
            MemoryLibraryLimits limits)
        {
            if (source == null) return null;
            if (source.kind == "zero")
                return BuildZeroOwner(source.diary?.pawnId, source.displayName);
            if (source.kind == "legacy")
                return BuildLegacyCompatibilityHeader(source, limits);
            if (source.kind == "unknown") return BuildUnknownOwnerHeader(source, limits);
            PawnKnowledgeState state = source.state;
            if (state == null || source.diary == null) return null;
            bool inert = false;
            int roots = 0;
            long latest = 0;
            for (int index = 0; state.threadRoots != null && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (root == null) continue;
                if (HasUnknownNewerReducerRevision(root)) { inert = true; continue; }
                roots++;
                latest = Math.Max(latest, LatestRootTick(root));
            }
            for (int index = 0; state.standaloneBlocks != null
                && index < state.standaloneBlocks.Count; index++)
                latest = Math.Max(latest, state.standaloneBlocks[index]?.originalEventTick ?? 0);
            for (int index = 0; state.importedArchiveRows != null
                && index < state.importedArchiveRows.Count; index++)
                latest = Math.Max(latest, state.importedArchiveRows[index]?.originalEventTick ?? 0);
            string scope = state.archiveOnly
                ? MemoryLibraryScopes.ArchiveOnly : MemoryLibraryScopes.Active;
            MemoryLibraryOwnerHandle primary = new MemoryLibraryOwnerHandle(
                scope, source.diary.pawnId, state.autobiographicalEpochToken ?? string.Empty);
            if (inert) source.compatibilityCandidate = BuildInertCompatibilityCandidate(
                source.diary.pawnId, state, limits);
            return new MemoryLibraryOwnerIndexSnapshot
            {
                directoryCultureSourceFingerprint = SavedCultureSourceFingerprint(state),
                ownerRow = new MemoryLibraryOwnerRow
                {
                    primaryHandle = primary,
                    activeOwnerEpochKey = !state.archiveOnly
                        && !string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)
                        ? new MemoryOwnerEpochKey
                        {
                            ownerPawnId = source.diary.pawnId,
                            epochToken = state.autobiographicalEpochToken
                        } : null,
                    compatibilityHandle = inert ? new MemoryLibraryOwnerHandle(
                        MemoryLibraryScopes.InertCurrentExact, source.diary.pawnId, string.Empty) : null,
                    displayName = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        source.displayName, limits.frozenDisplayLabelUtf16Units),
                    lifecycleToken = source.active ? "active" : state.archiveOnly ? "archive" : "saved",
                    culture = BuildMemoryOwnerCultureDto(state, limits),
                    threadCount = roots,
                    standaloneCount = state.standaloneBlocks?.Count ?? 0,
                    importedCount = state.importedArchiveRows?.Count ?? 0,
                    latestActivityTick = latest,
                    hasArchive = (state.importedArchiveRows?.Count ?? 0) > 0 || inert,
                    legacyRawPending = inert,
                    structuralRevision = state.structuralRevision,
                    statusRevision = state.statusRevision,
                    compatibilitySourcePayloadRevision = 0,
                    normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                        source.displayName, limits.searchScalars, limits.searchUtf16Units)
                }
            };
        }

        private MemoryLibraryOwnerIndexSnapshot BuildUnknownOwnerHeader(
            MemoryLibraryOwnerSource source,
            MemoryLibraryLimits limits)
        {
            bool current = unresolvedOwnerArchiveRows != null
                && unresolvedOwnerArchiveRows.Count > 0;
            MemoryLibraryOwnerHandle compatibility = rawUnresolvedOwnerArchiveInput != null
                && rawUnresolvedOwnerArchiveInput.Count > 0
                ? new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.LegacyRawUnknown, string.Empty, string.Empty)
                : null;
            string display = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                "PawnDiary.Memory.Library.UnknownOwner".Translate().ToString(),
                limits.frozenDisplayLabelUtf16Units);
            MemoryLibraryOwnerIndexSnapshot header = new MemoryLibraryOwnerIndexSnapshot
            {
                ownerRow = new MemoryLibraryOwnerRow
                {
                    primaryHandle = current ? new MemoryLibraryOwnerHandle(
                        MemoryLibraryScopes.UnresolvedImported, string.Empty, string.Empty) : null,
                    compatibilityHandle = compatibility,
                    displayName = display,
                    lifecycleToken = "unknown",
                    importedCount = unresolvedOwnerArchiveRows?.Count ?? 0,
                    hasArchive = true,
                    legacyRawPending = compatibility != null,
                    structuralRevision = unresolvedArchiveStructuralRevision,
                    normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                        display, limits.searchScalars, limits.searchUtf16Units)
                }
            };
            if (compatibility != null)
                source.compatibilityCandidate = BuildUnknownCompatibilityCandidate(limits);
            return header;
        }

        private MemoryLibraryOwnerIndexSnapshot BuildLegacyCompatibilityHeader(
            MemoryLibraryOwnerSource source,
            MemoryLibraryLimits limits)
        {
            if (source?.diary == null || source.state == null) return null;
            MemoryLibraryOwnerHandle handle = new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.LegacyRawExact, source.diary.pawnId, string.Empty);
            source.compatibilityCandidate = BuildLegacyCompatibilityCandidate(
                handle, source.state.records, limits);
            return new MemoryLibraryOwnerIndexSnapshot
            {
                directoryCultureSourceFingerprint = SavedCultureSourceFingerprint(source.state),
                ownerRow = new MemoryLibraryOwnerRow
                {
                    compatibilityHandle = handle,
                    displayName = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        source.displayName, limits.frozenDisplayLabelUtf16Units),
                    lifecycleToken = "migration_pending",
                    culture = BuildMemoryOwnerCultureDto(source.state, limits),
                    legacyRawPending = true,
                    normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                        source.displayName, limits.searchScalars, limits.searchUtf16Units)
                }
            };
        }

        private MemoryCompatibilityCandidate BuildLegacyCompatibilityCandidate(
            MemoryLibraryOwnerHandle handle,
            List<ImportantMemoryRecord> records,
            MemoryLibraryLimits limits)
        {
            long bytes = 0;
            bool clamped = false;
            try
            {
                checked
                {
                    for (int index = 0; records != null && index < records.Count; index++)
                        bytes += SavedLegacyUnresolvedOwnerArchiveInputV1
                            .LegacyRecordLogicalBytes(records[index]);
                }
            }
            catch (OverflowException)
            {
                bytes = long.MaxValue;
                clamped = true;
            }
            List<string> fields = new List<string>();
            for (int index = 0; records != null && index < records.Count; index++)
                AddLegacyRecordFingerprint(fields, records[index]);
            return new MemoryCompatibilityCandidate
            {
                handle = handle,
                stateToken = "preparing",
                reasonToken = MemoryLibraryScopes.LegacyRawExact,
                rowCount = records?.Count ?? 0,
                logicalByteCount = bytes,
                countWasClamped = clamped,
                safePreview = LegacyRecordPreview(
                    records != null && records.Count > 0 ? records[0] : null, limits),
                sourceFingerprint = MemoryLibraryPolicy.StreamFingerprint(fields.ToArray())
            };
        }

        private MemoryCompatibilityCandidate BuildUnknownCompatibilityCandidate(
            MemoryLibraryLimits limits)
        {
            MemoryLibraryOwnerHandle handle = new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.LegacyRawUnknown, string.Empty, string.Empty);
            MemoryLogicalSizeResult size = SizeListValidated(rawUnresolvedOwnerArchiveInput);
            List<string> fields = new List<string>();
            for (int index = 0; rawUnresolvedOwnerArchiveInput != null
                && index < rawUnresolvedOwnerArchiveInput.Count; index++)
            {
                SavedLegacyUnresolvedOwnerArchiveInputV1 raw =
                    rawUnresolvedOwnerArchiveInput[index];
                fields.Add(raw?.savedOwnerIdentityKindToken ?? string.Empty);
                fields.Add(raw?.savedOwnerIdentityValue ?? string.Empty);
                fields.Add((raw?.sourceContainerOrdinal ?? -1)
                    .ToString(CultureInfo.InvariantCulture));
                fields.Add((raw?.sourceRecordOrdinal ?? -1)
                    .ToString(CultureInfo.InvariantCulture));
                AddLegacyRecordFingerprint(fields, raw?.legacyRecord);
            }
            return new MemoryCompatibilityCandidate
            {
                handle = handle,
                stateToken = "preparing",
                reasonToken = MemoryLibraryScopes.LegacyRawUnknown,
                rowCount = rawUnresolvedOwnerArchiveInput?.Count ?? 0,
                logicalByteCount = size.valid ? size.totalBytes : 0,
                countWasClamped = !size.valid,
                safePreview = LegacyRecordPreview(
                    rawUnresolvedOwnerArchiveInput != null
                        && rawUnresolvedOwnerArchiveInput.Count > 0
                        ? rawUnresolvedOwnerArchiveInput[0]?.legacyRecord : null,
                    limits),
                sourceFingerprint = MemoryLibraryPolicy.StreamFingerprint(fields.ToArray())
            };
        }

        private MemoryCompatibilityCandidate BuildInertCompatibilityCandidate(
            string ownerId,
            PawnKnowledgeState state,
            MemoryLibraryLimits limits)
        {
            List<SavedMemoryThreadRoot> inert = new List<SavedMemoryThreadRoot>();
            List<string> fields = new List<string>();
            for (int index = 0; state?.threadRoots != null
                && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (!HasUnknownNewerReducerRevision(root)) continue;
                inert.Add(root);
                fields.Add(root.rootId ?? string.Empty);
                fields.Add(root.lastAppliedReducerRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(root.structuralRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(root.statusRevision.ToString(CultureInfo.InvariantCulture));
            }
            MemoryLogicalSizeResult size = SizeListValidated(inert);
            return new MemoryCompatibilityCandidate
            {
                handle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.InertCurrentExact, ownerId, string.Empty),
                stateToken = "inert",
                reasonToken = MemoryLibraryScopes.InertCurrentExact,
                rowCount = inert.Count,
                logicalByteCount = size.valid ? size.totalBytes : 0,
                countWasClamped = !size.valid,
                safePreview = string.Empty,
                sourceFingerprint = MemoryLibraryPolicy.StreamFingerprint(fields.ToArray())
            };
        }

        private void ApplyMemoryCompatibilityPublications(
            MemoryLibraryDirectoryBuildJob job,
            List<MemoryLibraryOwnerIndexSnapshot> indexed)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = indexed.Count - 1; index >= 0; index--)
            {
                MemoryLibraryOwnerIndexSnapshot snapshot = indexed[index];
                MemoryLibraryOwnerSource source = null;
                for (int workIndex = 0; workIndex < job.work.Count; workIndex++)
                    if (ReferenceEquals(job.work[workIndex].headerSnapshot, snapshot))
                    {
                        source = job.work[workIndex];
                        break;
                    }
                MemoryCompatibilityCandidate candidate = source?.compatibilityCandidate;
                if (candidate == null) continue;
                MemoryCompatibilitySourcePublication publication =
                    PublishMemoryCompatibilityCandidate(candidate);
                if (publication == null)
                {
                    snapshot.ownerRow.compatibilityHandle = null;
                    snapshot.ownerRow.compatibilitySourcePayloadRevision = 0;
                    snapshot.ownerRow.legacyRawPending = false;
                    if (snapshot.ownerRow.primaryHandle == null) indexed.RemoveAt(index);
                    continue;
                }
                string key = OwnerIndexKey(candidate.handle);
                seen.Add(key);
                snapshot.ownerRow.compatibilitySourcePayloadRevision = publication.revision;
            }
            List<string> removed = new List<string>();
            foreach (string key in memoryLibraryCompatibilityPublications.Keys)
                if (!seen.Contains(key)) removed.Add(key);
            for (int index = 0; index < removed.Count; index++)
                memoryLibraryCompatibilityPublications.Remove(removed[index]);
        }

        private MemoryCompatibilitySourcePublication PublishMemoryCompatibilityCandidate(
            MemoryCompatibilityCandidate candidate)
        {
            string key = OwnerIndexKey(candidate?.handle);
            if (string.IsNullOrEmpty(key)) return null;
            string fingerprint = MemoryLibraryPolicy.StreamFingerprint(
                key,
                candidate.sourceFingerprint,
                candidate.stateToken,
                candidate.reasonToken,
                candidate.rowCount.ToString(CultureInfo.InvariantCulture),
                candidate.logicalByteCount.ToString(CultureInfo.InvariantCulture),
                candidate.countWasClamped ? "1" : "0",
                candidate.safePreview);
            long existingRevision = 0;
            bool byteEquivalent = false;
            if (memoryLibraryCompatibilityPublications.TryGetValue(
                key, out MemoryCompatibilitySourcePublication existing))
            {
                existingRevision = existing.revision;
                byteEquivalent = string.Equals(
                    existing.fingerprint, fingerprint, StringComparison.Ordinal);
                if (byteEquivalent) return existing;
            }
            if (!MemoryLibraryPolicy.TryNextCompatibilityRevision(
                    existingRevision, byteEquivalent, out long revision))
            {
                memoryLibraryCompatibilityPublications.Remove(key);
                return null;
            }
            MemoryCompatibilitySourcePublication result =
                new MemoryCompatibilitySourcePublication
                {
                    fingerprint = fingerprint,
                    revision = revision,
                    pending = new MemoryLegacyPendingDto
                    {
                        handle = new MemoryLibraryOwnerHandle(
                            candidate.handle?.scopeToken,
                            candidate.handle?.exactOwnerPawnIdOrEmpty,
                            candidate.handle?.epochTokenOrEmpty),
                        stateToken = candidate.stateToken,
                        reasonToken = candidate.reasonToken,
                        rowCount = candidate.rowCount,
                        logicalByteCount = candidate.logicalByteCount,
                        countWasClamped = candidate.countWasClamped,
                        sourcePayloadRevision = revision,
                        safePreview = candidate.safePreview
                    }
                };
            memoryLibraryCompatibilityPublications[key] = result;
            return result;
        }

        private static string LegacyRecordPreview(
            ImportantMemoryRecord record,
            MemoryLibraryLimits limits)
        {
            string value = !string.IsNullOrWhiteSpace(record?.manualTextOverride)
                ? record.manualTextOverride
                : !string.IsNullOrWhiteSpace(record?.fallbackSummary)
                    ? record.fallbackSummary
                    : record?.dateLabel ?? record?.topicKey ?? string.Empty;
            return MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                value, limits.importedPreviewUtf16Units);
        }

        private static void AddLegacyRecordFingerprint(
            List<string> fields,
            ImportantMemoryRecord record)
        {
            if (fields == null) return;
            fields.Add(record?.recordId ?? string.Empty);
            fields.Add(record?.dedupKey ?? string.Empty);
            fields.Add(record?.sourceEventId ?? string.Empty);
            fields.Add(record?.sourceKind ?? string.Empty);
            fields.Add(record?.recallScope ?? string.Empty);
            fields.Add(record?.eventKind ?? string.Empty);
            fields.Add(record?.topicKey ?? string.Empty);
            fields.Add((record?.tick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(record?.dateLabel ?? string.Empty);
            fields.Add(record?.fallbackSummary ?? string.Empty);
            fields.Add(record?.manualTextOverride ?? string.Empty);
            AddStringListFingerprint(fields, record?.participantIds);
            AddStringListFingerprint(fields, record?.participantNames);
            AddStringListFingerprint(fields, record?.subjectKeys);
            AddStringListFingerprint(fields, record?.factKeys);
            AddStringListFingerprint(fields, record?.factValues);
        }

        private static void AddStringListFingerprint(List<string> fields, List<string> values)
        {
            fields.Add((values?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            for (int index = 0; values != null && index < values.Count; index++)
                fields.Add(values[index] ?? string.Empty);
        }

        private static string MemoryLibraryOwnerSourceFingerprint(
            MemoryLibraryOwnerSource source,
            MemoryLibraryDirectoryBuildJob job)
        {
            PawnKnowledgeState state = source?.state;
            List<string> fields = new List<string>
            {
                source?.kind ?? string.Empty,
                source?.diary?.pawnId ?? string.Empty,
                source?.displayName ?? string.Empty,
                state?.autobiographicalEpochToken ?? string.Empty,
                (state?.structuralRevision ?? 0).ToString(CultureInfo.InvariantCulture),
                (state?.statusRevision ?? 0).ToString(CultureInfo.InvariantCulture),
                (source?.headerSnapshot?.ownerRow?.structuralRevision ?? 0)
                    .ToString(CultureInfo.InvariantCulture),
                (job?.settingsRevision ?? 0).ToString(CultureInfo.InvariantCulture),
                (job?.ttlDayRevision ?? 0).ToString(CultureInfo.InvariantCulture),
                job?.language?.ToString() ?? string.Empty
            };
            for (int index = 0; state?.threadRoots != null
                && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                fields.Add(root?.rootId ?? string.Empty);
                fields.Add((root?.structuralRevision ?? 0).ToString(CultureInfo.InvariantCulture));
                fields.Add((root?.statusRevision ?? 0).ToString(CultureInfo.InvariantCulture));
            }
            return MemoryLibraryPolicy.StreamFingerprint(fields.ToArray());
        }

        private void RequestMemoryLibraryOwnerBuild(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey) || !memoryLibraryOwnerSources.ContainsKey(ownerKey))
                return;
            memoryLibraryPendingOwnerBuildKey = ownerKey;
        }

        private bool RequestMemoryLibraryListBuild(
            string ownerKey,
            string ownerSourceFingerprint,
            string streamFingerprint,
            string contentFingerprint,
            MemoryLibraryOwnerIndexSnapshot owner,
            MemoryLibraryListQuery query,
            long nextDayBoundary,
            long ttlValidUntilTickExclusive,
            long settingsRevision,
            long ttlDayRevision)
        {
            if (memoryLibraryListBuildJobs.TryGetValue(
                    ownerKey, out MemoryLibraryListBuildJob existing)
                && string.Equals(existing.streamFingerprint,
                    streamFingerprint, StringComparison.Ordinal)
                && string.Equals(existing.contentFingerprint,
                    contentFingerprint, StringComparison.Ordinal)) return true;
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            MemoryLibraryListQuery detachedQuery = CopyMemoryLibraryListQuery(query);
            MemoryImportedListSelectionJob selection =
                MemoryLibraryIndexPolicy.BeginImportedListSelection(
                    owner, detachedQuery, limits);
            if (selection == null) return false;
            CancelMemoryLibraryListBuild(ownerKey);
            memoryLibraryListBuildJobs[ownerKey] = new MemoryLibraryListBuildJob
            {
                ownerKey = ownerKey,
                ownerSourceFingerprint = ownerSourceFingerprint,
                streamFingerprint = streamFingerprint,
                contentFingerprint = contentFingerprint,
                ownerSnapshot = owner,
                query = detachedQuery,
                limits = limits,
                selectionJob = selection,
                diaryStateVersion = DiaryStateVersion.Current,
                observationPublicationRevision = memoryObservationPublicationRevision,
                directoryRevision = memoryLibraryDirectoryRevision,
                committedSettingsRevision = settingsRevision,
                languageDisplayRevision = memoryLibraryLanguageDisplayRevision,
                ttlDayRevision = ttlDayRevision,
                nextDayBoundary = nextDayBoundary,
                ttlValidUntilTickExclusive = ttlValidUntilTickExclusive
            };
            memoryLibraryListBuildOrder.AddLast(ownerKey);
            return true;
        }

        private void AdvanceMemoryLibraryListBuild()
        {
            if (memoryLibraryListBuildOrder.First == null) return;
            string ownerKey = memoryLibraryListBuildOrder.First.Value;
            if (!memoryLibraryListBuildJobs.TryGetValue(
                    ownerKey, out MemoryLibraryListBuildJob job))
            {
                memoryLibraryListBuildOrder.RemoveFirst();
                return;
            }
            if (!MemoryLibraryListBuildStillCurrent(job))
            {
                CancelMemoryLibraryListBuild(ownerKey);
                return;
            }
            int workCap = Math.Max(1, (int)ReadCapacityLong(
                "sliceWorkItems", 60, MemoryLibraryLimitCeilings.SliceWorkItems));
            long microseconds = Math.Max(1, ReadCapacityLong(
                "sliceTargetMicroseconds", 750,
                MemoryLibraryLimitCeilings.SliceTargetMicroseconds));
            Stopwatch timer = Stopwatch.StartNew();
            int completed = 0;
            bool done = job.selectionJob.source.Count == 0;
            while (!done && completed < workCap)
            {
                done = MemoryLibraryIndexPolicy.AdvanceImportedListSelection(
                    job.selectionJob, job.limits);
                completed++;
                if (timer.ElapsedTicks * 1000000L / Stopwatch.Frequency >= microseconds) break;
            }
            if (!done)
            {
                memoryLibraryListBuildOrder.RemoveFirst();
                memoryLibraryListBuildOrder.AddLast(ownerKey);
                return;
            }
            MemoryLibraryListSelection selection =
                MemoryLibraryIndexPolicy.CompleteImportedListSelection(job.selectionJob);
            if (selection != null && MemoryLibraryListBuildStillCurrent(job))
            {
                // Validate the complete private result before consuming a global publication revision.
                MemoryLibraryListResult candidate = MemoryLibraryIndexPolicy.QueryListSelection(
                    job.ownerSnapshot,
                    job.query,
                    selection,
                    job.directoryRevision,
                    1,
                    job.committedSettingsRevision,
                    job.languageDisplayRevision,
                    job.ttlDayRevision,
                    job.nextDayBoundary,
                    job.limits);
                if (candidate.status == MemoryLibraryStatuses.Ready)
                {
                    MemoryLibraryPublication publication = ResolveCompleteLibraryPublication(
                        memoryLibraryListPublications,
                        job.streamFingerprint,
                        job.contentFingerprint,
                        job.ownerKey,
                        job.ownerSnapshot,
                        0,
                        job.nextDayBoundary,
                        job.ttlValidUntilTickExclusive,
                        string.Empty);
                    if (publication != null) publication.listSelection = selection;
                }
            }
            CancelMemoryLibraryListBuild(ownerKey);
        }

        private bool MemoryLibraryListBuildStillCurrent(MemoryLibraryListBuildJob job)
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return job != null
                && MemoryLibraryObservationFenceSatisfied
                && job.observationPublicationRevision
                    == memoryObservationPublicationRevision
                && !MemoryLibrarySourceTupleChanged()
                && MemoryLibraryPolicy.LibraryBuildFenceMatches(
                    job.diaryStateVersion,
                    DiaryStateVersion.Current,
                    job.directoryRevision,
                    memoryLibraryDirectoryRevision,
                    job.committedSettingsRevision,
                    MemoryEffectivePolicyProvider.PublicationRevision,
                    job.languageDisplayRevision,
                    memoryLibraryLanguageDisplayRevision,
                    job.ttlDayRevision,
                    now / 60000L)
                && memoryLibraryOwners.TryGetValue(
                    job.ownerKey, out MemoryLibraryOwnerIndexSnapshot owner)
                && ReferenceEquals(owner, job.ownerSnapshot)
                && memoryLibraryOwnerCacheFingerprints.TryGetValue(
                    job.ownerKey, out string sourceFingerprint)
                && string.Equals(sourceFingerprint,
                    job.ownerSourceFingerprint, StringComparison.Ordinal);
        }

        private void CancelMemoryLibraryListBuild(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return;
            memoryLibraryListBuildJobs.Remove(ownerKey);
            memoryLibraryListBuildOrder.Remove(ownerKey);
        }

        private static MemoryLibraryListQuery CopyMemoryLibraryListQuery(
            MemoryLibraryListQuery source)
        {
            if (source == null) return null;
            MemoryLibraryOwnerHandle handle = source.primaryHandle == null ? null
                : new MemoryLibraryOwnerHandle(
                    source.primaryHandle.scopeToken,
                    source.primaryHandle.exactOwnerPawnIdOrEmpty,
                    source.primaryHandle.epochTokenOrEmpty);
            MemoryOwnerEpochKey epoch = source.activeOwnerEpochKey == null ? null
                : new MemoryOwnerEpochKey
                {
                    ownerPawnId = source.activeOwnerEpochKey.ownerPawnId,
                    epochToken = source.activeOwnerEpochKey.epochToken
                };
            MemoryLibraryFilters filters = source.filters == null
                ? new MemoryLibraryFilters()
                : new MemoryLibraryFilters
                {
                    importanceMask = source.filters.importanceMask,
                    categoryMask = source.filters.categoryMask,
                    stateToken = source.filters.stateToken ?? string.Empty
                };
            return new MemoryLibraryListQuery
            {
                primaryHandle = handle,
                activeOwnerEpochKey = epoch,
                viewTag = source.viewTag ?? string.Empty,
                filters = filters,
                search = source.search ?? string.Empty,
                sortToken = source.sortToken ?? string.Empty,
                listStart = source.listStart,
                listCount = source.listCount,
                expectedDirectoryRevision = source.expectedDirectoryRevision,
                expectedListSnapshotRevision = 0
            };
        }

        private void BuildPendingMemoryLibraryOwnerIndex()
        {
            string key = memoryLibraryPendingOwnerBuildKey;
            memoryLibraryPendingOwnerBuildKey = string.Empty;
            if (!memoryLibraryOwnerSources.TryGetValue(
                key, out MemoryLibraryOwnerSource source)) return;
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            MemoryLibraryOwnerIndexSnapshot snapshot = source.kind == "current"
                ? MemoryLibraryIndexPolicy.BuildOwner(BuildOwnerInput(
                    source.diary, source.state, source.displayName, source.active, limits), limits)
                : source.kind == "unknown" ? BuildUnknownOwner(limits)
                : source.kind == "zero" ? BuildZeroOwner(source.diary?.pawnId, source.displayName)
                : null;
            if (snapshot == null) return;
            MemoryLibraryOwnerRow directoryRow = FindPrimaryDirectoryRow(
                snapshot.ownerRow?.primaryHandle);
            if (directoryRow != null)
            {
                snapshot.ownerRow.compatibilityHandle = directoryRow.compatibilityHandle;
                snapshot.ownerRow.compatibilitySourcePayloadRevision =
                    directoryRow.compatibilitySourcePayloadRevision;
                snapshot.ownerRow.legacyRawPending = directoryRow.legacyRawPending;
            }
            memoryLibraryOwners[key] = snapshot;
            memoryLibraryOwnerCacheFingerprints[key] = source.sourceFingerprint;
            TouchMemoryLibraryOwner(key);
            int cap = Math.Max(1, (int)ReadCapacityLong("cachedOwnerStates", 6, 8));
            while (memoryLibraryOwnerLru.Count > cap)
            {
                string evicted = memoryLibraryOwnerLru.First.Value;
                memoryLibraryOwnerLru.RemoveFirst();
                memoryLibraryOwners.Remove(evicted);
                memoryLibraryOwnerCacheFingerprints.Remove(evicted);
                InvalidateMemoryLibraryPublicationsForOwner(evicted);
            }
        }

        private void TouchMemoryLibraryOwner(string ownerKey)
        {
            LinkedListNode<string> node = memoryLibraryOwnerLru.Find(ownerKey);
            if (node != null) memoryLibraryOwnerLru.Remove(node);
            memoryLibraryOwnerLru.AddLast(ownerKey);
        }

        private void PruneMemoryLibraryOwnerCache()
        {
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, MemoryLibraryOwnerIndexSnapshot> pair in memoryLibraryOwners)
            {
                if (!memoryLibraryOwnerSources.TryGetValue(
                        pair.Key, out MemoryLibraryOwnerSource source)
                    || !memoryLibraryOwnerCacheFingerprints.TryGetValue(
                        pair.Key, out string fingerprint)
                    || !string.Equals(fingerprint, source.sourceFingerprint,
                        StringComparison.Ordinal)) remove.Add(pair.Key);
            }
            for (int index = 0; index < remove.Count; index++)
            {
                memoryLibraryOwners.Remove(remove[index]);
                memoryLibraryOwnerCacheFingerprints.Remove(remove[index]);
                memoryLibraryOwnerLru.Remove(remove[index]);
                InvalidateMemoryLibraryPublicationsForOwner(remove[index]);
            }
        }

        private void ExpireMemoryLibraryOwnerSnapshot(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return;
            memoryLibraryOwners.Remove(ownerKey);
            memoryLibraryOwnerCacheFingerprints.Remove(ownerKey);
            memoryLibraryOwnerLru.Remove(ownerKey);
            InvalidateMemoryLibraryPublicationsForOwner(ownerKey);
        }

        private void InvalidateMemoryLibraryPublicationsForOwner(string ownerKey)
        {
            CancelMemoryLibraryListBuild(ownerKey);
            RemoveMemoryLibraryPublications(memoryLibraryListPublications, ownerKey);
            RemoveMemoryLibraryPublications(memoryLibraryDetailPublications, ownerKey);
        }

        private static void RemoveMemoryLibraryPublications(
            Dictionary<string, MemoryLibraryPublication> publications,
            string ownerKey)
        {
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, MemoryLibraryPublication> pair in publications)
                if (string.Equals(pair.Value?.ownerKey, ownerKey, StringComparison.Ordinal))
                    remove.Add(pair.Key);
            for (int index = 0; index < remove.Count; index++) publications.Remove(remove[index]);
        }

        private MemoryLibraryOwnerRow FindPrimaryDirectoryRow(MemoryLibraryOwnerHandle handle)
        {
            string key = OwnerIndexKey(handle);
            for (int index = 0; index < memoryLibraryDirectory.Count; index++)
                if (string.Equals(OwnerIndexKey(memoryLibraryDirectory[index]?.primaryHandle),
                    key, StringComparison.Ordinal)) return memoryLibraryDirectory[index];
            return null;
        }

        private MemoryLibraryOwnerIndexInput BuildOwnerInput(
            PawnDiaryRecord diary,
            PawnKnowledgeState state,
            string displayName,
            bool active,
            MemoryLibraryLimits limits)
        {
            long snapshotNow = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            string scope = state.archiveOnly
                ? MemoryLibraryScopes.ArchiveOnly : MemoryLibraryScopes.Active;
            MemoryLibraryOwnerHandle handle = new MemoryLibraryOwnerHandle(
                scope, diary.pawnId, state.autobiographicalEpochToken ?? string.Empty);
            MemoryLibraryOwnerIndexInput input = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = handle,
                ownerEpochKey = !state.archiveOnly
                    && !string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)
                    ? new MemoryOwnerEpochKey
                    {
                        ownerPawnId = diary.pawnId,
                        epochToken = state.autobiographicalEpochToken
                    }
                    : null,
                displayName = displayName,
                lifecycleToken = active ? "active" : state.archiveOnly ? "archive" : "saved",
                culture = BuildMemoryOwnerCultureDto(state, limits),
                structuralRevision = state.structuralRevision,
                statusRevision = state.statusRevision,
                snapshotNowTick = snapshotNow,
                nextLocalizedDayBoundary = NextMemoryLibraryDayBoundary()
            };
            bool inert = false;
            for (int index = 0; state.threadRoots != null && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (root == null) continue;
                if (HasUnknownNewerReducerRevision(root)) { inert = true; continue; }
                MemoryLibraryRootIndexInput rootInput = BuildRootInput(
                    root, state, displayName, limits, policy, snapshotNow);
                if (rootInput != null) input.roots.Add(rootInput);
            }
            for (int index = 0; state.standaloneBlocks != null
                && index < state.standaloneBlocks.Count; index++)
            {
                SavedMemoryBlock block = state.standaloneBlocks[index];
                if (ShouldProjectSavedBlock(block, policy, snapshotNow))
                    input.standalone.Add(BuildMemoryBlockRow(
                    block, null, state.structuralRevision, displayName, limits));
            }
            for (int index = 0; state.importedArchiveRows != null
                && index < state.importedArchiveRows.Count; index++)
            {
                SavedImportedMemoryRow row = state.importedArchiveRows[index];
                if (row != null) input.imported.Add(BuildImportedRow(
                    row, scope, diary.pawnId, state.structuralRevision, limits));
            }
            if (inert)
            {
                input.compatibilityHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.InertCurrentExact, diary.pawnId, string.Empty);
                input.compatibilitySourcePayloadRevision = Math.Max(
                    1, Math.Max(state.structuralRevision, state.statusRevision));
            }
            return input;
        }

        private MemoryLibraryRootIndexInput BuildRootInput(
            SavedMemoryThreadRoot root,
            PawnKnowledgeState owner,
            string ownerDisplayName,
            MemoryLibraryLimits limits,
            MemoryPolicySnapshot policy,
            long snapshotNow)
        {
            if (root == null || string.IsNullOrWhiteSpace(root.rootId)) return null;
            MemoryRootHandle handle = new MemoryRootHandle
            {
                ownerPawnId = root.ownerPawnId ?? string.Empty,
                epochToken = root.ownerEpochToken ?? string.Empty,
                rootId = root.rootId
            };
            MemoryLibraryRootIndexInput result = new MemoryLibraryRootIndexInput
            {
                currentStatus = BuildCurrentStatusDto(owner, root, limits),
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = handle,
                    subjectLabel = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        root.frozenSubjectLabel, limits.frozenDisplayLabelUtf16Units),
                    subjectTypeToken = root.subjectKind == "pawn" ? "Person" : root.subjectKind,
                    latestActivityTick = LatestRootTick(root),
                    chapterCount = root.chapters?.Count ?? 0,
                    structuralRevision = root.structuralRevision,
                    statusRevision = root.statusRevision,
                    normalizedSearch = MemoryLibraryPolicy.BuildSearchProjection(
                        new[] { root.frozenSubjectLabel, ownerDisplayName },
                        limits.normalizedFieldUtf16Units,
                        limits.rowProjectionUtf16Units)
                }
            };
            for (int index = 0; root.visibleBlocks != null
                && index < root.visibleBlocks.Count; index++)
            {
                SavedMemoryBlock block = root.visibleBlocks[index];
                if (ShouldProjectSavedBlock(block, policy, snapshotNow))
                    result.children.Add(BuildMemoryBlockRow(
                    block, root, root.structuralRevision, ownerDisplayName, limits));
            }
            if (ShouldProjectSavedBlock(root.rollingSummaryBlock, policy, snapshotNow))
                result.children.Add(BuildMemoryBlockRow(
                    root.rollingSummaryBlock, root, root.structuralRevision,
                    ownerDisplayName, limits));
            for (int index = 0; root.chapters != null && index < root.chapters.Count; index++)
            {
                SavedMemoryChapter chapter = root.chapters[index];
                if (chapter == null) continue;
                result.chapters.Add(new MemoryChapterRow
                {
                    chapterId = chapter.chapterId ?? string.Empty,
                    ordinal = chapter.ordinal,
                    phaseToken = chapter.phaseToken ?? string.Empty,
                    openedTick = chapter.openedTick,
                    lastActivityTick = chapter.lastActivityTick,
                    closedTick = chapter.closedTick,
                    closureReasonToken = chapter.closureReasonToken ?? string.Empty,
                    closed = chapter.closed
                });
            }
            result.chapters.Sort((left, right) => right.ordinal.CompareTo(left.ordinal));
            result.children.Sort(delegate(MemoryBlockRow left, MemoryBlockRow right)
            {
                if (left.rollingSummary != right.rollingSummary)
                    return left.rollingSummary ? -1 : 1;
                long leftChapter = ChapterOrdinal(result.chapters, left.chapterId);
                long rightChapter = ChapterOrdinal(result.chapters, right.chapterId);
                int chapter = rightChapter.CompareTo(leftChapter);
                if (chapter != 0) return chapter;
                int tick = left.originalTick.CompareTo(right.originalTick);
                return tick != 0 ? tick : string.Compare(
                    left.recordHandle?.recordId, right.recordHandle?.recordId,
                    StringComparison.Ordinal);
            });
            return result;
        }

        private static MemoryCurrentStatusDto BuildCurrentStatusDto(
            PawnKnowledgeState owner,
            SavedMemoryThreadRoot root,
            MemoryLibraryLimits limits)
        {
            SavedMemoryAwarenessSnapshot selected = null;
            for (int index = 0; owner?.ownerAwarenessSnapshots != null
                && index < owner.ownerAwarenessSnapshots.Count; index++)
            {
                SavedMemoryAwarenessSnapshot candidate = owner.ownerAwarenessSnapshots[index];
                if (candidate != null
                    && string.Equals(candidate.subjectKind, root.subjectKind, StringComparison.Ordinal)
                    && string.Equals(candidate.subjectId, root.subjectId, StringComparison.Ordinal))
                {
                    selected = candidate;
                    break;
                }
            }
            if (selected == null) return new MemoryCurrentStatusDto();
            MemoryCurrentStatusDto result = new MemoryCurrentStatusDto
            {
                statusToken = string.IsNullOrWhiteSpace(selected.trackingStateToken)
                    ? "Unknown" : selected.trackingStateToken,
                knownnessEvidenceToken = selected.knownnessEvidenceToken ?? string.Empty,
                sourceCaptureGeneration = selected.captureInvalidationGeneration,
                capturedTick = selected.lastObservedTick,
                statusSnapshotRevision = selected.snapshotRevision
            };
            for (int index = 0; selected.stateFacts != null
                && index < selected.stateFacts.Count
                && result.frozenDisplayFields.Count < limits.currentStatusFieldCount; index++)
            {
                SavedMemoryStateFact fact = selected.stateFacts[index];
                if (fact != null) MemoryLibraryPolicy.TryAppendBoundedText(
                    result.frozenDisplayFields,
                    (fact.factKey ?? string.Empty) + ": " + (fact.factValue ?? string.Empty),
                    limits.currentStatusFieldCount,
                    limits.currentStatusFieldTextUtf16Units);
            }
            return result;
        }

        private MemoryBlockRow BuildMemoryBlockRow(
            SavedMemoryBlock block,
            SavedMemoryThreadRoot root,
            long targetStructuralRevision,
            string ownerDisplayName,
            MemoryLibraryLimits limits)
        {
            bool summary = block.kind == MemoryContractTokens.KindSummary;
            bool rolling = summary
                && block.summaryRole == MemoryContractTokens.SummaryRoleRolling;
            bool closed = summary
                && block.summaryRole == MemoryContractTokens.SummaryRoleClosed;
            int categoryMask = summary
                ? block.summaryPayload?.derivedCategoryMask ?? 0
                : MemoryCategoryBits.ForToken(block.category);
            int importance = MemoryLibraryPolicy.ImportanceMask(summary
                ? block.summaryPayload?.highestSurvivingImportance
                : block.importance);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            string automatic = summary
                ? SelectCurrentSummaryNaturalWording(block, root, policy)
                : block.automaticWording;
            string wording = block.playerEdited && !string.IsNullOrEmpty(block.playerWording)
                ? block.playerWording : automatic;
            string primary = block.primarySubject?.frozenLabel ?? string.Empty;
            List<string> searchFields = new List<string> { wording, primary };
            for (int index = 0; block.secondarySubjects != null
                && index < block.secondarySubjects.Count; index++)
                searchFields.Add(block.secondarySubjects[index]?.frozenLabel ?? string.Empty);
            int dateTick = (int)Math.Max(0, Math.Min(int.MaxValue, block.originalEventTick));
            searchFields.Add(KnowledgeDateLabelAt(null, dateTick));
            searchFields.Add(block.category ?? string.Empty);
            searchFields.Add(ownerDisplayName ?? string.Empty);
            bool threaded = root != null;
            bool eventKind = block.kind == MemoryContractTokens.KindEvent;
            bool landmark = block.kind == MemoryContractTokens.KindLandmark;
            bool canEdit = !rolling && (landmark || closed || (!threaded && eventKind));
            long expiry = SummaryFutureExpiry(block, policy);
            List<MemorySummaryContributionDescriptor> contributions = summary
                ? BuildSummaryContributionDescriptors(block, policy, limits)
                : new List<MemorySummaryContributionDescriptor>();
            int savedSummaryContributionCount = summary
                ? CountSummaryContributions(block) : 0;
            int projectedCategoryMask = summary ? 0 : categoryMask;
            int projectedImportanceMask = summary ? 0 : importance;
            int projectedHighest = summary ? 0 : importance;
            for (int index = 0; index < contributions.Count; index++)
            {
                projectedCategoryMask |= contributions[index].categoryMask;
                projectedImportanceMask |= contributions[index].importanceMask;
                projectedHighest = HigherLibraryImportance(
                    projectedHighest, contributions[index].importanceMask);
            }
            if (summary && contributions.Count == 0)
            {
                projectedCategoryMask = categoryMask;
                projectedImportanceMask = importance;
                projectedHighest = importance;
            }
            string wholeSearch = MemoryLibraryPolicy.BuildSearchProjection(
                searchFields,
                limits.normalizedFieldUtf16Units,
                limits.rowProjectionUtf16Units);
            MemoryBlockRow row = new MemoryBlockRow
            {
                recordHandle = new MemoryRecordHandle
                {
                    ownerPawnId = block.ownerPawnId ?? string.Empty,
                    epochToken = block.ownerEpochToken ?? string.Empty,
                    recordId = block.recordId ?? string.Empty
                },
                rootHandle = threaded ? new MemoryRootHandle
                {
                    ownerPawnId = root.ownerPawnId ?? string.Empty,
                    epochToken = root.ownerEpochToken ?? string.Empty,
                    rootId = root.rootId ?? string.Empty
                } : null,
                chapterId = block.chapterId ?? string.Empty,
                targetStructuralRevision = targetStructuralRevision,
                kind = block.kind ?? string.Empty,
                summaryRole = block.summaryRole ?? string.Empty,
                projectedCategoryMask = projectedCategoryMask,
                projectedImportanceMask = projectedImportanceMask,
                projectedHighestImportanceMask = projectedHighest,
                originalTick = summary
                    ? block.summaryPayload?.earliestSurvivingTick ?? block.originalEventTick
                    : block.originalEventTick,
                activityTick = summary
                    ? block.summaryPayload?.latestSurvivingTick ?? block.originalEventTick
                    : block.originalEventTick,
                projectedNextExpiryTick = expiry,
                displayWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    wording, limits.blockTextUtf16Units),
                primarySubjectLabel = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    primary, limits.frozenDisplayLabelUtf16Units),
                playerEdited = block.playerEdited,
                suppressed = block.suppressed,
                canSuppress = true,
                canSaveWording = canEdit,
                canUseOriginal = block.playerEdited && !rolling,
                canDevForget = true,
                lastAutomaticIncludedTick = block.lastAutomaticIncludedTick,
                automaticInclusionCount = block.automaticInclusionCount,
                providerExposureState = block.providerExposureState ?? string.Empty,
                normalizedSearch = wholeSearch,
                normalizedWholeSearch = wholeSearch,
                summaryContributions = contributions,
                rollingSummary = rolling,
                closedSummary = closed,
                ageUnknown = block.ageUnknown
            };
            if (summary && !block.playerEdited
                && contributions.Count < savedSummaryContributionCount)
            {
                row = MemoryLibraryPolicy.ProjectSummaryForSnapshot(
                    row, contributions, limits);
            }
            return row;
        }

        private static bool ShouldProjectSavedBlock(
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy,
            long nowTick)
        {
            if (block == null || policy == null) return false;
            if (block.kind != MemoryContractTokens.KindSummary)
                return MemoryLibraryPolicy.RetainedAtSnapshot(
                    nowTick,
                    block.originalEventTick,
                    block.ageUnknown,
                    block.playerEdited,
                    MemoryLibraryPolicy.ImportanceMask(block.importance),
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks);
            if (block.playerEdited) return true;
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (contribution != null && MemoryLibraryPolicy.RetainedAtSnapshot(
                            nowTick,
                            contribution.originalEventTick,
                            contribution.ageUnknown,
                            false,
                            MemoryLibraryPolicy.ImportanceMask(contribution.importance),
                            policy.minorMemoryLifetimeTicks,
                            policy.regularMemoryLifetimeTicks)) return true;
                }
            }
            return false;
        }

        private static int CountSummaryContributions(SavedMemoryBlock block)
        {
            int count = 0;
            for (int bucketIndex = 0; block?.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                    if (bucket.contributions[index] != null) count++;
            }
            return count;
        }

        /// <summary>
        /// Selects one Summary's disposable provider wording for Library display only while its
        /// exact current category projection remains valid. Suppressed or player-edited memories
        /// deliberately expose no provider prose; their deterministic/player wording remains usable.
        /// </summary>
        private string SelectCurrentSummaryNaturalWording(
            SavedMemoryBlock block,
            SavedMemoryThreadRoot root,
            MemoryPolicySnapshot policy)
        {
            string deterministic = block?.summaryPayload?.deterministicWording
                ?? block?.automaticWording
                ?? string.Empty;
            if (block == null || block.suppressed || block.playerEdited) return deterministic;
            SummaryWordingCurrentSnapshot current = CurrentSummarySnapshot(root, block, policy);
            SavedMemorySummaryPayload payload = block.summaryPayload;
            if (current == null || payload == null) return deterministic;
            MemoryRecallSummaryWordingSnapshot wording = new MemoryRecallSummaryWordingSnapshot
            {
                currentProjectionFingerprint = current.projectionFingerprint ?? string.Empty,
                currentFormatRevision = current.formatRevision,
                currentCategoryMask = current.categoryMask,
                optionalWording = payload.optionalLlmWording ?? string.Empty,
                optionalFingerprint = payload.optionalLlmFingerprint ?? string.Empty,
                optionalFormatRevision = payload.optionalLlmFormatRevision,
                optionalCategoryMask = payload.optionalLlmCategoryMask,
                optionalSucceeded = string.Equals(
                    payload.lastWordingDispositionToken,
                    MemoryOptionalWordingDispositionTokens.Success,
                    StringComparison.Ordinal)
            };
            return MemoryNaturalWordingProjection.Select(
                false,
                deterministic,
                wording,
                Math.Max(1, MemoryOptionalTuning()?.fallbackSummaryMaxChars ?? 240));
        }

        private List<MemorySummaryContributionDescriptor> BuildSummaryContributionDescriptors(
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy,
            MemoryLibraryLimits limits)
        {
            List<MemorySummaryContributionDescriptor> result =
                new List<MemorySummaryContributionDescriptor>();
            int cap = (int)ReadCapacityTuplePart(
                "datedContributionDescriptorMatchCaps", 0, 32, 128);
            Dictionary<string, SavedMemorySubjectRef> subjects =
                new Dictionary<string, SavedMemorySubjectRef>(StringComparer.Ordinal);
            for (int index = 0; block.summaryPayload?.subjectRefs != null
                && index < block.summaryPayload.subjectRefs.Count; index++)
            {
                SavedMemorySubjectRef subject = block.summaryPayload.subjectRefs[index];
                if (subject != null && !string.IsNullOrEmpty(subject.subjectRefId)
                    && !subjects.ContainsKey(subject.subjectRefId))
                    subjects.Add(subject.subjectRefId, subject);
            }
            Dictionary<string, SavedMemoryProvenance> provenance =
                new Dictionary<string, SavedMemoryProvenance>(StringComparer.Ordinal);
            for (int index = 0; block.summaryPayload?.provenanceRefs != null
                && index < block.summaryPayload.provenanceRefs.Count; index++)
            {
                SavedMemoryProvenance item = block.summaryPayload.provenanceRefs[index];
                if (item != null && !string.IsNullOrEmpty(item.provenanceRefId)
                    && !provenance.ContainsKey(item.provenanceRefId))
                    provenance.Add(item.provenanceRefId, item);
            }
            int ordinal = 0;
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count && result.Count < cap; index++, ordinal++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (contribution == null) continue;
                    int importance = MemoryLibraryPolicy.ImportanceMask(contribution.importance);
                    if (!MemoryLibraryPolicy.RetainedAtSnapshot(
                            now,
                            contribution.originalEventTick,
                            contribution.ageUnknown,
                            block.playerEdited,
                            importance,
                            policy?.minorMemoryLifetimeTicks ?? long.MaxValue,
                            policy?.regularMemoryLifetimeTicks ?? long.MaxValue)) continue;
                    MemorySummaryContributionDescriptor descriptor =
                        new MemorySummaryContributionDescriptor
                        {
                            sourceOrdinal = ordinal,
                            categoryMask = MemoryCategoryBits.ForToken(contribution.category),
                            importanceMask = importance,
                            originalTick = contribution.originalEventTick,
                            ageUnknown = contribution.ageUnknown,
                            browsePreview = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                                contribution.canonicalValue, limits.blockTextUtf16Units),
                            nextExpiryTick = MemoryLibraryPolicy.FutureExpiryTick(
                                contribution.originalEventTick,
                                contribution.ageUnknown,
                                block.playerEdited,
                                importance,
                                policy?.minorMemoryLifetimeTicks ?? long.MaxValue,
                                policy?.regularMemoryLifetimeTicks ?? long.MaxValue,
                                now)
                        };
                    descriptor.searchFields.Add(contribution.canonicalValue ?? string.Empty);
                    descriptor.searchFields.Add(contribution.category ?? string.Empty);
                    descriptor.searchFields.Add(contribution.importance ?? string.Empty);
                    descriptor.searchFields.Add(KnowledgeDateLabelAt(null, (int)Math.Max(
                        0, Math.Min(int.MaxValue, contribution.originalEventTick))));
                    descriptor.factDescriptors.Add((bucket.factKind ?? string.Empty) + ":" +
                        MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                            contribution.canonicalValue, limits.blockTextUtf16Units));
                    for (int refIndex = 0; contribution.subjectRefIds != null
                        && refIndex < contribution.subjectRefIds.Count; refIndex++)
                    {
                        if (!subjects.TryGetValue(contribution.subjectRefIds[refIndex],
                            out SavedMemorySubjectRef subject)) continue;
                        string label = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                            subject.frozenLabel, limits.frozenDisplayLabelUtf16Units);
                        descriptor.searchFields.Add(label);
                        descriptor.subjectDescriptors.Add(
                            (subject.subjectKind ?? string.Empty) + ":" + label);
                    }
                    for (int refIndex = 0; contribution.provenanceRefIds != null
                        && refIndex < contribution.provenanceRefIds.Count; refIndex++)
                    {
                        if (!provenance.TryGetValue(contribution.provenanceRefIds[refIndex],
                            out SavedMemoryProvenance item)) continue;
                        descriptor.provenanceDescriptors.Add(
                            (item.sourceKindToken ?? string.Empty) + ":" +
                            (item.sourceOccurrenceId ?? string.Empty));
                    }
                    result.Add(descriptor);
                }
            }
            return result;
        }

        private static int HigherLibraryImportance(int left, int right)
        {
            if ((left & MemoryLibraryPolicy.ImportanceImportant) != 0
                || (right & MemoryLibraryPolicy.ImportanceImportant) != 0)
                return MemoryLibraryPolicy.ImportanceImportant;
            if ((left & MemoryLibraryPolicy.ImportanceRegular) != 0
                || (right & MemoryLibraryPolicy.ImportanceRegular) != 0)
                return MemoryLibraryPolicy.ImportanceRegular;
            return (left & MemoryLibraryPolicy.ImportanceMinor) != 0
                || (right & MemoryLibraryPolicy.ImportanceMinor) != 0
                ? MemoryLibraryPolicy.ImportanceMinor : 0;
        }

        private long SummaryFutureExpiry(SavedMemoryBlock block, MemoryPolicySnapshot policy)
        {
            if (block == null || policy == null || block.playerEdited) return long.MaxValue;
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            if (block.kind != MemoryContractTokens.KindSummary)
                return MemoryLibraryPolicy.FutureExpiryTick(
                    block.originalEventTick,
                    block.ageUnknown,
                    block.playerEdited,
                    MemoryLibraryPolicy.ImportanceMask(block.importance),
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks,
                    now);
            long earliest = long.MaxValue;
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (contribution == null) continue;
                    int importance = MemoryLibraryPolicy.ImportanceMask(
                        contribution.importance);
                    if (!MemoryLibraryPolicy.RetainedAtSnapshot(
                            now,
                            contribution.originalEventTick,
                            contribution.ageUnknown,
                            false,
                            importance,
                            policy.minorMemoryLifetimeTicks,
                            policy.regularMemoryLifetimeTicks)) continue;
                    earliest = Math.Min(earliest, MemoryLibraryPolicy.FutureExpiryTick(
                        contribution.originalEventTick,
                        contribution.ageUnknown,
                        false,
                        importance,
                        policy.minorMemoryLifetimeTicks,
                        policy.regularMemoryLifetimeTicks,
                        now));
                }
            }
            return earliest;
        }

        private MemoryBlockDetail BuildMemoryBlockDetail(
            SavedMemoryBlock block,
            MemoryLibraryLimits limits,
            MemoryBlockRow projection)
        {
            MemoryBlockDetail result = new MemoryBlockDetail
            {
                automaticWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    block.kind == MemoryContractTokens.KindSummary
                        ? projection?.displayWording ?? block.summaryPayload?.deterministicWording
                        : block.automaticWording,
                    limits.blockTextUtf16Units),
                playerWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    block.playerWording, limits.blockTextUtf16Units),
                sourcePageLinkToken = block.sourceEventId ?? string.Empty
            };
            for (int index = 0; block.facts != null && index < block.facts.Count && index < 16; index++)
            {
                SavedMemoryCanonicalFact fact = block.facts[index];
                if (fact != null) result.factDescriptors.Add(
                    (fact.factKind ?? string.Empty) + ":" +
                    MemoryLibraryPolicy.ClampUtf16CompleteScalar(fact.canonicalValue, 240));
            }
            if (block.kind == MemoryContractTokens.KindSummary
                && projection?.summaryContributions != null
                && projection.summaryContributions.Count > 0)
            {
                for (int index = 0; index < projection.summaryContributions.Count
                    && result.factDescriptors.Count < 16; index++)
                {
                    MemorySummaryContributionDescriptor descriptor =
                        projection.summaryContributions[index];
                    AddDistinctBounded(result.factDescriptors, descriptor?.factDescriptors, 16);
                    AddDistinctBounded(result.subjectDescriptors, descriptor?.subjectDescriptors, 16);
                    AddDistinctBounded(result.provenanceDescriptors,
                        descriptor?.provenanceDescriptors, 8);
                }
            }
            else if (block.kind == MemoryContractTokens.KindSummary)
            {
                for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                    && bucketIndex < block.summaryPayload.factBuckets.Count
                    && result.factDescriptors.Count < 16; bucketIndex++)
                {
                    SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                    if (bucket == null) continue;
                    result.factDescriptors.Add((bucket.factKind ?? string.Empty) + ":" +
                        MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                            bucket.derivedRangeMax, 240));
                }
                for (int index = 0; block.summaryPayload?.subjectRefs != null
                    && index < block.summaryPayload.subjectRefs.Count
                    && result.subjectDescriptors.Count < 16; index++)
                    AddSubjectDetail(result.subjectDescriptors,
                        block.summaryPayload.subjectRefs[index],
                        limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.summaryPayload?.provenanceRefs != null
                    && index < block.summaryPayload.provenanceRefs.Count && index < 8; index++)
                    AddProvenanceDetail(result.provenanceDescriptors,
                        block.summaryPayload.provenanceRefs[index]);
            }
            else
            {
                AddSubjectDetail(result.subjectDescriptors, block.primarySubject,
                    limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.secondarySubjects != null
                    && index < block.secondarySubjects.Count
                    && result.subjectDescriptors.Count < 16; index++)
                    AddSubjectDetail(result.subjectDescriptors, block.secondarySubjects[index],
                        limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.provenance != null
                    && index < block.provenance.Count && index < 8; index++)
                    AddProvenanceDetail(result.provenanceDescriptors, block.provenance[index]);
            }
            if (Prefs.DevMode)
            {
                AddDevReason(result.devIdentifiersAndReasons,
                    "record=" + (block.recordId ?? string.Empty), limits);
                AddDevReason(result.devIdentifiersAndReasons,
                    "source=" + (block.sourceOccurrenceId ?? string.Empty), limits);
                AddDevReason(result.devIdentifiersAndReasons,
                    "root=" + (block.rootId ?? string.Empty), limits);
                AddDevReason(result.devIdentifiersAndReasons,
                    "chapter=" + (block.chapterId ?? string.Empty), limits);
            }
            return result;
        }

        private static void AddDevReason(
            List<string> target,
            string value,
            MemoryLibraryLimits limits)
        {
            if (target == null || limits == null || target.Count >= limits.devReasonCount) return;
            string bounded = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                value, limits.devReasonTextUtf16Units);
            if (bounded.Length > 0) target.Add(bounded);
        }

        private static void AddDistinctBounded(
            List<string> target,
            List<string> source,
            int cap)
        {
            for (int index = 0; target != null && source != null
                && index < source.Count && target.Count < cap; index++)
            {
                string value = source[index] ?? string.Empty;
                if (!target.Contains(value)) target.Add(value);
            }
        }

        private static void AddSubjectDetail(
            List<string> target,
            SavedMemorySubjectRef subject,
            int labelCap)
        {
            if (subject == null || target == null) return;
            target.Add((subject.subjectKind ?? string.Empty) + ":" +
                MemoryLibraryPolicy.ClampUtf16CompleteScalar(subject.frozenLabel, labelCap));
        }

        private static void AddProvenanceDetail(
            List<string> target,
            SavedMemoryProvenance provenance)
        {
            if (target == null || provenance == null) return;
            target.Add((provenance.sourceKindToken ?? string.Empty) + ":" +
                (provenance.sourceOccurrenceId ?? string.Empty));
        }

        private MemoryImportedSearchDescriptor BuildImportedRow(
            SavedImportedMemoryRow row,
            string scope,
            string ownerId,
            long structuralRevision,
            MemoryLibraryLimits limits)
        {
            return new MemoryImportedSearchDescriptor
            {
                row = new MemoryImportedRow
                {
                    archiveHandle = new MemoryArchiveHandle
                    {
                        archiveScopeToken = scope,
                        exactOwnerPawnIdOrEmpty = ownerId ?? string.Empty,
                        archiveRecordId = row.archiveRecordId ?? string.Empty
                    },
                    preview = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        row.importedWording, limits.importedPreviewUtf16Units),
                    originalTick = row.originalEventTick,
                    ageUnknown = row.ageUnknown,
                    migrationReasonToken = row.migrationReasonToken ?? string.Empty,
                    targetStructuralRevision = structuralRevision
                },
                rawSearchText = row.importedWording ?? string.Empty
            };
        }

        private MemoryOwnerCultureDto BuildMemoryOwnerCultureDto(
            PawnKnowledgeState state,
            MemoryLibraryLimits limits)
        {
            MemoryOwnerCultureDto result = new MemoryOwnerCultureDto();
            ResolveCultureDisplay(state?.originCultureDefName, out result.originStateToken,
                out result.originDisplayLabel, limits);
            string source = state?.originCultureSource ?? string.Empty;
            result.originProvenanceToken = MemoryLibraryPolicy.CultureProvenanceToken(source);
            ResolveCultureDisplay(state?.adoptedCultureDefName, out result.adoptedStateToken,
                out result.adoptedDisplayLabel, limits);
            return result;
        }

        private static string SavedCultureSourceFingerprint(PawnKnowledgeState state)
        {
            return MemoryLibraryPolicy.StreamFingerprint(
                state?.originCultureDefName ?? string.Empty,
                state?.originCultureSource ?? string.Empty,
                state?.adoptedCultureDefName ?? string.Empty);
        }

        private static void ResolveCultureDisplay(
            string defName,
            out string stateToken,
            out string displayLabel,
            MemoryLibraryLimits limits)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                stateToken = "none";
                displayLabel = string.Empty;
                return;
            }
            DiaryCultureProfileDef def =
                DefDatabase<DiaryCultureProfileDef>.GetNamedSilentFail(defName.Trim());
            stateToken = MemoryLibraryPolicy.CultureStateToken(defName, def != null);
            displayLabel = def == null ? string.Empty
                : MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    def.LabelCap.ToString(), limits.frozenDisplayLabelUtf16Units);
        }

        private MemoryLibraryOwnerIndexSnapshot BuildZeroOwner(string ownerId, string name)
        {
            return MemoryLibraryIndexPolicy.BuildOwner(new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.Active, ownerId, string.Empty),
                displayName = name,
                lifecycleToken = "active",
                culture = new MemoryOwnerCultureDto(),
                structuralRevision = 0,
                statusRevision = 0,
                nextLocalizedDayBoundary = NextMemoryLibraryDayBoundary()
            }, BuildMemoryLibraryLimits());
        }

        private MemoryLibraryOwnerIndexSnapshot BuildUnknownOwner(MemoryLibraryLimits limits)
        {
            bool current = unresolvedOwnerArchiveRows != null
                && unresolvedOwnerArchiveRows.Count > 0;
            MemoryLibraryOwnerHandle primary = current
                ? new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.UnresolvedImported, string.Empty, string.Empty)
                : null;
            MemoryLibraryOwnerHandle compatibility = rawUnresolvedOwnerArchiveInput != null
                && rawUnresolvedOwnerArchiveInput.Count > 0
                ? new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.LegacyRawUnknown, string.Empty, string.Empty)
                : null;
            MemoryLibraryOwnerIndexSnapshot result = new MemoryLibraryOwnerIndexSnapshot
            {
                ownerRow = new MemoryLibraryOwnerRow
                {
                    primaryHandle = primary,
                    compatibilityHandle = compatibility,
                    displayName = "PawnDiary.Memory.Library.UnknownOwner".Translate().ToString(),
                    lifecycleToken = "unknown",
                    importedCount = unresolvedOwnerArchiveRows?.Count ?? 0,
                    hasArchive = true,
                    legacyRawPending = compatibility != null,
                    structuralRevision = unresolvedArchiveStructuralRevision,
                    compatibilitySourcePayloadRevision = compatibility == null ? 0
                        : Math.Max(1, rawUnresolvedArchiveReattributionGeneration)
                }
            };
            result.ownerRow.normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                result.ownerRow.displayName, limits.searchScalars, limits.searchUtf16Units);
            for (int index = 0; unresolvedOwnerArchiveRows != null
                && index < unresolvedOwnerArchiveRows.Count; index++)
            {
                SavedImportedMemoryRow row = unresolvedOwnerArchiveRows[index];
                if (row != null) result.imported.Add(BuildImportedRow(
                    row,
                    MemoryLibraryScopes.UnresolvedImported,
                    string.Empty,
                    unresolvedArchiveStructuralRevision,
                    limits));
            }
            return result;
        }

        private string DirectoryFingerprint(List<MemoryLibraryOwnerIndexSnapshot> rows)
        {
            List<string> fields = new List<string>();
            for (int index = 0; rows != null && index < rows.Count; index++)
            {
                MemoryLibraryOwnerRow row = rows[index]?.ownerRow;
                if (row == null) continue;
                fields.Add(OwnerIndexKey(row.primaryHandle));
                fields.Add(OwnerIndexKey(row.compatibilityHandle));
                fields.Add(row.displayName ?? string.Empty);
                fields.Add(row.lifecycleToken ?? string.Empty);
                fields.Add(row.threadCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.standaloneCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.importedCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.latestActivityTick.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.structuralRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.statusRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.compatibilitySourcePayloadRevision
                    .ToString(CultureInfo.InvariantCulture));
                fields.Add(row.hasArchive ? "1" : "0");
                fields.Add(row.legacyRawPending ? "1" : "0");
                fields.Add(CultureFingerprint(row.culture));
                fields.Add(rows[index]?.directoryCultureSourceFingerprint ?? string.Empty);
                fields.Add(OwnerSnapshotFingerprint(rows[index]));
            }
            fields.Add(memoryLibraryAdditionalLegacyRawOwners.ToString(CultureInfo.InvariantCulture));
            fields.Add(memoryLibraryAdditionalZeroOwners.ToString(CultureInfo.InvariantCulture));
            return MemoryLibraryPolicy.StreamFingerprint(fields.ToArray());
        }

        private static string OwnerSnapshotFingerprint(MemoryLibraryOwnerIndexSnapshot snapshot)
        {
            List<string> fields = new List<string>();
            MemoryLibraryOwnerRow owner = snapshot?.ownerRow;
            fields.Add("owner");
            fields.Add(OwnerIndexKey(owner?.primaryHandle));
            fields.Add(OwnerEpochKey(owner?.activeOwnerEpochKey));
            fields.Add(OwnerIndexKey(owner?.compatibilityHandle));
            fields.Add(owner?.displayName ?? string.Empty);
            fields.Add(owner?.lifecycleToken ?? string.Empty);
            fields.Add((owner?.structuralRevision ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((owner?.statusRevision ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((owner?.compatibilitySourcePayloadRevision ?? 0)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add((snapshot?.ownerEarliestFiniteExpiryTickExclusive ?? long.MaxValue)
                .ToString(CultureInfo.InvariantCulture));
            for (int rootIndex = 0; snapshot?.roots != null
                && rootIndex < snapshot.roots.Count; rootIndex++)
            {
                MemoryLibraryRootIndexInput root = snapshot.roots[rootIndex];
                AddThreadHeaderFingerprintFields(fields, root?.header);
                fields.Add((root?.rootEarliestFiniteExpiryTickExclusive ?? long.MaxValue)
                    .ToString(CultureInfo.InvariantCulture));
                AddCurrentStatusFingerprintFields(fields, root?.currentStatus);
                for (int chapterIndex = 0; root?.chapters != null
                    && chapterIndex < root.chapters.Count; chapterIndex++)
                    AddChapterFingerprintFields(fields, root.chapters[chapterIndex]);
                for (int childIndex = 0; root?.children != null
                    && childIndex < root.children.Count; childIndex++)
                    AddBlockFingerprintFields(fields, root.children[childIndex]);
            }
            for (int index = 0; snapshot?.standalone != null
                && index < snapshot.standalone.Count; index++)
                AddBlockFingerprintFields(fields, snapshot.standalone[index]);
            for (int index = 0; snapshot?.imported != null
                && index < snapshot.imported.Count; index++)
            {
                MemoryImportedSearchDescriptor descriptor = snapshot.imported[index];
                MemoryImportedRow row = descriptor?.row;
                fields.Add("imported");
                fields.Add(ArchiveHandleKey(row?.archiveHandle));
                fields.Add(row?.preview ?? string.Empty);
                fields.Add(descriptor?.rawSearchText ?? string.Empty);
                fields.Add((row?.originalTick ?? 0).ToString(CultureInfo.InvariantCulture));
                fields.Add(row != null && row.ageUnknown ? "1" : "0");
                fields.Add(row?.migrationReasonToken ?? string.Empty);
                fields.Add((row?.targetStructuralRevision ?? 0)
                    .ToString(CultureInfo.InvariantCulture));
            }
            return MemoryLibraryPolicy.StreamFingerprint(fields.ToArray());
        }

        private static void AddThreadHeaderFingerprintFields(
            List<string> fields,
            MemoryThreadHeaderRow header)
        {
            fields.Add("root");
            fields.Add(RootHandleKey(header?.rootHandle));
            fields.Add(header?.subjectLabel ?? string.Empty);
            fields.Add(header?.subjectTypeToken ?? string.Empty);
            fields.Add((header?.latestActivityTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.chapterCount ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.targetCountedVisibleBlockCount ?? 0)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.manageableMemoryCount ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.highestImportanceMask ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.editedCount ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.suppressedCount ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(header?.normalizedSearch ?? string.Empty);
            fields.Add((header?.structuralRevision ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((header?.statusRevision ?? 0).ToString(CultureInfo.InvariantCulture));
        }

        private static void AddCurrentStatusFingerprintFields(
            List<string> fields,
            MemoryCurrentStatusDto status)
        {
            fields.Add("status");
            fields.Add(status?.statusToken ?? string.Empty);
            for (int index = 0; status?.frozenDisplayFields != null
                && index < status.frozenDisplayFields.Count; index++)
                fields.Add(status.frozenDisplayFields[index] ?? string.Empty);
            fields.Add(status?.knownnessEvidenceToken ?? string.Empty);
            fields.Add((status?.sourceCaptureGeneration ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((status?.capturedTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((status?.statusSnapshotRevision ?? 0).ToString(CultureInfo.InvariantCulture));
        }

        private static void AddChapterFingerprintFields(
            List<string> fields,
            MemoryChapterRow chapter)
        {
            fields.Add("chapter");
            fields.Add(chapter?.chapterId ?? string.Empty);
            fields.Add((chapter?.ordinal ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(chapter?.phaseToken ?? string.Empty);
            fields.Add((chapter?.openedTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((chapter?.lastActivityTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((chapter?.closedTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(chapter?.closureReasonToken ?? string.Empty);
            fields.Add(chapter != null && chapter.closed ? "1" : "0");
        }

        private static void AddBlockFingerprintFields(
            List<string> fields,
            MemoryBlockRow row)
        {
            fields.Add("block");
            fields.Add(row?.recordHandle?.recordId ?? string.Empty);
            fields.Add(RootHandleKey(row?.rootHandle));
            fields.Add(row?.chapterId ?? string.Empty);
            fields.Add(row?.kind ?? string.Empty);
            fields.Add(row?.summaryRole ?? string.Empty);
            fields.Add((row?.projectedCategoryMask ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.projectedImportanceMask ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.projectedHighestImportanceMask ?? 0)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.originalTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.activityTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(row?.displayWording ?? string.Empty);
            fields.Add(row?.primarySubjectLabel ?? string.Empty);
            fields.Add(row?.normalizedSearch ?? string.Empty);
            fields.Add(row?.normalizedWholeSearch ?? string.Empty);
            fields.Add(row != null && row.playerEdited ? "1" : "0");
            fields.Add(row != null && row.suppressed ? "1" : "0");
            fields.Add(row != null && row.canSuppress ? "1" : "0");
            fields.Add(row != null && row.canSaveWording ? "1" : "0");
            fields.Add(row != null && row.canUseOriginal ? "1" : "0");
            fields.Add(row != null && row.canDevForget ? "1" : "0");
            fields.Add((row?.lastAutomaticIncludedTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.automaticInclusionCount ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add(row?.providerExposureState ?? string.Empty);
            fields.Add(row != null && row.rollingSummary ? "1" : "0");
            fields.Add(row != null && row.closedSummary ? "1" : "0");
            fields.Add(row != null && row.ageUnknown ? "1" : "0");
            fields.Add((row?.targetStructuralRevision ?? 0)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.projectedNextExpiryTick ?? long.MaxValue)
                .ToString(CultureInfo.InvariantCulture));
            for (int index = 0; row?.summaryContributions != null
                && index < row.summaryContributions.Count; index++)
                AddSummaryContributionFingerprintFields(fields, row.summaryContributions[index]);
        }

        private static void AddSummaryContributionFingerprintFields(
            List<string> fields,
            MemorySummaryContributionDescriptor contribution)
        {
            fields.Add("contribution");
            fields.Add((contribution?.sourceOrdinal ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((contribution?.categoryMask ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((contribution?.importanceMask ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((contribution?.originalTick ?? 0).ToString(CultureInfo.InvariantCulture));
            fields.Add((contribution?.nextExpiryTick ?? long.MaxValue)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add(contribution != null && contribution.ageUnknown ? "1" : "0");
            fields.Add(contribution?.browsePreview ?? string.Empty);
            AddStringListFingerprintFields(fields, "search", contribution?.searchFields);
            AddStringListFingerprintFields(fields, "fact", contribution?.factDescriptors);
            AddStringListFingerprintFields(fields, "subject", contribution?.subjectDescriptors);
            AddStringListFingerprintFields(fields, "provenance", contribution?.provenanceDescriptors);
        }

        private static void AddStringListFingerprintFields(
            List<string> fields,
            string marker,
            List<string> values)
        {
            fields.Add(marker ?? string.Empty);
            fields.Add((values?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            for (int index = 0; values != null && index < values.Count; index++)
                fields.Add(values[index] ?? string.Empty);
        }

        private static string CultureFingerprint(MemoryOwnerCultureDto culture)
        {
            if (culture == null) return string.Empty;
            return string.Join("|", culture.originStateToken, culture.originDisplayLabel,
                culture.originProvenanceToken, culture.adoptedStateToken,
                culture.adoptedDisplayLabel);
        }

        private MemoryLibraryPublication ResolveCompleteLibraryPublication(
            Dictionary<string, MemoryLibraryPublication> cache,
            string streamFingerprint,
            string contentFingerprint,
            string ownerKey,
            MemoryLibraryOwnerIndexSnapshot ownerSnapshot,
            long expectedRevision,
            long nextDayBoundary,
            long ttlValidUntilTickExclusive,
            string textContent)
        {
            if (cache == null || expectedRevision < 0) return null;
            if (cache.TryGetValue(streamFingerprint, out MemoryLibraryPublication existing))
            {
                if (expectedRevision > 0)
                    return existing.revision == expectedRevision ? existing : null;
                if (string.Equals(existing.fingerprint, contentFingerprint,
                    StringComparison.Ordinal)) return existing;
                cache.Remove(streamFingerprint);
            }
            if (expectedRevision > 0) return null;

            // One most-recent stream of each kind per cached owner. Eviction makes every positive
            // continuation for the superseded stream Stale without retaining history.
            List<string> superseded = new List<string>();
            foreach (KeyValuePair<string, MemoryLibraryPublication> pair in cache)
                if (string.Equals(pair.Value?.ownerKey, ownerKey, StringComparison.Ordinal))
                    superseded.Add(pair.Key);
            for (int index = 0; index < superseded.Count; index++) cache.Remove(superseded[index]);
            if (!memoryLibraryClock.TryAllocate(out long revision))
            {
                return null;
            }
            MemoryLibraryPublication published = new MemoryLibraryPublication
            {
                fingerprint = contentFingerprint ?? string.Empty,
                revision = revision,
                ownerKey = ownerKey ?? string.Empty,
                ownerSnapshot = ownerSnapshot,
                directoryRevision = memoryLibraryDirectoryRevision,
                committedSettingsRevision = MemoryEffectivePolicyProvider.PublicationRevision,
                languageDisplayRevision = memoryLibraryLanguageDisplayRevision,
                ttlDayRevision = Math.Max(0, Find.TickManager?.TicksGame ?? 0) / 60000L,
                nextDayBoundary = nextDayBoundary,
                ttlValidUntilTickExclusive = ttlValidUntilTickExclusive,
                textContent = textContent ?? string.Empty
            };
            cache[streamFingerprint] = published;
            return published;
        }

        private bool LibraryPublicationExpiredOrSuperseded(
            MemoryLibraryPublication publication)
        {
            if (publication == null) return true;
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return now >= publication.ttlValidUntilTickExclusive
                || publication.committedSettingsRevision
                    != MemoryEffectivePolicyProvider.PublicationRevision
                || !ReferenceEquals(memoryLibraryObservedLanguage, LanguageDatabase.activeLanguage)
                || publication.ttlDayRevision != now / 60000L;
        }

        private string ListQueryFingerprint(
            MemoryLibraryListQuery query,
            MemoryLibraryOwnerRow owner)
        {
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            string search = MemoryLibraryPolicy.NormalizeSearch(
                query.search, limits.searchScalars, limits.searchUtf16Units);
            return MemoryLibraryPolicy.StreamFingerprint(
                OwnerIndexKey(query.primaryHandle),
                OwnerEpochKey(query.activeOwnerEpochKey),
                query.viewTag ?? string.Empty,
                FiltersKey(query.filters),
                search,
                query.sortToken ?? string.Empty,
                Math.Min(query.listCount, limits.libraryWindowRows)
                    .ToString(CultureInfo.InvariantCulture));
        }

        private string DetailQueryFingerprint(MemoryThreadDetailQuery query)
        {
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            return MemoryLibraryPolicy.StreamFingerprint(
                RootHandleKey(query.rootHandle),
                FiltersKey(query.filters),
                MemoryLibraryPolicy.NormalizeSearch(
                    query.search, limits.searchScalars, limits.searchUtf16Units),
                Math.Min(query.detailCount, limits.libraryWindowRows)
                    .ToString(CultureInfo.InvariantCulture));
        }

        private MemoryLibraryLimits BuildMemoryLibraryLimits()
        {
            return new MemoryLibraryLimits
            {
                libraryWindowRows = Math.Max(1, (int)ReadCapacityLong(
                    "libraryWindowRows", 64, MemoryLibraryLimitCeilings.LibraryWindowRows)),
                libraryWindowCeiling = MemoryLibraryLimitCeilings.LibraryWindowRows,
                chapterHeaderRows = Math.Max(1, (int)ReadCapacityLong(
                    "chapterHeaderWindowRows", 32,
                    MemoryLibraryLimitCeilings.ChapterHeaderRows)),
                searchScalars = (int)ReadCapacityTuplePart(
                    "searchQueryBounds", 0, 80, MemoryLibraryLimitCeilings.SearchScalars),
                searchUtf16Units = (int)ReadCapacityTuplePart(
                    "searchQueryBounds", 1, 160, MemoryLibraryLimitCeilings.SearchUtf16Units),
                normalizedFieldUtf16Units = (int)ReadCapacityLong(
                    "normalizedSearchFieldUnits", 120,
                    MemoryLibraryLimitCeilings.NormalizedFieldUtf16Units),
                rowProjectionUtf16Units = (int)ReadCapacityLong(
                    "rowSearchProjectionUnits", 480,
                    MemoryLibraryLimitCeilings.RowProjectionUtf16Units),
                frozenDisplayLabelUtf16Units = (int)ReadCapacityLong(
                    "frozenDisplayLabelUnits", 80,
                    MemoryLibraryLimitCeilings.FrozenDisplayLabelUtf16Units),
                blockTextUtf16Units = (int)ReadCapacityLong(
                    "blockWordingUnits", 240, MemoryLibraryLimitCeilings.BlockTextUtf16Units),
                currentStatusFieldCount = (int)ReadCapacityTuplePart(
                    "currentStatusFieldTextCaps", 0, 4,
                    MemoryLibraryLimitCeilings.CurrentStatusFieldCount),
                currentStatusFieldTextUtf16Units = (int)ReadCapacityTuplePart(
                    "currentStatusFieldTextCaps", 1, 240,
                    MemoryLibraryLimitCeilings.CurrentStatusFieldTextUtf16Units),
                devReasonCount = (int)ReadCapacityTuplePart(
                    "devReasonCountTextCaps", 0, 8,
                    MemoryLibraryLimitCeilings.DevReasonCount),
                devReasonTextUtf16Units = (int)ReadCapacityTuplePart(
                    "devReasonCountTextCaps", 1, 80,
                    MemoryLibraryLimitCeilings.DevReasonTextUtf16Units),
                copyDiagnosticUtf16Units = (int)ReadCapacityLong(
                    "copyDiagnosticUnits", 2000,
                    MemoryLibraryLimitCeilings.CopyDiagnosticUtf16Units),
                importedPreviewUtf16Units = (int)ReadCapacityTuplePart(
                    "importedPreviewChunkUnits", 0, 240,
                    MemoryLibraryLimitCeilings.ImportedPreviewUtf16Units),
                importedSearchScratchUtf16Units = (int)ReadCapacityLong(
                    "importedSearchScratchUnits", 49152,
                    MemoryLibraryLimitCeilings.ImportedSearchScratchUtf16Units),
                importedTextChunkUtf16Units = (int)ReadCapacityTuplePart(
                    "importedPreviewChunkUnits", 1, 1000,
                    MemoryLibraryLimitCeilings.ImportedTextChunkUtf16Units),
                commandEntries = Math.Max(1, (int)ReadCapacityLong(
                    "libraryCommandEntries", 32, MemoryLibraryLimitCeilings.CommandEntries))
            };
        }

        /// <summary>
        /// Returns a detached copy of the XML/capacity-owned Library input limits. The UI snapshots
        /// this once when it opens so search text is normalized before entering session state.
        /// </summary>
        internal MemoryLibraryLimits MemoryLibraryInputLimitsForUi()
        {
            return BuildMemoryLibraryLimits();
        }

        private long NextMemoryLibraryDayBoundary()
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            long day = now / 60000L;
            return day >= long.MaxValue / 60000L - 1
                ? long.MaxValue : (day + 1) * 60000L;
        }

        private static MemoryLibraryListResult InvalidList(string reason)
        {
            return new MemoryLibraryListResult
            {
                status = MemoryLibraryStatuses.Invalid,
                reasonToken = reason ?? string.Empty
            };
        }

        private string ResolveImportedRow(
            MemoryArchiveHandle handle,
            out SavedImportedMemoryRow row,
            out long structuralRevision)
        {
            row = null;
            structuralRevision = 0;
            if (!MemoryLibraryPolicy.ValidArchiveHandle(handle))
                return MemoryLibraryStatuses.Invalid;
            if (handle.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported
                && string.IsNullOrEmpty(handle.exactOwnerPawnIdOrEmpty))
            {
                row = FindImported(unresolvedOwnerArchiveRows, handle.archiveRecordId);
                structuralRevision = unresolvedArchiveStructuralRevision;
                return row != null ? MemoryLibraryStatuses.Ready : MemoryLibraryStatuses.Missing;
            }
            if (handle.archiveScopeToken != MemoryLibraryScopes.Active
                && handle.archiveScopeToken != MemoryLibraryScopes.ArchiveOnly)
                return MemoryLibraryStatuses.Invalid;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(handle.exactOwnerPawnIdOrEmpty);
            if (owner == null) return MemoryLibraryStatuses.Missing;
            string exactScope = owner.archiveOnly
                ? MemoryLibraryScopes.ArchiveOnly : MemoryLibraryScopes.Active;
            if (!string.Equals(handle.archiveScopeToken, exactScope, StringComparison.Ordinal))
                return MemoryLibraryStatuses.Invalid;
            row = FindImported(owner.importedArchiveRows, handle.archiveRecordId);
            structuralRevision = owner.structuralRevision;
            return row != null ? MemoryLibraryStatuses.Ready : MemoryLibraryStatuses.Missing;
        }

        private static SavedImportedMemoryRow FindImported(
            List<SavedImportedMemoryRow> rows,
            string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.archiveRecordId == id) return rows[index];
            return null;
        }

        private static SavedMemoryBlock FindSavedBlock(List<SavedMemoryBlock> rows, string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.recordId == id) return rows[index];
            return null;
        }

        private static SavedMemoryThreadRoot FindSavedRoot(
            List<SavedMemoryThreadRoot> rows,
            string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.rootId == id) return rows[index];
            return null;
        }

        private static bool RootAndRecordHandlesMatch(
            MemoryRootHandle root,
            MemoryRecordHandle record)
        {
            return root != null && record != null
                && !string.IsNullOrWhiteSpace(root.rootId)
                && root.ownerPawnId == record.ownerPawnId
                && root.epochToken == record.epochToken;
        }

        private static void ForgetRootBlock(
            List<SavedMemoryThreadRoot> roots,
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            PawnKnowledgeState owner)
        {
            if (ReferenceEquals(root.rollingSummaryBlock, block)) root.rollingSummaryBlock = null;
            else root.visibleBlocks.Remove(block);
            if (block.summaryRole == MemoryContractTokens.SummaryRoleClosed)
            {
                for (int index = 0; root.chapters != null && index < root.chapters.Count; index++)
                {
                    SavedMemoryChapter chapter = root.chapters[index];
                    if (chapter?.closedSummaryRecordId == block.recordId)
                        chapter.closedSummaryRecordId = string.Empty;
                }
            }
            for (int index = root.chapters.Count - 1; index >= 0; index--)
            {
                SavedMemoryChapter chapter = root.chapters[index];
                bool referenced = false;
                for (int blockIndex = 0; root.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                    if (root.visibleBlocks[blockIndex]?.chapterId == chapter?.chapterId)
                        referenced = true;
                if (!referenced && chapter != null && !chapter.closed)
                    root.chapters.RemoveAt(index);
            }
            if ((root.visibleBlocks == null || root.visibleBlocks.Count == 0)
                && root.rollingSummaryBlock == null) roots.Remove(root);
        }

        private static bool ValidLibraryCommandEnvelope(MemoryLibraryCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.libraryClientToken)
                || command.libraryClientToken.Length > 120 || command.commandId <= 0
                || command.targetStructuralRevision <= 0) return false;
            bool active = command.recordHandle != null;
            bool imported = command.archiveHandle != null;
            return active != imported;
        }

        private static MemoryLibraryCommandResult NewLibraryCommandResult(
            MemoryLibraryCommand command)
        {
            return new MemoryLibraryCommandResult
            {
                libraryClientToken = command?.libraryClientToken ?? string.Empty,
                commandId = command?.commandId ?? 0,
                status = MemoryLibraryCommandStatuses.Invalid
            };
        }

        private MemoryLibraryOwnerRow FindCompatibilityDirectoryRow(
            MemoryLibraryOwnerHandle handle)
        {
            string key = OwnerIndexKey(handle);
            for (int index = 0; index < memoryLibraryDirectory.Count; index++)
                if (OwnerIndexKey(memoryLibraryDirectory[index]?.compatibilityHandle) == key)
                    return memoryLibraryDirectory[index];
            return null;
        }

        private string PawnDiaryRecordName(string ownerId)
        {
            return LookupDiaryByPawnId(ownerId)?.pawnName ?? ownerId ?? string.Empty;
        }

        private static long LatestRootTick(SavedMemoryThreadRoot root)
        {
            long latest = 0;
            for (int index = 0; root?.visibleBlocks != null
                && index < root.visibleBlocks.Count; index++)
                latest = Math.Max(latest, root.visibleBlocks[index]?.originalEventTick ?? 0);
            latest = Math.Max(latest, root?.rollingSummaryBlock?.summaryPayload?.latestSurvivingTick ?? 0);
            return latest;
        }

        private static long ChapterOrdinal(List<MemoryChapterRow> chapters, string chapterId)
        {
            for (int index = 0; chapters != null && index < chapters.Count; index++)
                if (chapters[index]?.chapterId == chapterId) return chapters[index].ordinal;
            return long.MinValue;
        }

        private static int CompareOwnerSnapshots(
            MemoryLibraryOwnerIndexSnapshot left,
            MemoryLibraryOwnerIndexSnapshot right)
        {
            return string.Compare(
                OwnerIndexKey(left?.ownerRow?.primaryHandle)
                    + OwnerIndexKey(left?.ownerRow?.compatibilityHandle),
                OwnerIndexKey(right?.ownerRow?.primaryHandle)
                    + OwnerIndexKey(right?.ownerRow?.compatibilityHandle),
                StringComparison.Ordinal);
        }

        private static string OwnerIndexKey(MemoryLibraryOwnerHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.scopeToken ?? string.Empty,
                handle.exactOwnerPawnIdOrEmpty ?? string.Empty,
                handle.epochTokenOrEmpty ?? string.Empty);
        }

        private static string OwnerEpochKey(MemoryOwnerEpochKey key)
        {
            return key == null ? string.Empty
                : (key.ownerPawnId ?? string.Empty) + "|" + (key.epochToken ?? string.Empty);
        }

        private static string RootHandleKey(MemoryRootHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.ownerPawnId ?? string.Empty,
                handle.epochToken ?? string.Empty,
                handle.rootId ?? string.Empty);
        }

        private static string ArchiveHandleKey(MemoryArchiveHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.archiveScopeToken ?? string.Empty,
                handle.exactOwnerPawnIdOrEmpty ?? string.Empty,
                handle.archiveRecordId ?? string.Empty);
        }

        private static string FiltersKey(MemoryLibraryFilters filters)
        {
            MemoryLibraryFilters value = filters ?? new MemoryLibraryFilters();
            return string.Join("|",
                value.importanceMask.ToString(CultureInfo.InvariantCulture),
                value.categoryMask.ToString(CultureInfo.InvariantCulture),
                value.stateToken ?? string.Empty);
        }

        private static string LibraryCommandKey(string client, long commandId)
        {
            return (client ?? string.Empty) + "|" + commandId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
