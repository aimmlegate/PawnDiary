// MemoryThreadRepairPolicy.cs — deterministic duplicate-root and save-shape repair for M4.
//
// Repair groups roots by their raw canonical tuple, never by a mutable label or stored rootId.
// Compatible duplicates merge in ordinal order. A conflicting row is quarantined as an archived
// detached root; player-authored payload is never guessed away to make a corrupt save fit.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

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
        /// <summary>Stable bounded-diagnostic token for discarded automatic repair alternates.</summary>
        internal const string AutomaticConflictDiagnosticToken = "repair_automatic_conflict";

        /// <summary>Stable current-schema Imported identity for one quarantined authored payload.</summary>
        internal static string ArchiveFingerprint(
            MemoryReducerRoot archivedRoot,
            string recordId,
            string contributionId,
            string reasonToken)
        {
            if (archivedRoot == null || string.IsNullOrWhiteSpace(recordId)
                || string.IsNullOrWhiteSpace(reasonToken)) return string.Empty;
            string canonical = OrdinalSegmentCodec.Segment("memory-repair-imported-v1")
                + OrdinalSegmentCodec.Segment(archivedRoot.ownerPawnId ?? string.Empty)
                + OrdinalSegmentCodec.Segment(archivedRoot.ownerEpochToken ?? string.Empty)
                + OrdinalSegmentCodec.Segment(recordId)
                + OrdinalSegmentCodec.Segment(contributionId ?? string.Empty)
                + OrdinalSegmentCodec.Segment(reasonToken)
                + OrdinalSegmentCodec.Segment(MemoryThreadReducer.CanonicalState(archivedRoot));
            try
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(canonical));
                    StringBuilder result = new StringBuilder(64);
                    for (int index = 0; index < digest.Length; index++)
                        result.Append(digest[index].ToString("x2",
                            System.Globalization.CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        /// <summary>Maps a pure repair result to its bounded component diagnostic, if any.</summary>
        internal static string DiagnosticReason(MemoryThreadRepairResult result)
        {
            return result != null && result.automaticConflictDroppedCount > 0
                ? AutomaticConflictDiagnosticToken : string.Empty;
        }

        /// <summary>
        /// Finds the physical source row whose content matches the repair-selected block.
        /// The comparison deliberately ignores every field repair itself rewrites — root/chapter
        /// placement, rebuilt summary identity, the normalized authored flag, and suppression
        /// (which repair combines with logical OR). Comparing those would make a saved row from a
        /// non-canonical duplicate never match its own winner, silently falling back to first-wins.
        /// </summary>
        internal static int FindPublicationSourceIndex(
            List<MemoryReducerBlock> candidates,
            MemoryReducerBlock selected)
        {
            if (candidates == null || selected == null) return -1;
            MemoryReducerBlock target = NeutralizeRepairPlacement(selected);
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] != null && SameBlock(
                    NeutralizeRepairPlacement(candidates[i]), target)) return i;
            return -1;
        }

        /// <summary>
        /// Blanks the placement/identity fields that repair owns so two rows can be compared on the
        /// payload the player and the prompt actually see.
        /// </summary>
        private static MemoryReducerBlock NeutralizeRepairPlacement(MemoryReducerBlock block)
        {
            MemoryReducerBlock copy = block.Clone();
            copy.suppressed = false;
            copy.rootId = string.Empty;
            copy.chapterId = string.Empty;
            NormalizePlayerWordingFlag(copy);
            if (copy.kind == MemoryContractTokens.KindSummary)
            {
                // Closed/rolling summary ids are derived from the canonical root and chapter ordinal,
                // so repair rebuilds them. An ordinary record keeps its id and stays discriminating.
                copy.recordId = string.Empty;
                copy.sourceOccurrenceId = string.Empty;
            }
            NeutralizeContributionChapterIds(copy.summaryPayload);
            return copy;
        }

        private static void NeutralizeContributionChapterIds(MemoryReducerSummary summary)
        {
            for (int i = 0; summary?.factBuckets != null && i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                for (int j = 0; bucket?.contributions != null
                    && j < bucket.contributions.Count; j++)
                    if (bucket.contributions[j] != null)
                        bucket.contributions[j].originChapterId = string.Empty;
            }
        }

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
                if (MemoryThreadReducer.HasUnknownNewerReducerRevision(source))
                {
                    result.refused = true;
                    result.reasonToken = "newer_reducer_revision";
                    return result;
                }
                string canonicalRootId;
                if (!TryCreateCanonicalRootId(source, out canonicalRootId))
                {
                    result.refused = true;
                    result.reasonToken = "invalid_root_identity";
                    return result;
                }
                MemoryReducerRoot repairSource = source;
                if (IsAuthored(source.rollingSummaryBlock))
                {
                    // Rolling summaries have derived identities and deterministic structured
                    // content, so a saved player override cannot remain in the rolling slot. Keep
                    // that override losslessly in Imported and repair the active slot back to its
                    // deterministic wording instead of refusing this owner forever.
                    MemoryReducerRoot archived = source.Clone();
                    archived.visibleBlocks.Clear();
                    result.archivedRoots.Add(archived);
                    result.authoredConflictArchived = true;
                    repairSource = source.Clone();
                    repairSource.rollingSummaryBlock.playerEdited = false;
                    repairSource.rollingSummaryBlock.playerWording = string.Empty;
                    result.changed = true;
                }
                MemoryReducerRoot canonicalized;
                string canonicalizeReason;
                if (!TryCanonicalizeRootPlacement(
                    repairSource, canonicalRootId, out canonicalized, out canonicalizeReason))
                {
                    result.refused = true;
                    result.reasonToken = canonicalizeReason;
                    return result;
                }
                result.changed |= !string.Equals(
                    MemoryThreadReducer.CanonicalState(source),
                    MemoryThreadReducer.CanonicalState(canonicalized),
                    StringComparison.Ordinal);
                List<MemoryReducerRoot> group;
                if (!groups.TryGetValue(canonicalRootId, out group))
                {
                    group = new List<MemoryReducerRoot>();
                    groups.Add(canonicalRootId, group);
                }
                group.Add(canonicalized);
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

                MemoryReducerRoot normalized;
                string normalizeReason;
                if (!TryCanonicalizeRootPlacement(
                    merged, pair.Key, out normalized, out normalizeReason))
                {
                    result.refused = true;
                    result.reasonToken = normalizeReason;
                    result.activeRoots.Clear();
                    result.archivedRoots.Clear();
                    return result;
                }
                result.changed |= !string.Equals(
                    MemoryThreadReducer.CanonicalState(merged),
                    MemoryThreadReducer.CanonicalState(normalized),
                    StringComparison.Ordinal);
                merged = normalized;
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

        private static bool TryCreateCanonicalRootId(
            MemoryReducerRoot source,
            out string canonicalRootId)
        {
            return MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
            {
                ownerPawnId = source.ownerPawnId,
                ownerEpochToken = source.ownerEpochToken,
                primarySubjectKind = source.subjectKind,
                primarySubjectId = source.subjectId
            }, out canonicalRootId);
        }

        private static bool TryCanonicalizeRootPlacement(
            MemoryReducerRoot source,
            string canonicalRootId,
            out MemoryReducerRoot repaired,
            out string reason)
        {
            MemoryReducerRoot root = source.Clone();
            repaired = null;
            reason = "invalid_repair_placement";
            root.rootId = canonicalRootId;
            List<MemoryReducerChapter> chapters = new List<MemoryReducerChapter>();
            for (int i = 0; root.chapters != null && i < root.chapters.Count; i++)
            {
                if (root.chapters[i] == null)
                {
                    reason = "null_chapter";
                    return false;
                }
                chapters.Add(root.chapters[i].Clone());
            }
            chapters.Sort(CompareRepairChapterCandidate);

            Dictionary<string, string> chapterIdMap =
                new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<long, string> ordinalMap = new Dictionary<long, string>();
            HashSet<long> usedOrdinals = new HashSet<long>();
            List<MemoryReducerChapter> pending = new List<MemoryReducerChapter>();
            long maximumOrdinal = 0;
            for (int i = 0; i < chapters.Count; i++)
            {
                MemoryReducerChapter chapter = chapters[i];
                if (chapter.ordinal > 0 && usedOrdinals.Add(chapter.ordinal))
                {
                    maximumOrdinal = Math.Max(maximumOrdinal, chapter.ordinal);
                    continue;
                }
                pending.Add(chapter);
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (maximumOrdinal == long.MaxValue)
                {
                    reason = "chapter_sequence_saturated";
                    return false;
                }
                maximumOrdinal++;
                pending[i].ordinal = maximumOrdinal;
                usedOrdinals.Add(maximumOrdinal);
            }

            for (int i = 0; i < chapters.Count; i++)
            {
                MemoryReducerChapter chapter = chapters[i];
                string oldId = chapter.chapterId ?? string.Empty;
                string newId;
                if (!MemoryIdentityCodec.TryCreateChapterId(
                    canonicalRootId, chapter.ordinal, out newId))
                {
                    reason = "invalid_chapter";
                    return false;
                }
                if (!chapterIdMap.ContainsKey(oldId)) chapterIdMap.Add(oldId, newId);
                if (!ordinalMap.ContainsKey(chapter.ordinal)) ordinalMap.Add(chapter.ordinal, newId);
                chapter.chapterId = newId;
            }
            root.chapters = chapters;
            if (maximumOrdinal == long.MaxValue)
            {
                reason = "chapter_sequence_saturated";
                return false;
            }
            // The cursor is a monotonic high-water mark, never max+1. Invariant cleanup may delete
            // the newest closed chapter once its summary ages out, and rewinding the cursor would
            // hand the next chapter that dead chapter's exact id — and, when it closes, the same
            // closed-summary record id. Identity reuse is what the whole codec exists to prevent.
            root.nextChapterOrdinal = Math.Max(source.nextChapterOrdinal, maximumOrdinal + 1);

            MemoryRootIdentity identity = new MemoryRootIdentity
            {
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                primarySubjectKind = root.subjectKind,
                primarySubjectId = root.subjectId
            };
            for (int i = 0; i < root.visibleBlocks.Count; i++)
            {
                if (!TryRemapBlock(
                    root.visibleBlocks[i], false, identity, canonicalRootId,
                    chapterIdMap, ordinalMap, out reason)) return false;
            }
            if (root.rollingSummaryBlock != null
                && !TryRemapBlock(
                    root.rollingSummaryBlock, true, identity, canonicalRootId,
                    chapterIdMap, ordinalMap, out reason)) return false;

            NormalizeOpenChapters(root);
            RebindClosedSummaryReferences(root);
            NormalizeOrder(root);
            repaired = root;
            reason = string.Empty;
            return true;
        }

        private static bool TryRemapBlock(
            MemoryReducerBlock block,
            bool rollingSlot,
            MemoryRootIdentity identity,
            string canonicalRootId,
            Dictionary<string, string> chapterIdMap,
            Dictionary<long, string> ordinalMap,
            out string reason)
        {
            reason = "invalid_block";
            if (block == null) return false;
            block.rootId = canonicalRootId;
            if (rollingSlot)
            {
                if (!string.IsNullOrWhiteSpace(block.playerWording))
                {
                    reason = "authored_rolling_summary";
                    return false;
                }
                block.playerEdited = false;
                block.playerWording = string.Empty;
                block.chapterId = string.Empty;
                if (!MemoryIdentityCodec.TryCreateRollingSummaryId(identity, out block.recordId)
                    || !MemoryIdentityCodec.TryCreateRollingSummarySourceId(
                        identity, out block.sourceOccurrenceId)) return false;
            }
            else
            {
                NormalizePlayerWordingFlag(block);
                string remappedChapter;
                if (!TryRemapChapterId(
                    block.chapterId, chapterIdMap, ordinalMap, out remappedChapter)) return false;
                block.chapterId = remappedChapter;
                if (block.kind == MemoryContractTokens.KindSummary)
                {
                    long ordinal;
                    string ignoredRoot;
                    if (!MemoryIdentityCodec.TryParseChapterId(
                            remappedChapter, out ignoredRoot, out ordinal)
                        || !MemoryIdentityCodec.TryCreateClosedSummaryId(
                            identity, ordinal, out block.recordId)
                        || !MemoryIdentityCodec.TryCreateClosedSummarySourceId(
                            identity, ordinal, out block.sourceOccurrenceId)) return false;
                }
            }
            RemapContributionChapterIds(block.summaryPayload, chapterIdMap, ordinalMap);
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Makes the authored flag agree with the authored prose, which the reducer contract
        /// requires. Prose without the flag gains protection. The inverse — a flag with no prose —
        /// carries no player text to preserve, so repair clears it rather than protecting an empty
        /// row forever; that is the only direction where repair drops a protection marker.
        /// </summary>
        private static void NormalizePlayerWordingFlag(MemoryReducerBlock block)
        {
            if (string.IsNullOrWhiteSpace(block.playerWording))
            {
                block.playerWording = string.Empty;
                block.playerEdited = false;
            }
            else
            {
                block.playerEdited = true;
            }
        }

        private static bool TryRemapChapterId(
            string oldId,
            Dictionary<string, string> chapterIdMap,
            Dictionary<long, string> ordinalMap,
            out string remapped)
        {
            if (chapterIdMap.TryGetValue(oldId ?? string.Empty, out remapped)) return true;
            string ignoredRoot;
            long ordinal;
            return MemoryIdentityCodec.TryParseChapterId(oldId, out ignoredRoot, out ordinal)
                && ordinalMap.TryGetValue(ordinal, out remapped);
        }

        private static void RemapContributionChapterIds(
            MemoryReducerSummary summary,
            Dictionary<string, string> chapterIdMap,
            Dictionary<long, string> ordinalMap)
        {
            for (int i = 0; summary?.factBuckets != null && i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                for (int j = 0; bucket?.contributions != null
                    && j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution contribution = bucket.contributions[j];
                    string remapped;
                    if (contribution != null && TryRemapChapterId(
                        contribution.originChapterId, chapterIdMap, ordinalMap, out remapped))
                        contribution.originChapterId = remapped;
                }
            }
        }

        private static void NormalizeOpenChapters(MemoryReducerRoot root)
        {
            if (root.chapters.Count == 0) return;
            MemoryReducerChapter greatest = root.chapters[0];
            for (int i = 1; i < root.chapters.Count; i++)
                if (CompareOpenWinner(greatest, root.chapters[i]) < 0) greatest = root.chapters[i];
            for (int i = 0; i < root.chapters.Count; i++)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                if (chapter.closed || ReferenceEquals(chapter, greatest) && !greatest.closed) continue;
                chapter.closed = true;
                chapter.closedTick = Math.Max(chapter.openedTick, chapter.lastActivityTick);
                chapter.closureReasonToken = MemoryChapterTokens.Repair;
                chapter.closedSummaryRecordId = string.Empty;
            }
        }

        private static void RebindClosedSummaryReferences(MemoryReducerRoot root)
        {
            Dictionary<string, string> summaryByChapter =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < root.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block != null && block.kind == MemoryContractTokens.KindSummary
                    && block.summaryRole == MemoryContractTokens.SummaryRoleClosed
                    && !summaryByChapter.ContainsKey(block.chapterId))
                    summaryByChapter.Add(block.chapterId, block.recordId);
            }
            for (int i = 0; i < root.chapters.Count; i++)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                string summaryId;
                chapter.closedSummaryRecordId = chapter.closed
                    && summaryByChapter.TryGetValue(chapter.chapterId, out summaryId)
                    ? summaryId : string.Empty;
            }
        }

        private static int CompareRepairChapterCandidate(
            MemoryReducerChapter left,
            MemoryReducerChapter right)
        {
            bool leftValid = left.ordinal > 0;
            bool rightValid = right.ordinal > 0;
            int valid = rightValid.CompareTo(leftValid);
            if (valid != 0) return valid;
            int ordinal = left.ordinal.CompareTo(right.ordinal);
            if (ordinal != 0) return ordinal;
            int id = string.Compare(left.chapterId, right.chapterId, StringComparison.Ordinal);
            if (id != 0) return id;
            int opened = left.openedTick.CompareTo(right.openedTick);
            if (opened != 0) return opened;
            int activity = left.lastActivityTick.CompareTo(right.lastActivityTick);
            if (activity != 0) return activity;
            int closed = left.closed.CompareTo(right.closed);
            if (closed != 0) return closed;
            return string.Compare(
                left.closureReasonToken, right.closureReasonToken, StringComparison.Ordinal);
        }

        private static int CompareOpenWinner(
            MemoryReducerChapter left,
            MemoryReducerChapter right)
        {
            int ordinal = left.ordinal.CompareTo(right.ordinal);
            if (ordinal != 0) return ordinal;
            int opened = left.openedTick.CompareTo(right.openedTick);
            if (opened != 0) return opened;
            int activity = left.lastActivityTick.CompareTo(right.lastActivityTick);
            if (activity != 0) return activity;
            return string.Compare(left.chapterId, right.chapterId, StringComparison.Ordinal);
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
