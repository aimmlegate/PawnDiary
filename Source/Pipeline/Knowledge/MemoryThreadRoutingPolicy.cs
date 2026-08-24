// MemoryThreadRoutingPolicy.cs — pure capture-contract validation, exact route resolution, and
// recall-consumer reachability for the unified memory system.
//
// XML Defs describe which exact extractor owns a route. Game adapters provide detached candidates
// for those declared extractors; this policy never falls through to labels, topics, list position,
// semantic similarity, or a live Pawn/Def lookup.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PawnDiary
{
    /// <summary>Stable aggregation/value tokens understood by deterministic summary policy.</summary>
    internal static class MemoryFactContractTokens
    {
        public const string CountOccurrences = "count_occurrences";
        public const string OrdinalSet = "ordinal_set";
        public const string Int64Range = "int64_range";
        public const string LatestState = "latest_state";

        public const string ValueEmpty = "empty";
        public const string ValueOrdinal = "ordinal";
        public const string ValueInt64 = "int64";
        public const string ValueState = "state";

        /// <summary>True only when the aggregation token owns the declared value grammar.</summary>
        public static bool IsMatchingPair(string aggregationToken, string valueKind)
        {
            return (aggregationToken == CountOccurrences && valueKind == ValueEmpty)
                || (aggregationToken == OrdinalSet && valueKind == ValueOrdinal)
                || (aggregationToken == Int64Range && valueKind == ValueInt64)
                || (aggregationToken == LatestState && valueKind == ValueState);
        }
    }

    /// <summary>One exact candidate collected by an impure adapter for a declared extractor.</summary>
    internal sealed class MemoryRouteCandidate
    {
        public string extractorToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenLabel = string.Empty;
    }

    /// <summary>Pure exact-route outcome. A blank root subject means Standalone.</summary>
    internal sealed class MemoryRouteResolution
    {
        public bool isThreaded;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string frozenLabel = string.Empty;
        public string reasonToken = string.Empty;
    }

    /// <summary>One deliberate consumer in the common memory exclusion/reachability registry.</summary>
    internal sealed class MemoryRecallConsumerContract
    {
        public string consumerId = string.Empty;
        public List<string> eligibleSubjectKinds = new List<string>();
        public List<string> eligibleWritingFormats = new List<string>();
        public bool allowsStandalone;
        public bool requiresCurrentStateRendering;
        public int fullMaximumLines;
        public int balancedMaximumLines;
        public int compactMaximumLines;
        public int offMaximumLines;
        public string characterCapDimensionToken = string.Empty;
        public bool requiresOwnerMatch;
        public bool requiresEpochMatch;
        public bool requiresCategoryEnabled;
        public bool honorsSuppression;
        public bool excludesCurrentEvent;
        public string usagePurposeToken = string.Empty;
        public bool createsExtraProviderRequest;
        public bool appliesCommonExclusionContract;
    }

    /// <summary>Pure validation and exact-route operations for M0 capture contracts.</summary>
    internal static class MemoryThreadRoutingPolicy
    {
        public const string StandaloneNoRoute = "standalone_no_route";
        public const string StandaloneMissingIdentity = "standalone_missing_identity";
        public const string StandaloneAmbiguousIdentity = "standalone_ambiguous_identity";
        public const string StandaloneOwnerSelf = "standalone_owner_self";
        public const string ThreadedExactRoute = "threaded_exact_route";

        /// <summary>
        /// Resolves only the route's declared equivalent extractor fields. Missing, ambiguous, or
        /// owner-self pawn identity becomes Standalone without consulting another candidate.
        /// </summary>
        public static MemoryRouteResolution Resolve(
            string ownerPawnId,
            MemoryThreadRouteRule route,
            IEnumerable<MemoryRouteCandidate> candidates)
        {
            if (route == null)
            {
                return Standalone(StandaloneNoRoute);
            }

            if (!MemoryContractTokens.IsKnownRootSubjectKind(route.subjectKind)
                || route.equivalentExtractors == null
                || route.equivalentExtractors.Count == 0
                || (route.subjectKind == MemoryContractTokens.SubjectStream
                    && !HasOneExactStreamSubject(route.equivalentExtractors)))
            {
                return Standalone(StandaloneMissingIdentity);
            }

            HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < route.equivalentExtractors.Count; index++)
            {
                MemoryRouteExtractor extractor = route.equivalentExtractors[index];
                if (extractor == null || string.IsNullOrWhiteSpace(extractor.extractorToken)
                    || !declared.Add(extractor.extractorToken))
                {
                    return Standalone(StandaloneAmbiguousIdentity);
                }
            }

            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            List<MemoryRouteCandidate> matching = new List<MemoryRouteCandidate>();
            foreach (MemoryRouteCandidate candidate in candidates
                ?? Enumerable.Empty<MemoryRouteCandidate>())
            {
                if (candidate == null || !declared.Contains(candidate.extractorToken))
                {
                    continue;
                }

                if (!string.Equals(
                        candidate.subjectKind,
                        route.subjectKind,
                        StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(candidate.subjectId)
                    || candidate.subjectId.Length
                        > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                    || !MemoryIdentityCodec.IsWellFormedUtf16(candidate.subjectId)
                    || !MemoryContractTokens.IsValidRootSubject(
                        candidate.subjectKind,
                        candidate.subjectId))
                {
                    return Standalone(StandaloneMissingIdentity);
                }

                string constantStreamSubject;
                if (route.subjectKind == MemoryContractTokens.SubjectStream
                    && (!TryGetKnownStreamSubject(
                            candidate.extractorToken,
                            out constantStreamSubject)
                        || !string.Equals(
                            candidate.subjectId,
                            constantStreamSubject,
                            StringComparison.Ordinal)))
                {
                    return Standalone(StandaloneMissingIdentity);
                }

                string pair = OrdinalSegmentCodec.Segment(candidate.subjectKind)
                    + OrdinalSegmentCodec.Segment(candidate.subjectId);
                distinct.Add(pair);
                matching.Add(candidate);
            }

            if (distinct.Count == 0)
            {
                return Standalone(StandaloneMissingIdentity);
            }

            if (distinct.Count != 1)
            {
                return Standalone(StandaloneAmbiguousIdentity);
            }

            // Equivalent extractors are ordered fallbacks. Candidate collection order is an adapter
            // detail, so choose the first declared extractor that resolved the sole exact identity.
            MemoryRouteCandidate selected = null;
            foreach (MemoryRouteExtractor extractor in route.equivalentExtractors)
            {
                selected = matching
                    .Where(candidate => string.Equals(
                        candidate.extractorToken,
                        extractor.extractorToken,
                        StringComparison.Ordinal))
                    .OrderBy(candidate => candidate.frozenLabel ?? string.Empty, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (selected != null) break;
            }
            if (selected == null) return Standalone(StandaloneMissingIdentity);
            if (route.subjectKind == MemoryContractTokens.SubjectPawn
                && string.Equals(ownerPawnId, selected.subjectId, StringComparison.Ordinal))
            {
                return Standalone(StandaloneOwnerSelf);
            }

            return new MemoryRouteResolution
            {
                isThreaded = true,
                subjectKind = selected.subjectKind,
                subjectId = selected.subjectId,
                frozenLabel = selected.frozenLabel ?? string.Empty,
                reasonToken = ThreadedExactRoute
            };
        }

        /// <summary>Validates one canonical value against its XML fact declaration.</summary>
        public static bool IsValidCanonicalValue(MemoryFactDescriptor descriptor, string value)
        {
            if (descriptor == null
                || string.IsNullOrWhiteSpace(descriptor.factKind)
                || descriptor.factKind.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || !MemoryFactContractTokens.IsMatchingPair(
                    descriptor.aggregationToken,
                    descriptor.canonicalValueKind))
            {
                return false;
            }

            string safe = value ?? string.Empty;
            if (safe.Length > 512 || !MemoryIdentityCodec.IsWellFormedUtf16(safe))
            {
                return false;
            }

            if (descriptor.canonicalValueKind == MemoryFactContractTokens.ValueEmpty)
            {
                return safe.Length == 0;
            }

            if (descriptor.canonicalValueKind == MemoryFactContractTokens.ValueOrdinal)
            {
                return !string.IsNullOrWhiteSpace(safe);
            }

            if (descriptor.canonicalValueKind == MemoryFactContractTokens.ValueInt64)
            {
                long parsed;
                return TryParseCanonicalInt64(safe, out parsed);
            }

            if (descriptor.canonicalValueKind == MemoryFactContractTokens.ValueState)
            {
                return !string.IsNullOrWhiteSpace(safe)
                    && descriptor.allowedStates != null
                    && descriptor.allowedStates.Contains(safe, StringComparer.Ordinal);
            }

            return false;
        }

        /// <summary>Returns an empty string for a valid rule, otherwise one stable diagnostic token.</summary>
        public static string ValidateRuleContract(ImportantEventRule rule)
        {
            if (rule == null) return "memory_contract_missing_rule";
            if (!MemoryContractTokens.IsKnownKind(rule.memoryKind))
                return "memory_contract_invalid_kind";
            if (!MemoryContractTokens.IsKnownCategory(rule.memoryCategory))
                return "memory_contract_invalid_category";
            if (!MemoryContractTokens.IsKnownImportance(rule.baseImportance))
                return "memory_contract_invalid_importance";
            if (string.IsNullOrWhiteSpace(rule.captureSourceToken))
                return "memory_contract_invalid_source";
            if (rule.memoryFacts == null || rule.memoryFacts.Count == 0
                || rule.memoryFacts.Any(fact => fact == null
                    || !MemoryFactContractTokens.IsMatchingPair(
                        fact.aggregationToken,
                        fact.canonicalValueKind)))
                return "memory_contract_invalid_fact";
            if (rule.threadRoute != null && !IsValidThreadRoute(rule.threadRoute))
                return "memory_contract_invalid_route";
            if (rule.promptConsumerIds == null || rule.promptConsumerIds.Count == 0
                || rule.promptConsumerIds.Any(id => MemoryRecallConsumerRegistry.Find(id) == null))
                return "memory_contract_unreachable";
            return string.Empty;
        }

        private static MemoryRouteResolution Standalone(string reason)
        {
            return new MemoryRouteResolution { reasonToken = reason };
        }

        private static bool IsValidThreadRoute(MemoryThreadRouteRule route)
        {
            if (route == null
                || !MemoryContractTokens.IsKnownRootSubjectKind(route.subjectKind)
                || route.equivalentExtractors == null
                || route.equivalentExtractors.Count == 0)
            {
                return false;
            }

            HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
            string declaredStreamSubject = null;
            foreach (MemoryRouteExtractor extractor in route.equivalentExtractors)
            {
                if (extractor == null
                    || string.IsNullOrWhiteSpace(extractor.extractorToken)
                    || !tokens.Add(extractor.extractorToken))
                {
                    return false;
                }

                if (route.subjectKind == MemoryContractTokens.SubjectStream)
                {
                    string streamSubject;
                    if (!TryGetKnownStreamSubject(extractor.extractorToken, out streamSubject)
                        || (declaredStreamSubject != null
                            && !string.Equals(
                                declaredStreamSubject,
                                streamSubject,
                                StringComparison.Ordinal)))
                    {
                        return false;
                    }
                    declaredStreamSubject = streamSubject;
                }
            }
            return true;
        }

        private static bool HasOneExactStreamSubject(
            IEnumerable<MemoryRouteExtractor> extractors)
        {
            string declaredSubject = null;
            foreach (MemoryRouteExtractor extractor in extractors)
            {
                string streamSubject;
                if (extractor == null
                    || !TryGetKnownStreamSubject(extractor.extractorToken, out streamSubject)
                    || (declaredSubject != null
                        && !string.Equals(
                            declaredSubject,
                            streamSubject,
                            StringComparison.Ordinal)))
                {
                    return false;
                }
                declaredSubject = streamSubject;
            }
            return declaredSubject != null;
        }

        private static bool TryGetKnownStreamSubject(
            string extractorToken,
            out string streamSubject)
        {
            const string constantPrefix = "constant:";
            streamSubject = string.Empty;
            if (string.IsNullOrEmpty(extractorToken)
                || !extractorToken.StartsWith(constantPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string candidate = extractorToken.Substring(constantPrefix.Length);
            if (!MemoryContractTokens.IsKnownStreamSubjectToken(candidate)) return false;
            streamSubject = candidate;
            return true;
        }

        /// <summary>Parses only the frozen invariant signed-decimal grammar used by memory facts.</summary>
        internal static bool TryParseCanonicalInt64(string value, out long parsed)
        {
            parsed = 0;
            if (string.IsNullOrEmpty(value)
                || value == "-0"
                || value[0] == '+'
                || (value.Length > 1 && value[0] == '0')
                || (value.Length > 2 && value[0] == '-' && value[1] == '0')
                || !long.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                return false;
            }

            return string.Equals(
                value,
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }
    }

    /// <summary>The one behavior-inert M0 registry of deliberate future memory consumers.</summary>
    internal static class MemoryRecallConsumerRegistry
    {
        public const string OrdinaryDiary = "ordinary_diary";
        public const string ExistingReflection = "existing_reflection";
        public const string NarrativeArc = "narrative_arc";
        public const string Comparison = "comparison";
        public const string Anniversary = "anniversary";
        public const string QuietMemory = "quiet_memory";
        public const string SummaryWording = "summary_wording";

        private static readonly List<MemoryRecallConsumerContract> Entries = CreateEntries();

        /// <summary>Returns a detached copy of every registered deliberate consumer.</summary>
        public static List<MemoryRecallConsumerContract> All()
        {
            return Entries.Select(Copy).ToList();
        }

        /// <summary>Returns one detached registered consumer, or null for an unknown token.</summary>
        public static MemoryRecallConsumerContract Find(string consumerId)
        {
            MemoryRecallConsumerContract found = Entries.FirstOrDefault(entry =>
                string.Equals(entry.consumerId, consumerId, StringComparison.Ordinal));
            return found == null ? null : Copy(found);
        }

        private static List<MemoryRecallConsumerContract> CreateEntries()
        {
            return new List<MemoryRecallConsumerContract>
            {
                Entry(OrdinaryDiary, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "normal_diary", false),
                Entry(ExistingReflection, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "reflection", false),
                Entry(NarrativeArc, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "arc", false),
                Entry(Comparison, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "comparison", false),
                Entry(Anniversary, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "anniversary", false),
                Entry(QuietMemory, true, true, 2, 1, 0, 0,
                    "blockWordingUnits", true, "quiet_memory", true),
                Entry(SummaryWording, false, false, 0, 0, 0, 0,
                    "summaryOptionalLlmWordingUnits", false, "summary_wording", true)
            };
        }

        private static MemoryRecallConsumerContract Entry(
            string id,
            bool standalone,
            bool currentState,
            int full,
            int balanced,
            int compact,
            int off,
            string characterCapDimension,
            bool currentEventExclusion,
            string purpose,
            bool extraRequest)
        {
            return new MemoryRecallConsumerContract
            {
                consumerId = id,
                eligibleSubjectKinds = new List<string>
                {
                    MemoryContractTokens.SubjectPawn,
                    MemoryContractTokens.SubjectFaction,
                    MemoryContractTokens.SubjectStream
                },
                eligibleWritingFormats = full > 0 || balanced > 0
                    ? new List<string> { "Full", "Balanced" }
                    : new List<string>(),
                allowsStandalone = standalone,
                requiresCurrentStateRendering = currentState,
                fullMaximumLines = full,
                balancedMaximumLines = balanced,
                compactMaximumLines = compact,
                offMaximumLines = off,
                characterCapDimensionToken = characterCapDimension,
                requiresOwnerMatch = true,
                requiresEpochMatch = true,
                requiresCategoryEnabled = true,
                honorsSuppression = true,
                excludesCurrentEvent = currentEventExclusion,
                usagePurposeToken = purpose,
                createsExtraProviderRequest = extraRequest,
                appliesCommonExclusionContract = true
            };
        }

        private static MemoryRecallConsumerContract Copy(MemoryRecallConsumerContract source)
        {
            return new MemoryRecallConsumerContract
            {
                consumerId = source.consumerId,
                eligibleSubjectKinds = new List<string>(source.eligibleSubjectKinds),
                eligibleWritingFormats = new List<string>(source.eligibleWritingFormats),
                allowsStandalone = source.allowsStandalone,
                requiresCurrentStateRendering = source.requiresCurrentStateRendering,
                fullMaximumLines = source.fullMaximumLines,
                balancedMaximumLines = source.balancedMaximumLines,
                compactMaximumLines = source.compactMaximumLines,
                offMaximumLines = source.offMaximumLines,
                characterCapDimensionToken = source.characterCapDimensionToken,
                requiresOwnerMatch = source.requiresOwnerMatch,
                requiresEpochMatch = source.requiresEpochMatch,
                requiresCategoryEnabled = source.requiresCategoryEnabled,
                honorsSuppression = source.honorsSuppression,
                excludesCurrentEvent = source.excludesCurrentEvent,
                usagePurposeToken = source.usagePurposeToken,
                createsExtraProviderRequest = source.createsExtraProviderRequest,
                appliesCommonExclusionContract = source.appliesCommonExclusionContract
            };
        }
    }
}
