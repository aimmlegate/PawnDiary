// PawnDiaryMemoryWordingAfterUseFixtureTests.cs — loaded-runtime contracts for memory reuse.
//
// These tests never queue HTTP work. They exercise the private post-publication scheduling seam
// against an inert component shell, and inspect compiled call sites to keep both automatic page
// paths wired to the owner-local cooldown clock and the background wording coordinator.
// The same shell also exercises frozen-record lookup and reflection text/receipt projection.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary;
using RimTestRedux;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Guards the runtime hand-off from a successfully published memory-bearing page to one
    /// optional, background wording refresh without changing canonical memory facts or old prose.
    /// </summary>
    public static class PawnDiaryMemoryWordingAfterUseFixtureTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const string StandaloneWordingRoot = "memory-wording-standalone-v1";
        private const string WinningVariant = "variant-after-use-winning";
        private const string LosingVariant = "variant-after-use-losing";

        /// <summary>
        /// The completed-page clock advances across its whole valid domain and fails closed at
        /// missing, negative, and saturated values instead of wrapping the reuse cooldown open.
        /// </summary>
        [Test]
        public static void CompletedEntryOrdinalAdvanceIsPositiveAndSaturating()
        {
            long next;
            Require(MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(1, out next)
                    && next == 2,
                "The first completed automatic page did not advance the owner-local clock.");
            Require(MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(
                        long.MaxValue - 1, out next)
                    && next == long.MaxValue,
                "The last representable completed-page advance was refused.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(
                        long.MaxValue, out next)
                    && next == long.MaxValue,
                "A saturated completed-page clock wrapped or changed.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(0, out next)
                    && next == 0,
                "A missing completed-page clock was treated as valid.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(-1, out next)
                    && next == -1,
                "A malformed negative completed-page clock was treated as valid.");
        }

        /// <summary>
        /// Every automatic publication adapter must advance the cooldown clock before post-use work.
        /// This checks the compiled assembly, so a refactor cannot silently leave ordinary pages,
        /// generated reflections, or direct integration pages out of the shared lifecycle.
        /// </summary>
        [Test]
        public static void MainAndReflectionPublicationWireOrdinalThenBackgroundRewrite()
        {
            MethodInfo main = RequirePrivateMethod("ApplyLlmResult");
            MethodInfo reflection = RequirePrivateMethod("ApplyMemoryReflectionCoordinatorResult");
            MethodInfo direct = typeof(DiaryGameComponent).GetMethod(
                "ApplyExternalDirectEntryText",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo advance = RequirePrivateMethod("AdvanceCompletedAutomaticPageOrdinal");
            MethodInfo schedule = RequirePrivateMethod("ScheduleUsedMemoryWording");
            Require(direct != null, "The direct-entry publication adapter was renamed.");

            int mainAdvance = DirectCallOffset(main, advance);
            int mainSchedule = DirectCallOffset(main, schedule);
            Require(mainAdvance >= 0 && mainSchedule > mainAdvance,
                "The ordinary LLM result path no longer advances the completed-page clock before "
                + "offering the background memory rewrite.");

            int directAdvance = DirectCallOffset(direct, advance);
            Require(directAdvance >= 0,
                "The direct-entry publication adapter no longer advances the completed-page clock.");

            int reflectionPublish = DirectCallOffset(reflection, direct);
            int reflectionSchedule = DirectCallOffset(reflection, schedule);
            Require(reflectionPublish >= 0 && reflectionSchedule > reflectionPublish,
                "The generated memory-reflection path no longer commits through the counted direct "
                + "publication adapter before offering the background rewrite.");
        }

        /// <summary>
        /// A successful page schedules exactly the single evidence row carried by its winning
        /// prompt variant. The current optional prose remains readable until a later coordinator
        /// result successfully replaces it.
        /// </summary>
        [Test]
        public static void PostUseSchedulingChoosesWinningSingleEvidenceAndKeepsOldWording()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Winner");
                SavedMemoryBlock winner = NewBlock(
                    owner, "record-after-use-winner", "source-after-use-winner",
                    "I still remember the old promise.", 4);
                SavedMemoryBlock loser = NewBlock(
                    owner, "record-after-use-loser", "source-after-use-loser",
                    "Another memory belongs only to the losing prompt variant.", 2);
                owner.standaloneBlocks.Add(winner);
                owner.standaloneBlocks.Add(loser);
                DiaryGameComponent component = NewComponent(owner);
                SavedActiveLogicalRequestV1 request = NewRequest(owner);
                request.frozenVariants.Add(VariantFor(LosingVariant, loser));
                request.frozenVariants.Add(VariantFor(WinningVariant, winner));

                string oldText = winner.optionalLlmWording;
                string oldFingerprint = winner.optionalLlmFingerprint;
                long oldRevision = winner.optionalLlmWordingRevision;
                long oldFormatRevision = winner.optionalLlmFormatRevision;
                int oldCategoryMask = winner.optionalLlmCategoryMask;

                InvokePostUseScheduler(
                    component,
                    request,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    60000L);

                List<SavedSummaryWordingOpportunityV1> slot = WordingSlot(component);
                SavedSummaryWordingOpportunityV1 pending = slot.Count == 1 ? slot[0] : null;
                SummaryWordingOpportunitySnapshot parsed;
                Require(pending != null
                        && pending.ownerPawnId == owner.pawnId
                        && pending.ownerEpochToken == owner.autobiographicalEpochToken
                        && pending.rootId == StandaloneWordingRoot
                        && pending.summaryRecordId == winner.recordId
                        && pending.expectedOptionalLlmWordingRevision == oldRevision
                        && pending.requestedTick == 60000L
                        && pending.dueTick == 60000L
                        && MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                            pending.opportunityKey, out parsed)
                        && parsed.summaryRecordId == winner.recordId,
                    "The post-use hook did not publish one exact opportunity for the winning "
                    + "variant's single evidence row.");
                Require(winner.optionalLlmWording == oldText
                        && winner.optionalLlmFingerprint == oldFingerprint
                        && winner.optionalLlmWordingRevision == oldRevision
                        && winner.optionalLlmFormatRevision == oldFormatRevision
                        && winner.optionalLlmCategoryMask == oldCategoryMask,
                    "Scheduling erased or mutated the current prose before a successful replacement.");
                Require(loser.optionalLlmWordingRevision == 2
                        && loser.optionalLlmWording.Contains("losing prompt variant"),
                    "The losing prompt variant's memory was mutated or selected.");
            });
        }

        /// <summary>
        /// Empty and multi-memory winning receipts fail closed: neither can guess which memory to
        /// rewrite, and neither consumes the owner's single saved wording slot.
        /// </summary>
        [Test]
        public static void ZeroOrMultipleWinningEvidenceDoesNotSchedule()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Cardinality");
                SavedMemoryBlock first = NewBlock(
                    owner, "record-after-use-first", "source-after-use-first",
                    "The first existing wording.", 1);
                SavedMemoryBlock second = NewBlock(
                    owner, "record-after-use-second", "source-after-use-second",
                    "The second existing wording.", 1);
                owner.standaloneBlocks.Add(first);
                owner.standaloneBlocks.Add(second);
                DiaryGameComponent component = NewComponent(owner);

                SavedActiveLogicalRequestV1 empty = NewRequest(owner);
                empty.frozenVariants.Add(new SavedFrozenPromptVariantV1
                {
                    variantKey = WinningVariant,
                    receiptPlan = new SavedFrozenEvidenceReceiptPlanV1()
                });
                InvokePostUseScheduler(
                    component,
                    empty,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    61000L);
                Require(WordingSlot(component).Count == 0,
                    "A receipt with zero evidence scheduled a guessed wording refresh.");

                SavedActiveLogicalRequestV1 multiple = NewRequest(owner);
                SavedFrozenPromptVariantV1 two = VariantFor(WinningVariant, first);
                two.receiptPlan.evidenceEntries.Add(EvidenceFor(second));
                multiple.frozenVariants.Add(two);
                InvokePostUseScheduler(
                    component,
                    multiple,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    62000L);
                Require(WordingSlot(component).Count == 0,
                    "A receipt with two evidence rows scheduled an ambiguous wording refresh.");
                Require(first.optionalLlmWordingRevision == 1
                        && second.optionalLlmWordingRevision == 1,
                    "A rejected cardinality path mutated an existing wording revision.");
            });
        }

        /// <summary>
        /// A failed generated reflection returns before publication, so it advances neither the
        /// completed-page ordinal nor the optional wording slot.
        /// </summary>
        [Test]
        public static void FailedReflectionPageDoesNotAdvanceOrSchedule()
        {
            PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Failed");
            SavedMemoryBlock block = NewBlock(
                owner, "record-after-use-failed", "source-after-use-failed",
                "This wording must survive a failed page.", 3);
            owner.standaloneBlocks.Add(block);
            DiaryGameComponent component = NewComponent(owner);
            SavedActiveLogicalRequestV1 request = NewRequest(owner);
            request.requestPurposeToken = MemoryDispatchTokens.MemoryReflection;
            request.frozenVariants.Add(VariantFor(WinningVariant, block));
            long ordinalBefore = owner.completedDiaryEntryOrdinal;

            MethodInfo apply = RequirePrivateMethod("ApplyMemoryReflectionCoordinatorResult");
            apply.Invoke(component, new object[]
            {
                request,
                new LlmGenerationResult
                {
                    success = false,
                    generatedText = string.Empty,
                    memoryInvocationPermit =
                        new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant }
                }
            });

            Require(owner.completedDiaryEntryOrdinal == ordinalBefore,
                "A failed generated reflection advanced the completed-page cooldown clock.");
            Require(WordingSlot(component).Count == 0,
                "A failed generated reflection scheduled a memory wording refresh.");
            Require(block.optionalLlmWordingRevision == 3
                    && block.optionalLlmWording.Contains("survive a failed page"),
                "A failed generated reflection changed the current memory prose.");
        }

        /// <summary>
        /// A later sibling changes the current root projection without deleting the pre-event
        /// selection. Revalidation still honors suppression, deletion, duplicate IDs, and edited TTL.
        /// </summary>
        [Test]
        public static void FrozenRecallSurvivesSiblingAndHonorsSavedRecordChanges()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_FrozenRecall_Regression");
                SavedMemoryThreadRoot root;
                SavedMemoryBlock old = NewThreadedRecallBlock(owner, out root);
                DiaryGameComponent component = NewComponent(owner);
                MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
                var tuning = new DiaryKnowledgeTuningDef();
                MemoryRecallQueryV2 query = RecallFixtureQuery(owner, 1000);
                object reservations = RequirePrivateMethod("SnapshotRecallReservations").Invoke(
                    component, new object[] { owner });
                var before = (List<MemoryRecallCandidateSnapshot>)RequirePrivateMethod(
                    "SnapshotRecallCandidates").Invoke(component,
                        new object[] { owner, policy, tuning, query, reservations });
                MemoryRecallSelectionResultV2 frozen = ImportantMemorySelector.SelectV2(query, before);
                Require(frozen.selected.Count == 1, "The fixture did not freeze the old memory.");

                SavedMemoryBlock later = NewBlock(owner, "record-later-sibling", "source-later-sibling",
                    "Later sibling must not enter the old page.", 0);
                later.rootId = root.rootId;
                later.chapterId = old.chapterId;
                later.originalEventTick = 200;
                root.visibleBlocks.Add(later);
                Func<MemoryRecallSelectionResultV2> revalidate = () =>
                {
                    var current = (List<MemoryRecallCandidateSnapshot>)RequirePrivateMethod(
                        "SnapshotFrozenRecallCandidates").Invoke(component,
                            new object[] { owner, policy, tuning, query, reservations, frozen });
                    return ImportantMemorySelector.RevalidateFrozenV2(frozen, query, current);
                };
                MemoryRecallSelectionResultV2 retained = revalidate();
                Require(retained.selected.Count == 1
                        && retained.selected[0].evidence.recordId == old.recordId
                        && retained.selected[0].candidate.historicalText == old.automaticWording,
                    "The newer sibling replaced or hid the exact pre-event memory.");
                root.visibleBlocks.Add(old);
                Require(revalidate().selected.Count == 0, "A duplicate saved identity was silently chosen.");
                root.visibleBlocks.RemoveAt(root.visibleBlocks.Count - 1);
                old.suppressed = true;
                Require(revalidate().selected.Count == 0, "Suppression failed on the frozen lookup path.");
                old.suppressed = false;

                old.importance = MemoryContractTokens.ImportanceMinor;
                old.playerEdited = true;
                old.playerWording = "A protected edited memory.";
                query.repetitionPolicy.currentTick = old.originalEventTick + policy.minorMemoryLifetimeTicks + 1;
                retained = revalidate();
                Require(retained.selected.Count == 1
                        && retained.selected[0].candidate.historicalText == old.playerWording,
                    "An edited memory became unusable after its ordinary TTL.");
                old.playerEdited = false;
                old.playerWording = string.Empty;
                Require(revalidate().selected.Count == 0,
                    "Removing edit protection failed to restore the original TTL.");
                old.playerEdited = true;
                root.visibleBlocks.Remove(old);
                Require(revalidate().selected.Count == 0,
                    "Deleting frozen evidence selected its later sibling as a replacement.");
            });
        }

        /// <summary>
        /// The real pre-stage adapter preserves current truth and all structural receipts after a
        /// wording-only send, then refuses cooldowns, active reservations, and prior narrative use.
        /// No transport queue or game mutation is involved.
        /// </summary>
        [Test]
        public static void ReflectionProjectionKeepsTruthAndGuardsAfterWordingOnlySend()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_ReflectionRecall_Regression");
                owner.completedDiaryEntryOrdinal = 10;
                SavedMemoryThreadRoot root;
                SavedMemoryBlock block = NewThreadedRecallBlock(owner, out root);
                block.importance = MemoryContractTokens.ImportanceImportant;
                block.optionalLlmWording = "Then we married.";
                // NewBlock already has a valid wording fingerprint and confirmed provider exposure,
                // while its narrative inclusion counter is still zero.
                owner.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
                {
                    snapshotId = "snapshot-current-divorce",
                    subjectKind = MemoryContractTokens.SubjectPawn,
                    subjectId = root.subjectId,
                    factStreamToken = "relationship",
                    trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                    lastObservedTick = 999,
                    stateFacts = new List<SavedMemoryStateFact>
                    {
                        new SavedMemoryStateFact { factKey = "relationship", factValue = "divorced" }
                    }
                });
                DiaryGameComponent component = NewComponent(owner);
                MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
                var tuning = new DiaryKnowledgeTuningDef();
                Func<MemoryRecallPromptProjection> project = () =>
                    (MemoryRecallPromptProjection)RequirePrivateMethod("BuildMemoryReflectionPrompt").Invoke(
                        component, new object[]
                        {
                            owner, block, ReflectionWorkClassTokens.MeaningfulMemory, policy, tuning, 1000L
                        });
                Func<SavedMemoryBlock> select = () =>
                    (SavedMemoryBlock)RequirePrivateMethod("SelectOptionalReflectionBlock").Invoke(
                        component, new object[] { owner, policy, true, 1000L, tuning, 0L });
                Require(ReferenceEquals(select(), block),
                    "Wording-only provider exposure prevented a meaningful reflection.");
                MemoryRecallPromptProjection projection = project();
                Require(projection.text.Contains("Then we married.") && projection.text.Contains("divorced"),
                    "Reflection prompt dropped historical wording or authoritative current truth.");
                Require(projection.evidence.Count == 1 && projection.guards.Count == 4
                        && projection.diagnostics.Exists(row => row.sourceId == "snapshot-current-divorce"),
                    "Reflection prompt lost exact evidence, structural guards, or current-state provenance.");

                string subjectKey = MemoryRepetitionGuardPolicy.SubjectKey(root.subjectKind, root.subjectId);
                owner.repetitionGuardRows.Add(new SavedMemoryRepetitionGuardRow
                {
                    ownerEpochToken = owner.autobiographicalEpochToken,
                    guardKind = MemoryRepetitionGuardKinds.Subject,
                    guardKey = subjectKey,
                    automaticInclusionCount = 1,
                    lastAutomaticIncludedTick = 999,
                    lastAutomaticIncludedEntryOrdinal = owner.completedDiaryEntryOrdinal
                });
                Require(select() == null && project().evidence.Count == 0,
                    "A cooling subject reached optional selection or its pre-stage prompt.");
                owner.repetitionGuardRows.Clear();
                SavedActiveLogicalRequestV1 reserved = NewRequest(owner);
                reserved.reservedGuardEntries.Add(new SavedFrozenGuardEntryV1
                {
                    guardKind = MemoryRepetitionGuardKinds.Subject,
                    guardKey = subjectKey
                });
                SetPrivateField(component, "activeMemoryCoordinatorRequests",
                    new List<SavedActiveLogicalRequestV1> { reserved });
                Require(select() == null && project().evidence.Count == 0,
                    "An active subject reservation was ignored by an optional reflection.");
                SetPrivateField(component, "activeMemoryCoordinatorRequests",
                    new List<SavedActiveLogicalRequestV1>());
                block.automaticInclusionCount = 1;
                Require(select() == null && project().evidence.Count == 0,
                    "A narrative-use receipt allowed a second memory-created page.");
            });
        }

        /// <summary>
        /// Calls the production adapter with detached saved rows only. Exact diplomacy must augment
        /// knownness, while an unavailable generation must not leave a misleading membership line.
        /// </summary>
        [Test]
        public static void FactionRecallProjectsExactGlobalDiplomacy()
        {
            PawnKnowledgeState owner = NewOwner("Pawn_FactionRecall_Regression");
            string subject;
            Require(MemoryIdentityCodec.TryCreateFactionSubjectId("Faction_RecallRegression", 2,
                out subject), "Could not create the exact faction subject.");
            owner.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
            {
                snapshotId = "known-faction-recall-regression",
                scopeKindToken = KnowledgeObservationTokens.ScopeFaction,
                subjectKind = MemoryContractTokens.SubjectFaction,
                subjectId = subject,
                factStreamToken = KnowledgeObservationTokens.StreamFactionConnection,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                stateFacts = new List<SavedMemoryStateFact>
                {
                    new SavedMemoryStateFact { factKey = KnowledgeObservationTokens.FactConnectionKind,
                        factValue = KnowledgeObservationTokens.ConnectionFamily }
                }
            });
            var global = new SavedGlobalFactionSnapshot
            {
                factionInstanceId = "Faction_RecallRegression", allocatorGeneration = 2,
                relationKindToken = "Hostile", goodwill = -90, leaderPawnId = "Pawn_CurrentLeader",
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 2, observedTick = 100
            };
            DiaryGameComponent component = NewComponent(owner);
            SetPrivateField(component, "globalFactionSnapshots", new List<SavedGlobalFactionSnapshot> { global });
            Func<MemoryRecallCandidateSnapshot> project = () =>
            {
                var candidate = new MemoryRecallCandidateSnapshot();
                ImportantMemorySelector.TryAddExactRoute(candidate.exactRoutes,
                    MemoryContractTokens.SubjectFaction, subject);
                RequirePrivateMethod("ApplyCurrentTruth").Invoke(component,
                    new object[] { candidate, owner, new DiaryKnowledgeTuningDef() });
                return candidate;
            };
            MemoryRecallCandidateSnapshot projected = project();
            Require(projected.currentStateCanRender && projected.currentStateApplicable
                && projected.currentStateText.Contains("relation_kind=Hostile")
                && projected.currentStateText.Contains("goodwill=-90")
                && projected.currentStateSourceId == subject,
                "Recall failed to join exact global diplomacy and provenance to owner awareness.");
            global.allocatorGeneration = 3;
            projected = project();
            Require(projected.currentStateApplicable && projected.currentStateContradictsHistorical
                && !projected.currentStateCanRender && projected.currentStateText.Length == 0,
                "A recycled faction instance leaked state or left a misleading partial correction.");
            global.allocatorGeneration = 2;
            owner.ownerAwarenessSnapshots.Clear();
            projected = project();
            Require(!projected.currentStateApplicable && projected.currentStateText.Length == 0,
                "Global faction knowledge bypassed the owner's awareness boundary.");
        }

        private static SavedMemoryBlock NewThreadedRecallBlock(
            PawnKnowledgeState owner,
            out SavedMemoryThreadRoot root)
        {
            root = new SavedMemoryThreadRoot
            {
                rootId = "root-recall-regression",
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                subjectKind = MemoryContractTokens.SubjectPawn,
                subjectId = "Pawn_RecallRegression_Subject"
            };
            SavedMemoryBlock block = NewBlock(owner, "record-recall-regression", "source-recall-regression",
                string.Empty, 0);
            block.rootId = root.rootId;
            block.chapterId = "chapter-recall-regression";
            block.originalEventTick = 100;
            root.visibleBlocks.Add(block);
            owner.threadRoots.Add(root);
            return block;
        }

        private static MemoryRecallQueryV2 RecallFixtureQuery(PawnKnowledgeState owner, long nowTick)
        {
            var query = new MemoryRecallQueryV2
            {
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                consumerId = MemoryRecallConsumerRegistry.OrdinaryDiary,
                writingFormat = MemoryRecallWritingFormats.Full,
                currentEventId = "event-recall-regression",
                currentSourceOccurrenceId = "event-recall-regression",
                useMemoriesInWriting = true,
                repetitionPolicy = new MemoryRepetitionPolicySnapshot
                {
                    currentTick = nowTick,
                    completedDiaryEntryOrdinal = 10,
                    ticksPerDay = 60000
                }
            };
            query.enabledCategories.Add(MemoryContractTokens.CategoryPersonal);
            ImportantMemorySelector.TryAddExactRoute(query.exactRoutes,
                MemoryContractTokens.SubjectPawn, "Pawn_RecallRegression_Subject");
            return query;
        }

        private static PawnKnowledgeState NewOwner(string ownerPawnId)
        {
            PawnKnowledgeState owner = PawnKnowledgeState.CreateCurrent(ownerPawnId);
            owner.autobiographicalEpochToken = EpochToken(91);
            return owner;
        }

        private static SavedMemoryBlock NewBlock(
            PawnKnowledgeState owner,
            string recordId,
            string sourceOccurrenceId,
            string oldWording,
            long wordingRevision)
        {
            const long formatRevision = 1;
            string canonical = "A lasting canonical fact for " + recordId + ".";
            string fingerprint;
            Require(MemoryOptionalAiPolicy.TryCreateBlockWordingProjectionFingerprint(
                    recordId,
                    MemoryContractTokens.KindEvent,
                    MemoryContractTokens.CategoryPersonal,
                    canonical,
                    formatRevision,
                    out fingerprint),
                "The fixture could not fingerprint its canonical memory wording.");
            return new SavedMemoryBlock
            {
                recordId = recordId,
                sourceOccurrenceId = sourceOccurrenceId,
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                kind = MemoryContractTokens.KindEvent,
                category = MemoryContractTokens.CategoryPersonal,
                importance = MemoryContractTokens.ImportanceRegular,
                automaticWording = canonical,
                optionalLlmWording = oldWording,
                optionalLlmWordingRevision = wordingRevision,
                optionalLlmFingerprint = fingerprint,
                optionalLlmFormatRevision = formatRevision,
                optionalLlmCategoryMask = MemoryCategoryBits.Personal,
                formatRevision = 1,
                providerExposureState = "confirmed_sent"
            };
        }

        private static DiaryGameComponent NewComponent(PawnKnowledgeState owner)
        {
            var record = new PawnDiaryRecord
            {
                pawnId = owner.pawnId,
                knowledgeState = owner
            };
            DiaryGameComponent component = PawnDiaryMemoryM1FixtureTests.NewMemoryComponent(
                new List<PawnDiaryRecord> { record },
                new List<SavedActiveLogicalRequestV1>(),
                new List<SavedImportedMemoryRow>());
            SetPrivateField(component, "diariesById",
                new Dictionary<string, PawnDiaryRecord>(StringComparer.Ordinal)
                {
                    { owner.pawnId, record }
                });
            SetPrivateField(component, "globalOptionalRequestCancellationGeneration", 7L);
            return component;
        }

        private static SavedActiveLogicalRequestV1 NewRequest(PawnKnowledgeState owner)
        {
            return new SavedActiveLogicalRequestV1
            {
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken
            };
        }

        private static SavedFrozenPromptVariantV1 VariantFor(
            string variantKey,
            SavedMemoryBlock block)
        {
            var receipt = new SavedFrozenEvidenceReceiptPlanV1();
            receipt.evidenceEntries.Add(EvidenceFor(block));
            return new SavedFrozenPromptVariantV1
            {
                variantKey = variantKey,
                receiptPlan = receipt
            };
        }

        private static SavedFrozenEvidenceEntryV1 EvidenceFor(SavedMemoryBlock block)
        {
            return new SavedFrozenEvidenceEntryV1
            {
                recordId = block.recordId,
                sourceOccurrenceId = block.sourceOccurrenceId,
                rootIdOrEmpty = block.rootId ?? string.Empty
            };
        }

        private static void InvokePostUseScheduler(
            DiaryGameComponent component,
            SavedActiveLogicalRequestV1 request,
            MemoryInvocationCommitPermitV1 permit,
            long nowTick)
        {
            RequirePrivateMethod("ScheduleUsedMemoryWording").Invoke(
                component, new object[] { request, permit, nowTick });
        }

        private static List<SavedSummaryWordingOpportunityV1> WordingSlot(
            DiaryGameComponent component)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                "summaryWordingOpportunities", PrivateInstance);
            Require(field != null, "The saved wording-slot fixture seam was renamed.");
            var value = field.GetValue(component) as List<SavedSummaryWordingOpportunityV1>;
            Require(value != null, "The saved wording slot was not initialized.");
            return value;
        }

        private static void WithOptionalMemoryPolicy(Action body)
        {
            MemoryPolicySnapshot prior = MemoryEffectivePolicyProvider.Current;
            Require(prior != null, "No published memory policy was available to the fixture.");
            MemorySettingsPolicyFieldsV1 fields = prior.ToFields();
            fields.saveNewMemories = true;
            fields.useMemoriesInWriting = true;
            fields.allowExtraMemoryAiRequests = true;
            fields.memoryCategoryMask |= MemoryCategoryBits.Personal;
            fields.optionalRequestInvalidationGeneration = Math.Max(
                1L, fields.optionalRequestInvalidationGeneration);
            MemoryPolicySnapshot enabled = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                MemoryPolicyDefAdapter.Bounds());
            Require(enabled.AllowsOptionalRequests
                    && MemoryEffectivePolicyProvider.Publish(enabled),
                "The fixture could not publish optional memory policy.");
            try
            {
                body();
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(prior);
            }
        }

        private static MethodInfo RequirePrivateMethod(string name)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(name, PrivateInstance);
            Require(method != null, "The private runtime fixture seam '" + name + "' was renamed.");
            return method;
        }

        private static int DirectCallOffset(MethodInfo caller, MethodInfo callee)
        {
            byte[] body = caller?.GetMethodBody()?.GetILAsByteArray();
            if (body == null || callee == null) return -1;
            byte[] token = BitConverter.GetBytes(callee.MetadataToken);
            for (int index = 0; index + token.Length < body.Length; index++)
            {
                // ECMA-335 call and callvirt both carry a four-byte metadata token immediately after
                // their one-byte opcode. Both target methods are private members of this same type,
                // so their MethodDef token is emitted directly rather than through a MemberRef.
                if (body[index] != 0x28 && body[index] != 0x6F) continue;
                bool match = true;
                for (int byteIndex = 0; byteIndex < token.Length; byteIndex++)
                {
                    if (body[index + 1 + byteIndex] == token[byteIndex]) continue;
                    match = false;
                    break;
                }
                if (match) return index;
            }
            return -1;
        }

        private static void SetPrivateField(
            DiaryGameComponent component,
            string name,
            object value)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(name, PrivateInstance);
            Require(field != null, "The private component field '" + name + "' was renamed.");
            field.SetValue(component, value);
        }

        private static string EpochToken(long sequence)
        {
            return OrdinalSegmentCodec.Segment("memory-epoch-v1")
                + OrdinalSegmentCodec.Segment(sequence.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
