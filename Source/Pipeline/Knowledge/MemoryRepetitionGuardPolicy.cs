// MemoryRepetitionGuardPolicy.cs — pure Recall v2 cooldown and reservation policy.
//
// Saved blocks own record-level use state, while SavedMemoryRepetitionGuardRow owns root, subject,
// pair, and novelty state. Game adapters detach both shapes into the snapshots below before calling
// this policy. The policy never reads settings, Scribe rows, Pawns, Verse time, or request state.
// It returns the exact canonical guard identities that M2 freezes into a receipt plan; only M2's
// already-hardened narrative-winner receipt is allowed to advance those guards.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable guard-kind tokens shared by recall selection and M2 receipt plans.</summary>
    internal static class MemoryRepetitionGuardKinds
    {
        public const string Record = "record";
        public const string Root = "root";
        public const string Subject = "subject";
        public const string Pair = "pair";
        public const string Novelty = "novelty";

        /// <summary>True only for one saved/frozen repetition-guard kind.</summary>
        public static bool IsKnown(string value)
        {
            return value == Record || value == Root || value == Subject
                || value == Pair || value == Novelty;
        }

        /// <summary>True only for a non-record kind stored in SavedMemoryRepetitionGuardRow.</summary>
        public static bool IsSavedRowKind(string value)
        {
            return value == Root || value == Subject || value == Pair || value == Novelty;
        }
    }

    /// <summary>Stable, bounded failure tokens for selector diagnostics.</summary>
    internal static class MemoryRepetitionRejectReasons
    {
        public const string InvalidPolicy = "repetition_invalid_policy";
        public const string InvalidGuard = "repetition_invalid_guard";
        public const string EpochMismatch = "repetition_epoch_mismatch";
        public const string DuplicateGuard = "repetition_duplicate_guard";
        public const string Reserved = "repetition_reserved";
        public const string TimeDistance = "repetition_time_distance";
        public const string EntryDistance = "repetition_entry_distance";
        public const string Saturated = "repetition_saturated";
    }

    /// <summary>
    /// Detached repetition inputs. The two player controls are combined with adapter-supplied,
    /// XML-owned safety minima; ticks-per-day is also supplied so pure code never imports Verse.
    /// </summary>
    internal sealed class MemoryRepetitionPolicySnapshot
    {
        public long currentTick;
        public long completedDiaryEntryOrdinal;
        public int ticksPerDay;
        public int memoryReuseDays;
        public int memoryRevisitEntryCount;
        public int recordMinimumReuseDays;
        public int recordMinimumEntryDistance;
        public int rootMinimumEntryDistance;
        public int subjectMinimumEntryDistance;
        public int pairMinimumEntryDistance;
        public int noveltyMinimumEntryDistance;
    }

    /// <summary>
    /// Detached current state of either one block-owned record guard or one saved non-record row.
    /// A reservation is an absolute exclusion: selection never guesses whether the request will win.
    /// </summary>
    internal sealed class MemoryRepetitionGuardState
    {
        public string ownerEpochToken = string.Empty;
        public string guardKind = string.Empty;
        public string guardKey = string.Empty;
        public long lastAutomaticIncludedTick;
        public long lastAutomaticIncludedEntryOrdinal;
        public long automaticInclusionCount;
        public bool reserved;
    }

    /// <summary>Pure all-or-nothing guard verdict plus canonical receipt-plan identities.</summary>
    internal sealed class MemoryRepetitionGuardEvaluation
    {
        public bool passes;
        public string rejectReason = string.Empty;
        public List<MemoryGuardIdentity> guardEntries = new List<MemoryGuardIdentity>();
    }

    /// <summary>Evaluates and constructs Recall v2's record/root/subject/pair/novelty guards.</summary>
    internal static class MemoryRepetitionGuardPolicy
    {
        private const string RecordKeyDomain = "memory-record-guard-v1";
        private const string RootKeyDomain = "memory-root-guard-v1";
        private const string SubjectKeyDomain = "memory-subject-guard-v1";
        private const string PairKeyDomain = "memory-pair-guard-v1";
        private const string NoveltyKeyDomain = "memory-novelty-guard-v1";
        private const int MaximumReuseDays = 35791;
        private const int MaximumEntryDistance = 1000000;

        /// <summary>
        /// Requires the record time/page guards and every supplied structural guard to pass. Invalid,
        /// future, overflowing, duplicate, or reserved state fails closed and returns no guard entries.
        /// </summary>
        public static MemoryRepetitionGuardEvaluation Evaluate(
            string ownerEpochToken,
            MemoryRepetitionGuardState recordGuard,
            List<MemoryRepetitionGuardState> structuralGuards,
            MemoryRepetitionPolicySnapshot policy)
        {
            MemoryRepetitionGuardEvaluation result = new MemoryRepetitionGuardEvaluation();
            if (!TryNormalizePolicy(policy))
            {
                result.rejectReason = MemoryRepetitionRejectReasons.InvalidPolicy;
                return result;
            }
            if (policy.completedDiaryEntryOrdinal == long.MaxValue)
            {
                // The one-based owner ordinal never wraps or rebases. At saturation a successful
                // memory-bearing page could not preserve the configured entry distance, so recall
                // fails closed while ordinary memory-free diary writing may continue.
                result.rejectReason = MemoryRepetitionRejectReasons.Saturated;
                return result;
            }

            if (!IsValidGuard(recordGuard, MemoryRepetitionGuardKinds.Record)
                || !string.Equals(
                    ownerEpochToken ?? string.Empty,
                    recordGuard.ownerEpochToken ?? string.Empty,
                    StringComparison.Ordinal))
            {
                result.rejectReason = IsValidGuard(recordGuard, MemoryRepetitionGuardKinds.Record)
                    ? MemoryRepetitionRejectReasons.EpochMismatch
                    : MemoryRepetitionRejectReasons.InvalidGuard;
                return result;
            }

            string failure = EvaluateState(
                recordGuard,
                policy,
                appliesTimeDistance: true,
                requiredEntryDistance: Math.Max(
                    policy.memoryRevisitEntryCount,
                    policy.recordMinimumEntryDistance));
            if (failure.Length > 0)
            {
                result.rejectReason = failure;
                return result;
            }

            List<MemoryRepetitionGuardState> ordered = new List<MemoryRepetitionGuardState>();
            if (structuralGuards != null)
            {
                for (int index = 0; index < structuralGuards.Count; index++)
                {
                    MemoryRepetitionGuardState guard = structuralGuards[index];
                    if (!IsValidGuard(guard, null)
                        || !MemoryRepetitionGuardKinds.IsSavedRowKind(guard.guardKind))
                    {
                        result.rejectReason = MemoryRepetitionRejectReasons.InvalidGuard;
                        return result;
                    }

                    if (!string.Equals(
                        ownerEpochToken ?? string.Empty,
                        guard.ownerEpochToken ?? string.Empty,
                        StringComparison.Ordinal))
                    {
                        result.rejectReason = MemoryRepetitionRejectReasons.EpochMismatch;
                        return result;
                    }

                    ordered.Add(guard);
                }
            }

            ordered.Sort(CompareStates);
            for (int index = 1; index < ordered.Count; index++)
            {
                if (SameIdentity(ordered[index - 1], ordered[index]))
                {
                    result.rejectReason = MemoryRepetitionRejectReasons.DuplicateGuard;
                    return result;
                }
            }

            for (int index = 0; index < ordered.Count; index++)
            {
                MemoryRepetitionGuardState guard = ordered[index];
                failure = EvaluateState(
                    guard,
                    policy,
                    appliesTimeDistance: false,
                    requiredEntryDistance: RequiredEntryDistance(guard.guardKind, policy));
                if (failure.Length > 0)
                {
                    result.rejectReason = failure;
                    return result;
                }
            }

            result.guardEntries.Add(ToIdentity(recordGuard));
            for (int index = 0; index < ordered.Count; index++)
            {
                result.guardEntries.Add(ToIdentity(ordered[index]));
            }
            result.guardEntries.Sort(CompareIdentities);
            result.passes = true;
            return result;
        }

        /// <summary>Builds the canonical record guard key, or empty for invalid identity.</summary>
        public static string RecordKey(string recordId)
        {
            return OneCompositeIdentityKey(RecordKeyDomain, recordId);
        }

        /// <summary>Builds the canonical root guard key, or empty for invalid identity.</summary>
        public static string RootKey(string rootId)
        {
            return OneCompositeIdentityKey(RootKeyDomain, rootId);
        }

        /// <summary>Builds one canonical typed-subject guard key.</summary>
        public static string SubjectKey(string subjectKind, string subjectId)
        {
            if (!ValidRawIdentity(subjectKind)
                || !ValidCompositeIdentity(subjectId)
                || !MemoryContractTokens.IsValidRootSubject(subjectKind, subjectId))
            {
                return string.Empty;
            }
            return BoundedCompositeKey(OrdinalSegmentCodec.Segment(SubjectKeyDomain)
                + OrdinalSegmentCodec.Segment(subjectKind)
                + OrdinalSegmentCodec.Segment(subjectId));
        }

        /// <summary>
        /// Builds one unordered exact pawn-pair key. Endpoint order cannot create two cooldown rows.
        /// </summary>
        public static string PairKey(string firstPawnId, string secondPawnId)
        {
            if (!ValidRawIdentity(firstPawnId) || !ValidRawIdentity(secondPawnId)
                || string.Equals(firstPawnId, secondPawnId, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string first = firstPawnId;
            string second = secondPawnId;
            if (string.CompareOrdinal(first, second) > 0)
            {
                string swap = first;
                first = second;
                second = swap;
            }

            return BoundedCompositeKey(OrdinalSegmentCodec.Segment(PairKeyDomain)
                + OrdinalSegmentCodec.Segment(first)
                + OrdinalSegmentCodec.Segment(second));
        }

        /// <summary>Builds one root-local novelty key for a chapter/projection identity.</summary>
        public static string NoveltyKey(string rootId, string noveltyId)
        {
            if (!ValidCompositeIdentity(rootId) || !ValidCompositeIdentity(noveltyId))
                return string.Empty;
            return BoundedCompositeKey(OrdinalSegmentCodec.Segment(NoveltyKeyDomain)
                + OrdinalSegmentCodec.Segment(rootId)
                + OrdinalSegmentCodec.Segment(noveltyId));
        }

        /// <summary>
        /// True only when a guard key has the exact canonical domain, segment count, bounds, and
        /// normalized endpoint ordering for its declared kind.
        /// </summary>
        public static bool IsCanonicalIdentity(string guardKind, string guardKey)
        {
            if (!MemoryRepetitionGuardKinds.IsKnown(guardKind)
                || !ValidCompositeIdentity(guardKey)) return false;

            int offset = 0;
            string domain;
            if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                    guardKey,
                    ref offset,
                    MemoryIdentityCodec.MaximumRawIdentityCharacters,
                    allowEmpty: false,
                    out domain))
            {
                return false;
            }

            string first;
            string second;
            switch (guardKind)
            {
                case MemoryRepetitionGuardKinds.Record:
                    return domain == RecordKeyDomain
                        && ReadComposite(guardKey, ref offset, out first)
                        && offset == guardKey.Length
                        && string.Equals(guardKey, RecordKey(first), StringComparison.Ordinal);
                case MemoryRepetitionGuardKinds.Root:
                    return domain == RootKeyDomain
                        && ReadComposite(guardKey, ref offset, out first)
                        && offset == guardKey.Length
                        && string.Equals(guardKey, RootKey(first), StringComparison.Ordinal);
                case MemoryRepetitionGuardKinds.Subject:
                    return domain == SubjectKeyDomain
                        && ReadRaw(guardKey, ref offset, out first)
                        && ReadComposite(guardKey, ref offset, out second)
                        && offset == guardKey.Length
                        && string.Equals(
                            guardKey,
                            SubjectKey(first, second),
                            StringComparison.Ordinal);
                case MemoryRepetitionGuardKinds.Pair:
                    return domain == PairKeyDomain
                        && ReadRaw(guardKey, ref offset, out first)
                        && ReadRaw(guardKey, ref offset, out second)
                        && offset == guardKey.Length
                        && string.Equals(guardKey, PairKey(first, second), StringComparison.Ordinal);
                default:
                    return domain == NoveltyKeyDomain
                        && ReadComposite(guardKey, ref offset, out first)
                        && ReadComposite(guardKey, ref offset, out second)
                        && offset == guardKey.Length
                        && string.Equals(
                            guardKey,
                            NoveltyKey(first, second),
                            StringComparison.Ordinal);
            }
        }

        private static string EvaluateState(
            MemoryRepetitionGuardState guard,
            MemoryRepetitionPolicySnapshot policy,
            bool appliesTimeDistance,
            int requiredEntryDistance)
        {
            if (guard.reserved) return MemoryRepetitionRejectReasons.Reserved;
            if (guard.automaticInclusionCount < 0
                || guard.automaticInclusionCount == long.MaxValue)
            {
                return MemoryRepetitionRejectReasons.Saturated;
            }

            if (!IsCoherentUseState(guard))
                return MemoryRepetitionRejectReasons.InvalidGuard;

            if (appliesTimeDistance && guard.lastAutomaticIncludedTick != 0)
            {
                int days = Math.Max(policy.memoryReuseDays, policy.recordMinimumReuseDays);
                long requiredTicks;
                try
                {
                    requiredTicks = checked((long)days * policy.ticksPerDay);
                }
                catch (OverflowException)
                {
                    return MemoryRepetitionRejectReasons.InvalidPolicy;
                }

                if (guard.lastAutomaticIncludedTick > policy.currentTick
                    || policy.currentTick - guard.lastAutomaticIncludedTick < requiredTicks)
                {
                    return MemoryRepetitionRejectReasons.TimeDistance;
                }
            }

            if (guard.lastAutomaticIncludedEntryOrdinal != 0)
            {
                if (guard.lastAutomaticIncludedEntryOrdinal > policy.completedDiaryEntryOrdinal
                    || policy.completedDiaryEntryOrdinal
                        - guard.lastAutomaticIncludedEntryOrdinal < requiredEntryDistance)
                {
                    return MemoryRepetitionRejectReasons.EntryDistance;
                }
            }

            return string.Empty;
        }

        private static bool TryNormalizePolicy(MemoryRepetitionPolicySnapshot policy)
        {
            return policy != null
                && policy.currentTick >= 0
                && policy.completedDiaryEntryOrdinal > 0
                && policy.ticksPerDay > 0
                && policy.memoryReuseDays > 0
                && policy.memoryReuseDays <= MaximumReuseDays
                && policy.memoryRevisitEntryCount > 0
                && policy.memoryRevisitEntryCount <= MaximumEntryDistance
                && policy.recordMinimumReuseDays >= 0
                && policy.recordMinimumReuseDays <= MaximumReuseDays
                && policy.recordMinimumEntryDistance >= 0
                && policy.recordMinimumEntryDistance <= MaximumEntryDistance
                && policy.rootMinimumEntryDistance >= 0
                && policy.rootMinimumEntryDistance <= MaximumEntryDistance
                && policy.subjectMinimumEntryDistance >= 0
                && policy.subjectMinimumEntryDistance <= MaximumEntryDistance
                && policy.pairMinimumEntryDistance >= 0
                && policy.pairMinimumEntryDistance <= MaximumEntryDistance
                && policy.noveltyMinimumEntryDistance >= 0
                && policy.noveltyMinimumEntryDistance <= MaximumEntryDistance;
        }

        private static bool IsValidGuard(MemoryRepetitionGuardState guard, string expectedKind)
        {
            return guard != null
                && MemoryRepetitionGuardKinds.IsKnown(guard.guardKind)
                && (expectedKind == null
                    || string.Equals(guard.guardKind, expectedKind, StringComparison.Ordinal))
                && ValidCompositeIdentity(guard.ownerEpochToken)
                && IsCanonicalIdentity(guard.guardKind, guard.guardKey);
        }

        private static bool IsCoherentUseState(MemoryRepetitionGuardState guard)
        {
            if (guard.lastAutomaticIncludedTick < 0
                || guard.lastAutomaticIncludedEntryOrdinal < 0)
            {
                return false;
            }

            if (guard.automaticInclusionCount == 0)
            {
                return guard.lastAutomaticIncludedTick == 0
                    && guard.lastAutomaticIncludedEntryOrdinal == 0;
            }

            return guard.lastAutomaticIncludedTick > 0
                && guard.lastAutomaticIncludedEntryOrdinal > 0;
        }

        private static int RequiredEntryDistance(
            string guardKind,
            MemoryRepetitionPolicySnapshot policy)
        {
            int minimum;
            switch (guardKind)
            {
                case MemoryRepetitionGuardKinds.Root:
                    minimum = policy.rootMinimumEntryDistance;
                    break;
                case MemoryRepetitionGuardKinds.Subject:
                    minimum = policy.subjectMinimumEntryDistance;
                    break;
                case MemoryRepetitionGuardKinds.Pair:
                    minimum = policy.pairMinimumEntryDistance;
                    break;
                default:
                    minimum = policy.noveltyMinimumEntryDistance;
                    break;
            }
            return Math.Max(policy.memoryRevisitEntryCount, minimum);
        }

        private static string OneCompositeIdentityKey(string domain, string identity)
        {
            return ValidCompositeIdentity(identity)
                ? BoundedCompositeKey(
                    OrdinalSegmentCodec.Segment(domain)
                    + OrdinalSegmentCodec.Segment(identity))
                : string.Empty;
        }

        private static string BoundedCompositeKey(string value)
        {
            return ValidCompositeIdentity(value) ? value : string.Empty;
        }

        private static bool ValidRawIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ValidCompositeIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ReadRaw(string key, ref int offset, out string value)
        {
            return OrdinalSegmentCodec.TryReadCanonicalSegment(
                key,
                ref offset,
                MemoryIdentityCodec.MaximumRawIdentityCharacters,
                allowEmpty: false,
                out value);
        }

        private static bool ReadComposite(string key, ref int offset, out string value)
        {
            return OrdinalSegmentCodec.TryReadCanonicalSegment(
                key,
                ref offset,
                MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters,
                allowEmpty: false,
                out value);
        }

        private static bool SameIdentity(
            MemoryRepetitionGuardState left,
            MemoryRepetitionGuardState right)
        {
            return string.Equals(left.guardKind, right.guardKind, StringComparison.Ordinal)
                && string.Equals(left.guardKey, right.guardKey, StringComparison.Ordinal);
        }

        private static int CompareStates(
            MemoryRepetitionGuardState left,
            MemoryRepetitionGuardState right)
        {
            int kind = string.CompareOrdinal(left.guardKind, right.guardKind);
            return kind != 0 ? kind : string.CompareOrdinal(left.guardKey, right.guardKey);
        }

        private static int CompareIdentities(MemoryGuardIdentity left, MemoryGuardIdentity right)
        {
            int kind = string.CompareOrdinal(left.guardKind, right.guardKind);
            return kind != 0 ? kind : string.CompareOrdinal(left.guardKey, right.guardKey);
        }

        private static MemoryGuardIdentity ToIdentity(MemoryRepetitionGuardState guard)
        {
            return new MemoryGuardIdentity
            {
                guardKind = guard.guardKind,
                guardKey = guard.guardKey
            };
        }
    }
}
