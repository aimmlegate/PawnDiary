// ImportantMemorySelector.cs — deterministic legacy retrieval plus dormant Recall v2 selection.
//
// The shipped selector stays byte-for-byte behavior-compatible while the unified memory system is
// behind LegacyShadow. Recall v2 accepts only detached owner/epoch/candidate/guard snapshots, applies
// the common consumer registry before ranking, freezes a bounded shortlist, and can later revalidate
// only those frozen IDs. It never queries a store, reads another owner, or uses topic overlap as an
// eligibility door.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable exact-route kinds accepted by the common Recall v2 contract.</summary>
    internal static class MemoryRecallRouteKinds
    {
        public const string Participant = "participant";
        public const string TypedSubject = "typed_subject";
        public const string Root = "root";
        public const string Faction = "faction";
        public const string StateIdentity = "state_identity";
        public const string NarrativeArc = "narrative_arc";
        public const string Comparison = "comparison";
        public const string Anniversary = "anniversary";

        /// <summary>True only for one deliberate exact recall route.</summary>
        public static bool IsKnown(string value)
        {
            return value == Participant || value == TypedSubject || value == Root
                || value == Faction || value == StateIdentity || value == NarrativeArc
                || value == Comparison || value == Anniversary;
        }
    }

    /// <summary>One exact, label-independent route identity on a query or candidate.</summary>
    internal sealed class MemoryRecallRouteIdentity
    {
        public string routeKind = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string routeKey = string.Empty;
    }

    /// <summary>
    /// Detached projection of one current-schema block/thread candidate. Historical wording and
    /// replaceable current-state wording are deliberately separate fields.
    /// </summary>
    internal sealed class MemoryRecallCandidateSnapshot
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string recordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string sourceEventId = string.Empty;
        public string rootId = string.Empty;
        public string chapterOrNoveltyId = string.Empty;
        public string kind = string.Empty;
        public string importance = string.Empty;
        public long originalEventTick;
        public bool available = true;
        public bool identityValid = true;
        public bool promptRouteValid = true;
        public bool ttlEligible = true;
        public bool categoryProjectionValid = true;
        public bool suppressed;
        public bool isThreadMember;
        public bool isCurrentThreadProjection = true;
        public bool directExactEventReference;
        public int narrativeFitScore;
        public string historicalText = string.Empty;
        public bool currentStateApplicable;
        public bool currentStateContradictsHistorical;
        public bool currentStateCanRender = true;
        public string currentStateText = string.Empty;
        public string currentStateSourceId = string.Empty;
        public List<string> categories = new List<string>();
        public List<string> topicKeys = new List<string>();
        public List<string> representedSourceOccurrenceIds = new List<string>();
        public List<MemoryRecallRouteIdentity> exactRoutes = new List<MemoryRecallRouteIdentity>();
        public MemoryRepetitionGuardState recordGuard;
        public List<MemoryGuardIdentity> requiredStructuralGuards =
            new List<MemoryGuardIdentity>();
        public List<MemoryRepetitionGuardState> structuralGuardStates =
            new List<MemoryRepetitionGuardState>();
    }

    /// <summary>Detached writing-purpose and owner snapshot for one Recall v2 selection.</summary>
    internal sealed class MemoryRecallQueryV2
    {
        public string consumerId = MemoryRecallConsumerRegistry.OrdinaryDiary;
        public string writingFormat = MemoryRecallWritingFormats.Full;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string pairCounterpartPawnId = string.Empty;
        public string currentEventId = string.Empty;
        public string currentSourceOccurrenceId = string.Empty;
        public bool useMemoriesInWriting = true;
        public List<string> enabledCategories = new List<string>();
        public List<string> topicKeys = new List<string>();
        public List<string> excludedSourceEventIds = new List<string>();
        public List<string> excludedSourceOccurrenceIds = new List<string>();
        public List<MemoryRecallRouteIdentity> exactRoutes = new List<MemoryRecallRouteIdentity>();
        public MemoryRepetitionPolicySnapshot repetitionPolicy;
    }

    /// <summary>One bounded Recall v2 selector diagnostic row.</summary>
    internal sealed class MemoryRecallCandidateReportV2
    {
        public string recordId = string.Empty;
        public bool selected;
        public bool exactRouteMatched;
        public bool topicMatched;
        public string rejectReason = string.Empty;
    }

    /// <summary>One selected line's exact evidence, guards, and detached wording.</summary>
    internal sealed class MemoryRecallSelectedCandidate
    {
        public MemoryRecallCandidateSnapshot candidate;
        public MemoryEvidenceIdentity evidence;
        public List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>();
        public List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>();
    }

    /// <summary>A bounded Recall v2 shortlist/result for one exact owner and epoch.</summary>
    internal sealed class MemoryRecallSelectionResultV2
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string consumerId = string.Empty;
        public string writingFormat = string.Empty;
        public int lineCap;
        public List<MemoryRecallSelectedCandidate> selected =
            new List<MemoryRecallSelectedCandidate>();
        public List<MemoryRecallCandidateReportV2> report =
            new List<MemoryRecallCandidateReportV2>();
    }

    /// <summary>Private paired-POV result; recipient selection consumes only initiator source IDs.</summary>
    internal sealed class MemoryRecallPairedResultV2
    {
        public MemoryRecallSelectionResultV2 initiator = new MemoryRecallSelectionResultV2();
        public MemoryRecallSelectionResultV2 recipient = new MemoryRecallSelectionResultV2();
    }

    /// <summary>
    /// Behavior-inert selector comparison. Under LegacyShadow the published IDs are always legacy IDs;
    /// V2 IDs exist only for diagnostics and fixtures.
    /// </summary>
    internal sealed class MemoryRecallShadowComparison
    {
        public string buildState = string.Empty;
        public bool differs;
        public bool publishesLegacy;
        public List<string> legacyRecordIds = new List<string>();
        public List<string> recallV2RecordIds = new List<string>();
        public List<string> publishedRecordIds = new List<string>();
    }

    /// <summary>Stable Recall v2 rejection vocabulary for bounded diagnostics.</summary>
    internal static class MemoryRecallRejectReasons
    {
        public const string InvalidQuery = "recall_invalid_query";
        public const string UnknownConsumer = "recall_unknown_consumer";
        public const string FormatDisabled = "recall_format_disabled";
        public const string OwnerMismatch = "recall_owner_mismatch";
        public const string EpochMismatch = "recall_epoch_mismatch";
        public const string CategoryDisabled = "recall_category_disabled";
        public const string Suppressed = "recall_suppressed";
        public const string MissingOrCorrupt = "recall_missing_or_corrupt";
        public const string NoExactRoute = "recall_no_exact_route";
        public const string CurrentEvent = "recall_current_event_or_ancestor";
        public const string RepresentedSource = "recall_represented_source";
        public const string InvalidThreadProjection = "recall_invalid_thread_projection";
        public const string CurrentTruthUnavailable = "recall_current_truth_unavailable";
        public const string GuardBypass = "recall_guard_contract_incomplete";
        public const string FrozenCandidateMissing = "recall_frozen_candidate_missing";
        public const string OverCap = "recall_ranked_below_line_cap";
    }

    /// <summary>Selects contextual records first, then at most one canonical background fallback.</summary>
    internal static class ImportantMemorySelector
    {
        private sealed class RankedCandidate
        {
            public ImportantMemoryRecordSnapshot record;
            public KnowledgeCandidateReport report;
        }

        private sealed class RankedRecallV2Candidate
        {
            public MemoryRecallCandidateSnapshot candidate;
            public MemoryRecallCandidateReportV2 report;
            public MemoryRepetitionGuardEvaluation guardEvaluation;
        }

        /// <summary>
        /// Builds one owner-private V2 shortlist. Exact route, ownership, exclusion, current-truth,
        /// thread-projection, and every repetition guard pass before deterministic ranking.
        /// </summary>
        public static MemoryRecallSelectionResultV2 SelectV2(
            MemoryRecallQueryV2 query,
            List<MemoryRecallCandidateSnapshot> candidates)
        {
            MemoryRecallSelectionResultV2 result = EmptyV2(query);
            MemoryRecallConsumerContract consumer = query == null
                ? null
                : MemoryRecallConsumerRegistry.Find(query.consumerId);
            string queryFailure = QueryFailure(query, consumer);
            if (queryFailure.Length > 0)
            {
                AddRejectedReports(result, candidates, queryFailure);
                return result;
            }

            result.lineCap = ConsumerLineCap(consumer, query.writingFormat);
            if (result.lineCap <= 0)
            {
                AddRejectedReports(result, candidates, MemoryRecallRejectReasons.FormatDisabled);
                return result;
            }

            List<RankedRecallV2Candidate> eligible = new List<RankedRecallV2Candidate>();
            HashSet<string> duplicateOwnerRecordIds = FindDuplicateOwnerRecordIds(
                query,
                candidates);
            for (int index = 0; candidates != null && index < candidates.Count; index++)
            {
                MemoryRecallCandidateSnapshot candidate = candidates[index];
                MemoryRecallCandidateReportV2 report = new MemoryRecallCandidateReportV2
                {
                    recordId = candidate?.recordId ?? string.Empty
                };
                result.report.Add(report);
                MemoryRepetitionGuardEvaluation guardEvaluation;
                string failure;
                if (candidate != null
                    && duplicateOwnerRecordIds.Contains(candidate.recordId))
                {
                    // A detached owner snapshot that assigns one record ID to multiple rows is
                    // ambiguous. Neither row may win by rank or enumeration order.
                    guardEvaluation = new MemoryRepetitionGuardEvaluation();
                    failure = MemoryRecallRejectReasons.MissingOrCorrupt;
                }
                else
                {
                    failure = CandidateFailure(
                        query, consumer, candidate, report, out guardEvaluation);
                }
                if (failure.Length > 0)
                {
                    report.rejectReason = failure;
                    continue;
                }

                eligible.Add(new RankedRecallV2Candidate
                {
                    candidate = candidate,
                    report = report,
                    guardEvaluation = guardEvaluation
                });
            }

            eligible.Sort(CompareRecallV2Candidates);
            HashSet<string> selectedSources = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < eligible.Count; index++)
            {
                RankedRecallV2Candidate ranked = eligible[index];
                if (SourcesOverlap(ranked.candidate, selectedSources))
                {
                    ranked.report.rejectReason = MemoryRecallRejectReasons.RepresentedSource;
                    continue;
                }

                if (result.selected.Count >= result.lineCap)
                {
                    ranked.report.rejectReason = MemoryRecallRejectReasons.OverCap;
                    continue;
                }

                ranked.report.selected = true;
                MemoryRecallSelectedCandidate selected = BuildSelected(
                    ranked.candidate,
                    ranked.guardEvaluation,
                    result.selected.Count);
                result.selected.Add(selected);
                AddCandidateSources(ranked.candidate, selectedSources);
            }

            return result;
        }

        /// <summary>
        /// Revalidates only a previously selected bounded shortlist. Missing or newly ineligible rows
        /// are omitted in place; lower-ranked store candidates are never queried or substituted.
        /// Frozen facts/source IDs/routes remain frozen, while current wording/status/guards are refreshed.
        /// </summary>
        public static MemoryRecallSelectionResultV2 RevalidateFrozenV2(
            MemoryRecallSelectionResultV2 frozen,
            MemoryRecallQueryV2 currentQuery,
            List<MemoryRecallCandidateSnapshot> currentOwnerCandidates)
        {
            MemoryRecallSelectionResultV2 result = EmptyV2(currentQuery);
            MemoryRecallConsumerContract consumer = currentQuery == null
                ? null
                : MemoryRecallConsumerRegistry.Find(currentQuery.consumerId);
            string queryFailure = QueryFailure(currentQuery, consumer);
            bool frozenEnvelopeMatches = frozen != null
                && frozen.selected != null
                && string.Equals(
                    frozen.ownerPawnId,
                    currentQuery?.ownerPawnId,
                    StringComparison.Ordinal)
                && string.Equals(
                    frozen.ownerEpochToken,
                    currentQuery?.ownerEpochToken,
                    StringComparison.Ordinal)
                && string.Equals(
                    frozen.consumerId,
                    currentQuery?.consumerId,
                    StringComparison.Ordinal);
            if (queryFailure.Length > 0 || !frozenEnvelopeMatches)
            {
                AddRejectedFrozenReports(result, frozen,
                    queryFailure.Length == 0 ? MemoryRecallRejectReasons.InvalidQuery : queryFailure);
                return result;
            }

            result.lineCap = ConsumerLineCap(consumer, currentQuery.writingFormat);
            HashSet<string> selectedSources = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < frozen.selected.Count && index < result.lineCap; index++)
            {
                MemoryRecallSelectedCandidate frozenSelected = frozen.selected[index];
                MemoryRecallCandidateSnapshot frozenCandidate = frozenSelected?.candidate;
                MemoryRecallCandidateReportV2 report = new MemoryRecallCandidateReportV2
                {
                    recordId = frozenCandidate?.recordId ?? string.Empty
                };
                result.report.Add(report);
                if (frozenCandidate == null)
                {
                    report.rejectReason = MemoryRecallRejectReasons.FrozenCandidateMissing;
                    continue;
                }
                if (!string.Equals(
                    frozenCandidate.ownerPawnId,
                    currentQuery.ownerPawnId,
                    StringComparison.Ordinal))
                {
                    report.rejectReason = MemoryRecallRejectReasons.OwnerMismatch;
                    continue;
                }
                if (!string.Equals(
                    frozenCandidate.ownerEpochToken,
                    currentQuery.ownerEpochToken,
                    StringComparison.Ordinal))
                {
                    report.rejectReason = MemoryRecallRejectReasons.EpochMismatch;
                    continue;
                }
                MemoryRecallCandidateSnapshot current = FindUniqueCurrent(
                    currentOwnerCandidates,
                    frozenCandidate.recordId,
                    currentQuery.ownerPawnId,
                    currentQuery.ownerEpochToken);
                if (current == null
                    || !string.Equals(
                        current.sourceOccurrenceId,
                        frozenCandidate.sourceOccurrenceId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        current.rootId,
                        frozenCandidate.rootId,
                        StringComparison.Ordinal))
                {
                    report.rejectReason = MemoryRecallRejectReasons.FrozenCandidateMissing;
                    continue;
                }

                MemoryRecallCandidateSnapshot merged = MergeFrozenWithCurrent(frozenCandidate, current);
                MemoryRepetitionGuardEvaluation guardEvaluation;
                string failure = CandidateFailure(
                    currentQuery, consumer, merged, report, out guardEvaluation);
                if (failure.Length == 0 && SourcesOverlap(merged, selectedSources))
                    failure = MemoryRecallRejectReasons.RepresentedSource;
                if (failure.Length > 0)
                {
                    report.rejectReason = failure;
                    continue;
                }

                report.selected = true;
                result.selected.Add(BuildSelected(merged, guardEvaluation, result.selected.Count));
                AddCandidateSources(merged, selectedSources);
            }

            return result;
        }

        /// <summary>
        /// Selects paired POVs without sharing candidates. The recipient receives only the initiator's
        /// selected sourceOccurrenceIds as exclusions, then reads its own supplied owner snapshot.
        /// </summary>
        public static MemoryRecallPairedResultV2 SelectPairedV2(
            MemoryRecallQueryV2 initiatorQuery,
            List<MemoryRecallCandidateSnapshot> initiatorCandidates,
            MemoryRecallQueryV2 recipientQuery,
            List<MemoryRecallCandidateSnapshot> recipientCandidates)
        {
            MemoryRecallPairedResultV2 result = new MemoryRecallPairedResultV2();
            if (initiatorQuery == null || recipientQuery == null
                || string.IsNullOrWhiteSpace(initiatorQuery.ownerPawnId)
                || string.IsNullOrWhiteSpace(recipientQuery.ownerPawnId)
                || string.Equals(
                    initiatorQuery.ownerPawnId,
                    recipientQuery.ownerPawnId,
                    StringComparison.Ordinal))
            {
                result.initiator = EmptyV2(initiatorQuery);
                AddRejectedReports(
                    result.initiator,
                    initiatorCandidates,
                    MemoryRecallRejectReasons.InvalidQuery);
                result.recipient = EmptyV2(recipientQuery);
                AddRejectedReports(
                    result.recipient,
                    recipientCandidates,
                    MemoryRecallRejectReasons.InvalidQuery);
                return result;
            }

            MemoryRecallQueryV2 privateInitiator = CopyQuery(initiatorQuery);
            privateInitiator.pairCounterpartPawnId = recipientQuery.ownerPawnId;
            result.initiator = SelectV2(privateInitiator, initiatorCandidates);
            MemoryRecallQueryV2 privateRecipient = CopyQuery(recipientQuery);
            privateRecipient.pairCounterpartPawnId = initiatorQuery.ownerPawnId;
            for (int index = 0; index < result.initiator.selected.Count; index++)
            {
                AddCandidateSources(
                    result.initiator.selected[index].candidate,
                    privateRecipient.excludedSourceOccurrenceIds);
            }
            result.recipient = SelectV2(privateRecipient, recipientCandidates);
            return result;
        }

        /// <summary>
        /// Compares legacy and V2 IDs while publishing only the activation-gate-selected side. M0–M10
        /// therefore exercise diagnostics without exposing partial V2 behavior.
        /// </summary>
        public static MemoryRecallShadowComparison CompareLegacyAndV2(
            KnowledgeSelectionResult legacy,
            MemoryRecallSelectionResultV2 recallV2)
        {
            MemoryRecallShadowComparison comparison = new MemoryRecallShadowComparison
            {
                buildState = MemorySystemActivationGate.BuildState,
                publishesLegacy = !MemorySystemActivationGate.IsCurrentRelease
            };
            for (int index = 0; legacy?.selected != null && index < legacy.selected.Count; index++)
            {
                comparison.legacyRecordIds.Add(legacy.selected[index]?.recordId ?? string.Empty);
            }
            for (int index = 0; recallV2?.selected != null && index < recallV2.selected.Count; index++)
            {
                comparison.recallV2RecordIds.Add(
                    recallV2.selected[index]?.candidate?.recordId ?? string.Empty);
            }
            comparison.differs = !SameOrdinalSequence(
                comparison.legacyRecordIds,
                comparison.recallV2RecordIds);
            comparison.publishedRecordIds.AddRange(comparison.publishesLegacy
                ? comparison.legacyRecordIds
                : comparison.recallV2RecordIds);
            return comparison;
        }

        private static MemoryRecallSelectionResultV2 EmptyV2(MemoryRecallQueryV2 query)
        {
            return new MemoryRecallSelectionResultV2
            {
                ownerPawnId = query?.ownerPawnId ?? string.Empty,
                ownerEpochToken = query?.ownerEpochToken ?? string.Empty,
                consumerId = query?.consumerId ?? string.Empty,
                writingFormat = query?.writingFormat ?? string.Empty
            };
        }

        private static string QueryFailure(
            MemoryRecallQueryV2 query,
            MemoryRecallConsumerContract consumer)
        {
            if (query == null
                || !ValidRawIdentity(query.ownerPawnId)
                || !ValidCompositeIdentity(query.ownerEpochToken)
                || !MemoryRecallWritingFormats.IsKnown(query.writingFormat))
            {
                return MemoryRecallRejectReasons.InvalidQuery;
            }

            if (consumer == null
                || !consumer.appliesCommonExclusionContract
                || !consumer.requiresOwnerMatch
                || !consumer.requiresEpochMatch
                || !consumer.requiresCategoryEnabled
                || !consumer.honorsSuppression)
            {
                return MemoryRecallRejectReasons.UnknownConsumer;
            }

            if ((consumer.excludesCurrentEvent
                    && !ValidCompositeIdentity(query.currentSourceOccurrenceId))
                || !ValidOptionalCompositeIdentity(query.currentEventId)
                || (!string.IsNullOrEmpty(query.pairCounterpartPawnId)
                    && (!ValidRawIdentity(query.pairCounterpartPawnId)
                        || string.Equals(
                            query.ownerPawnId,
                            query.pairCounterpartPawnId,
                            StringComparison.Ordinal)))
                || !ValidCompositeIdentityList(query.excludedSourceEventIds)
                || !ValidCompositeIdentityList(query.excludedSourceOccurrenceIds)
                || !ValidEnabledCategoryList(query.enabledCategories)
                || !ValidRouteList(query.exactRoutes, consumer))
            {
                // A consumer that promises the common self/ancestor exclusion cannot silently
                // proceed without the current occurrence identity. Optional ancestor lists may be
                // empty, but malformed or duplicate identity/route rows make the detached contract
                // incomplete.
                return MemoryRecallRejectReasons.InvalidQuery;
            }

            return query.useMemoriesInWriting
                ? string.Empty
                : MemoryRecallRejectReasons.FormatDisabled;
        }

        private static int ConsumerLineCap(
            MemoryRecallConsumerContract consumer,
            string writingFormat)
        {
            if (consumer == null) return 0;
            switch (writingFormat)
            {
                case MemoryRecallWritingFormats.Full:
                    return Math.Max(0, Math.Min(2, consumer.fullMaximumLines));
                case MemoryRecallWritingFormats.Balanced:
                    return Math.Max(0, Math.Min(1, consumer.balancedMaximumLines));
                case MemoryRecallWritingFormats.Compact:
                    return 0;
                default:
                    return 0;
            }
        }

        private static string CandidateFailure(
            MemoryRecallQueryV2 query,
            MemoryRecallConsumerContract consumer,
            MemoryRecallCandidateSnapshot candidate,
            MemoryRecallCandidateReportV2 report,
            out MemoryRepetitionGuardEvaluation guardEvaluation)
        {
            guardEvaluation = new MemoryRepetitionGuardEvaluation();
            if (candidate == null) return MemoryRecallRejectReasons.MissingOrCorrupt;

            // Ownership is the first content-independent candidate check. A mixed or malicious
            // adapter list therefore cannot make another owner's wording/guards influence even the
            // rejection path, and frozen revalidation applies the same privacy boundary.
            if (!string.Equals(
                candidate.ownerPawnId,
                query.ownerPawnId,
                StringComparison.Ordinal))
            {
                return MemoryRecallRejectReasons.OwnerMismatch;
            }

            if (!string.Equals(
                candidate.ownerEpochToken,
                query.ownerEpochToken,
                StringComparison.Ordinal))
            {
                return MemoryRecallRejectReasons.EpochMismatch;
            }

            if (!candidate.available
                || !candidate.identityValid
                || !candidate.promptRouteValid
                || !candidate.ttlEligible
                || !candidate.categoryProjectionValid
                || !ValidCompositeIdentity(candidate.recordId)
                || !ValidCompositeIdentity(candidate.sourceOccurrenceId)
                || !ValidOptionalCompositeIdentity(candidate.sourceEventId)
                || !ValidOptionalCompositeIdentity(candidate.rootId)
                || !ValidOptionalCompositeIdentity(candidate.chapterOrNoveltyId)
                || !MemoryContractTokens.IsKnownKind(candidate.kind)
                || !MemoryContractTokens.IsKnownImportance(candidate.importance)
                || string.IsNullOrWhiteSpace(candidate.historicalText)
                || !CandidateSourcesValid(candidate)
                || !ValidRouteList(candidate.exactRoutes, consumer))
            {
                return MemoryRecallRejectReasons.MissingOrCorrupt;
            }

            if (candidate.suppressed) return MemoryRecallRejectReasons.Suppressed;
            if (!HasEnabledCategory(candidate.categories, query.enabledCategories))
                return MemoryRecallRejectReasons.CategoryDisabled;

            if ((!string.IsNullOrWhiteSpace(candidate.sourceEventId)
                    && (string.Equals(
                            candidate.sourceEventId,
                            query.currentEventId,
                            StringComparison.Ordinal)
                        || ContainsOrdinal(
                            query.excludedSourceEventIds,
                            candidate.sourceEventId)))
                || CandidateHasSource(candidate, query.currentSourceOccurrenceId)
                || CandidateHasAnySource(
                    candidate,
                    query.excludedSourceOccurrenceIds))
            {
                return MemoryRecallRejectReasons.CurrentEvent;
            }

            if (candidate.isThreadMember
                && (!ValidCompositeIdentity(candidate.rootId)
                    || (!candidate.isCurrentThreadProjection
                        && !DirectLandmarkDoor(candidate, consumer))))
            {
                return MemoryRecallRejectReasons.InvalidThreadProjection;
            }
            if (!candidate.isThreadMember
                && !string.IsNullOrWhiteSpace(candidate.rootId))
            {
                return MemoryRecallRejectReasons.MissingOrCorrupt;
            }
            if (!candidate.isThreadMember && !consumer.allowsStandalone)
                return MemoryRecallRejectReasons.NoExactRoute;

            report.exactRouteMatched = HasExactRoute(query, candidate, consumer);
            report.topicMatched = SharesOrdinal(query.topicKeys, candidate.topicKeys);
            if (!report.exactRouteMatched) return MemoryRecallRejectReasons.NoExactRoute;

            bool hasCurrentStateText = !string.IsNullOrWhiteSpace(candidate.currentStateText);
            bool hasCurrentStateSource = !string.IsNullOrWhiteSpace(candidate.currentStateSourceId);
            bool currentStateMissing = !candidate.currentStateApplicable
                || !candidate.currentStateCanRender
                || !hasCurrentStateText
                || !hasCurrentStateSource;
            if ((!candidate.currentStateApplicable
                    && (hasCurrentStateText || hasCurrentStateSource))
                || (hasCurrentStateSource
                    && !ValidCompositeIdentity(candidate.currentStateSourceId))
                || (candidate.currentStateContradictsHistorical && currentStateMissing)
                || (candidate.currentStateApplicable
                    && consumer.requiresCurrentStateRendering
                    && currentStateMissing))
            {
                return MemoryRecallRejectReasons.CurrentTruthUnavailable;
            }

            if (!GuardContractComplete(query, candidate))
                return MemoryRecallRejectReasons.GuardBypass;
            guardEvaluation = MemoryRepetitionGuardPolicy.Evaluate(
                query.ownerEpochToken,
                candidate.recordGuard,
                candidate.structuralGuardStates,
                query.repetitionPolicy);
            if (!guardEvaluation.passes)
            {
                return string.IsNullOrWhiteSpace(guardEvaluation.rejectReason)
                    ? MemoryRecallRejectReasons.GuardBypass
                    : guardEvaluation.rejectReason;
            }
            if (!EveryRequiredGuardReturned(
                candidate.requiredStructuralGuards,
                guardEvaluation.guardEntries))
            {
                return MemoryRecallRejectReasons.GuardBypass;
            }

            return string.Empty;
        }

        private static bool HasExactRoute(
            MemoryRecallQueryV2 query,
            MemoryRecallCandidateSnapshot candidate,
            MemoryRecallConsumerContract consumer)
        {
            if (query.exactRoutes == null || candidate.exactRoutes == null) return false;
            for (int queryIndex = 0; queryIndex < query.exactRoutes.Count; queryIndex++)
            {
                MemoryRecallRouteIdentity queryRoute = query.exactRoutes[queryIndex];
                if (!ValidRoute(queryRoute, consumer)
                    || IsOwnerSelfPawnRoute(queryRoute, query.ownerPawnId)) continue;
                for (int candidateIndex = 0;
                    candidateIndex < candidate.exactRoutes.Count;
                    candidateIndex++)
                {
                    MemoryRecallRouteIdentity candidateRoute =
                        candidate.exactRoutes[candidateIndex];
                    if (ValidRoute(candidateRoute, consumer)
                        && !IsOwnerSelfPawnRoute(candidateRoute, query.ownerPawnId)
                        && RouteAllowedForConsumer(candidateRoute.routeKind, consumer.consumerId)
                        && SameRoute(queryRoute, candidateRoute))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool ValidRoute(
            MemoryRecallRouteIdentity route,
            MemoryRecallConsumerContract consumer)
        {
            if (route == null
                || !MemoryRecallRouteKinds.IsKnown(route.routeKind)
                || !ValidCompositeIdentity(route.routeKey))
            {
                return false;
            }

            bool hasSubject = !string.IsNullOrEmpty(route.subjectKind)
                || !string.IsNullOrEmpty(route.subjectId);
            if (!hasSubject) return !RouteRequiresSubject(route.routeKind);
            return ValidRawIdentity(route.subjectKind)
                && ValidCompositeIdentity(route.subjectId)
                && MemoryContractTokens.IsValidRootSubject(
                    route.subjectKind,
                    route.subjectId)
                && consumer?.eligibleSubjectKinds != null
                && ContainsOrdinal(consumer.eligibleSubjectKinds, route.subjectKind);
        }

        private static bool ValidRouteList(
            List<MemoryRecallRouteIdentity> routes,
            MemoryRecallConsumerContract consumer)
        {
            if (routes == null || routes.Count == 0) return false;
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < routes.Count; index++)
            {
                MemoryRecallRouteIdentity route = routes[index];
                if (!ValidRoute(route, consumer)
                    || !RouteAllowedForConsumer(route.routeKind, consumer?.consumerId)
                    || !distinct.Add(RouteTuple(route)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool RouteRequiresSubject(string routeKind)
        {
            return routeKind == MemoryRecallRouteKinds.Participant
                || routeKind == MemoryRecallRouteKinds.TypedSubject
                || routeKind == MemoryRecallRouteKinds.Root
                || routeKind == MemoryRecallRouteKinds.Faction
                || routeKind == MemoryRecallRouteKinds.StateIdentity;
        }

        private static bool IsOwnerSelfPawnRoute(
            MemoryRecallRouteIdentity route,
            string ownerPawnId)
        {
            return route != null
                && route.subjectKind == MemoryContractTokens.SubjectPawn
                && string.Equals(route.subjectId, ownerPawnId, StringComparison.Ordinal);
        }

        private static bool RouteAllowedForConsumer(string routeKind, string consumerId)
        {
            if (routeKind == MemoryRecallRouteKinds.Comparison)
                return consumerId == MemoryRecallConsumerRegistry.Comparison;
            if (routeKind == MemoryRecallRouteKinds.Anniversary)
                return consumerId == MemoryRecallConsumerRegistry.Anniversary;
            if (routeKind == MemoryRecallRouteKinds.NarrativeArc)
                return consumerId == MemoryRecallConsumerRegistry.NarrativeArc;
            return true;
        }

        private static bool DirectLandmarkDoor(
            MemoryRecallCandidateSnapshot candidate,
            MemoryRecallConsumerContract consumer)
        {
            if (candidate.kind != MemoryContractTokens.KindLandmark) return false;
            return candidate.directExactEventReference
                || consumer.consumerId == MemoryRecallConsumerRegistry.Comparison
                || consumer.consumerId == MemoryRecallConsumerRegistry.ExistingReflection
                || consumer.consumerId == MemoryRecallConsumerRegistry.Anniversary;
        }

        private static bool GuardContractComplete(
            MemoryRecallQueryV2 query,
            MemoryRecallCandidateSnapshot candidate)
        {
            if (candidate.recordGuard == null
                || candidate.recordGuard.guardKind != MemoryRepetitionGuardKinds.Record
                || !string.Equals(
                    candidate.recordGuard.guardKey,
                    MemoryRepetitionGuardPolicy.RecordKey(candidate.recordId),
                    StringComparison.Ordinal)
                || candidate.requiredStructuralGuards == null
                || candidate.structuralGuardStates == null)
            {
                return false;
            }

            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(candidate.rootId))
            {
                expected.Add(GuardTuple(
                    MemoryRepetitionGuardKinds.Root,
                    MemoryRepetitionGuardPolicy.RootKey(candidate.rootId)));
            }
            if (!string.IsNullOrEmpty(candidate.chapterOrNoveltyId))
            {
                if (!ValidCompositeIdentity(candidate.rootId)) return false;
                expected.Add(GuardTuple(
                    MemoryRepetitionGuardKinds.Novelty,
                    MemoryRepetitionGuardPolicy.NoveltyKey(
                        candidate.rootId,
                        candidate.chapterOrNoveltyId)));
            }
            if (!string.IsNullOrEmpty(query.pairCounterpartPawnId))
            {
                string pairKey = MemoryRepetitionGuardPolicy.PairKey(
                    query.ownerPawnId,
                    query.pairCounterpartPawnId);
                if (pairKey.Length == 0) return false;
                expected.Add(GuardTuple(MemoryRepetitionGuardKinds.Pair, pairKey));
            }
            for (int routeIndex = 0; routeIndex < candidate.exactRoutes.Count; routeIndex++)
            {
                MemoryRecallRouteIdentity route = candidate.exactRoutes[routeIndex];
                if (route == null || string.IsNullOrEmpty(route.subjectKind)) continue;
                string subjectKey = MemoryRepetitionGuardPolicy.SubjectKey(
                    route.subjectKind,
                    route.subjectId);
                if (subjectKey.Length == 0) return false;
                expected.Add(GuardTuple(MemoryRepetitionGuardKinds.Subject, subjectKey));
            }

            HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < candidate.requiredStructuralGuards.Count; index++)
            {
                MemoryGuardIdentity guard = candidate.requiredStructuralGuards[index];
                if (guard == null
                    || !MemoryRepetitionGuardKinds.IsSavedRowKind(guard.guardKind)
                    || !ValidCompositeIdentity(guard.guardKey)
                    || !required.Add(GuardTuple(guard.guardKind, guard.guardKey)))
                {
                    return false;
                }
            }
            if (!required.SetEquals(expected)) return false;

            HashSet<string> suppliedStates = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < candidate.structuralGuardStates.Count; index++)
            {
                MemoryRepetitionGuardState state = candidate.structuralGuardStates[index];
                if (state == null
                    || !suppliedStates.Add(GuardTuple(state.guardKind, state.guardKey)))
                {
                    return false;
                }
            }
            return suppliedStates.SetEquals(expected);
        }

        private static bool EveryRequiredGuardReturned(
            List<MemoryGuardIdentity> required,
            List<MemoryGuardIdentity> actual)
        {
            for (int requiredIndex = 0; required != null && requiredIndex < required.Count; requiredIndex++)
            {
                bool found = false;
                for (int actualIndex = 0; actual != null && actualIndex < actual.Count; actualIndex++)
                {
                    if (SameGuard(required[requiredIndex], actual[actualIndex]))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        private static MemoryRecallSelectedCandidate BuildSelected(
            MemoryRecallCandidateSnapshot source,
            MemoryRepetitionGuardEvaluation guardEvaluation,
            int lineOrdinal)
        {
            MemoryRecallCandidateSnapshot candidate = CopyCandidate(source);
            MemoryRecallSelectedCandidate selected = new MemoryRecallSelectedCandidate
            {
                candidate = candidate,
                evidence = new MemoryEvidenceIdentity
                {
                    recordId = candidate.recordId,
                    sourceOccurrenceId = candidate.sourceOccurrenceId,
                    rootIdOrEmpty = candidate.rootId
                }
            };
            for (int index = 0; index < guardEvaluation.guardEntries.Count; index++)
            {
                MemoryGuardIdentity guard = guardEvaluation.guardEntries[index];
                selected.guards.Add(new MemoryGuardIdentity
                {
                    guardKind = guard.guardKind,
                    guardKey = guard.guardKey
                });
            }
            selected.diagnostics.Add(new MemoryDiagnosticIdentity
            {
                provenanceKindToken = MemoryRecallDiagnosticKinds.EpisodicMemory,
                sourceId = candidate.sourceOccurrenceId,
                recordIdOrEmpty = candidate.recordId,
                sourceOccurrenceIdOrEmpty = candidate.sourceOccurrenceId,
                rootIdOrEmpty = candidate.rootId,
                lineOrdinal = lineOrdinal
            });
            // Mirrors the renderer's own condition: an unrenderable current state is never planned as
            // provenance, so a line's evidence rows can never promise text the renderer will omit.
            if (candidate.currentStateApplicable
                && candidate.currentStateCanRender
                && !string.IsNullOrWhiteSpace(candidate.currentStateText)
                && !string.IsNullOrWhiteSpace(candidate.currentStateSourceId))
            {
                selected.diagnostics.Add(new MemoryDiagnosticIdentity
                {
                    provenanceKindToken = MemoryRecallDiagnosticKinds.CurrentState,
                    sourceId = candidate.currentStateSourceId,
                    recordIdOrEmpty = candidate.recordId,
                    sourceOccurrenceIdOrEmpty = candidate.sourceOccurrenceId,
                    rootIdOrEmpty = candidate.rootId,
                    lineOrdinal = lineOrdinal
                });
            }
            return selected;
        }

        private static int CompareRecallV2Candidates(
            RankedRecallV2Candidate left,
            RankedRecallV2Candidate right)
        {
            int importance = ImportanceRank(right.candidate.importance)
                .CompareTo(ImportanceRank(left.candidate.importance));
            if (importance != 0) return importance;
            int fit = right.candidate.narrativeFitScore.CompareTo(left.candidate.narrativeFitScore);
            if (fit != 0) return fit;
            int topic = right.report.topicMatched.CompareTo(left.report.topicMatched);
            if (topic != 0) return topic;
            int tick = right.candidate.originalEventTick.CompareTo(left.candidate.originalEventTick);
            return tick != 0
                ? tick
                : string.CompareOrdinal(left.candidate.recordId, right.candidate.recordId);
        }

        private static int ImportanceRank(string importance)
        {
            if (importance == MemoryContractTokens.ImportanceImportant) return 3;
            if (importance == MemoryContractTokens.ImportanceRegular) return 2;
            return 1;
        }

        private static bool SourcesOverlap(
            MemoryRecallCandidateSnapshot candidate,
            HashSet<string> selectedSources)
        {
            if (selectedSources.Contains(candidate.sourceOccurrenceId)) return true;
            for (int index = 0;
                candidate.representedSourceOccurrenceIds != null
                && index < candidate.representedSourceOccurrenceIds.Count;
                index++)
            {
                if (selectedSources.Contains(candidate.representedSourceOccurrenceIds[index]))
                    return true;
            }
            return false;
        }

        private static bool CandidateHasSource(
            MemoryRecallCandidateSnapshot candidate,
            string sourceOccurrenceId)
        {
            if (string.IsNullOrWhiteSpace(sourceOccurrenceId)) return false;
            if (string.Equals(
                candidate.sourceOccurrenceId,
                sourceOccurrenceId,
                StringComparison.Ordinal)) return true;
            return ContainsOrdinal(
                candidate.representedSourceOccurrenceIds,
                sourceOccurrenceId);
        }

        private static bool CandidateHasAnySource(
            MemoryRecallCandidateSnapshot candidate,
            List<string> excluded)
        {
            if (excluded == null) return false;
            for (int index = 0; index < excluded.Count; index++)
            {
                if (CandidateHasSource(candidate, excluded[index])) return true;
            }
            return false;
        }

        private static bool CandidateSourcesValid(MemoryRecallCandidateSnapshot candidate)
        {
            // Represented sources drive a hard exclusion, so an absent list is "we do not know what
            // this row stands for" — not "it stands for nothing". Accepting it as empty would let a
            // Summary share a prompt with one of its own facts, so a missing list fails closed.
            if (candidate.representedSourceOccurrenceIds == null) return false;

            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal)
            {
                candidate.sourceOccurrenceId
            };
            for (int index = 0;
                index < candidate.representedSourceOccurrenceIds.Count;
                index++)
            {
                string source = candidate.representedSourceOccurrenceIds[index];
                if (!ValidCompositeIdentity(source) || !distinct.Add(source)) return false;
            }
            return true;
        }

        private static void AddCandidateSources(
            MemoryRecallCandidateSnapshot candidate,
            HashSet<string> target)
        {
            target.Add(candidate.sourceOccurrenceId);
            for (int index = 0;
                candidate.representedSourceOccurrenceIds != null
                && index < candidate.representedSourceOccurrenceIds.Count;
                index++)
            {
                target.Add(candidate.representedSourceOccurrenceIds[index]);
            }
        }

        private static void AddCandidateSources(
            MemoryRecallCandidateSnapshot candidate,
            List<string> target)
        {
            AddUniqueOrdinal(target, candidate.sourceOccurrenceId);
            for (int index = 0;
                candidate.representedSourceOccurrenceIds != null
                && index < candidate.representedSourceOccurrenceIds.Count;
                index++)
            {
                AddUniqueOrdinal(target, candidate.representedSourceOccurrenceIds[index]);
            }
        }

        private static bool HasEnabledCategory(
            List<string> candidateCategories,
            List<string> enabledCategories)
        {
            bool enabled = false;
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                candidateCategories != null && index < candidateCategories.Count;
                index++)
            {
                string category = candidateCategories[index];
                if (!MemoryContractTokens.IsKnownCategory(category)
                    || !distinct.Add(category)) return false;
                if (ContainsOrdinal(enabledCategories, category)) enabled = true;
            }
            return distinct.Count > 0 && enabled;
        }

        private static MemoryRecallCandidateSnapshot FindUniqueCurrent(
            List<MemoryRecallCandidateSnapshot> candidates,
            string recordId,
            string ownerPawnId,
            string ownerEpochToken)
        {
            MemoryRecallCandidateSnapshot found = null;
            for (int index = 0; candidates != null && index < candidates.Count; index++)
            {
                MemoryRecallCandidateSnapshot candidate = candidates[index];
                if (candidate == null
                    || !string.Equals(
                        candidate.ownerPawnId,
                        ownerPawnId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        candidate.ownerEpochToken,
                        ownerEpochToken,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        candidate.recordId,
                        recordId,
                        StringComparison.Ordinal)) continue;
                if (found != null) return null;
                found = candidate;
            }
            return found;
        }

        private static HashSet<string> FindDuplicateOwnerRecordIds(
            MemoryRecallQueryV2 query,
            List<MemoryRecallCandidateSnapshot> candidates)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> duplicates = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; candidates != null && index < candidates.Count; index++)
            {
                MemoryRecallCandidateSnapshot candidate = candidates[index];
                if (candidate == null
                    || !string.Equals(
                        candidate.ownerPawnId,
                        query.ownerPawnId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        candidate.ownerEpochToken,
                        query.ownerEpochToken,
                        StringComparison.Ordinal)
                    || !ValidCompositeIdentity(candidate.recordId))
                {
                    continue;
                }
                if (!seen.Add(candidate.recordId)) duplicates.Add(candidate.recordId);
            }
            return duplicates;
        }

        private static MemoryRecallCandidateSnapshot MergeFrozenWithCurrent(
            MemoryRecallCandidateSnapshot frozen,
            MemoryRecallCandidateSnapshot current)
        {
            MemoryRecallCandidateSnapshot merged = CopyCandidate(frozen);
            merged.ownerPawnId = current.ownerPawnId;
            merged.ownerEpochToken = current.ownerEpochToken;
            merged.available = current.available;
            merged.identityValid = current.identityValid;
            merged.promptRouteValid = current.promptRouteValid;
            merged.ttlEligible = current.ttlEligible;
            merged.categoryProjectionValid = current.categoryProjectionValid;
            merged.suppressed = current.suppressed;
            merged.historicalText = current.historicalText;
            merged.currentStateApplicable = current.currentStateApplicable;
            merged.currentStateContradictsHistorical = current.currentStateContradictsHistorical;
            merged.currentStateCanRender = current.currentStateCanRender;
            merged.currentStateText = current.currentStateText;
            merged.currentStateSourceId = current.currentStateSourceId;
            merged.recordGuard = CopyGuardState(current.recordGuard);
            merged.structuralGuardStates = CopyGuardStates(current.structuralGuardStates);
            return merged;
        }

        private static MemoryRecallCandidateSnapshot CopyCandidate(
            MemoryRecallCandidateSnapshot source)
        {
            MemoryRecallCandidateSnapshot copy = new MemoryRecallCandidateSnapshot
            {
                ownerPawnId = source.ownerPawnId,
                ownerEpochToken = source.ownerEpochToken,
                recordId = source.recordId,
                sourceOccurrenceId = source.sourceOccurrenceId,
                sourceEventId = source.sourceEventId,
                rootId = source.rootId,
                chapterOrNoveltyId = source.chapterOrNoveltyId,
                kind = source.kind,
                importance = source.importance,
                originalEventTick = source.originalEventTick,
                available = source.available,
                identityValid = source.identityValid,
                promptRouteValid = source.promptRouteValid,
                ttlEligible = source.ttlEligible,
                categoryProjectionValid = source.categoryProjectionValid,
                suppressed = source.suppressed,
                isThreadMember = source.isThreadMember,
                isCurrentThreadProjection = source.isCurrentThreadProjection,
                directExactEventReference = source.directExactEventReference,
                narrativeFitScore = source.narrativeFitScore,
                historicalText = source.historicalText,
                currentStateApplicable = source.currentStateApplicable,
                currentStateContradictsHistorical = source.currentStateContradictsHistorical,
                currentStateCanRender = source.currentStateCanRender,
                currentStateText = source.currentStateText,
                currentStateSourceId = source.currentStateSourceId,
                recordGuard = CopyGuardState(source.recordGuard)
            };
            copy.categories.AddRange(source.categories ?? new List<string>());
            copy.topicKeys.AddRange(source.topicKeys ?? new List<string>());
            copy.representedSourceOccurrenceIds.AddRange(
                source.representedSourceOccurrenceIds ?? new List<string>());
            for (int index = 0; source.exactRoutes != null && index < source.exactRoutes.Count; index++)
            {
                MemoryRecallRouteIdentity route = source.exactRoutes[index];
                copy.exactRoutes.Add(route == null ? null : new MemoryRecallRouteIdentity
                {
                    routeKind = route.routeKind,
                    subjectKind = route.subjectKind,
                    subjectId = route.subjectId,
                    routeKey = route.routeKey
                });
            }
            for (int index = 0;
                source.requiredStructuralGuards != null
                && index < source.requiredStructuralGuards.Count;
                index++)
            {
                MemoryGuardIdentity guard = source.requiredStructuralGuards[index];
                copy.requiredStructuralGuards.Add(guard == null ? null : new MemoryGuardIdentity
                {
                    guardKind = guard.guardKind,
                    guardKey = guard.guardKey
                });
            }
            copy.structuralGuardStates = CopyGuardStates(source.structuralGuardStates);
            return copy;
        }

        private static MemoryRecallQueryV2 CopyQuery(MemoryRecallQueryV2 source)
        {
            MemoryRecallQueryV2 copy = new MemoryRecallQueryV2
            {
                consumerId = source.consumerId,
                writingFormat = source.writingFormat,
                ownerPawnId = source.ownerPawnId,
                ownerEpochToken = source.ownerEpochToken,
                pairCounterpartPawnId = source.pairCounterpartPawnId,
                currentEventId = source.currentEventId,
                currentSourceOccurrenceId = source.currentSourceOccurrenceId,
                useMemoriesInWriting = source.useMemoriesInWriting,
                repetitionPolicy = CopyRepetitionPolicy(source.repetitionPolicy)
            };
            copy.enabledCategories.AddRange(source.enabledCategories ?? new List<string>());
            copy.topicKeys.AddRange(source.topicKeys ?? new List<string>());
            copy.excludedSourceEventIds.AddRange(source.excludedSourceEventIds ?? new List<string>());
            copy.excludedSourceOccurrenceIds.AddRange(
                source.excludedSourceOccurrenceIds ?? new List<string>());
            for (int index = 0; source.exactRoutes != null && index < source.exactRoutes.Count; index++)
            {
                MemoryRecallRouteIdentity route = source.exactRoutes[index];
                copy.exactRoutes.Add(route == null ? null : new MemoryRecallRouteIdentity
                {
                    routeKind = route.routeKind,
                    subjectKind = route.subjectKind,
                    subjectId = route.subjectId,
                    routeKey = route.routeKey
                });
            }
            return copy;
        }

        private static MemoryRepetitionPolicySnapshot CopyRepetitionPolicy(
            MemoryRepetitionPolicySnapshot source)
        {
            if (source == null) return null;
            return new MemoryRepetitionPolicySnapshot
            {
                currentTick = source.currentTick,
                completedDiaryEntryOrdinal = source.completedDiaryEntryOrdinal,
                ticksPerDay = source.ticksPerDay,
                memoryReuseDays = source.memoryReuseDays,
                memoryRevisitEntryCount = source.memoryRevisitEntryCount,
                recordMinimumReuseDays = source.recordMinimumReuseDays,
                recordMinimumEntryDistance = source.recordMinimumEntryDistance,
                rootMinimumEntryDistance = source.rootMinimumEntryDistance,
                subjectMinimumEntryDistance = source.subjectMinimumEntryDistance,
                pairMinimumEntryDistance = source.pairMinimumEntryDistance,
                noveltyMinimumEntryDistance = source.noveltyMinimumEntryDistance
            };
        }

        private static MemoryRepetitionGuardState CopyGuardState(
            MemoryRepetitionGuardState source)
        {
            if (source == null) return null;
            return new MemoryRepetitionGuardState
            {
                ownerEpochToken = source.ownerEpochToken,
                guardKind = source.guardKind,
                guardKey = source.guardKey,
                lastAutomaticIncludedTick = source.lastAutomaticIncludedTick,
                lastAutomaticIncludedEntryOrdinal = source.lastAutomaticIncludedEntryOrdinal,
                automaticInclusionCount = source.automaticInclusionCount,
                reserved = source.reserved
            };
        }

        private static List<MemoryRepetitionGuardState> CopyGuardStates(
            List<MemoryRepetitionGuardState> source)
        {
            List<MemoryRepetitionGuardState> copy = new List<MemoryRepetitionGuardState>();
            for (int index = 0; source != null && index < source.Count; index++)
                copy.Add(CopyGuardState(source[index]));
            return copy;
        }

        private static void AddRejectedReports(
            MemoryRecallSelectionResultV2 result,
            List<MemoryRecallCandidateSnapshot> candidates,
            string reason)
        {
            for (int index = 0; candidates != null && index < candidates.Count; index++)
            {
                result.report.Add(new MemoryRecallCandidateReportV2
                {
                    recordId = candidates[index]?.recordId ?? string.Empty,
                    rejectReason = reason
                });
            }
        }

        private static void AddRejectedFrozenReports(
            MemoryRecallSelectionResultV2 result,
            MemoryRecallSelectionResultV2 frozen,
            string reason)
        {
            for (int index = 0; frozen?.selected != null && index < frozen.selected.Count; index++)
            {
                result.report.Add(new MemoryRecallCandidateReportV2
                {
                    recordId = frozen.selected[index]?.candidate?.recordId ?? string.Empty,
                    rejectReason = reason
                });
            }
        }

        private static bool SameRoute(
            MemoryRecallRouteIdentity left,
            MemoryRecallRouteIdentity right)
        {
            return string.Equals(left.routeKind, right.routeKind, StringComparison.Ordinal)
                && string.Equals(left.subjectKind, right.subjectKind, StringComparison.Ordinal)
                && string.Equals(left.subjectId, right.subjectId, StringComparison.Ordinal)
                && string.Equals(left.routeKey, right.routeKey, StringComparison.Ordinal);
        }

        private static bool SameGuard(MemoryGuardIdentity left, MemoryGuardIdentity right)
        {
            return left != null && right != null
                && string.Equals(left.guardKind, right.guardKind, StringComparison.Ordinal)
                && string.Equals(left.guardKey, right.guardKey, StringComparison.Ordinal);
        }

        private static string GuardTuple(string guardKind, string guardKey)
        {
            return OrdinalSegmentCodec.Segment(guardKind)
                + OrdinalSegmentCodec.Segment(guardKey);
        }

        private static string RouteTuple(MemoryRecallRouteIdentity route)
        {
            return OrdinalSegmentCodec.Segment(route.routeKind)
                + OrdinalSegmentCodec.Segment(route.subjectKind)
                + OrdinalSegmentCodec.Segment(route.subjectId)
                + OrdinalSegmentCodec.Segment(route.routeKey);
        }

        private static bool ValidRawIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ValidCompositeIdentityList(List<string> values)
        {
            if (values == null) return false;
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index];
                if (!ValidCompositeIdentity(value) || !distinct.Add(value)) return false;
            }
            return true;
        }

        private static bool ValidEnabledCategoryList(List<string> values)
        {
            if (values == null) return false;
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                if (!MemoryContractTokens.IsKnownCategory(values[index])
                    || !distinct.Add(values[index])) return false;
            }
            return true;
        }

        private static bool ValidCompositeIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ValidOptionalCompositeIdentity(string value)
        {
            return value != null
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ContainsOrdinal(List<string> values, string target)
        {
            if (values == null || target == null) return false;
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], target, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool SharesOrdinal(List<string> left, List<string> right)
        {
            for (int index = 0; left != null && index < left.Count; index++)
            {
                if (ContainsOrdinal(right, left[index])) return true;
            }
            return false;
        }

        private static void AddUniqueOrdinal(List<string> target, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !ContainsOrdinal(target, value)) target.Add(value);
        }

        private static bool SameOrdinalSequence(List<string> left, List<string> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Runs eligibility + ranking and returns contextual records newest-relevance-first, followed
        /// by the owner's canonical background only when a line slot remains. Broad mood/social/body/
        /// danger domains never match by themselves — only shared participants and exact keys do.
        /// Participant-required queries (currently Social Reflection) never receive the fallback.
        /// </summary>
        public static KnowledgeSelectionResult Select(
            KnowledgeQuery query,
            List<ImportantMemoryRecordSnapshot> records,
            KnowledgePolicySnapshot policy)
        {
            KnowledgeSelectionResult result = new KnowledgeSelectionResult();
            if (query == null || records == null || records.Count == 0)
            {
                return result;
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            List<RankedCandidate> eligible = new List<RankedCandidate>();
            List<RankedCandidate> backgroundFallbacks = new List<RankedCandidate>();
            for (int i = 0; i < records.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = records[i];
                KnowledgeCandidateReport report = new KnowledgeCandidateReport();
                if (record == null || string.IsNullOrWhiteSpace(record.recordId))
                {
                    continue;
                }

                report.recordId = record.recordId;
                report.eventKind = record.eventKind ?? string.Empty;
                result.report.Add(report);

                // A background row deliberately carries no participants or subject keys. Only the
                // exact canonical singleton for this query owner can use the fallback door; malformed,
                // cross-owner, and duplicate lookalikes stay visible in diagnostics but never become
                // factual prompt canon. H7's subject-specific participant gate remains absolute.
                if (string.Equals(
                    PlayerMemoryPolicy.NormalizeRecallScope(record.recallScope),
                    KnowledgeTokens.RecallScopeBackground,
                    StringComparison.Ordinal))
                {
                    if (!query.requireParticipantOverlap
                        && PlayerMemoryPolicy.IsCanonicalBackstory(record, query.ownerPawnId))
                    {
                        backgroundFallbacks.Add(new RankedCandidate
                        {
                            record = record,
                            report = report
                        });
                    }
                    else
                    {
                        report.rejectReason = KnowledgeRejectReasons.NoOverlap;
                    }

                    continue;
                }

                // Contextual retrieval is reserved for gameplay-captured rows. A corrupt or future
                // player-authored row cannot fabricate participants/subjects to enter this path; the
                // canonical background door above is the only player-memory path in this release.
                if (!string.Equals(
                        PlayerMemoryPolicy.NormalizeSourceKind(record.sourceKind),
                        KnowledgeTokens.SourceKindCaptured,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        PlayerMemoryPolicy.NormalizeRecallScope(record.recallScope),
                        KnowledgeTokens.RecallScopeContextual,
                        StringComparison.Ordinal))
                {
                    report.rejectReason = KnowledgeRejectReasons.NoOverlap;
                    continue;
                }

                // Self-echo guard: the record deposited by this very event never surfaces on it.
                if (!string.IsNullOrWhiteSpace(record.sourceEventId)
                    && string.Equals(record.sourceEventId, query.eventId, StringComparison.OrdinalIgnoreCase))
                {
                    report.rejectReason = KnowledgeRejectReasons.SelfEcho;
                    continue;
                }

                // A delayed derivative page (currently H7 social reflection) has a different ID
                // from the direct page that caused it. The caller supplies that original source ID
                // explicitly so it cannot be presented as an older memory about the same event.
                if (!string.IsNullOrWhiteSpace(record.sourceEventId)
                    && ContainsIgnoreCase(query.excludedSourceEventIds, record.sourceEventId))
                {
                    report.rejectReason = KnowledgeRejectReasons.ExcludedSource;
                    continue;
                }

                report.sharedParticipant = SharesParticipant(query.participantIds, record.participants);
                report.sharedSubject = SharesSubjectKey(query.subjectKeys, record.subjectKeys);
                report.sharedTopic = !string.IsNullOrWhiteSpace(record.topicKey)
                    && ContainsIgnoreCase(query.topicKeys, record.topicKey);

                // Eligibility (§3.1): a concrete participant OR an exact subject/entity key.
                // Topic overlap alone is a ranking tier, never an eligibility door.
                if ((query.requireParticipantOverlap && !report.sharedParticipant)
                    || (!query.requireParticipantOverlap
                        && !report.sharedParticipant
                        && !report.sharedSubject))
                {
                    report.rejectReason = KnowledgeRejectReasons.NoOverlap;
                    continue;
                }

                eligible.Add(new RankedCandidate { record = record, report = report });
            }

            eligible.Sort(CompareCandidates);
            backgroundFallbacks.Sort(CompareCandidates);
            int cap = Math.Max(0, safePolicy.relevantPastMaxLines);
            for (int i = 0; i < eligible.Count; i++)
            {
                if (i < cap)
                {
                    eligible[i].report.selected = true;
                    result.selected.Add(eligible[i].record);
                }
                else
                {
                    eligible[i].report.rejectReason = KnowledgeRejectReasons.OverCap;
                }
            }

            // Background is a fallback, never a competitor. Even a newer player-authored row cannot
            // displace an exact relationship/body/status memory that qualified above.
            for (int i = 0; i < backgroundFallbacks.Count; i++)
            {
                if (i == 0 && result.selected.Count < cap)
                {
                    backgroundFallbacks[i].report.selected = true;
                    result.selected.Add(backgroundFallbacks[i].record);
                }
                else
                {
                    backgroundFallbacks[i].report.rejectReason = KnowledgeRejectReasons.OverCap;
                }
            }

            return result;
        }

        /// <summary>
        /// Fixed ranking (§3.1): shared participant, then exact entity key, then topic family,
        /// then newest tick, then record ID ordinal — fully deterministic, stable ties included.
        /// </summary>
        private static int CompareCandidates(RankedCandidate left, RankedCandidate right)
        {
            int participant = right.report.sharedParticipant.CompareTo(left.report.sharedParticipant);
            if (participant != 0)
            {
                return participant;
            }

            int subject = right.report.sharedSubject.CompareTo(left.report.sharedSubject);
            if (subject != 0)
            {
                return subject;
            }

            int topic = right.report.sharedTopic.CompareTo(left.report.sharedTopic);
            if (topic != 0)
            {
                return topic;
            }

            int tick = right.record.tick.CompareTo(left.record.tick);
            return tick != 0
                ? tick
                : string.Compare(left.record.recordId, right.record.recordId, StringComparison.Ordinal);
        }

        private static bool SharesParticipant(List<string> queryIds, List<KnowledgeParticipant> participants)
        {
            if (queryIds == null || participants == null)
            {
                return false;
            }

            for (int i = 0; i < participants.Count; i++)
            {
                KnowledgeParticipant participant = participants[i];
                if (participant == null || string.IsNullOrWhiteSpace(participant.pawnId))
                {
                    continue;
                }

                if (ContainsIgnoreCase(queryIds, participant.pawnId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SharesSubjectKey(List<string> queryKeys, List<string> recordKeys)
        {
            if (queryKeys == null || recordKeys == null)
            {
                return false;
            }

            for (int i = 0; i < recordKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(recordKeys[i])
                    && ContainsIgnoreCase(queryKeys, recordKeys[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIgnoreCase(List<string> values, string target)
        {
            if (values == null || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            string trimmed = target.Trim();
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && string.Equals(values[i].Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the retrieval query for the current event from the XML-owned extraction rules
        /// (policy.querySubjectKeyRules) plus the event's classified topic families. Shared with
        /// the capture path so query keys and record keys can never drift apart.
        /// </summary>
        public static KnowledgeQuery BuildQuery(
            string eventId,
            string ownerPawnId,
            string otherPawnId,
            int currentTick,
            string gameContext,
            string eventDefName,
            List<ImportantEventRule> rules,
            KnowledgePolicySnapshot policy)
        {
            KnowledgeQuery query = new KnowledgeQuery
            {
                eventId = eventId ?? string.Empty,
                ownerPawnId = ownerPawnId ?? string.Empty,
                currentTick = currentTick
            };

            if (!string.IsNullOrWhiteSpace(otherPawnId)
                && !string.Equals(otherPawnId, ownerPawnId, StringComparison.OrdinalIgnoreCase))
            {
                query.participantIds.Add(otherPawnId.Trim());
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            if (safePolicy.querySubjectKeyRules != null)
            {
                for (int i = 0; i < safePolicy.querySubjectKeyRules.Count; i++)
                {
                    KnowledgeSubjectKeyRule rule = safePolicy.querySubjectKeyRules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.contextKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(gameContext, rule.contextKey);
                    if (KnowledgeTokens.IsSentinelValue(value))
                    {
                        continue;
                    }

                    string key = ImportantEventClassifier.ComposeSubjectKey(rule.prefix, value);
                    if (!ContainsIgnoreCase(query.subjectKeys, key))
                    {
                        query.subjectKeys.Add(key);
                    }
                }
            }

            // The current event's own important-event classification supplies the topic families
            // (ranking tier 3) and any rule-declared subject keys — e.g. a new arm-loss event
            // queries "part:Arm" exactly like the record it is about to deposit.
            if (rules != null)
            {
                KnowledgeCaptureSignal probe = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalEvent,
                    defName = eventDefName ?? string.Empty,
                    gameContext = gameContext ?? string.Empty
                };
                ImportantEventRule match = ImportantEventClassifier.FirstMatch(probe, rules);
                if (match != null)
                {
                    if (!string.IsNullOrWhiteSpace(match.topicKey)
                        && !ContainsIgnoreCase(query.topicKeys, match.topicKey))
                    {
                        query.topicKeys.Add(match.topicKey.Trim());
                    }

                    if (match.subjectKeyRules != null)
                    {
                        for (int i = 0; i < match.subjectKeyRules.Count; i++)
                        {
                            KnowledgeSubjectKeyRule rule = match.subjectKeyRules[i];
                            if (rule == null || string.IsNullOrWhiteSpace(rule.contextKey))
                            {
                                continue;
                            }

                            string value = DiaryContextFields.Value(gameContext, rule.contextKey);
                            if (KnowledgeTokens.IsSentinelValue(value))
                            {
                                continue;
                            }

                            string key = ImportantEventClassifier.ComposeSubjectKey(rule.prefix, value);
                            if (!ContainsIgnoreCase(query.subjectKeys, key))
                            {
                                query.subjectKeys.Add(key);
                            }
                        }
                    }

                    if (match.constantSubjectKeys != null)
                    {
                        for (int i = 0; i < match.constantSubjectKeys.Count; i++)
                        {
                            string constant = match.constantSubjectKeys[i];
                            if (!string.IsNullOrWhiteSpace(constant)
                                && !ContainsIgnoreCase(query.subjectKeys, constant.Trim()))
                            {
                                query.subjectKeys.Add(constant.Trim());
                            }
                        }
                    }
                }
            }

            return query;
        }
    }
}
