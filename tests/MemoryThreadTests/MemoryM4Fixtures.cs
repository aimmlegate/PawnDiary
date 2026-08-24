// MemoryM4Fixtures.cs — standalone adversarial fixtures for the complete pure M4 backend.
//
// These tests intentionally construct exact canonical identities and never load Verse. They cover
// expiry boundaries, chapter reduction, edited exceptions, deterministic repair, fixed points,
// suppression taint, hard-cap ordering, and protected atomic refusal.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace MemoryThreadTests
{
    internal static class MemoryM4Fixtures
    {
        private static int assertions;

        public static int Run()
        {
            assertions = 0;
            TestElapsedChapterBoundaries();
            TestBlockTtlBoundaries();
            TestSummaryFactTtlAndOriginalTicks();
            TestTargetEquationsAndEditedException();
            TestSuppressionTaintAndFixedPoint();
            TestEmergencyImportanceOrder();
            TestProtectedSaturationIsAtomic();
            TestValidationRefusalIsAtomic();
            TestMalformedNullAndUnknownData();
            TestPermutationDeterminism();
            TestDuplicateRootRepairAndAuthoredConflict();
            TestPressurePlannerOrderingAndRefusal();
            TestExactPlacementAndLookup();
            TestElapsedMaintenanceSlices();
            TestTargetBoundaryMatrix();
            TestTtlNeverRefreshes();
            TestPerFactExpiryKeepsRemainingReferences();
            TestCheckedPressureOverflow();
            TestMaintenanceReplayAndClockEdges();
            TestLegacyEvictionParityAndOwnerQualification();
            TestPolicyBoundsAndLongPause();
            TestCanonicalAggregationCatalog();
            TestAnchorAwareSummaryPressure();
            TestRollingContributionsMoveOnClosure();
            TestRevisionSaturationRefusesMutation();
            TestRepairPreservesNonCollidingEvidence();
            TestSummaryReferenceBounds();
            TestContributionReferenceConstructionAndRefusal();
            TestEmptyClosedChapterCleanup();
            TestPlayerWordingAgreement();
            TestUnknownNewerReducerRevisionsStayInert();
            TestIterativePressurePrefixPlanning();
            TestRepairPlacementRemapAndOpenOrder();
            TestRepairPublicationWinnerAndDiagnostic();
            return assertions;
        }

        private static void TestElapsedChapterBoundaries()
        {
            MemoryChapterClosurePlan before = MemoryChapterPolicy.PlanClosure(
                new MemoryChapterClosureRequest
                {
                    nowTick = 109,
                    lastActivityTick = 10,
                    inactivityTicks = 100
                });
            False("m4.chapter.before-boundary", before.shouldClose);
            MemoryChapterClosurePlan exact = MemoryChapterPolicy.PlanClosure(
                new MemoryChapterClosureRequest
                {
                    nowTick = 110,
                    lastActivityTick = 10,
                    inactivityTicks = 100
                });
            True("m4.chapter.exact-boundary", exact.shouldClose);
            Equal("m4.chapter.reason", MemoryChapterTokens.Inactivity, exact.reasonToken);
            Equal("m4.chapter.exact-due-tick", 110L, exact.closedTick);
            MemoryChapterClosurePlan overdue = MemoryChapterPolicy.PlanClosure(
                new MemoryChapterClosureRequest
                {
                    nowTick = 1000,
                    lastActivityTick = 10,
                    inactivityTicks = 100
                });
            Equal("m4.chapter.overdue-still-due-tick", 110L, overdue.closedTick);
            Equal("m4.chapter.long-pause", long.MaxValue,
                MemoryChapterPolicy.Elapsed(long.MaxValue, -1));
            MemoryChapterClosurePlan explicitReason = MemoryChapterPolicy.PlanClosure(
                new MemoryChapterClosureRequest
                {
                    nowTick = 20,
                    lastActivityTick = 20,
                    inactivityTicks = 100,
                    formalEnd = true,
                    reversal = true,
                    lifecycleBoundary = true
                });
            Equal("m4.chapter.explicit-priority", MemoryChapterTokens.FormalEnd,
                explicitReason.reasonToken);
        }

        private static void TestBlockTtlBoundaries()
        {
            True("m4.ttl.minor.exact", MemoryThreadReducer.IsExpired(
                110, 10, false, MemoryContractTokens.ImportanceMinor, 100, 1000));
            False("m4.ttl.minor.before", MemoryThreadReducer.IsExpired(
                109, 10, false, MemoryContractTokens.ImportanceMinor, 100, 1000));
            True("m4.ttl.regular.exact", MemoryThreadReducer.IsExpired(
                1010, 10, false, MemoryContractTokens.ImportanceRegular, 100, 1000));
            False("m4.ttl.important.never", MemoryThreadReducer.IsExpired(
                long.MaxValue, 0, false, MemoryContractTokens.ImportanceImportant, 0, 0));
            False("m4.ttl.unknown.never", MemoryThreadReducer.IsExpired(
                long.MaxValue, 0, true, MemoryContractTokens.ImportanceMinor, 0, 0));

            MemoryReducerRoot root = Root(10);
            root.visibleBlocks.Add(Block(root, 1, 0, MemoryContractTokens.ImportanceMinor, false));
            root.visibleBlocks.Add(Block(root, 2, 0, MemoryContractTokens.ImportanceRegular, false));
            root.visibleBlocks.Add(Block(root, 3, 0, MemoryContractTokens.ImportanceImportant, false));
            MemoryReducerBlock unknown = Block(
                root, 4, 0, MemoryContractTokens.ImportanceMinor, false);
            unknown.ageUnknown = true;
            root.visibleBlocks.Add(unknown);
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, Policy(100, 100, 100));
            True("m4.ttl.block.accepted", !reduced.refused);
            Equal("m4.ttl.block.expired-count", 2, reduced.expiredBlocks);
            Equal("m4.ttl.block.survivors", 2, reduced.replacement.visibleBlocks.Count);
            True("m4.ttl.block.important-survives",
                Find(reduced.replacement, root.visibleBlocks[2].recordId) != null);
            True("m4.ttl.block.unknown-survives",
                Find(reduced.replacement, root.visibleBlocks[3].recordId) != null);
        }

        private static void TestSummaryFactTtlAndOriginalTicks()
        {
            MemoryReducerRoot root = Root(10);
            root.visibleBlocks.Add(Block(root, 1, 100, MemoryContractTokens.ImportanceMinor, false));
            root.visibleBlocks.Add(Block(root, 2, 100, MemoryContractTokens.ImportanceImportant, false));
            MemoryReducerPolicy closePolicy = Policy(101, 1000, 1000);
            closePolicy.chapterInactivityTicks = 1;
            MemoryThreadReductionResult closed = MemoryThreadReducer.Reduce(root, closePolicy);
            True("m4.summary.close.accepted", !closed.refused);
            Equal("m4.summary.close.one-block", 1, closed.replacement.visibleBlocks.Count);
            MemoryReducerBlock summary = closed.replacement.visibleBlocks[0];
            Equal("m4.summary.close.kind", MemoryContractTokens.KindSummary, summary.kind);
            Equal("m4.summary.original-tick", 100L,
                summary.summaryPayload.factBuckets[0].contributions[0].originalEventTick);
            Equal("m4.summary.stable-block-tick", 100L, summary.originalEventTick);

            MemoryReducerPolicy expiry = Policy(1100, 1000, 1000);
            MemoryThreadReductionResult aged = MemoryThreadReducer.Reduce(closed.replacement, expiry);
            True("m4.summary.expiry.accepted", !aged.refused);
            Equal("m4.summary.expiry.one-contribution", 1, aged.expiredContributions);
            Equal("m4.summary.expiry.important-remains", 1,
                TotalContributions(aged.replacement));
            Equal("m4.summary.expiry.tick-still-original", 100L,
                aged.replacement.visibleBlocks[0].summaryPayload.factBuckets[0]
                    .contributions[0].originalEventTick);
        }

        private static void TestTargetEquationsAndEditedException()
        {
            MemoryReducerRoot root = Root(1000);
            for (int i = 0; i < 20; i++)
                root.visibleBlocks.Add(Block(root, i + 1, i + 1,
                    MemoryContractTokens.ImportanceRegular, i < 5));
            MemoryReducerPolicy policy = Policy(1000, 10000, 10000);
            policy.targetVisibleBlocks = 12;
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            True("m4.target.accepted", !reduced.refused);
            int edited = EditedCount(reduced.replacement);
            int unedited = reduced.replacement.visibleBlocks.Count - edited;
            True("m4.target.u-equation", unedited <= Math.Max(0, 12 - edited));
            True("m4.target.total-equation", edited + unedited <= Math.Max(12, edited));
            Equal("m4.target.edited-preserved", 5, edited);
            True("m4.target.single-rolling", reduced.replacement.rollingSummaryBlock != null);

            MemoryReducerRoot editedOnly = Root(1000);
            for (int i = 0; i < 20; i++) editedOnly.visibleBlocks.Add(Block(
                editedOnly, i + 1, i, MemoryContractTokens.ImportanceMinor, true));
            MemoryThreadReductionResult exception = MemoryThreadReducer.Reduce(editedOnly, policy);
            True("m4.target.edited-exception.accepted", !exception.refused);
            Equal("m4.target.edited-exception.all", 20, BlockCount(exception.replacement));
            Equal("m4.target.edited-exception.no-rolling", null,
                exception.replacement.rollingSummaryBlock);
        }

        private static void TestSuppressionTaintAndFixedPoint()
        {
            MemoryReducerRoot root = Root(1000);
            for (int i = 0; i < 7; i++)
            {
                MemoryReducerBlock block = Block(root, i + 1, i,
                    MemoryContractTokens.ImportanceRegular, false);
                block.suppressed = i == 0;
                root.visibleBlocks.Add(block);
            }
            MemoryReducerPolicy policy = Policy(1000, 10000, 10000);
            policy.targetVisibleBlocks = 4;
            MemoryThreadReductionResult first = MemoryThreadReducer.Reduce(root, policy);
            True("m4.suppression.taint", first.replacement.rollingSummaryBlock.suppressed);
            string once = MemoryThreadReducer.CanonicalState(first.replacement);
            MemoryThreadReductionResult second = MemoryThreadReducer.Reduce(first.replacement, policy);
            True("m4.fixed-point.accepted", !second.refused);
            False("m4.fixed-point.unchanged", second.changed);
            Equal("m4.fixed-point.bytes", once,
                MemoryThreadReducer.CanonicalState(second.replacement));
        }

        private static void TestEmergencyImportanceOrder()
        {
            MemoryReducerRoot root = Root(0);
            root.visibleBlocks.Add(Block(root, 1, 10, MemoryContractTokens.ImportanceImportant, false));
            root.visibleBlocks.Add(Block(root, 2, 11, MemoryContractTokens.ImportanceRegular, false));
            root.visibleBlocks.Add(Block(root, 3, 12, MemoryContractTokens.ImportanceMinor, false));
            MemoryReducerPolicy policy = Policy(100, 1000, 1000);
            policy.chapterInactivityTicks = 1;
            policy.maximumContributionsPerSummary = 2;
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            Equal("m4.emergency.removed-one", 1, reduced.emergencyAtomsRemoved);
            List<string> importance = ContributionImportance(reduced.replacement);
            False("m4.emergency.low-first", importance.Contains(MemoryContractTokens.ImportanceMinor));
            True("m4.emergency.medium-remains", importance.Contains(MemoryContractTokens.ImportanceRegular));
            True("m4.emergency.high-remains", importance.Contains(MemoryContractTokens.ImportanceImportant));
        }

        private static void TestProtectedSaturationIsAtomic()
        {
            MemoryReducerRoot root = Root(100);
            for (int i = 0; i < 5; i++) root.visibleBlocks.Add(Block(
                root, i + 1, i, MemoryContractTokens.ImportanceMinor, true));
            string before = MemoryThreadReducer.CanonicalState(root);
            MemoryReducerPolicy policy = Policy(100, 1000, 1000);
            policy.maximumVisibleBlocks = 4;
            MemoryThreadReductionResult refused = MemoryThreadReducer.Reduce(root, policy);
            True("m4.saturation.refused", refused.refused);
            True("m4.saturation.protected", refused.protectedSaturation);
            Equal("m4.saturation.no-replacement", null, refused.replacement);
            Equal("m4.saturation.input-atomic", before, MemoryThreadReducer.CanonicalState(root));
        }

        private static void TestValidationRefusalIsAtomic()
        {
            MemoryReducerRoot root = Root(100);
            root.visibleBlocks.Add(Block(root, 1, 10, MemoryContractTokens.ImportanceMinor, false));
            root.visibleBlocks.Add(root.visibleBlocks[0].Clone());
            string before = MemoryThreadReducer.CanonicalState(root);
            MemoryThreadReductionResult refused = MemoryThreadReducer.Reduce(root, Policy(100, 1, 1));
            True("m4.validation.duplicate-refused", refused.refused);
            Equal("m4.validation.atomic", before, MemoryThreadReducer.CanonicalState(root));
        }

        private static void TestPermutationDeterminism()
        {
            MemoryReducerRoot left = Root(1000);
            for (int i = 0; i < 10; i++) left.visibleBlocks.Add(Block(
                left, i + 1, i, i % 2 == 0 ? MemoryContractTokens.ImportanceMinor
                    : MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerRoot right = left.Clone();
            right.visibleBlocks.Reverse();
            MemoryReducerPolicy policy = Policy(1000, 10000, 10000);
            policy.targetVisibleBlocks = 4;
            MemoryThreadReductionResult a = MemoryThreadReducer.Reduce(left, policy);
            MemoryThreadReductionResult b = MemoryThreadReducer.Reduce(right, policy);
            Equal("m4.permutation.reducer", MemoryThreadReducer.CanonicalState(a.replacement),
                MemoryThreadReducer.CanonicalState(b.replacement));
        }

        private static void TestMalformedNullAndUnknownData()
        {
            True("m4.invalid.null-root", MemoryThreadReducer.Reduce(
                null, Policy(0, 1, 1)).refused);
            MemoryReducerRoot malformed = Root(0);
            malformed.subjectId = "bad\uD800";
            string before = MemoryThreadReducer.CanonicalState(malformed);
            MemoryThreadReductionResult malformedResult = MemoryThreadReducer.Reduce(
                malformed, Policy(0, 1, 1));
            True("m4.invalid.utf16", malformedResult.refused);
            Equal("m4.invalid.utf16.atomic", before,
                MemoryThreadReducer.CanonicalState(malformed));

            MemoryReducerRoot unknown = Root(0);
            MemoryReducerBlock unknownBlock = Block(
                unknown, 1, 0, MemoryContractTokens.ImportanceMinor, false);
            unknownBlock.importance = "legendary_unknown";
            unknown.visibleBlocks.Add(unknownBlock);
            True("m4.invalid.unknown-token", MemoryThreadReducer.Reduce(
                unknown, Policy(0, 1, 1)).refused);

            MemoryReducerRoot nullFact = Root(0);
            MemoryReducerBlock broken = Block(
                nullFact, 1, 0, MemoryContractTokens.ImportanceMinor, false);
            broken.facts.Add(null);
            nullFact.visibleBlocks.Add(broken);
            True("m4.invalid.null-fact", MemoryThreadReducer.Reduce(
                nullFact, Policy(0, 1, 1)).refused);
        }

        private static void TestTargetBoundaryMatrix()
        {
            foreach (int target in new[] { 4, 12, 64 })
            foreach (int count in new[] { target, target + 1, target * 3 })
            {
                MemoryReducerRoot root = Root(10000);
                for (int i = 0; i < count; i++) root.visibleBlocks.Add(Block(
                    root, i + 1, i, MemoryContractTokens.ImportanceRegular, false));
                MemoryReducerPolicy policy = Policy(10000, 20000, 20000);
                policy.targetVisibleBlocks = target;
                policy.maximumVisibleBlocks = 1024;
                MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
                True("m4.target-matrix.accepted." + target + "." + count, !reduced.refused);
                True("m4.target-matrix.bound." + target + "." + count,
                    reduced.replacement.visibleBlocks.Count <= target);
                if (count == target)
                    Equal("m4.target-matrix.exact." + target, target,
                        reduced.replacement.visibleBlocks.Count);
                else
                {
                    Equal("m4.target-matrix.detail-count." + target + "." + count,
                        target, reduced.replacement.visibleBlocks.Count);
                    True("m4.target-matrix.rolling." + target + "." + count,
                        reduced.replacement.rollingSummaryBlock != null);
                }
            }
        }

        private static void TestTtlNeverRefreshes()
        {
            MemoryReducerRoot edited = Root(100);
            MemoryReducerBlock old = Block(
                edited, 1, 0, MemoryContractTokens.ImportanceMinor, true);
            edited.visibleBlocks.Add(old);
            MemoryReducerPolicy policy = Policy(100, 10, 10);
            MemoryThreadReductionResult protectedResult = MemoryThreadReducer.Reduce(edited, policy);
            Equal("m4.ttl.edit.tick-not-refreshed", 0L,
                protectedResult.replacement.visibleBlocks[0].originalEventTick);
            protectedResult.replacement.visibleBlocks[0].playerEdited = false;
            protectedResult.replacement.visibleBlocks[0].playerWording = string.Empty;
            MemoryThreadReductionResult unedited = MemoryThreadReducer.Reduce(
                protectedResult.replacement, policy);
            Equal("m4.ttl.edit-unprotect.expires-original", 1, unedited.expiredBlocks);

            MemoryReducerRoot lookup = Root(100);
            lookup.visibleBlocks.Add(Block(
                lookup, 1, 0, MemoryContractTokens.ImportanceMinor, false));
            int index = MemoryThreadLookupPolicy.FindExactRoot(
                new List<MemoryReducerRoot> { lookup }, lookup.ownerPawnId,
                lookup.ownerEpochToken, lookup.subjectKind, lookup.subjectId);
            Equal("m4.ttl.lookup.found", 0, index);
            Equal("m4.ttl.lookup.no-refresh", 0L, lookup.visibleBlocks[0].originalEventTick);

            MemoryReducerRoot merged = Root(100);
            for (int i = 0; i < 6; i++) merged.visibleBlocks.Add(Block(
                merged, i + 1, i, MemoryContractTokens.ImportanceMinor, false));
            MemoryReducerPolicy mergePolicy = Policy(6, 100, 100);
            mergePolicy.targetVisibleBlocks = 4;
            MemoryThreadReductionResult merge = MemoryThreadReducer.Reduce(merged, mergePolicy);
            long earliest = long.MaxValue;
            for (int i = 0; i < merge.replacement.rollingSummaryBlock.summaryPayload.factBuckets.Count; i++)
                for (int j = 0; j < merge.replacement.rollingSummaryBlock.summaryPayload
                    .factBuckets[i].contributions.Count; j++)
                    earliest = Math.Min(earliest, merge.replacement.rollingSummaryBlock.summaryPayload
                        .factBuckets[i].contributions[j].originalEventTick);
            Equal("m4.ttl.merge.original-min", 0L, earliest);
            MemoryThreadRepairResult repaired = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { merge.replacement.Clone() }, mergePolicy);
            Equal("m4.ttl.repair.no-refresh", earliest,
                repaired.activeRoots[0].rollingSummaryBlock.summaryPayload.factBuckets[0]
                    .contributions[0].originalEventTick);
            MemoryMaintenanceSlicePlan maintenance = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 6,
                    lastRunTick = 0,
                    dirty = true,
                    itemCount = 1,
                    maximumWorkItems = 1,
                    intervalTicks = 100
                });
            True("m4.ttl.maintenance.due", maintenance.due);
            Equal("m4.ttl.maintenance.no-payload-mutation", earliest,
                repaired.activeRoots[0].rollingSummaryBlock.summaryPayload.factBuckets[0]
                    .contributions[0].originalEventTick);
        }

        private static void TestPerFactExpiryKeepsRemainingReferences()
        {
            MemoryReducerRoot root = Root(0);
            MemoryReducerBlock minor = Block(
                root, 1, 0, MemoryContractTokens.ImportanceMinor, false);
            minor.facts[0].subjectRefIds.Add("subject-minor");
            minor.facts[0].provenanceRefIds.Add("provenance-minor");
            MemoryReducerBlock important = Block(
                root, 2, 0, MemoryContractTokens.ImportanceImportant, false);
            important.facts[0].subjectRefIds.Add("subject-important");
            important.facts[0].provenanceRefIds.Add("provenance-important");
            root.visibleBlocks.Add(minor);
            root.visibleBlocks.Add(important);
            MemoryReducerPolicy close = Policy(1, 100, 100);
            close.chapterInactivityTicks = 1;
            MemoryThreadReductionResult summary = MemoryThreadReducer.Reduce(root, close);
            MemoryThreadReductionResult expired = MemoryThreadReducer.Reduce(
                summary.replacement, Policy(100, 100, 100));
            Equal("m4.per-fact.expired-one", 1, expired.expiredContributions);
            MemoryReducerContribution survivor = expired.replacement.visibleBlocks[0]
                .summaryPayload.factBuckets[0].contributions[0];
            Equal("m4.per-fact.subject-survives", "subject-important",
                survivor.subjectRefIds[0]);
            Equal("m4.per-fact.provenance-survives", "provenance-important",
                survivor.provenanceRefIds[0]);
            False("m4.per-fact.no-stale-subject",
                survivor.subjectRefIds.Contains("subject-minor"));
            False("m4.per-fact.no-stale-provenance",
                survivor.provenanceRefIds.Contains("provenance-minor"));
        }

        private static void TestCheckedPressureOverflow()
        {
            MemoryPressurePlan saturated = KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest
                {
                    bytesToRelease = long.MaxValue,
                    atoms = new List<MemoryPressureAtom>
                    {
                        new MemoryPressureAtom
                        {
                            ownerPawnId = "owner",
                            recordId = "a",
                            importance = MemoryContractTokens.ImportanceMinor,
                            logicalBytes = long.MaxValue - 1
                        },
                        new MemoryPressureAtom
                        {
                            ownerPawnId = "owner",
                            recordId = "b",
                            importance = MemoryContractTokens.ImportanceRegular,
                            logicalBytes = 100
                        }
                    }
                });
            True("m4.pressure.checked-saturation", saturated.canApply);
            Equal("m4.pressure.checked-value", long.MaxValue, saturated.releasedBytes);
            False("m4.pressure.negative-refuses", KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest { bytesToRelease = -1 }).canApply);
        }

        private static void TestMaintenanceReplayAndClockEdges()
        {
            MemoryMaintenanceSlicePlan unchanged = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 10, lastRunTick = 10, intervalTicks = 1,
                    itemCount = 10, maximumWorkItems = 2
                });
            False("m4.maintenance.unchanged-clock", unchanged.due);
            MemoryMaintenanceSlicePlan backwards = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 9, lastRunTick = 10, intervalTicks = 1,
                    itemCount = 10, maximumWorkItems = 2
                });
            False("m4.maintenance.backwards-clock", backwards.due);
            MemoryMaintenanceSlicePlan first = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 10, lastRunTick = 10, dirty = true,
                    intervalTicks = 100, itemCount = 5, maximumWorkItems = 2
                });
            MemoryMaintenanceSlicePlan replay = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 10, lastRunTick = first.nextLastRunTick, dirty = true,
                    intervalTicks = 100, itemCount = 5,
                    nextItemIndex = first.nextItemIndex, maximumWorkItems = 2
                });
            Equal("m4.maintenance.replay-start", 2, replay.startIndex);
            Equal("m4.maintenance.replay-progress", 4, replay.nextItemIndex);
            False("m4.maintenance.replay-not-complete", replay.completedCycle);
            MemoryMaintenanceSlicePlan finish = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 10, lastRunTick = replay.nextLastRunTick, dirty = true,
                    intervalTicks = 100, itemCount = 5,
                    nextItemIndex = replay.nextItemIndex, maximumWorkItems = 2
                });
            True("m4.maintenance.terminates", finish.completedCycle);
            Equal("m4.maintenance.finish-last-run", 10L, finish.nextLastRunTick);
        }

        private static void TestLegacyEvictionParityAndOwnerQualification()
        {
            KnowledgeOwnerLoad ownerA = LegacyOwner("owner-a", "same", "a-new");
            KnowledgeOwnerLoad ownerB = LegacyOwner("owner-b", "same", "b-new");
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxRecordsPerPawn = 1;
            policy.maxRecordsGlobal = 100;
            List<KnowledgeOwnerLoad> owners = new List<KnowledgeOwnerLoad> { ownerB, ownerA };
            KnowledgeEvictionPlan legacy = KnowledgeEvictionPlanner.Plan(owners, policy);
            QualifiedKnowledgeEvictionPlan qualified = KnowledgeEvictionPlanner.PlanQualified(
                owners, policy);
            Equal("m4.legacy.parity-count", legacy.dropRecordIds.Count,
                qualified.drops.Count);
            Equal("m4.legacy.qualified-two", 2, qualified.drops.Count);
            HashSet<string> handles = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < qualified.drops.Count; i++) handles.Add(
                qualified.drops[i].ownerPawnId + "/" + qualified.drops[i].recordId);
            True("m4.legacy.owner-a-qualified", handles.Contains("owner-a/same"));
            True("m4.legacy.owner-b-qualified", handles.Contains("owner-b/same"));

            KnowledgeOwnerLoad duplicateIds = new KnowledgeOwnerLoad { ownerPawnId = "owner-c" };
            duplicateIds.records.Add(new KnowledgeRecordStub
                { recordId = "duplicate", tick = 1, sourceIndex = 0 });
            duplicateIds.records.Add(new KnowledgeRecordStub
                { recordId = "duplicate", tick = 2, sourceIndex = 1 });
            QualifiedKnowledgeEvictionPlan oneDuplicate = KnowledgeEvictionPlanner.PlanQualified(
                new List<KnowledgeOwnerLoad> { duplicateIds }, new KnowledgePolicySnapshot
                {
                    maxRecordsPerPawn = 1,
                    maxRecordsGlobal = 100
                });
            Equal("m4.legacy.duplicate-id-one-drop", 1, oneDuplicate.drops.Count);
            Equal("m4.legacy.duplicate-id-exact-index", 0,
                oneDuplicate.drops[0].sourceIndex);
        }

        private static void TestDuplicateRootRepairAndAuthoredConflict()
        {
            MemoryReducerRoot root = Root(100);
            root.visibleBlocks.Add(Block(root, 1, 10, MemoryContractTokens.ImportanceRegular, false));
            MemoryThreadRepairResult duplicate = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { root.Clone(), root.Clone() }, Policy(100, 1000, 1000));
            True("m4.repair.duplicate.accepted", !duplicate.refused);
            Equal("m4.repair.duplicate.one-root", 1, duplicate.activeRoots.Count);
            Equal("m4.repair.duplicate.one-block", 1,
                duplicate.activeRoots[0].visibleBlocks.Count);

            MemoryReducerRoot suppressed = root.Clone();
            suppressed.visibleBlocks[0].suppressed = true;
            MemoryThreadRepairResult suppressionMerge = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { suppressed, root }, Policy(100, 1000, 1000));
            True("m4.repair.suppression.accepted", !suppressionMerge.refused);
            Equal("m4.repair.suppression.no-archive", 0, suppressionMerge.archivedRoots.Count);
            True("m4.repair.suppression.or",
                suppressionMerge.activeRoots[0].visibleBlocks[0].suppressed);

            MemoryReducerRoot authoredA = root.Clone();
            authoredA.visibleBlocks[0].playerEdited = true;
            authoredA.visibleBlocks[0].playerWording = "authored winner";
            MemoryReducerRoot authoredB = authoredA.Clone();
            authoredB.visibleBlocks[0].facts[0].canonicalValue = "authored-conflict";
            MemoryThreadRepairResult archived = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { authoredA, authoredB }, Policy(100, 1000, 1000));
            True("m4.repair.conflict.accepted", !archived.refused);
            Equal("m4.repair.conflict.archived", 1, archived.archivedRoots.Count);
            True("m4.repair.conflict.authored", archived.authoredConflictArchived);

            MemoryThreadRepairResult reverse = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { authoredB, authoredA }, Policy(100, 1000, 1000));
            Equal("m4.repair.permutation.active",
                MemoryThreadReducer.CanonicalState(archived.activeRoots[0]),
                MemoryThreadReducer.CanonicalState(reverse.activeRoots[0]));
            Equal("m4.repair.permutation.archive",
                MemoryThreadReducer.CanonicalState(archived.archivedRoots[0]),
                MemoryThreadReducer.CanonicalState(reverse.archivedRoots[0]));
        }

        private static void TestPressurePlannerOrderingAndRefusal()
        {
            List<MemoryPressureAtom> atoms = new List<MemoryPressureAtom>
            {
                Atom("high", MemoryContractTokens.ImportanceImportant, 1, false),
                Atom("medium", MemoryContractTokens.ImportanceRegular, 2, false),
                Atom("low-new", MemoryContractTokens.ImportanceMinor, 3, false),
                Atom("low-old", MemoryContractTokens.ImportanceMinor, 1, false),
                Atom("edited", MemoryContractTokens.ImportanceMinor, 0, true)
            };
            MemoryPressurePlan plan = KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest
                {
                    bytesToRelease = 3,
                    blocksToRelease = 3,
                    atoms = atoms
                });
            True("m4.pressure.can-apply", plan.canApply);
            Equal("m4.pressure.low-old-first", "low-old", plan.removals[0].recordId);
            Equal("m4.pressure.low-new-second", "low-new", plan.removals[1].recordId);
            Equal("m4.pressure.medium-third", "medium", plan.removals[2].recordId);
            MemoryPressurePlan refused = KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest
                {
                    bytesToRelease = 100,
                    blocksToRelease = 100,
                    atoms = atoms
                });
            False("m4.pressure.refused", refused.canApply);
            True("m4.pressure.protected-saturation", refused.protectedSaturation);
            Equal("m4.pressure.atomic-empty", 0, refused.removals.Count);
        }

        private static void TestPolicyBoundsAndLongPause()
        {
            Equal("m4.policy.target.minimum", 4,
                new MemoryReducerPolicy { targetVisibleBlocks = -1 }.Normalize().targetVisibleBlocks);
            Equal("m4.policy.target.default", 12,
                new MemoryReducerPolicy().Normalize().targetVisibleBlocks);
            Equal("m4.policy.target.maximum", 64,
                new MemoryReducerPolicy { targetVisibleBlocks = int.MaxValue }.Normalize()
                    .targetVisibleBlocks);
            MemoryReducerRoot root = Root(1);
            root.visibleBlocks.Add(Block(root, 1, 1, MemoryContractTokens.ImportanceMinor, false));
            MemoryReducerPolicy policy = Policy(long.MaxValue, 1, 1);
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            Equal("m4.long-pause.expired", 1, reduced.expiredBlocks);
            Equal("m4.long-pause.empty", 0, BlockCount(reduced.replacement));
        }

        private static void TestCanonicalAggregationCatalog()
        {
            MemoryReducerRoot countRoot = Root(10);
            MemoryReducerBlock countA = Block(countRoot, 1, 1,
                MemoryContractTokens.ImportanceRegular, false);
            MemoryReducerBlock countB = Block(countRoot, 2, 2,
                MemoryContractTokens.ImportanceRegular, false);
            SetFactContract(countA, countRoot, "visits",
                MemoryFactContractTokens.CountOccurrences,
                MemoryFactContractTokens.ValueEmpty, string.Empty);
            SetFactContract(countB, countRoot, "visits",
                MemoryFactContractTokens.CountOccurrences,
                MemoryFactContractTokens.ValueEmpty, string.Empty);
            countRoot.visibleBlocks.Add(countA);
            countRoot.visibleBlocks.Add(countB);
            MemoryReducerPolicy close = Policy(11, 1000, 1000);
            close.chapterInactivityTicks = 1;
            MemoryThreadReductionResult counted = MemoryThreadReducer.Reduce(countRoot, close);
            True("m4.aggregate.count.accepted", !counted.refused);
            MemoryReducerSummary countSummary = counted.replacement.visibleBlocks[0].summaryPayload;
            Equal("m4.aggregate.semantic-one-bucket", 1, countSummary.factBuckets.Count);
            Equal("m4.aggregate.count-two", 2, countSummary.factBuckets[0].derivedCount);

            MemoryReducerRoot rangeRoot = Root(10);
            MemoryReducerBlock rangeA = Block(rangeRoot, 1, 1,
                MemoryContractTokens.ImportanceRegular, false);
            MemoryReducerBlock rangeB = Block(rangeRoot, 2, 2,
                MemoryContractTokens.ImportanceRegular, false);
            SetFactContract(rangeA, rangeRoot, "opinion",
                MemoryFactContractTokens.Int64Range,
                MemoryFactContractTokens.ValueInt64, "-2");
            SetFactContract(rangeB, rangeRoot, "opinion",
                MemoryFactContractTokens.Int64Range,
                MemoryFactContractTokens.ValueInt64, "5");
            rangeRoot.visibleBlocks.Add(rangeA);
            rangeRoot.visibleBlocks.Add(rangeB);
            MemoryThreadReductionResult ranged = MemoryThreadReducer.Reduce(rangeRoot, close);
            True("m4.aggregate.range.accepted", !ranged.refused);
            MemoryReducerBucket range = ranged.replacement.visibleBlocks[0]
                .summaryPayload.factBuckets[0];
            Equal("m4.aggregate.range.min", "-2", range.derivedRangeMin);
            Equal("m4.aggregate.range.max", "5", range.derivedRangeMax);

            MemoryReducerRoot malformed = Root(10);
            MemoryReducerBlock bad = Block(malformed, 1, 1,
                MemoryContractTokens.ImportanceRegular, false);
            SetFactContract(bad, malformed, "opinion",
                MemoryFactContractTokens.Int64Range,
                MemoryFactContractTokens.ValueInt64, "01");
            malformed.visibleBlocks.Add(bad);
            True("m4.aggregate.noncanonical-int-refused",
                MemoryThreadReducer.Reduce(malformed, close).refused);
        }

        private static void TestAnchorAwareSummaryPressure()
        {
            MemoryReducerRoot root = Root(10);
            for (int i = 1; i <= 3; i++) root.visibleBlocks.Add(Block(
                root, i, i, MemoryContractTokens.ImportanceMinor, false));
            MemoryReducerPolicy policy = Policy(11, 1000, 1000);
            policy.chapterInactivityTicks = 1;
            policy.maximumContributionsPerSummary = 2;
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            True("m4.anchor.accepted", !reduced.refused);
            List<string> values = ContributionValues(reduced.replacement);
            True("m4.anchor.first-survives", values.Contains("value-1"));
            False("m4.anchor.non-anchor-removed", values.Contains("value-2"));
            True("m4.anchor.latest-survives", values.Contains("value-3"));
        }

        private static void TestRollingContributionsMoveOnClosure()
        {
            MemoryReducerRoot root = Root(100);
            for (int i = 1; i <= 5; i++) root.visibleBlocks.Add(Block(
                root, i, i, MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerPolicy fold = Policy(100, 1000, 1000);
            fold.targetVisibleBlocks = 4;
            MemoryThreadReductionResult folded = MemoryThreadReducer.Reduce(root, fold);
            Equal("m4.close-rolling.pre-visible", 4,
                folded.replacement.visibleBlocks.Count);
            True("m4.close-rolling.pre-rolling",
                folded.replacement.rollingSummaryBlock != null);

            MemoryReducerPolicy close = Policy(101, 1000, 1000);
            close.chapterInactivityTicks = 1;
            close.targetVisibleBlocks = 64;
            MemoryThreadReductionResult closed = MemoryThreadReducer.Reduce(
                folded.replacement, close);
            True("m4.close-rolling.accepted", !closed.refused);
            Equal("m4.close-rolling.one-closed", 1, closed.replacement.visibleBlocks.Count);
            Equal("m4.close-rolling.rolling-cleared", null,
                closed.replacement.rollingSummaryBlock);
            Equal("m4.close-rolling.all-contributions", 5,
                TotalContributions(closed.replacement));
        }

        private static void TestRevisionSaturationRefusesMutation()
        {
            MemoryReducerRoot root = Root(100);
            root.structuralRevision = long.MaxValue;
            for (int i = 1; i <= 5; i++) root.visibleBlocks.Add(Block(
                root, i, i, MemoryContractTokens.ImportanceRegular, false));
            string before = MemoryThreadReducer.CanonicalState(root);
            MemoryReducerPolicy target = Policy(100, 1000, 1000);
            target.targetVisibleBlocks = 4;
            MemoryThreadReductionResult refused = MemoryThreadReducer.Reduce(root, target);
            True("m4.revision.root.refused", refused.refused);
            Equal("m4.revision.root.reason", "revision_saturated", refused.reasonToken);
            Equal("m4.revision.root.atomic", before, MemoryThreadReducer.CanonicalState(root));

            MemoryReducerRoot summaryRoot = Root(10);
            summaryRoot.visibleBlocks.Add(Block(summaryRoot, 1, 1,
                MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerPolicy close = Policy(11, 1000, 1000);
            close.chapterInactivityTicks = 1;
            MemoryReducerRoot summary = MemoryThreadReducer.Reduce(summaryRoot, close).replacement;
            MemoryReducerSummary payload = summary.visibleBlocks[0].summaryPayload;
            payload.factsRevision = long.MaxValue;
            payload.factBuckets[0].contributions[0].canonicalValue = "changed-state";
            MemoryThreadReductionResult factsRefused = MemoryThreadReducer.Reduce(summary, close);
            True("m4.revision.facts.refused", factsRefused.refused);
            Equal("m4.revision.facts.reason", "revision_saturated", factsRefused.reasonToken);
        }

        private static void TestRepairPreservesNonCollidingEvidence()
        {
            MemoryReducerRoot first = Root(100);
            first.visibleBlocks.Add(Block(first, 1, 10,
                MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerRoot second = first.Clone();
            second.visibleBlocks[0].facts[0].canonicalValue = "zz-conflict";
            second.visibleBlocks.Add(Block(second, 2, 11,
                MemoryContractTokens.ImportanceRegular, false));
            True("m4.repair.complete-state-distinguishes-conflict",
                MemoryThreadReducer.CanonicalState(first)
                    != MemoryThreadReducer.CanonicalState(second));
            MemoryThreadRepairResult repaired = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { first, second }, Policy(100, 1000, 1000));
            True("m4.repair.unique.accepted", !repaired.refused);
            Equal("m4.repair.unique.root", 1, repaired.activeRoots.Count);
            Equal("m4.repair.unique.block-preserved", 2,
                repaired.activeRoots[0].visibleBlocks.Count);
            Equal("m4.repair.unique.auto-diagnostic", 1,
                repaired.automaticConflictDroppedCount);
        }

        private static void TestSummaryReferenceBounds()
        {
            MemoryReducerRoot root = Root(10);
            for (int i = 1; i <= 3; i++)
            {
                MemoryReducerBlock block = Block(root, i, i,
                    MemoryContractTokens.ImportanceMinor, false);
                block.facts[0].subjectRefIds.Add("subject-ref-" + i);
                block.facts[0].provenanceRefIds.Add("provenance-ref-" + i);
                root.visibleBlocks.Add(block);
            }
            MemoryReducerPolicy policy = Policy(11, 1000, 1000);
            policy.chapterInactivityTicks = 1;
            policy.maximumDistinctSubjects = 3; // bucket subject plus two retained refs
            policy.maximumProvenanceTotal = 2;
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            True("m4.refs.accepted", !reduced.refused);
            Equal("m4.refs.pressure-one", 1, reduced.emergencyAtomsRemoved);
            Equal("m4.refs.two-contributions", 2, TotalContributions(reduced.replacement));
        }

        private static void TestContributionReferenceConstructionAndRefusal()
        {
            List<MemoryReducerSubjectRefCandidate> candidates =
                new List<MemoryReducerSubjectRefCandidate>
                {
                    new MemoryReducerSubjectRefCandidate
                    {
                        subjectRefId = "ref-z-canonical",
                        subjectKind = MemoryContractTokens.SubjectPawn,
                        subjectId = "canonical"
                    },
                    new MemoryReducerSubjectRefCandidate
                    {
                        subjectRefId = "ref-b",
                        subjectKind = MemoryContractTokens.SubjectPawn,
                        subjectId = "other-b"
                    },
                    new MemoryReducerSubjectRefCandidate
                    {
                        subjectRefId = "ref-a",
                        subjectKind = MemoryContractTokens.SubjectPawn,
                        subjectId = "other-a"
                    },
                    new MemoryReducerSubjectRefCandidate
                    {
                        subjectRefId = "ref-a",
                        subjectKind = MemoryContractTokens.SubjectPawn,
                        subjectId = "other-a"
                    }
                };
            List<string> subjects = MemoryContributionReferencePolicy.SelectSubjectRefIds(
                candidates, MemoryContractTokens.SubjectPawn, "canonical", 2);
            Equal("m4.refs.construct.subject-count", 2, subjects.Count);
            Equal("m4.refs.construct.subject-first", "ref-a", subjects[0]);
            Equal("m4.refs.construct.subject-second", "ref-b", subjects[1]);
            List<string> provenance = MemoryContributionReferencePolicy.SelectReferenceIds(
                new List<string> { "prov-z", "prov-a", "prov-b", "prov-a" }, 2);
            Equal("m4.refs.construct.provenance-count", 2, provenance.Count);
            Equal("m4.refs.construct.provenance-first", "prov-a", provenance[0]);
            Equal("m4.refs.construct.provenance-second", "prov-b", provenance[1]);

            MemoryReducerRoot root = Root(10);
            for (int i = 1; i <= 5; i++)
                root.visibleBlocks.Add(Block(root, i, i,
                    MemoryContractTokens.ImportanceMinor, false));
            root.visibleBlocks[0].facts[0].subjectRefIds.Add("ref-a");
            root.visibleBlocks[0].facts[0].subjectRefIds.Add("ref-b");
            root.visibleBlocks[0].facts[0].subjectRefIds.Add("ref-c");
            string before = MemoryThreadReducer.CanonicalState(root);
            MemoryReducerPolicy policy = Policy(11, 1000, 1000);
            policy.targetVisibleBlocks = 4;
            policy.maximumSubjectRefsPerContribution = 2;
            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(root, policy);
            True("m4.refs.over-cap.refused", reduced.refused);
            Equal("m4.refs.over-cap.reason", "reference_descriptor_cap", reduced.reasonToken);
            True("m4.refs.over-cap.no-replacement", reduced.replacement == null);
            Equal("m4.refs.over-cap.input-atomic", before,
                MemoryThreadReducer.CanonicalState(root));
        }

        private static void TestEmptyClosedChapterCleanup()
        {
            MemoryReducerRoot root = Root(10);
            MemoryReducerChapter chapter = root.chapters[0];
            chapter.closed = true;
            chapter.closedTick = 10;
            chapter.closureReasonToken = MemoryChapterTokens.Inactivity;
            root.lastAppliedReducerRevision = MemoryThreadReducer.CurrentReducerRevision;

            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(
                root, Policy(10, 1000, 1000));
            True("m4.cleanup.empty-chapter.accepted", !reduced.refused);
            Equal("m4.cleanup.empty-chapter.removed", 0, reduced.replacement.chapters.Count);
            True("m4.cleanup.empty-root-removable",
                MemoryThreadReducer.IsRemovableEmptyRoot(reduced.replacement));
        }

        private static void TestPlayerWordingAgreement()
        {
            MemoryReducerRoot root = Root(10);
            MemoryReducerBlock authored = Block(
                root, 1, 1, MemoryContractTokens.ImportanceMinor, false);
            authored.playerWording = "Authored wording must be protected.";
            root.visibleBlocks.Add(authored);
            string before = MemoryThreadReducer.CanonicalState(root);

            MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(
                root, Policy(1000, 1, 1));
            True("m4.wording.flag-disagreement-refused", reduced.refused);
            Equal("m4.wording.flag-disagreement-reason",
                "invalid_player_wording", reduced.reasonToken);
            Equal("m4.wording.flag-disagreement-atomic", before,
                MemoryThreadReducer.CanonicalState(root));

            MemoryThreadRepairResult repair = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { root }, Policy(1000, 1, 1));
            True("m4.wording.repair-accepted", !repair.refused);
            True("m4.wording.repair-protects-authored",
                repair.activeRoots[0].visibleBlocks[0].playerEdited);
            Equal("m4.wording.repair-preserves-text", authored.playerWording,
                repair.activeRoots[0].visibleBlocks[0].playerWording);
        }

        private static void TestUnknownNewerReducerRevisionsStayInert()
        {
            MemoryReducerRoot root = Root(10);
            root.lastAppliedReducerRevision = MemoryThreadReducer.CurrentReducerRevision + 1;
            root.visibleBlocks.Add(Block(
                root, 1, 1, MemoryContractTokens.ImportanceRegular, false));
            string before = MemoryThreadReducer.CanonicalState(root);
            MemoryThreadReductionResult rootResult = MemoryThreadReducer.Reduce(
                root, Policy(10, 1000, 1000));
            True("m4.revision.newer-root-refused", rootResult.refused);
            Equal("m4.revision.newer-root-reason",
                "newer_reducer_revision", rootResult.reasonToken);
            Equal("m4.revision.newer-root-atomic", before,
                MemoryThreadReducer.CanonicalState(root));

            MemoryReducerRoot summarySource = Root(10);
            summarySource.visibleBlocks.Add(Block(
                summarySource, 1, 1, MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerPolicy close = Policy(11, 1000, 1000);
            close.chapterInactivityTicks = 1;
            MemoryReducerRoot summary = MemoryThreadReducer.Reduce(
                summarySource, close).replacement;
            summary.visibleBlocks[0].summaryPayload.reducerRevision =
                MemoryThreadReducer.CurrentReducerRevision + 1;
            string summaryBefore = MemoryThreadReducer.CanonicalState(summary);
            MemoryThreadReductionResult summaryResult = MemoryThreadReducer.Reduce(summary, close);
            True("m4.revision.newer-summary-refused", summaryResult.refused);
            Equal("m4.revision.newer-summary-reason",
                "newer_reducer_revision", summaryResult.reasonToken);
            Equal("m4.revision.newer-summary-atomic", summaryBefore,
                MemoryThreadReducer.CanonicalState(summary));
            False("m4.revision.newer-root-not-repaired",
                MemoryThreadRepairPolicy.NeedsRepair(root));
            False("m4.revision.newer-summary-not-repaired",
                MemoryThreadRepairPolicy.NeedsRepair(summary));
        }

        private static void TestIterativePressurePrefixPlanning()
        {
            List<MemoryPressureAtom> atoms = new List<MemoryPressureAtom>
            {
                new MemoryPressureAtom
                {
                    ownerPawnId = "owner",
                    rootId = "root",
                    recordId = "summary",
                    contributionId = "old-contribution",
                    importance = MemoryContractTokens.ImportanceMinor,
                    originalEventTick = 1,
                    logicalBytes = 5
                },
                new MemoryPressureAtom
                {
                    ownerPawnId = "owner",
                    rootId = "root",
                    recordId = "summary",
                    contributionId = "new-contribution",
                    importance = MemoryContractTokens.ImportanceRegular,
                    originalEventTick = 2,
                    logicalBytes = 5,
                    blockUnits = 1
                }
            };

            MemoryPressurePlan approximate = KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest
                {
                    bytesToRelease = 100,
                    atoms = atoms
                });
            False("m4.pressure.summary-approximation-would-refuse", approximate.canApply);
            MemoryPressurePlan next = KnowledgeEvictionPlanner.PlanNextMemoryPressureAtom(atoms);
            True("m4.pressure.iterative-prefix-can-apply", next.canApply);
            Equal("m4.pressure.iterative-prefix-one", 1, next.removals.Count);
            Equal("m4.pressure.iterative-prefix-oldest", "old-contribution",
                next.removals[0].contributionId);
        }

        private static void TestRepairPlacementRemapAndOpenOrder()
        {
            MemoryReducerRoot source = Root(10);
            source.visibleBlocks.Add(Block(
                source, 1, 1, MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerPolicy close = Policy(11, 1000, 1000);
            close.chapterInactivityTicks = 1;
            MemoryReducerRoot malformed = MemoryThreadReducer.Reduce(source, close).replacement;
            MemoryReducerBlock closedSummary = malformed.visibleBlocks[0];
            malformed.rootId = "legacy-root";
            malformed.chapters[0].chapterId = "legacy-chapter";
            malformed.chapters[0].closedSummaryRecordId = "legacy-summary";
            closedSummary.rootId = "legacy-root";
            closedSummary.chapterId = "legacy-chapter";
            closedSummary.recordId = "legacy-summary";
            closedSummary.sourceOccurrenceId = "legacy-summary-source";
            closedSummary.summaryPayload.factBuckets[0].contributions[0].originChapterId =
                "legacy-chapter";
            True("m4.repair.single-malformed-queued",
                MemoryThreadRepairPolicy.NeedsRepair(malformed));

            MemoryThreadRepairResult repaired = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { malformed }, close);
            True("m4.repair.placement.accepted", !repaired.refused);
            True("m4.repair.placement.changed", repaired.changed);
            MemoryReducerRoot canonical = repaired.activeRoots[0];
            string expectedRoot;
            MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
            {
                ownerPawnId = canonical.ownerPawnId,
                ownerEpochToken = canonical.ownerEpochToken,
                primarySubjectKind = canonical.subjectKind,
                primarySubjectId = canonical.subjectId
            }, out expectedRoot);
            string expectedChapter;
            MemoryIdentityCodec.TryCreateChapterId(expectedRoot, 1, out expectedChapter);
            string expectedSummary;
            MemoryIdentityCodec.TryCreateClosedSummaryId(new MemoryRootIdentity
            {
                ownerPawnId = canonical.ownerPawnId,
                ownerEpochToken = canonical.ownerEpochToken,
                primarySubjectKind = canonical.subjectKind,
                primarySubjectId = canonical.subjectId
            }, 1, out expectedSummary);
            Equal("m4.repair.placement.root", expectedRoot, canonical.rootId);
            Equal("m4.repair.placement.chapter", expectedChapter,
                canonical.chapters[0].chapterId);
            Equal("m4.repair.placement.block-chapter", expectedChapter,
                canonical.visibleBlocks[0].chapterId);
            Equal("m4.repair.placement.summary-id", expectedSummary,
                canonical.visibleBlocks[0].recordId);
            Equal("m4.repair.placement.summary-pointer", expectedSummary,
                canonical.chapters[0].closedSummaryRecordId);
            Equal("m4.repair.placement.contribution-origin", expectedChapter,
                canonical.visibleBlocks[0].summaryPayload.factBuckets[0]
                    .contributions[0].originChapterId);

            MemoryReducerRoot multipleOpen = Root(100);
            multipleOpen.visibleBlocks.Add(Block(
                multipleOpen, 2, 2, MemoryContractTokens.ImportanceRegular, false));
            string secondChapterId;
            MemoryIdentityCodec.TryCreateChapterId(multipleOpen.rootId, 2, out secondChapterId);
            multipleOpen.chapters.Add(new MemoryReducerChapter
            {
                chapterId = secondChapterId,
                ordinal = 2,
                openedTick = 50,
                lastActivityTick = 100
            });
            multipleOpen.nextChapterOrdinal = 3;
            MemoryReducerRoot permuted = multipleOpen.Clone();
            permuted.chapters.Reverse();
            MemoryThreadRepairResult orderedA = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { multipleOpen }, Policy(100, 1000, 1000));
            MemoryThreadRepairResult orderedB = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { permuted }, Policy(100, 1000, 1000));
            True("m4.repair.open.accepted-a", !orderedA.refused);
            True("m4.repair.open.accepted-b", !orderedB.refused);
            Equal("m4.repair.open.permutation",
                MemoryThreadReducer.CanonicalState(orderedA.activeRoots[0]),
                MemoryThreadReducer.CanonicalState(orderedB.activeRoots[0]));
            Equal("m4.repair.open.only-newest", 1,
                OpenChapterCount(orderedA.activeRoots[0]));
            Equal("m4.repair.open.newest-id", secondChapterId,
                OpenChapterId(orderedA.activeRoots[0]));
        }

        private static void TestRepairPublicationWinnerAndDiagnostic()
        {
            MemoryReducerRoot automaticRoot = Root(100);
            automaticRoot.visibleBlocks.Add(Block(
                automaticRoot, 1, 10, MemoryContractTokens.ImportanceRegular, false));
            MemoryReducerRoot editedRoot = automaticRoot.Clone();
            editedRoot.visibleBlocks[0].playerEdited = true;
            editedRoot.visibleBlocks[0].playerWording = "The authored winner.";
            editedRoot.visibleBlocks[0].facts[0].canonicalValue = "authored-value";
            MemoryThreadRepairResult repair = MemoryThreadRepairPolicy.Repair(
                new List<MemoryReducerRoot> { automaticRoot, editedRoot },
                Policy(100, 1000, 1000));
            True("m4.repair.publish.accepted", !repair.refused);
            MemoryReducerBlock desired = repair.activeRoots[0].visibleBlocks[0];
            Equal("m4.repair.publish.authored-selected", "The authored winner.",
                desired.playerWording);
            Equal("m4.repair.publish.source-index", 1,
                MemoryThreadRepairPolicy.FindPublicationSourceIndex(
                    new List<MemoryReducerBlock>
                    {
                        automaticRoot.visibleBlocks[0],
                        editedRoot.visibleBlocks[0]
                    }, desired));
            Equal("m4.repair.publish.diagnostic-token",
                MemoryThreadRepairPolicy.AutomaticConflictDiagnosticToken,
                MemoryThreadRepairPolicy.DiagnosticReason(repair));
        }

        private static void TestElapsedMaintenanceSlices()
        {
            MemoryMaintenanceSlicePlan notDue = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 99,
                    lastRunTick = 0,
                    intervalTicks = 100,
                    itemCount = 100,
                    maximumWorkItems = 30
                });
            False("m4.maintenance.not-modulo", notDue.due);
            MemoryMaintenanceSlicePlan first = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 1000,
                    lastRunTick = 0,
                    intervalTicks = 100,
                    itemCount = 100,
                    maximumWorkItems = 30
                });
            True("m4.maintenance.long-pause-due", first.due);
            Equal("m4.maintenance.bounded", 30, first.workItems);
            Equal("m4.maintenance.cursor", 30, first.nextItemIndex);
            False("m4.maintenance.not-complete", first.completedCycle);
            Equal("m4.maintenance.last-run-on-cycle", 0L, first.nextLastRunTick);
            MemoryMaintenanceSlicePlan dirty = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 1,
                    lastRunTick = 1,
                    intervalTicks = 100,
                    dirty = true,
                    itemCount = 2,
                    nextItemIndex = 1,
                    maximumWorkItems = 30
                });
            True("m4.maintenance.dirty-immediate", dirty.due && dirty.completedCycle);
            Equal("m4.maintenance.wrap", 0, dirty.nextItemIndex);
            True("m4.maintenance.settings-shorter", MemoryMaintenancePolicy.SettingsChangeMakesDirty(
                10, 20, 12, 9, 20, 12));
            False("m4.maintenance.settings-raise", MemoryMaintenancePolicy.SettingsChangeMakesDirty(
                10, 20, 12, 11, 21, 13));
            MemoryMaintenanceSlicePlan secondElapsedCycle = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 200,
                    lastRunTick = 100,
                    intervalTicks = 100,
                    dirty = false,
                    itemCount = 0,
                    maximumWorkItems = 1
                });
            True("m4.maintenance.second-cycle-due", secondElapsedCycle.due);
            True("m4.maintenance.second-cycle-rebuilds-snapshot",
                MemoryMaintenancePolicy.ShouldRebuildSnapshot(secondElapsedCycle, 0));
        }

        private static void TestExactPlacementAndLookup()
        {
            MemoryReducerRoot root = Root(10);
            root.frozenSubjectLabel = "same display label";
            MemoryReducerRoot other = Root(10);
            other.subjectId = OrdinalSegmentCodec.Segment("other-subject");
            string otherId;
            MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
            {
                ownerPawnId = other.ownerPawnId,
                ownerEpochToken = other.ownerEpochToken,
                primarySubjectKind = other.subjectKind,
                primarySubjectId = other.subjectId
            }, out otherId);
            other.rootId = otherId;
            other.frozenSubjectLabel = "same display label";
            Equal("m4.lookup.label-not-identity", 1, MemoryThreadLookupPolicy.FindExactRoot(
                new List<MemoryReducerRoot> { root, other }, other.ownerPawnId,
                other.ownerEpochToken, other.subjectKind, other.subjectId));

            MemoryPlacementPlan threaded = MemoryThreadLookupPolicy.PlanPlacement(
                new MemoryPlacementRequest
                {
                    ownerPawnId = root.ownerPawnId,
                    ownerEpochToken = root.ownerEpochToken,
                    routeReliable = true,
                    subjectKind = root.subjectKind,
                    subjectId = root.subjectId
                });
            True("m4.lookup.threaded", threaded.valid && threaded.threaded && !threaded.standalone);
            Equal("m4.lookup.canonical-root", root.rootId, threaded.rootId);
            MemoryPlacementPlan standalone = MemoryThreadLookupPolicy.PlanPlacement(
                new MemoryPlacementRequest
                {
                    ownerPawnId = root.ownerPawnId,
                    ownerEpochToken = root.ownerEpochToken,
                    routeReliable = false,
                    subjectKind = "bad",
                    subjectId = "bad"
                });
            True("m4.lookup.standalone", standalone.valid && standalone.standalone
                && !standalone.threaded && standalone.rootId.Length == 0);

            MemoryReducerBlock block = Block(root, 1, 1,
                MemoryContractTokens.ImportanceMinor, false);
            block.rootId = string.Empty;
            block.chapterId = string.Empty;
            Equal("m4.lookup.standalone-exact", 0,
                MemoryThreadLookupPolicy.FindExactStandalone(
                    new List<MemoryReducerBlock> { block }, block.ownerPawnId,
                    block.ownerEpochToken, block.recordId));
            Equal("m4.lookup.standalone-owner-private", -1,
                MemoryThreadLookupPolicy.FindExactStandalone(
                    new List<MemoryReducerBlock> { block }, "different-owner",
                    block.ownerEpochToken, block.recordId));
        }

        private static MemoryReducerPolicy Policy(long now, long minor, long regular)
        {
            return new MemoryReducerPolicy
            {
                nowTick = now,
                minorLifetimeTicks = minor,
                regularLifetimeTicks = regular,
                chapterInactivityTicks = 1000000,
                targetVisibleBlocks = 12,
                maximumVisibleBlocks = 128,
                maximumFactBuckets = 16,
                maximumContributionsPerBucket = 32,
                maximumContributionsPerSummary = 32
            };
        }

        private static MemoryReducerRoot Root(long lastActivityTick)
        {
            string epoch = MemoryIdentityCodec.PlanEpochAllocation(new MemoryEpochAllocationRequest
            {
                ownerPawnId = "owner",
                lastIssuedSequence = 0
            }).epochToken;
            MemoryRootIdentity identity = new MemoryRootIdentity
            {
                ownerPawnId = "owner",
                ownerEpochToken = epoch,
                primarySubjectKind = MemoryContractTokens.SubjectPawn,
                primarySubjectId = OrdinalSegmentCodec.Segment("subject")
            };
            string rootId;
            MemoryIdentityCodec.TryCreateRootId(identity, out rootId);
            string chapterId;
            MemoryIdentityCodec.TryCreateChapterId(rootId, 1, out chapterId);
            MemoryReducerRoot root = new MemoryReducerRoot
            {
                rootId = rootId,
                ownerPawnId = identity.ownerPawnId,
                ownerEpochToken = identity.ownerEpochToken,
                subjectKind = identity.primarySubjectKind,
                subjectId = identity.primarySubjectId,
                nextChapterOrdinal = 2
            };
            root.chapters.Add(new MemoryReducerChapter
            {
                chapterId = chapterId,
                ordinal = 1,
                openedTick = 0,
                lastActivityTick = lastActivityTick
            });
            return root;
        }

        private static MemoryReducerBlock Block(
            MemoryReducerRoot root,
            int number,
            long tick,
            string importance,
            bool edited)
        {
            string source = OrdinalSegmentCodec.Segment("occ-" + number);
            MemoryRecordIdentity identity = new MemoryRecordIdentity
            {
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                sourceOccurrenceId = source,
                captureRuleId = "rule",
                factDiscriminator = "fact"
            };
            string recordId;
            MemoryIdentityCodec.TryCreateRecordId(identity, out recordId);
            string factId;
            MemoryIdentityCodec.TryCreateFactId("rule", "fact", "status",
                MemoryContractTokens.SubjectPawn, root.subjectId,
                MemoryFactContractTokens.LatestState, out factId);
            MemoryReducerBlock block = new MemoryReducerBlock
            {
                recordId = recordId,
                sourceOccurrenceId = source,
                captureRuleId = "rule",
                factDiscriminator = "fact",
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                kind = MemoryContractTokens.KindEvent,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = MemoryContractTokens.CategoryPersonal,
                importance = importance,
                originalEventTick = tick,
                rootId = root.rootId,
                chapterId = root.chapters[0].chapterId,
                playerEdited = edited,
                playerWording = edited ? "edited wording " + number : string.Empty
            };
            block.facts.Add(new MemoryReducerFact
            {
                factId = factId,
                factKind = "status",
                canonicalSubjectKind = MemoryContractTokens.SubjectPawn,
                canonicalSubjectId = root.subjectId,
                aggregationToken = MemoryFactContractTokens.LatestState,
                canonicalValueKind = MemoryFactContractTokens.ValueState,
                canonicalValue = "value-" + number
            });
            return block;
        }

        private static void SetFactContract(
            MemoryReducerBlock block,
            MemoryReducerRoot root,
            string factKind,
            string aggregation,
            string valueKind,
            string value)
        {
            string factId;
            if (!MemoryIdentityCodec.TryCreateFactId(
                block.captureRuleId, block.factDiscriminator, factKind,
                MemoryContractTokens.SubjectPawn, root.subjectId, aggregation, out factId))
                throw new InvalidOperationException("Fixture fact identity failed.");
            MemoryReducerFact fact = block.facts[0];
            fact.factId = factId;
            fact.factKind = factKind;
            fact.aggregationToken = aggregation;
            fact.canonicalValueKind = valueKind;
            fact.canonicalValue = value;
        }

        private static MemoryPressureAtom Atom(
            string id,
            string importance,
            long tick,
            bool edited)
        {
            return new MemoryPressureAtom
            {
                ownerPawnId = "owner",
                rootId = "root",
                recordId = id,
                importance = importance,
                originalEventTick = tick,
                playerEdited = edited,
                logicalBytes = 1,
                blockUnits = 1
            };
        }

        private static KnowledgeOwnerLoad LegacyOwner(
            string ownerId,
            string oldestId,
            string newestId)
        {
            KnowledgeOwnerLoad owner = new KnowledgeOwnerLoad { ownerPawnId = ownerId };
            owner.records.Add(new KnowledgeRecordStub { recordId = oldestId, tick = 1 });
            owner.records.Add(new KnowledgeRecordStub { recordId = newestId, tick = 2 });
            return owner;
        }

        private static MemoryReducerBlock Find(MemoryReducerRoot root, string recordId)
        {
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].recordId == recordId) return root.visibleBlocks[i];
            return root.rollingSummaryBlock != null && root.rollingSummaryBlock.recordId == recordId
                ? root.rollingSummaryBlock : null;
        }

        private static int BlockCount(MemoryReducerRoot root)
        {
            return root.visibleBlocks.Count + (root.rollingSummaryBlock == null ? 0 : 1);
        }

        private static int EditedCount(MemoryReducerRoot root)
        {
            int total = 0;
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].playerEdited) total++;
            return total;
        }

        private static int OpenChapterCount(MemoryReducerRoot root)
        {
            int total = 0;
            for (int i = 0; i < root.chapters.Count; i++)
                if (!root.chapters[i].closed) total++;
            return total;
        }

        private static string OpenChapterId(MemoryReducerRoot root)
        {
            for (int i = 0; i < root.chapters.Count; i++)
                if (!root.chapters[i].closed) return root.chapters[i].chapterId;
            return string.Empty;
        }

        private static int TotalContributions(MemoryReducerRoot root)
        {
            int total = 0;
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                total += TotalContributions(root.visibleBlocks[i]);
            total += TotalContributions(root.rollingSummaryBlock);
            return total;
        }

        private static int TotalContributions(MemoryReducerBlock block)
        {
            if (block == null || block.summaryPayload == null) return 0;
            int total = 0;
            for (int i = 0; i < block.summaryPayload.factBuckets.Count; i++)
                total += block.summaryPayload.factBuckets[i].contributions.Count;
            return total;
        }

        private static List<string> ContributionImportance(MemoryReducerRoot root)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < root.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block.summaryPayload == null) continue;
                for (int j = 0; j < block.summaryPayload.factBuckets.Count; j++)
                    for (int k = 0; k < block.summaryPayload.factBuckets[j].contributions.Count; k++)
                        result.Add(block.summaryPayload.factBuckets[j].contributions[k].importance);
            }
            return result;
        }

        private static List<string> ContributionValues(MemoryReducerRoot root)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < root.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block.summaryPayload == null) continue;
                for (int j = 0; j < block.summaryPayload.factBuckets.Count; j++)
                    for (int k = 0; k < block.summaryPayload.factBuckets[j].contributions.Count; k++)
                        result.Add(block.summaryPayload.factBuckets[j].contributions[k].canonicalValue);
            }
            if (root.rollingSummaryBlock != null)
                for (int j = 0; j < root.rollingSummaryBlock.summaryPayload.factBuckets.Count; j++)
                    for (int k = 0; k < root.rollingSummaryBlock.summaryPayload
                        .factBuckets[j].contributions.Count; k++)
                        result.Add(root.rollingSummaryBlock.summaryPayload.factBuckets[j]
                            .contributions[k].canonicalValue);
            return result;
        }

        private static void True(string name, bool value)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("FAILED: " + name);
        }

        private static void False(string name, bool value)
        {
            True(name, !value);
        }

        private static void Equal<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("FAILED: " + name + " expected ["
                    + expected + "] got [" + actual + "]");
        }
    }
}
