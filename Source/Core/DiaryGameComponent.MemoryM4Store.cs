// DiaryGameComponent.MemoryM4Store.cs — unified saved-store/index/atomic mutation adapter.
//
// Pure M4 code never sees Verse. This main-thread adapter projects saved rows to detached reducer
// snapshots, validates revision/count/logical-byte bounds, prebuilds complete replacement lists, and
// publishes them with a short reference-assignment commit tail. CurrentRelease uses these operations
// as the canonical store; LegacyShadow keeps them available only to compatibility fixtures.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Typed outcome for one optional saved-store mutation.</summary>
    internal enum MemoryStoreMutationOutcome
    {
        Admitted,
        Duplicate,
        Invalid,
        MigrationPending,
        StaleRevision,
        CapacityRefused,
        ProtectedSaturation,
        RevisionSaturated,
        ChapterSequenceSaturated,
        RequiredLandmarkCapacityRefused
    }

    /// <summary>Detached admission request assembled at a main-thread capture boundary.</summary>
    internal sealed class MemoryStoreAdmissionRequest
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long expectedOwnerStructuralRevision = -1;
        public long expectedIndexGeneration = -1;
        public bool routeReliable;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenSubjectLabel = string.Empty;
        public string chapterPhaseToken = string.Empty;
        public string chapterDirective = string.Empty;
        public string chapterClosureReasonToken = string.Empty;
        public bool requiredLifecycleLandmark;
        public long nowTick;
        public SavedMemoryBlock block;
    }

    /// <summary>Admission result; a non-Admitted outcome publishes no optional-memory mutation.</summary>
    internal sealed class MemoryStoreAdmissionResult
    {
        public MemoryStoreMutationOutcome outcome;
        public string rootId = string.Empty;
        public string recordId = string.Empty;
        public long committedOwnerStructuralRevision;
    }

    public partial class DiaryGameComponent
    {
        // ---- Transient T8.1 derivatives. These fields are never Scribed. ----
        private readonly Dictionary<string, PawnKnowledgeState> memoryM4OwnerById =
            new Dictionary<string, PawnKnowledgeState>(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedMemoryThreadRoot> memoryM4RootByCanonicalId =
            new Dictionary<string, SavedMemoryThreadRoot>(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedMemoryBlock> memoryM4BlockByQualifiedRecord =
            new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedMemoryBlock> memoryM4StandaloneByQualifiedRecord =
            new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedMemoryChapter> memoryM4ChapterByQualifiedId =
            new Dictionary<string, SavedMemoryChapter>(StringComparer.Ordinal);
        private readonly Dictionary<string, SavedMemoryBlock> memoryM4SourceFactDedupByKey =
            new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal);
        private long memoryM4IndexGeneration = 1;
        private bool memoryM4IndexesDirty = true;
        private int memoryM4GlobalActiveBlockCount;
        private int memoryM4GlobalEditedBlockCount;

        /// <summary>Marks all M4 derivatives stale after a saved reference assignment.</summary>
        internal void MarkMemoryM4IndexesDirty()
        {
            memoryM4IndexesDirty = true;
        }

        /// <summary>
        /// Rebuilds every exact owner/root/block/chapter/dedup index from saved truth. Duplicate
        /// canonical keys keep the first physical holder only; load repair consumes the full lists.
        /// </summary>
        internal void RebuildMemoryM4Indexes()
        {
            memoryM4OwnerById.Clear();
            memoryM4RootByCanonicalId.Clear();
            memoryM4BlockByQualifiedRecord.Clear();
            memoryM4StandaloneByQualifiedRecord.Clear();
            memoryM4ChapterByQualifiedId.Clear();
            memoryM4SourceFactDedupByKey.Clear();
            memoryM4GlobalActiveBlockCount = 0;
            memoryM4GlobalEditedBlockCount = 0;
            for (int i = 0; diaries != null && i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                PawnKnowledgeState state = diary?.knowledgeState;
                if (diary == null || state == null || !state.IsCurrentSchema()
                    || string.IsNullOrWhiteSpace(diary.pawnId)
                    || memoryM4OwnerById.ContainsKey(diary.pawnId)) continue;
                memoryM4OwnerById.Add(diary.pawnId, state);
                IndexOwner(state);
            }
            memoryM4IndexesDirty = false;
            if (memoryM4IndexGeneration < long.MaxValue) memoryM4IndexGeneration++;
        }

        /// <summary>Exact root read; never creates or matches a label.</summary>
        internal SavedMemoryThreadRoot FindMemoryThreadRootExact(
            string ownerPawnId,
            string ownerEpochToken,
            string subjectKind,
            string subjectId)
        {
            EnsureMemoryM4Indexes();
            string rootId;
            if (!MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
            {
                ownerPawnId = ownerPawnId,
                ownerEpochToken = ownerEpochToken,
                primarySubjectKind = subjectKind,
                primarySubjectId = subjectId
            }, out rootId)) return null;
            SavedMemoryThreadRoot root;
            return memoryM4RootByCanonicalId.TryGetValue(rootId, out root) ? root : null;
        }

        /// <summary>Exact standalone read by full owner/epoch/record handle.</summary>
        internal SavedMemoryBlock FindStandaloneMemoryExact(
            string ownerPawnId,
            string ownerEpochToken,
            string recordId)
        {
            EnsureMemoryM4Indexes();
            SavedMemoryBlock block;
            return memoryM4StandaloneByQualifiedRecord.TryGetValue(
                QualifiedRecord(ownerPawnId, ownerEpochToken, recordId), out block) ? block : null;
        }

        /// <summary>
        /// Performs one serialized lookup-or-create/admission operation. All fallible work occurs on
        /// detached replacements before the final saved list/revision assignments.
        /// </summary>
        internal MemoryStoreAdmissionResult TryAdmitMemoryBlock(MemoryStoreAdmissionRequest request)
        {
            MemoryStoreAdmissionResult result = new MemoryStoreAdmissionResult
            {
                outcome = MemoryStoreMutationOutcome.Invalid,
                recordId = request?.block?.recordId ?? string.Empty
            };
            if (!ValidateAdmissionRequest(request)) return result;
            EnsureMemoryM4Indexes();
            if (request.expectedIndexGeneration >= 0
                && request.expectedIndexGeneration != memoryM4IndexGeneration)
            {
                result.outcome = MemoryStoreMutationOutcome.StaleRevision;
                return result;
            }
            PawnKnowledgeState state;
            if (!memoryM4OwnerById.TryGetValue(request.ownerPawnId, out state))
            {
                result.outcome = MemoryStoreMutationOutcome.MigrationPending;
                return result;
            }
            if (!string.Equals(state.autobiographicalEpochToken, request.ownerEpochToken,
                StringComparison.Ordinal)) return result;
            if (request.expectedOwnerStructuralRevision >= 0
                && request.expectedOwnerStructuralRevision != state.structuralRevision)
            {
                result.outcome = MemoryStoreMutationOutcome.StaleRevision;
                return result;
            }
            string qualified = QualifiedRecord(
                request.ownerPawnId, request.ownerEpochToken, request.block.recordId);
            if (memoryM4BlockByQualifiedRecord.ContainsKey(qualified))
            {
                result.outcome = MemoryStoreMutationOutcome.Duplicate;
                result.committedOwnerStructuralRevision = state.structuralRevision;
                return result;
            }

            MemoryChapterAdmissionPlan chapterPlan = MemoryChapterPolicy.PlanAdmission(
                request.chapterDirective, request.chapterClosureReasonToken);
            if (!chapterPlan.valid) return result;
            MemoryPlacementPlan placement = MemoryThreadLookupPolicy.PlanPlacement(
                new MemoryPlacementRequest
                {
                    ownerPawnId = request.ownerPawnId,
                    ownerEpochToken = request.ownerEpochToken,
                    routeReliable = request.routeReliable && !chapterPlan.remainStandalone,
                    subjectKind = request.subjectKind,
                    subjectId = request.subjectId
                });
            if (!placement.valid) return result;

            // Unchanged siblings are immutable during planning, so copy only the owning lists and the
            // one root that can change. The pressure fallback still deep-clones every affected owner.
            List<SavedMemoryBlock> replacementStandalone = new List<SavedMemoryBlock>(
                state.standaloneBlocks ?? new List<SavedMemoryBlock>());
            List<SavedMemoryThreadRoot> replacementRoots = new List<SavedMemoryThreadRoot>(
                state.threadRoots ?? new List<SavedMemoryThreadRoot>());
            MemoryReducerPolicy policy = BuildMemoryReducerPolicy(request.nowTick);
            if (placement.standalone || request.block.ageUnknown)
            {
                // A fact without a trustworthy original date cannot establish or advance chapter
                // chronology. Preserve it exactly as Important/Standalone instead of inventing tick 0.
                SavedMemoryBlock standalone = CloneSavedBlock(request.block);
                standalone.rootId = string.Empty;
                standalone.chapterId = string.Empty;
                replacementStandalone.Add(standalone);
            }
            else
            {
                result.rootId = placement.rootId;
                int rootIndex = FindSavedRootIndex(replacementRoots, placement.rootId);
                if (rootIndex >= 0 && IsLateAfterClosedBoundary(
                    replacementRoots[rootIndex], request.block.originalEventTick,
                    request.block.ageUnknown))
                {
                    // A delayed record cannot reopen or rewrite a sealed chapter. Preserve the exact
                    // event as Standalone and let bounded provenance diagnostics arrive with M6.
                    SavedMemoryBlock lateStandalone = CloneSavedBlock(request.block);
                    lateStandalone.rootId = string.Empty;
                    lateStandalone.chapterId = string.Empty;
                    replacementStandalone.Add(lateStandalone);
                    result.rootId = string.Empty;
                }
                else
                {
                    SavedMemoryThreadRoot savedRoot;
                    if (rootIndex < 0)
                    {
                        savedRoot = CreateSavedRoot(request, placement.rootId);
                        replacementRoots.Add(savedRoot);
                        rootIndex = replacementRoots.Count - 1;
                    }
                    else
                    {
                        savedRoot = CloneSavedRoot(replacementRoots[rootIndex]);
                        replacementRoots[rootIndex] = savedRoot;
                    }
                    if (chapterPlan.closeOpenBeforeAdmission)
                        CloseOpenChapter(
                            savedRoot, request.block.originalEventTick,
                            chapterPlan.closureReasonToken);
                    bool chapterSequenceSaturated;
                    SavedMemoryChapter chapter = FindOrCreateOpenChapter(
                        savedRoot, request.block.originalEventTick,
                        request.chapterPhaseToken, out chapterSequenceSaturated);
                    if (chapter == null)
                    {
                        if (chapterSequenceSaturated)
                            result.outcome = MemoryStoreMutationOutcome.ChapterSequenceSaturated;
                        return result;
                    }
                    SavedMemoryBlock candidate = CloneSavedBlock(request.block);
                    candidate.rootId = savedRoot.rootId;
                    candidate.chapterId = chapter.chapterId;
                    savedRoot.visibleBlocks.Add(candidate);
                    if (!request.block.ageUnknown)
                        chapter.lastActivityTick = Math.Max(
                            chapter.lastActivityTick, request.block.originalEventTick);
                    if (chapterPlan.closeAdmittingChapterAfterAdmission)
                    {
                        chapter.closed = true;
                        chapter.closedTick = Math.Max(
                            chapter.lastActivityTick, request.block.originalEventTick);
                        chapter.closureReasonToken = chapterPlan.closureReasonToken;
                    }

                    MemoryThreadReductionResult reduction = MemoryThreadReducer.Reduce(
                        ToReducerRoot(savedRoot, policy), policy);
                    if (reduction.refused)
                    {
                        result.outcome = reduction.protectedSaturation
                            ? MemoryStoreMutationOutcome.ProtectedSaturation
                            : reduction.reasonToken == "revision_saturated"
                                ? MemoryStoreMutationOutcome.RevisionSaturated
                            : MemoryStoreMutationOutcome.Invalid;
                        return result;
                    }
                    replacementRoots[rootIndex] = FromReducerRoot(reduction.replacement, savedRoot);
                }
            }

            long nextOwnerRevision;
            if (!TryIncrement(state.structuralRevision, out nextOwnerRevision)) return result;
            MemoryStoreMutationOutcome capacity = ValidateDetachedCapacity(
                state, replacementStandalone, replacementRoots, request.requiredLifecycleLandmark);
            if (capacity != MemoryStoreMutationOutcome.Admitted)
            {
                // Pressure must evaluate the complete projected admission, not the currently saved
                // graph that is still exactly at its cap. The incoming record is excluded from this
                // transaction's eviction candidates; either existing eligible atoms make room and
                // the candidate publishes atomically, or no part of the detached plan publishes.
                MemoryPressureCommitResult pressure = TryApplyMemoryPressureCaps(
                    request.nowTick, state, replacementStandalone, replacementRoots,
                    request.block.recordId);
                result.outcome = pressure.changed
                    ? MemoryStoreMutationOutcome.Admitted
                    : pressure.protectedSaturation
                        ? request.requiredLifecycleLandmark
                            ? MemoryStoreMutationOutcome.RequiredLandmarkCapacityRefused
                            : MemoryStoreMutationOutcome.ProtectedSaturation
                        : capacity;
                if (pressure.changed)
                {
                    result.committedOwnerStructuralRevision =
                        pressure.committedOwnerStructuralRevision;
                    if (memoryObservationActiveAdmissionBudget != null)
                    {
                        // Pressure may evict siblings as well as the target projection, so its rare
                        // path refreshes the complete running session before another observation
                        // item can spend headroom.
                        RefreshMemoryObservationBudgetSession(
                            memoryObservationActiveAdmissionBudget);
                    }
                    MarkMemoryLibrarySavedProjectionDirty();
                }
                return result;
            }

            // Commit tail: every allocation, callback, policy decision, and size walk is complete.
            List<SavedMemoryBlock> priorStandalone = state.standaloneBlocks;
            List<SavedMemoryThreadRoot> priorRoots = state.threadRoots;
            state.standaloneBlocks = replacementStandalone;
            state.threadRoots = replacementRoots;
            state.structuralRevision = nextOwnerRevision;

            // Refresh only the mutated owner. A defensive full rebuild remains the recovery path if
            // corrupt loaded identity prevents exact unindex/reindex after the saved swap committed.
            try
            {
                ReindexMemoryM4OwnerAfterCommit(state, priorStandalone, priorRoots);
                MemoryObservationBudgetSession observationBudget =
                    memoryObservationActiveAdmissionBudget;
                MemoryOwnerByteTotals runningOwner;
                if (observationBudget != null
                    && observationBudget.owners.TryGetValue(
                        state.pawnId, out runningOwner)
                    && runningOwner.valid)
                {
                    // ValidateDetachedCapacity already committed the exact list delta to the running
                    // session. Publish this owner subtotal locally; the slice tail derives the
                    // component subtotal and copies the complete session once.
                    memoryByteTotalsByOwner[state.pawnId] = runningOwner;
                }
                else if (!RefreshMemorySizeIndexForOwner(state))
                {
                    RebuildMemorySizeIndexes();
                }
            }
            catch
            {
                memoryM4IndexesDirty = true;
                RebuildMemorySizeIndexes();
            }
            result.outcome = MemoryStoreMutationOutcome.Admitted;
            result.committedOwnerStructuralRevision = nextOwnerRevision;
            // Some durable captures intentionally create no diary page, so DiaryStateVersion does
            // not necessarily change. Invalidate the detached Library next to the authoritative
            // store commit so warm publications cannot omit the new block or pressure evictions.
            MarkMemoryLibrarySavedProjectionDirty();
            return result;
        }

        /// <summary>Projects and reduces one saved root for bounded maintenance.</summary>
        internal bool TryReduceSavedMemoryRoot(
            PawnKnowledgeState owner,
            int rootIndex,
            MemoryReducerPolicy policy)
        {
            if (owner == null || owner.threadRoots == null || rootIndex < 0
                || rootIndex >= owner.threadRoots.Count || owner.threadRoots[rootIndex] == null) return false;
            SavedMemoryThreadRoot prior = owner.threadRoots[rootIndex];
            MemoryThreadReductionResult reduction = MemoryThreadReducer.Reduce(
                ToReducerRoot(prior, policy), policy);
            if (reduction.refused) return false;
            SavedMemoryThreadRoot replacement = FromReducerRoot(reduction.replacement, prior);
            bool removeRoot = MemoryThreadReducer.IsRemovableEmptyRoot(reduction.replacement);
            if (!reduction.changed && !removeRoot) return false;
            List<SavedMemoryThreadRoot> roots = new List<SavedMemoryThreadRoot>(owner.threadRoots);
            if (removeRoot) roots.RemoveAt(rootIndex);
            else roots[rootIndex] = replacement;
            long revision;
            if (!TryIncrement(owner.structuralRevision, out revision)) return false;
            owner.threadRoots = roots;
            owner.structuralRevision = revision;
            memoryM4IndexesDirty = true;
            return true;
        }

        /// <summary>Applies original-tick TTL to standalone blocks as one owner-list swap.</summary>
        internal bool TryReduceSavedStandaloneBlocks(
            PawnKnowledgeState owner,
            MemoryReducerPolicy policy)
        {
            if (owner == null || owner.standaloneBlocks == null
                || owner.standaloneBlocks.Count == 0) return false;
            List<SavedMemoryBlock> replacement = CloneSavedBlocks(owner.standaloneBlocks);
            bool changed = false;
            for (int i = replacement.Count - 1; i >= 0; i--)
            {
                SavedMemoryBlock block = replacement[i];
                if (block == null)
                {
                    replacement.RemoveAt(i);
                    changed = true;
                    continue;
                }
                bool playerWordingAgrees = block.playerEdited
                    == !string.IsNullOrWhiteSpace(block.playerWording);
                if (playerWordingAgrees && !block.playerEdited
                    && block.kind != MemoryContractTokens.KindSummary
                    && MemoryThreadReducer.IsExpired(
                        policy.nowTick, block.originalEventTick, block.ageUnknown,
                        block.importance, policy.minorLifetimeTicks, policy.regularLifetimeTicks))
                {
                    replacement.RemoveAt(i);
                    changed = true;
                }
            }
            if (!changed) return false;
            long revision;
            if (!TryIncrement(owner.structuralRevision, out revision)) return false;
            owner.standaloneBlocks = replacement;
            owner.structuralRevision = revision;
            memoryM4IndexesDirty = true;
            return true;
        }

        /// <summary>Repairs duplicate roots for one owner and swaps once on success.</summary>
        internal bool TryRepairSavedMemoryRoots(PawnKnowledgeState owner, MemoryReducerPolicy policy)
        {
            return TryRepairSavedMemoryRoots(
                owner, policy, CreateMemoryMigrationBudgetSession());
        }

        /// <summary>
        /// Repairs one owner against a caller-owned running budget. Maintenance shares this session
        /// across its whole slice so multiple Imported archives cannot each spend the same headroom.
        /// </summary>
        private bool TryRepairSavedMemoryRoots(
            PawnKnowledgeState owner,
            MemoryReducerPolicy policy,
            MemoryMigrationBudgetSession budget)
        {
            if (owner == null || budget == null
                || owner.threadRoots == null || owner.threadRoots.Count == 0) return false;
            List<MemoryReducerRoot> projected = new List<MemoryReducerRoot>();
            for (int i = 0; i < owner.threadRoots.Count; i++)
                if (owner.threadRoots[i] != null)
                {
                    MemoryReducerRoot root = ToReducerRoot(owner.threadRoots[i], policy);
                    // T7.2 is root-local, but this M4 repair publishes one complete owner list. Do not
                    // let an understood sibling rewrite/reorder a newer inert root in that transaction.
                    if (MemoryThreadReducer.HasUnknownNewerReducerRevision(root)) return false;
                    projected.Add(root);
                }
            MemoryThreadRepairResult repair = MemoryThreadRepairPolicy.Repair(projected, policy);
            if (repair.refused)
            {
                // A refusing owner is retried every maintenance cycle and never converges. Leave one
                // bounded marker so the state is visible instead of silently burning a work item.
                RecordMemoryDiagnosticOnce("repair_refused", "owner");
                return false;
            }
            if (!repair.changed) return false;
            List<SavedMemoryThreadRoot> replacements = new List<SavedMemoryThreadRoot>();
            for (int i = 0; i < repair.activeRoots.Count; i++)
                replacements.Add(FromReducerRoot(repair.activeRoots[i], BuildOriginalRegistryRoot(
                    owner.threadRoots, repair.activeRoots[i], policy)));
            List<SavedImportedMemoryRow> imported = new List<SavedImportedMemoryRow>(
                owner.importedArchiveRows ?? new List<SavedImportedMemoryRow>());
            if (!TryAppendRepairArchiveRows(
                    owner, repair.archivedRoots, policy, imported))
            {
                RecordMemoryDiagnosticOnce("legacy_authored_conflict", "owner");
                return false;
            }
            long revision;
            if (!TryIncrement(owner.structuralRevision, out revision)) return false;
            PawnKnowledgeState projectedOwner = BuildRepairCapacityProjection(
                owner, replacements, imported, revision);
            MemoryMigrationBudgetProjection budgetProjection;
            if (!LegacyReplacementWithinBounds(
                    owner.pawnId,
                    projectedOwner,
                    budget,
                    out budgetProjection))
            {
                RecordMemoryDiagnosticOnce("legacy_authored_conflict", "owner");
                return false;
            }
            owner.threadRoots = replacements;
            owner.importedArchiveRows = imported;
            owner.structuralRevision = revision;
            ApplyMemoryMigrationBudgetProjection(budget, budgetProjection);
            memoryM4IndexesDirty = true;
            string diagnosticReason = MemoryThreadRepairPolicy.DiagnosticReason(repair);
            if (!string.IsNullOrEmpty(diagnosticReason))
                RecordMemoryDiagnostic(diagnosticReason, "owner");
            return true;
        }

        private bool TryAppendRepairArchiveRows(
            PawnKnowledgeState owner,
            List<MemoryReducerRoot> archivedRoots,
            MemoryReducerPolicy policy,
            List<SavedImportedMemoryRow> destination)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; destination != null && index < destination.Count; index++)
            {
                string id = destination[index]?.archiveRecordId ?? string.Empty;
                if (id.Length == 0 || !ids.Add(id)) return false;
            }
            for (int rootIndex = 0; archivedRoots != null
                && rootIndex < archivedRoots.Count; rootIndex++)
            {
                MemoryReducerRoot pureRoot = archivedRoots[rootIndex];
                SavedMemoryThreadRoot original = BuildOriginalRegistryRoot(
                    owner.threadRoots, pureRoot, policy);
                SavedMemoryThreadRoot savedRoot = FromReducerRoot(pureRoot, original);
                if (!TryAppendRepairArchiveBlock(
                        pureRoot, savedRoot.rollingSummaryBlock, ids, destination)) return false;
                for (int blockIndex = 0; savedRoot.visibleBlocks != null
                    && blockIndex < savedRoot.visibleBlocks.Count; blockIndex++)
                {
                    SavedMemoryBlock block = savedRoot.visibleBlocks[blockIndex];
                    if (!TryAppendRepairArchiveBlock(
                            pureRoot, block, ids, destination)) return false;
                }
            }
            return true;
        }

        private static bool TryAppendRepairArchiveBlock(
            MemoryReducerRoot pureRoot,
            SavedMemoryBlock block,
            HashSet<string> ids,
            List<SavedImportedMemoryRow> destination)
        {
            if (!IsPlayerAuthored(block)) return true;
            bool contributionRows = false;
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int contributionIndex = 0; bucket?.contributions != null
                    && contributionIndex < bucket.contributions.Count; contributionIndex++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[contributionIndex];
                    SavedImportedMemoryRow row = BuildRepairArchiveRow(
                        pureRoot, block, bucket, contribution);
                    if (row == null) return false;
                    if (ids.Add(row.archiveRecordId)) destination.Add(row);
                    contributionRows = true;
                }
            }
            if (contributionRows) return true;
            SavedImportedMemoryRow ordinary = BuildRepairArchiveRow(
                pureRoot, block, null, null);
            if (ordinary == null) return false;
            if (ids.Add(ordinary.archiveRecordId)) destination.Add(ordinary);
            return true;
        }

        private static SavedImportedMemoryRow BuildRepairArchiveRow(
            MemoryReducerRoot pureRoot,
            SavedMemoryBlock block,
            SavedMemoryFactBucket bucket,
            SavedMemoryFactContribution contribution)
        {
            if (pureRoot == null || block == null || !IsPlayerAuthored(block)) return null;
            string archiveId = MemoryThreadRepairPolicy.ArchiveFingerprint(
                pureRoot, block.recordId, contribution?.contributionId, "authored_conflict");
            if (archiveId.Length == 0) return null;
            SavedMemoryBlock clone = CloneSavedBlock(block);
            SavedImportedMemoryRow row = new SavedImportedMemoryRow
            {
                schemaVersion = 1,
                archiveRecordId = archiveId,
                savedOwnerIdentityKindToken = "exact_id",
                savedOwnerIdentityValue = pureRoot.ownerPawnId ?? string.Empty,
                originalRecordId = block.recordId ?? string.Empty,
                sourceOccurrenceId = contribution?.sourceOccurrenceId
                    ?? block.sourceOccurrenceId ?? string.Empty,
                sourceEventId = block.sourceEventId ?? string.Empty,
                originalEventTick = contribution?.originalEventTick ?? block.originalEventTick,
                ageUnknown = contribution?.ageUnknown ?? block.ageUnknown,
                importedWording = block.playerWording ?? string.Empty,
                originalKindToken = block.kind ?? string.Empty,
                originalSummaryRoleToken = block.summaryRole ?? string.Empty,
                originalCategoryToken = contribution?.category ?? block.category ?? string.Empty,
                originalImportanceToken = contribution?.importance ?? block.importance ?? string.Empty,
                routePolicyToken = string.Empty,
                sourceTypeToken = "current_schema_repair",
                conflictFingerprint = archiveId,
                migrationReasonToken = "authored_conflict"
            };
            if (contribution == null)
            {
                row.primarySubject = clone.primarySubject;
                row.secondarySubjects = clone.secondarySubjects;
                row.canonicalFacts = clone.facts;
                row.provenance = clone.provenance;
            }
            else
            {
                row.secondarySubjects = clone.summaryPayload?.subjectRefs
                    ?? new List<SavedMemorySubjectRef>();
                row.provenance = clone.summaryPayload?.provenanceRefs
                    ?? new List<SavedMemoryProvenance>();
                row.canonicalFacts.Add(new SavedMemoryCanonicalFact
                {
                    schemaVersion = 1,
                    factId = contribution.originFactId ?? string.Empty,
                    factKind = bucket?.factKind ?? string.Empty,
                    canonicalSubjectKind = bucket?.canonicalSubjectKind ?? string.Empty,
                    canonicalSubjectId = bucket?.canonicalSubjectId ?? string.Empty,
                    aggregationToken = bucket?.aggregationToken ?? string.Empty,
                    canonicalValueKind = "text",
                    canonicalValue = contribution.canonicalValue ?? string.Empty,
                    majorTurningPoint = contribution.majorTurningPoint,
                    reversal = contribution.reversal
                });
                row.summaryContributionEvidence = new SavedImportedSummaryContributionEvidenceV1
                {
                    schemaVersion = 1,
                    contributionId = contribution.contributionId ?? string.Empty,
                    originChapterId = contribution.originChapterId ?? string.Empty,
                    originRecordId = contribution.originRecordId ?? string.Empty,
                    originFactOrdinal = contribution.originFactOrdinal,
                    originFactId = contribution.originFactId ?? string.Empty,
                    originalEventTick = contribution.originalEventTick,
                    ageUnknown = contribution.ageUnknown,
                    category = contribution.category ?? string.Empty,
                    importance = contribution.importance ?? string.Empty,
                    canonicalValue = contribution.canonicalValue ?? string.Empty,
                    majorTurningPoint = contribution.majorTurningPoint,
                    reversal = contribution.reversal,
                    sourceOccurrenceId = contribution.sourceOccurrenceId ?? string.Empty,
                    subjectRefIds = new List<string>(
                        contribution.subjectRefIds ?? new List<string>()),
                    provenanceRefIds = new List<string>(
                        contribution.provenanceRefIds ?? new List<string>())
                };
            }
            row.Normalize();
            return row;
        }

        private static PawnKnowledgeState BuildRepairCapacityProjection(
            PawnKnowledgeState owner,
            List<SavedMemoryThreadRoot> roots,
            List<SavedImportedMemoryRow> imported,
            long structuralRevision)
        {
            return new PawnKnowledgeState
            {
                pawnId = owner.pawnId,
                schemaVersion = owner.schemaVersion,
                originCultureDefName = owner.originCultureDefName,
                originCultureSource = owner.originCultureSource,
                adoptedCultureDefName = owner.adoptedCultureDefName,
                records = owner.records,
                autobiographicalEpochToken = owner.autobiographicalEpochToken,
                archiveOnly = owner.archiveOnly,
                epochFenceOnly = owner.epochFenceOnly,
                requestCancellationGeneration = owner.requestCancellationGeneration,
                structuralRevision = structuralRevision,
                statusRevision = owner.statusRevision,
                completedDiaryEntryOrdinal = owner.completedDiaryEntryOrdinal,
                standaloneBlocks = owner.standaloneBlocks,
                threadRoots = roots,
                playerBackground = owner.playerBackground,
                ownerAwarenessSnapshots = owner.ownerAwarenessSnapshots,
                openCaptureEpisodes = owner.openCaptureEpisodes,
                repetitionGuardRows = owner.repetitionGuardRows,
                importedArchiveRows = imported,
                migrationDiagnosticFlags = owner.migrationDiagnosticFlags
            };
        }

        private void EnsureMemoryM4Indexes()
        {
            if (memoryM4IndexesDirty) RebuildMemoryM4Indexes();
        }

        private void IndexOwner(PawnKnowledgeState state)
        {
            for (int i = 0; state.standaloneBlocks != null && i < state.standaloneBlocks.Count; i++)
            {
                SavedMemoryBlock block = state.standaloneBlocks[i];
                if (block == null) continue;
                string key = QualifiedRecord(block.ownerPawnId, block.ownerEpochToken, block.recordId);
                if (!memoryM4StandaloneByQualifiedRecord.ContainsKey(key))
                    memoryM4StandaloneByQualifiedRecord.Add(key, block);
                IndexBlock(state, block, key);
            }
            for (int i = 0; state.threadRoots != null && i < state.threadRoots.Count; i++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[i];
                if (root == null) continue;
                string canonical;
                if (MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
                    {
                        ownerPawnId = root.ownerPawnId,
                        ownerEpochToken = root.ownerEpochToken,
                        primarySubjectKind = root.subjectKind,
                        primarySubjectId = root.subjectId
                    }, out canonical))
                {
                    if (!memoryM4RootByCanonicalId.ContainsKey(canonical))
                        memoryM4RootByCanonicalId.Add(canonical, root);
                }
                for (int j = 0; root.chapters != null && j < root.chapters.Count; j++)
                {
                    SavedMemoryChapter chapter = root.chapters[j];
                    string chapterKey = QualifiedRecord(
                        root.ownerPawnId, root.ownerEpochToken, chapter?.chapterId);
                    if (chapter != null && !memoryM4ChapterByQualifiedId.ContainsKey(chapterKey))
                        memoryM4ChapterByQualifiedId.Add(chapterKey, chapter);
                }
                for (int j = 0; root.visibleBlocks != null && j < root.visibleBlocks.Count; j++)
                {
                    SavedMemoryBlock block = root.visibleBlocks[j];
                    if (block == null) continue;
                    IndexBlock(state, block, QualifiedRecord(
                        block.ownerPawnId, block.ownerEpochToken, block.recordId));
                }
                if (root.rollingSummaryBlock != null) IndexBlock(state, root.rollingSummaryBlock,
                    QualifiedRecord(root.rollingSummaryBlock.ownerPawnId,
                        root.rollingSummaryBlock.ownerEpochToken,
                        root.rollingSummaryBlock.recordId));
            }
        }

        private void IndexBlock(PawnKnowledgeState state, SavedMemoryBlock block, string key)
        {
            if (!memoryM4BlockByQualifiedRecord.ContainsKey(key))
                memoryM4BlockByQualifiedRecord.Add(key, block);
            if (!memoryM4SourceFactDedupByKey.ContainsKey(key))
                memoryM4SourceFactDedupByKey.Add(key, block);
            memoryM4GlobalActiveBlockCount++;
            if (IsPlayerAuthored(block)) memoryM4GlobalEditedBlockCount++;
        }

        /// <summary>Replaces one owner's exact transient M4 entries without walking the colony.</summary>
        private void ReindexMemoryM4OwnerAfterCommit(
            PawnKnowledgeState state,
            List<SavedMemoryBlock> priorStandalone,
            List<SavedMemoryThreadRoot> priorRoots)
        {
            UnindexMemoryM4Owner(priorStandalone, priorRoots);
            memoryM4OwnerById[state.pawnId] = state;
            IndexOwner(state);
            memoryM4IndexesDirty = false;
            if (memoryM4IndexGeneration < long.MaxValue) memoryM4IndexGeneration++;
        }

        private void UnindexMemoryM4Owner(
            List<SavedMemoryBlock> standalone,
            List<SavedMemoryThreadRoot> roots)
        {
            for (int index = 0; standalone != null && index < standalone.Count; index++)
            {
                SavedMemoryBlock block = standalone[index];
                if (block == null) continue;
                string key = QualifiedRecord(block.ownerPawnId, block.ownerEpochToken, block.recordId);
                RemoveSame(memoryM4StandaloneByQualifiedRecord, key, block);
                UnindexMemoryM4Block(block, key);
            }
            for (int index = 0; roots != null && index < roots.Count; index++)
            {
                SavedMemoryThreadRoot root = roots[index];
                if (root == null) continue;
                string canonical;
                if (MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
                    {
                        ownerPawnId = root.ownerPawnId,
                        ownerEpochToken = root.ownerEpochToken,
                        primarySubjectKind = root.subjectKind,
                        primarySubjectId = root.subjectId
                    }, out canonical)) RemoveSame(memoryM4RootByCanonicalId, canonical, root);
                for (int chapterIndex = 0; root.chapters != null
                    && chapterIndex < root.chapters.Count; chapterIndex++)
                {
                    SavedMemoryChapter chapter = root.chapters[chapterIndex];
                    if (chapter == null) continue;
                    RemoveSame(memoryM4ChapterByQualifiedId,
                        QualifiedRecord(root.ownerPawnId, root.ownerEpochToken, chapter.chapterId),
                        chapter);
                }
                for (int blockIndex = 0; root.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                {
                    SavedMemoryBlock block = root.visibleBlocks[blockIndex];
                    if (block == null) continue;
                    UnindexMemoryM4Block(block, QualifiedRecord(
                        block.ownerPawnId, block.ownerEpochToken, block.recordId));
                }
                if (root.rollingSummaryBlock != null)
                {
                    SavedMemoryBlock block = root.rollingSummaryBlock;
                    UnindexMemoryM4Block(block, QualifiedRecord(
                        block.ownerPawnId, block.ownerEpochToken, block.recordId));
                }
            }
        }

        private void UnindexMemoryM4Block(SavedMemoryBlock block, string key)
        {
            RemoveSame(memoryM4BlockByQualifiedRecord, key, block);
            RemoveSame(memoryM4SourceFactDedupByKey, key, block);
            memoryM4GlobalActiveBlockCount = Math.Max(0, memoryM4GlobalActiveBlockCount - 1);
            if (IsPlayerAuthored(block))
                memoryM4GlobalEditedBlockCount = Math.Max(0, memoryM4GlobalEditedBlockCount - 1);
        }

        private static void RemoveSame<T>(Dictionary<string, T> index, string key, T expected)
            where T : class
        {
            T current;
            if (index.TryGetValue(key, out current) && ReferenceEquals(current, expected))
                index.Remove(key);
        }

        private MemoryStoreMutationOutcome ValidateDetachedCapacity(
            PawnKnowledgeState current,
            List<SavedMemoryBlock> standalone,
            List<SavedMemoryThreadRoot> roots,
            bool requiredLandmark)
        {
            int ownerBlocks = CountBlocks(standalone, roots);
            int ownerEdited = CountEdited(standalone, roots);
            int priorBlocks = CountBlocks(current.standaloneBlocks, current.threadRoots);
            int priorEdited = CountEdited(current.standaloneBlocks, current.threadRoots);
            int ownerCap = (int)ReadCapacityLong("manageableBlocksPerOwner", 128, 1024);
            int ownerEditedCap = (int)ReadCapacityLong("editedBlocksOwner", 32, 128);
            int globalEditedCap = (int)ReadCapacityLong("editedBlocksGlobal", 1000, 4000);
            int globalSoft;
            int globalHard;
            ReadCapacityPair("globalBlockCaps", 5000, 6000, 40000, 44000,
                out globalSoft, out globalHard);
            int projectedGlobal = memoryM4GlobalActiveBlockCount - priorBlocks + ownerBlocks;
            int projectedEdited = memoryM4GlobalEditedBlockCount - priorEdited + ownerEdited;
            if (ownerBlocks > ownerCap || ownerEdited > ownerEditedCap
                || projectedEdited > globalEditedCap
                || projectedGlobal > (requiredLandmark ? globalHard : globalSoft))
            {
                return requiredLandmark
                    ? MemoryStoreMutationOutcome.RequiredLandmarkCapacityRefused
                    : MemoryStoreMutationOutcome.CapacityRefused;
            }

            MemoryLogicalSizeResult oldStandalone = SizeSavedBlockList(current.standaloneBlocks);
            MemoryLogicalSizeResult newStandalone = SizeSavedBlockList(standalone);
            MemoryLogicalSizeResult oldRoots = SizeSavedRootList(current.threadRoots);
            MemoryLogicalSizeResult newRoots = SizeSavedRootList(roots);
            if (!oldStandalone.valid || !newStandalone.valid || !oldRoots.valid || !newRoots.valid)
                return MemoryStoreMutationOutcome.CapacityRefused;
            long delta;
            try
            {
                delta = checked(newStandalone.totalBytes + newRoots.totalBytes
                    - oldStandalone.totalBytes - oldRoots.totalBytes);
            }
            catch (OverflowException)
            {
                return MemoryStoreMutationOutcome.CapacityRefused;
            }
            MemoryObservationBudgetSession observationBudget =
                memoryObservationActiveAdmissionBudget;
            if (delta <= 0)
            {
                if (observationBudget == null)
                    return MemoryStoreMutationOutcome.Admitted;
                MemoryOwnerByteTotals shrinkingOwner;
                if (!observationBudget.owners.TryGetValue(
                        current.pawnId, out shrinkingOwner)
                    || !shrinkingOwner.valid
                    || shrinkingOwner.activeBytes < 0
                    || shrinkingOwner.importedBytes < 0
                    || observationBudget.global.globalActiveBytes < 0
                    || observationBudget.global.globalImportedBytes < 0)
                    return MemoryStoreMutationOutcome.CapacityRefused;
                try
                {
                    long ownerActive = checked(shrinkingOwner.activeBytes + delta);
                    long globalActive = checked(
                        observationBudget.global.globalActiveBytes + delta);
                    if (ownerActive < 0 || globalActive < 0)
                        return MemoryStoreMutationOutcome.CapacityRefused;
                    observationBudget.owners[current.pawnId] = new MemoryOwnerByteTotals
                    {
                        valid = true,
                        activeBytes = ownerActive,
                        importedBytes = shrinkingOwner.importedBytes
                    };
                    observationBudget.global = new MemoryPayloadBudgetTotals
                    {
                        globalActiveBytes = globalActive,
                        globalImportedBytes = observationBudget.global.globalImportedBytes
                    };
                    // A shrink is always beneficial even when corrupt/legacy state started above a
                    // current cap. Do not make it pass growth/reserve gates before accepting it.
                    return MemoryStoreMutationOutcome.Admitted;
                }
                catch (OverflowException)
                {
                    return MemoryStoreMutationOutcome.CapacityRefused;
                }
            }
            MemoryOwnerByteTotals owner = new MemoryOwnerByteTotals();
            MemoryPayloadBudgetTotals global;
            bool usingObservationBudget = observationBudget != null
                && observationBudget.owners.TryGetValue(current.pawnId, out owner)
                && owner.valid;
            if (usingObservationBudget)
            {
                global = observationBudget.global;
            }
            else
            {
                RebuildMemorySizeIndexes();
                owner = GetOwnerByteTotals(current.pawnId);
                global = GetGlobalBudgetTotals();
            }
            if (!owner.valid || global.globalActiveBytes < 0 || global.globalImportedBytes < 0)
                return MemoryStoreMutationOutcome.CapacityRefused;
            MemoryBudgetDecision budget = ActiveMemoryPayloadBudget.TryAdmit(
                CurrentMemoryBudgetLimits(),
                owner.activeBytes, owner.importedBytes, delta, 0, global);
            if (usingObservationBudget
                && budget.outcome == MemoryBudgetOutcome.Admitted)
            {
                observationBudget.global = budget.newTotals;
                observationBudget.owners[current.pawnId] = new MemoryOwnerByteTotals
                {
                    valid = true,
                    activeBytes = budget.newOwnerActiveBytes,
                    importedBytes = budget.newOwnerImportedBytes
                };
            }
            return budget.outcome == MemoryBudgetOutcome.Admitted
                ? MemoryStoreMutationOutcome.Admitted
                : requiredLandmark
                    ? MemoryStoreMutationOutcome.RequiredLandmarkCapacityRefused
                    : MemoryStoreMutationOutcome.CapacityRefused;
        }

        private static bool ValidateAdmissionRequest(MemoryStoreAdmissionRequest request)
        {
            if (request == null || request.block == null || string.IsNullOrEmpty(request.ownerPawnId)
                || request.nowTick < 0 || request.block.playerEdited
                || !string.IsNullOrWhiteSpace(request.block.playerWording)
                || request.block.kind == MemoryContractTokens.KindSummary
                || !MemoryContractTokens.IsKnownKind(request.block.kind)
                || !MemoryContractTokens.IsKnownCategory(request.block.category)
                || !MemoryContractTokens.IsKnownImportance(request.block.importance)
                || (request.block.ageUnknown
                    && request.block.importance != MemoryContractTokens.ImportanceImportant)
                || request.block.ownerPawnId != request.ownerPawnId
                || request.block.ownerEpochToken != request.ownerEpochToken
                || request.block.summaryRole != MemoryContractTokens.SummaryRoleNone
                || request.block.summaryPayload != null
                || string.IsNullOrWhiteSpace(request.chapterPhaseToken)
                || !MemoryChapterDirectiveTokens.IsKnown(request.chapterDirective)
                || (MemoryChapterDirectiveTokens.ClosesChapter(request.chapterDirective)
                    ? !MemoryChapterTokens.IsKnownClosureReason(
                        request.chapterClosureReasonToken)
                    : !string.IsNullOrEmpty(request.chapterClosureReasonToken))) return false;
            MemoryRecordIdentity identity;
            if (!MemoryIdentityCodec.TryParseRecordId(request.block.recordId, out identity)
                || identity.ownerPawnId != request.ownerPawnId
                || identity.ownerEpochToken != request.ownerEpochToken
                || identity.sourceOccurrenceId != request.block.sourceOccurrenceId
                || identity.captureRuleId != request.block.captureRuleId
                || identity.factDiscriminator != request.block.factDiscriminator) return false;
            MemoryReducerBlock detached = ToReducerBlock(request.block);
            HashSet<string> factIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < detached.facts.Count; i++)
                if (!MemoryThreadReducer.ValidateFact(
                        detached.facts[i], identity.captureRuleId, identity.factDiscriminator)
                    || !factIds.Add(detached.facts[i].factId)) return false;
            return true;
        }

        private static SavedMemoryThreadRoot CreateSavedRoot(
            MemoryStoreAdmissionRequest request,
            string rootId)
        {
            return new SavedMemoryThreadRoot
            {
                schemaVersion = 1,
                rootId = rootId,
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                subjectKind = request.subjectKind,
                subjectId = request.subjectId,
                frozenSubjectLabel = request.frozenSubjectLabel ?? string.Empty,
                structuralRevision = 1,
                statusRevision = 1,
                nextChapterOrdinal = 1
            };
        }

        private static SavedMemoryChapter FindOrCreateOpenChapter(
            SavedMemoryThreadRoot root,
            long originalEventTick,
            string phaseToken,
            out bool sequenceSaturated)
        {
            sequenceSaturated = false;
            for (int i = root.chapters.Count - 1; i >= 0; i--)
                if (!root.chapters[i].closed)
                {
                    if (string.IsNullOrEmpty(root.chapters[i].phaseToken))
                        root.chapters[i].phaseToken = phaseToken ?? string.Empty;
                    return root.chapters[i];
                }
            if (root.nextChapterOrdinal <= 0) return null;
            long ordinal = root.nextChapterOrdinal;
            if (ordinal == long.MaxValue)
            {
                sequenceSaturated = true;
                return null;
            }
            string chapterId;
            if (!MemoryIdentityCodec.TryCreateChapterId(root.rootId, ordinal, out chapterId)) return null;
            SavedMemoryChapter chapter = new SavedMemoryChapter
            {
                schemaVersion = 1,
                chapterId = chapterId,
                ordinal = ordinal,
                phaseToken = phaseToken ?? string.Empty,
                openedTick = Math.Max(0, originalEventTick),
                lastActivityTick = Math.Max(0, originalEventTick)
            };
            root.chapters.Add(chapter);
            root.nextChapterOrdinal = ordinal + 1;
            return chapter;
        }

        /// <summary>Closes the sole open chapter on a detached root before a new phase is admitted.</summary>
        private static void CloseOpenChapter(
            SavedMemoryThreadRoot root,
            long eventTick,
            string reasonToken)
        {
            if (root?.chapters == null) return;
            for (int i = root.chapters.Count - 1; i >= 0; i--)
            {
                SavedMemoryChapter chapter = root.chapters[i];
                if (chapter == null || chapter.closed) continue;
                chapter.closed = true;
                chapter.closedTick = Math.Max(chapter.lastActivityTick, eventTick);
                chapter.closureReasonToken = reasonToken ?? string.Empty;
                return;
            }
        }

        private MemoryReducerPolicy BuildMemoryReducerPolicy(long nowTick)
        {
            DiaryKnowledgeTuningDef tuning = Verse.DefDatabase<DiaryKnowledgeTuningDef>
                .GetNamedSilentFail(DiaryKnowledgePolicy.TuningDefName);
            MemoryPolicySnapshot effective = MemoryEffectivePolicyProvider.Current;
            int minorDays = effective != null && !effective.compatibilityFailClosed
                ? effective.minorMemoryLifetimeDays
                : tuning == null ? 15 : Math.Max(1, Math.Min(3600,
                    tuning.minorMemoryLifetimeDefaultDays));
            int regularDays = effective != null && !effective.compatibilityFailClosed
                ? effective.regularMemoryLifetimeDays
                : tuning == null ? 60 : Math.Max(1, Math.Min(3600,
                    tuning.regularMemoryLifetimeDefaultDays));
            int inactivityDays = tuning == null ? 15 : Math.Max(1, Math.Min(3600,
                tuning.memoryChapterInactivityDays));
            int target = effective != null && !effective.compatibilityFailClosed
                ? effective.memoryThreadTarget
                : tuning == null ? 12 : Math.Max(4, Math.Min(64,
                    tuning.memoryThreadTargetDefault));
            return new MemoryReducerPolicy
            {
                nowTick = Math.Max(0, nowTick),
                minorLifetimeTicks = (long)minorDays * 60000L,
                regularLifetimeTicks = (long)regularDays * 60000L,
                chapterInactivityTicks = (long)inactivityDays * 60000L,
                targetVisibleBlocks = target,
                maximumFactBuckets = (int)ReadCapacityLong("factBuckets", 16, 64),
                maximumContributionsPerBucket = (int)ReadCapacityTuplePart(
                    "datedContributionDescriptorMatchCaps", 0, 32, 128),
                maximumContributionsPerSummary = (int)ReadCapacityTuplePart(
                    "datedContributionDescriptorMatchCaps", 1, 32, 128),
                maximumDistinctSubjects = (int)ReadCapacityLong("distinctSubjects", 4, 32),
                maximumSubjectRefsPerContribution = (int)ReadCapacityLong(
                    "subjectRefsPerContribution", 2, 8),
                maximumProvenanceTotal = (int)ReadCapacityLong("provenanceTotal", 16, 128),
                maximumProvenanceRefsPerContribution = (int)ReadCapacityLong(
                    "provenancePerContribution", 2, 8),
                maximumVisibleBlocks = (int)ReadCapacityLong(
                    "manageableBlocksPerOwner", 128, 1024),
                maximumDeterministicWordingUnits = (int)ReadCapacityLong(
                    "summaryDeterministicWordingUnits", 240, 1200)
            };
        }

        private static MemoryReducerRoot ToReducerRoot(
            SavedMemoryThreadRoot saved,
            MemoryReducerPolicy policy)
        {
            MemoryReducerRoot root = new MemoryReducerRoot
            {
                rootId = saved.rootId,
                ownerPawnId = saved.ownerPawnId,
                ownerEpochToken = saved.ownerEpochToken,
                subjectKind = saved.subjectKind,
                subjectId = saved.subjectId,
                frozenSubjectLabel = saved.frozenSubjectLabel,
                structuralRevision = saved.structuralRevision,
                statusRevision = saved.statusRevision,
                lastAppliedReducerRevision = saved.lastAppliedReducerRevision,
                nextChapterOrdinal = saved.nextChapterOrdinal
            };
            for (int i = 0; saved.chapters != null && i < saved.chapters.Count; i++)
            {
                SavedMemoryChapter c = saved.chapters[i];
                if (c == null) continue;
                root.chapters.Add(new MemoryReducerChapter
                {
                    chapterId = c.chapterId,
                    ordinal = c.ordinal,
                    phaseToken = c.phaseToken,
                    openedTick = c.openedTick,
                    lastActivityTick = c.lastActivityTick,
                    closedTick = c.closedTick,
                    closureReasonToken = c.closureReasonToken,
                    closed = c.closed,
                    closedSummaryRecordId = c.closedSummaryRecordId
                });
            }
            for (int i = 0; saved.visibleBlocks != null && i < saved.visibleBlocks.Count; i++)
                if (saved.visibleBlocks[i] != null)
                    root.visibleBlocks.Add(ToReducerBlock(saved.visibleBlocks[i],
                        policy.maximumSubjectRefsPerContribution,
                        policy.maximumProvenanceRefsPerContribution));
            if (saved.rollingSummaryBlock != null)
                root.rollingSummaryBlock = ToReducerBlock(saved.rollingSummaryBlock,
                    policy.maximumSubjectRefsPerContribution,
                    policy.maximumProvenanceRefsPerContribution);
            return root;
        }

        private static MemoryReducerBlock ToReducerBlock(SavedMemoryBlock saved)
        {
            return ToReducerBlock(saved, int.MaxValue, int.MaxValue);
        }

        private static MemoryReducerBlock ToReducerBlock(
            SavedMemoryBlock saved,
            int maximumSubjectRefsPerContribution,
            int maximumProvenanceRefsPerContribution)
        {
            MemoryReducerBlock block = new MemoryReducerBlock
            {
                recordId = saved.recordId,
                sourceOccurrenceId = saved.sourceOccurrenceId,
                captureRuleId = saved.captureRuleId,
                factDiscriminator = saved.factDiscriminator,
                ownerPawnId = saved.ownerPawnId,
                ownerEpochToken = saved.ownerEpochToken,
                kind = saved.kind,
                summaryRole = saved.summaryRole,
                category = saved.category,
                importance = saved.importance,
                originalEventTick = saved.originalEventTick,
                ageUnknown = saved.ageUnknown,
                rootId = saved.rootId,
                chapterId = saved.chapterId,
                playerEdited = saved.playerEdited,
                suppressed = saved.suppressed,
                requiredLifecycleLandmark = saved.requiredLifecycleLandmark,
                automaticWording = saved.automaticWording,
                playerWording = saved.playerWording
            };
            List<MemoryReducerSubjectRefCandidate> subjects = SubjectRefCandidates(
                saved.secondarySubjects);
            List<string> provenance = MemoryContributionReferencePolicy.SelectReferenceIds(
                ProvenanceIds(saved.provenance), maximumProvenanceRefsPerContribution);
            for (int i = 0; saved.facts != null && i < saved.facts.Count; i++)
            {
                SavedMemoryCanonicalFact fact = saved.facts[i];
                if (fact == null) continue;
                block.facts.Add(new MemoryReducerFact
                {
                    factId = fact.factId,
                    factKind = fact.factKind,
                    canonicalSubjectKind = fact.canonicalSubjectKind,
                    canonicalSubjectId = fact.canonicalSubjectId,
                    aggregationToken = fact.aggregationToken,
                    canonicalValueKind = fact.canonicalValueKind,
                    canonicalValue = fact.canonicalValue,
                    majorTurningPoint = fact.majorTurningPoint,
                    reversal = fact.reversal,
                    // The bucket subject is implicit. Persist only other disclosed subjects, in
                    // deterministic ordinal order, up to the XML-owned per-contribution bound.
                    subjectRefIds = MemoryContributionReferencePolicy.SelectSubjectRefIds(
                        subjects, fact.canonicalSubjectKind, fact.canonicalSubjectId,
                        maximumSubjectRefsPerContribution),
                    provenanceRefIds = new List<string>(provenance)
                });
            }
            if (saved.summaryPayload != null) block.summaryPayload = ToReducerSummary(saved.summaryPayload);
            return block;
        }

        private static MemoryReducerSummary ToReducerSummary(SavedMemorySummaryPayload saved)
        {
            MemoryReducerSummary summary = new MemoryReducerSummary
            {
                reducerRevision = saved.reducerRevision,
                factsRevision = saved.factsRevision,
                canonicalFactsFingerprint = saved.canonicalFactsFingerprint,
                derivedCategoryMask = saved.derivedCategoryMask,
                highestSurvivingImportance = saved.highestSurvivingImportance,
                earliestSurvivingTick = saved.earliestSurvivingTick,
                latestSurvivingTick = saved.latestSurvivingTick,
                deterministicWording = saved.deterministicWording,
                optionalLlmWording = saved.optionalLlmWording,
                optionalLlmFingerprint = saved.optionalLlmFingerprint
            };
            for (int i = 0; saved.subjectRefs != null && i < saved.subjectRefs.Count; i++)
                if (saved.subjectRefs[i] != null)
                    summary.availableSubjectRefIds.Add(saved.subjectRefs[i].subjectRefId);
            summary.availableSubjectRefIds.Sort(StringComparer.Ordinal);
            for (int i = 0; saved.provenanceRefs != null && i < saved.provenanceRefs.Count; i++)
                if (saved.provenanceRefs[i] != null)
                    summary.availableProvenanceRefIds.Add(saved.provenanceRefs[i].provenanceRefId);
            summary.availableProvenanceRefIds.Sort(StringComparer.Ordinal);
            for (int i = 0; saved.factBuckets != null && i < saved.factBuckets.Count; i++)
            {
                SavedMemoryFactBucket source = saved.factBuckets[i];
                if (source == null) continue;
                MemoryReducerBucket bucket = new MemoryReducerBucket
                {
                    bucketKey = source.bucketKey,
                    factKind = source.factKind,
                    canonicalSubjectKind = source.canonicalSubjectKind,
                    canonicalSubjectId = source.canonicalSubjectId,
                    aggregationToken = source.aggregationToken,
                    derivedCount = source.derivedCount,
                    derivedRangeMin = source.derivedRangeMin,
                    derivedRangeMax = source.derivedRangeMax,
                    earliestSurvivingTick = source.earliestSurvivingTick,
                    latestSurvivingTick = source.latestSurvivingTick
                };
                for (int j = 0; source.contributions != null && j < source.contributions.Count; j++)
                {
                    SavedMemoryFactContribution c = source.contributions[j];
                    if (c == null) continue;
                    bucket.contributions.Add(new MemoryReducerContribution
                    {
                        contributionId = c.contributionId,
                        originChapterId = c.originChapterId,
                        originRecordId = c.originRecordId,
                        originFactOrdinal = c.originFactOrdinal,
                        originFactId = c.originFactId,
                        originalEventTick = c.originalEventTick,
                        ageUnknown = c.ageUnknown,
                        category = c.category,
                        importance = c.importance,
                        canonicalValue = c.canonicalValue,
                        majorTurningPoint = c.majorTurningPoint,
                        reversal = c.reversal,
                        sourceOccurrenceId = c.sourceOccurrenceId,
                        subjectRefIds = c.subjectRefIds == null
                            ? new List<string>() : new List<string>(c.subjectRefIds),
                        provenanceRefIds = c.provenanceRefIds == null
                            ? new List<string>() : new List<string>(c.provenanceRefIds)
                    });
                }
                summary.factBuckets.Add(bucket);
            }
            return summary;
        }

        private static SavedMemoryThreadRoot FromReducerRoot(
            MemoryReducerRoot pure,
            SavedMemoryThreadRoot original)
        {
            Dictionary<string, SavedMemoryBlock> originals = SavedBlockMap(original);
            Dictionary<string, SavedMemorySubjectRef> subjects = SubjectMap(original);
            Dictionary<string, SavedMemoryProvenance> provenance = ProvenanceMap(original);
            SavedMemoryThreadRoot root = new SavedMemoryThreadRoot
            {
                schemaVersion = 1,
                rootId = pure.rootId,
                ownerPawnId = pure.ownerPawnId,
                ownerEpochToken = pure.ownerEpochToken,
                subjectKind = pure.subjectKind,
                subjectId = pure.subjectId,
                frozenSubjectLabel = pure.frozenSubjectLabel,
                structuralRevision = pure.structuralRevision,
                statusRevision = pure.statusRevision,
                lastAppliedReducerRevision = pure.lastAppliedReducerRevision,
                nextChapterOrdinal = pure.nextChapterOrdinal
            };
            for (int i = 0; i < pure.chapters.Count; i++)
            {
                MemoryReducerChapter c = pure.chapters[i];
                root.chapters.Add(new SavedMemoryChapter
                {
                    schemaVersion = 1,
                    chapterId = c.chapterId,
                    ordinal = c.ordinal,
                    phaseToken = c.phaseToken,
                    openedTick = c.openedTick,
                    lastActivityTick = c.lastActivityTick,
                    closedTick = c.closedTick,
                    closureReasonToken = c.closureReasonToken,
                    closed = c.closed,
                    closedSummaryRecordId = c.closedSummaryRecordId
                });
            }
            for (int i = 0; i < pure.visibleBlocks.Count; i++)
                root.visibleBlocks.Add(FromReducerBlock(pure.visibleBlocks[i], originals, subjects, provenance));
            if (pure.rollingSummaryBlock != null)
                root.rollingSummaryBlock = FromReducerBlock(
                    pure.rollingSummaryBlock, originals, subjects, provenance);
            return root;
        }

        private static SavedMemoryBlock FromReducerBlock(
            MemoryReducerBlock pure,
            Dictionary<string, SavedMemoryBlock> originals,
            Dictionary<string, SavedMemorySubjectRef> subjects,
            Dictionary<string, SavedMemoryProvenance> provenance)
        {
            SavedMemoryBlock existing;
            if (pure.kind != MemoryContractTokens.KindSummary
                && originals.TryGetValue(pure.recordId, out existing))
            {
                SavedMemoryBlock retained = CloneSavedBlock(existing);
                retained.ownerPawnId = pure.ownerPawnId;
                retained.ownerEpochToken = pure.ownerEpochToken;
                retained.rootId = pure.rootId;
                retained.chapterId = pure.chapterId;
                retained.playerEdited = pure.playerEdited;
                retained.playerWording = pure.playerWording;
                retained.suppressed = pure.suppressed;
                return retained;
            }
            originals.TryGetValue(pure.recordId, out existing);
            SavedMemoryBlock block = existing == null ? new SavedMemoryBlock() : CloneSavedBlock(existing);
            block.schemaVersion = 1;
            block.recordId = pure.recordId;
            block.sourceOccurrenceId = pure.sourceOccurrenceId;
            block.captureRuleId = "memory_summary_reducer_v1";
            block.factDiscriminator = pure.summaryRole;
            block.ownerPawnId = pure.ownerPawnId;
            block.ownerEpochToken = pure.ownerEpochToken;
            block.kind = pure.kind;
            block.summaryRole = pure.summaryRole;
            block.category = string.Empty;
            block.importance = string.Empty;
            block.originalEventTick = pure.originalEventTick;
            block.ageUnknown = pure.ageUnknown;
            block.rootId = pure.rootId;
            block.chapterId = pure.chapterId;
            block.playerEdited = pure.playerEdited;
            block.playerWording = pure.playerWording;
            block.suppressed = pure.suppressed;
            block.requiredLifecycleLandmark = pure.requiredLifecycleLandmark;
            block.facts.Clear();
            block.provenance.Clear();
            block.primarySubject = null;
            block.secondarySubjects.Clear();
            SavedMemorySummaryPayload priorPayload = existing?.summaryPayload;
            block.summaryPayload = FromReducerSummary(
                pure.summaryPayload, subjects, provenance, priorPayload);
            if (block.formatRevision <= 0) block.formatRevision = 1;
            block.automaticWording = string.Empty;
            if (string.IsNullOrEmpty(block.providerExposureState)) block.providerExposureState = "not_sent";
            return block;
        }

        private static SavedMemorySummaryPayload FromReducerSummary(
            MemoryReducerSummary pure,
            Dictionary<string, SavedMemorySubjectRef> subjects,
            Dictionary<string, SavedMemoryProvenance> provenance,
            SavedMemorySummaryPayload prior)
        {
            bool factsUnchanged = prior != null
                && prior.factsRevision == pure.factsRevision
                && string.Equals(prior.canonicalFactsFingerprint,
                    pure.canonicalFactsFingerprint, StringComparison.Ordinal);
            SavedMemorySummaryPayload saved = new SavedMemorySummaryPayload
            {
                schemaVersion = 1,
                reducerRevision = pure.reducerRevision,
                factsRevision = pure.factsRevision,
                canonicalFactsFingerprint = pure.canonicalFactsFingerprint,
                derivedCategoryMask = pure.derivedCategoryMask,
                highestSurvivingImportance = pure.highestSurvivingImportance,
                earliestSurvivingTick = pure.earliestSurvivingTick,
                latestSurvivingTick = pure.latestSurvivingTick,
                deterministicWording = pure.deterministicWording,
                optionalLlmWording = factsUnchanged
                    ? prior.optionalLlmWording ?? string.Empty : string.Empty,
                optionalLlmFingerprint = factsUnchanged
                    ? prior.optionalLlmFingerprint ?? string.Empty : string.Empty,
                optionalLlmFormatRevision = factsUnchanged
                    ? prior.optionalLlmFormatRevision : 0,
                optionalLlmCategoryMask = factsUnchanged
                    ? prior.optionalLlmCategoryMask : 0,
                // Preserve the last terminal fingerprint across a natural fact change. Its mismatch
                // is what allows M10 to create exactly one new opportunity for the new projection.
                lastSettledWordingFingerprint = prior?.lastSettledWordingFingerprint
                    ?? string.Empty,
                lastSettledWordingReducerRevision =
                    prior?.lastSettledWordingReducerRevision ?? 0,
                lastSettledWordingFormatRevision =
                    prior?.lastSettledWordingFormatRevision ?? 0,
                lastWordingDispositionToken = prior?.lastWordingDispositionToken
                    ?? MemoryOptionalWordingDispositionTokens.None
            };
            HashSet<string> subjectIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> provenanceIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pure.factBuckets.Count; i++)
            {
                MemoryReducerBucket source = pure.factBuckets[i];
                SavedMemoryFactBucket bucket = new SavedMemoryFactBucket
                {
                    schemaVersion = 1,
                    bucketKey = source.bucketKey,
                    factKind = source.factKind,
                    canonicalSubjectKind = source.canonicalSubjectKind,
                    canonicalSubjectId = source.canonicalSubjectId,
                    aggregationToken = source.aggregationToken,
                    derivedCount = source.derivedCount,
                    derivedRangeMin = source.derivedRangeMin,
                    derivedRangeMax = source.derivedRangeMax,
                    earliestSurvivingTick = source.earliestSurvivingTick,
                    latestSurvivingTick = source.latestSurvivingTick
                };
                for (int j = 0; j < source.contributions.Count; j++)
                {
                    MemoryReducerContribution c = source.contributions[j];
                    bucket.contributions.Add(new SavedMemoryFactContribution
                    {
                        schemaVersion = 1,
                        contributionId = c.contributionId,
                        originChapterId = c.originChapterId,
                        originRecordId = c.originRecordId,
                        originFactOrdinal = c.originFactOrdinal,
                        originFactId = c.originFactId,
                        originalEventTick = c.originalEventTick,
                        ageUnknown = c.ageUnknown,
                        category = c.category,
                        importance = c.importance,
                        canonicalValue = c.canonicalValue,
                        majorTurningPoint = c.majorTurningPoint,
                        reversal = c.reversal,
                        sourceOccurrenceId = c.sourceOccurrenceId,
                        subjectRefIds = new List<string>(c.subjectRefIds),
                        provenanceRefIds = new List<string>(c.provenanceRefIds)
                    });
                    for (int k = 0; k < c.subjectRefIds.Count; k++) subjectIds.Add(c.subjectRefIds[k]);
                    for (int k = 0; k < c.provenanceRefIds.Count; k++) provenanceIds.Add(c.provenanceRefIds[k]);
                }
                saved.factBuckets.Add(bucket);
            }
            List<string> orderedSubjects = new List<string>(subjectIds);
            orderedSubjects.Sort(StringComparer.Ordinal);
            for (int i = 0; i < orderedSubjects.Count; i++)
            {
                SavedMemorySubjectRef row;
                if (subjects.TryGetValue(orderedSubjects[i], out row)) saved.subjectRefs.Add(CloneSubject(row));
            }
            List<string> orderedProvenance = new List<string>(provenanceIds);
            orderedProvenance.Sort(StringComparer.Ordinal);
            for (int i = 0; i < orderedProvenance.Count; i++)
            {
                SavedMemoryProvenance row;
                if (provenance.TryGetValue(orderedProvenance[i], out row))
                    saved.provenanceRefs.Add(CloneProvenance(row));
            }
            return saved;
        }

        private static SavedMemoryBlock CloneSavedBlock(SavedMemoryBlock value)
        {
            if (value == null) return null;
            SavedMemoryBlock copy = new SavedMemoryBlock
            {
                schemaVersion = value.schemaVersion,
                recordId = value.recordId,
                sourceOccurrenceId = value.sourceOccurrenceId,
                sourceEventId = value.sourceEventId,
                captureRuleId = value.captureRuleId,
                factDiscriminator = value.factDiscriminator,
                ownerPawnId = value.ownerPawnId,
                ownerEpochToken = value.ownerEpochToken,
                kind = value.kind,
                summaryRole = value.summaryRole,
                category = value.category,
                importance = value.importance,
                originalEventTick = value.originalEventTick,
                ageUnknown = value.ageUnknown,
                rootId = value.rootId,
                chapterId = value.chapterId,
                primarySubject = CloneSubject(value.primarySubject),
                automaticWording = value.automaticWording,
                playerWording = value.playerWording,
                playerEdited = value.playerEdited,
                suppressed = value.suppressed,
                requiredLifecycleLandmark = value.requiredLifecycleLandmark,
                formatRevision = value.formatRevision,
                lastAutomaticIncludedTick = value.lastAutomaticIncludedTick,
                lastAutomaticIncludedEntryOrdinal = value.lastAutomaticIncludedEntryOrdinal,
                automaticInclusionCount = value.automaticInclusionCount,
                providerExposureState = value.providerExposureState,
                lastProviderExposureTick = value.lastProviderExposureTick
            };
            for (int i = 0; value.secondarySubjects != null && i < value.secondarySubjects.Count; i++)
                if (value.secondarySubjects[i] != null) copy.secondarySubjects.Add(CloneSubject(value.secondarySubjects[i]));
            for (int i = 0; value.facts != null && i < value.facts.Count; i++)
                if (value.facts[i] != null) copy.facts.Add(CloneFact(value.facts[i]));
            for (int i = 0; value.provenance != null && i < value.provenance.Count; i++)
                if (value.provenance[i] != null) copy.provenance.Add(CloneProvenance(value.provenance[i]));
            if (value.summaryPayload != null)
                copy.summaryPayload = CloneSummaryPayload(value.summaryPayload);
            return copy;
        }

        private static SavedMemoryThreadRoot CloneSavedRoot(SavedMemoryThreadRoot value)
        {
            if (value == null) return null;
            SavedMemoryThreadRoot copy = new SavedMemoryThreadRoot
            {
                schemaVersion = value.schemaVersion,
                rootId = value.rootId,
                ownerPawnId = value.ownerPawnId,
                ownerEpochToken = value.ownerEpochToken,
                subjectKind = value.subjectKind,
                subjectId = value.subjectId,
                frozenSubjectLabel = value.frozenSubjectLabel,
                structuralRevision = value.structuralRevision,
                statusRevision = value.statusRevision,
                lastAppliedReducerRevision = value.lastAppliedReducerRevision,
                nextChapterOrdinal = value.nextChapterOrdinal
            };
            for (int i = 0; value.chapters != null && i < value.chapters.Count; i++)
            {
                SavedMemoryChapter c = value.chapters[i];
                if (c == null) continue;
                copy.chapters.Add(new SavedMemoryChapter
                {
                    schemaVersion = c.schemaVersion,
                    chapterId = c.chapterId,
                    ordinal = c.ordinal,
                    phaseToken = c.phaseToken,
                    openedTick = c.openedTick,
                    lastActivityTick = c.lastActivityTick,
                    closedTick = c.closedTick,
                    closureReasonToken = c.closureReasonToken,
                    closed = c.closed,
                    closedSummaryRecordId = c.closedSummaryRecordId
                });
            }
            for (int i = 0; value.visibleBlocks != null && i < value.visibleBlocks.Count; i++)
                if (value.visibleBlocks[i] != null) copy.visibleBlocks.Add(CloneSavedBlock(value.visibleBlocks[i]));
            copy.rollingSummaryBlock = CloneSavedBlock(value.rollingSummaryBlock);
            return copy;
        }

        private static List<SavedMemoryBlock> CloneSavedBlocks(List<SavedMemoryBlock> values)
        {
            List<SavedMemoryBlock> result = new List<SavedMemoryBlock>();
            for (int i = 0; values != null && i < values.Count; i++)
                if (values[i] != null) result.Add(CloneSavedBlock(values[i]));
            return result;
        }

        private static List<SavedMemoryThreadRoot> CloneSavedRoots(List<SavedMemoryThreadRoot> values)
        {
            List<SavedMemoryThreadRoot> result = new List<SavedMemoryThreadRoot>();
            for (int i = 0; values != null && i < values.Count; i++)
                if (values[i] != null) result.Add(CloneSavedRoot(values[i]));
            return result;
        }

        private static SavedMemorySubjectRef CloneSubject(SavedMemorySubjectRef value)
        {
            return value == null ? null : new SavedMemorySubjectRef
            {
                schemaVersion = value.schemaVersion,
                subjectRefId = value.subjectRefId,
                subjectKind = value.subjectKind,
                subjectId = value.subjectId,
                frozenLabel = value.frozenLabel,
                roleToken = value.roleToken,
                knownnessToken = value.knownnessToken
            };
        }

        private static SavedMemoryCanonicalFact CloneFact(SavedMemoryCanonicalFact value)
        {
            return new SavedMemoryCanonicalFact
            {
                schemaVersion = value.schemaVersion,
                factId = value.factId,
                factKind = value.factKind,
                canonicalSubjectKind = value.canonicalSubjectKind,
                canonicalSubjectId = value.canonicalSubjectId,
                aggregationToken = value.aggregationToken,
                canonicalValueKind = value.canonicalValueKind,
                canonicalValue = value.canonicalValue,
                majorTurningPoint = value.majorTurningPoint,
                reversal = value.reversal
            };
        }

        private static SavedMemoryProvenance CloneProvenance(SavedMemoryProvenance value)
        {
            return new SavedMemoryProvenance
            {
                schemaVersion = value.schemaVersion,
                provenanceRefId = value.provenanceRefId,
                sourceKindToken = value.sourceKindToken,
                sourceOccurrenceId = value.sourceOccurrenceId,
                sourceEventId = value.sourceEventId,
                captureRuleId = value.captureRuleId,
                factDiscriminator = value.factDiscriminator,
                integrationToken = value.integrationToken
            };
        }

        private static SavedMemorySummaryPayload CloneSummaryPayload(SavedMemorySummaryPayload value)
        {
            SavedMemorySummaryPayload copy = new SavedMemorySummaryPayload
            {
                schemaVersion = value.schemaVersion,
                reducerRevision = value.reducerRevision,
                factsRevision = value.factsRevision,
                canonicalFactsFingerprint = value.canonicalFactsFingerprint,
                derivedCategoryMask = value.derivedCategoryMask,
                highestSurvivingImportance = value.highestSurvivingImportance,
                earliestSurvivingTick = value.earliestSurvivingTick,
                latestSurvivingTick = value.latestSurvivingTick,
                deterministicWording = value.deterministicWording,
                optionalLlmWording = value.optionalLlmWording,
                optionalLlmFingerprint = value.optionalLlmFingerprint,
                optionalLlmFormatRevision = value.optionalLlmFormatRevision,
                optionalLlmCategoryMask = value.optionalLlmCategoryMask,
                lastSettledWordingFingerprint = value.lastSettledWordingFingerprint,
                lastSettledWordingReducerRevision = value.lastSettledWordingReducerRevision,
                lastSettledWordingFormatRevision = value.lastSettledWordingFormatRevision,
                lastWordingDispositionToken = value.lastWordingDispositionToken
            };
            for (int i = 0; value.factBuckets != null && i < value.factBuckets.Count; i++)
            {
                SavedMemoryFactBucket bucket = value.factBuckets[i];
                if (bucket == null) continue;
                SavedMemoryFactBucket bucketCopy = new SavedMemoryFactBucket
                {
                    schemaVersion = bucket.schemaVersion,
                    bucketKey = bucket.bucketKey,
                    factKind = bucket.factKind,
                    canonicalSubjectKind = bucket.canonicalSubjectKind,
                    canonicalSubjectId = bucket.canonicalSubjectId,
                    aggregationToken = bucket.aggregationToken,
                    derivedCount = bucket.derivedCount,
                    derivedRangeMin = bucket.derivedRangeMin,
                    derivedRangeMax = bucket.derivedRangeMax,
                    earliestSurvivingTick = bucket.earliestSurvivingTick,
                    latestSurvivingTick = bucket.latestSurvivingTick
                };
                for (int j = 0; bucket.contributions != null && j < bucket.contributions.Count; j++)
                {
                    SavedMemoryFactContribution c = bucket.contributions[j];
                    if (c == null) continue;
                    bucketCopy.contributions.Add(new SavedMemoryFactContribution
                    {
                        schemaVersion = c.schemaVersion,
                        contributionId = c.contributionId,
                        originChapterId = c.originChapterId,
                        originRecordId = c.originRecordId,
                        originFactOrdinal = c.originFactOrdinal,
                        originFactId = c.originFactId,
                        originalEventTick = c.originalEventTick,
                        ageUnknown = c.ageUnknown,
                        category = c.category,
                        importance = c.importance,
                        canonicalValue = c.canonicalValue,
                        majorTurningPoint = c.majorTurningPoint,
                        reversal = c.reversal,
                        sourceOccurrenceId = c.sourceOccurrenceId,
                        subjectRefIds = c.subjectRefIds == null
                            ? new List<string>() : new List<string>(c.subjectRefIds),
                        provenanceRefIds = c.provenanceRefIds == null
                            ? new List<string>() : new List<string>(c.provenanceRefIds)
                    });
                }
                copy.factBuckets.Add(bucketCopy);
            }
            for (int i = 0; value.subjectRefs != null && i < value.subjectRefs.Count; i++)
                if (value.subjectRefs[i] != null) copy.subjectRefs.Add(CloneSubject(value.subjectRefs[i]));
            for (int i = 0; value.provenanceRefs != null && i < value.provenanceRefs.Count; i++)
                if (value.provenanceRefs[i] != null)
                    copy.provenanceRefs.Add(CloneProvenance(value.provenanceRefs[i]));
            return copy;
        }

        private static Dictionary<string, SavedMemoryBlock> SavedBlockMap(SavedMemoryThreadRoot root)
        {
            Dictionary<string, SavedMemoryBlock> result =
                new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal);
            for (int i = 0; root?.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i] != null && !result.ContainsKey(root.visibleBlocks[i].recordId))
                    result.Add(root.visibleBlocks[i].recordId, root.visibleBlocks[i]);
            if (root?.rollingSummaryBlock != null
                && !result.ContainsKey(root.rollingSummaryBlock.recordId))
                result.Add(root.rollingSummaryBlock.recordId, root.rollingSummaryBlock);
            return result;
        }

        private static Dictionary<string, SavedMemorySubjectRef> SubjectMap(SavedMemoryThreadRoot root)
        {
            Dictionary<string, SavedMemorySubjectRef> result =
                new Dictionary<string, SavedMemorySubjectRef>(StringComparer.Ordinal);
            Dictionary<string, SavedMemoryBlock> blocks = SavedBlockMap(root);
            foreach (SavedMemoryBlock block in blocks.Values)
            {
                AddSubject(result, block.primarySubject);
                for (int i = 0; block.secondarySubjects != null && i < block.secondarySubjects.Count; i++)
                    AddSubject(result, block.secondarySubjects[i]);
                if (block.summaryPayload != null)
                    for (int i = 0; i < block.summaryPayload.subjectRefs.Count; i++)
                        AddSubject(result, block.summaryPayload.subjectRefs[i]);
            }
            return result;
        }

        private static Dictionary<string, SavedMemoryProvenance> ProvenanceMap(SavedMemoryThreadRoot root)
        {
            Dictionary<string, SavedMemoryProvenance> result =
                new Dictionary<string, SavedMemoryProvenance>(StringComparer.Ordinal);
            Dictionary<string, SavedMemoryBlock> blocks = SavedBlockMap(root);
            foreach (SavedMemoryBlock block in blocks.Values)
            {
                for (int i = 0; block.provenance != null && i < block.provenance.Count; i++)
                    AddProvenance(result, block.provenance[i]);
                if (block.summaryPayload != null)
                    for (int i = 0; i < block.summaryPayload.provenanceRefs.Count; i++)
                        AddProvenance(result, block.summaryPayload.provenanceRefs[i]);
            }
            return result;
        }

        private static Dictionary<string, SavedMemorySubjectRef> SubjectMap(SavedMemorySummaryPayload value)
        {
            Dictionary<string, SavedMemorySubjectRef> result =
                new Dictionary<string, SavedMemorySubjectRef>(StringComparer.Ordinal);
            for (int i = 0; value.subjectRefs != null && i < value.subjectRefs.Count; i++)
                AddSubject(result, value.subjectRefs[i]);
            return result;
        }

        private static Dictionary<string, SavedMemoryProvenance> ProvenanceMap(SavedMemorySummaryPayload value)
        {
            Dictionary<string, SavedMemoryProvenance> result =
                new Dictionary<string, SavedMemoryProvenance>(StringComparer.Ordinal);
            for (int i = 0; value.provenanceRefs != null && i < value.provenanceRefs.Count; i++)
                AddProvenance(result, value.provenanceRefs[i]);
            return result;
        }

        private static void AddSubject(
            Dictionary<string, SavedMemorySubjectRef> map,
            SavedMemorySubjectRef value)
        {
            if (value != null && !string.IsNullOrEmpty(value.subjectRefId)
                && !map.ContainsKey(value.subjectRefId)) map.Add(value.subjectRefId, value);
        }

        private static void AddProvenance(
            Dictionary<string, SavedMemoryProvenance> map,
            SavedMemoryProvenance value)
        {
            if (value != null && !string.IsNullOrEmpty(value.provenanceRefId)
                && !map.ContainsKey(value.provenanceRefId)) map.Add(value.provenanceRefId, value);
        }

        private static List<MemoryReducerSubjectRefCandidate> SubjectRefCandidates(
            List<SavedMemorySubjectRef> rows)
        {
            List<MemoryReducerSubjectRefCandidate> result =
                new List<MemoryReducerSubjectRefCandidate>();
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                SavedMemorySubjectRef value = rows[i];
                if (value == null || string.IsNullOrEmpty(value.subjectRefId)) continue;
                result.Add(new MemoryReducerSubjectRefCandidate
                {
                    subjectRefId = value.subjectRefId,
                    subjectKind = value.subjectKind,
                    subjectId = value.subjectId
                });
            }
            return result;
        }

        private static List<string> ProvenanceIds(List<SavedMemoryProvenance> rows)
        {
            List<string> result = new List<string>();
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                string value = rows[i]?.provenanceRefId;
                if (!string.IsNullOrEmpty(value) && !result.Contains(value)) result.Add(value);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static int FindSavedRootIndex(List<SavedMemoryThreadRoot> roots, string rootId)
        {
            for (int i = 0; i < roots.Count; i++) if (roots[i].rootId == rootId) return i;
            return -1;
        }

        private static SavedMemoryThreadRoot BuildOriginalRegistryRoot(
            List<SavedMemoryThreadRoot> roots,
            MemoryReducerRoot selectedRoot,
            MemoryReducerPolicy policy)
        {
            string rootId = selectedRoot?.rootId ?? string.Empty;
            SavedMemoryThreadRoot combined = null;
            HashSet<string> chapters = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> records = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; roots != null && i < roots.Count; i++)
            {
                SavedMemoryThreadRoot candidate = roots[i];
                string canonical;
                if (candidate == null || !MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
                    {
                        ownerPawnId = candidate.ownerPawnId,
                        ownerEpochToken = candidate.ownerEpochToken,
                        primarySubjectKind = candidate.subjectKind,
                        primarySubjectId = candidate.subjectId
                    }, out canonical) || canonical != rootId) continue;
                if (combined == null)
                {
                    combined = CloneSavedRoot(candidate);
                    for (int j = 0; j < combined.chapters.Count; j++)
                        chapters.Add(combined.chapters[j].chapterId);
                    for (int j = 0; j < combined.visibleBlocks.Count; j++)
                        records.Add(combined.visibleBlocks[j].recordId);
                    if (combined.rollingSummaryBlock != null)
                        records.Add(combined.rollingSummaryBlock.recordId);
                    continue;
                }
                for (int j = 0; candidate.chapters != null && j < candidate.chapters.Count; j++)
                    if (candidate.chapters[j] != null && chapters.Add(candidate.chapters[j].chapterId))
                    {
                        SavedMemoryChapter c = candidate.chapters[j];
                        combined.chapters.Add(new SavedMemoryChapter
                        {
                            schemaVersion = c.schemaVersion,
                            chapterId = c.chapterId,
                            ordinal = c.ordinal,
                            phaseToken = c.phaseToken,
                            openedTick = c.openedTick,
                            lastActivityTick = c.lastActivityTick,
                            closedTick = c.closedTick,
                            closureReasonToken = c.closureReasonToken,
                            closed = c.closed,
                            closedSummaryRecordId = c.closedSummaryRecordId
                        });
                    }
                for (int j = 0; candidate.visibleBlocks != null
                    && j < candidate.visibleBlocks.Count; j++)
                {
                    SavedMemoryBlock source = candidate.visibleBlocks[j];
                    if (source == null) continue;
                    if (records.Add(source.recordId))
                    {
                        combined.visibleBlocks.Add(CloneSavedBlock(source));
                        continue;
                    }
                    int existingIndex = FindSavedBlockIndex(
                        combined.visibleBlocks, source.recordId);
                    MemoryReducerBlock selected = FindReducerBlock(selectedRoot, source, policy);
                    if (existingIndex >= 0 && selected != null
                        && ShouldReplacePublicationSource(
                            combined.visibleBlocks[existingIndex], source, selected, policy))
                        combined.visibleBlocks[existingIndex] = CloneSavedBlock(source);
                }
                if (candidate.rollingSummaryBlock != null)
                {
                    SavedMemoryBlock source = candidate.rollingSummaryBlock;
                    if (records.Add(source.recordId))
                        combined.rollingSummaryBlock = CloneSavedBlock(source);
                    else if (combined.rollingSummaryBlock != null)
                    {
                        MemoryReducerBlock selected = FindReducerBlock(
                            selectedRoot, source, policy);
                        if (selected != null && ShouldReplacePublicationSource(
                            combined.rollingSummaryBlock, source, selected, policy))
                            combined.rollingSummaryBlock = CloneSavedBlock(source);
                    }
                }
            }
            return combined ?? new SavedMemoryThreadRoot();
        }

        private static bool ShouldReplacePublicationSource(
            SavedMemoryBlock current,
            SavedMemoryBlock candidate,
            MemoryReducerBlock selected,
            MemoryReducerPolicy policy)
        {
            List<MemoryReducerBlock> choices = new List<MemoryReducerBlock>
            {
                ToReducerBlock(current,
                    policy.maximumSubjectRefsPerContribution,
                    policy.maximumProvenanceRefsPerContribution),
                ToReducerBlock(candidate,
                    policy.maximumSubjectRefsPerContribution,
                    policy.maximumProvenanceRefsPerContribution)
            };
            return MemoryThreadRepairPolicy.FindPublicationSourceIndex(choices, selected) == 1;
        }

        /// <summary>
        /// Finds the published block that one saved row became. An ordinary record keeps its record
        /// id through repair, so the exact lookup answers it. A Summary row has its id rebuilt from
        /// the canonical root and chapter ordinal, so that case falls back to a payload match.
        /// </summary>
        private static MemoryReducerBlock FindReducerBlock(
            MemoryReducerRoot root,
            SavedMemoryBlock saved,
            MemoryReducerPolicy policy)
        {
            if (root == null || saved == null) return null;
            for (int i = 0; root.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].recordId == saved.recordId) return root.visibleBlocks[i];
            if (root.rollingSummaryBlock != null
                && root.rollingSummaryBlock.recordId == saved.recordId)
                return root.rollingSummaryBlock;
            if (saved.kind != MemoryContractTokens.KindSummary) return null;
            List<MemoryReducerBlock> probe = new List<MemoryReducerBlock>
            {
                ToReducerBlock(saved,
                    policy.maximumSubjectRefsPerContribution,
                    policy.maximumProvenanceRefsPerContribution)
            };
            for (int i = 0; root.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
                if (MemoryThreadRepairPolicy.FindPublicationSourceIndex(
                    probe, root.visibleBlocks[i]) == 0) return root.visibleBlocks[i];
            return root.rollingSummaryBlock != null
                && MemoryThreadRepairPolicy.FindPublicationSourceIndex(
                    probe, root.rollingSummaryBlock) == 0 ? root.rollingSummaryBlock : null;
        }

        private static int FindSavedBlockIndex(
            List<SavedMemoryBlock> blocks,
            string recordId)
        {
            for (int i = 0; blocks != null && i < blocks.Count; i++)
                if (blocks[i] != null && blocks[i].recordId == recordId) return i;
            return -1;
        }

        private static bool HasOpenChapter(SavedMemoryThreadRoot root)
        {
            for (int i = 0; root.chapters != null && i < root.chapters.Count; i++)
                if (!root.chapters[i].closed) return true;
            return false;
        }

        private static bool IsLateAfterClosedBoundary(
            SavedMemoryThreadRoot root,
            long originalEventTick,
            bool ageUnknown)
        {
            if (root == null || ageUnknown) return false;
            long latestBoundary = -1;
            for (int i = 0; root.chapters != null && i < root.chapters.Count; i++)
            {
                SavedMemoryChapter chapter = root.chapters[i];
                if (chapter != null && chapter.closed)
                    latestBoundary = Math.Max(latestBoundary, chapter.closedTick);
            }
            return latestBoundary >= 0 && originalEventTick <= latestBoundary;
        }

        private static int CountBlocks(
            List<SavedMemoryBlock> standalone,
            List<SavedMemoryThreadRoot> roots)
        {
            int count = standalone == null ? 0 : standalone.Count;
            for (int i = 0; roots != null && i < roots.Count; i++)
                count += roots[i].visibleBlocks.Count + (roots[i].rollingSummaryBlock == null ? 0 : 1);
            return count;
        }

        private static int CountEdited(
            List<SavedMemoryBlock> standalone,
            List<SavedMemoryThreadRoot> roots)
        {
            int count = 0;
            for (int i = 0; standalone != null && i < standalone.Count; i++)
                if (IsPlayerAuthored(standalone[i])) count++;
            for (int i = 0; roots != null && i < roots.Count; i++)
            {
                for (int j = 0; j < roots[i].visibleBlocks.Count; j++)
                    if (IsPlayerAuthored(roots[i].visibleBlocks[j])) count++;
                if (roots[i].rollingSummaryBlock != null
                    && IsPlayerAuthored(roots[i].rollingSummaryBlock)) count++;
            }
            return count;
        }

        private static bool IsPlayerAuthored(SavedMemoryBlock block)
        {
            return block != null && (block.playerEdited
                || !string.IsNullOrWhiteSpace(block.playerWording));
        }

        private static MemoryLogicalSizeResult SizeSavedBlockList(List<SavedMemoryBlock> rows)
        {
            long total = 4;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                MemoryLogicalSizeResult sized = MemoryLogicalPayloadSizer.Size(rows[i]);
                if (!sized.valid || total > long.MaxValue - sized.totalBytes)
                    return new MemoryLogicalSizeResult { valid = false };
                total += sized.totalBytes;
            }
            return new MemoryLogicalSizeResult { valid = true, totalBytes = total };
        }

        private static MemoryLogicalSizeResult SizeSavedRootList(List<SavedMemoryThreadRoot> rows)
        {
            long total = 4;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                MemoryLogicalSizeResult sized = MemoryLogicalPayloadSizer.Size(rows[i]);
                if (!sized.valid || total > long.MaxValue - sized.totalBytes)
                    return new MemoryLogicalSizeResult { valid = false };
                total += sized.totalBytes;
            }
            return new MemoryLogicalSizeResult { valid = true, totalBytes = total };
        }

        private static bool TryIncrement(long value, out long next)
        {
            if (value < 0 || value == long.MaxValue) { next = value; return false; }
            next = value + 1;
            return true;
        }

        private static string QualifiedRecord(string owner, string epoch, string id)
        {
            return OrdinalSegmentCodec.Segment(owner ?? string.Empty)
                + OrdinalSegmentCodec.Segment(epoch ?? string.Empty)
                + OrdinalSegmentCodec.Segment(id ?? string.Empty);
        }
    }
}
