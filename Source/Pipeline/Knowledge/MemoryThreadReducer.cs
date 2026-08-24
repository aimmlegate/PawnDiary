// MemoryThreadReducer.cs — pure M4 TTL, chapter summarization, target, and hard-bound reducer.
//
// Runtime adapters translate saved IExposable rows to these detached snapshots, call Reduce, size
// the complete replacement, and only then swap it into the save graph. Keeping this file plain C#
// makes the destructive policy exhaustively testable without starting RimWorld.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Plain subject-row projection used while building bounded contribution references.</summary>
    internal sealed class MemoryReducerSubjectRefCandidate
    {
        public string subjectRefId = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
    }

    /// <summary>
    /// Builds the bounded reference sets copied from one Event/Landmark fact into a Summary
    /// contribution. The bucket already carries the fact's canonical subject, so only other
    /// disclosed subjects belong in <c>subjectRefIds</c>.
    /// </summary>
    internal static class MemoryContributionReferencePolicy
    {
        public static List<string> SelectSubjectRefIds(
            List<MemoryReducerSubjectRefCandidate> candidates,
            string canonicalSubjectKind,
            string canonicalSubjectId,
            int maximum)
        {
            List<string> selected = new List<string>();
            if (candidates == null || maximum <= 0) return selected;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                MemoryReducerSubjectRefCandidate candidate = candidates[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.subjectRefId)
                    || (string.Equals(candidate.subjectKind, canonicalSubjectKind,
                            StringComparison.Ordinal)
                        && string.Equals(candidate.subjectId, canonicalSubjectId,
                            StringComparison.Ordinal))
                    || !seen.Add(candidate.subjectRefId)) continue;
                selected.Add(candidate.subjectRefId);
            }
            selected.Sort(StringComparer.Ordinal);
            if (selected.Count > maximum) selected.RemoveRange(maximum, selected.Count - maximum);
            return selected;
        }

        public static List<string> SelectReferenceIds(List<string> candidates, int maximum)
        {
            List<string> selected = new List<string>();
            if (candidates == null || maximum <= 0) return selected;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && seen.Add(candidates[i]))
                    selected.Add(candidates[i]);
            selected.Sort(StringComparer.Ordinal);
            if (selected.Count > maximum) selected.RemoveRange(maximum, selected.Count - maximum);
            return selected;
        }
    }

    /// <summary>Detached fact carried by an Event or Landmark block.</summary>
    internal sealed class MemoryReducerFact
    {
        public string factId = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public string canonicalValueKind = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;
        public List<string> subjectRefIds = new List<string>();
        public List<string> provenanceRefIds = new List<string>();

        public MemoryReducerFact Clone()
        {
            return new MemoryReducerFact
            {
                factId = factId ?? string.Empty,
                factKind = factKind ?? string.Empty,
                canonicalSubjectKind = canonicalSubjectKind ?? string.Empty,
                canonicalSubjectId = canonicalSubjectId ?? string.Empty,
                aggregationToken = aggregationToken ?? string.Empty,
                canonicalValueKind = canonicalValueKind ?? string.Empty,
                canonicalValue = canonicalValue ?? string.Empty,
                majorTurningPoint = majorTurningPoint,
                reversal = reversal,
                subjectRefIds = Copy(subjectRefIds),
                provenanceRefIds = Copy(provenanceRefIds)
            };
        }

        private static List<string> Copy(List<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }
    }

    /// <summary>One immutable, dated atom retained inside a Summary bucket.</summary>
    internal sealed class MemoryReducerContribution
    {
        public string contributionId = string.Empty;
        public string originChapterId = string.Empty;
        public string originRecordId = string.Empty;
        public int originFactOrdinal = -1;
        public string originFactId = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        public string category = string.Empty;
        public string importance = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;
        public string sourceOccurrenceId = string.Empty;
        public List<string> subjectRefIds = new List<string>();
        public List<string> provenanceRefIds = new List<string>();

        public MemoryReducerContribution Clone()
        {
            return new MemoryReducerContribution
            {
                contributionId = contributionId ?? string.Empty,
                originChapterId = originChapterId ?? string.Empty,
                originRecordId = originRecordId ?? string.Empty,
                originFactOrdinal = originFactOrdinal,
                originFactId = originFactId ?? string.Empty,
                originalEventTick = originalEventTick,
                ageUnknown = ageUnknown,
                category = category ?? string.Empty,
                importance = importance ?? string.Empty,
                canonicalValue = canonicalValue ?? string.Empty,
                majorTurningPoint = majorTurningPoint,
                reversal = reversal,
                sourceOccurrenceId = sourceOccurrenceId ?? string.Empty,
                subjectRefIds = subjectRefIds == null
                    ? new List<string>() : new List<string>(subjectRefIds),
                provenanceRefIds = provenanceRefIds == null
                    ? new List<string>() : new List<string>(provenanceRefIds)
            };
        }
    }

    /// <summary>One canonical aggregation bucket inside a Summary.</summary>
    internal sealed class MemoryReducerBucket
    {
        public string bucketKey = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public List<MemoryReducerContribution> contributions =
            new List<MemoryReducerContribution>();
        public int derivedCount;
        public string derivedRangeMin = string.Empty;
        public string derivedRangeMax = string.Empty;
        public long earliestSurvivingTick;
        public long latestSurvivingTick;

        public MemoryReducerBucket Clone()
        {
            MemoryReducerBucket copy = new MemoryReducerBucket
            {
                bucketKey = bucketKey ?? string.Empty,
                factKind = factKind ?? string.Empty,
                canonicalSubjectKind = canonicalSubjectKind ?? string.Empty,
                canonicalSubjectId = canonicalSubjectId ?? string.Empty,
                aggregationToken = aggregationToken ?? string.Empty,
                derivedCount = derivedCount,
                derivedRangeMin = derivedRangeMin ?? string.Empty,
                derivedRangeMax = derivedRangeMax ?? string.Empty,
                earliestSurvivingTick = earliestSurvivingTick,
                latestSurvivingTick = latestSurvivingTick
            };
            if (contributions != null)
                for (int i = 0; i < contributions.Count; i++)
                    if (contributions[i] != null) copy.contributions.Add(contributions[i].Clone());
            return copy;
        }
    }

    /// <summary>Canonical structured source of one Summary block.</summary>
    internal sealed class MemoryReducerSummary
    {
        public int reducerRevision;
        public long factsRevision;
        public string canonicalFactsFingerprint = string.Empty;
        public List<MemoryReducerBucket> factBuckets = new List<MemoryReducerBucket>();
        public List<string> availableSubjectRefIds = new List<string>();
        public List<string> availableProvenanceRefIds = new List<string>();
        public int derivedCategoryMask;
        public string highestSurvivingImportance = string.Empty;
        public long earliestSurvivingTick;
        public long latestSurvivingTick;
        public string deterministicWording = string.Empty;
        public string optionalLlmWording = string.Empty;
        public string optionalLlmFingerprint = string.Empty;

        public MemoryReducerSummary Clone()
        {
            MemoryReducerSummary copy = new MemoryReducerSummary
            {
                reducerRevision = reducerRevision,
                factsRevision = factsRevision,
                canonicalFactsFingerprint = canonicalFactsFingerprint ?? string.Empty,
                derivedCategoryMask = derivedCategoryMask,
                highestSurvivingImportance = highestSurvivingImportance ?? string.Empty,
                earliestSurvivingTick = earliestSurvivingTick,
                latestSurvivingTick = latestSurvivingTick,
                deterministicWording = deterministicWording ?? string.Empty,
                optionalLlmWording = optionalLlmWording ?? string.Empty,
                optionalLlmFingerprint = optionalLlmFingerprint ?? string.Empty
            };
            if (factBuckets != null)
                for (int i = 0; i < factBuckets.Count; i++)
                    if (factBuckets[i] != null) copy.factBuckets.Add(factBuckets[i].Clone());
            copy.availableSubjectRefIds = availableSubjectRefIds == null
                ? new List<string>() : new List<string>(availableSubjectRefIds);
            copy.availableProvenanceRefIds = availableProvenanceRefIds == null
                ? new List<string>() : new List<string>(availableProvenanceRefIds);
            return copy;
        }
    }

    /// <summary>Detached Event, Landmark, or Summary block.</summary>
    internal sealed class MemoryReducerBlock
    {
        public string recordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string kind = string.Empty;
        public string summaryRole = string.Empty;
        public string category = string.Empty;
        public string importance = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        public string rootId = string.Empty;
        public string chapterId = string.Empty;
        public bool playerEdited;
        public bool suppressed;
        public bool requiredLifecycleLandmark;
        public string automaticWording = string.Empty;
        public string playerWording = string.Empty;
        public List<MemoryReducerFact> facts = new List<MemoryReducerFact>();
        public MemoryReducerSummary summaryPayload;

        public MemoryReducerBlock Clone()
        {
            MemoryReducerBlock copy = new MemoryReducerBlock
            {
                recordId = recordId ?? string.Empty,
                sourceOccurrenceId = sourceOccurrenceId ?? string.Empty,
                captureRuleId = captureRuleId ?? string.Empty,
                factDiscriminator = factDiscriminator ?? string.Empty,
                ownerPawnId = ownerPawnId ?? string.Empty,
                ownerEpochToken = ownerEpochToken ?? string.Empty,
                kind = kind ?? string.Empty,
                summaryRole = summaryRole ?? string.Empty,
                category = category ?? string.Empty,
                importance = importance ?? string.Empty,
                originalEventTick = originalEventTick,
                ageUnknown = ageUnknown,
                rootId = rootId ?? string.Empty,
                chapterId = chapterId ?? string.Empty,
                playerEdited = playerEdited,
                suppressed = suppressed,
                requiredLifecycleLandmark = requiredLifecycleLandmark,
                automaticWording = automaticWording ?? string.Empty,
                playerWording = playerWording ?? string.Empty,
                summaryPayload = summaryPayload == null ? null : summaryPayload.Clone()
            };
            if (facts != null)
                for (int i = 0; i < facts.Count; i++)
                    if (facts[i] != null) copy.facts.Add(facts[i].Clone());
            return copy;
        }
    }

    /// <summary>Detached flat chapter metadata.</summary>
    internal sealed class MemoryReducerChapter
    {
        public string chapterId = string.Empty;
        public long ordinal;
        public string phaseToken = string.Empty;
        public long openedTick;
        public long lastActivityTick;
        public long closedTick;
        public string closureReasonToken = string.Empty;
        public bool closed;
        public string closedSummaryRecordId = string.Empty;

        public MemoryReducerChapter Clone()
        {
            return (MemoryReducerChapter)MemberwiseClone();
        }
    }

    /// <summary>One exact owner/epoch/subject root supplied to the reducer.</summary>
    internal sealed class MemoryReducerRoot
    {
        public string rootId = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenSubjectLabel = string.Empty;
        public long structuralRevision;
        public long statusRevision;
        public int lastAppliedReducerRevision;
        public long nextChapterOrdinal = 1;
        public List<MemoryReducerChapter> chapters = new List<MemoryReducerChapter>();
        public List<MemoryReducerBlock> visibleBlocks = new List<MemoryReducerBlock>();
        public MemoryReducerBlock rollingSummaryBlock;

        public MemoryReducerRoot Clone()
        {
            MemoryReducerRoot copy = new MemoryReducerRoot
            {
                rootId = rootId ?? string.Empty,
                ownerPawnId = ownerPawnId ?? string.Empty,
                ownerEpochToken = ownerEpochToken ?? string.Empty,
                subjectKind = subjectKind ?? string.Empty,
                subjectId = subjectId ?? string.Empty,
                frozenSubjectLabel = frozenSubjectLabel ?? string.Empty,
                structuralRevision = structuralRevision,
                statusRevision = statusRevision,
                lastAppliedReducerRevision = lastAppliedReducerRevision,
                nextChapterOrdinal = nextChapterOrdinal,
                rollingSummaryBlock = rollingSummaryBlock == null
                    ? null : rollingSummaryBlock.Clone()
            };
            if (chapters != null)
                for (int i = 0; i < chapters.Count; i++)
                    if (chapters[i] != null) copy.chapters.Add(chapters[i].Clone());
            if (visibleBlocks != null)
                for (int i = 0; i < visibleBlocks.Count; i++)
                    if (visibleBlocks[i] != null) copy.visibleBlocks.Add(visibleBlocks[i].Clone());
            return copy;
        }
    }

    /// <summary>Tunable detached M4 policy, normalized before every reduction.</summary>
    internal sealed class MemoryReducerPolicy
    {
        public long nowTick;
        public long minorLifetimeTicks = 15L * 60000L;
        public long regularLifetimeTicks = 60L * 60000L;
        public long chapterInactivityTicks = 15L * 60000L;
        public int targetVisibleBlocks = 12;
        public int maximumFactBuckets = 16;
        public int maximumContributionsPerBucket = 32;
        public int maximumContributionsPerSummary = 32;
        public int maximumDistinctSubjects = 4;
        public int maximumSubjectRefsPerContribution = 2;
        public int maximumProvenanceTotal = 16;
        public int maximumProvenanceRefsPerContribution = 2;
        public int maximumVisibleBlocks = 128;
        public int maximumDeterministicWordingUnits = 240;

        public MemoryReducerPolicy Normalize()
        {
            return new MemoryReducerPolicy
            {
                nowTick = Math.Max(0, nowTick),
                minorLifetimeTicks = Math.Max(0, minorLifetimeTicks),
                regularLifetimeTicks = Math.Max(0, regularLifetimeTicks),
                chapterInactivityTicks = Math.Max(0, chapterInactivityTicks),
                targetVisibleBlocks = Math.Max(4, Math.Min(64, targetVisibleBlocks)),
                maximumFactBuckets = Math.Max(1, Math.Min(64, maximumFactBuckets)),
                maximumContributionsPerBucket = Math.Max(
                    1, Math.Min(128, maximumContributionsPerBucket)),
                maximumContributionsPerSummary = Math.Max(
                    1, Math.Min(128, maximumContributionsPerSummary)),
                maximumDistinctSubjects = Math.Max(1, Math.Min(32, maximumDistinctSubjects)),
                maximumSubjectRefsPerContribution = Math.Max(
                    1, Math.Min(8, maximumSubjectRefsPerContribution)),
                maximumProvenanceTotal = Math.Max(1, Math.Min(128, maximumProvenanceTotal)),
                maximumProvenanceRefsPerContribution = Math.Max(
                    1, Math.Min(8, maximumProvenanceRefsPerContribution)),
                maximumVisibleBlocks = Math.Max(4, Math.Min(1024, maximumVisibleBlocks)),
                maximumDeterministicWordingUnits = Math.Max(
                    1, Math.Min(1200, maximumDeterministicWordingUnits))
            };
        }
    }

    /// <summary>Atomic pure result. refused leaves replacement null and input untouched.</summary>
    internal sealed class MemoryThreadReductionResult
    {
        public bool refused;
        public bool changed;
        public bool protectedSaturation;
        public string reasonToken = string.Empty;
        public int expiredBlocks;
        public int expiredContributions;
        public int summarizedBlocks;
        public int emergencyAtomsRemoved;
        public MemoryReducerRoot replacement;
    }

    /// <summary>Deterministic M4 reducer. All destructive work occurs on a detached clone.</summary>
    internal static class MemoryThreadReducer
    {
        public const int CurrentReducerRevision = 1;

        /// <summary>
        /// Runs validation → TTL → closure → target → merge → hard caps → emergency pressure →
        /// cleanup/fingerprint. Invalid or protected-saturated input is never partly mutated.
        /// </summary>
        public static MemoryThreadReductionResult Reduce(
            MemoryReducerRoot input,
            MemoryReducerPolicy suppliedPolicy)
        {
            MemoryThreadReductionResult result = new MemoryThreadReductionResult();
            MemoryReducerPolicy policy = (suppliedPolicy ?? new MemoryReducerPolicy()).Normalize();
            if (HasUnknownNewerReducerRevision(input))
            {
                // A known-schema root written by a newer reducer is not old input that this build may
                // normalize. Leave it byte-for-byte inert so an older build cannot silently downgrade
                // facts or retention semantics it does not understand (T7.2).
                result.refused = true;
                result.reasonToken = "newer_reducer_revision";
                return result;
            }
            string invalid;
            if (!Validate(input, out invalid))
            {
                result.refused = true;
                result.reasonToken = invalid;
                return result;
            }
            if (!ReferenceDescriptorCapsHold(input, policy))
            {
                // Per-contribution descriptor overflow is malformed input, not aggregate pressure.
                // Refuse before cloning/removal so normalization can never erase unrelated evidence
                // while chasing a bound that deleting other contributions cannot repair.
                result.refused = true;
                result.reasonToken = "reference_descriptor_cap";
                return result;
            }

            string before = CanonicalState(input);
            MemoryReducerRoot root = input.Clone();
            Expire(root, policy, result);
            // Expired summaries must disappear before target accounting. Otherwise an empty row can
            // force a live detail block into rolling history immediately before that row is removed.
            RemoveEmptySummaries(root);
            CloseOverdueChapters(root, policy);
            SummarizeClosedChapters(root, result);
            EnforceTarget(root, policy, result);
            if (!NormalizeSummaries(root, policy, result))
                return RevisionSaturated(result);
            RemoveEmptySummaries(root);
            EnforceVisibleHardCap(root, policy, result);
            if (!NormalizeSummaries(root, policy, result))
                return RevisionSaturated(result);
            RemoveEmptySummaries(root);

            if (CountAllBlocks(root) > policy.maximumVisibleBlocks)
            {
                // Every remaining block is protected. The caller may refuse an admission, but the
                // already-saved canon remains byte-for-byte intact rather than being partly erased.
                result.refused = true;
                result.protectedSaturation = true;
                result.reasonToken = "protected_saturation";
                return result;
            }

            root.lastAppliedReducerRevision = CurrentReducerRevision;
            string afterWithoutRevision = CanonicalState(root);
            result.changed = !string.Equals(before, afterWithoutRevision, StringComparison.Ordinal);
            if (result.changed)
            {
                if (input.structuralRevision == long.MaxValue)
                    return RevisionSaturated(result);
                root.structuralRevision = input.structuralRevision + 1;
            }
            result.replacement = root;
            return result;
        }

        /// <summary>
        /// Returns true when a known-schema root or one of its Summary payloads was committed by a
        /// reducer newer than this build. Callers must keep such a root inert rather than repairing it.
        /// </summary>
        internal static bool HasUnknownNewerReducerRevision(MemoryReducerRoot root)
        {
            if (root == null) return false;
            if (root.lastAppliedReducerRevision > CurrentReducerRevision) return true;
            for (int i = 0; root.visibleBlocks != null && i < root.visibleBlocks.Count; i++)
            {
                MemoryReducerSummary summary = root.visibleBlocks[i]?.summaryPayload;
                if (summary != null && summary.reducerRevision > CurrentReducerRevision) return true;
            }
            return root.rollingSummaryBlock?.summaryPayload != null
                && root.rollingSummaryBlock.summaryPayload.reducerRevision > CurrentReducerRevision;
        }

        /// <summary>
        /// Reports whether invariant cleanup may remove the complete root after reduction. Closed,
        /// unreferenced chapter metadata does not keep an otherwise empty root alive.
        /// </summary>
        internal static bool IsRemovableEmptyRoot(MemoryReducerRoot root)
        {
            if (root == null || root.visibleBlocks == null || root.chapters == null) return false;
            if (root.visibleBlocks.Count != 0 || root.rollingSummaryBlock != null) return false;
            for (int i = 0; i < root.chapters.Count; i++)
                if (root.chapters[i] != null && !root.chapters[i].closed) return false;
            return true;
        }

        private static MemoryThreadReductionResult RevisionSaturated(
            MemoryThreadReductionResult result)
        {
            result.refused = true;
            result.changed = false;
            result.reasonToken = "revision_saturated";
            result.replacement = null;
            return result;
        }

        /// <summary>Exact TTL rule shared by blocks and Summary contributions.</summary>
        public static bool IsExpired(
            long nowTick,
            long originalTick,
            bool ageUnknown,
            string importance,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            if (ageUnknown || importance == MemoryContractTokens.ImportanceImportant) return false;
            long lifetime = importance == MemoryContractTokens.ImportanceMinor
                ? Math.Max(0, minorLifetimeTicks)
                : importance == MemoryContractTokens.ImportanceRegular
                    ? Math.Max(0, regularLifetimeTicks) : long.MaxValue;
            return MemoryChapterPolicy.Elapsed(Math.Max(0, nowTick), Math.Max(0, originalTick))
                >= lifetime;
        }

        /// <summary>Canonical diagnostic representation used by fixed-point and permutation tests.</summary>
        public static string CanonicalState(MemoryReducerRoot root)
        {
            if (root == null) return "null";
            StringBuilder value = new StringBuilder();
            Add(value, root.rootId); Add(value, root.ownerPawnId); Add(value, root.ownerEpochToken);
            Add(value, root.subjectKind); Add(value, root.subjectId);
            Add(value, root.frozenSubjectLabel);
            Add(value, root.structuralRevision); Add(value, root.statusRevision);
            Add(value, root.lastAppliedReducerRevision); Add(value, root.nextChapterOrdinal);
            List<MemoryReducerChapter> chapters = root.chapters == null
                ? new List<MemoryReducerChapter>() : new List<MemoryReducerChapter>(root.chapters);
            chapters.Sort(CompareChapter);
            Add(value, chapters.Count);
            for (int i = 0; i < chapters.Count; i++)
            {
                MemoryReducerChapter c = chapters[i];
                Add(value, c.chapterId); Add(value, c.ordinal); Add(value, c.phaseToken);
                Add(value, c.openedTick); Add(value, c.lastActivityTick); Add(value, c.closedTick);
                Add(value, c.closureReasonToken); Add(value, c.closed); Add(value, c.closedSummaryRecordId);
            }
            List<MemoryReducerBlock> blocks = root.visibleBlocks == null
                ? new List<MemoryReducerBlock>() : new List<MemoryReducerBlock>(root.visibleBlocks);
            blocks.Sort(CompareBlock);
            Add(value, blocks.Count);
            for (int i = 0; i < blocks.Count; i++) AddBlock(value, blocks[i]);
            AddBlock(value, root.rollingSummaryBlock);
            return value.ToString();
        }

        private static bool Validate(MemoryReducerRoot root, out string reason)
        {
            reason = "invalid_root";
            if (root == null || root.chapters == null || root.visibleBlocks == null
                || root.structuralRevision < 0 || root.statusRevision < 0
                || root.lastAppliedReducerRevision < 0) return false;
            MemoryRootIdentity identity = new MemoryRootIdentity
            {
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                primarySubjectKind = root.subjectKind,
                primarySubjectId = root.subjectId
            };
            string expectedRoot;
            if (!MemoryIdentityCodec.TryCreateRootId(identity, out expectedRoot)
                || !string.Equals(root.rootId, expectedRoot, StringComparison.Ordinal)) return false;

            HashSet<string> chapterIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<long> ordinals = new HashSet<long>();
            long maximumOrdinal = 0;
            for (int i = 0; i < root.chapters.Count; i++)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                string chapterRoot;
                long ordinal;
                if (chapter == null || !MemoryIdentityCodec.TryParseChapterId(
                        chapter.chapterId, out chapterRoot, out ordinal)
                    || chapterRoot != root.rootId || ordinal != chapter.ordinal
                    || !chapterIds.Add(chapter.chapterId) || !ordinals.Add(ordinal)
                    || chapter.openedTick < 0 || chapter.lastActivityTick < chapter.openedTick
                    || (chapter.closed && (!MemoryChapterTokens.IsKnownClosureReason(
                        chapter.closureReasonToken) || chapter.closedTick < chapter.openedTick))
                    || (!chapter.closed && (!string.IsNullOrEmpty(chapter.closureReasonToken)
                        || !string.IsNullOrEmpty(chapter.closedSummaryRecordId))))
                {
                    reason = "invalid_chapter";
                    return false;
                }
                maximumOrdinal = Math.Max(maximumOrdinal, ordinal);
            }
            if (root.nextChapterOrdinal <= maximumOrdinal) { reason = "invalid_chapter_cursor"; return false; }

            HashSet<string> recordIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (!ValidateBlock(root.visibleBlocks[i], root, chapterIds, recordIds, false, out reason))
                    return false;
            if (root.rollingSummaryBlock != null
                && !ValidateBlock(root.rollingSummaryBlock, root, chapterIds, recordIds, true, out reason))
                return false;
            return true;
        }

        private static bool ValidateBlock(
            MemoryReducerBlock block,
            MemoryReducerRoot root,
            HashSet<string> chapterIds,
            HashSet<string> recordIds,
            bool rollingSlot,
            out string reason)
        {
            reason = "invalid_block";
            if (block == null || block.facts == null || string.IsNullOrEmpty(block.recordId)
                || !recordIds.Add(block.recordId) || block.ownerPawnId != root.ownerPawnId
                || block.ownerEpochToken != root.ownerEpochToken || block.rootId != root.rootId
                || !MemoryContractTokens.IsKnownKind(block.kind)) return false;
            bool hasPlayerWording = !string.IsNullOrWhiteSpace(block.playerWording);
            if (block.playerEdited != hasPlayerWording)
            {
                // Authored prose without the protection flag could otherwise expire, fold, or evict.
                // The inverse flag-only shape is equally ambiguous outside explicit repair.
                reason = "invalid_player_wording";
                return false;
            }
            if (block.kind == MemoryContractTokens.KindSummary)
            {
                string expected;
                MemoryRootIdentity identity = Identity(root);
                if (rollingSlot)
                {
                    if (block.summaryRole != MemoryContractTokens.SummaryRoleRolling
                        || !string.IsNullOrEmpty(block.chapterId)
                        || block.playerEdited || !string.IsNullOrEmpty(block.playerWording)
                        || !MemoryIdentityCodec.TryCreateRollingSummaryId(identity, out expected)) return false;
                }
                else
                {
                    long ordinal;
                    string chapterRoot;
                    if (block.summaryRole != MemoryContractTokens.SummaryRoleClosed
                        || !chapterIds.Contains(block.chapterId)
                        || !MemoryIdentityCodec.TryParseChapterId(block.chapterId, out chapterRoot, out ordinal)
                        || !MemoryIdentityCodec.TryCreateClosedSummaryId(identity, ordinal, out expected)) return false;
                }
                if (block.recordId != expected || block.summaryPayload == null
                    || block.category.Length != 0 || block.importance.Length != 0
                    || block.facts.Count != 0 || block.automaticWording.Length != 0) return false;
                return ValidateSummary(block.summaryPayload);
            }
            if (rollingSlot || block.summaryRole != MemoryContractTokens.SummaryRoleNone
                || !chapterIds.Contains(block.chapterId)
                || !MemoryContractTokens.IsKnownCategory(block.category)
                || !MemoryContractTokens.IsKnownImportance(block.importance)
                || block.summaryPayload != null || (!block.ageUnknown && block.originalEventTick < 0)) return false;
            MemoryRecordIdentity record;
            if (!MemoryIdentityCodec.TryParseRecordId(block.recordId, out record)
                || record.ownerPawnId != root.ownerPawnId || record.ownerEpochToken != root.ownerEpochToken
                || record.sourceOccurrenceId != block.sourceOccurrenceId
                || record.captureRuleId != block.captureRuleId
                || record.factDiscriminator != block.factDiscriminator) return false;
            HashSet<string> factIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < block.facts.Count; i++)
                if (!ValidateFact(block.facts[i], record.captureRuleId, record.factDiscriminator)
                    || !factIds.Add(block.facts[i].factId)) return false;
            return true;
        }

        internal static bool ValidateFact(MemoryReducerFact fact, string rule, string discriminator)
        {
            if (fact == null || fact.subjectRefIds == null || fact.provenanceRefIds == null) return false;
            string expected;
            return MemoryIdentityCodec.TryCreateFactId(
                    rule, discriminator, fact.factKind, fact.canonicalSubjectKind,
                    fact.canonicalSubjectId, fact.aggregationToken, out expected)
                && expected == fact.factId && UniqueNonblank(fact.subjectRefIds)
                && UniqueNonblank(fact.provenanceRefIds)
                && IsCanonicalValue(
                    fact.aggregationToken, fact.canonicalValueKind, fact.canonicalValue);
        }

        private static bool ValidateSummary(MemoryReducerSummary summary)
        {
            if (summary == null || summary.factBuckets == null || summary.factsRevision < 0
                || summary.reducerRevision < 0
                || !UniqueNonblank(summary.availableSubjectRefIds)
                || !UniqueNonblank(summary.availableProvenanceRefIds)) return false;
            HashSet<string> availableSubjects = new HashSet<string>(
                summary.availableSubjectRefIds, StringComparer.Ordinal);
            HashSet<string> availableProvenance = new HashSet<string>(
                summary.availableProvenanceRefIds, StringComparer.Ordinal);
            HashSet<string> bucketIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> contributionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                string expectedBucketKey;
                if (bucket == null || bucket.contributions == null
                    || !TryCreateBucketKey(bucket.factKind, bucket.canonicalSubjectKind,
                        bucket.canonicalSubjectId, bucket.aggregationToken, out expectedBucketKey)
                    || !string.Equals(bucket.bucketKey, expectedBucketKey, StringComparison.Ordinal)
                    || !bucketIds.Add(bucket.bucketKey)) return false;
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution c = bucket.contributions[j];
                    string expected;
                    if (c == null || !contributionIds.Add(c.contributionId)
                        || !MemoryIdentityCodec.TryCreateContributionId(
                            c.originRecordId, c.originFactOrdinal, c.originFactId, out expected)
                        || expected != c.contributionId || !MemoryContractTokens.IsKnownCategory(c.category)
                        || !MemoryContractTokens.IsKnownImportance(c.importance)
                        || string.IsNullOrEmpty(c.sourceOccurrenceId)
                        || (!c.ageUnknown && c.originalEventTick < 0)
                        || !UniqueNonblank(c.subjectRefIds) || !UniqueNonblank(c.provenanceRefIds)
                        || !IsCanonicalValueForAggregation(
                            bucket.aggregationToken, c.canonicalValue)) return false;
                    for (int k = 0; k < c.subjectRefIds.Count; k++)
                        if (!availableSubjects.Contains(c.subjectRefIds[k])) return false;
                    for (int k = 0; k < c.provenanceRefIds.Count; k++)
                        if (!availableProvenance.Contains(c.provenanceRefIds[k])) return false;
                }
            }
            return true;
        }

        private static void Expire(
            MemoryReducerRoot root,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            for (int i = root.visibleBlocks.Count - 1; i >= 0; i--)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block.kind != MemoryContractTokens.KindSummary && !block.playerEdited
                    && IsExpired(policy.nowTick, block.originalEventTick, block.ageUnknown,
                        block.importance, policy.minorLifetimeTicks, policy.regularLifetimeTicks))
                {
                    root.visibleBlocks.RemoveAt(i);
                    result.expiredBlocks++;
                }
            }
            ExpireSummary(root.rollingSummaryBlock, policy, result);
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].kind == MemoryContractTokens.KindSummary)
                    ExpireSummary(root.visibleBlocks[i], policy, result);
        }

        private static void ExpireSummary(
            MemoryReducerBlock block,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            // Player-authored Summary prose protects the Summary as one indivisible atom.
            if (block == null || block.playerEdited || block.summaryPayload == null) return;
            for (int i = block.summaryPayload.factBuckets.Count - 1; i >= 0; i--)
            {
                MemoryReducerBucket bucket = block.summaryPayload.factBuckets[i];
                for (int j = bucket.contributions.Count - 1; j >= 0; j--)
                {
                    MemoryReducerContribution c = bucket.contributions[j];
                    if (IsExpired(policy.nowTick, c.originalEventTick, c.ageUnknown, c.importance,
                        policy.minorLifetimeTicks, policy.regularLifetimeTicks))
                    {
                        bucket.contributions.RemoveAt(j);
                        result.expiredContributions++;
                    }
                }
            }
        }

        private static void CloseOverdueChapters(MemoryReducerRoot root, MemoryReducerPolicy policy)
        {
            for (int i = 0; i < root.chapters.Count; i++)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                MemoryChapterClosurePlan plan = MemoryChapterPolicy.PlanClosure(
                    new MemoryChapterClosureRequest
                    {
                        alreadyClosed = chapter.closed,
                        nowTick = policy.nowTick,
                        lastActivityTick = chapter.lastActivityTick,
                        inactivityTicks = policy.chapterInactivityTicks
                    });
                if (!plan.shouldClose) continue;
                chapter.closed = true;
                chapter.closedTick = plan.closedTick;
                chapter.closureReasonToken = plan.reasonToken;
            }
        }

        private static void SummarizeClosedChapters(
            MemoryReducerRoot root,
            MemoryThreadReductionResult result)
        {
            root.chapters.Sort(CompareChapter);
            for (int i = 0; i < root.chapters.Count; i++)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                if (!chapter.closed || !string.IsNullOrEmpty(chapter.closedSummaryRecordId)) continue;
                List<MemoryReducerBlock> sources = new List<MemoryReducerBlock>();
                for (int j = 0; j < root.visibleBlocks.Count; j++)
                {
                    MemoryReducerBlock block = root.visibleBlocks[j];
                    if (block.chapterId == chapter.chapterId && !block.playerEdited)
                        sources.Add(block);
                }
                MemoryReducerBlock rollingFragment = ExtractChapterContributions(
                    root, chapter.chapterId);
                if (sources.Count == 0 && rollingFragment == null) continue;
                sources.Sort(CompareBlock);
                MemoryReducerBlock summary = CreateSummary(root, chapter);
                bool fragmentFirst = rollingFragment != null
                    && (sources.Count == 0 || CompareBlock(rollingFragment, sources[0]) < 0);
                if (fragmentFirst) Fold(summary, rollingFragment);
                for (int j = 0; j < sources.Count; j++) Fold(summary, sources[j]);
                if (rollingFragment != null && !fragmentFirst) Fold(summary, rollingFragment);
                for (int j = 0; j < sources.Count; j++) root.visibleBlocks.Remove(sources[j]);
                root.visibleBlocks.Add(summary);
                chapter.closedSummaryRecordId = summary.recordId;
                result.summarizedBlocks += sources.Count + (rollingFragment == null ? 0 : 1);
            }
        }

        /// <summary>
        /// Detaches only one chapter's contributions from rolling history. The returned fragment is
        /// an off-graph transport object consumed immediately by <see cref="Fold"/>.
        /// </summary>
        private static MemoryReducerBlock ExtractChapterContributions(
            MemoryReducerRoot root,
            string chapterId)
        {
            MemoryReducerBlock rolling = root.rollingSummaryBlock;
            if (rolling == null || rolling.summaryPayload == null) return null;
            MemoryReducerBlock fragment = null;
            for (int i = rolling.summaryPayload.factBuckets.Count - 1; i >= 0; i--)
            {
                MemoryReducerBucket sourceBucket = rolling.summaryPayload.factBuckets[i];
                MemoryReducerBucket fragmentBucket = null;
                for (int j = sourceBucket.contributions.Count - 1; j >= 0; j--)
                {
                    MemoryReducerContribution contribution = sourceBucket.contributions[j];
                    if (!string.Equals(contribution.originChapterId, chapterId,
                        StringComparison.Ordinal)) continue;
                    if (fragment == null)
                    {
                        fragment = CreateSummary(root, null);
                        fragment.suppressed = rolling.suppressed;
                    }
                    if (fragmentBucket == null)
                    {
                        fragmentBucket = sourceBucket.Clone();
                        fragmentBucket.contributions.Clear();
                        fragment.summaryPayload.factBuckets.Add(fragmentBucket);
                    }
                    fragmentBucket.contributions.Add(contribution.Clone());
                    sourceBucket.contributions.RemoveAt(j);
                }
                if (sourceBucket.contributions.Count == 0)
                    rolling.summaryPayload.factBuckets.RemoveAt(i);
            }
            if (fragment == null) return null;
            MemoryReducerContribution earliest = null;
            for (int i = 0; i < fragment.summaryPayload.factBuckets.Count; i++)
                for (int j = 0; j < fragment.summaryPayload.factBuckets[i].contributions.Count; j++)
                {
                    MemoryReducerContribution candidate =
                        fragment.summaryPayload.factBuckets[i].contributions[j];
                    if (earliest == null || CompareContribution(candidate, earliest) < 0)
                        earliest = candidate;
                }
            fragment.originalEventTick = earliest == null ? 0 : earliest.originalEventTick;
            fragment.ageUnknown = earliest != null && earliest.ageUnknown;
            return fragment;
        }

        private static void EnforceTarget(
            MemoryReducerRoot root,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            int edited = CountEdited(root);
            int allowedUnedited = Math.Max(0, policy.targetVisibleBlocks - edited);
            while (CountUnedited(root) > allowedUnedited)
            {
                MemoryReducerBlock source = OldestUnedited(root.visibleBlocks);
                if (source == null) break;
                MemoryReducerBlock rolling = EnsureRolling(root);
                // Do not fold the rolling block into itself. It lives in its own slot, so source is
                // always a visible row here.
                Fold(rolling, source);
                ClearClosedSummaryReference(root, source.recordId);
                root.visibleBlocks.Remove(source);
                result.summarizedBlocks++;
            }
        }

        private static bool NormalizeSummaries(
            MemoryReducerRoot root,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].kind == MemoryContractTokens.KindSummary)
                    if (!NormalizeSummary(root.visibleBlocks[i], policy, result)) return false;
            if (root.rollingSummaryBlock != null)
                if (!NormalizeSummary(root.rollingSummaryBlock, policy, result)) return false;
            return true;
        }

        private static bool NormalizeSummary(
            MemoryReducerBlock block,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            MemoryReducerSummary summary = block.summaryPayload;
            if (summary == null) return true;
            for (int i = summary.factBuckets.Count - 1; i >= 0; i--)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                bucket.contributions.Sort(CompareContribution);
                DeduplicateContributions(bucket.contributions);
                while (!block.playerEdited
                    && bucket.contributions.Count > policy.maximumContributionsPerBucket)
                {
                    RemoveEmergencyContribution(summary, bucket);
                    result.emergencyAtomsRemoved++;
                }
                if (bucket.contributions.Count == 0) summary.factBuckets.RemoveAt(i);
            }
            summary.factBuckets.Sort(CompareBucket);
            while (!block.playerEdited && TotalContributions(summary) > policy.maximumContributionsPerSummary)
            {
                RemoveEmergencyContribution(summary);
                result.emergencyAtomsRemoved++;
            }
            while (!block.playerEdited && summary.factBuckets.Count > policy.maximumFactBuckets)
            {
                RemoveEmergencyContribution(summary);
                result.emergencyAtomsRemoved++;
                RemoveEmptyBuckets(summary);
            }
            while (!block.playerEdited && !SummaryAggregateReferenceBoundsHold(summary, policy))
            {
                RemoveEmergencyContribution(summary);
                result.emergencyAtomsRemoved++;
                RemoveEmptyBuckets(summary);
            }
            for (int i = summary.factBuckets.Count - 1; i >= 0; i--)
            {
                if (summary.factBuckets[i].contributions.Count == 0)
                    summary.factBuckets.RemoveAt(i);
                else
                    DeriveBucket(summary.factBuckets[i]);
            }
            RebuildAvailableReferenceIds(summary);
            return DeriveSummary(summary, policy.maximumDeterministicWordingUnits);
        }

        private static void RebuildAvailableReferenceIds(MemoryReducerSummary summary)
        {
            HashSet<string> subjects = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> provenance = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < summary.factBuckets.Count; i++)
                for (int j = 0; j < summary.factBuckets[i].contributions.Count; j++)
                {
                    MemoryReducerContribution contribution =
                        summary.factBuckets[i].contributions[j];
                    for (int k = 0; k < contribution.subjectRefIds.Count; k++)
                        subjects.Add(contribution.subjectRefIds[k]);
                    for (int k = 0; k < contribution.provenanceRefIds.Count; k++)
                        provenance.Add(contribution.provenanceRefIds[k]);
                }
            summary.availableSubjectRefIds = new List<string>(subjects);
            summary.availableSubjectRefIds.Sort(StringComparer.Ordinal);
            summary.availableProvenanceRefIds = new List<string>(provenance);
            summary.availableProvenanceRefIds.Sort(StringComparer.Ordinal);
        }

        private static void EnforceVisibleHardCap(
            MemoryReducerRoot root,
            MemoryReducerPolicy policy,
            MemoryThreadReductionResult result)
        {
            while (CountAllBlocks(root) > policy.maximumVisibleBlocks)
            {
                MemoryReducerBlock source = OldestUnedited(root.visibleBlocks);
                if (source == null)
                {
                    if (root.rollingSummaryBlock != null && !root.rollingSummaryBlock.playerEdited)
                    {
                        RemoveEmergencyContribution(root.rollingSummaryBlock.summaryPayload);
                        result.emergencyAtomsRemoved++;
                        RemoveEmptySummaries(root);
                        continue;
                    }
                    result.protectedSaturation = true;
                    return;
                }
                MemoryReducerBlock rolling = EnsureRolling(root);
                Fold(rolling, source);
                ClearClosedSummaryReference(root, source.recordId);
                root.visibleBlocks.Remove(source);
                result.summarizedBlocks++;
            }
        }

        private static MemoryReducerBlock CreateSummary(
            MemoryReducerRoot root,
            MemoryReducerChapter chapter)
        {
            MemoryRootIdentity identity = Identity(root);
            string recordId;
            string sourceId;
            if (chapter == null)
            {
                MemoryIdentityCodec.TryCreateRollingSummaryId(identity, out recordId);
                MemoryIdentityCodec.TryCreateRollingSummarySourceId(identity, out sourceId);
            }
            else
            {
                MemoryIdentityCodec.TryCreateClosedSummaryId(identity, chapter.ordinal, out recordId);
                MemoryIdentityCodec.TryCreateClosedSummarySourceId(identity, chapter.ordinal, out sourceId);
            }
            return new MemoryReducerBlock
            {
                recordId = recordId,
                sourceOccurrenceId = sourceId,
                captureRuleId = "memory_summary_reducer_v1",
                factDiscriminator = chapter == null
                    ? MemoryContractTokens.SummaryRoleRolling : MemoryContractTokens.SummaryRoleClosed,
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                kind = MemoryContractTokens.KindSummary,
                summaryRole = chapter == null
                    ? MemoryContractTokens.SummaryRoleRolling : MemoryContractTokens.SummaryRoleClosed,
                rootId = root.rootId,
                chapterId = chapter == null ? string.Empty : chapter.chapterId,
                summaryPayload = new MemoryReducerSummary()
            };
        }

        private static MemoryReducerBlock EnsureRolling(MemoryReducerRoot root)
        {
            if (root.rollingSummaryBlock == null) root.rollingSummaryBlock = CreateSummary(root, null);
            return root.rollingSummaryBlock;
        }

        private static void Fold(MemoryReducerBlock target, MemoryReducerBlock source)
        {
            if (TotalContributions(target.summaryPayload) == 0)
            {
                // A Summary block has one stable ordering tick. Closed and rolling creation both
                // consume sources oldest-first, so this is the earliest/first absorbed source and
                // later contribution churn never rewrites it (§T7.4).
                target.originalEventTick = source.originalEventTick;
                target.ageUnknown = source.ageUnknown;
            }
            target.suppressed |= source.suppressed; // whole-Summary suppression taint
            if (source.kind == MemoryContractTokens.KindSummary)
            {
                UnionIds(target.summaryPayload.availableSubjectRefIds,
                    source.summaryPayload.availableSubjectRefIds);
                UnionIds(target.summaryPayload.availableProvenanceRefIds,
                    source.summaryPayload.availableProvenanceRefIds);
                for (int i = 0; i < source.summaryPayload.factBuckets.Count; i++)
                {
                    MemoryReducerBucket sourceBucket = source.summaryPayload.factBuckets[i];
                    MemoryReducerBucket targetBucket = FindOrAddBucket(target.summaryPayload, sourceBucket);
                    for (int j = 0; j < sourceBucket.contributions.Count; j++)
                        targetBucket.contributions.Add(sourceBucket.contributions[j].Clone());
                }
                return;
            }
            for (int i = 0; i < source.facts.Count; i++)
            {
                MemoryReducerFact fact = source.facts[i];
                UnionIds(target.summaryPayload.availableSubjectRefIds, fact.subjectRefIds);
                UnionIds(target.summaryPayload.availableProvenanceRefIds, fact.provenanceRefIds);
                MemoryReducerBucket bucket = FindOrAddBucket(target.summaryPayload, fact);
                string contributionId;
                MemoryIdentityCodec.TryCreateContributionId(
                    source.recordId, i, fact.factId, out contributionId);
                bucket.contributions.Add(new MemoryReducerContribution
                {
                    contributionId = contributionId,
                    originChapterId = source.chapterId,
                    originRecordId = source.recordId,
                    originFactOrdinal = i,
                    originFactId = fact.factId,
                    originalEventTick = source.originalEventTick,
                    ageUnknown = source.ageUnknown,
                    category = source.category,
                    importance = source.importance,
                    canonicalValue = fact.canonicalValue,
                    majorTurningPoint = fact.majorTurningPoint,
                    reversal = fact.reversal,
                    sourceOccurrenceId = source.sourceOccurrenceId,
                    subjectRefIds = new List<string>(fact.subjectRefIds),
                    provenanceRefIds = new List<string>(fact.provenanceRefIds)
                });
            }
        }

        private static MemoryReducerBucket FindOrAddBucket(
            MemoryReducerSummary summary,
            MemoryReducerFact fact)
        {
            string bucketKey;
            TryCreateBucketKey(fact.factKind, fact.canonicalSubjectKind,
                fact.canonicalSubjectId, fact.aggregationToken, out bucketKey);
            for (int i = 0; i < summary.factBuckets.Count; i++)
                if (summary.factBuckets[i].bucketKey == bucketKey) return summary.factBuckets[i];
            MemoryReducerBucket created = new MemoryReducerBucket
            {
                bucketKey = bucketKey,
                factKind = fact.factKind,
                canonicalSubjectKind = fact.canonicalSubjectKind,
                canonicalSubjectId = fact.canonicalSubjectId,
                aggregationToken = fact.aggregationToken
            };
            summary.factBuckets.Add(created);
            return created;
        }

        private static MemoryReducerBucket FindOrAddBucket(
            MemoryReducerSummary summary,
            MemoryReducerBucket source)
        {
            string bucketKey;
            TryCreateBucketKey(source.factKind, source.canonicalSubjectKind,
                source.canonicalSubjectId, source.aggregationToken, out bucketKey);
            for (int i = 0; i < summary.factBuckets.Count; i++)
                if (summary.factBuckets[i].bucketKey == bucketKey) return summary.factBuckets[i];
            MemoryReducerBucket created = source.Clone();
            created.bucketKey = bucketKey;
            created.contributions.Clear();
            summary.factBuckets.Add(created);
            return created;
        }

        private static void DeriveBucket(MemoryReducerBucket bucket)
        {
            bucket.derivedCount = bucket.contributions.Count;
            bucket.earliestSurvivingTick = long.MaxValue;
            bucket.latestSurvivingTick = 0;
            long minimum = long.MaxValue;
            long maximum = long.MinValue;
            bool hasRange = bucket.aggregationToken == MemoryFactContractTokens.Int64Range;
            for (int i = 0; i < bucket.contributions.Count; i++)
            {
                MemoryReducerContribution c = bucket.contributions[i];
                if (!c.ageUnknown)
                {
                    bucket.earliestSurvivingTick = Math.Min(bucket.earliestSurvivingTick, c.originalEventTick);
                    bucket.latestSurvivingTick = Math.Max(bucket.latestSurvivingTick, c.originalEventTick);
                }
                long parsed;
                if (hasRange && MemoryThreadRoutingPolicy.TryParseCanonicalInt64(
                    c.canonicalValue, out parsed))
                {
                    minimum = Math.Min(minimum, parsed);
                    maximum = Math.Max(maximum, parsed);
                }
                else if (hasRange) hasRange = false;
            }
            if (bucket.earliestSurvivingTick == long.MaxValue) bucket.earliestSurvivingTick = 0;
            bucket.derivedRangeMin = hasRange ? minimum.ToString(CultureInfo.InvariantCulture) : string.Empty;
            bucket.derivedRangeMax = hasRange ? maximum.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static bool DeriveSummary(MemoryReducerSummary summary, int wordingCap)
        {
            List<string> keys = new List<string>();
            List<MemorySummaryFingerprintContribution> descriptors =
                new List<MemorySummaryFingerprintContribution>();
            summary.derivedCategoryMask = 0;
            summary.highestSurvivingImportance = string.Empty;
            summary.earliestSurvivingTick = long.MaxValue;
            summary.latestSurvivingTick = 0;
            StringBuilder wording = new StringBuilder();
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                keys.Add(bucket.bucketKey);
                if (wording.Length > 0) wording.Append("; ");
                wording.Append(bucket.factKind).Append('=').Append(
                    RenderBucketProjection(bucket));
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution c = bucket.contributions[j];
                    summary.derivedCategoryMask |= CategoryBit(c.category);
                    if (ImportanceRank(c.importance) > ImportanceRank(summary.highestSurvivingImportance))
                        summary.highestSurvivingImportance = c.importance;
                    if (!c.ageUnknown)
                    {
                        summary.earliestSurvivingTick = Math.Min(
                            summary.earliestSurvivingTick, c.originalEventTick);
                        summary.latestSurvivingTick = Math.Max(
                            summary.latestSurvivingTick, c.originalEventTick);
                    }
                    descriptors.Add(FingerprintDescriptor(c));
                }
            }
            if (summary.earliestSurvivingTick == long.MaxValue) summary.earliestSurvivingTick = 0;
            string fingerprint;
            if (!MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                CurrentReducerRevision, keys, descriptors, out fingerprint)) fingerprint = string.Empty;
            if (fingerprint != summary.canonicalFactsFingerprint)
            {
                if (summary.factsRevision == long.MaxValue) return false;
                summary.factsRevision++;
            }
            summary.canonicalFactsFingerprint = fingerprint;
            summary.reducerRevision = CurrentReducerRevision;
            summary.deterministicWording = TruncateUtf16(writing: wording.ToString(), cap: wordingCap);
            // Optional LLM wording is intentionally disabled throughout M4.
            summary.optionalLlmWording = string.Empty;
            summary.optionalLlmFingerprint = string.Empty;
            return true;
        }

        private static void UnionIds(List<string> target, List<string> source)
        {
            HashSet<string> seen = new HashSet<string>(target, StringComparer.Ordinal);
            for (int i = 0; source != null && i < source.Count; i++)
                if (seen.Add(source[i])) target.Add(source[i]);
            target.Sort(StringComparer.Ordinal);
        }

        private static string RenderBucketProjection(MemoryReducerBucket bucket)
        {
            if (bucket.aggregationToken == MemoryFactContractTokens.Int64Range)
                return bucket.derivedRangeMin + ".." + bucket.derivedRangeMax;
            if (bucket.aggregationToken == MemoryFactContractTokens.LatestState)
            {
                MemoryReducerContribution latest = SelectLatestState(bucket.contributions);
                return latest == null ? string.Empty : latest.canonicalValue;
            }
            if (bucket.aggregationToken == MemoryFactContractTokens.OrdinalSet)
            {
                SortedDictionary<string, int> counts =
                    new SortedDictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < bucket.contributions.Count; i++)
                {
                    string value = bucket.contributions[i].canonicalValue;
                    int count;
                    counts.TryGetValue(value, out count);
                    counts[value] = count + 1;
                }
                StringBuilder rendered = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    if (rendered.Length > 0) rendered.Append(',');
                    rendered.Append(pair.Key);
                    if (pair.Value > 1) rendered.Append('x').Append(
                        pair.Value.ToString(CultureInfo.InvariantCulture));
                }
                return rendered.ToString();
            }
            return bucket.derivedCount.ToString(CultureInfo.InvariantCulture);
        }

        private static MemoryReducerContribution SelectLatestState(
            List<MemoryReducerContribution> contributions)
        {
            MemoryReducerContribution selected = null;
            for (int i = 0; contributions != null && i < contributions.Count; i++)
            {
                MemoryReducerContribution candidate = contributions[i];
                if (selected == null)
                {
                    selected = candidate;
                    continue;
                }
                if (selected.ageUnknown != candidate.ageUnknown)
                {
                    if (selected.ageUnknown) selected = candidate;
                    continue;
                }
                if (!candidate.ageUnknown && candidate.originalEventTick != selected.originalEventTick)
                {
                    if (candidate.originalEventTick > selected.originalEventTick) selected = candidate;
                    continue;
                }
                if (CompareContribution(candidate, selected) < 0) selected = candidate;
            }
            return selected;
        }

        private static MemorySummaryFingerprintContribution FingerprintDescriptor(
            MemoryReducerContribution c)
        {
            return new MemorySummaryFingerprintContribution
            {
                contributionId = c.contributionId,
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
                subjectRefIds = new List<string>(c.subjectRefIds),
                provenanceRefIds = new List<string>(c.provenanceRefIds)
            };
        }

        private sealed class AnchorRoles
        {
            public bool firstEvent;
            public bool latestEvent;
            public bool latestState;
            public bool majorTurningPoint;
            public bool latestReversal;

            public int Count
            {
                get
                {
                    int count = 0;
                    if (firstEvent) count++;
                    if (latestEvent) count++;
                    if (latestState) count++;
                    if (majorTurningPoint) count++;
                    if (latestReversal) count++;
                    return count;
                }
            }
        }

        private sealed class ContributionGroup
        {
            public readonly List<MemoryReducerContribution> members =
                new List<MemoryReducerContribution>();
            public MemoryReducerContribution representative;
            public bool ageUnknown;
            public long tick;
            public bool hasReversal;
        }

        private static void RemoveEmergencyContribution(MemoryReducerSummary summary)
        {
            RemoveEmergencyContribution(summary, null);
        }

        private static void RemoveEmergencyContribution(
            MemoryReducerSummary summary,
            MemoryReducerBucket restrictedBucket)
        {
            Dictionary<MemoryReducerContribution, AnchorRoles> roles = DeriveAnchorRoles(summary);
            MemoryReducerBucket chosenBucket = null;
            MemoryReducerContribution chosen = null;
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                if (restrictedBucket != null && !ReferenceEquals(bucket, restrictedBucket)) continue;
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution candidate = bucket.contributions[j];
                    if (chosen == null || CompareEmergency(candidate, chosen, roles) < 0)
                    {
                        chosen = candidate;
                        chosenBucket = bucket;
                    }
                }
            }
            if (chosenBucket != null) chosenBucket.contributions.Remove(chosen);
        }

        private static int CompareEmergency(
            MemoryReducerContribution left,
            MemoryReducerContribution right,
            Dictionary<MemoryReducerContribution, AnchorRoles> roles)
        {
            int rank = ImportanceRank(left.importance).CompareTo(ImportanceRank(right.importance));
            if (rank != 0) return rank;
            AnchorRoles leftRoles = roles[left];
            AnchorRoles rightRoles = roles[right];
            int roleCount = leftRoles.Count.CompareTo(rightRoles.Count);
            if (roleCount != 0) return roleCount;
            if (leftRoles.Count == 0) return CompareContribution(left, right); // oldest non-anchor first
            int compared = leftRoles.majorTurningPoint.CompareTo(rightRoles.majorTurningPoint);
            if (compared != 0) return compared;
            compared = leftRoles.latestReversal.CompareTo(rightRoles.latestReversal);
            if (compared != 0) return compared;
            compared = leftRoles.latestState.CompareTo(rightRoles.latestState);
            if (compared != 0) return compared;
            compared = leftRoles.latestEvent.CompareTo(rightRoles.latestEvent);
            if (compared != 0) return compared;
            compared = leftRoles.firstEvent.CompareTo(rightRoles.firstEvent);
            if (compared != 0) return compared;
            // At an equal anchor rank, the greatest total-order row is least protected.
            return -CompareContribution(left, right);
        }

        private static Dictionary<MemoryReducerContribution, AnchorRoles> DeriveAnchorRoles(
            MemoryReducerSummary summary)
        {
            Dictionary<MemoryReducerContribution, AnchorRoles> roles =
                new Dictionary<MemoryReducerContribution, AnchorRoles>();
            SortedDictionary<string, ContributionGroup> groups =
                new SortedDictionary<string, ContributionGroup>(StringComparer.Ordinal);
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution contribution = bucket.contributions[j];
                    roles[contribution] = new AnchorRoles
                    {
                        majorTurningPoint = contribution.majorTurningPoint
                    };
                    string groupKey = OrdinalSegmentCodec.Segment(
                            contribution.sourceOccurrenceId ?? string.Empty)
                        + OrdinalSegmentCodec.Segment(contribution.originalEventTick.ToString(
                            CultureInfo.InvariantCulture))
                        + OrdinalSegmentCodec.Segment(contribution.ageUnknown ? "1" : "0");
                    ContributionGroup group;
                    if (!groups.TryGetValue(groupKey, out group))
                    {
                        group = new ContributionGroup
                        {
                            ageUnknown = contribution.ageUnknown,
                            tick = contribution.originalEventTick,
                            representative = contribution
                        };
                        groups.Add(groupKey, group);
                    }
                    group.members.Add(contribution);
                    group.hasReversal |= contribution.reversal;
                    if (CompareContribution(contribution, group.representative) < 0)
                        group.representative = contribution;
                }
            }

            ContributionGroup first = null;
            ContributionGroup latest = null;
            ContributionGroup latestReversal = null;
            bool hasKnownReversal = false;
            foreach (KeyValuePair<string, ContributionGroup> pair in groups)
            {
                ContributionGroup group = pair.Value;
                if (!group.ageUnknown)
                {
                    if (first == null || group.tick < first.tick
                        || (group.tick == first.tick && CompareContribution(
                            group.representative, first.representative) < 0)) first = group;
                    if (latest == null || group.tick > latest.tick
                        || (group.tick == latest.tick && CompareContribution(
                            group.representative, latest.representative) < 0)) latest = group;
                }
                if (!group.hasReversal) continue;
                if (!group.ageUnknown)
                {
                    if (!hasKnownReversal || group.tick > latestReversal.tick
                        || (group.tick == latestReversal.tick && CompareContribution(
                            group.representative, latestReversal.representative) < 0))
                        latestReversal = group;
                    hasKnownReversal = true;
                }
                else if (!hasKnownReversal && (latestReversal == null || CompareContribution(
                    group.representative, latestReversal.representative) < 0))
                {
                    latestReversal = group;
                }
            }
            ApplyGroupRole(first, roles, 0);
            ApplyGroupRole(latest, roles, 1);
            ApplyGroupRole(latestReversal, roles, 2);

            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                if (bucket.aggregationToken != MemoryFactContractTokens.LatestState) continue;
                MemoryReducerContribution selected = SelectLatestState(bucket.contributions);
                if (selected != null) roles[selected].latestState = true;
            }
            return roles;
        }

        private static void ApplyGroupRole(
            ContributionGroup group,
            Dictionary<MemoryReducerContribution, AnchorRoles> roles,
            int role)
        {
            if (group == null) return;
            for (int i = 0; i < group.members.Count; i++)
            {
                AnchorRoles target = roles[group.members[i]];
                if (role == 0) target.firstEvent = true;
                else if (role == 1) target.latestEvent = true;
                else target.latestReversal = true;
            }
        }

        private static bool ReferenceDescriptorCapsHold(
            MemoryReducerRoot root,
            MemoryReducerPolicy policy)
        {
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (!BlockReferenceDescriptorCapsHold(root.visibleBlocks[i], policy)) return false;
            return root.rollingSummaryBlock == null
                || BlockReferenceDescriptorCapsHold(root.rollingSummaryBlock, policy);
        }

        private static bool BlockReferenceDescriptorCapsHold(
            MemoryReducerBlock block,
            MemoryReducerPolicy policy)
        {
            if (block.kind != MemoryContractTokens.KindSummary)
            {
                for (int i = 0; i < block.facts.Count; i++)
                    if (block.facts[i].subjectRefIds.Count
                            > policy.maximumSubjectRefsPerContribution
                        || block.facts[i].provenanceRefIds.Count
                            > policy.maximumProvenanceRefsPerContribution) return false;
                return true;
            }
            for (int i = 0; i < block.summaryPayload.factBuckets.Count; i++)
                for (int j = 0;
                    j < block.summaryPayload.factBuckets[i].contributions.Count; j++)
                {
                    MemoryReducerContribution contribution =
                        block.summaryPayload.factBuckets[i].contributions[j];
                    if (contribution.subjectRefIds.Count
                            > policy.maximumSubjectRefsPerContribution
                        || contribution.provenanceRefIds.Count
                            > policy.maximumProvenanceRefsPerContribution) return false;
                }
            return true;
        }

        private static bool SummaryAggregateReferenceBoundsHold(
            MemoryReducerSummary summary,
            MemoryReducerPolicy policy)
        {
            HashSet<string> subjects = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> provenance = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                subjects.Add("bucket:" + OrdinalSegmentCodec.Segment(bucket.canonicalSubjectKind)
                    + OrdinalSegmentCodec.Segment(bucket.canonicalSubjectId));
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution contribution = bucket.contributions[j];
                    for (int k = 0; k < contribution.subjectRefIds.Count; k++)
                        subjects.Add("ref:" + contribution.subjectRefIds[k]);
                    for (int k = 0; k < contribution.provenanceRefIds.Count; k++)
                        provenance.Add(contribution.provenanceRefIds[k]);
                }
            }
            return subjects.Count <= policy.maximumDistinctSubjects
                && provenance.Count <= policy.maximumProvenanceTotal;
        }

        private static void RemoveEmptyBuckets(MemoryReducerSummary summary)
        {
            for (int i = summary.factBuckets.Count - 1; i >= 0; i--)
                if (summary.factBuckets[i].contributions.Count == 0)
                    summary.factBuckets.RemoveAt(i);
        }

        private static long EffectiveTick(MemoryReducerContribution value)
        {
            return value.ageUnknown ? long.MaxValue : value.originalEventTick;
        }

        private static void RemoveEmptySummaries(MemoryReducerRoot root)
        {
            for (int i = root.visibleBlocks.Count - 1; i >= 0; i--)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block.kind == MemoryContractTokens.KindSummary && !block.playerEdited
                    && TotalContributions(block.summaryPayload) == 0)
                {
                    ClearClosedSummaryReference(root, block.recordId);
                    root.visibleBlocks.RemoveAt(i);
                }
            }
            if (root.rollingSummaryBlock != null && !root.rollingSummaryBlock.playerEdited
                && TotalContributions(root.rollingSummaryBlock.summaryPayload) == 0)
                root.rollingSummaryBlock = null;
            RemoveUnreferencedClosedChapters(root);
        }

        private static void RemoveUnreferencedClosedChapters(MemoryReducerRoot root)
        {
            HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < root.visibleBlocks.Count; i++)
            {
                MemoryReducerBlock block = root.visibleBlocks[i];
                if (block == null) continue;
                if (!string.IsNullOrEmpty(block.chapterId)) referenced.Add(block.chapterId);
                AddContributionChapterReferences(block.summaryPayload, referenced);
            }
            AddContributionChapterReferences(root.rollingSummaryBlock?.summaryPayload, referenced);
            for (int i = root.chapters.Count - 1; i >= 0; i--)
            {
                MemoryReducerChapter chapter = root.chapters[i];
                if (chapter != null && chapter.closed
                    && string.IsNullOrEmpty(chapter.closedSummaryRecordId)
                    && !referenced.Contains(chapter.chapterId)) root.chapters.RemoveAt(i);
            }
        }

        private static void AddContributionChapterReferences(
            MemoryReducerSummary summary,
            HashSet<string> referenced)
        {
            for (int i = 0; summary?.factBuckets != null && i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                for (int j = 0; bucket?.contributions != null
                    && j < bucket.contributions.Count; j++)
                {
                    string chapterId = bucket.contributions[j]?.originChapterId;
                    if (!string.IsNullOrEmpty(chapterId)) referenced.Add(chapterId);
                }
            }
        }

        private static void ClearClosedSummaryReference(MemoryReducerRoot root, string recordId)
        {
            for (int i = 0; i < root.chapters.Count; i++)
                if (root.chapters[i].closedSummaryRecordId == recordId)
                    root.chapters[i].closedSummaryRecordId = string.Empty;
        }

        private static void DeduplicateContributions(List<MemoryReducerContribution> values)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!seen.Add(values[i].contributionId))
                {
                    values.RemoveAt(i);
                    i--;
                }
        }

        private static bool UniqueNonblank(List<string> values)
        {
            if (values == null) return false;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (string.IsNullOrEmpty(values[i]) || !seen.Add(values[i])) return false;
            return true;
        }

        /// <summary>Builds the exact semantic bucket tuple; occurrence/rule identity is excluded.</summary>
        private static bool TryCreateBucketKey(
            string factKind,
            string subjectKind,
            string subjectId,
            string aggregationToken,
            out string bucketKey)
        {
            bucketKey = string.Empty;
            if (string.IsNullOrWhiteSpace(factKind) || string.IsNullOrWhiteSpace(subjectKind)
                || string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(aggregationToken)
                || factKind.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || subjectKind.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || subjectId.Length > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                || aggregationToken.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || !MemoryIdentityCodec.IsWellFormedUtf16(factKind)
                || !MemoryIdentityCodec.IsWellFormedUtf16(subjectKind)
                || !MemoryIdentityCodec.IsWellFormedUtf16(subjectId)
                || !MemoryIdentityCodec.IsWellFormedUtf16(aggregationToken)) return false;
            bucketKey = OrdinalSegmentCodec.Segment(factKind)
                + OrdinalSegmentCodec.Segment(subjectKind)
                + OrdinalSegmentCodec.Segment(subjectId)
                + OrdinalSegmentCodec.Segment(aggregationToken);
            return bucketKey.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters;
        }

        private static bool IsCanonicalValue(
            string aggregationToken,
            string valueKind,
            string value)
        {
            return MemoryFactContractTokens.IsMatchingPair(aggregationToken, valueKind)
                && IsCanonicalValueForAggregation(aggregationToken, value);
        }

        private static bool IsCanonicalValueForAggregation(string aggregationToken, string value)
        {
            string safe = value ?? string.Empty;
            if (safe.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || !MemoryIdentityCodec.IsWellFormedUtf16(safe)) return false;
            if (aggregationToken == MemoryFactContractTokens.CountOccurrences)
                return safe.Length == 0;
            if (aggregationToken == MemoryFactContractTokens.OrdinalSet
                || aggregationToken == MemoryFactContractTokens.LatestState)
                return !string.IsNullOrWhiteSpace(safe);
            if (aggregationToken == MemoryFactContractTokens.Int64Range)
            {
                long parsed;
                return MemoryThreadRoutingPolicy.TryParseCanonicalInt64(safe, out parsed);
            }
            return false;
        }

        private static int CountAllBlocks(MemoryReducerRoot root)
        {
            return root.visibleBlocks.Count + (root.rollingSummaryBlock == null ? 0 : 1);
        }

        private static int CountEdited(MemoryReducerRoot root)
        {
            int count = 0;
            for (int i = 0; i < root.visibleBlocks.Count; i++)
                if (root.visibleBlocks[i].playerEdited) count++;
            return count;
        }

        private static int CountUnedited(MemoryReducerRoot root)
        {
            return root.visibleBlocks.Count - CountEdited(root);
        }

        private static MemoryReducerBlock OldestUnedited(List<MemoryReducerBlock> blocks)
        {
            MemoryReducerBlock selected = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                MemoryReducerBlock candidate = blocks[i];
                if (candidate.playerEdited) continue;
                if (selected == null || CompareBlock(candidate, selected) < 0) selected = candidate;
            }
            return selected;
        }

        private static int TotalContributions(MemoryReducerSummary summary)
        {
            if (summary == null || summary.factBuckets == null) return 0;
            int total = 0;
            for (int i = 0; i < summary.factBuckets.Count; i++)
                total += summary.factBuckets[i].contributions.Count;
            return total;
        }

        private static int CompareChapter(MemoryReducerChapter left, MemoryReducerChapter right)
        {
            int ordinal = left.ordinal.CompareTo(right.ordinal);
            return ordinal != 0 ? ordinal : string.Compare(
                left.chapterId, right.chapterId, StringComparison.Ordinal);
        }

        private static int CompareBlock(MemoryReducerBlock left, MemoryReducerBlock right)
        {
            long leftTick = left.ageUnknown ? long.MaxValue : left.originalEventTick;
            long rightTick = right.ageUnknown ? long.MaxValue : right.originalEventTick;
            int tick = leftTick.CompareTo(rightTick);
            if (tick != 0) return tick;
            int compared = string.Compare(left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal);
            if (compared != 0) return compared;
            compared = string.Compare(left.rootId, right.rootId, StringComparison.Ordinal);
            if (compared != 0) return compared;
            compared = string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
            if (compared != 0) return compared;
            return string.Compare(BlockTuple(left), BlockTuple(right), StringComparison.Ordinal);
        }

        private static int CompareBucket(MemoryReducerBucket left, MemoryReducerBucket right)
        {
            return string.Compare(left.bucketKey, right.bucketKey, StringComparison.Ordinal);
        }

        private static int CompareContribution(
            MemoryReducerContribution left,
            MemoryReducerContribution right)
        {
            int tick = EffectiveTick(left).CompareTo(EffectiveTick(right));
            if (tick != 0) return tick;
            int compared = string.Compare(
                left.contributionId, right.contributionId, StringComparison.Ordinal);
            return compared != 0 ? compared : string.Compare(
                ContributionTuple(left), ContributionTuple(right), StringComparison.Ordinal);
        }

        private static string BlockTuple(MemoryReducerBlock block)
        {
            StringBuilder builder = new StringBuilder();
            AddBlock(builder, block);
            return builder.ToString();
        }

        private static string ContributionTuple(MemoryReducerContribution contribution)
        {
            StringBuilder builder = new StringBuilder();
            Add(builder, contribution.originChapterId); Add(builder, contribution.originRecordId);
            Add(builder, contribution.originFactOrdinal); Add(builder, contribution.originFactId);
            Add(builder, contribution.originalEventTick); Add(builder, contribution.ageUnknown);
            Add(builder, contribution.category); Add(builder, contribution.importance);
            Add(builder, contribution.canonicalValue); Add(builder, contribution.majorTurningPoint);
            Add(builder, contribution.reversal); Add(builder, contribution.sourceOccurrenceId);
            AddStrings(builder, contribution.subjectRefIds);
            AddStrings(builder, contribution.provenanceRefIds);
            return builder.ToString();
        }

        private static int ImportanceRank(string importance)
        {
            if (importance == MemoryContractTokens.ImportanceImportant) return 3;
            if (importance == MemoryContractTokens.ImportanceRegular) return 2;
            if (importance == MemoryContractTokens.ImportanceMinor) return 1;
            return 0;
        }

        private static int CategoryBit(string category)
        {
            if (category == MemoryContractTokens.CategoryPersonal) return 1 << 0;
            if (category == MemoryContractTokens.CategoryRelationships) return 1 << 1;
            if (category == MemoryContractTokens.CategoryFamily) return 1 << 2;
            if (category == MemoryContractTokens.CategoryFactions) return 1 << 3;
            return 0;
        }

        private static MemoryRootIdentity Identity(MemoryReducerRoot root)
        {
            return new MemoryRootIdentity
            {
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                primarySubjectKind = root.subjectKind,
                primarySubjectId = root.subjectId
            };
        }

        private static string TruncateUtf16(string writing, int cap)
        {
            string value = writing ?? string.Empty;
            if (value.Length <= cap) return value;
            int length = cap;
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, Math.Max(0, length));
        }

        private static void Add(StringBuilder builder, object value)
        {
            builder.Append(OrdinalSegmentCodec.Segment(Convert.ToString(
                value, CultureInfo.InvariantCulture) ?? string.Empty));
        }

        private static void AddBlock(StringBuilder builder, MemoryReducerBlock block)
        {
            if (block == null) { Add(builder, "null"); return; }
            Add(builder, block.recordId); Add(builder, block.sourceOccurrenceId);
            Add(builder, block.captureRuleId); Add(builder, block.factDiscriminator);
            Add(builder, block.ownerPawnId); Add(builder, block.ownerEpochToken);
            Add(builder, block.rootId); Add(builder, block.kind); Add(builder, block.summaryRole);
            Add(builder, block.category); Add(builder, block.importance); Add(builder, block.originalEventTick);
            Add(builder, block.ageUnknown); Add(builder, block.chapterId); Add(builder, block.playerEdited);
            Add(builder, block.suppressed); Add(builder, block.requiredLifecycleLandmark);
            Add(builder, block.automaticWording); Add(builder, block.playerWording);
            if (block.summaryPayload == null)
            {
                Add(builder, block.facts.Count);
                for (int i = 0; i < block.facts.Count; i++)
                {
                    MemoryReducerFact fact = block.facts[i];
                    Add(builder, fact.factId); Add(builder, fact.factKind);
                    Add(builder, fact.canonicalSubjectKind); Add(builder, fact.canonicalSubjectId);
                    Add(builder, fact.aggregationToken); Add(builder, fact.canonicalValueKind);
                    Add(builder, fact.canonicalValue); Add(builder, fact.majorTurningPoint);
                    Add(builder, fact.reversal); AddStrings(builder, fact.subjectRefIds);
                    AddStrings(builder, fact.provenanceRefIds);
                }
                return;
            }
            MemoryReducerSummary summary = block.summaryPayload;
            Add(builder, summary.reducerRevision); Add(builder, summary.factsRevision);
            Add(builder, summary.canonicalFactsFingerprint); Add(builder, summary.derivedCategoryMask);
            Add(builder, summary.highestSurvivingImportance); Add(builder, summary.earliestSurvivingTick);
            Add(builder, summary.latestSurvivingTick); Add(builder, summary.deterministicWording);
            Add(builder, summary.optionalLlmWording); Add(builder, summary.optionalLlmFingerprint);
            AddStrings(builder, summary.availableSubjectRefIds);
            AddStrings(builder, summary.availableProvenanceRefIds);
            Add(builder, summary.factBuckets.Count);
            for (int i = 0; i < summary.factBuckets.Count; i++)
            {
                MemoryReducerBucket bucket = summary.factBuckets[i];
                Add(builder, bucket.bucketKey); Add(builder, bucket.factKind);
                Add(builder, bucket.canonicalSubjectKind); Add(builder, bucket.canonicalSubjectId);
                Add(builder, bucket.aggregationToken); Add(builder, bucket.derivedCount);
                Add(builder, bucket.derivedRangeMin); Add(builder, bucket.derivedRangeMax);
                Add(builder, bucket.earliestSurvivingTick); Add(builder, bucket.latestSurvivingTick);
                Add(builder, bucket.contributions.Count);
                for (int j = 0; j < bucket.contributions.Count; j++)
                {
                    MemoryReducerContribution c = bucket.contributions[j];
                    Add(builder, c.contributionId); Add(builder, c.originChapterId);
                    Add(builder, c.originRecordId); Add(builder, c.originFactOrdinal);
                    Add(builder, c.originFactId); Add(builder, c.originalEventTick);
                    Add(builder, c.ageUnknown); Add(builder, c.category); Add(builder, c.importance);
                    Add(builder, c.canonicalValue); Add(builder, c.majorTurningPoint);
                    Add(builder, c.reversal); Add(builder, c.sourceOccurrenceId);
                    AddStrings(builder, c.subjectRefIds); AddStrings(builder, c.provenanceRefIds);
                }
            }
        }

        private static void AddStrings(StringBuilder builder, List<string> values)
        {
            Add(builder, values == null ? -1 : values.Count);
            for (int i = 0; values != null && i < values.Count; i++) Add(builder, values[i]);
        }
    }
}
