// PawnDiaryMemoryM2FixtureTests.cs — LOADED RimTest coverage for the dormant M2 dispatch envelope.
//
// COMPILE-ONLY FOR AGENTS. These fixtures use real Scribe when the user next runs RimTest, but they
// never contact a provider: request/permit transitions remain detached and no HTTP worker is started.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PawnDiary;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    public static class PawnDiaryMemoryM2FixtureTests
    {
        [Test]
        public static void DiaryEventAcceptedPromptAndActiveRequestRoundTrip()
        {
            SavedActiveLogicalRequestV1 active = NewSavedRequest();
            DiaryEvent source = new DiaryEvent
            {
                eventId = "Event_M2_Scribe",
                tick = 123,
                date = "1st of Aprimay",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "fixture=true",
                instruction = "fixture",
                colorCue = DiaryEvent.WhiteColorCue,
                solo = true,
                initiatorPawnId = "Pawn_M2"
            };
            source.SetAcceptedPromptPair(
                DiaryEvent.InitiatorRole,
                "exact system\r\nfixture",
                "exact user\nfixture");
            source.SetActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole, active);

            RunWithTempFile(path =>
            {
                DiaryEvent saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, "event"));
                DiaryEvent loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "event"));

                Require(loaded != null, "M2 DiaryEvent did not round-trip.");
                Require(loaded.AcceptedSystemPromptForRole(DiaryEvent.InitiatorRole)
                        == "exact system\r\nfixture"
                    && loaded.PromptForRole(DiaryEvent.InitiatorRole)
                        == "exact user\nfixture",
                    "The exact accepted prompt pair changed through Scribe.");
                SavedActiveLogicalRequestV1 loadedRequest =
                    loaded.ActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole);
                Require(loadedRequest != null
                        && loadedRequest.logicalRequestId == active.logicalRequestId
                        && loadedRequest.frozenVariants.Count == 1,
                    "The POV-owned active request did not round-trip.");
                Require(MemoryDispatchPolicy.ValidateRequest(
                        MemoryDispatchSavedAdapter.ToSnapshot(loadedRequest)),
                    "The round-tripped active request no longer validates.");
            });
        }

        [Test]
        public static void SavedAdapterCommitsPermitAndReceiptBeforeResult()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            Require(saved.requestStateToken == MemoryRequestStateMachineContracts.Staged,
                "Fixture must begin staged.");
            Require(MemoryDispatchSavedAdapter.TryActivate(saved),
                "A valid committed row did not activate.");

            SavedActiveLogicalAttemptV1 attempt;
            Require(MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    saved.frozenVariants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out attempt),
                "Initial physical attempt did not prepare.");
            MemoryDispatchFenceSnapshot fence = Fence(saved);
            MemoryInvocationCommitPlan invocation;
            Require(MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved, 1, fence, 0, 456, out invocation),
                "Invocation permit transaction did not commit.");
            Require(invocation.permit != null
                    && MemoryDispatchPolicy.PermitFingerprintIsValid(invocation.permit),
                "Committed permit fingerprint is invalid.");
            Require(!MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Result publication was allowed before its receipt.");
            Require(MemoryDispatchSavedAdapter.MarkReceiptApplied(
                    saved, 1, MemoryDispatchTokens.Success, 500),
                "Invocation receipt did not apply.");
            Require(MemoryDispatchSavedAdapter.MarkReceiptApplied(
                    saved, 1, MemoryDispatchTokens.Success, 501),
                "Idempotent receipt replay was rejected.");
            Require(!MemoryDispatchSavedAdapter.MarkReceiptApplied(
                    saved, 1, MemoryDispatchTokens.ProviderError, 501),
                "A duplicate receipt changed its committed terminal outcome.");
            Require(MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Result did not publish after receipt.");
            Require(!MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Duplicate result publication was not rejected.");
        }

        /// <summary>
        /// The owner-local dispatch refresh must account for both the component-owned request row and
        /// its referenced owner after permit, receipt, and result mutations. Each incremental snapshot
        /// is compared to an immediate full byte-index rebuild of the same saved truth.
        /// </summary>
        [Test]
        public static void IncrementalDispatchIndexesMatchFullRebuildAtEveryStage()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            PawnKnowledgeState state = NewAccountingState(out root, out block);
            DiaryGameComponent component = PawnDiaryMemoryM1FixtureTests.NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = state.pawnId, knowledgeState = state }
                },
                new List<SavedActiveLogicalRequestV1> { saved },
                null);
            component.RebuildMemorySizeIndexes();
            MethodInfo refresh = typeof(DiaryGameComponent).GetMethod(
                "RefreshMemoryDispatchSizeIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo applyAccounting = typeof(DiaryGameComponent).GetMethod(
                "ApplyInvocationAccounting",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(refresh != null && applyAccounting != null,
                "A dispatch index/accounting fixture seam was renamed.");

            Require(MemoryDispatchSavedAdapter.TryActivate(saved),
                "The dispatch index fixture could not activate its request.");
            SavedActiveLogicalAttemptV1 attempt;
            Require(MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    saved.frozenVariants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out attempt),
                "The dispatch index fixture could not prepare its attempt.");
            MemoryInvocationCommitPlan invocation;
            Require(MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved, 1, Fence(saved), 0, 456, out invocation),
                "The dispatch index fixture could not commit its permit.");
            Require((bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        saved.frozenVariants[0],
                        invocation,
                        saved,
                        456L
                    }),
                "The dispatch index fixture could not apply permit accounting.");
            refresh.Invoke(component, new object[] { state });
            RequireIncrementalIndexParity(component, state, "permit");

            Require(DiaryGameComponent.ApplyConfirmedExposure(
                    state, saved.frozenVariants[0], 456),
                "The dispatch index fixture could not apply confirmed exposure.");
            Require(MemoryDispatchSavedAdapter.MarkReceiptApplied(
                    saved, 1, MemoryDispatchTokens.Success, 500),
                "The dispatch index fixture could not apply its receipt.");
            refresh.Invoke(component, new object[] { state });
            RequireIncrementalIndexParity(component, state, "receipt");

            Require(MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "The dispatch index fixture could not apply its result.");
            refresh.Invoke(component, new object[] { state });
            RequireIncrementalIndexParity(component, state, "result");
        }

        /// <summary>
        /// Potential exposure and narrative use are distinct status mutations. Both advance the exact
        /// owner/root revision once; only narrative use spends the inclusion counter.
        /// </summary>
        [Test]
        public static void PotentialExposureAndNarrativeUseAdvanceStatusDomains()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            PawnKnowledgeState state = NewAccountingState(out root, out block);
            DiaryGameComponent component = PawnDiaryMemoryM1FixtureTests.NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = state.pawnId, knowledgeState = state }
                },
                new List<SavedActiveLogicalRequestV1> { saved },
                null);
            component.RebuildMemorySizeIndexes();
            MethodInfo applyAccounting = typeof(DiaryGameComponent).GetMethod(
                "ApplyInvocationAccounting",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(applyAccounting != null,
                "The invocation-accounting fixture seam was renamed.");

            MemoryInvocationCommitPlan potential = new MemoryInvocationCommitPlan
            {
                applyPotentialExposure = true
            };
            Require((bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        saved.frozenVariants[0],
                        potential,
                        saved,
                        600L
                    })
                    && block.providerExposureState == "potentially_sent"
                    && block.lastProviderExposureTick == 600
                    && block.automaticInclusionCount == 0
                    && state.statusRevision == 6
                    && root.statusRevision == 8,
                "Potential exposure did not advance only the exact status domain.");

            MemoryInvocationCommitPlan narrative = new MemoryInvocationCommitPlan
            {
                applyNarrativeUse = true
            };
            Require((bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        saved.frozenVariants[0],
                        narrative,
                        saved,
                        601L
                    })
                    && block.providerExposureState == "potentially_sent"
                    && block.lastProviderExposureTick == 601
                    && block.automaticInclusionCount == 1
                    && block.lastAutomaticIncludedTick == 601
                    && state.statusRevision == 7
                    && root.statusRevision == 9,
                "Narrative use did not advance inclusion and status exactly once.");
        }

        /// <summary>
        /// Recall evaluates a missing structural row as unused zero-state, so the first winning
        /// narrative invocation must materialize every frozen root/subject/pair/novelty cooldown.
        /// Later uses update those exact rows instead of appending a second copy.
        /// </summary>
        [Test]
        public static void NarrativeUseMaterializesEveryFrozenStructuralGuardOnce()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            SavedFrozenPromptVariantV1 variant = saved.frozenVariants[0];
            AddCanonicalStructuralGuards(variant.receiptPlan);
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            PawnKnowledgeState state = NewAccountingState(out root, out block);
            state.completedDiaryEntryOrdinal = 9;
            DiaryGameComponent component = PawnDiaryMemoryM1FixtureTests.NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = state.pawnId, knowledgeState = state }
                },
                new List<SavedActiveLogicalRequestV1> { saved },
                null);
            component.RebuildMemorySizeIndexes();
            MethodInfo applyAccounting = typeof(DiaryGameComponent).GetMethod(
                "ApplyInvocationAccounting",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo refresh = typeof(DiaryGameComponent).GetMethod(
                "RefreshMemoryDispatchSizeIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(applyAccounting != null && refresh != null,
                "A structural-guard accounting/index fixture seam was renamed.");

            MemoryInvocationCommitPlan narrative = new MemoryInvocationCommitPlan
            {
                applyNarrativeUse = true
            };
            Require((bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        variant,
                        narrative,
                        saved,
                        700L
                    }),
                "The first narrative use did not commit guard accounting.");
            RequireStructuralGuardRows(
                state,
                saved,
                variant,
                expectedCount: 1,
                expectedTick: 700,
                expectedEntryOrdinal: 9);
            refresh.Invoke(component, new object[] { state });
            RequireIncrementalIndexParity(component, state, "structural guard materialization");
            SavedMemoryRepetitionGuardRow[] firstRows =
                state.repetitionGuardRows.ToArray();

            Require((bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        variant,
                        narrative,
                        saved,
                        701L
                    }),
                "A later narrative use did not update guard accounting.");
            RequireStructuralGuardRows(
                state,
                saved,
                variant,
                expectedCount: 2,
                expectedTick: 701,
                expectedEntryOrdinal: 9);
            Require(state.repetitionGuardRows.Count == firstRows.Length
                    && block.automaticInclusionCount == 2,
                "Repeated materialization duplicated a structural row or changed record semantics.");
            for (int index = 0; index < firstRows.Length; index++)
            {
                Require(ReferenceEquals(firstRows[index], state.repetitionGuardRows[index]),
                    "Repeated materialization replaced an existing structural guard row.");
            }

            long statusBeforeRejectedUse = state.statusRevision;
            firstRows[0].automaticInclusionCount = long.MaxValue;
            Require(!(bool)applyAccounting.Invoke(component, new object[]
                    {
                        state,
                        variant,
                        narrative,
                        saved,
                        702L
                    })
                    && state.statusRevision == statusBeforeRejectedUse
                    && state.repetitionGuardRows.Count == 4
                    && block.automaticInclusionCount == 2
                    && block.lastAutomaticIncludedTick == 701
                    && firstRows[1].automaticInclusionCount == 2
                    && firstRows[1].lastAutomaticIncludedTick == 701,
                "A saturated structural guard allowed a partial narrative-use mutation.");
        }

        [Test]
        public static void BrainwipeFenceRejectsOldPermitAndLoadedRowsNeverResend()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            Require(MemoryDispatchSavedAdapter.TryActivate(saved), "Fixture activation failed.");
            SavedActiveLogicalAttemptV1 attempt;
            Require(MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    saved.frozenVariants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out attempt),
                "Fixture attempt preparation failed.");
            MemoryDispatchFenceSnapshot oldFence = Fence(saved);
            MemoryInvocationCommitPlan invocation;
            Require(MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved, 1, oldFence, 0, 789, out invocation),
                "Fixture invocation failed.");

            MemoryDispatchFenceSnapshot postWipeFence = Fence(saved);
            postWipeFence.ownerEpochToken = EpochToken(99);
            postWipeFence.ownerCancellationGeneration++;
            MemoryTerminalCallbackPlan stale =
                MemoryDispatchSavedAdapter.PlanTerminalCallback(
                    saved,
                    invocation.permit,
                    postWipeFence,
                    MemoryDispatchTokens.Success,
                    true);
            Require(!stale.accepted && stale.outcomeToken == MemoryDispatchTokens.Stale,
                "An old-epoch result survived the Brainwipe fence.");

            MemoryLoadSettlementPlan load = MemoryDispatchPolicy.PlanLoadedRequestSettlement(
                MemoryDispatchSavedAdapter.ToSnapshot(saved));
            Require(load.valid && load.hadCommittedInvocation
                    && !load.restoreNormalPovRetryable
                    && load.outcomeToken
                        == MemoryDispatchTokens.LoadInterruptedAfterInvocation,
                "A loaded invoked request was not terminally settled without resend.");
        }

        [Test]
        public static void InvalidLoadedRowsFailClosedAndFutureSchemasRejectEarly()
        {
            SavedActiveLogicalRequestV1 definitelyBeforeInvocation = NewSavedRequest();
            Require(!MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(
                    definitelyBeforeInvocation),
                "A valid staged row was not recognized as safely pre-invocation.");

            SavedActiveLogicalRequestV1 malformedInvoked = NewSavedRequest();
            malformedInvoked.activeAttempts.Add(new SavedActiveLogicalAttemptV1
            {
                schemaVersion = 1,
                attemptOrdinal = 1,
                variantKey = malformedInvoked.frozenVariants[0].variantKey,
                attemptOriginToken = MemoryDispatchTokens.Initial,
                attemptStateToken = MemoryRequestStateMachineContracts.AttemptInvocationCommitted,
                invocationSequence = 1,
                invocationTick = 99
            });
            Require(MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(malformedInvoked),
                "Malformed loaded invocation evidence was allowed to retry.");

            SavedActiveLogicalRequestV1 missingIssuedAttempt = NewSavedRequest();
            missingIssuedAttempt.requestStateToken =
                MemoryRequestStateMachineContracts.Activated;
            missingIssuedAttempt.lastIssuedAttemptOrdinal = 1;
            Require(MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(
                    missingIssuedAttempt),
                "An invalid activated row with a missing issued attempt was allowed to retry.");

            SavedActiveLogicalRequestV1 missingWinnerAttempt = NewSavedRequest();
            missingWinnerAttempt.requestStateToken =
                MemoryRequestStateMachineContracts.Activated;
            missingWinnerAttempt.narrativeUseWinnerAttemptOrdinal = 1;
            missingWinnerAttempt.narrativeUseWinnerVariantKey =
                missingWinnerAttempt.frozenVariants[0].variantKey;
            Require(MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(
                    missingWinnerAttempt),
                "An invalid activated row with missing winner evidence was allowed to retry.");

            SavedActiveLogicalRequestV1 futureOuter = NewSavedRequest();
            futureOuter.schemaVersion = 2;
            RequireThrowsNewer(() =>
                DiaryGameComponent.RequireActiveMemoryRequestNotNewer(futureOuter));

            SavedActiveLogicalRequestV1 futureNested = NewSavedRequest();
            futureNested.frozenVariants[0].schemaVersion = 2;
            RequireThrowsNewer(() =>
                DiaryGameComponent.RequireActiveMemoryRequestNotNewer(futureNested));
        }

        [Test]
        public static void AcceptedPromptPairSurvivesRegenerationPreparationAndRollback()
        {
            DiaryEvent diaryEvent = new DiaryEvent();
            diaryEvent.SetAcceptedPromptPair(
                DiaryEvent.InitiatorRole, "accepted-system", "accepted-user");
            diaryEvent.PrepareForAcceptedPromptRegeneration(DiaryEvent.InitiatorRole);
            Require(diaryEvent.AcceptedSystemPromptForRole(DiaryEvent.InitiatorRole)
                    == "accepted-system"
                && diaryEvent.PromptForRole(DiaryEvent.InitiatorRole) == "accepted-user",
                "Regeneration preparation split the exact accepted prompt pair.");
            diaryEvent.RollBackQueuedBeforeActivation(DiaryEvent.InitiatorRole);
            Require(diaryEvent.AcceptedSystemPromptForRole(DiaryEvent.InitiatorRole)
                    == "accepted-system"
                && diaryEvent.PromptForRole(DiaryEvent.InitiatorRole) == "accepted-user",
                "Activation rollback split the exact accepted prompt pair.");
        }

        /// <summary>
        /// Provider receipts are status-only mutations: the exact block, its parent root, and its
        /// owner advance once, while duplicate receipts and saturated revisions commit nothing.
        /// </summary>
        [Test]
        public static void ConfirmedExposureAdvancesOwnerAndRootStatusExactlyOnce()
        {
            SavedActiveLogicalRequestV1 request = NewSavedRequest();
            SavedFrozenPromptVariantV1 variant = request.frozenVariants[0];
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent("Pawn_M2");
            state.autobiographicalEpochToken = EpochToken(31);
            state.statusRevision = 5;
            SavedMemoryBlock block = new SavedMemoryBlock
            {
                schemaVersion = 1,
                recordId = "record-m2",
                sourceOccurrenceId = "source-m2",
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = state.autobiographicalEpochToken,
                rootId = "root-m2",
                providerExposureState = "potentially_sent",
                lastProviderExposureTick = 100
            };
            SavedMemoryThreadRoot root = new SavedMemoryThreadRoot
            {
                schemaVersion = 1,
                rootId = "root-m2",
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = state.autobiographicalEpochToken,
                statusRevision = 7
            };
            root.visibleBlocks.Add(block);
            state.threadRoots.Add(root);

            Require(DiaryGameComponent.ApplyConfirmedExposure(state, variant, 200),
                "A matching successful receipt did not commit confirmed exposure.");
            Require(block.providerExposureState == "confirmed_sent"
                    && block.lastProviderExposureTick == 200
                    && state.statusRevision == 6
                    && root.statusRevision == 8,
                "Confirmed exposure did not advance the exact owner/root status domain.");
            Require(!DiaryGameComponent.ApplyConfirmedExposure(state, variant, 200)
                    && state.statusRevision == 6
                    && root.statusRevision == 8,
                "An idempotent receipt replay advanced status revisions twice.");

            block.providerExposureState = "potentially_sent";
            root.statusRevision = long.MaxValue;
            Require(!DiaryGameComponent.ApplyConfirmedExposure(state, variant, 300)
                    && block.providerExposureState == "potentially_sent"
                    && block.lastProviderExposureTick == 200
                    && state.statusRevision == 6,
                "Root revision saturation allowed a partial exposure mutation.");
        }

        private static PawnKnowledgeState NewAccountingState(
            out SavedMemoryThreadRoot root,
            out SavedMemoryBlock block)
        {
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent("Pawn_M2");
            state.autobiographicalEpochToken = EpochToken(31);
            state.statusRevision = 5;
            block = new SavedMemoryBlock
            {
                schemaVersion = 1,
                recordId = "record-m2",
                sourceOccurrenceId = "source-m2",
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = state.autobiographicalEpochToken,
                rootId = "root-m2",
                providerExposureState = "not_sent",
                lastProviderExposureTick = 0
            };
            root = new SavedMemoryThreadRoot
            {
                schemaVersion = 1,
                rootId = "root-m2",
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = state.autobiographicalEpochToken,
                statusRevision = 7
            };
            root.visibleBlocks.Add(block);
            state.threadRoots.Add(root);
            return state;
        }

        private static void AddCanonicalStructuralGuards(
            SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            receipt.guardEntries.Clear();
            AddGuard(receipt, MemoryRepetitionGuardKinds.Novelty,
                MemoryRepetitionGuardPolicy.NoveltyKey("root-m2", "chapter-m2"));
            AddGuard(receipt, MemoryRepetitionGuardKinds.Pair,
                MemoryRepetitionGuardPolicy.PairKey("Pawn_M2", "Pawn_Other"));
            AddGuard(receipt, MemoryRepetitionGuardKinds.Record,
                MemoryRepetitionGuardPolicy.RecordKey("record-m2"));
            AddGuard(receipt, MemoryRepetitionGuardKinds.Root,
                MemoryRepetitionGuardPolicy.RootKey("root-m2"));
            AddGuard(receipt, MemoryRepetitionGuardKinds.Subject,
                MemoryRepetitionGuardPolicy.SubjectKey(
                    MemoryContractTokens.SubjectPawn, "Pawn_Other"));
        }

        private static void AddGuard(
            SavedFrozenEvidenceReceiptPlanV1 receipt,
            string kind,
            string key)
        {
            Require(!string.IsNullOrWhiteSpace(key),
                "The structural-guard fixture could not build a canonical key.");
            receipt.guardEntries.Add(new SavedFrozenGuardEntryV1
            {
                schemaVersion = 1,
                guardKind = kind,
                guardKey = key
            });
        }

        private static void RequireStructuralGuardRows(
            PawnKnowledgeState state,
            SavedActiveLogicalRequestV1 saved,
            SavedFrozenPromptVariantV1 variant,
            long expectedCount,
            long expectedTick,
            long expectedEntryOrdinal)
        {
            Require(state.repetitionGuardRows.Count == 4,
                "The exact four non-record guard rows were not materialized.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < state.repetitionGuardRows.Count; index++)
            {
                SavedMemoryRepetitionGuardRow row = state.repetitionGuardRows[index];
                Require(row != null
                        && row.schemaVersion == 1
                        && row.ownerEpochToken == state.autobiographicalEpochToken
                        && MemoryRepetitionGuardKinds.IsSavedRowKind(row.guardKind)
                        && MemoryRepetitionGuardPolicy.IsCanonicalIdentity(
                            row.guardKind, row.guardKey)
                        && identities.Add(row.guardKind + "\n" + row.guardKey)
                        && row.automaticInclusionCount == expectedCount
                        && row.lastAutomaticIncludedTick == expectedTick
                        && row.lastAutomaticIncludedEntryOrdinal == expectedEntryOrdinal
                        && row.lastSourceOccurrenceId == "source-m2"
                        && row.lastCommittedLogicalRequestId == saved.logicalRequestId
                        && row.lastCommittedEvidenceSetFingerprint
                            == variant.receiptPlan.evidenceSetFingerprint,
                    "A materialized structural guard row has incomplete accounting fields.");
            }
        }

        private static void RequireIncrementalIndexParity(
            DiaryGameComponent component,
            PawnKnowledgeState state,
            string stage)
        {
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
                    && incrementalGlobal.globalActiveBytes == rebuiltGlobal.globalActiveBytes
                    && incrementalGlobal.globalImportedBytes == rebuiltGlobal.globalImportedBytes,
                "Incremental dispatch indexes diverged after " + stage + ".");
        }

        private static void RequireThrowsNewer(Action action)
        {
            try
            {
                action();
                throw new InvalidOperationException(
                    "A newer nested M2 schema was accepted by an older build.");
            }
            catch (DiaryGameComponent.NewerPawnDiarySaveFormatException)
            {
                // Expected conservative downgrade boundary.
            }
        }

        private static SavedActiveLogicalRequestV1 NewSavedRequest()
        {
            MemoryLogicalRequestSnapshot request = new MemoryLogicalRequestSnapshot
            {
                logicalRequestSequence = 31,
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                sessionId = 4,
                eventIdOrOpportunityKey = "Event_M2",
                povRoleToken = DiaryEvent.InitiatorRole,
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = EpochToken(31),
                ownerCancellationGeneration = 2,
                globalCancellationGeneration = 3,
                requestStateToken = MemoryRequestStateMachineContracts.Staged
            };
            Require(MemoryIdentityCodec.TryCreateLogicalRequestId(
                    request.logicalRequestSequence, out request.logicalRequestId),
                "Could not create request id.");
            MemoryEvidenceIdentity evidence = new MemoryEvidenceIdentity
            {
                recordId = "record-m2",
                sourceOccurrenceId = "source-m2",
                rootIdOrEmpty = "root-m2"
            };
            MemoryGuardIdentity guard = new MemoryGuardIdentity
            {
                guardKind = "record",
                guardKey = "guard-m2"
            };
            request.reservedEvidence.Add(evidence);
            request.reservedGuards.Add(guard);

            MemoryFrozenPromptVariantSnapshot variant =
                new MemoryFrozenPromptVariantSnapshot
                {
                    variantOrdinal = 0,
                    templateIdentity = "template-m2",
                    contextDetailIdentity = "detail-m2",
                    systemPrompt = "system-m2",
                    userPrompt = "user-m2"
                };
            variant.receipt.evidence.Add(evidence);
            variant.receipt.guards.Add(guard);
            Require(MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    variant.receipt.evidence,
                    out variant.receipt.evidenceSetFingerprint),
                "Could not create evidence fingerprint.");
            Require(MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    variant.receipt.evidence,
                    variant.receipt.guards,
                    out variant.receipt.receiptPlanFingerprint),
                "Could not create receipt fingerprint.");
            string diagnosticFingerprint;
            Require(MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    variant.diagnostics, out diagnosticFingerprint),
                "Could not create diagnostic fingerprint.");
            Require(MemoryIdentityCodec.TryCreatePromptVariantKey(
                    request.logicalRequestId,
                    0,
                    request.requestPurposeToken,
                    variant.templateIdentity,
                    variant.contextDetailIdentity,
                    variant.systemPrompt,
                    variant.userPrompt,
                    variant.receipt.receiptPlanFingerprint,
                    diagnosticFingerprint,
                    out variant.variantKey),
                "Could not create variant key.");
            request.variants.Add(variant);
            Require(MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.reservedEvidence,
                    request.reservedGuards,
                    out request.evidenceEpochToken),
                "Could not create evidence epoch.");
            Require(MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.evidenceEpochToken,
                    out request.logicalRequestKey),
                "Could not create request key.");

            SavedActiveLogicalRequestV1 saved;
            Require(MemoryDispatchSavedAdapter.TryCreateSavedRequest(request, out saved),
                "Could not materialize valid saved request.");
            return saved;
        }

        private static MemoryDispatchFenceSnapshot Fence(SavedActiveLogicalRequestV1 request)
        {
            return new MemoryDispatchFenceSnapshot
            {
                sessionId = request.sessionId,
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                ownerCancellationGeneration = request.ownerCancellationGeneration,
                globalCancellationGeneration = request.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    request.optionalRequestInvalidationGeneration
            };
        }

        private static string EpochToken(long sequence)
        {
            string value = sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return OrdinalSegmentCodec.Segment("memory-epoch-v1")
                + OrdinalSegmentCodec.Segment(value);
        }

        private static void RunWithTempFile(Action<string> body)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_mem_m2_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                body(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void SaveWithScribe(string path, Action expose)
        {
            SafeSaver.Save(path, "savegame", () => expose());
        }

        private static void LoadVarsWithScribe(string path, Action expose)
        {
            Scribe.loader.InitLoading(path);
            try
            {
                expose();
                Scribe.loader.FinalizeLoading();
            }
            catch
            {
                Scribe.ForceStop();
                throw;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
