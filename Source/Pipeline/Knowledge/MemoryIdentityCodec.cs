// MemoryIdentityCodec.cs — pure, bounded composite-key encoding for the unified memory system.
//
// Every identity is framed as canonical decimal UTF-16 length plus ':' plus the exact segment. The
// shared low-level helper preserves Social Reflection's shipped byte shape, while the memory-facing
// methods reject malformed, blank, unpaired-surrogate, or over-ceiling identity instead of trimming
// or truncating it into a collision.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Shared framing primitive extracted from Social Reflection's original Segment/TryReadSegment
    /// pattern. Legacy methods deliberately retain that parser's exact acceptance behavior.
    /// </summary>
    internal static class OrdinalSegmentCodec
    {
        /// <summary>Encodes one segment with the shipped length-prefix grammar.</summary>
        public static string Segment(string value)
        {
            string safe = value ?? string.Empty;
            return safe.Length.ToString(CultureInfo.InvariantCulture) + ":" + safe;
        }

        /// <summary>
        /// Reads one nonempty segment exactly as the shipped Social Reflection parser did. This
        /// compatibility path is intentionally more permissive than new memory identity parsing.
        /// </summary>
        public static bool TryReadLegacyNonEmptySegment(
            string key,
            ref int offset,
            out int valueStart,
            out int valueLength)
        {
            valueStart = 0;
            valueLength = 0;
            if (key == null || offset < 0 || offset >= key.Length) return false;

            int length = 0;
            int digitCount = 0;
            while (offset < key.Length && key[offset] != ':')
            {
                char value = key[offset];
                if (value < '0' || value > '9' || length > (int.MaxValue - 9) / 10)
                    return false;
                length = (length * 10) + (value - '0');
                digitCount++;
                offset++;
            }
            if (digitCount == 0 || offset >= key.Length || key[offset] != ':' || length <= 0)
                return false;

            offset++;
            valueStart = offset;
            valueLength = length;
            if (length > key.Length - offset) return false;
            offset += length;
            return true;
        }

        /// <summary>
        /// Reads one canonical new segment. Canonical lengths have no sign or leading zero, except
        /// the single digit '0'; the caller decides whether an empty value is legal.
        /// </summary>
        public static bool TryReadCanonicalSegment(
            string key,
            ref int offset,
            int maximumValueCharacters,
            bool allowEmpty,
            out string value)
        {
            value = string.Empty;
            if (key == null || offset < 0 || offset >= key.Length
                || maximumValueCharacters < 0)
            {
                return false;
            }

            int lengthStart = offset;
            int length = 0;
            int digitCount = 0;
            while (offset < key.Length && key[offset] != ':')
            {
                char current = key[offset];
                if (current < '0' || current > '9'
                    || length > (int.MaxValue - (current - '0')) / 10)
                {
                    return false;
                }

                length = (length * 10) + (current - '0');
                digitCount++;
                offset++;
            }

            if (digitCount == 0 || offset >= key.Length || key[offset] != ':'
                || (digitCount > 1 && key[lengthStart] == '0')
                || length > maximumValueCharacters
                || (!allowEmpty && length == 0))
            {
                return false;
            }

            offset++;
            if (length > key.Length - offset)
            {
                return false;
            }

            string candidate = key.Substring(offset, length);
            if (!MemoryIdentityCodec.IsWellFormedUtf16(candidate))
            {
                return false;
            }

            offset += length;
            value = candidate;
            return true;
        }
    }

    /// <summary>Builds and validates the canonical bounded keys in §T5 of the memory plan.</summary>
    internal static class MemoryIdentityCodec
    {
        // Defensive ceilings are code-owned collision/safety limits, not player-facing tuning.
        public const int MaximumRawIdentityCharacters = 512;
        public const int MaximumEmbeddedCompositeCharacters = 4096;
        public const int MaximumCompleteKeyCharacters = 8192;
        public const int MaximumCanonicalRepairTupleCharacters = 4194304;
        public const int MaximumFrozenPromptCharacters = 32768;

        private const string RootDomain = "memory-root-v1";
        private const string RecordDomain = "memory-record-v1";
        private const string ChapterDomain = "memory-chapter-v1";
        private const string SourceFallbackDomain = "memory-source-occurrence-fallback-v1";
        private const string FactionSubjectDomain = "memory-faction-subject-v1";
        private const string RollingSummaryDomain = "memory-summary-rolling-v1";
        private const string ClosedSummaryDomain = "memory-summary-closed-v1";
        private const string SummarySourceDomain = "memory-summary-source-v1";
        private const string ContributionDomain = "memory-contribution-v1";
        private const string ProvenanceDomain = "memory-provenance-ref-v1";
        private const string EpochDomain = "memory-epoch-v1";
        private const string EpochFallbackDomain = "memory-epoch-fallback-v1";
        private const string RepairIdDomain = "memory-repair-id-v1";
        private const string LogicalRequestDomain = "memory-logical-request-v1";

        private const string EpochFallbackSeedDomain = "memory-epoch-fallback-seed-v1";
        private const string EpochFallbackStepDomain = "memory-epoch-fallback-step-v1";
        private const string EpochFallbackCommitDomain = "memory-epoch-fallback-commit-v1";
        private const string EpochFallbackRepairDomain = "memory-epoch-fallback-repair-v1";
        private const string RepairIdentityDomain = "memory-repair-identity-v1";
        private const string RepairPayloadDomain = "memory-repair-payload-v1";

        /// <summary>Creates one exact root key, or rejects invalid/owner-self identity.</summary>
        public static bool TryCreateRootId(MemoryRootIdentity identity, out string rootId)
        {
            rootId = string.Empty;
            if (identity == null
                || !IsRequiredRaw(identity.ownerPawnId)
                || !IsCanonicalEpochToken(identity.ownerEpochToken)
                || !MemoryContractTokens.IsValidRootSubject(
                    identity.primarySubjectKind,
                    identity.primarySubjectId)
                || !IsRequiredComposite(identity.primarySubjectId)
                || (identity.primarySubjectKind == MemoryContractTokens.SubjectPawn
                    && string.Equals(
                        identity.ownerPawnId,
                        identity.primarySubjectId,
                        StringComparison.Ordinal)))
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    RootDomain,
                    identity.ownerPawnId,
                    identity.ownerEpochToken,
                    identity.primarySubjectKind,
                    identity.primarySubjectId
                },
                out rootId);
        }

        /// <summary>Parses a complete canonical root key with no trailing data.</summary>
        public static bool TryParseRootId(string rootId, out MemoryRootIdentity identity)
        {
            identity = null;
            string[] values;
            int[] limits =
            {
                MaximumRawIdentityCharacters,
                MaximumRawIdentityCharacters,
                MaximumEmbeddedCompositeCharacters,
                MaximumRawIdentityCharacters,
                MaximumEmbeddedCompositeCharacters
            };
            if (!TryReadExact(rootId, limits, out values)
                || values[0] != RootDomain)
            {
                return false;
            }

            MemoryRootIdentity parsed = new MemoryRootIdentity
            {
                ownerPawnId = values[1],
                ownerEpochToken = values[2],
                primarySubjectKind = values[3],
                primarySubjectId = values[4]
            };
            string canonical;
            if (!TryCreateRootId(parsed, out canonical)
                || !string.Equals(rootId, canonical, StringComparison.Ordinal))
            {
                return false;
            }

            identity = parsed;
            return true;
        }

        /// <summary>Creates the owner-private source/rule/fact record identity.</summary>
        public static bool TryCreateRecordId(MemoryRecordIdentity identity, out string recordId)
        {
            recordId = string.Empty;
            if (identity == null
                || !IsRequiredRaw(identity.ownerPawnId)
                || !IsCanonicalEpochToken(identity.ownerEpochToken)
                || !IsRequiredComposite(identity.sourceOccurrenceId)
                || !IsRequiredRaw(identity.captureRuleId)
                || !IsRequiredRaw(identity.factDiscriminator))
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    RecordDomain,
                    identity.ownerPawnId,
                    identity.ownerEpochToken,
                    identity.sourceOccurrenceId,
                    identity.captureRuleId,
                    identity.factDiscriminator
                },
                out recordId);
        }

        /// <summary>Parses a complete canonical record key with no trailing data.</summary>
        public static bool TryParseRecordId(string recordId, out MemoryRecordIdentity identity)
        {
            identity = null;
            string[] values;
            int[] limits =
            {
                MaximumRawIdentityCharacters,
                MaximumRawIdentityCharacters,
                MaximumEmbeddedCompositeCharacters,
                MaximumEmbeddedCompositeCharacters,
                MaximumRawIdentityCharacters,
                MaximumRawIdentityCharacters
            };
            if (!TryReadExact(recordId, limits, out values)
                || values[0] != RecordDomain)
            {
                return false;
            }

            MemoryRecordIdentity parsed = new MemoryRecordIdentity
            {
                ownerPawnId = values[1],
                ownerEpochToken = values[2],
                sourceOccurrenceId = values[3],
                captureRuleId = values[4],
                factDiscriminator = values[5]
            };
            string canonical;
            if (!TryCreateRecordId(parsed, out canonical)
                || !string.Equals(recordId, canonical, StringComparison.Ordinal))
            {
                return false;
            }

            identity = parsed;
            return true;
        }

        /// <summary>Creates one monotonically numbered chapter ID beneath an existing root.</summary>
        public static bool TryCreateChapterId(
            string rootId,
            long chapterOrdinal,
            out string chapterId)
        {
            chapterId = string.Empty;
            MemoryRootIdentity ignored;
            if (chapterOrdinal <= 0
                || string.IsNullOrEmpty(rootId)
                || rootId.Length > MaximumEmbeddedCompositeCharacters
                || !TryParseRootId(rootId, out ignored))
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    ChapterDomain,
                    rootId,
                    chapterOrdinal.ToString(CultureInfo.InvariantCulture)
                },
                out chapterId);
        }

        /// <summary>Parses one complete canonical chapter ID.</summary>
        public static bool TryParseChapterId(
            string chapterId,
            out string rootId,
            out long chapterOrdinal)
        {
            rootId = string.Empty;
            chapterOrdinal = 0;
            string[] values;
            if (!TryReadExact(
                    chapterId,
                    new[]
                    {
                        MaximumRawIdentityCharacters,
                        MaximumEmbeddedCompositeCharacters,
                        MaximumRawIdentityCharacters
                    },
                    out values)
                || values[0] != ChapterDomain
                || !TryParseCanonicalNonnegativeInt64(values[2], out chapterOrdinal)
                || chapterOrdinal <= 0)
            {
                return false;
            }

            MemoryRootIdentity ignored;
            string canonical;
            if (!TryParseRootId(values[1], out ignored)
                || !TryCreateChapterId(values[1], chapterOrdinal, out canonical)
                || !string.Equals(chapterId, canonical, StringComparison.Ordinal))
            {
                chapterOrdinal = 0;
                return false;
            }

            rootId = values[1];
            return true;
        }

        /// <summary>Creates the exact faction-instance/generation subject identity.</summary>
        public static bool TryCreateFactionSubjectId(
            string factionInstanceId,
            long allocatorGeneration,
            out string subjectId)
        {
            subjectId = string.Empty;
            if (!IsRequiredRaw(factionInstanceId) || allocatorGeneration <= 0)
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    FactionSubjectDomain,
                    factionInstanceId,
                    allocatorGeneration.ToString(CultureInfo.InvariantCulture)
                },
                out subjectId);
        }

        /// <summary>Parses one complete canonical faction-instance/generation subject identity.</summary>
        public static bool TryParseFactionSubjectId(
            string subjectId,
            out string factionInstanceId,
            out long allocatorGeneration)
        {
            factionInstanceId = string.Empty;
            allocatorGeneration = 0;
            string[] values;
            if (!TryReadExact(
                    subjectId,
                    new[]
                    {
                        MaximumRawIdentityCharacters,
                        MaximumRawIdentityCharacters,
                        MaximumRawIdentityCharacters
                    },
                    out values)
                || values[0] != FactionSubjectDomain
                || !TryParseCanonicalNonnegativeInt64(values[2], out allocatorGeneration)
                || allocatorGeneration <= 0)
            {
                allocatorGeneration = 0;
                return false;
            }

            string canonical;
            if (!TryCreateFactionSubjectId(values[1], allocatorGeneration, out canonical)
                || !string.Equals(subjectId, canonical, StringComparison.Ordinal))
            {
                allocatorGeneration = 0;
                return false;
            }

            factionInstanceId = values[1];
            return true;
        }

        /// <summary>
        /// Creates the bounded no-page source fallback after sorting and deduplicating exact subjects.
        /// Refuses when the adapter cannot prove that the tuple is unique in its source domain.
        /// </summary>
        public static bool TryCreateSourceOccurrenceFallback(
            MemorySourceOccurrenceFallback input,
            out string sourceOccurrenceId)
        {
            sourceOccurrenceId = string.Empty;
            if (input == null || !input.sourceProvesUniqueness
                || !IsRequiredRaw(input.stableSignalToken)
                || input.eventTickInvariant < 0
                || input.sourceLocalSequenceInvariant < 0
                || !IsRequiredRaw(input.factDiscriminator))
            {
                return false;
            }

            List<MemoryTypedSubject> ordered = new List<MemoryTypedSubject>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (MemoryTypedSubject subject in input.subjects
                ?? Enumerable.Empty<MemoryTypedSubject>())
            {
                if (subject == null
                    || !MemoryContractTokens.IsValidRootSubject(
                        subject.subjectKind,
                        subject.subjectId)
                    || !IsRequiredComposite(subject.subjectId))
                {
                    return false;
                }

                string pair;
                if (!TryJoin(new[] { subject.subjectKind, subject.subjectId }, out pair))
                {
                    return false;
                }

                if (seen.Add(pair))
                {
                    ordered.Add(new MemoryTypedSubject
                    {
                        subjectKind = subject.subjectKind,
                        subjectId = subject.subjectId
                    });
                }
            }

            ordered.Sort((left, right) =>
            {
                int kind = string.CompareOrdinal(left.subjectKind, right.subjectKind);
                return kind != 0 ? kind : string.CompareOrdinal(left.subjectId, right.subjectId);
            });

            List<string> segments = new List<string>
            {
                SourceFallbackDomain,
                input.stableSignalToken,
                input.eventTickInvariant.ToString(CultureInfo.InvariantCulture),
                input.sourceLocalSequenceInvariant.ToString(CultureInfo.InvariantCulture),
                input.factDiscriminator,
                ordered.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (MemoryTypedSubject subject in ordered)
            {
                segments.Add(subject.subjectKind);
                segments.Add(subject.subjectId);
            }

            return TryJoin(segments, out sourceOccurrenceId);
        }

        /// <summary>Creates the stable rolling Summary ID from raw root tuple fields.</summary>
        public static bool TryCreateRollingSummaryId(
            MemoryRootIdentity identity,
            out string summaryId)
        {
            return TryCreateSummaryId(RollingSummaryDomain, identity, null, out summaryId);
        }

        /// <summary>Creates the stable closed-chapter Summary ID from raw root tuple fields.</summary>
        public static bool TryCreateClosedSummaryId(
            MemoryRootIdentity identity,
            long chapterOrdinal,
            out string summaryId)
        {
            return TryCreateSummaryId(ClosedSummaryDomain, identity, chapterOrdinal, out summaryId);
        }

        /// <summary>
        /// Plans one monotonic epoch allocation without mutating saved state. Malformed carrier rows
        /// are inert; only canonical normal/fallback tokens participate in the live collision set.
        /// </summary>
        public static MemoryEpochAllocationPlan PlanEpochAllocation(
            MemoryEpochAllocationRequest request)
        {
            MemoryEpochAllocationPlan refused = new MemoryEpochAllocationPlan();
            if (request == null || !IsRequiredRaw(request.ownerPawnId)
                || request.lastIssuedSequence < 0)
            {
                return refused;
            }

            SortedSet<string> live = new SortedSet<string>(StringComparer.Ordinal);
            bool hasFallbackCarrier = false;
            foreach (string candidate in request.liveEpochCarriers ?? Enumerable.Empty<string>())
            {
                bool isFallback;
                if (TryValidateEpochToken(candidate, out isFallback))
                {
                    live.Add(candidate);
                    hasFallbackCarrier |= isFallback;
                }
            }

            bool chainEmpty = string.IsNullOrEmpty(request.fallbackChain);
            bool chainValid = IsLowercaseSha256(request.fallbackChain);
            bool invalidChain = !chainEmpty && !chainValid;
            bool inconsistentFallbackRegistry = chainEmpty && hasFallbackCarrier;
            bool mustRepair = invalidChain || inconsistentFallbackRegistry;

            if (!mustRepair && chainEmpty && request.lastIssuedSequence < long.MaxValue)
            {
                long next = checked(request.lastIssuedSequence + 1);
                string normal;
                if (!TryJoin(
                        new[] { EpochDomain, next.ToString(CultureInfo.InvariantCulture) },
                        out normal))
                {
                    return refused;
                }

                // A collision means the supplied high-water was not actually exhaustive. Ordinary
                // enrollment refuses; Brainwipe uses the same bounded repair cursor as bad chain data.
                if (!live.Contains(normal))
                {
                    return new MemoryEpochAllocationPlan
                    {
                        canMutate = true,
                        outcomeToken = MemoryEpochAllocationPlan.Normal,
                        epochToken = normal,
                        nextSequence = next,
                        nextFallbackChain = string.Empty
                    };
                }

                mustRepair = true;
            }

            if (mustRepair && !request.isTargetBrainwipe)
            {
                return refused;
            }

            string priorChain;
            bool repaired = false;
            if (mustRepair)
            {
                StringBuilder repair = new StringBuilder();
                repair.Append(OrdinalSegmentCodec.Segment(EpochFallbackRepairDomain));
                repair.Append(OrdinalSegmentCodec.Segment(
                    request.lastIssuedSequence.ToString(CultureInfo.InvariantCulture)));
                repair.Append(OrdinalSegmentCodec.Segment(
                    live.Count.ToString(CultureInfo.InvariantCulture)));
                foreach (string token in live)
                    repair.Append(OrdinalSegmentCodec.Segment(token));
                priorChain = ComputeSha256Utf8(repair.ToString());
                repaired = true;
            }
            else if (chainValid)
            {
                priorChain = request.fallbackChain;
            }
            else
            {
                priorChain = ComputeSha256Utf8(
                    OrdinalSegmentCodec.Segment(EpochFallbackSeedDomain)
                    + OrdinalSegmentCodec.Segment(long.MaxValue.ToString(CultureInfo.InvariantCulture)));
            }

            string stepHash = ComputeSha256Utf8(
                OrdinalSegmentCodec.Segment(EpochFallbackStepDomain)
                + OrdinalSegmentCodec.Segment(priorChain)
                + OrdinalSegmentCodec.Segment(request.ownerPawnId));
            string epochToken = string.Empty;
            long chosenProbe = -1;
            for (long probe = 0; probe <= live.Count; probe++)
            {
                string candidate;
                if (!TryJoin(
                        new[]
                        {
                            EpochFallbackDomain,
                            stepHash,
                            probe.ToString(CultureInfo.InvariantCulture)
                        },
                        out candidate))
                {
                    return refused;
                }

                if (!live.Contains(candidate))
                {
                    epochToken = candidate;
                    chosenProbe = probe;
                    break;
                }
            }

            if (chosenProbe < 0) return refused;
            string nextChain = ComputeSha256Utf8(
                OrdinalSegmentCodec.Segment(EpochFallbackCommitDomain)
                + OrdinalSegmentCodec.Segment(priorChain)
                + OrdinalSegmentCodec.Segment(epochToken));
            return new MemoryEpochAllocationPlan
            {
                canMutate = true,
                outcomeToken = MemoryEpochAllocationPlan.Fallback,
                epochToken = epochToken,
                // A nonempty fallback chain permanently saturates the numeric allocator. Publishing
                // long.MaxValue with the chain prevents a corrupt-low high-water from re-entering
                // normal allocation after reload (T6.9).
                nextSequence = long.MaxValue,
                nextFallbackChain = nextChain,
                repairedFallbackChain = repaired,
                probeOrdinal = chosenProbe,
                priorFallbackChain = priorChain,
                stepHash = stepHash
            };
        }

        /// <summary>Recognizes one canonical normal or fallback epoch token.</summary>
        public static bool TryValidateEpochToken(string epochToken, out bool isFallback)
        {
            long ignoredSequence;
            return TryParseEpochToken(epochToken, out isFallback, out ignoredSequence);
        }

        /// <summary>
        /// Parses one canonical epoch token into its fallback flag and, for a NORMAL token, its
        /// positive issued sequence. Fallback tokens carry no numeric sequence and report 0.
        /// The §T13.2 carrier registry uses this to raise the saved allocator high-water.
        /// </summary>
        public static bool TryParseEpochToken(
            string epochToken,
            out bool isFallback,
            out long normalSequence)
        {
            isFallback = false;
            normalSequence = 0;
            string[] values;
            if (TryReadExact(
                    epochToken,
                    new[] { MaximumRawIdentityCharacters, MaximumRawIdentityCharacters },
                    out values))
            {
                return values[0] == EpochDomain
                    && TryParseCanonicalNonnegativeInt64(values[1], out normalSequence)
                    && normalSequence > 0;
            }

            if (TryReadExact(
                    epochToken,
                    new[]
                    {
                        MaximumRawIdentityCharacters,
                        MaximumRawIdentityCharacters,
                        MaximumRawIdentityCharacters
                    },
                    out values)
                && values[0] == EpochFallbackDomain
                && IsLowercaseSha256(values[1]))
            {
                long ignoredProbe;
                if (!TryParseCanonicalNonnegativeInt64(values[2], out ignoredProbe))
                {
                    return false;
                }

                isFallback = true;
                return true;
            }

            return false;
        }

        /// <summary>Creates the synthetic rolling Summary source-occurrence identity.</summary>
        public static bool TryCreateRollingSummarySourceId(
            MemoryRootIdentity identity,
            out string sourceOccurrenceId)
        {
            return TryCreateSummaryId(SummarySourceDomain, identity, null, out sourceOccurrenceId);
        }

        /// <summary>Creates the synthetic closed Summary source-occurrence identity.</summary>
        public static bool TryCreateClosedSummarySourceId(
            MemoryRootIdentity identity,
            long chapterOrdinal,
            out string sourceOccurrenceId)
        {
            return TryCreateSummaryId(SummarySourceDomain, identity, chapterOrdinal, out sourceOccurrenceId);
        }

        /// <summary>Creates one immutable Summary contribution identity.</summary>
        public static bool TryCreateContributionId(
            string originRecordId,
            long originFactOrdinal,
            string originFactId,
            out string contributionId)
        {
            contributionId = string.Empty;
            if (!IsRequiredComposite(originRecordId) || originFactOrdinal < 0
                || !IsRequiredComposite(originFactId))
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    ContributionDomain,
                    originRecordId,
                    originFactOrdinal.ToString(CultureInfo.InvariantCulture),
                    originFactId
                },
                out contributionId);
        }

        /// <summary>Creates the exact current-schema canonical fact identity tuple.</summary>
        public static bool TryCreateFactId(
            string captureRuleId,
            string factDiscriminator,
            string factKind,
            string canonicalSubjectKind,
            string canonicalSubjectId,
            string aggregationToken,
            out string factId)
        {
            factId = string.Empty;
            if (!IsRequiredRaw(captureRuleId) || !IsRequiredRaw(factDiscriminator)
                || !IsRequiredRaw(factKind) || !IsRequiredRaw(canonicalSubjectKind)
                || !IsRequiredComposite(canonicalSubjectId) || !IsRequiredRaw(aggregationToken))
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    captureRuleId, factDiscriminator, factKind, canonicalSubjectKind,
                    canonicalSubjectId, aggregationToken
                },
                out factId);
        }

        /// <summary>Creates a subject reference ID; display labels are deliberately excluded.</summary>
        public static bool TryCreateSubjectRefId(
            string subjectKind,
            string subjectId,
            string roleToken,
            string knownnessToken,
            out string subjectRefId)
        {
            subjectRefId = string.Empty;
            if (!IsRequiredRaw(subjectKind) || !IsRequiredComposite(subjectId)
                || !IsRequiredRaw(roleToken) || !IsRequiredRaw(knownnessToken))
            {
                return false;
            }

            return TryJoin(
                new[] { subjectKind, subjectId, roleToken, knownnessToken }, out subjectRefId);
        }

        /// <summary>Creates a provenance reference ID, including its two intentionally nullable fields.</summary>
        public static bool TryCreateProvenanceRefId(
            string sourceKindToken,
            string sourceOccurrenceId,
            string sourceEventId,
            string captureRuleId,
            string factDiscriminator,
            string integrationToken,
            out string provenanceRefId)
        {
            provenanceRefId = string.Empty;
            bool knownKind = sourceKindToken == "diary_event"
                || sourceKindToken == "capture_signal"
                || sourceKindToken == "legacy_migration"
                || sourceKindToken == "integration";
            bool sourceEventValid = sourceKindToken == "diary_event"
                ? IsRequiredComposite(sourceEventId)
                : IsOptionalComposite(sourceEventId);
            bool integrationValid = sourceKindToken == "integration"
                ? IsRequiredRaw(integrationToken)
                : string.IsNullOrEmpty(integrationToken);
            if (!knownKind || !IsRequiredComposite(sourceOccurrenceId)
                || !sourceEventValid || !IsRequiredRaw(captureRuleId)
                || !IsRequiredRaw(factDiscriminator) || !integrationValid)
            {
                return false;
            }

            return TryJoin(
                new[]
                {
                    ProvenanceDomain, sourceKindToken, sourceOccurrenceId, sourceEventId,
                    captureRuleId, factDiscriminator, integrationToken
                },
                out provenanceRefId);
        }

        /// <summary>
        /// Mints the one allowed deterministic replacement grammar for an opaque saved-ID collision.
        /// The identity and payload tuples must already be normalized, recursively framed schema rows.
        /// </summary>
        public static bool TryCreateRepairId(
            string kindToken,
            string originalOpaqueId,
            string canonicalIdentityTuple,
            string canonicalPayloadTuple,
            long collisionOrdinal,
            out string repairId)
        {
            repairId = string.Empty;
            if (!IsRepairKind(kindToken) || !IsRequiredComposite(originalOpaqueId)
                || !IsCanonicalRepairTuple(canonicalIdentityTuple)
                || !IsCanonicalRepairTuple(canonicalPayloadTuple)
                || collisionOrdinal < 0)
            {
                return false;
            }

            string identityHash = ComputeSha256Utf8(
                OrdinalSegmentCodec.Segment(RepairIdentityDomain)
                + OrdinalSegmentCodec.Segment(kindToken)
                + canonicalIdentityTuple);
            string payloadHash = ComputeSha256Utf8(
                OrdinalSegmentCodec.Segment(RepairPayloadDomain)
                + OrdinalSegmentCodec.Segment(kindToken)
                + canonicalPayloadTuple);
            return TryJoin(
                new[]
                {
                    RepairIdDomain, kindToken, originalOpaqueId, identityHash, payloadHash,
                    collisionOrdinal.ToString(CultureInfo.InvariantCulture)
                },
                out repairId);
        }

        /// <summary>Parses a complete canonical repair ID without needing the discarded payload tuples.</summary>
        public static bool TryParseRepairId(string repairId, out MemoryRepairIdentity identity)
        {
            identity = null;
            string[] values;
            long ordinal;
            if (!TryReadExact(
                    repairId,
                    new[]
                    {
                        MaximumRawIdentityCharacters, MaximumRawIdentityCharacters,
                        MaximumEmbeddedCompositeCharacters, MaximumRawIdentityCharacters,
                        MaximumRawIdentityCharacters, MaximumRawIdentityCharacters
                    },
                    out values)
                || values[0] != RepairIdDomain || !IsRepairKind(values[1])
                || !IsLowercaseSha256(values[3]) || !IsLowercaseSha256(values[4])
                || !TryParseCanonicalNonnegativeInt64(values[5], out ordinal))
            {
                return false;
            }

            string canonical;
            if (!TryJoin(values, out canonical)
                || !string.Equals(repairId, canonical, StringComparison.Ordinal))
            {
                return false;
            }

            identity = new MemoryRepairIdentity
            {
                kindToken = values[1],
                originalOpaqueId = values[2],
                identityHash = values[3],
                payloadHash = values[4],
                collisionOrdinal = ordinal
            };
            return true;
        }

        /// <summary>Computes T17's H(domain, fields...) over BOM-less UTF-8 framed values.</summary>
        public static bool TryComputeFramedHash(
            string domain,
            IEnumerable<string> fields,
            out string hash)
        {
            hash = string.Empty;
            if (!IsRequiredRaw(domain) || fields == null) return false;
            StringBuilder input = new StringBuilder(OrdinalSegmentCodec.Segment(domain));
            foreach (string field in fields)
            {
                if (field == null || !IsWellFormedUtf16(field)
                    || field.Length > MaximumCanonicalRepairTupleCharacters)
                {
                    return false;
                }

                input.Append(OrdinalSegmentCodec.Segment(field));
                if (input.Length > MaximumCanonicalRepairTupleCharacters) return false;
            }

            hash = ComputeSha256Utf8(input.ToString());
            return true;
        }

        /// <summary>Creates the positive component-global logical request ID.</summary>
        public static bool TryCreateLogicalRequestId(long sequence, out string logicalRequestId)
        {
            logicalRequestId = string.Empty;
            if (sequence <= 0) return false;
            return TryJoin(
                new[] { LogicalRequestDomain, sequence.ToString(CultureInfo.InvariantCulture) },
                out logicalRequestId);
        }

        /// <summary>Parses one complete canonical logical request ID.</summary>
        public static bool TryParseLogicalRequestId(string logicalRequestId, out long sequence)
        {
            sequence = 0;
            string[] values;
            if (!TryReadExact(
                    logicalRequestId,
                    new[] { MaximumRawIdentityCharacters, MaximumRawIdentityCharacters },
                    out values)
                || values[0] != LogicalRequestDomain
                || !TryParseCanonicalNonnegativeInt64(values[1], out sequence)
                || sequence <= 0)
            {
                sequence = 0;
                return false;
            }
            return true;
        }

        /// <summary>Creates the active request deduplication key from its exact immutable tuple.</summary>
        public static bool TryCreateLogicalRequestKey(
            string requestPurposeToken,
            string eventIdOrOpportunityKey,
            string povRoleToken,
            string ownerPawnId,
            string ownerEpochToken,
            string evidenceEpochToken,
            out string logicalRequestKey)
        {
            logicalRequestKey = string.Empty;
            bool ownerless = string.IsNullOrEmpty(ownerPawnId) && string.IsNullOrEmpty(ownerEpochToken);
            bool owned = IsRequiredRaw(ownerPawnId) && IsCanonicalEpochToken(ownerEpochToken);
            if (!IsRequestPurpose(requestPurposeToken)
                || !IsRequiredComposite(eventIdOrOpportunityKey)
                || !IsPovRole(povRoleToken)
                || (povRoleToken == "neutral" ? !ownerless : !owned)
                || !IsLowercaseSha256(evidenceEpochToken))
            {
                return false;
            }

            return TryComputeFramedHash(
                "memory-logical-request-key-v1",
                new[]
                {
                    requestPurposeToken, eventIdOrOpportunityKey, povRoleToken, ownerPawnId,
                    ownerEpochToken, evidenceEpochToken
                },
                out logicalRequestKey);
        }

        /// <summary>Creates the exact ordered frozen evidence-set fingerprint.</summary>
        public static bool TryCreateEvidenceSetFingerprint(
            IEnumerable<MemoryEvidenceIdentity> evidence,
            out string evidenceSetFingerprint)
        {
            evidenceSetFingerprint = string.Empty;
            List<string> fields;
            if (!TryFlattenEvidence(evidence, out fields)) return false;
            return TryComputeFramedHash(
                "memory-evidence-set-v1", fields, out evidenceSetFingerprint);
        }

        /// <summary>
        /// Creates the one frozen evidence-cycle token from the canonical union across all variants.
        /// Input permutation and byte-equivalent duplicates cannot change the result.
        /// </summary>
        public static bool TryCreateEvidenceEpochToken(
            string requestPurposeToken,
            string eventIdOrOpportunityKey,
            string povRoleToken,
            string ownerPawnId,
            string ownerEpochToken,
            IEnumerable<MemoryEvidenceIdentity> unionEvidence,
            IEnumerable<MemoryGuardIdentity> unionGuards,
            out string evidenceEpochToken)
        {
            evidenceEpochToken = string.Empty;
            bool ownerless = string.IsNullOrEmpty(ownerPawnId) && string.IsNullOrEmpty(ownerEpochToken);
            bool owned = IsRequiredRaw(ownerPawnId) && IsCanonicalEpochToken(ownerEpochToken);
            if (!IsRequestPurpose(requestPurposeToken)
                || !IsRequiredComposite(eventIdOrOpportunityKey)
                || !IsPovRole(povRoleToken)
                || (povRoleToken == "neutral" ? !ownerless : !owned))
            {
                return false;
            }

            List<MemoryEvidenceIdentity> evidence =
                (unionEvidence ?? Enumerable.Empty<MemoryEvidenceIdentity>()).ToList();
            for (int index = 0; index < evidence.Count; index++)
            {
                MemoryEvidenceIdentity row = evidence[index];
                if (row == null || !IsRequiredComposite(row.recordId)
                    || !IsRequiredComposite(row.sourceOccurrenceId)
                    || !IsOptionalComposite(row.rootIdOrEmpty)) return false;
            }
            evidence = evidence.OrderBy(row => row, new EvidenceComparer()).ToList();
            for (int index = evidence.Count - 1; index > 0; index--)
                if (CompareEvidence(evidence[index - 1], evidence[index]) == 0)
                    evidence.RemoveAt(index);

            List<MemoryGuardIdentity> guards =
                (unionGuards ?? Enumerable.Empty<MemoryGuardIdentity>()).ToList();
            for (int index = 0; index < guards.Count; index++)
            {
                MemoryGuardIdentity row = guards[index];
                if (row == null || !IsRequiredRaw(row.guardKind)
                    || !IsRequiredComposite(row.guardKey)) return false;
            }
            guards = guards.OrderBy(row => row.guardKind, StringComparer.Ordinal)
                .ThenBy(row => row.guardKey, StringComparer.Ordinal).ToList();
            for (int index = guards.Count - 1; index > 0; index--)
                if (string.Equals(guards[index - 1].guardKind, guards[index].guardKind,
                        StringComparison.Ordinal)
                    && string.Equals(guards[index - 1].guardKey, guards[index].guardKey,
                        StringComparison.Ordinal))
                    guards.RemoveAt(index);

            List<string> fields = new List<string>
            {
                requestPurposeToken, eventIdOrOpportunityKey, povRoleToken, ownerPawnId,
                ownerEpochToken, evidence.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (MemoryEvidenceIdentity row in evidence)
            {
                fields.Add(row.recordId);
                fields.Add(row.sourceOccurrenceId);
                fields.Add(row.rootIdOrEmpty);
            }
            fields.Add(guards.Count.ToString(CultureInfo.InvariantCulture));
            foreach (MemoryGuardIdentity row in guards)
            {
                fields.Add(row.guardKind);
                fields.Add(row.guardKey);
            }
            return TryComputeFramedHash(
                "memory-evidence-epoch-v1", fields, out evidenceEpochToken);
        }

        /// <summary>Creates the complete evidence-plus-guard receipt plan fingerprint.</summary>
        public static bool TryCreateReceiptPlanFingerprint(
            IEnumerable<MemoryEvidenceIdentity> evidence,
            IEnumerable<MemoryGuardIdentity> guards,
            out string receiptPlanFingerprint)
        {
            receiptPlanFingerprint = string.Empty;
            List<string> evidenceFields;
            if (!TryFlattenEvidence(evidence, out evidenceFields)) return false;
            string evidenceFingerprint;
            if (!TryComputeFramedHash(
                    "memory-evidence-set-v1", evidenceFields, out evidenceFingerprint))
            {
                return false;
            }

            List<string> fields = new List<string>
            {
                evidenceFingerprint,
                evidenceFields[0]
            };
            fields.AddRange(evidenceFields.Skip(1));

            List<MemoryGuardIdentity> rows = (guards ?? Enumerable.Empty<MemoryGuardIdentity>()).ToList();
            fields.Add(rows.Count.ToString(CultureInfo.InvariantCulture));
            MemoryGuardIdentity previous = null;
            for (int index = 0; index < rows.Count; index++)
            {
                MemoryGuardIdentity row = rows[index];
                if (row == null || !IsRequiredRaw(row.guardKind)
                    || !IsRequiredComposite(row.guardKey))
                {
                    return false;
                }
                if (previous != null
                    && (string.CompareOrdinal(previous.guardKind, row.guardKind) > 0
                        || (string.Equals(previous.guardKind, row.guardKind, StringComparison.Ordinal)
                            && string.CompareOrdinal(previous.guardKey, row.guardKey) >= 0)))
                {
                    return false;
                }
                previous = row;
                fields.Add(row.guardKind);
                fields.Add(row.guardKey);
            }

            return TryComputeFramedHash(
                "memory-receipt-plan-v1", fields, out receiptPlanFingerprint);
        }

        /// <summary>Creates the canonical line-first diagnostic provenance fingerprint.</summary>
        public static bool TryCreateDiagnosticProvenanceFingerprint(
            IEnumerable<MemoryDiagnosticIdentity> diagnostics,
            out string diagnosticProvenanceFingerprint)
        {
            diagnosticProvenanceFingerprint = string.Empty;
            List<MemoryDiagnosticIdentity> rows =
                (diagnostics ?? Enumerable.Empty<MemoryDiagnosticIdentity>()).ToList();
            List<string> fields = new List<string>
            {
                rows.Count.ToString(CultureInfo.InvariantCulture)
            };
            MemoryDiagnosticIdentity previous = null;
            for (int index = 0; index < rows.Count; index++)
            {
                MemoryDiagnosticIdentity row = rows[index];
                if (row == null || row.lineOrdinal < 0
                    || !IsRequiredRaw(row.provenanceKindToken)
                    || !IsRequiredComposite(row.sourceId)
                    || !IsOptionalComposite(row.recordIdOrEmpty)
                    || !IsOptionalComposite(row.sourceOccurrenceIdOrEmpty)
                    || !IsOptionalComposite(row.rootIdOrEmpty))
                {
                    return false;
                }
                string ordinal = row.lineOrdinal.ToString(CultureInfo.InvariantCulture);
                if (previous != null && CompareDiagnostics(previous, row) >= 0) return false;
                previous = row;
                fields.Add(row.provenanceKindToken);
                fields.Add(row.sourceId);
                fields.Add(row.recordIdOrEmpty);
                fields.Add(row.sourceOccurrenceIdOrEmpty);
                fields.Add(row.rootIdOrEmpty);
                fields.Add(ordinal);
            }
            return TryComputeFramedHash(
                "memory-diagnostic-provenance-v1", fields,
                out diagnosticProvenanceFingerprint);
        }

        /// <summary>Creates one immutable prompt variant key; transport lane details are excluded.</summary>
        public static bool TryCreatePromptVariantKey(
            string logicalRequestId,
            int variantOrdinal,
            string requestPurposeToken,
            string templateIdentity,
            string contextDetailIdentity,
            string systemPrompt,
            string userPrompt,
            string receiptPlanFingerprint,
            string diagnosticProvenanceFingerprint,
            out string variantKey)
        {
            variantKey = string.Empty;
            long ignored;
            if (!TryParseLogicalRequestId(logicalRequestId, out ignored)
                || variantOrdinal < 0 || !IsRequestPurpose(requestPurposeToken)
                || !IsRequiredComposite(templateIdentity)
                || !IsRequiredComposite(contextDetailIdentity)
                || !IsBoundedPrompt(systemPrompt) || !IsBoundedPrompt(userPrompt)
                || !IsLowercaseSha256(receiptPlanFingerprint)
                || !IsLowercaseSha256(diagnosticProvenanceFingerprint))
            {
                return false;
            }

            return TryComputeFramedHash(
                "memory-prompt-variant-v1",
                new[]
                {
                    logicalRequestId,
                    variantOrdinal.ToString(CultureInfo.InvariantCulture),
                    requestPurposeToken,
                    templateIdentity,
                    contextDetailIdentity,
                    ComputeSha256Utf8(systemPrompt),
                    ComputeSha256Utf8(userPrompt),
                    receiptPlanFingerprint,
                    diagnosticProvenanceFingerprint
                },
                out variantKey);
        }

        /// <summary>Creates the declaration-order fingerprint echoed by every send receipt/result.</summary>
        public static bool TryCreateInvocationPermitFingerprint(
            MemoryInvocationPermitIdentity permit,
            out string permitFingerprint)
        {
            permitFingerprint = string.Empty;
            long ignored;
            bool ownerless = permit != null
                && string.IsNullOrEmpty(permit.ownerPawnId)
                && string.IsNullOrEmpty(permit.ownerEpochToken);
            bool owned = permit != null && IsRequiredRaw(permit.ownerPawnId)
                && IsCanonicalEpochToken(permit.ownerEpochToken);
            string expectedRequestKey;
            bool optionalPurpose = permit != null
                && (permit.requestPurposeToken == "memory_reflection"
                    || permit.requestPurposeToken == "summary_wording");
            bool narrativePurpose = permit != null
                && (permit.requestPurposeToken == "normal_diary"
                    || permit.requestPurposeToken == "memory_reflection");
            if (permit == null
                || !TryParseLogicalRequestId(permit.logicalRequestId, out ignored)
                || !IsLowercaseSha256(permit.logicalRequestKey)
                || !IsRequestPurpose(permit.requestPurposeToken)
                || permit.sessionId <= 0
                || !IsRequiredComposite(permit.eventIdOrOpportunityKey)
                || !IsPovRole(permit.povRoleToken)
                || (permit.povRoleToken == "neutral" ? !ownerless : !owned)
                || !IsLowercaseSha256(permit.evidenceEpochToken)
                || permit.ownerCancellationGeneration < 0
                || permit.globalCancellationGeneration < 0
                || (optionalPurpose
                    ? permit.optionalRequestInvalidationGeneration <= 0
                        || permit.optionalRequestInvalidationGeneration == long.MaxValue
                    : permit.optionalRequestInvalidationGeneration != 0)
                || permit.attemptOrdinal <= 0
                || !IsLowercaseSha256(permit.variantKey)
                || !IsLowercaseSha256(permit.receiptPlanFingerprint)
                || permit.invocationSequence <= 0 || permit.invocationTick < 0
                || permit.narrativeUseWinnerAttemptOrdinal < 0
                || permit.narrativeUseWinnerAttemptOrdinal > permit.attemptOrdinal
                || (!narrativePurpose && permit.narrativeUseWinnerAttemptOrdinal != 0)
                || !TryCreateLogicalRequestKey(
                    permit.requestPurposeToken, permit.eventIdOrOpportunityKey,
                    permit.povRoleToken, permit.ownerPawnId, permit.ownerEpochToken,
                    permit.evidenceEpochToken, out expectedRequestKey)
                || !string.Equals(
                    permit.logicalRequestKey, expectedRequestKey, StringComparison.Ordinal))
            {
                return false;
            }

            return TryComputeFramedHash(
                "memory-invocation-permit-v1",
                new[]
                {
                    permit.logicalRequestId, permit.logicalRequestKey,
                    permit.requestPurposeToken,
                    permit.sessionId.ToString(CultureInfo.InvariantCulture),
                    permit.eventIdOrOpportunityKey, permit.povRoleToken, permit.ownerPawnId,
                    permit.ownerEpochToken, permit.evidenceEpochToken,
                    permit.ownerCancellationGeneration.ToString(CultureInfo.InvariantCulture),
                    permit.globalCancellationGeneration.ToString(CultureInfo.InvariantCulture),
                    permit.optionalRequestInvalidationGeneration.ToString(CultureInfo.InvariantCulture),
                    permit.attemptOrdinal.ToString(CultureInfo.InvariantCulture),
                    permit.variantKey, permit.receiptPlanFingerprint,
                    permit.invocationSequence.ToString(CultureInfo.InvariantCulture),
                    permit.invocationTick.ToString(CultureInfo.InvariantCulture),
                    permit.narrativeUseWinnerAttemptOrdinal.ToString(CultureInfo.InvariantCulture)
                },
                out permitFingerprint);
        }

        /// <summary>True only when a string contains complete UTF-16 scalar pairs.</summary>
        public static bool IsWellFormedUtf16(string value)
        {
            if (value == null) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    index++;
                }
                else if (char.IsLowSurrogate(current))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCreateSummaryId(
            string domain,
            MemoryRootIdentity identity,
            long? chapterOrdinal,
            out string summaryId)
        {
            summaryId = string.Empty;
            string rootId;
            if (!TryCreateRootId(identity, out rootId)
                || (chapterOrdinal.HasValue && chapterOrdinal.Value <= 0))
            {
                return false;
            }

            List<string> segments = new List<string>
            {
                domain,
                identity.ownerPawnId,
                identity.ownerEpochToken,
                identity.primarySubjectKind,
                identity.primarySubjectId
            };
            if (chapterOrdinal.HasValue)
            {
                segments.Add(chapterOrdinal.Value.ToString(CultureInfo.InvariantCulture));
            }

            return TryJoin(segments, out summaryId);
        }

        private static bool TryJoin(IEnumerable<string> segments, out string encoded)
        {
            encoded = string.Empty;
            if (segments == null) return false;

            StringBuilder builder = new StringBuilder();
            foreach (string segment in segments)
            {
                if (segment == null || !IsWellFormedUtf16(segment))
                {
                    return false;
                }

                string framed = OrdinalSegmentCodec.Segment(segment);
                if (framed.Length > MaximumCompleteKeyCharacters - builder.Length)
                {
                    return false;
                }

                builder.Append(framed);
            }

            if (builder.Length == 0 || builder.Length > MaximumCompleteKeyCharacters)
            {
                return false;
            }

            encoded = builder.ToString();
            return true;
        }

        private static bool TryReadExact(
            string encoded,
            int[] maximumSegmentCharacters,
            out string[] values)
        {
            values = null;
            if (string.IsNullOrEmpty(encoded)
                || encoded.Length > MaximumCompleteKeyCharacters
                || maximumSegmentCharacters == null)
            {
                return false;
            }

            string[] parsed = new string[maximumSegmentCharacters.Length];
            int offset = 0;
            for (int index = 0; index < parsed.Length; index++)
            {
                if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                        encoded,
                        ref offset,
                        maximumSegmentCharacters[index],
                        false,
                        out parsed[index]))
                {
                    return false;
                }
            }

            if (offset != encoded.Length)
            {
                return false;
            }

            values = parsed;
            return true;
        }

        private static bool IsRequiredRaw(string value)
        {
            return IsRequired(value, MaximumRawIdentityCharacters);
        }

        private static bool IsRequiredComposite(string value)
        {
            return IsRequired(value, MaximumEmbeddedCompositeCharacters);
        }

        private static bool IsOptionalComposite(string value)
        {
            return value != null && value.Length <= MaximumEmbeddedCompositeCharacters
                && IsWellFormedUtf16(value);
        }

        private static bool IsCanonicalEpochToken(string value)
        {
            bool ignored;
            return TryValidateEpochToken(value, out ignored);
        }

        private static bool TryFlattenEvidence(
            IEnumerable<MemoryEvidenceIdentity> evidence,
            out List<string> fields)
        {
            List<MemoryEvidenceIdentity> rows =
                (evidence ?? Enumerable.Empty<MemoryEvidenceIdentity>()).ToList();
            fields = new List<string>
            {
                rows.Count.ToString(CultureInfo.InvariantCulture)
            };
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                MemoryEvidenceIdentity row = rows[index];
                if (row == null || !IsRequiredComposite(row.recordId)
                    || !IsRequiredComposite(row.sourceOccurrenceId)
                    || !IsOptionalComposite(row.rootIdOrEmpty))
                {
                    fields = null;
                    return false;
                }
                string tuple = OrdinalSegmentCodec.Segment(row.recordId)
                    + OrdinalSegmentCodec.Segment(row.sourceOccurrenceId)
                    + OrdinalSegmentCodec.Segment(row.rootIdOrEmpty);
                if (!seen.Add(tuple))
                {
                    fields = null;
                    return false;
                }
                fields.Add(row.recordId);
                fields.Add(row.sourceOccurrenceId);
                fields.Add(row.rootIdOrEmpty);
            }
            return true;
        }

        private static int CompareDiagnostics(
            MemoryDiagnosticIdentity left,
            MemoryDiagnosticIdentity right)
        {
            int comparison = left.lineOrdinal.CompareTo(right.lineOrdinal);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.provenanceKindToken, right.provenanceKindToken);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.sourceId, right.sourceId);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.recordIdOrEmpty, right.recordIdOrEmpty);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(
                left.sourceOccurrenceIdOrEmpty, right.sourceOccurrenceIdOrEmpty);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.rootIdOrEmpty, right.rootIdOrEmpty);
        }

        private static int CompareEvidence(
            MemoryEvidenceIdentity left,
            MemoryEvidenceIdentity right)
        {
            int comparison = string.CompareOrdinal(left.recordId, right.recordId);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.sourceOccurrenceId, right.sourceOccurrenceId);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.rootIdOrEmpty, right.rootIdOrEmpty);
        }

        private sealed class EvidenceComparer : IComparer<MemoryEvidenceIdentity>
        {
            public int Compare(MemoryEvidenceIdentity left, MemoryEvidenceIdentity right)
            {
                return CompareEvidence(left, right);
            }
        }

        private static bool IsRequestPurpose(string value)
        {
            return value == "normal_diary" || value == "memory_reflection"
                || value == "summary_wording" || value == "manual_regenerate";
        }

        private static bool IsPovRole(string value)
        {
            return value == "initiator" || value == "recipient" || value == "neutral";
        }

        private static bool IsBoundedPrompt(string value)
        {
            return value != null && value.Length <= MaximumFrozenPromptCharacters
                && IsWellFormedUtf16(value);
        }

        private static bool IsCanonicalRepairTuple(string value)
        {
            if (string.IsNullOrEmpty(value)
                || value.Length > MaximumCanonicalRepairTupleCharacters
                || !IsWellFormedUtf16(value))
            {
                return false;
            }

            int offset = 0;
            int segmentCount = 0;
            while (offset < value.Length)
            {
                string ignored;
                if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                        value,
                        ref offset,
                        MaximumCanonicalRepairTupleCharacters,
                        true,
                        out ignored))
                {
                    return false;
                }
                segmentCount++;
            }
            return segmentCount > 0;
        }

        private static bool IsRepairKind(string value)
        {
            return value == "record" || value == "contribution"
                || value == "chapter" || value == "archive";
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9')
                    || (current >= 'a' && current <= 'f')))
                    return false;
            }
            return true;
        }

        private static string ComputeSha256Utf8(string value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static bool IsRequired(string value, int maximumCharacters)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= maximumCharacters
                && IsWellFormedUtf16(value);
        }

        private static bool TryParseCanonicalNonnegativeInt64(string value, out long parsed)
        {
            parsed = 0;
            if (string.IsNullOrEmpty(value)
                || (value.Length > 1 && value[0] == '0')
                || !long.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsed)
                || parsed < 0)
            {
                parsed = 0;
                return false;
            }

            return string.Equals(
                value,
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }
    }
}
