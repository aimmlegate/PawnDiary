// MemoryThreadRepairPolicy.cs — deterministic duplicate-root and save-shape repair for M4.
//
// Repair groups roots by their raw canonical tuple, never by a mutable label or stored rootId.
// Compatible duplicates merge in ordinal order. A conflicting row is quarantined as an archived
// detached root; player-authored payload is never guessed away to make a corrupt save fit.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Pure repair result consumed atomically by the saved-store adapter.</summary>
    internal sealed class MemoryThreadRepairResult
    {
        public bool refused;
        public bool changed;
        public bool authoredConflictArchived;
        public int automaticConflictDroppedCount;
        public string reasonToken = string.Empty;
        public List<MemoryReducerRoot> activeRoots = new List<MemoryReducerRoot>();
        public List<MemoryReducerRoot> archivedRoots = new List<MemoryReducerRoot>();
    }

    /// <summary>Repairs exact-root duplicates without consulting live game objects.</summary>
    internal static class MemoryThreadRepairPolicy
    {
        /// <summary>
        /// Produces an input-permutation-independent active root set. Invalid raw identity refuses
        /// the whole plan; conflicting payload is archived, and compatible rows reduce normally.
        /// </summary>
        public static MemoryThreadRepairResult Repair(
            List<MemoryReducerRoot> suppliedRoots,
            MemoryReducerPolicy policy)
        {
            MemoryThreadRepairResult result = new MemoryThreadRepairResult();
            List<MemoryReducerRoot> roots = suppliedRoots ?? new List<MemoryReducerRoot>();
            SortedDictionary<string, List<MemoryReducerRoot>> groups =
                new SortedDictionary<string, List<MemoryReducerRoot>>(StringComparer.Ordinal);
            for (int i = 0; i < roots.Count; i++)
            {
                MemoryReducerRoot source = roots[i];
                if (source == null)
                {
                    result.refused = true;
                    result.reasonToken = "null_root";
                    return result;
                }
                string canonicalRootId;
                if (!MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
                    {
                        ownerPawnId = source.ownerPawnId,
                        ownerEpochToken = source.ownerEpochToken,
                        primarySubjectKind = source.subjectKind,
                        primarySubjectId = source.subjectId
                    }, out canonicalRootId))
                {
                    result.refused = true;
                    result.reasonToken = "invalid_root_identity";
                    return result;
                }
                List<MemoryReducerRoot> group;
                if (!groups.TryGetValue(canonicalRootId, out group))
                {
                    group = new List<MemoryReducerRoot>();
                    groups.Add(canonicalRootId, group);
                }
                group.Add(CanonicalizeRootPlacement(source, canonicalRootId));
            }

            foreach (KeyValuePair<string, List<MemoryReducerRoot>> pair in groups)
            {
                List<MemoryReducerRoot> group = pair.Value;
                group.Sort(delegate(MemoryReducerRoot left, MemoryReducerRoot right)
                {
                    int authored = ContainsEdited(right).CompareTo(ContainsEdited(left));
                    if (authored != 0) return authored;
                    return string.Compare(MemoryThreadReducer.CanonicalState(left),
                        MemoryThreadReducer.CanonicalState(right), StringComparison.Ordinal);
                });
                MemoryReducerRoot merged = group[0].Clone();
                for (int i = 1; i < group.Count; i++)
                {
                    MemoryReducerRoot candidate = group[i];
                    if (!TryMerge(merged, candidate))
                    {
                        if (ContainsEdited(candidate))
                        {
                            result.archivedRoots.Add(candidate.Clone());
                            result.authoredConflictArchived = true;
                        }
                        else
                        {
                            result.automaticConflictDroppedCount++;
                        }
                        result.changed = true;
                    }
                    else
                    {
                        result.changed = true;
                    }
                }

                NormalizeOrder(merged);
                MemoryThreadReductionResult reduced = MemoryThreadReducer.Reduce(merged, policy);
                if (reduced.refused)
                {
                    result.refused = true;
                    result.reasonToken = reduced.reasonToken;
                    result.activeRoots.Clear();
                    result.archivedRoots.Clear();
                    return result;
                }
                result.changed |= reduced.changed;
                result.activeRoots.Add(reduced.replacement);
            }

            return result;
        }

        private static MemoryReducerRoot CanonicalizeRootPlacement(
            MemoryReducerRoot source,
            string canonicalRootId)
        {
            MemoryReducerRoot root = source.Clone();
            root.rootId = canonicalRootId;
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                root.visibleBlocks[i].rootId = canonicalRootId;
            if (root.rollingSummaryBlock != null)
                root.rollingSummaryBlock.rootId = canonicalRootId;
            return root;
        }

        private static bool TryMerge(MemoryReducerRoot target, MemoryReducerRoot candidate)
        {
            // Merge every non-colliding row even when another row conflicts. The caller may quarantine
            // an authored alternate or diagnose an automatic loser, but unrelated evidence must not
            // disappear merely because one record ID disagrees.
            bool compatible = true;
            Dictionary<string, MemoryReducerChapter> chapters = ChapterMap(target.chapters);
            for (int i = 0; i < candidate.chapters.Count; i++)
            {
                MemoryReducerChapter existing;
                if (chapters.TryGetValue(candidate.chapters[i].chapterId, out existing)
                    && !SameChapter(existing, candidate.chapters[i])) compatible = false;
            }
            Dictionary<string, MemoryReducerBlock> blocks = BlockMap(target.visibleBlocks);
            for (int i = 0; i < candidate.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock existing;
                if (blocks.TryGetValue(candidate.visibleBlocks[i].recordId, out existing)
                    && !SameBlockIgnoringSuppression(existing, candidate.visibleBlocks[i]))
                    compatible = false;
            }
            if (target.rollingSummaryBlock != null && candidate.rollingSummaryBlock != null
                && !SameBlockIgnoringSuppression(
                    target.rollingSummaryBlock, candidate.rollingSummaryBlock)) compatible = false;

            for (int i = 0; i < candidate.chapters.Count; i++)
                if (!chapters.ContainsKey(candidate.chapters[i].chapterId))
                    target.chapters.Add(candidate.chapters[i].Clone());
            for (int i = 0; i < candidate.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock existing;
                if (!blocks.TryGetValue(candidate.visibleBlocks[i].recordId, out existing))
                {
                    target.visibleBlocks.Add(candidate.visibleBlocks[i].Clone());
                    blocks.Add(candidate.visibleBlocks[i].recordId,
                        target.visibleBlocks[target.visibleBlocks.Count - 1]);
                }
                else if (!SameBlockIgnoringSuppression(existing, candidate.visibleBlocks[i]))
                {
                    MemoryReducerBlock winner = PreferBlock(existing, candidate.visibleBlocks[i]);
                    if (!ReferenceEquals(winner, existing))
                    {
                        int targetIndex = target.visibleBlocks.IndexOf(existing);
                        MemoryReducerBlock replacement = winner.Clone();
                        replacement.suppressed |= existing.suppressed
                            || candidate.visibleBlocks[i].suppressed;
                        target.visibleBlocks[targetIndex] = replacement;
                        blocks[candidate.visibleBlocks[i].recordId] = replacement;
                    }
                }
                else
                    existing.suppressed |= candidate.visibleBlocks[i].suppressed;
            }
            if (target.rollingSummaryBlock == null && candidate.rollingSummaryBlock != null)
                target.rollingSummaryBlock = candidate.rollingSummaryBlock.Clone();
            else if (target.rollingSummaryBlock != null && candidate.rollingSummaryBlock != null)
            {
                bool suppressed = target.rollingSummaryBlock.suppressed
                    || candidate.rollingSummaryBlock.suppressed;
                if (!SameBlockIgnoringSuppression(
                    target.rollingSummaryBlock, candidate.rollingSummaryBlock))
                    target.rollingSummaryBlock = PreferBlock(
                        target.rollingSummaryBlock, candidate.rollingSummaryBlock).Clone();
                target.rollingSummaryBlock.suppressed = suppressed;
            }
            target.structuralRevision = Math.Max(target.structuralRevision, candidate.structuralRevision);
            target.statusRevision = Math.Max(target.statusRevision, candidate.statusRevision);
            target.nextChapterOrdinal = Math.Max(target.nextChapterOrdinal, candidate.nextChapterOrdinal);
            if (!string.IsNullOrEmpty(candidate.frozenSubjectLabel)
                && (string.IsNullOrEmpty(target.frozenSubjectLabel)
                    || string.Compare(candidate.frozenSubjectLabel, target.frozenSubjectLabel,
                        StringComparison.Ordinal) < 0))
                target.frozenSubjectLabel = candidate.frozenSubjectLabel;
            return compatible;
        }

        private static MemoryReducerBlock PreferBlock(
            MemoryReducerBlock left,
            MemoryReducerBlock right)
        {
            bool leftAuthored = IsAuthored(left);
            bool rightAuthored = IsAuthored(right);
            if (leftAuthored != rightAuthored) return rightAuthored ? right : left;
            return string.Compare(
                MemoryThreadReducer.CanonicalState(Holder(left)),
                MemoryThreadReducer.CanonicalState(Holder(right)),
                StringComparison.Ordinal) <= 0 ? left : right;
        }

        private static bool IsAuthored(MemoryReducerBlock block)
        {
            return block != null && (block.playerEdited
                || !string.IsNullOrEmpty(block.playerWording));
        }

        private static Dictionary<string, MemoryReducerChapter> ChapterMap(
            List<MemoryReducerChapter> values)
        {
            Dictionary<string, MemoryReducerChapter> result =
                new Dictionary<string, MemoryReducerChapter>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!result.ContainsKey(values[i].chapterId)) result.Add(values[i].chapterId, values[i]);
            return result;
        }

        private static Dictionary<string, MemoryReducerBlock> BlockMap(List<MemoryReducerBlock> values)
        {
            Dictionary<string, MemoryReducerBlock> result =
                new Dictionary<string, MemoryReducerBlock>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!result.ContainsKey(values[i].recordId)) result.Add(values[i].recordId, values[i]);
            return result;
        }

        private static bool SameChapter(MemoryReducerChapter left, MemoryReducerChapter right)
        {
            return left.chapterId == right.chapterId && left.ordinal == right.ordinal
                && left.phaseToken == right.phaseToken && left.openedTick == right.openedTick
                && left.lastActivityTick == right.lastActivityTick && left.closedTick == right.closedTick
                && left.closureReasonToken == right.closureReasonToken && left.closed == right.closed
                && left.closedSummaryRecordId == right.closedSummaryRecordId;
        }

        private static bool SameBlock(MemoryReducerBlock left, MemoryReducerBlock right)
        {
            MemoryReducerRoot leftHolder = Holder(left);
            MemoryReducerRoot rightHolder = Holder(right);
            return string.Equals(MemoryThreadReducer.CanonicalState(leftHolder),
                MemoryThreadReducer.CanonicalState(rightHolder), StringComparison.Ordinal);
        }

        private static bool SameBlockIgnoringSuppression(
            MemoryReducerBlock left,
            MemoryReducerBlock right)
        {
            MemoryReducerBlock leftCopy = left.Clone();
            MemoryReducerBlock rightCopy = right.Clone();
            leftCopy.suppressed = false;
            rightCopy.suppressed = false;
            return SameBlock(leftCopy, rightCopy);
        }

        private static MemoryReducerRoot Holder(MemoryReducerBlock block)
        {
            MemoryReducerRoot holder = new MemoryReducerRoot
            {
                rootId = block.rootId,
                ownerPawnId = block.ownerPawnId,
                ownerEpochToken = block.ownerEpochToken,
                subjectKind = MemoryContractTokens.SubjectStream,
                subjectId = MemoryContractTokens.StreamBodyHistory,
                nextChapterOrdinal = 1
            };
            holder.visibleBlocks.Add(block);
            return holder;
        }

        private static bool ContainsEdited(MemoryReducerRoot root)
        {
            if (IsAuthored(root.rollingSummaryBlock)) return true;
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (IsAuthored(root.visibleBlocks[i])) return true;
            return false;
        }

        private static void NormalizeOrder(MemoryReducerRoot root)
        {
            root.chapters.Sort(delegate(MemoryReducerChapter left, MemoryReducerChapter right)
            {
                int ordinal = left.ordinal.CompareTo(right.ordinal);
                return ordinal != 0 ? ordinal : string.Compare(
                    left.chapterId, right.chapterId, StringComparison.Ordinal);
            });
            root.visibleBlocks.Sort(delegate(MemoryReducerBlock left, MemoryReducerBlock right)
            {
                long leftTick = left.ageUnknown ? long.MaxValue : left.originalEventTick;
                long rightTick = right.ageUnknown ? long.MaxValue : right.originalEventTick;
                int tick = leftTick.CompareTo(rightTick);
                return tick != 0 ? tick : string.Compare(
                    left.recordId, right.recordId, StringComparison.Ordinal);
            });
        }
    }
}
