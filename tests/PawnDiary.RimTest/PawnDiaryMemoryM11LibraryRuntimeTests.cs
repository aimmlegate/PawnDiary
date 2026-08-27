// PawnDiaryMemoryM11LibraryRuntimeTests.cs — loaded, nonvisual M11 Memory Library repository and
// command-drain coverage. Tests query the same detached DTOs used by IMGUI but never call drawing.
//
// Every owner is disposable. Library commands are proven invisible before the component update
// drain, exact after it, revision-fenced on replay, and removable with one client reset.
using System;
using System.Collections.Generic;
using System.Linq;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded no-create Library views, detail streams, cursors, and commands.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11LibraryRuntimeTests
    {
        /// <summary>
        /// Publishes all three views, pages thread/block/imported detail, and proves queries create no
        /// saved owner state or structural/status mutation.
        /// </summary>
        [Test]
        public static void LibraryPublishesEveryViewWithoutMutatingSavedTruth()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Library " + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_A",
                    "Equal subject label",
                    1);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, pawn, state, display);
                long structuralBefore = state.structuralRevision;
                long statusBefore = state.statusRevision;

                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                Require(owner.primaryHandle != null
                        && owner.primaryHandle.scopeToken == MemoryLibraryScopes.Active
                        && owner.primaryHandle.exactOwnerPawnIdOrEmpty == ownerId
                        && owner.primaryHandle.epochTokenOrEmpty
                            == state.autobiographicalEpochToken
                        && owner.threadCount == 1
                        && owner.standaloneCount == 1
                        && owner.importedCount == 1,
                    "The owner directory lost exact identity or one of the three Memory views.");
                Require(owner.culture != null
                        && owner.culture.originDisplayLabel != null
                        && owner.culture.adoptedDisplayLabel != null,
                    "The Library did not detach a safe culture projection.");

                MemoryLibraryListResult threads =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Threads);
                MemoryLibraryListResult standalone =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Standalone);
                MemoryLibraryListResult imported =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Imported);
                Require(threads.rows.Count == 1
                        && threads.rows[0].tag == MemoryLibraryRowTags.Thread
                        && threads.rows[0].thread != null,
                    "The Threads view did not publish its one exact root.");
                Require(standalone.rows.Count == 1
                        && standalone.rows[0].tag == MemoryLibraryRowTags.Standalone
                        && standalone.rows[0].standalone != null,
                    "The Standalone view did not publish its one exact block.");
                Require(imported.rows.Count == 1
                        && imported.rows[0].tag == MemoryLibraryRowTags.Imported
                        && imported.rows[0].imported != null,
                    "The Imported view did not publish its one inert archive row.");

                MemoryThreadHeaderRow header = threads.rows[0].thread;
                MemoryThreadDetailResult threadDetail = scope.Component.QueryMemoryThreadDetail(
                    new MemoryThreadDetailQuery
                    {
                        rootHandle = header.rootHandle,
                        filters = new MemoryLibraryFilters(),
                        detailStart = 0,
                        detailCount = 64,
                        expectedDetailSnapshotRevision = 0
                    });
                Require(threadDetail.status == MemoryLibraryStatuses.Ready
                        && threadDetail.header != null
                        && threadDetail.chapters.Count == 1
                        && threadDetail.blocks.Count == 1
                        && threadDetail.blocks[0].displayWording.Contains(
                            "M11 relationship wording"),
                    "Thread detail lost its chapter, block, or deterministic wording.");

                MemoryBlockRow standaloneRow = standalone.rows[0].standalone;
                MemoryBlockDetailResult blockDetail = scope.Component.QueryMemoryBlockDetail(
                    new MemoryBlockDetailQuery
                    {
                        recordHandle = standaloneRow.recordHandle,
                        rootHandle = standaloneRow.rootHandle,
                        placementToken = "standalone",
                        targetStructuralRevision = standaloneRow.targetStructuralRevision,
                        projectionToken = "full",
                        filters = new MemoryLibraryFilters()
                    });
                Require(blockDetail.status == MemoryLibraryStatuses.Ready
                        && blockDetail.detail != null
                        && blockDetail.detail.factDescriptors.Count > 0
                        && blockDetail.detail.provenanceDescriptors.Count > 0,
                    "Standalone detail omitted bounded fact/provenance diagnostics.");

                MemoryImportedRow importedRow = imported.rows[0].imported;
                MemoryImportedDetailResult importedDetail =
                    scope.Component.QueryMemoryImportedDetail(
                        new MemoryImportedDetailQuery
                        {
                            archiveHandle = importedRow.archiveHandle,
                            textStart = 0,
                            textCount = 240,
                            expectedArchiveTextSnapshotRevision = 0,
                            targetStructuralRevision = importedRow.targetStructuralRevision
                        });
                Require(importedDetail.status == MemoryLibraryStatuses.Ready
                        && importedDetail.textChunk.Contains("M11 imported wording")
                        && importedDetail.archiveTextSnapshotRevision > 0,
                    "Imported detail did not page the preserved Unicode wording.");

                MemoryLibraryListResult childSearch =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Threads,
                        "relationship wording");
                Require(childSearch.rows.Count == 1,
                    "A child-only search failed to retain its thread context.");
                Require(state.structuralRevision == structuralBefore
                        && state.statusRevision == statusBefore
                        && ReferenceEquals(
                            scope.RequireDiaryRecord(pawn).knowledgeState, state),
                    "A no-create Library query mutated or replaced saved owner truth.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// Suppression and Imported forget execute only on the component drain; stale replay cannot
        /// apply a second mutation and a pinned pre-mutation list cannot remain Ready.
        /// </summary>
        [Test]
        public static void CommandsDrainOutsideDrawAndFenceReplay()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            const string client = "rimtest-memory-library-client";
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Commands " + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_Command",
                    "Command subject",
                    2);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, pawn, state, display);

                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                MemoryLibraryListResult standalone =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Standalone);
                MemoryBlockRow row = standalone.rows.Single().standalone;
                MemoryLibraryListQuery pinned =
                    PawnDiaryMemoryM11RuntimeFixture.PinnedListQuery(
                        owner, standalone, MemoryLibraryViews.Standalone);
                MemoryLibraryCommand suppress = MemoryLibraryUiPolicy.BuildBlockCommand(
                    client,
                    1,
                    MemoryLibraryActions.SetSuppressed,
                    row,
                    true,
                    null);
                Require(suppress != null
                        && scope.Component.TryEnqueueMemoryLibraryCommand(suppress),
                    "The Library rejected a valid detached suppression command.");
                Require(!state.standaloneBlocks.Single().suppressed,
                    "Enqueue mutated saved state during the UI/draw side of the boundary.");

                PawnDiaryMemoryM11RuntimeFixture.DrainLibraryCommands(scope.Component);
                MemoryLibraryCommandResult first;
                Require(scope.Component.TryTakeMemoryLibraryCommandResult(
                            client, 1, out first)
                        && first.status == MemoryLibraryCommandStatuses.Success
                        && state.standaloneBlocks.Single().suppressed,
                    "The component drain did not commit suppression exactly once.");
                long revisionAfterFirst = state.structuralRevision;
                MemoryLibraryListResult stalePinned =
                    scope.Component.QueryMemoryLibraryList(pinned);
                Require(stalePinned.status != MemoryLibraryStatuses.Ready,
                    "A pinned pre-mutation Library list remained Ready after structural change.");

                Require(scope.Component.TryEnqueueMemoryLibraryCommand(suppress),
                    "The Library refused to terminally classify a replayed command envelope.");
                PawnDiaryMemoryM11RuntimeFixture.DrainLibraryCommands(scope.Component);
                MemoryLibraryCommandResult replay;
                Require(scope.Component.TryTakeMemoryLibraryCommandResult(
                            client, 1, out replay)
                        && replay.status == MemoryLibraryCommandStatuses.Stale
                        && state.structuralRevision == revisionAfterFirst
                        && state.standaloneBlocks.Single().suppressed,
                    "A stale replay mutated the block or escaped revision fencing.");

                MemoryLibraryOwnerResult refreshedOwners;
                MemoryLibraryOwnerRow refreshedOwner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out refreshedOwners);
                MemoryLibraryListResult imported =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        refreshedOwner,
                        refreshedOwners.directoryRevision,
                        MemoryLibraryViews.Imported);
                MemoryLibraryCommand forget =
                    MemoryLibraryUiPolicy.BuildImportedForgetCommand(
                        client, 2, imported.rows.Single().imported);
                Require(forget != null
                        && scope.Component.TryEnqueueMemoryLibraryCommand(forget)
                        && state.importedArchiveRows.Count == 1,
                    "Imported Dev Forget did not stage without mutating saved truth.");
                PawnDiaryMemoryM11RuntimeFixture.DrainLibraryCommands(scope.Component);
                MemoryLibraryCommandResult forgotten;
                Require(scope.Component.TryTakeMemoryLibraryCommandResult(
                            client, 2, out forgotten)
                        && forgotten.status == MemoryLibraryCommandStatuses.Success
                        && state.importedArchiveRows.Count == 0,
                    "Imported Dev Forget did not remove exactly its selected archive row.");

                MemoryLibraryCommand abandoned = new MemoryLibraryCommand
                {
                    libraryClientToken = client,
                    commandId = 3,
                    actionToken = MemoryLibraryActions.SetSuppressed,
                    recordHandle = row.recordHandle,
                    placementToken = "standalone",
                    targetStructuralRevision = state.structuralRevision,
                    hasDesiredSuppressed = true,
                    desiredSuppressed = false
                };
                Require(scope.Component.TryEnqueueMemoryLibraryCommand(abandoned),
                    "The client-abandon fixture could not queue its disposable command.");
                scope.Component.AbandonMemoryLibraryClient(client);
                PawnDiaryMemoryM11RuntimeFixture.DrainLibraryCommands(scope.Component);
                MemoryLibraryCommandResult absent;
                Require(!scope.Component.TryTakeMemoryLibraryCommandResult(
                            client, 3, out absent)
                        && state.standaloneBlocks.Single().suppressed,
                    "Closing a Library client failed to prune its unexecuted command.");
            }
            finally
            {
                try
                {
                    scope.Component.AbandonMemoryLibraryClient(client);
                }
                finally
                {
                    PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
                }
            }
        }

        /// <summary>
        /// A detached edit queued while a row is retained must recheck exact TTL when the component
        /// drains it. Otherwise Save Wording could turn an expired row into a permanent edit.
        /// </summary>
        [Test]
        public static void CommandDrainCannotResurrectRowThatExpiredAfterEnqueue()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            MemoryPolicySnapshot priorPolicy = MemoryEffectivePolicyProvider.Current;
            const string client = "rimtest-memory-library-ttl-command";
            try
            {
                // A player may intentionally configure immediate Minor expiry. Publish a retained
                // baseline so this fixture can first enqueue a valid row before crossing its TTL.
                MemorySettingsPolicyFieldsV1 retainedFields = priorPolicy.ToFields();
                retainedFields.minorMemoryLifetimeDays = Math.Max(
                    1, retainedFields.minorMemoryLifetimeDays);
                retainedFields.regularMemoryLifetimeDays = Math.Max(
                    retainedFields.minorMemoryLifetimeDays,
                    retainedFields.regularMemoryLifetimeDays);
                var retainedPolicy = new MemoryPolicySnapshot(
                    MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                    false,
                    retainedFields,
                    "rimtest-command-retained-ttl-" + Guid.NewGuid().ToString("N"));
                Require(MemoryEffectivePolicyProvider.Publish(retainedPolicy),
                    "The TTL command fixture could not publish its retained baseline policy.");

                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 TTL Command "
                    + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_TtlCommand",
                    "TTL command subject",
                    72,
                    extraStandalone: 1);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, pawn, state, display);

                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                MemoryLibraryListResult standalone =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        owner,
                        owners.directoryRevision,
                        MemoryLibraryViews.Standalone);
                MemoryBlockRow row = standalone.rows
                    .Select(item => item.standalone)
                    .Single(item => item.projectedHighestImportanceMask
                        == MemoryLibraryPolicy.ImportanceMinor);
                var draft = new MemoryLibraryUiEditDraft
                {
                    recordHandle = row.recordHandle,
                    rootHandle = row.rootHandle,
                    placementToken = "standalone",
                    targetStructuralRevision = row.targetStructuralRevision,
                    text = "This expired row must not become permanent."
                };
                MemoryLibraryCommand command = MemoryLibraryUiPolicy.BuildBlockCommand(
                    client,
                    1,
                    MemoryLibraryActions.SaveWording,
                    row,
                    false,
                    draft);
                Require(command != null
                        && scope.Component.TryEnqueueMemoryLibraryCommand(command),
                    "The TTL command fixture could not queue its initially valid edit.");

                MemorySettingsPolicyFieldsV1 fields = retainedPolicy.ToFields();
                fields.minorMemoryLifetimeDays = 0;
                var immediateMinorTtl = new MemoryPolicySnapshot(
                    MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                    false,
                    fields,
                    "rimtest-command-immediate-ttl-" + Guid.NewGuid().ToString("N"));
                Require(MemoryEffectivePolicyProvider.Publish(immediateMinorTtl),
                    "The TTL command fixture could not publish its boundary policy.");

                long structuralBefore = state.structuralRevision;
                SavedMemoryBlock saved = state.standaloneBlocks.Single(
                    block => block.recordId == row.recordHandle.recordId);
                PawnDiaryMemoryM11RuntimeFixture.DrainLibraryCommands(scope.Component);
                MemoryLibraryCommandResult result;
                Require(scope.Component.TryTakeMemoryLibraryCommandResult(
                            client, 1, out result)
                        && result.status == MemoryLibraryCommandStatuses.Missing
                        && !saved.playerEdited
                        && string.IsNullOrEmpty(saved.playerWording)
                        && state.structuralRevision == structuralBefore,
                    "A command drained after expiry resurrected or mutated the saved row.");
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(priorPolicy);
                try
                {
                    scope.Component.AbandonMemoryLibraryClient(client);
                }
                finally
                {
                    PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
                }
            }
        }

        /// <summary>
        /// A pressure-only maintenance commit is still a saved-memory mutation and must invalidate
        /// an already-published list even when no reducer work changed an owner first.
        /// </summary>
        [Test]
        public static void PressureOnlyMaintenanceInvalidatesWarmLibraryPublication()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Pressure Cache "
                    + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_PressureCache",
                    "Pressure cache subject",
                    72,
                    extraStandalone: 128);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(scope, pawn, state, display);
                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                MemoryLibraryListResult warm = PawnDiaryMemoryM11RuntimeFixture.RequireList(
                    scope.Component,
                    owner,
                    owners.directoryRevision,
                    MemoryLibraryViews.Standalone);
                MemoryLibraryListQuery pinned =
                    PawnDiaryMemoryM11RuntimeFixture.PinnedListQuery(
                        owner, warm, MemoryLibraryViews.Standalone);
                int before = state.standaloneBlocks.Count;

                Require(PawnDiaryMemoryM11RuntimeFixture.CompleteMaintenancePressure(
                            scope.Component)
                        && state.standaloneBlocks.Count < before,
                    "The pressure-only fixture did not evict an eligible over-cap memory.");
                Require(scope.Component.QueryMemoryLibraryList(pinned).status
                        != MemoryLibraryStatuses.Ready,
                    "A warm Library list survived a pressure-only saved mutation.");

                MemoryLibraryOwnerResult refreshedOwners;
                MemoryLibraryOwnerRow refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out refreshedOwners);
                Require(refreshed.standaloneCount == state.standaloneBlocks.Count,
                    "The rebuilt Library owner count did not reflect pressure eviction.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// A durable factual admission can occur without a diary page. The authoritative M4 commit
        /// must still invalidate a warm detached Library publication immediately.
        /// </summary>
        [Test]
        public static void PageLessAdmissionInvalidatesWarmLibraryPublication()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Admission Cache "
                    + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_AdmissionCache",
                    "Admission cache subject",
                    74);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(scope, pawn, state, display);
                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                MemoryLibraryListResult warm = PawnDiaryMemoryM11RuntimeFixture.RequireList(
                    scope.Component,
                    owner,
                    owners.directoryRevision,
                    MemoryLibraryViews.Standalone);
                MemoryLibraryListQuery pinned =
                    PawnDiaryMemoryM11RuntimeFixture.PinnedListQuery(
                        owner, warm, MemoryLibraryViews.Standalone);
                int before = state.standaloneBlocks.Count;
                SavedMemoryBlock block =
                    PawnDiaryMemoryM11RuntimeFixture.BuildStandaloneAdmissionBlock(
                        ownerId, state.autobiographicalEpochToken, "library-cache");

                MemoryStoreAdmissionResult admitted = scope.Component.TryAdmitMemoryBlock(
                    new MemoryStoreAdmissionRequest
                    {
                        ownerPawnId = ownerId,
                        ownerEpochToken = state.autobiographicalEpochToken,
                        expectedOwnerStructuralRevision = state.structuralRevision,
                        expectedIndexGeneration = -1,
                        routeReliable = false,
                        chapterPhaseToken = "fixture",
                        chapterDirective = MemoryChapterDirectiveTokens.ContinueCurrent,
                        nowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                        block = block
                    });
                Require(admitted.outcome == MemoryStoreMutationOutcome.Admitted
                        && state.standaloneBlocks.Count == before + 1,
                    "The page-less cache fixture could not commit its factual block.");
                Require(scope.Component.QueryMemoryLibraryList(pinned).status
                        != MemoryLibraryStatuses.Ready,
                    "A warm Library list survived a page-less durable factual admission.");
                MemoryLibraryOwnerResult invalidatedDirectory =
                    scope.Component.QueryMemoryLibraryOwners(new MemoryLibraryOwnerQuery
                    {
                        search = display,
                        sortToken = "name",
                        start = 0,
                        count = 64,
                        expectedDirectoryRevision = 0
                    });
                Require(invalidatedDirectory.status == MemoryLibraryStatuses.Preparing,
                    "A direct no-client owner query returned Ready over a cleared directory.");

                MemoryLibraryOwnerResult refreshedOwners;
                MemoryLibraryOwnerRow refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out refreshedOwners);
                Require(refreshed.standaloneCount == state.standaloneBlocks.Count,
                    "The rebuilt Library owner count omitted the page-less admission.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// The admission pressure fallback returns before the ordinary commit tail. Its atomic
        /// eviction-plus-admission must still invalidate a list published before the attempt.
        /// </summary>
        [Test]
        public static void PressureFallbackAdmissionInvalidatesWarmLibraryPublication()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Pressure Admission Cache "
                    + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_PressureAdmissionCache",
                    "Pressure admission cache subject",
                    75,
                    extraStandalone: 128);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(scope, pawn, state, display);
                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                MemoryLibraryListResult warm = PawnDiaryMemoryM11RuntimeFixture.RequireList(
                    scope.Component,
                    owner,
                    owners.directoryRevision,
                    MemoryLibraryViews.Standalone);
                MemoryLibraryListQuery pinned =
                    PawnDiaryMemoryM11RuntimeFixture.PinnedListQuery(
                        owner, warm, MemoryLibraryViews.Standalone);
                int before = state.standaloneBlocks.Count;
                SavedMemoryBlock block =
                    PawnDiaryMemoryM11RuntimeFixture.BuildStandaloneAdmissionBlock(
                        ownerId, state.autobiographicalEpochToken, "pressure-library-cache");

                object observationBudget =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(scope.Component);
                MemoryStoreAdmissionResult admitted;
                PawnDiaryMemoryM11RuntimeFixture.SetActiveObservationAdmissionBudget(
                    scope.Component, observationBudget);
                try
                {
                    admitted = scope.Component.TryAdmitMemoryBlock(
                        new MemoryStoreAdmissionRequest
                        {
                            ownerPawnId = ownerId,
                            ownerEpochToken = state.autobiographicalEpochToken,
                            expectedOwnerStructuralRevision = state.structuralRevision,
                            expectedIndexGeneration = -1,
                            routeReliable = false,
                            chapterPhaseToken = "fixture",
                            chapterDirective = MemoryChapterDirectiveTokens.ContinueCurrent,
                            nowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                            block = block
                        });
                }
                finally
                {
                    PawnDiaryMemoryM11RuntimeFixture.SetActiveObservationAdmissionBudget(
                        scope.Component, null);
                }
                Require(admitted.outcome == MemoryStoreMutationOutcome.Admitted
                        && state.standaloneBlocks.Any(
                            candidate => candidate?.recordId == block.recordId)
                        && state.standaloneBlocks.Count < before + 1,
                    "The pressure fixture did not use atomic eviction-plus-admission.");
                DiaryGameComponent.MemoryOwnerByteTotals sessionOwner =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(
                        observationBudget, ownerId);
                DiaryGameComponent.MemoryOwnerByteTotals publishedOwner =
                    scope.Component.GetOwnerByteTotals(ownerId);
                MemoryPayloadBudgetTotals sessionGlobal =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(observationBudget);
                MemoryPayloadBudgetTotals publishedGlobal =
                    scope.Component.GetGlobalBudgetTotals();
                Require(sessionOwner.valid && publishedOwner.valid
                        && sessionOwner.activeBytes == publishedOwner.activeBytes
                        && sessionOwner.importedBytes == publishedOwner.importedBytes
                        && sessionGlobal.globalActiveBytes == publishedGlobal.globalActiveBytes
                        && sessionGlobal.globalImportedBytes
                            == publishedGlobal.globalImportedBytes,
                    "Pressure admission left the active observation budget stale.");
                Require(scope.Component.QueryMemoryLibraryList(pinned).status
                        != MemoryLibraryStatuses.Ready,
                    "A warm Library list survived the admission pressure early-return branch.");

                MemoryLibraryOwnerResult refreshedOwners;
                MemoryLibraryOwnerRow refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out refreshedOwners);
                Require(refreshed.standaloneCount == state.standaloneBlocks.Count,
                    "The rebuilt Library owner count omitted pressure admission changes.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// Legacy retention can delete raw rows without touching current-schema reducers. That
        /// eviction must stale a pinned compatibility payload and rebuild it from the surviving row.
        /// </summary>
        [Test]
        public static void LegacyEvictionInvalidatesWarmCompatibilityPublication()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryKnowledgeTuningDef tuning =
                DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail("Diary_Knowledge");
            Require(tuning != null, "The legacy eviction fixture requires Diary_Knowledge.");
            int priorPawnCap = tuning.maxRecordsPerPawn;
            int priorGlobalCap = tuning.maxRecordsGlobal;
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 Legacy Eviction Cache "
                    + Guid.NewGuid().ToString("N");
                var state = new PawnKnowledgeState
                {
                    pawnId = ownerId,
                    schemaVersion = 1,
                    originCultureDefName = "PawnDiary_RimTest_LegacyCulture",
                    originCultureSource = "rimtest"
                };
                state.records.Add(new ImportantMemoryRecord
                {
                    recordId = "rimtest-legacy-eviction-old",
                    dedupKey = "rimtest-legacy-eviction-old",
                    sourceEventId = "rimtest-legacy-eviction-event-old",
                    sourceKind = KnowledgeTokens.SourceKindCaptured,
                    recallScope = KnowledgeTokens.RecallScopeContextual,
                    eventKind = "relation.spouse.gained",
                    tick = 1,
                    fallbackSummary = "Old disposable legacy row."
                });
                state.records.Add(new ImportantMemoryRecord
                {
                    recordId = "rimtest-legacy-eviction-new",
                    dedupKey = "rimtest-legacy-eviction-new",
                    sourceEventId = "rimtest-legacy-eviction-event-new",
                    sourceKind = KnowledgeTokens.SourceKindCaptured,
                    recallScope = KnowledgeTokens.RecallScopeContextual,
                    eventKind = "relation.spouse.gained",
                    tick = 2,
                    fallbackSummary = "New surviving legacy row."
                });
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(scope, pawn, state, display);
                MemoryLibraryOwnerResult warmOwners;
                MemoryLibraryOwnerRow warmOwner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out warmOwners);
                var pinned = new MemoryCompatibilityQuery
                {
                    compatibilityHandle = warmOwner.compatibilityHandle,
                    sourcePayloadRevision = warmOwner.compatibilitySourcePayloadRevision
                };
                MemoryCompatibilityResult warm =
                    scope.Component.QueryMemoryCompatibility(pinned);
                Require(warm.status == MemoryLibraryStatuses.Ready
                        && warm.pending != null
                        && warm.pending.rowCount == 2,
                    "The fixture did not publish both raw legacy rows before eviction.");

                tuning.maxRecordsPerPawn = 1;
                tuning.maxRecordsGlobal = Math.Max(2, priorGlobalCap);
                PawnDiaryMemoryM11RuntimeFixture.ApplyLegacyKnowledgeEviction(scope.Component);

                Require(state.records.Count == 1
                        && state.records[0].recordId == "rimtest-legacy-eviction-new",
                    "Legacy retention did not evict exactly the oldest disposable row.");
                Require(scope.Component.QueryMemoryCompatibility(pinned).status
                        != MemoryLibraryStatuses.Ready,
                    "A warm legacy compatibility payload survived authoritative eviction.");

                MemoryLibraryOwnerResult refreshedOwners;
                MemoryLibraryOwnerRow refreshedOwner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out refreshedOwners);
                MemoryCompatibilityResult refreshed =
                    scope.Component.QueryMemoryCompatibility(new MemoryCompatibilityQuery
                    {
                        compatibilityHandle = refreshedOwner.compatibilityHandle,
                        sourcePayloadRevision =
                            refreshedOwner.compatibilitySourcePayloadRevision
                    });
                Require(refreshed.status == MemoryLibraryStatuses.Ready
                        && refreshed.pending != null
                        && refreshed.pending.rowCount == 1
                        && refreshed.sourcePayloadRevision
                            != warm.sourcePayloadRevision,
                    "The rebuilt compatibility payload did not reflect legacy eviction.");
            }
            finally
            {
                tuning.maxRecordsPerPawn = priorPawnCap;
                tuning.maxRecordsGlobal = priorGlobalCap;
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// Rebuilding a detached owner after a TTL boundary excludes already-due ordinary rows and
        /// expired Summary contributions instead of converting their deadlines to never-expire.
        /// </summary>
        [Test]
        public static void LibraryRebuildDoesNotResurrectExpiredSavedRows()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            MemoryPolicySnapshot priorPolicy = MemoryEffectivePolicyProvider.Current;
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                string display = "PawnDiary M11 TTL Rebuild "
                    + Guid.NewGuid().ToString("N");
                PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    ownerId,
                    "PawnDiary_Exact_Subject_TtlRebuild",
                    "TTL rebuild subject",
                    73,
                    extraStandalone: 2);
                SavedMemoryBlock summary = state.threadRoots.Single().visibleBlocks.Single();
                summary.kind = MemoryContractTokens.KindSummary;
                summary.summaryRole = MemoryContractTokens.SummaryRoleClosed;
                summary.summaryPayload = new SavedMemorySummaryPayload
                {
                    reducerRevision = 1,
                    factsRevision = 1,
                    canonicalFactsFingerprint = new string('b', 64),
                    derivedCategoryMask = MemoryCategoryBits.Personal
                        | MemoryCategoryBits.Relationships,
                    highestSurvivingImportance = MemoryContractTokens.ImportanceImportant,
                    deterministicWording = "expired summary value; surviving summary value"
                };
                SavedMemoryFactBucket bucket = new SavedMemoryFactBucket
                {
                    bucketKey = "ttl-rebuild-bucket",
                    factKind = "relationship_phase",
                    aggregationToken = "ordinal_set",
                    derivedCount = 2
                };
                bucket.contributions.Add(new SavedMemoryFactContribution
                {
                    contributionId = "ttl-expired",
                    originRecordId = "ttl-origin-expired",
                    originFactOrdinal = 0,
                    originFactId = "ttl-fact-expired",
                    originalEventTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                    category = MemoryContractTokens.CategoryPersonal,
                    importance = MemoryContractTokens.ImportanceMinor,
                    canonicalValue = "expired summary value"
                });
                bucket.contributions.Add(new SavedMemoryFactContribution
                {
                    contributionId = "ttl-survives",
                    originRecordId = "ttl-origin-survives",
                    originFactOrdinal = 1,
                    originFactId = "ttl-fact-survives",
                    originalEventTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                    category = MemoryContractTokens.CategoryRelationships,
                    importance = MemoryContractTokens.ImportanceImportant,
                    canonicalValue = "surviving summary value"
                });
                summary.summaryPayload.factBuckets.Add(bucket);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(scope, pawn, state, display);

                MemoryLibraryOwnerResult warmOwners;
                MemoryLibraryOwnerRow warmOwner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out warmOwners);
                PawnDiaryMemoryM11RuntimeFixture.RequireList(
                    scope.Component,
                    warmOwner,
                    warmOwners.directoryRevision,
                    MemoryLibraryViews.Standalone);

                MemorySettingsPolicyFieldsV1 fields = priorPolicy.ToFields();
                fields.minorMemoryLifetimeDays = 0;
                fields.regularMemoryLifetimeDays = 0;
                var immediateTtl = new MemoryPolicySnapshot(
                    MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                    false,
                    fields,
                    "rimtest-immediate-ttl-" + Guid.NewGuid().ToString("N"));
                Require(MemoryEffectivePolicyProvider.Publish(immediateTtl),
                    "The TTL rebuild fixture could not publish its isolated exact-boundary policy.");

                MemoryLibraryOwnerResult rebuiltOwners;
                MemoryLibraryOwnerRow rebuiltOwner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out rebuiltOwners);
                MemoryLibraryListResult standalone =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        rebuiltOwner,
                        rebuiltOwners.directoryRevision,
                        MemoryLibraryViews.Standalone);
                Require(standalone.rows.Count == 1
                        && standalone.rows.Single().standalone.projectedHighestImportanceMask
                            == MemoryLibraryPolicy.ImportanceImportant,
                    "Expired ordinary rows reappeared in the rebuilt owner snapshot.");
                MemoryLibraryListResult threads =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        rebuiltOwner,
                        rebuiltOwners.directoryRevision,
                        MemoryLibraryViews.Threads);
                MemoryThreadDetailResult detail = scope.Component.QueryMemoryThreadDetail(
                    new MemoryThreadDetailQuery
                    {
                        rootHandle = threads.rows.Single().thread.rootHandle,
                        filters = new MemoryLibraryFilters(),
                        detailStart = 0,
                        detailCount = 64,
                        expectedDetailSnapshotRevision = 0
                    });
                Require(detail.status == MemoryLibraryStatuses.Ready
                        && detail.blocks.Count == 1
                        && detail.blocks[0].summaryContributions.Count == 1
                        && !detail.blocks[0].canSaveWording
                        && detail.blocks[0].displayWording.Contains("surviving summary value")
                        && !detail.blocks[0].displayWording.Contains("expired summary value")
                        && detail.blocks[0].normalizedSearch.Contains(
                            "SURVIVING SUMMARY VALUE")
                        && detail.blocks[0].normalizedWholeSearch.Contains(
                            "SURVIVING SUMMARY VALUE")
                        && !detail.blocks[0].normalizedSearch.Contains("EXPIRED SUMMARY VALUE")
                        && !detail.blocks[0].normalizedWholeSearch.Contains(
                            "EXPIRED SUMMARY VALUE"),
                    "Expired Summary contributions survived a detached TTL rebuild.");
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(priorPolicy);
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}
