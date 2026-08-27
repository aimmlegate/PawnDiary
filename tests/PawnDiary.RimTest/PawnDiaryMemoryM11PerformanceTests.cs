// PawnDiaryMemoryM11PerformanceTests.cs — loaded Mono/Scribe smoke measurements for the frozen
// M11 thread-target matrix N=4/12/64 and its friend-only transient policy scope.
//
// This is a regression smoke, not the authenticated release-vector selection harness described in
// the design plan. It asserts deterministic bytes/shape and logs elapsed/allocation observations;
// timing is bounded only by a generous hang guard so ordinary developer hardware is not flaky.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded Scribe/logical-size smoke at every product-owned thread target.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11PerformanceTests
    {
        /// <summary>
        /// Each N profile survives two real Scribe loads with identical logical bytes; size grows
        /// monotonically, measurements remain finite, and the fixture policy always restores.
        /// </summary>
        [Test]
        public static void LoadedScribeSmokeMatrixCoversN4N12N64()
        {
            int[] targets = { 4, 12, 64 };
            long priorLogicalBytes = -1;
            long priorXmlBytes = -1;
            Require(!MemoryPerformanceFixturePolicy.Active
                    && string.IsNullOrEmpty(MemoryPerformanceFixturePolicy.ScopeTag),
                "A prior benchmark leaked its friend-only policy scope.");

            for (int index = 0; index < targets.Length; index++)
            {
                int target = targets[index];
                string tag = "m11-loaded-smoke-n" + target;
                MemoryPerformanceFixturePolicyScope policyScope =
                    new MemoryPerformanceFixturePolicyScope(tag);
                try
                {
                    Require(MemoryPerformanceFixturePolicy.Active
                            && MemoryPerformanceFixturePolicy.ScopeTag == tag,
                        "The friend-only performance scope did not publish its exact tag.");
                    bool nestedRejected = false;
                    try
                    {
                        new MemoryPerformanceFixturePolicyScope("illegal-nested");
                    }
                    catch (InvalidOperationException)
                    {
                        nestedRejected = true;
                    }
                    Require(nestedRejected,
                        "The nonreentrant performance-policy boundary accepted a nested override.");

                    PawnKnowledgeState state =
                        PawnDiaryMemoryM11RuntimeFixture.BuildThreadTargetOwner(
                            "Pawn_Performance_" + target,
                            target);
                    MemoryLogicalSizeResult sourceSize =
                        MemoryLogicalPayloadSizer.Size(state);
                    Require(sourceSize.valid && sourceSize.totalBytes > 0,
                        "Logical sizing failed for N=" + target + ": "
                            + sourceSize.errorPath);

                    long allocationBefore = GC.GetTotalMemory(false);
                    Stopwatch elapsed = Stopwatch.StartNew();
                    ScribeMeasurement first = RoundTrip(state, target, 1);
                    ScribeMeasurement second = RoundTrip(first.loaded, target, 2);
                    elapsed.Stop();
                    long allocationAfter = GC.GetTotalMemory(false);
                    long allocationDelta = Math.Max(0, allocationAfter - allocationBefore);
                    Require(first.logicalBytes == sourceSize.totalBytes
                            && second.logicalBytes == first.logicalBytes
                            && second.ownerEpoch == first.ownerEpoch
                            && first.rootCount == target
                            && second.rootCount == first.rootCount
                            && first.blockCount == target
                            && second.blockCount == first.blockCount
                            && first.xmlBytes > 0
                            && second.xmlBytes == first.xmlBytes,
                        "N=" + target
                            + " changed canonical shape/bytes across the second Scribe load.");
                    Require(sourceSize.totalBytes > priorLogicalBytes
                            && first.xmlBytes > priorXmlBytes,
                        "Increasing N did not increase both logical and exact XML bytes.");
                    Require(elapsed.Elapsed < TimeSpan.FromSeconds(30),
                        "N=" + target + " exceeded the loaded Scribe smoke hang guard.");

                    Log.Message("[Pawn Diary RimTest] Memory M11 performance smoke"
                        + " N=" + target
                        + " logicalBytes=" + sourceSize.totalBytes
                        + " xmlBytes=" + first.xmlBytes
                        + " elapsedMs=" + elapsed.ElapsedMilliseconds
                        + " observedHeapDelta=" + allocationDelta);
                    priorLogicalBytes = sourceSize.totalBytes;
                    priorXmlBytes = first.xmlBytes;
                }
                finally
                {
                    policyScope.Dispose();
                }
                Require(!MemoryPerformanceFixturePolicy.Active
                        && string.IsNullOrEmpty(MemoryPerformanceFixturePolicy.ScopeTag),
                    "The performance-policy scope did not restore after N=" + target + ".");
            }
        }

        /// <summary>
        /// The friend-only policy scope must move real production settings and capacity readers,
        /// then restore the ordinary publication and XML vector without leaking across cells.
        /// </summary>
        [Test]
        public static void PerformancePolicyScopeMovesProductionBoundariesAndRestores()
        {
            MemoryPolicySnapshot releasePolicy = MemoryEffectivePolicyProvider.Current;
            int releaseOwnerCap = PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerCap();
            MemoryPolicySnapshot benchmarkPolicy = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                MemorySettingsPolicyFieldsV1.CreateBenchmarkProfile(4),
                new MemorySettingsBounds());
            var capacityVector = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ownerSlotTriple"] = "7/8/9",
                ["activeOwnerBytes"] = "8192",
                ["combinedOwnerBytes"] = "16384",
                ["activeGlobalBytes"] = "32768",
                ["combinedGlobalBytes"] = "65536"
            };

            using (new MemoryPerformanceFixturePolicyScope(
                "m11-production-policy-override", capacityVector, benchmarkPolicy))
            {
                Require(ReferenceEquals(MemoryEffectivePolicyProvider.Current, benchmarkPolicy)
                        && MemoryEffectivePolicyProvider.Current.memoryThreadTarget == 4,
                    "The benchmark settings publication did not reach production consumers.");
                Require(PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerCap() == 7,
                    "The benchmark capacity vector did not reach the tuple reader.");

                PawnDiaryRimTestScope componentScope = PawnDiaryRimTestScope.Begin();
                try
                {
                    object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(
                        componentScope.Component);
                    MemoryBudgetLimits limits =
                        PawnDiaryMemoryM11RuntimeFixture.ObservationBudgetLimits(budget);
                    Require(limits.activeOwnerBytes == 8192
                            && limits.combinedOwnerBytes == 16384
                            && limits.activeGlobalBytes == 32768
                            && limits.combinedGlobalBytes == 65536,
                        "The benchmark byte ceilings did not reach the production budget.");
                }
                finally
                {
                    componentScope.TearDown();
                }
            }

            Require(ReferenceEquals(MemoryEffectivePolicyProvider.Current, releasePolicy)
                    && PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerCap() == releaseOwnerCap
                    && !MemoryPerformanceFixturePolicy.Active,
                "The benchmark policy did not restore the complete release policy.");
        }

        /// <summary>
        /// First observation of a diary with no envelope must attach and fast-publish an exact M4
        /// owner. The immediately following factual admission may not fail MigrationPending, and
        /// incremental byte totals must equal a defensive full rebuild.
        /// </summary>
        [Test]
        public static void ObservationEnrollmentFastPublishKeepsM4AndBytesExact()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            var allocator = new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                diary.knowledgeState = null;
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                object enrollment = PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                    component, pawn, budget);
                Require(enrollment != null
                        && PawnDiaryMemoryM11RuntimeFixture.ApplyObservationBaseline(
                            component, enrollment, budget, "fast-publish"),
                    "The production observation path did not enroll its blank diary.");

                PawnKnowledgeState state = diary.knowledgeState;
                Require(state != null
                        && !string.IsNullOrEmpty(state.autobiographicalEpochToken),
                    "Observation enrollment did not publish its current owner epoch.");
                SavedMemoryBlock block =
                    PawnDiaryMemoryM11RuntimeFixture.BuildStandaloneAdmissionBlock(
                        state.pawnId,
                        state.autobiographicalEpochToken,
                        "fast-publish");
                MemoryStoreAdmissionResult admitted;
                PawnDiaryMemoryM11RuntimeFixture.SetActiveObservationAdmissionBudget(
                    component, budget);
                try
                {
                    admitted = component.TryAdmitMemoryBlock(
                        new MemoryStoreAdmissionRequest
                        {
                            ownerPawnId = state.pawnId,
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
                        component, null);
                }
                PawnDiaryMemoryM11RuntimeFixture.CompleteObservationTick(component, budget);
                Require(admitted.outcome == MemoryStoreMutationOutcome.Admitted
                        && component.FindStandaloneMemoryExact(
                            state.pawnId,
                            state.autobiographicalEpochToken,
                            block.recordId) != null,
                    "Post-observation M4 admission was missing, stale, or MigrationPending.");

                DiaryGameComponent.MemoryOwnerByteTotals incrementalOwner =
                    component.GetOwnerByteTotals(state.pawnId);
                MemoryPayloadBudgetTotals incrementalGlobal = component.GetGlobalBudgetTotals();
                component.RebuildMemorySizeIndexes();
                DiaryGameComponent.MemoryOwnerByteTotals rebuiltOwner =
                    component.GetOwnerByteTotals(state.pawnId);
                MemoryPayloadBudgetTotals rebuiltGlobal = component.GetGlobalBudgetTotals();
                Require(incrementalOwner.valid && rebuiltOwner.valid
                        && incrementalOwner.activeBytes == rebuiltOwner.activeBytes
                        && incrementalOwner.importedBytes == rebuiltOwner.importedBytes
                        && incrementalGlobal.globalActiveBytes
                            == rebuiltGlobal.globalActiveBytes
                        && incrementalGlobal.globalImportedBytes
                            == rebuiltGlobal.globalImportedBytes,
                    "Observation fast publication diverged from a full byte-index rebuild.");
            }
            finally
            {
                scope.TearDown();
                allocator.Restore(component);
            }
        }

        /// <summary>
        /// Existing current envelopes with blank epochs consume active-owner slots when observation
        /// enrolls them; a second blank owner cannot reuse the same final slot in one session.
        /// </summary>
        [Test]
        public static void ObservationBlankEpochEnrollmentConsumesSharedOwnerSlot()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            var allocator = new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
            try
            {
                Pawn firstPawn = scope.CreateAdultColonist();
                Pawn secondPawn = scope.CreateAdultColonist();
                PawnDiaryRecord firstDiary = scope.RequireDiaryRecord(firstPawn);
                PawnDiaryRecord secondDiary = scope.RequireDiaryRecord(secondPawn);
                firstDiary.knowledgeState = PawnKnowledgeState.CreateCurrent(
                    firstPawn.GetUniqueLoadID());
                secondDiary.knowledgeState = PawnKnowledgeState.CreateCurrent(
                    secondPawn.GetUniqueLoadID());
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                int ownerCap = PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerCap();
                PawnDiaryMemoryM11RuntimeFixture.ObservationActiveOwnerCount(
                    budget, ownerCap - 1);
                object firstEnrollment =
                    PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                        component, firstPawn, budget);
                Require(firstEnrollment != null
                        && PawnDiaryMemoryM11RuntimeFixture.ApplyObservationBaseline(
                            component, firstEnrollment, budget, "blank-cap-a")
                        && PawnDiaryMemoryM11RuntimeFixture.ObservationActiveOwnerCount(budget)
                            == ownerCap,
                    "Blank-current enrollment did not consume the final owner slot.");
                object refused = PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                    component, secondPawn, budget);
                Require(refused == null
                        && string.IsNullOrEmpty(
                            secondDiary.knowledgeState.autobiographicalEpochToken),
                    "A second blank-current owner reused an already-consumed slot.");
                PawnDiaryMemoryM11RuntimeFixture.CompleteObservationTick(component, budget);
            }
            finally
            {
                scope.TearDown();
                allocator.Restore(component);
            }
        }

        /// <summary>
        /// Observation must never populate or clean lifecycle-only envelopes. Archive owners are
        /// immutable, and an empty Brainwipe fence belongs exclusively to the completion Landmark.
        /// </summary>
        [Test]
        public static void ObservationLeavesBrainwipeFencesAndArchivesUntouched()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            try
            {
                Pawn fencedPawn = scope.CreateAdultColonist();
                Pawn archivedPawn = scope.CreateAdultColonist();
                PawnKnowledgeState fenced = PawnKnowledgeState.CreateCurrent(
                    fencedPawn.GetUniqueLoadID());
                fenced.autobiographicalEpochToken =
                    PawnDiaryMemoryM11RuntimeFixture.EpochToken(8301);
                fenced.epochFenceOnly = true;
                PawnKnowledgeState archived = PawnKnowledgeState.CreateCurrent(
                    archivedPawn.GetUniqueLoadID());
                archived.autobiographicalEpochToken =
                    PawnDiaryMemoryM11RuntimeFixture.EpochToken(8302);
                archived.archiveOnly = true;
                fenced.ownerAwarenessSnapshots.Add(RelativeAwareness("fence-subject"));
                archived.ownerAwarenessSnapshots.Add(RelativeAwareness("archive-subject"));
                scope.RequireDiaryRecord(fencedPawn).knowledgeState = fenced;
                scope.RequireDiaryRecord(archivedPawn).knowledgeState = archived;
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                long fencedStatus = fenced.statusRevision;
                long archivedStatus = archived.statusRevision;
                Require(PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                            component, fencedPawn, budget) == null
                        && PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                            component, archivedPawn, budget) == null,
                    "Observation enrolled a Brainwipe fence or archive-only envelope.");
                PawnDiaryMemoryM11RuntimeFixture.RemoveRelativeObservation(
                    component, fenced.pawnId, "fence-subject", budget);
                PawnDiaryMemoryM11RuntimeFixture.RemoveRelativeObservation(
                    component, archived.pawnId, "archive-subject", budget);
                Require(fenced.epochFenceOnly && !fenced.archiveOnly
                        && archived.archiveOnly && !archived.epochFenceOnly
                        && fenced.ownerAwarenessSnapshots.Count == 1
                        && archived.ownerAwarenessSnapshots.Count == 1
                        && fenced.statusRevision == fencedStatus
                        && archived.statusRevision == archivedStatus,
                    "Observation mutated lifecycle-only saved truth through a cleanup path.");
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>
        /// Awareness cleanup is a beneficial shrink even when loaded totals begin above a current
        /// cap; both owner and global running totals must advance by the exact saved-byte delta.
        /// </summary>
        [Test]
        public static void ObservationAwarenessShrinkSucceedsAboveCurrentGlobalCap()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(pawn.GetUniqueLoadID());
                state.autobiographicalEpochToken =
                    PawnDiaryMemoryM11RuntimeFixture.EpochToken(8303);
                state.ownerAwarenessSnapshots.Add(RelativeAwareness("shrink-subject"));
                scope.RequireDiaryRecord(pawn).knowledgeState = state;
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                MemoryLogicalSizeResult sizeBefore = MemoryLogicalPayloadSizer.Size(state);
                DiaryGameComponent.MemoryOwnerByteTotals ownerBefore =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(
                        budget, state.pawnId);
                MemoryPayloadBudgetTotals globalBefore =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                MemoryBudgetLimits limits =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationBudgetLimits(budget);
                globalBefore.globalActiveBytes = checked(limits.activeGlobalBytes + 4096);
                PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget, globalBefore);

                PawnDiaryMemoryM11RuntimeFixture.RemoveRelativeObservation(
                    component, state.pawnId, "shrink-subject", budget);

                MemoryLogicalSizeResult sizeAfter = MemoryLogicalPayloadSizer.Size(state);
                DiaryGameComponent.MemoryOwnerByteTotals ownerAfter =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(
                        budget, state.pawnId);
                MemoryPayloadBudgetTotals globalAfter =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                long exactShrink = checked(sizeBefore.totalBytes - sizeAfter.totalBytes);
                Require(sizeBefore.valid && sizeAfter.valid && exactShrink > 0
                        && state.ownerAwarenessSnapshots.Count == 0
                        && ownerBefore.activeBytes - ownerAfter.activeBytes == exactShrink
                        && globalBefore.globalActiveBytes - globalAfter.globalActiveBytes
                            == exactShrink
                        && ownerBefore.importedBytes == ownerAfter.importedBytes
                        && globalBefore.globalImportedBytes == globalAfter.globalImportedBytes
                        && globalAfter.globalActiveBytes > limits.activeGlobalBytes,
                    "Over-cap awareness cleanup was refused or advanced inexact running totals.");
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>
        /// Consecutive observation slices reuse exact published byte indexes. A different subsystem's
        /// publication fences the retained budget but seeds its replacement without another graph walk.
        /// </summary>
        [Test]
        public static void ObservationBudgetReusesPublishedSizeIndexesAcrossSlices()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            try
            {
                component.RebuildMemorySizeIndexes();
                long baselineWalks =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component);
                long baselineGeneration =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component);
                object first = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                object second = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                Require(ReferenceEquals(first, second)
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component)
                            == baselineWalks
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component)
                            == baselineGeneration,
                    "Consecutive observation slices repeated a full saved-graph byte walk.");

                component.RebuildMemorySizeIndexes();
                long externallyPublishedWalks =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component);
                long externallyPublishedGeneration =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component);
                object refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                Require(!ReferenceEquals(first, refreshed)
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component)
                            == externallyPublishedWalks
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component)
                            == externallyPublishedGeneration
                        && PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(refreshed)
                            .globalActiveBytes
                            == component.GetGlobalBudgetTotals().globalActiveBytes,
                    "An external byte-index publication caused a redundant walk or stale budget reuse.");
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>
        /// Ordinary epoch enrollment needs headroom in both the active directory and the broader
        /// active-plus-fence union. Conversely, completing an existing Brainwipe fence consumes only
        /// active headroom and must remain fenced when that stricter directory is full.
        /// </summary>
        [Test]
        public static void OwnerDirectoryCapsRefuseEnrollmentWithoutPartialMutation()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            var allocator = new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
            List<SavedMemoryDiagnosticCounter> priorDiagnostics =
                PawnDiaryMemoryM11RuntimeFixture.SnapshotMemoryDiagnostics(component);
            List<PawnDiaryRecord> fillers = null;
            try
            {
                PawnDiaryMemoryM11RuntimeFixture.ReplaceMemoryDiagnostics(
                    component, new List<SavedMemoryDiagnosticCounter>());
                int activeCap = PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerCap();
                int unionCap = PawnDiaryMemoryM11RuntimeFixture.ObservationEpochFenceCap();
                Require(activeCap > 1 && unionCap > activeCap,
                    "The loaded owner-directory tuple cannot express the fence reserve fixture.");
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
                state.structuralRevision = 7;
                state.statusRevision = 5;
                diary.knowledgeState = state;

                int activeFillers = activeCap - 2;
                fillers = PawnDiaryMemoryM11RuntimeFixture.AppendSyntheticOwnerDirectory(
                    component,
                    activeFillers,
                    unionCap - activeFillers,
                    "union-full");
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();
                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                PawnDiaryMemoryM11RuntimeFixture.ObservationActiveOwnerCount(
                    budget, activeFillers);
                PawnDiaryMemoryM11RuntimeFixture.ObservationNonArchiveEpochOwnerCount(
                    budget, unionCap);
                MemoryLogicalSizeResult blankBefore = MemoryLogicalPayloadSizer.Size(state);
                long allocatorSequenceBefore =
                    PawnDiaryMemoryM11RuntimeFixture.EpochAllocatorSequence(component);
                string allocatorChainBefore =
                    PawnDiaryMemoryM11RuntimeFixture.EpochAllocatorFallbackChain(component);

                object observationEnrollment =
                    PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                        component, pawn, budget);
                object factualEnrollment =
                    PawnDiaryMemoryM11RuntimeFixture.BeginFactualOwnerEnrollment(
                        component, state, false);
                MemoryLogicalSizeResult blankAfter = MemoryLogicalPayloadSizer.Size(state);
                Require(observationEnrollment == null && factualEnrollment == null
                        && blankBefore.valid && blankAfter.valid
                        && blankBefore.totalBytes == blankAfter.totalBytes
                        && string.IsNullOrEmpty(state.autobiographicalEpochToken)
                        && !state.epochFenceOnly
                        && state.structuralRevision == 7
                        && PawnDiaryMemoryM11RuntimeFixture.EpochAllocatorSequence(component)
                            == allocatorSequenceBefore
                        && PawnDiaryMemoryM11RuntimeFixture.EpochAllocatorFallbackChain(component)
                            == allocatorChainBefore,
                    "Union-full ordinary enrollment mutated the owner or allocator.");

                PawnDiaryMemoryM11RuntimeFixture.RemoveSyntheticOwnerDirectory(
                    component, fillers);
                fillers = null;
                state.autobiographicalEpochToken =
                    PawnDiaryMemoryM11RuntimeFixture.EpochToken(8451);
                state.epochFenceOnly = true;
                state.structuralRevision = 11;
                fillers = PawnDiaryMemoryM11RuntimeFixture.AppendSyntheticOwnerDirectory(
                    component, activeCap, 0, "active-full");
                object fenceEnrollment =
                    PawnDiaryMemoryM11RuntimeFixture.BeginFactualOwnerEnrollment(
                        component, state, true);
                List<SavedMemoryDiagnosticCounter> diagnostics =
                    PawnDiaryMemoryM11RuntimeFixture.SnapshotMemoryDiagnostics(component);
                Require(fenceEnrollment == null
                        && state.epochFenceOnly
                        && state.autobiographicalEpochToken
                            == PawnDiaryMemoryM11RuntimeFixture.EpochToken(8451)
                        && state.structuralRevision == 11
                        && diagnostics.Count == 1
                        && diagnostics[0].reasonToken == "brainwipe_capacity"
                        && diagnostics[0].saturatedCount == 1,
                    "Active-full Brainwipe completion consumed its fence or omitted its diagnostic.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.RemoveSyntheticOwnerDirectory(
                    component, fillers);
                PawnDiaryMemoryM11RuntimeFixture.ReplaceMemoryDiagnostics(
                    component, priorDiagnostics);
                scope.TearDown();
                allocator.Restore(component);
            }
        }

        /// <summary>
        /// A culture-only owner mutation has no factual block to refresh indexes later. It must
        /// fence a retained observation budget and publish exact replacement totals without a full
        /// saved-graph walk.
        /// </summary>
        [Test]
        public static void CultureOnlyMutationFencesRetainedObservationBudgetExactly()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
                state.autobiographicalEpochToken =
                    PawnDiaryMemoryM11RuntimeFixture.EpochToken(7351);
                diary.knowledgeState = state;
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object retained =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                long walksBefore =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component);
                long generationBefore =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component);

                component.CaptureOriginCulture(
                    pawn, "PawnDiary_RimTest_CultureOnlyMutation");

                object refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                DiaryGameComponent.MemoryOwnerByteTotals publishedOwner =
                    component.GetOwnerByteTotals(ownerId);
                DiaryGameComponent.MemoryOwnerByteTotals sessionOwner =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(
                        refreshed, ownerId);
                MemoryPayloadBudgetTotals publishedGlobal = component.GetGlobalBudgetTotals();
                MemoryPayloadBudgetTotals sessionGlobal =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(refreshed);
                Require(state.originCultureDefName
                            == "PawnDiary_RimTest_CultureOnlyMutation"
                        && !ReferenceEquals(retained, refreshed)
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component)
                            == walksBefore
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component)
                            > generationBefore
                        && publishedOwner.valid && sessionOwner.valid
                        && publishedOwner.activeBytes == sessionOwner.activeBytes
                        && publishedOwner.importedBytes == sessionOwner.importedBytes
                        && publishedGlobal.globalActiveBytes == sessionGlobal.globalActiveBytes
                        && publishedGlobal.globalImportedBytes
                            == sessionGlobal.globalImportedBytes,
                    "Culture-only saved state reused a stale observation budget or walked the colony.");

                // The checks above prove the mutation used the owner-only seam. This defensive
                // rebuild is test-only and independently proves that seam incorporated every byte,
                // rather than merely advancing the generation around stale totals.
                component.RebuildMemorySizeIndexes();
                DiaryGameComponent.MemoryOwnerByteTotals rebuiltOwner =
                    component.GetOwnerByteTotals(ownerId);
                MemoryPayloadBudgetTotals rebuiltGlobal = component.GetGlobalBudgetTotals();
                Require(rebuiltOwner.valid
                        && rebuiltOwner.activeBytes == publishedOwner.activeBytes
                        && rebuiltOwner.importedBytes == publishedOwner.importedBytes
                        && rebuiltGlobal.globalActiveBytes == publishedGlobal.globalActiveBytes
                        && rebuiltGlobal.globalImportedBytes
                            == publishedGlobal.globalImportedBytes,
                    "Culture-only incremental byte publication disagreed with a full rebuild.");
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>
        /// Adding the first row for a bounded component diagnostic grows saved metadata. It must
        /// incrementally fence retained observation totals; fixed-width count increments need not.
        /// </summary>
        [Test]
        public static void NewDiagnosticRowFencesRetainedObservationBudgetExactly()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            List<SavedMemoryDiagnosticCounter> prior =
                PawnDiaryMemoryM11RuntimeFixture.SnapshotMemoryDiagnostics(component);
            try
            {
                PawnDiaryMemoryM11RuntimeFixture.ReplaceMemoryDiagnostics(
                    component, new List<SavedMemoryDiagnosticCounter>());
                object retained =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                long walksBefore =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component);
                long generationBefore =
                    PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component);

                component.RecordMemoryDiagnostic("capacity_refused", "component");

                object refreshed =
                    PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                MemoryPayloadBudgetTotals incremental = component.GetGlobalBudgetTotals();
                MemoryPayloadBudgetTotals session =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(refreshed);
                Require(!ReferenceEquals(retained, refreshed)
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexFullRebuildCount(component)
                            == walksBefore
                        && PawnDiaryMemoryM11RuntimeFixture.SizeIndexGeneration(component)
                            > generationBefore
                        && PawnDiaryMemoryM11RuntimeFixture.SnapshotMemoryDiagnostics(component)
                            .Count == 1
                        && incremental.globalActiveBytes == session.globalActiveBytes
                        && incremental.globalImportedBytes == session.globalImportedBytes,
                    "A new saved diagnostic row reused stale observation byte totals.");

                component.RebuildMemorySizeIndexes();
                MemoryPayloadBudgetTotals rebuilt = component.GetGlobalBudgetTotals();
                Require(rebuilt.globalActiveBytes == incremental.globalActiveBytes
                        && rebuilt.globalImportedBytes == incremental.globalImportedBytes,
                    "Incremental diagnostic-row sizing disagreed with a full rebuild.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ReplaceMemoryDiagnostics(component, prior);
                scope.TearDown();
            }
        }

        /// <summary>
        /// The first allocator fallback grows component-owned chain metadata by 64 ASCII bytes.
        /// Observation admission and fast publication must charge that growth exactly once.
        /// </summary>
        [Test]
        public static void ObservationFallbackEpochChargesComponentChainExactly()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            var allocator = new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                diary.knowledgeState = null;
                PawnDiaryMemoryM11RuntimeFixture.SetEpochAllocator(
                    component, long.MaxValue, string.Empty);
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                object enrollment = PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                    component, pawn, budget);
                Require(enrollment != null
                        && PawnDiaryMemoryM11RuntimeFixture.ApplyObservationBaseline(
                            component, enrollment, budget, "fallback-chain"),
                    "The saturated allocator could not enroll through its fallback epoch.");
                bool fallback;
                Require(diary.knowledgeState != null
                        && MemoryIdentityCodec.TryValidateEpochToken(
                            diary.knowledgeState.autobiographicalEpochToken, out fallback)
                        && fallback,
                    "Observation did not publish a canonical fallback epoch at saturation.");
                PawnDiaryMemoryM11RuntimeFixture.CompleteObservationTick(component, budget);

                DiaryGameComponent.MemoryOwnerByteTotals incrementalOwner =
                    component.GetOwnerByteTotals(diary.pawnId);
                MemoryPayloadBudgetTotals incrementalGlobal = component.GetGlobalBudgetTotals();
                component.RebuildMemorySizeIndexes();
                DiaryGameComponent.MemoryOwnerByteTotals rebuiltOwner =
                    component.GetOwnerByteTotals(diary.pawnId);
                MemoryPayloadBudgetTotals rebuiltGlobal = component.GetGlobalBudgetTotals();
                Require(incrementalOwner.valid && rebuiltOwner.valid
                        && incrementalOwner.activeBytes == rebuiltOwner.activeBytes
                        && incrementalOwner.importedBytes == rebuiltOwner.importedBytes
                        && incrementalGlobal.globalActiveBytes == rebuiltGlobal.globalActiveBytes
                        && incrementalGlobal.globalImportedBytes
                            == rebuiltGlobal.globalImportedBytes,
                    "Fallback-chain growth was omitted or double-charged by fast publication.");
            }
            finally
            {
                scope.TearDown();
                allocator.Restore(component);
            }
        }

        /// <summary>
        /// A blank current owner may replace a large awareness row while saturated enrollment grows
        /// the fallback allocator chain. The beneficial owner branch must still charge that separate
        /// component growth before its fast totals are published.
        /// </summary>
        [Test]
        public static void ObservationFallbackChainIsChargedDuringOwnerShrink()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            var allocator = new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
                state.structuralRevision = 1;
                state.statusRevision = 1;
                state.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
                {
                    snapshotId = "rimtest-observation-fallback-shrink",
                    scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                    subjectKind = KnowledgeObservationTokens.SubjectPawn,
                    subjectId = "rimtest-subject-fallback-shrink",
                    factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                    captureInvalidationGeneration = 1,
                    knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                    stateFacts = new List<SavedMemoryStateFact>(),
                    firstObservedTick = 1,
                    lastObservedTick = 1,
                    lastSourceOccurrenceId = new string('x', 512),
                    trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                    snapshotRevision = 1
                });
                diary.knowledgeState = state;
                PawnDiaryMemoryM11RuntimeFixture.SetEpochAllocator(
                    component, long.MaxValue, string.Empty);
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();
                MemoryLogicalSizeResult ownerBefore = MemoryLogicalPayloadSizer.Size(state);

                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                DiaryGameComponent.MemoryOwnerByteTotals ownerBudgetBefore =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(budget, ownerId);
                MemoryPayloadBudgetTotals globalBudgetBefore =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                MemoryBudgetLimits limits =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationBudgetLimits(budget);
                globalBudgetBefore.globalActiveBytes =
                    checked(limits.activeGlobalBytes + 4096);
                PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(
                    budget, globalBudgetBefore);
                object enrollment = PawnDiaryMemoryM11RuntimeFixture.PrepareObservationOwner(
                    component, pawn, budget);
                Require(enrollment != null
                        && PawnDiaryMemoryM11RuntimeFixture.ApplyObservationBaseline(
                            component, enrollment, budget, "fallback-shrink"),
                    "The blank-owner fallback shrink fixture could not commit its replacement.");
                bool fallback;
                MemoryLogicalSizeResult ownerAfter = MemoryLogicalPayloadSizer.Size(state);
                Require(ownerBefore.valid && ownerAfter.valid
                        && ownerAfter.totalBytes <= ownerBefore.totalBytes
                        && MemoryIdentityCodec.TryValidateEpochToken(
                            state.autobiographicalEpochToken, out fallback)
                        && fallback,
                    "The fixture did not reach the nonpositive owner-delta fallback branch.");
                DiaryGameComponent.MemoryOwnerByteTotals ownerBudgetAfter =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationOwnerTotals(budget, ownerId);
                MemoryPayloadBudgetTotals globalBudgetAfter =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                long ownerDelta = checked(ownerAfter.totalBytes - ownerBefore.totalBytes);
                const long firstFallbackChainBytes = 64;
                long aggregateDelta = checked(ownerDelta + firstFallbackChainBytes);
                Require(aggregateDelta < 0
                        && ownerBudgetAfter.activeBytes - ownerBudgetBefore.activeBytes
                            == ownerDelta
                        && globalBudgetAfter.globalActiveBytes
                            - globalBudgetBefore.globalActiveBytes == aggregateDelta
                        && globalBudgetAfter.globalImportedBytes
                            == globalBudgetBefore.globalImportedBytes
                        && globalBudgetAfter.globalActiveBytes > limits.activeGlobalBytes,
                    "The over-cap owner-shrink branch omitted or separately gated fallback-chain bytes.");
            }
            finally
            {
                scope.TearDown();
                allocator.Restore(component);
            }
        }

        /// <summary>
        /// Removed global-faction truth is a beneficial component shrink and remains admissible when
        /// a loaded/settings-changed session begins above today's global cap.
        /// </summary>
        [Test]
        public static void GlobalFactionObservationShrinkSucceedsAboveCurrentCap()
        {
            PawnDiaryMemoryM11RuntimeFixture.RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            DiaryGameComponent component = scope.Component;
            List<SavedGlobalFactionSnapshot> prior =
                PawnDiaryMemoryM11RuntimeFixture.SnapshotGlobalFactionObservations(component);
            try
            {
                var row = new SavedGlobalFactionSnapshot
                {
                    factionInstanceId = "rimtest-global-faction-shrink",
                    allocatorGeneration = 1,
                    factionDefName = "PawnDiary_RimTest_Faction",
                    frozenDisplayLabel = "Disposable faction",
                    goodwill = 10,
                    relationKindToken = "neutral",
                    observedTick = 1,
                    trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                    snapshotRevision = 1
                };
                PawnDiaryMemoryM11RuntimeFixture.InstallGlobalFactionObservation(component, row);
                object budget = PawnDiaryMemoryM11RuntimeFixture.CreateObservationBudget(component);
                MemoryPayloadBudgetTotals before =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                MemoryBudgetLimits limits =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationBudgetLimits(budget);
                before.globalActiveBytes = checked(limits.activeGlobalBytes + 4096);
                PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget, before);
                MemoryLogicalSizeResult rowSize = MemoryLogicalPayloadSizer.Size(row);

                PawnDiaryMemoryM11RuntimeFixture.RemoveGlobalFactionObservation(
                    component, row.factionInstanceId, budget);

                MemoryPayloadBudgetTotals after =
                    PawnDiaryMemoryM11RuntimeFixture.ObservationGlobalTotals(budget);
                // The list-count prefix is present before and after removal, so it cancels. Saved
                // list elements carry no additional nullable-presence byte beyond the row itself.
                long exactShrink = rowSize.totalBytes;
                Require(rowSize.valid
                        && PawnDiaryMemoryM11RuntimeFixture.GlobalFactionObservationCount(component)
                            == 0
                        && before.globalActiveBytes - after.globalActiveBytes == exactShrink
                        && before.globalImportedBytes == after.globalImportedBytes
                        && after.globalActiveBytes > limits.activeGlobalBytes,
                    "Over-cap global-faction cleanup was refused or charged inexactly.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.RestoreGlobalFactionObservations(component, prior);
                scope.TearDown();
            }
        }

        private static SavedMemoryAwarenessSnapshot RelativeAwareness(string subjectId)
        {
            return new SavedMemoryAwarenessSnapshot
            {
                snapshotId = "rimtest-lifecycle-awareness-" + subjectId,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = subjectId,
                factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                stateFacts = new List<SavedMemoryStateFact>(),
                firstObservedTick = 1,
                lastObservedTick = 1,
                lastSourceOccurrenceId = "rimtest-lifecycle-observation",
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 1
            };
        }

        private static ScribeMeasurement RoundTrip(
            PawnKnowledgeState source,
            int target,
            int pass)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_m11_perf_n" + target + "_p" + pass + "_"
                    + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                PawnKnowledgeState saved = source;
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref saved, "memory");
                Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;
                long xmlBytes = new FileInfo(path).Length;

                PawnKnowledgeState loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "memory");
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
                Require(loaded != null && loaded.IsCurrentSchema(),
                    "N=" + target + " pass " + pass + " did not load a current envelope.");
                MemoryLogicalSizeResult size = MemoryLogicalPayloadSizer.Size(loaded);
                Require(size.valid,
                    "N=" + target + " pass " + pass
                        + " failed logical registry validation: " + size.errorPath);
                return new ScribeMeasurement
                {
                    loaded = loaded,
                    logicalBytes = size.totalBytes,
                    xmlBytes = xmlBytes,
                    ownerEpoch = loaded.autobiographicalEpochToken,
                    blockCount = loaded.standaloneBlocks.Count
                        + SumVisibleBlocks(loaded),
                    rootCount = loaded.threadRoots.Count
                };
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // A locked measurement file must not conceal the assertion that came first.
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }

        private static int SumVisibleBlocks(PawnKnowledgeState state)
        {
            int total = 0;
            for (int index = 0; index < state.threadRoots.Count; index++)
            {
                total += state.threadRoots[index]?.visibleBlocks?.Count ?? 0;
            }
            return total;
        }

        private sealed class ScribeMeasurement
        {
            internal PawnKnowledgeState loaded;
            internal long logicalBytes;
            internal long xmlBytes;
            internal string ownerEpoch = string.Empty;
            internal int rootCount;
            internal int blockCount;
        }
    }
}
