// PawnDiaryMemoryM11LibraryRuntimeTests.cs — loaded, nonvisual M11 Memory Library repository and
// command-drain coverage. Tests query the same detached DTOs used by IMGUI but never call drawing.
//
// Every owner is disposable. Library commands are proven invisible before the component update
// drain, exact after it, revision-fenced on replay, and removable with one client reset.
using System;
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

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}
