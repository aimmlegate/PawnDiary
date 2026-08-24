// DiaryGameComponent.MemoryPressure.cs — cross-owner atomic emergency pressure for M4.
//
// This adapter builds complete detached owner replacements, consumes the one pure eviction order,
// repeatedly remeasures real logical bytes/counts, and publishes every touched owner together. A
// protected-saturation refusal discards the entire detached plan; ordinary diary work remains valid.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class MemoryPressureOwnerWork
        {
            public PawnKnowledgeState owner;
            public long expectedRevision;
            public List<SavedMemoryBlock> priorStandalone;
            public List<SavedMemoryThreadRoot> priorRoots;
            public List<SavedMemoryBlock> standalone;
            public List<SavedMemoryThreadRoot> roots;
            public string protectedAdmissionRecordId = string.Empty;
            public bool changed;
        }

        private sealed class MemoryPressureCommitResult
        {
            public bool changed;
            public bool protectedSaturation;
            public long committedOwnerStructuralRevision = -1;
        }

        /// <summary>
        /// Restores owner/global count and byte headroom through the exact emergency atom order.
        /// Every saved assignment is deferred until every owner plan and revision is proven.
        /// </summary>
        private MemoryPressureCommitResult TryApplyMemoryPressureCaps(
            long nowTick,
            PawnKnowledgeState projectedOwner = null,
            List<SavedMemoryBlock> projectedStandalone = null,
            List<SavedMemoryThreadRoot> projectedRoots = null,
            string protectedAdmissionRecordId = "")
        {
            MemoryPressureCommitResult result = new MemoryPressureCommitResult();
            EnsureMemoryM4Indexes();
            RebuildMemorySizeIndexes();
            List<MemoryPressureOwnerWork> work = new List<MemoryPressureOwnerWork>();
            bool foundProjectedOwner = projectedOwner == null;
            foreach (KeyValuePair<string, PawnKnowledgeState> pair in memoryM4OwnerById)
            {
                PawnKnowledgeState owner = pair.Value;
                bool useProjection = ReferenceEquals(owner, projectedOwner);
                foundProjectedOwner |= useProjection;
                work.Add(new MemoryPressureOwnerWork
                {
                    owner = owner,
                    expectedRevision = owner.structuralRevision,
                    priorStandalone = owner.standaloneBlocks,
                    priorRoots = owner.threadRoots,
                    standalone = CloneSavedBlocks(useProjection
                        ? projectedStandalone : owner.standaloneBlocks),
                    roots = CloneSavedRoots(useProjection
                        ? projectedRoots : owner.threadRoots),
                    protectedAdmissionRecordId = useProjection
                        ? protectedAdmissionRecordId ?? string.Empty : string.Empty,
                    // Publishing a projected admission is itself a change even before pressure
                    // removes an older atom. No assignment occurs unless every cap is later proven.
                    changed = useProjection
                });
            }
            if (!foundProjectedOwner)
            {
                result.protectedSaturation = true;
                return result;
            }
            work.Sort(delegate(MemoryPressureOwnerWork left, MemoryPressureOwnerWork right)
            {
                return string.Compare(left.owner.pawnId, right.owner.pawnId, StringComparison.Ordinal);
            });
            MemoryReducerPolicy policy = BuildMemoryReducerPolicy(nowTick);
            int ownerBlockCap = (int)ReadCapacityLong("manageableBlocksPerOwner", 128, 1024);
            int ownerEditedCap = (int)ReadCapacityLong("editedBlocksOwner", 32, 128);
            long ownerActiveCap = ReadCapacityLong("activeOwnerBytes", 196608, 2097152);
            long ownerCombinedCap = ReadCapacityLong("combinedOwnerBytes", 262144, 4194304);

            for (int i = 0; i < work.Count; i++)
            {
                MemoryPressureOwnerWork owner = work[i];
                int guard = CountPressureAtoms(owner) + 1;
                bool ownerSatisfied = false;
                while (guard-- > 0)
                {
                    long active;
                    long combined;
                    if (!TryProjectedOwnerBytes(owner, out active, out combined))
                    {
                        result.protectedSaturation = true;
                        return result;
                    }
                    int blocks = CountBlocks(owner.standalone, owner.roots);
                    int edited = CountEdited(owner.standalone, owner.roots);
                    if (blocks <= ownerBlockCap && edited <= ownerEditedCap
                        && active <= ownerActiveCap && combined <= ownerCombinedCap)
                    {
                        ownerSatisfied = true;
                        break;
                    }
                    if (edited > ownerEditedCap)
                    {
                        result.protectedSaturation = true;
                        return result;
                    }
                    List<MemoryPressureAtom> atoms = BuildPressureAtoms(owner);
                    // Remove one canonical prefix atom, then remeasure the complete detached graph.
                    // The last Summary contribution can release bucket/payload/block/chapter overhead
                    // that its own row size cannot represent, so aggregate byte estimates must not
                    // decide protected saturation.
                    MemoryPressurePlan plan =
                        KnowledgeEvictionPlanner.PlanNextMemoryPressureAtom(atoms);
                    if (!plan.canApply || !ApplyPressureRemovals(owner, plan.removals, policy))
                    {
                        result.protectedSaturation = true;
                        return result;
                    }
                    owner.changed = true;
                }
                if (!ownerSatisfied)
                {
                    result.protectedSaturation = true;
                    return result;
                }
            }

            int globalSoft;
            int ignoredHard;
            ReadCapacityPair("globalBlockCaps", 5000, 6000, 40000, 44000,
                out globalSoft, out ignoredHard);
            int globalEditedCap = (int)ReadCapacityLong("editedBlocksGlobal", 1000, 4000);
            long globalActiveCap = ReadCapacityLong("activeGlobalBytes", 6291456, 25165824);
            long globalCombinedCap = ReadCapacityLong("combinedGlobalBytes", 8388608, 33554432);
            int globalGuard = TotalPressureAtoms(work) + 1;
            bool globalSatisfied = false;
            while (globalGuard-- > 0)
            {
                int blocks = 0;
                int edited = 0;
                for (int i = 0; i < work.Count; i++)
                {
                    blocks += CountBlocks(work[i].standalone, work[i].roots);
                    edited += CountEdited(work[i].standalone, work[i].roots);
                }
                long active;
                long combined;
                if (!TryProjectedGlobalBytes(work, out active, out combined))
                {
                    result.protectedSaturation = true;
                    return result;
                }
                if (blocks <= globalSoft && edited <= globalEditedCap
                    && active <= globalActiveCap && combined <= globalCombinedCap)
                {
                    globalSatisfied = true;
                    break;
                }
                if (edited > globalEditedCap)
                {
                    result.protectedSaturation = true;
                    return result;
                }
                List<MemoryPressureAtom> atoms = new List<MemoryPressureAtom>();
                for (int i = 0; i < work.Count; i++) atoms.AddRange(BuildPressureAtoms(work[i]));
                MemoryPressurePlan plan =
                    KnowledgeEvictionPlanner.PlanNextMemoryPressureAtom(atoms);
                if (!plan.canApply)
                {
                    result.protectedSaturation = true;
                    return result;
                }
                bool any = false;
                for (int i = 0; i < work.Count; i++)
                {
                    List<MemoryPressureAtom> ownerRemovals = new List<MemoryPressureAtom>();
                    for (int j = 0; j < plan.removals.Count; j++)
                        if (plan.removals[j].ownerPawnId == work[i].owner.pawnId)
                            ownerRemovals.Add(plan.removals[j]);
                    if (ownerRemovals.Count == 0) continue;
                    if (!ApplyPressureRemovals(work[i], ownerRemovals, policy))
                    {
                        result.protectedSaturation = true;
                        return result;
                    }
                    work[i].changed = true;
                    any = true;
                }
                if (!any)
                {
                    result.protectedSaturation = true;
                    return result;
                }
            }
            if (!globalSatisfied)
            {
                result.protectedSaturation = true;
                return result;
            }

            List<MemoryPressureOwnerWork> changed = work.FindAll(row => row.changed);
            if (changed.Count == 0) return result;
            for (int i = 0; i < changed.Count; i++)
            {
                long ignored;
                if (changed[i].owner.structuralRevision != changed[i].expectedRevision
                    || !TryIncrement(changed[i].expectedRevision, out ignored)) return result;
            }

            // Cross-owner commit tail: prebuilt references and already-proven revisions only.
            for (int i = 0; i < changed.Count; i++)
            {
                changed[i].owner.standaloneBlocks = changed[i].standalone;
                changed[i].owner.threadRoots = changed[i].roots;
                changed[i].owner.structuralRevision = changed[i].expectedRevision + 1;
                if (ReferenceEquals(changed[i].owner, projectedOwner))
                    result.committedOwnerStructuralRevision =
                        changed[i].owner.structuralRevision;
            }
            memoryM4IndexesDirty = true;
            result.changed = true;
            try
            {
                RebuildMemoryM4Indexes();
                RebuildMemorySizeIndexes();
            }
            catch
            {
                memoryM4IndexesDirty = true;
            }
            return result;
        }

        private static List<MemoryPressureAtom> BuildPressureAtoms(MemoryPressureOwnerWork owner)
        {
            List<MemoryPressureAtom> atoms = new List<MemoryPressureAtom>();
            for (int i = 0; i < owner.standalone.Count; i++)
                AddBlockPressureAtoms(owner.owner.pawnId, owner.standalone[i],
                    owner.protectedAdmissionRecordId, atoms);
            for (int i = 0; i < owner.roots.Count; i++)
            {
                SavedMemoryThreadRoot root = owner.roots[i];
                if (HasUnknownNewerReducerRevision(root)) continue;
                for (int j = 0; j < root.visibleBlocks.Count; j++)
                    AddBlockPressureAtoms(owner.owner.pawnId, root.visibleBlocks[j],
                        owner.protectedAdmissionRecordId, atoms);
                AddBlockPressureAtoms(owner.owner.pawnId, root.rollingSummaryBlock,
                    owner.protectedAdmissionRecordId, atoms);
            }
            MarkSummaryTerminalBlockUnits(atoms);
            return atoms;
        }

        private static bool HasUnknownNewerReducerRevision(SavedMemoryThreadRoot root)
        {
            if (root == null) return false;
            if (root.lastAppliedReducerRevision > MemoryThreadReducer.CurrentReducerRevision)
                return true;
            for (int i = 0; root.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
            {
                SavedMemorySummaryPayload summary = root.visibleBlocks[i]?.summaryPayload;
                if (summary != null
                    && summary.reducerRevision > MemoryThreadReducer.CurrentReducerRevision)
                    return true;
            }
            return root.rollingSummaryBlock?.summaryPayload != null
                && root.rollingSummaryBlock.summaryPayload.reducerRevision
                    > MemoryThreadReducer.CurrentReducerRevision;
        }

        private static void AddBlockPressureAtoms(
            string ownerId,
            SavedMemoryBlock block,
            string protectedAdmissionRecordId,
            List<MemoryPressureAtom> atoms)
        {
            if (block == null || IsPlayerAuthored(block)) return;
            if (block.kind != MemoryContractTokens.KindSummary)
            {
                if (block.recordId == protectedAdmissionRecordId) return;
                MemoryLogicalSizeResult size = MemoryLogicalPayloadSizer.Size(block);
                if (!size.valid) return;
                atoms.Add(new MemoryPressureAtom
                {
                    ownerPawnId = ownerId,
                    rootId = block.rootId ?? string.Empty,
                    recordId = block.recordId,
                    importance = block.importance,
                    ageUnknown = block.ageUnknown,
                    originalEventTick = block.originalEventTick,
                    logicalBytes = size.totalBytes,
                    blockUnits = 1
                });
                return;
            }
            for (int i = 0; block.summaryPayload?.factBuckets != null
                && i < block.summaryPayload.factBuckets.Count; i++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[i];
                for (int j = 0; bucket?.contributions != null
                    && j < bucket.contributions.Count; j++)
                {
                    SavedMemoryFactContribution c = bucket.contributions[j];
                    if (c == null || c.originRecordId == protectedAdmissionRecordId) continue;
                    MemoryLogicalSizeResult size = MemoryLogicalPayloadSizer.Size(c);
                    if (!size.valid) continue;
                    atoms.Add(new MemoryPressureAtom
                    {
                        ownerPawnId = ownerId,
                        rootId = block.rootId ?? string.Empty,
                        recordId = block.recordId,
                        contributionId = c.contributionId,
                        importance = c.importance,
                        ageUnknown = c.ageUnknown,
                        originalEventTick = c.originalEventTick,
                        logicalBytes = size.totalBytes
                    });
                }
            }
        }

        private static void MarkSummaryTerminalBlockUnits(List<MemoryPressureAtom> atoms)
        {
            Dictionary<string, MemoryPressureAtom> terminal =
                new Dictionary<string, MemoryPressureAtom>(StringComparer.Ordinal);
            for (int i = 0; i < atoms.Count; i++)
            {
                MemoryPressureAtom atom = atoms[i];
                if (string.IsNullOrEmpty(atom.contributionId)) continue;
                string key = atom.ownerPawnId + "\n" + atom.rootId + "\n" + atom.recordId;
                MemoryPressureAtom prior;
                if (!terminal.TryGetValue(key, out prior)
                    || ComparePressureOrder(prior, atom) < 0) terminal[key] = atom;
            }
            foreach (MemoryPressureAtom atom in terminal.Values) atom.blockUnits = 1;
        }

        private static int ComparePressureOrder(MemoryPressureAtom left, MemoryPressureAtom right)
        {
            int tier = PressureTier(left.importance).CompareTo(PressureTier(right.importance));
            if (tier != 0) return tier;
            long leftTick = left.ageUnknown ? long.MaxValue : left.originalEventTick;
            long rightTick = right.ageUnknown ? long.MaxValue : right.originalEventTick;
            int tick = leftTick.CompareTo(rightTick);
            if (tick != 0) return tick;
            return string.Compare(left.contributionId, right.contributionId, StringComparison.Ordinal);
        }

        private static int PressureTier(string value)
        {
            if (value == MemoryContractTokens.ImportanceImportant) return 2;
            if (value == MemoryContractTokens.ImportanceRegular) return 1;
            return 0;
        }

        private static bool ApplyPressureRemovals(
            MemoryPressureOwnerWork owner,
            List<MemoryPressureAtom> removals,
            MemoryReducerPolicy policy)
        {
            bool any = false;
            HashSet<string> affectedRootIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < removals.Count; i++)
            {
                MemoryPressureAtom atom = removals[i];
                if (string.IsNullOrEmpty(atom.rootId))
                {
                    for (int j = owner.standalone.Count - 1; j >= 0; j--)
                        if (owner.standalone[j].recordId == atom.recordId)
                        {
                            owner.standalone.RemoveAt(j);
                            any = true;
                            break;
                        }
                    continue;
                }
                int rootIndex = FindSavedRootIndex(owner.roots, atom.rootId);
                if (rootIndex < 0) continue;
                SavedMemoryThreadRoot root = owner.roots[rootIndex];
                if (string.IsNullOrEmpty(atom.contributionId))
                {
                    for (int j = root.visibleBlocks.Count - 1; j >= 0; j--)
                        if (root.visibleBlocks[j].recordId == atom.recordId)
                        {
                            ClearClosedSummaryReferenceSaved(root, atom.recordId);
                            root.visibleBlocks.RemoveAt(j);
                            any = true;
                            affectedRootIds.Add(root.rootId);
                            break;
                        }
                    if (root.rollingSummaryBlock != null
                        && root.rollingSummaryBlock.recordId == atom.recordId)
                    {
                        root.rollingSummaryBlock = null;
                        any = true;
                        affectedRootIds.Add(root.rootId);
                    }
                }
                else
                {
                    SavedMemoryBlock summary = FindSavedBlock(root, atom.recordId);
                    for (int j = summary?.summaryPayload?.factBuckets.Count - 1 ?? -1; j >= 0; j--)
                    {
                        SavedMemoryFactBucket bucket = summary.summaryPayload.factBuckets[j];
                        for (int k = bucket.contributions.Count - 1; k >= 0; k--)
                            if (bucket.contributions[k].contributionId == atom.contributionId)
                            {
                                bucket.contributions.RemoveAt(k);
                                any = true;
                                affectedRootIds.Add(root.rootId);
                                break;
                            }
                    }
                }
            }
            if (!any) return false;
            for (int i = owner.roots.Count - 1; i >= 0; i--)
            {
                SavedMemoryThreadRoot saved = owner.roots[i];
                if (!affectedRootIds.Contains(saved.rootId)) continue;
                MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(
                    ToReducerRoot(saved, policy), policy);
                if (reduced.refused) return false;
                SavedMemoryThreadRoot normalized = FromReducerRoot(reduced.replacement, saved);
                if (normalized.visibleBlocks.Count == 0 && normalized.rollingSummaryBlock == null
                    && !HasOpenChapter(normalized)) owner.roots.RemoveAt(i);
                else owner.roots[i] = normalized;
            }
            return true;
        }

        private static SavedMemoryBlock FindSavedBlock(SavedMemoryThreadRoot root, string recordId)
        {
            for (int i = 0; root.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].recordId == recordId) return root.visibleBlocks[i];
            return root.rollingSummaryBlock != null && root.rollingSummaryBlock.recordId == recordId
                ? root.rollingSummaryBlock : null;
        }

        private static void ClearClosedSummaryReferenceSaved(
            SavedMemoryThreadRoot root,
            string recordId)
        {
            for (int i = 0; root.chapters != null && i < root.chapters.Count; i++)
                if (root.chapters[i].closedSummaryRecordId == recordId)
                    root.chapters[i].closedSummaryRecordId = string.Empty;
        }

        private bool TryProjectedOwnerBytes(
            MemoryPressureOwnerWork work,
            out long active,
            out long combined)
        {
            active = 0;
            combined = 0;
            MemoryOwnerByteTotals baseline = GetOwnerByteTotals(work.owner.pawnId);
            long delta;
            if (!baseline.valid || !TryListDelta(work, out delta)) return false;
            try
            {
                active = checked(baseline.activeBytes + delta);
                combined = checked(active + baseline.importedBytes);
                return active >= 0 && combined >= 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private bool TryProjectedGlobalBytes(
            List<MemoryPressureOwnerWork> work,
            out long active,
            out long combined)
        {
            active = 0;
            combined = 0;
            MemoryPayloadBudgetTotals baseline = GetGlobalBudgetTotals();
            if (baseline.globalActiveBytes < 0 || baseline.globalImportedBytes < 0) return false;
            long delta = 0;
            try
            {
                for (int i = 0; i < work.Count; i++)
                {
                    long ownerDelta;
                    if (!TryListDelta(work[i], out ownerDelta)) return false;
                    delta = checked(delta + ownerDelta);
                }
                active = checked(baseline.globalActiveBytes + delta);
                combined = checked(active + baseline.globalImportedBytes);
                return active >= 0 && combined >= 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryListDelta(MemoryPressureOwnerWork work, out long delta)
        {
            delta = 0;
            MemoryLogicalSizeResult oldStandalone = SizeSavedBlockList(work.priorStandalone);
            MemoryLogicalSizeResult newStandalone = SizeSavedBlockList(work.standalone);
            MemoryLogicalSizeResult oldRoots = SizeSavedRootList(work.priorRoots);
            MemoryLogicalSizeResult newRoots = SizeSavedRootList(work.roots);
            if (!oldStandalone.valid || !newStandalone.valid || !oldRoots.valid || !newRoots.valid)
                return false;
            try
            {
                delta = checked(newStandalone.totalBytes + newRoots.totalBytes
                    - oldStandalone.totalBytes - oldRoots.totalBytes);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static int CountPressureAtoms(MemoryPressureOwnerWork owner)
        {
            return BuildPressureAtoms(owner).Count;
        }

        private static int TotalPressureAtoms(List<MemoryPressureOwnerWork> work)
        {
            int total = 0;
            for (int i = 0; i < work.Count; i++)
            {
                int count = CountPressureAtoms(work[i]);
                if (total > int.MaxValue - count) return int.MaxValue - 1;
                total += count;
            }
            return total;
        }
    }
}
