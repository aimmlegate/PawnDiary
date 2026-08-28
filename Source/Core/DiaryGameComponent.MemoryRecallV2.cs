// DiaryGameComponent.MemoryRecallV2.cs — main-thread adapter from the saved M4 memory store to the
// pure Recall-v2 selector/renderer/receipt contracts.
//
// The adapter detaches one exact owner and autobiographical epoch, never passes a Pawn/Def/store
// object into pure selection, and never reads DLC trackers. It is release-gated by its callers:
// CurrentRelease uses this adapter while LegacyShadow remains the explicit compatibility branch.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Request-build cache of exact detached projections. It is transient because the canonical
        /// evidence/guards move into SavedActiveLogicalRequestV1 before transport becomes visible.
        /// </summary>
        private readonly Dictionary<string, MemoryRecallPromptProjection>
            memoryRecallV2ProjectionCache =
                new Dictionary<string, MemoryRecallPromptProjection>(StringComparer.Ordinal);

        /// <summary>
        /// Event-time selected shortlists. Queue-time prompt variants may only revalidate these exact
        /// rows; they may not rerun ranking and substitute a different memory.
        /// </summary>
        private readonly Dictionary<string, MemoryRecallSelectionResultV2>
            memoryRecallV2FrozenSelectionCache =
                new Dictionary<string, MemoryRecallSelectionResultV2>(StringComparer.Ordinal);

        // One event normally contributes at most two entries. The cap is defensive against events
        // that are captured but never reach dispatch, and clearing is safe because a cache miss means
        // fail-closed, memory-free generation rather than reselection.
        private const int MaximumFrozenRecallSelections = 128;
        private const int MaximumFrozenRecallProjections = MaximumFrozenRecallSelections * 3;

        /// <summary>Clears event-bound detached projections at a loaded-game boundary.</summary>
        private void ResetMemoryRecallV2Transient()
        {
            memoryRecallV2ProjectionCache.Clear();
            memoryRecallV2FrozenSelectionCache.Clear();
        }

        /// <summary>
        /// Selects and freezes one exact POV shortlist at the event boundary.
        /// </summary>
        private MemoryRecallPromptProjection FreezeMemoryRecallV2Projection(
            DiaryEvent diaryEvent,
            string povRole,
            PromptContextDetailLevel contextDetailLevel,
            bool persistSelection = true)
        {
            return BuildMemoryRecallV2Projection(
                diaryEvent,
                povRole,
                contextDetailLevel,
                freezeSelection: true,
                persistFrozenSelection: persistSelection);
        }

        /// <summary>
        /// Revalidates the event-time shortlist immediately before one prompt variant freezes. Missing,
        /// corrupt, cooling, private, or over-cap memory simply produces an empty/background-only
        /// field; ordinary diary generation remains available.
        /// </summary>
        private MemoryRecallPromptProjection PrepareMemoryRecallV2Projection(
            DiaryEvent diaryEvent,
            string povRole,
            PromptContextDetailLevel contextDetailLevel)
        {
            return BuildMemoryRecallV2Projection(
                diaryEvent,
                povRole,
                contextDetailLevel,
                freezeSelection: false,
                persistFrozenSelection: false);
        }

        private MemoryRecallPromptProjection BuildMemoryRecallV2Projection(
            DiaryEvent diaryEvent,
            string povRole,
            PromptContextDetailLevel contextDetailLevel,
            bool freezeSelection,
            bool persistFrozenSelection)
        {
            var empty = new MemoryRecallPromptProjection();
            if (diaryEvent == null
                || !DiaryEvent.RoleIsInitiatorOrRecipient(povRole)
                || diaryEvent.IsSkipped(povRole))
            {
                return empty;
            }

            string ownerPawnId = diaryEvent.PawnIdForRole(povRole) ?? string.Empty;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(ownerPawnId);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            string writingFormat = RecallWritingFormat(contextDetailLevel);
            if (owner == null || policy == null
                || policy.compatibilityFailClosed)
            {
                diaryEvent.SetMemoryContext(povRole, string.Empty);
                CacheRecallProjection(diaryEvent.eventId, povRole, writingFormat, empty);
                return empty;
            }

            DiaryKnowledgeTuningDef tuning = MemoryOptionalTuning();
            KnowledgePolicySnapshot legacyPolicy = DiaryKnowledgePolicy.Snapshot(
                applyGlobalMemorySetting: false);
            if (string.IsNullOrWhiteSpace(owner.autobiographicalEpochToken))
            {
                // The player background is an independently enabled singleton, not episodic evidence.
                // A new profile may legitimately own it before the first event allocates an epoch.
                MemoryRecallPromptProjection backgroundOnly = RenderRecallV2Projection(
                    owner,
                    new MemoryRecallSelectionResultV2(),
                    writingFormat,
                    policy,
                    tuning,
                    legacyPolicy);
                diaryEvent.SetMemoryContext(povRole, backgroundOnly.text);
                CacheRecallProjection(
                    diaryEvent.eventId,
                    povRole,
                    writingFormat,
                    backgroundOnly);
                return backgroundOnly;
            }
            string otherPawnId = DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole)
                ? diaryEvent.initiatorPawnId ?? string.Empty
                : diaryEvent.recipientPawnId ?? string.Empty;
            KnowledgeQuery legacyQuery = ImportantMemorySelector.BuildQuery(
                diaryEvent.eventId,
                ownerPawnId,
                otherPawnId,
                diaryEvent.tick,
                diaryEvent.gameContext,
                diaryEvent.interactionDefName,
                DiaryKnowledgePolicy.ImportantEventRules(),
                legacyPolicy);

            MemoryRecallQueryV2 query = BuildRecallV2Query(
                diaryEvent,
                povRole,
                owner,
                policy,
                tuning,
                writingFormat,
                otherPawnId,
                legacyPolicy,
                legacyQuery);
            MemoryRecallReservationView reservations = SnapshotRecallReservations(owner);
            List<MemoryRecallCandidateSnapshot> candidates = SnapshotRecallCandidates(
                owner,
                policy,
                tuning,
                query,
                reservations);

            // Paired POVs remain private. The recipient receives only source identities from the
            // initiator's already-frozen shortlist, never its candidate graph or wording. A Summary
            // contributes every source occurrence represented by its selected projection.
            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole)
                && !diaryEvent.solo
                && !string.IsNullOrWhiteSpace(diaryEvent.initiatorPawnId))
            {
                MemoryRecallSelectionResultV2 initiatorSelection = CachedFrozenRecallSelection(
                    diaryEvent,
                    DiaryEvent.InitiatorRole);
                AddExcludedSelectedSources(query, initiatorSelection);
            }

            MemoryRecallSelectionResultV2 selected;
            if (freezeSelection)
            {
                selected = ImportantMemorySelector.SelectV2(query, candidates);
                if (persistFrozenSelection)
                    CacheFrozenRecallSelection(diaryEvent, povRole, selected);
            }
            else
            {
                selected = ImportantMemorySelector.RevalidateFrozenV2(
                    CachedFrozenRecallSelection(diaryEvent, povRole),
                    query,
                    candidates);
            }
            MemoryRecallPromptProjection projection = RenderRecallV2Projection(
                owner,
                selected,
                writingFormat,
                policy,
                tuning,
                legacyPolicy);
            diaryEvent.SetMemoryContext(povRole, projection.text);
            CacheRecallProjection(
                diaryEvent.eventId,
                povRole,
                writingFormat,
                projection);
            return projection;
        }

        /// <summary>
        /// Rebuilds only the independently-enabled player background after episodic recall refuses
        /// staging. This preserves the Culture/Background switch while keeping optional memory
        /// evidence out of the retry prompt.
        /// </summary>
        private void PrepareMemoryRecallV2BackgroundOnly(
            DiaryEvent diaryEvent,
            string povRole,
            PromptContextDetailLevel contextDetailLevel)
        {
            string writingFormat = RecallWritingFormat(contextDetailLevel);
            var projection = new MemoryRecallPromptProjection();
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(
                diaryEvent?.PawnIdForRole(povRole) ?? string.Empty);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            KnowledgePolicySnapshot legacyPolicy = DiaryKnowledgePolicy.Snapshot(
                applyGlobalMemorySetting: false);
            if (owner != null
                && policy != null
                && !policy.compatibilityFailClosed
                && MemoryContextPrompt.AllowsPawnBackground(
                    writingFormat,
                    policy.usePawnBackground)
                && !string.IsNullOrWhiteSpace(owner.playerBackground))
            {
                string background = ImportantMemoryLineRenderer.FormatBackground(
                    owner.playerBackground,
                    legacyPolicy?.backgroundMemoryLineFormat ?? string.Empty);
                int maximumCharacters = Math.Max(
                    1,
                    legacyPolicy?.relevantPastMaxChars ?? 500);
                if (background.Length <= maximumCharacters) projection.text = background;
            }
            diaryEvent?.SetMemoryContext(povRole, projection.text);
            CacheRecallProjection(
                diaryEvent?.eventId,
                povRole,
                writingFormat,
                projection);
        }

        private static string RecallWritingFormat(
            PromptContextDetailLevel contextDetailLevel)
        {
            PromptContextDetailLevel level = PromptContextSelector.Normalize(contextDetailLevel);
            if (level == PromptContextDetailLevel.Compact)
                return MemoryRecallWritingFormats.Compact;
            return level == PromptContextDetailLevel.Balanced
                ? MemoryRecallWritingFormats.Balanced
                : MemoryRecallWritingFormats.Full;
        }

        private static MemoryRecallQueryV2 BuildRecallV2Query(
            DiaryEvent diaryEvent,
            string povRole,
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            string writingFormat,
            string otherPawnId,
            KnowledgePolicySnapshot legacyPolicy,
            KnowledgeQuery legacyQuery)
        {
            bool socialReflection = string.Equals(
                diaryEvent.interactionDefName,
                SocialReflectionEventData.DefNameToken,
                StringComparison.OrdinalIgnoreCase);
            string exactCounterpartPawnId = socialReflection
                ? DiaryContextFields.Value(
                    diaryEvent.gameContext,
                    SocialReflectionEventData.SubjectIdContextKey)
                : otherPawnId ?? string.Empty;
            var query = new MemoryRecallQueryV2
            {
                consumerId = socialReflection
                    ? MemoryRecallConsumerRegistry.ExistingReflection
                    : MemoryRecallConsumerRegistry.OrdinaryDiary,
                writingFormat = writingFormat,
                ownerPawnId = owner.pawnId ?? string.Empty,
                ownerEpochToken = owner.autobiographicalEpochToken ?? string.Empty,
                // H7 is structurally solo. Its exact subject still participates in route matching,
                // but must not be mistaken for a paired-POV repetition guard.
                pairCounterpartPawnId = diaryEvent.solo
                    ? string.Empty
                    : exactCounterpartPawnId,
                currentEventId = diaryEvent.eventId ?? string.Empty,
                // Current shipped page capture uses the event ID as its stable source occurrence.
                currentSourceOccurrenceId = diaryEvent.eventId ?? string.Empty,
                useMemoriesInWriting = policy.useMemoriesInWriting,
                repetitionPolicy = new MemoryRepetitionPolicySnapshot
                {
                    currentTick = Math.Max(0, Find.TickManager?.TicksGame ?? diaryEvent.tick),
                    completedDiaryEntryOrdinal = owner.completedDiaryEntryOrdinal,
                    ticksPerDay = GenDate.TicksPerDay,
                    memoryReuseDays = policy.memoryReuseDays,
                    memoryRevisitEntryCount = policy.memoryRevisitEntryCount,
                    recordMinimumReuseDays = NonNegative(
                        tuning?.memoryRecordMinimumReuseDays ?? 1),
                    recordMinimumEntryDistance = NonNegative(
                        tuning?.memoryRecordMinimumEntryDistance ?? 1),
                    rootMinimumEntryDistance = NonNegative(
                        tuning?.memoryRootMinimumEntryDistance ?? 1),
                    subjectMinimumEntryDistance = NonNegative(
                        tuning?.memorySubjectMinimumEntryDistance ?? 1),
                    pairMinimumEntryDistance = NonNegative(
                        tuning?.memoryPairMinimumEntryDistance ?? 1),
                    noveltyMinimumEntryDistance = NonNegative(
                        tuning?.memoryNoveltyMinimumEntryDistance ?? 1)
                }
            };
            AddEnabledCategories(query.enabledCategories, policy.memoryCategoryMask);
            AddRecallRoute(
                query.exactRoutes,
                MemoryContractTokens.SubjectPawn,
                exactCounterpartPawnId);
            for (int index = 0; legacyQuery?.participantIds != null
                && index < legacyQuery.participantIds.Count; index++)
            {
                AddRecallRoute(
                    query.exactRoutes,
                    MemoryContractTokens.SubjectPawn,
                    legacyQuery.participantIds[index]);
            }
            // Legacy subject keys are strings such as "part:Arm" and "faction:Empire". Treating one
            // string as pawn + faction + stream identity invents routes. Reclassify the current event
            // through the same pure factual contract used at capture and copy only its typed subjects.
            List<ImportantMemoryDraft> currentDrafts = ImportantEventClassifier.Classify(
                new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalEvent,
                    defName = diaryEvent.interactionDefName ?? string.Empty,
                    sourceEventId = diaryEvent.eventId ?? string.Empty,
                    sourceOccurrenceId = diaryEvent.eventId ?? string.Empty,
                    tick = diaryEvent.tick,
                    dateLabel = diaryEvent.date ?? string.Empty,
                    gameContext = diaryEvent.gameContext ?? string.Empty,
                    initiatorPawnId = diaryEvent.initiatorPawnId ?? string.Empty,
                    initiatorName = diaryEvent.initiatorName ?? string.Empty,
                    recipientPawnId = diaryEvent.recipientPawnId ?? string.Empty,
                    recipientName = diaryEvent.recipientName ?? string.Empty
                },
                DiaryKnowledgePolicy.ImportantEventRules(),
                legacyPolicy);
            ImportantMemorySelector.AddFactualDraftRoutes(
                query.exactRoutes, currentDrafts, owner.pawnId ?? string.Empty);
            if (legacyQuery?.topicKeys != null) query.topicKeys.AddRange(legacyQuery.topicKeys);
            if (legacyQuery?.excludedSourceEventIds != null)
                query.excludedSourceEventIds.AddRange(legacyQuery.excludedSourceEventIds);
            if (socialReflection)
            {
                // The reflection page has a new event identity, so exclude the direct source page
                // explicitly just like the legacy selector. This prevents recursive self-evidence.
                string sourceEventId = DiaryContextFields.Value(
                    diaryEvent.gameContext,
                    SocialReflectionEventData.SourceEventIdContextKey);
                if (!string.IsNullOrWhiteSpace(sourceEventId))
                    query.excludedSourceEventIds.Add(sourceEventId.Trim());
            }
            return query;
        }

        private List<MemoryRecallCandidateSnapshot> SnapshotRecallCandidates(
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            MemoryRecallQueryV2 query,
            MemoryRecallReservationView reservations)
        {
            var result = new List<MemoryRecallCandidateSnapshot>();
            for (int index = 0; owner.standaloneBlocks != null
                && index < owner.standaloneBlocks.Count; index++)
            {
                MemoryRecallCandidateSnapshot candidate = SnapshotRecallCandidate(
                    owner,
                    null,
                    owner.standaloneBlocks[index],
                    false,
                    policy,
                    tuning,
                    query,
                    reservations);
                if (candidate != null) result.Add(candidate);
            }
            for (int rootIndex = 0; owner.threadRoots != null
                && rootIndex < owner.threadRoots.Count; rootIndex++)
            {
                SavedMemoryThreadRoot root = owner.threadRoots[rootIndex];
                if (root == null) continue;
                SavedMemoryBlock projection = CurrentThreadProjection(
                    root, policy, query);
                MemoryRecallCandidateSnapshot candidate = SnapshotRecallCandidate(
                    owner,
                    root,
                    projection,
                    true,
                    policy,
                    tuning,
                    query,
                    reservations);
                if (candidate != null) result.Add(candidate);
            }
            return result;
        }

        private MemoryRecallCandidateSnapshot SnapshotRecallCandidate(
            PawnKnowledgeState owner,
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            bool threadProjection,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            MemoryRecallQueryV2 query,
            MemoryRecallReservationView reservations)
        {
            if (block == null) return null;
            bool summary = block.kind == MemoryContractTokens.KindSummary;
            int enabledSummaryMask = summary
                ? (block.summaryPayload?.derivedCategoryMask ?? 0) & policy.memoryCategoryMask
                : 0;
            SummaryWordingCurrentSnapshot currentSummary = summary
                ? CurrentSummarySnapshot(root, block, policy)
                : null;
            bool playerSummaryProjection = summary
                && block.playerEdited
                && enabledSummaryMask != 0
                && enabledSummaryMask == (block.summaryPayload?.derivedCategoryMask ?? 0)
                && !string.IsNullOrWhiteSpace(block.playerWording);
            string deterministic = block.playerEdited && !summary
                    && !string.IsNullOrWhiteSpace(block.playerWording)
                ? block.playerWording
                : summary
                    ? playerSummaryProjection
                        ? block.playerWording
                        : currentSummary?.deterministicWording ?? string.Empty
                    : block.automaticWording;
            var candidate = new MemoryRecallCandidateSnapshot
            {
                ownerPawnId = block.ownerPawnId ?? string.Empty,
                ownerEpochToken = block.ownerEpochToken ?? string.Empty,
                recordId = block.recordId ?? string.Empty,
                sourceOccurrenceId = block.sourceOccurrenceId ?? string.Empty,
                sourceEventId = block.sourceEventId ?? string.Empty,
                rootId = root?.rootId ?? string.Empty,
                chapterOrNoveltyId = threadProjection ? block.chapterId ?? string.Empty : string.Empty,
                kind = block.kind ?? string.Empty,
                importance = summary
                    ? HighestEnabledSummaryImportance(block.summaryPayload, enabledSummaryMask)
                    : block.importance ?? string.Empty,
                originalEventTick = summary
                    ? LatestEnabledSummaryTick(block.summaryPayload, enabledSummaryMask)
                    : block.originalEventTick,
                suppressed = block.suppressed,
                isThreadMember = threadProjection,
                isCurrentThreadProjection = true,
                narrativeFitScore = block.requiredLifecycleLandmark ? 2 : 0,
                historicalText = deterministic ?? string.Empty,
                // Player-edited Summary prose cannot be split by category. It remains recallable only
                // when every category represented by the Summary is enabled.
                categoryProjectionValid = !summary
                    || currentSummary != null
                    || playerSummaryProjection,
                // Summary retention is applied per contribution by the reducer. The Summary block's
                // own original tick is a stable identity field, not an expiry timestamp.
                ttlEligible = summary || !MemoryThreadReducer.IsExpired(
                    query.repetitionPolicy.currentTick,
                    block.originalEventTick,
                    block.ageUnknown,
                    block.importance,
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks)
            };
            AddCandidateCategories(candidate, block, enabledSummaryMask);
            AddCandidateRoutes(candidate, root, block, enabledSummaryMask);
            AddRepresentedSources(candidate, block, enabledSummaryMask);
            candidate.recordGuard = new MemoryRepetitionGuardState
            {
                ownerEpochToken = block.ownerEpochToken ?? string.Empty,
                guardKind = MemoryRepetitionGuardKinds.Record,
                guardKey = MemoryRepetitionGuardPolicy.RecordKey(block.recordId),
                lastAutomaticIncludedTick = block.lastAutomaticIncludedTick,
                lastAutomaticIncludedEntryOrdinal = block.lastAutomaticIncludedEntryOrdinal,
                automaticInclusionCount = block.automaticInclusionCount,
                reserved = reservations.evidenceRecordIds.Contains(block.recordId ?? string.Empty)
            };
            AddStructuralRecallGuards(candidate, owner, query, reservations);
            ApplyCurrentTruth(candidate, owner, tuning);
            if (summary)
            {
                SavedMemorySummaryPayload payload = block.summaryPayload;
                if (currentSummary != null && payload != null)
                {
                    candidate.naturalWording = new MemoryRecallNaturalWordingSnapshot
                    {
                        currentProjectionFingerprint =
                            currentSummary.projectionFingerprint ?? string.Empty,
                        currentFormatRevision = currentSummary.formatRevision,
                        currentCategoryMask = currentSummary.categoryMask,
                        optionalWording = payload.optionalLlmWording ?? string.Empty,
                        optionalFingerprint = payload.optionalLlmFingerprint ?? string.Empty,
                        optionalFormatRevision = payload.optionalLlmFormatRevision,
                        optionalCategoryMask = payload.optionalLlmCategoryMask,
                        optionalSucceeded = string.Equals(
                            payload.lastWordingDispositionToken,
                            MemoryOptionalWordingDispositionTokens.Success,
                            StringComparison.Ordinal)
                    };
                }
            }
            else if (!block.playerEdited)
            {
                MemoryBlockWordingCurrentSnapshot currentBlock =
                    CurrentBlockWordingSnapshot(root, block, policy);
                if (currentBlock != null)
                {
                    candidate.naturalWording = new MemoryRecallNaturalWordingSnapshot
                    {
                        currentProjectionFingerprint =
                            currentBlock.projectionFingerprint ?? string.Empty,
                        currentFormatRevision = currentBlock.wordingFormatRevision,
                        currentCategoryMask = currentBlock.categoryMask,
                        optionalWording = block.optionalLlmWording ?? string.Empty,
                        optionalFingerprint = block.optionalLlmFingerprint ?? string.Empty,
                        optionalFormatRevision = block.optionalLlmFormatRevision,
                        optionalCategoryMask = block.optionalLlmCategoryMask,
                        optionalSucceeded = !string.IsNullOrWhiteSpace(
                            block.optionalLlmWording)
                    };
                }
            }
            return candidate;
        }

        private static MemoryRecallPromptProjection RenderRecallV2Projection(
            PawnKnowledgeState owner,
            MemoryRecallSelectionResultV2 selected,
            string writingFormat,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            KnowledgePolicySnapshot legacyPolicy)
        {
            int maximumCharacters = Math.Max(1, legacyPolicy?.relevantPastMaxChars ?? 500);
            string background = string.Empty;
            if (MemoryContextPrompt.AllowsPawnBackground(
                    writingFormat,
                    policy.usePawnBackground)
                && !string.IsNullOrWhiteSpace(owner.playerBackground))
            {
                background = ImportantMemoryLineRenderer.FormatBackground(
                    owner.playerBackground,
                    legacyPolicy?.backgroundMemoryLineFormat ?? string.Empty);
                if (background.Length > maximumCharacters) background = string.Empty;
            }
            int remaining = Math.Max(
                0,
                maximumCharacters - (background.Length == 0 ? 0 : background.Length + 1));
            var lines = new List<MemoryRecallPromptLine>();
            for (int index = 0; selected?.selected != null
                && index < selected.selected.Count; index++)
            {
                MemoryRecallPromptLine line = ImportantMemoryLineRenderer.RenderV2(
                    selected.selected[index],
                    Math.Max(1, legacyPolicy?.fallbackSummaryMaxChars ?? 240),
                    Math.Max(1, tuning?.memoryCurrentStateMaximumCharacters ?? 240));
                if (line != null) lines.Add(line);
            }
            MemoryRecallPromptProjection episodic = MemoryContextPrompt.ProjectV2(
                writingFormat,
                string.Empty,
                legacyPolicy?.currentStateInstruction ?? string.Empty,
                lines,
                remaining,
                MemoryDispatchPolicy.MaximumEvidencePerVariant,
                MemoryDispatchPolicy.MaximumGuardsPerVariant,
                MemoryDispatchPolicy.MaximumDiagnosticsPerVariant);
            episodic.text = background.Length == 0
                ? episodic.text
                : episodic.text.Length == 0
                    ? background
                    : background + "\n" + episodic.text;
            return episodic;
        }

        /// <summary>
        /// Freezes one normal-diary logical request from the exact already-rendered lane prompts.
        /// A Compact/background-only lane carries an empty receipt; the request exists only when at
        /// least one variant actually contains episodic evidence.
        /// </summary>
        private bool TryBuildNormalMemoryRequestForPromptVariants(
            DiaryEvent diaryEvent,
            string povRole,
            Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants,
            out SavedActiveLogicalRequestV1 saved,
            out bool hadRecallEvidence)
        {
            saved = null;
            hadRecallEvidence = false;
            if (diaryEvent == null
                || promptVariants == null
                || promptVariants.Count == 0) return false;

            var frozen = new List<MemoryOptionalPromptVariantInput>();
            foreach (KeyValuePair<ApiLaneIdentity, LlmPromptVariant> pair in promptVariants)
            {
                LlmPromptVariant prompt = pair.Value;
                if (prompt == null) return false;
                string writingFormat = RecallWritingFormat(prompt.contextDetailLevel);
                MemoryRecallPromptProjection projection = CachedRecallProjection(
                    diaryEvent.eventId,
                    povRole,
                    writingFormat) ?? new MemoryRecallPromptProjection();
                // The effective prompt factory consumed this exact role/format projection in the
                // immediately preceding call. Bind its detached receipt directly; substring search
                // is ambiguous when ordinary prompt text happens to contain the same wording.
                bool hasProjection = !string.IsNullOrWhiteSpace(projection.text);
                List<MemoryEvidenceIdentity> evidence = hasProjection
                    ? projection.evidence
                    : new List<MemoryEvidenceIdentity>();
                List<MemoryGuardIdentity> guards = hasProjection
                    ? projection.guards
                    : new List<MemoryGuardIdentity>();
                List<MemoryDiagnosticIdentity> diagnostics = hasProjection
                    ? projection.diagnostics
                    : new List<MemoryDiagnosticIdentity>();
                hadRecallEvidence |= evidence != null && evidence.Count > 0;

                var candidate = new MemoryOptionalPromptVariantInput
                {
                    templateIdentity = "normal-diary-recall-v2",
                    contextDetailIdentity = RecallContextIdentity(prompt.contextDetailLevel),
                    systemPrompt = prompt.systemPrompt ?? string.Empty,
                    userPrompt = prompt.rawText ?? string.Empty,
                    evidence = evidence ?? new List<MemoryEvidenceIdentity>(),
                    guards = guards ?? new List<MemoryGuardIdentity>(),
                    diagnostics = diagnostics ?? new List<MemoryDiagnosticIdentity>()
                };
                MemoryOptionalPromptVariantInput duplicate = FindExactPromptVariant(
                    frozen,
                    candidate.systemPrompt,
                    candidate.userPrompt);
                if (duplicate != null)
                {
                    // Equal transport bytes cannot name two different receipt plans. Refuse memory
                    // rather than selecting by lane enumeration order.
                    if (!SameRecallReceipt(duplicate, candidate)) return false;
                    continue;
                }
                frozen.Add(candidate);
            }

            if (!hadRecallEvidence) return true;
            // Inspect the already-rendered projections before validating mutable owner state. If
            // that state is malformed, QueuePrompt must know it is discarding visible recall and
            // rebuild every lane memory-free instead of sending unreceipted evidence.
            string ownerPawnId = diaryEvent.PawnIdForRole(povRole) ?? string.Empty;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(ownerPawnId);
            if (owner == null
                || string.IsNullOrWhiteSpace(owner.autobiographicalEpochToken)
                || owner.requestCancellationGeneration <= 0
                || owner.requestCancellationGeneration == long.MaxValue) return false;
            long nextSequence;
            if (!TryPlanNextMemoryLogicalRequestSequence(out nextSequence)) return false;
            var build = new MemoryOptionalRequestBuildInput
            {
                logicalRequestSequence = nextSequence,
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                sessionId = LlmClient.CurrentSessionId,
                opportunityKey = diaryEvent.eventId ?? string.Empty,
                povRoleToken = povRole ?? string.Empty,
                ownerPawnId = owner.pawnId ?? string.Empty,
                ownerEpochToken = owner.autobiographicalEpochToken ?? string.Empty,
                ownerCancellationGeneration = owner.requestCancellationGeneration,
                // Non-optional requests carry a positive inert value. CurrentFence intentionally
                // compares it to the saved row itself; settings cancellation never targets it.
                globalCancellationGeneration = 1,
                optionalRequestInvalidationGeneration = 0,
                variants = frozen
            };
            MemoryLogicalRequestSnapshot snapshot;
            if (!MemoryOptionalAiPolicy.TryBuildLogicalRequest(build, out snapshot)
                || !MemoryDispatchSavedAdapter.TryCreateSavedRequest(snapshot, out saved)
                || !CanAdmitActiveMemoryRequest(saved))
            {
                saved = null;
                return false;
            }
            for (int index = 0; index < frozen.Count; index++)
            {
                string key = VariantKeyForExactPrompt(
                    saved,
                    frozen[index].systemPrompt,
                    frozen[index].userPrompt);
                if (string.IsNullOrWhiteSpace(key))
                {
                    saved = null;
                    return false;
                }
            }
            return true;
        }

        private static MemoryOptionalPromptVariantInput FindExactPromptVariant(
            List<MemoryOptionalPromptVariantInput> variants,
            string systemPrompt,
            string userPrompt)
        {
            for (int index = 0; variants != null && index < variants.Count; index++)
            {
                MemoryOptionalPromptVariantInput variant = variants[index];
                if (variant != null
                    && variant.systemPrompt == systemPrompt
                    && variant.userPrompt == userPrompt) return variant;
            }
            return null;
        }

        private static bool SameRecallReceipt(
            MemoryOptionalPromptVariantInput left,
            MemoryOptionalPromptVariantInput right)
        {
            string leftEvidence;
            string rightEvidence;
            string leftReceipt;
            string rightReceipt;
            string leftDiagnostics;
            string rightDiagnostics;
            return MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    left.evidence,
                    out leftEvidence)
                && MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    right.evidence,
                    out rightEvidence)
                && leftEvidence == rightEvidence
                && MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    left.evidence,
                    left.guards,
                    out leftReceipt)
                && MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    right.evidence,
                    right.guards,
                    out rightReceipt)
                && leftReceipt == rightReceipt
                && MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    left.diagnostics,
                    out leftDiagnostics)
                && MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    right.diagnostics,
                    out rightDiagnostics)
                && leftDiagnostics == rightDiagnostics;
        }

        private static string RecallContextIdentity(PromptContextDetailLevel level)
        {
            PromptContextDetailLevel normalized = PromptContextSelector.Normalize(level);
            if (normalized == PromptContextDetailLevel.Balanced) return "Balanced";
            if (normalized == PromptContextDetailLevel.Compact) return "Compact";
            return "Full";
        }

        private void ApplyCurrentTruth(
            MemoryRecallCandidateSnapshot candidate,
            PawnKnowledgeState owner,
            DiaryKnowledgeTuningDef tuning)
        {
            SavedMemoryAwarenessSnapshot best = null;
            for (int index = 0; owner.ownerAwarenessSnapshots != null
                && index < owner.ownerAwarenessSnapshots.Count; index++)
            {
                SavedMemoryAwarenessSnapshot row = owner.ownerAwarenessSnapshots[index];
                if (row == null
                    || row.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                    || row.stateFacts == null
                    || row.stateFacts.Count == 0
                    || !CandidateHasSubject(
                        candidate,
                        row.subjectKind,
                        row.subjectId)) continue;
                if (best == null
                    || row.lastObservedTick > best.lastObservedTick
                    || (row.lastObservedTick == best.lastObservedTick
                        && string.CompareOrdinal(row.snapshotId, best.snapshotId) < 0))
                    best = row;
            }
            if (best == null) return;
            string text = RenderCurrentState(best, tuning);
            if (string.IsNullOrWhiteSpace(text)) return;
            candidate.currentStateApplicable = true;
            // Always present saved current truth as authoritative alongside history. This avoids
            // guessing whether arbitrary wording contradicts a structured current-state stream.
            candidate.currentStateContradictsHistorical = true;
            candidate.currentStateCanRender = true;
            candidate.currentStateText = text;
            candidate.currentStateSourceId = best.snapshotId ?? string.Empty;
        }

        private static string RenderCurrentState(
            SavedMemoryAwarenessSnapshot row,
            DiaryKnowledgeTuningDef tuning)
        {
            int maximumFacts = Math.Max(1, tuning?.memoryCurrentStateMaximumFacts ?? 8);
            int maximumCharacters = Math.Max(
                1,
                tuning?.memoryCurrentStateMaximumCharacters ?? 240);
            var builder = new StringBuilder("current_state:");
            if (!string.IsNullOrWhiteSpace(row.factStreamToken))
                builder.Append(' ').Append(OneLine(row.factStreamToken));
            int emitted = 0;
            for (int index = 0; row.stateFacts != null
                && index < row.stateFacts.Count
                && emitted < maximumFacts; index++)
            {
                SavedMemoryStateFact fact = row.stateFacts[index];
                if (fact == null
                    || string.IsNullOrWhiteSpace(fact.factKey)
                    || string.IsNullOrWhiteSpace(fact.factValue)) continue;
                string addition = (emitted == 0 ? " " : "; ")
                    + OneLine(fact.factKey) + "=" + OneLine(fact.factValue);
                if (builder.Length + addition.Length > maximumCharacters) break;
                builder.Append(addition);
                emitted++;
            }
            return emitted == 0 ? string.Empty : builder.ToString();
        }

        private void AddStructuralRecallGuards(
            MemoryRecallCandidateSnapshot candidate,
            PawnKnowledgeState owner,
            MemoryRecallQueryV2 query,
            MemoryRecallReservationView reservations)
        {
            if (!string.IsNullOrEmpty(candidate.rootId))
                AddStructuralRecallGuard(
                    candidate,
                    owner,
                    MemoryRepetitionGuardKinds.Root,
                    MemoryRepetitionGuardPolicy.RootKey(candidate.rootId),
                    reservations);
            if (!string.IsNullOrEmpty(candidate.chapterOrNoveltyId))
                AddStructuralRecallGuard(
                    candidate,
                    owner,
                    MemoryRepetitionGuardKinds.Novelty,
                    MemoryRepetitionGuardPolicy.NoveltyKey(
                        candidate.rootId,
                        candidate.chapterOrNoveltyId),
                    reservations);
            var seenSubjects = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; candidate.exactRoutes != null
                && index < candidate.exactRoutes.Count; index++)
            {
                MemoryRecallRouteIdentity route = candidate.exactRoutes[index];
                if (route == null || string.IsNullOrEmpty(route.subjectKind)) continue;
                string key = MemoryRepetitionGuardPolicy.SubjectKey(
                    route.subjectKind,
                    route.subjectId);
                if (seenSubjects.Add(key))
                    AddStructuralRecallGuard(
                        candidate,
                        owner,
                        MemoryRepetitionGuardKinds.Subject,
                        key,
                        reservations);
            }
            if (!string.IsNullOrWhiteSpace(query.pairCounterpartPawnId))
                AddStructuralRecallGuard(
                    candidate,
                    owner,
                    MemoryRepetitionGuardKinds.Pair,
                    MemoryRepetitionGuardPolicy.PairKey(
                        query.ownerPawnId,
                        query.pairCounterpartPawnId),
                    reservations);
        }

        private static void AddStructuralRecallGuard(
            MemoryRecallCandidateSnapshot candidate,
            PawnKnowledgeState owner,
            string kind,
            string key,
            MemoryRecallReservationView reservations)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            candidate.requiredStructuralGuards.Add(new MemoryGuardIdentity
            {
                guardKind = kind,
                guardKey = key
            });
            SavedMemoryRepetitionGuardRow saved;
            reservations.savedGuardRows.TryGetValue(kind + "\n" + key, out saved);
            candidate.structuralGuardStates.Add(new MemoryRepetitionGuardState
            {
                ownerEpochToken = owner.autobiographicalEpochToken ?? string.Empty,
                guardKind = kind,
                guardKey = key,
                lastAutomaticIncludedTick = saved?.lastAutomaticIncludedTick ?? 0,
                lastAutomaticIncludedEntryOrdinal =
                    saved?.lastAutomaticIncludedEntryOrdinal ?? 0,
                automaticInclusionCount = saved?.automaticInclusionCount ?? 0,
                reserved = reservations.guardTuples.Contains(kind + "\n" + key)
            });
        }

        private MemoryRecallReservationView SnapshotRecallReservations(PawnKnowledgeState owner)
        {
            var result = new MemoryRecallReservationView();
            string ownerPawnId = owner?.pawnId ?? string.Empty;
            string ownerEpochToken = owner?.autobiographicalEpochToken ?? string.Empty;
            for (int index = 0; owner?.repetitionGuardRows != null
                && index < owner.repetitionGuardRows.Count; index++)
            {
                SavedMemoryRepetitionGuardRow row = owner.repetitionGuardRows[index];
                if (row == null) continue;
                string tuple = (row.guardKind ?? string.Empty) + "\n"
                    + (row.guardKey ?? string.Empty);
                if (!result.savedGuardRows.ContainsKey(tuple))
                    result.savedGuardRows.Add(tuple, row);
            }
            AddRecallReservations(
                result,
                activeMemoryCoordinatorRequests,
                ownerPawnId,
                ownerEpochToken);
            IReadOnlyList<DiaryEvent> hot = events?.AllEvents;
            for (int index = 0; hot != null && index < hot.Count; index++)
            {
                AddRecallReservation(result, hot[index]?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.InitiatorRole), ownerPawnId, ownerEpochToken);
                AddRecallReservation(result, hot[index]?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.RecipientRole), ownerPawnId, ownerEpochToken);
                AddRecallReservation(result, hot[index]?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.NeutralRole), ownerPawnId, ownerEpochToken);
            }
            return result;
        }

        private static void AddRecallReservations(
            MemoryRecallReservationView target,
            List<SavedActiveLogicalRequestV1> requests,
            string ownerPawnId,
            string ownerEpochToken)
        {
            for (int index = 0; requests != null && index < requests.Count; index++)
                AddRecallReservation(target, requests[index], ownerPawnId, ownerEpochToken);
        }

        private static void AddRecallReservation(
            MemoryRecallReservationView target,
            SavedActiveLogicalRequestV1 request,
            string ownerPawnId,
            string ownerEpochToken)
        {
            if (request == null
                || request.ownerPawnId != ownerPawnId
                || request.ownerEpochToken != ownerEpochToken) return;
            for (int index = 0; request.reservedEvidenceEntries != null
                && index < request.reservedEvidenceEntries.Count; index++)
            {
                string recordId = request.reservedEvidenceEntries[index]?.recordId;
                if (!string.IsNullOrWhiteSpace(recordId))
                    target.evidenceRecordIds.Add(recordId);
            }
            for (int index = 0; request.reservedGuardEntries != null
                && index < request.reservedGuardEntries.Count; index++)
            {
                SavedFrozenGuardEntryV1 guard = request.reservedGuardEntries[index];
                if (guard != null)
                    target.guardTuples.Add((guard.guardKind ?? string.Empty)
                        + "\n" + (guard.guardKey ?? string.Empty));
            }
        }

        private static void AddCandidateRoutes(
            MemoryRecallCandidateSnapshot candidate,
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            int enabledSummaryMask)
        {
            if (root != null)
                AddRecallRoute(candidate.exactRoutes, root.subjectKind, root.subjectId);
            if (block.kind == MemoryContractTokens.KindSummary)
            {
                HashSet<string> enabledSubjectRefs = EnabledSummarySubjectRefs(
                    block.summaryPayload, enabledSummaryMask);
                for (int index = 0; block.summaryPayload?.subjectRefs != null
                    && index < block.summaryPayload.subjectRefs.Count; index++)
                {
                    SavedMemorySubjectRef subject = block.summaryPayload.subjectRefs[index];
                    if (subject != null && enabledSubjectRefs.Contains(subject.subjectRefId ?? string.Empty))
                    {
                        AddRecallRoute(
                            candidate.exactRoutes,
                            subject.subjectKind,
                            subject.subjectId);
                    }
                }
                return;
            }
            if (block.primarySubject != null)
                AddRecallRoute(
                    candidate.exactRoutes,
                    block.primarySubject.subjectKind,
                    block.primarySubject.subjectId);
            for (int index = 0; block.secondarySubjects != null
                && index < block.secondarySubjects.Count; index++)
            {
                SavedMemorySubjectRef subject = block.secondarySubjects[index];
                AddRecallRoute(
                    candidate.exactRoutes,
                    subject?.subjectKind,
                    subject?.subjectId);
            }
        }

        private static void AddRecallRoute(
            List<MemoryRecallRouteIdentity> target,
            string subjectKind,
            string subjectId)
        {
            ImportantMemorySelector.TryAddExactRoute(target, subjectKind, subjectId);
        }

        private static void AddRepresentedSources(
            MemoryRecallCandidateSnapshot candidate,
            SavedMemoryBlock block,
            int enabledSummaryMask)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal)
            {
                block.sourceOccurrenceId ?? string.Empty
            };
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int contributionIndex = 0; bucket?.contributions != null
                    && contributionIndex < bucket.contributions.Count; contributionIndex++)
                {
                    string source = bucket.contributions[contributionIndex]
                        ?.sourceOccurrenceId ?? string.Empty;
                    SavedMemoryFactContribution contribution =
                        bucket.contributions[contributionIndex];
                    if (!SummaryContributionEnabled(contribution, enabledSummaryMask)) continue;
                    if (!string.IsNullOrWhiteSpace(source) && seen.Add(source))
                        candidate.representedSourceOccurrenceIds.Add(source);
                }
            }
            candidate.representedSourceOccurrenceIds.Sort(StringComparer.Ordinal);
        }

        private static void AddCandidateCategories(
            MemoryRecallCandidateSnapshot candidate,
            SavedMemoryBlock block,
            int enabledSummaryMask)
        {
            if (block.kind == MemoryContractTokens.KindSummary)
            {
                AddEnabledCategories(candidate.categories, enabledSummaryMask);
                return;
            }
            if (MemoryContractTokens.IsKnownCategory(block.category))
                candidate.categories.Add(block.category);
        }

        private static HashSet<string> EnabledSummarySubjectRefs(
            SavedMemorySummaryPayload payload,
            int enabledSummaryMask)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int bucketIndex = 0; payload?.factBuckets != null
                && bucketIndex < payload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = payload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (!SummaryContributionEnabled(contribution, enabledSummaryMask)) continue;
                    for (int subjectIndex = 0; contribution.subjectRefIds != null
                        && subjectIndex < contribution.subjectRefIds.Count; subjectIndex++)
                    {
                        string subjectRefId = contribution.subjectRefIds[subjectIndex] ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(subjectRefId)) result.Add(subjectRefId);
                    }
                }
            }
            return result;
        }

        private static string HighestEnabledSummaryImportance(
            SavedMemorySummaryPayload payload,
            int enabledSummaryMask)
        {
            string result = string.Empty;
            for (int bucketIndex = 0; payload?.factBuckets != null
                && bucketIndex < payload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = payload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (SummaryContributionEnabled(contribution, enabledSummaryMask)
                        && RecallImportanceRank(contribution.importance)
                            > RecallImportanceRank(result))
                        result = contribution.importance;
                }
            }
            return result;
        }

        private static long LatestEnabledSummaryTick(
            SavedMemorySummaryPayload payload,
            int enabledSummaryMask)
        {
            long result = 0;
            for (int bucketIndex = 0; payload?.factBuckets != null
                && bucketIndex < payload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = payload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (SummaryContributionEnabled(contribution, enabledSummaryMask)
                        && !contribution.ageUnknown)
                        result = Math.Max(result, contribution.originalEventTick);
                }
            }
            return result;
        }

        private static bool SummaryContributionEnabled(
            SavedMemoryFactContribution contribution,
            int enabledSummaryMask)
        {
            int bit = MemoryCategoryBits.ForToken(contribution?.category);
            return contribution != null && bit != 0 && (enabledSummaryMask & bit) != 0;
        }

        private static int RecallImportanceRank(string importance)
        {
            if (importance == MemoryContractTokens.ImportanceImportant) return 3;
            if (importance == MemoryContractTokens.ImportanceRegular) return 2;
            if (importance == MemoryContractTokens.ImportanceMinor) return 1;
            return 0;
        }

        private static void AddEnabledCategories(List<string> target, int mask)
        {
            AddCategory(target, mask, MemoryCategoryBits.Personal,
                MemoryContractTokens.CategoryPersonal);
            AddCategory(target, mask, MemoryCategoryBits.Relationships,
                MemoryContractTokens.CategoryRelationships);
            AddCategory(target, mask, MemoryCategoryBits.Family,
                MemoryContractTokens.CategoryFamily);
            AddCategory(target, mask, MemoryCategoryBits.Factions,
                MemoryContractTokens.CategoryFactions);
        }

        private static void AddCategory(
            List<string> target,
            int mask,
            int bit,
            string token)
        {
            if ((mask & bit) != 0 && !target.Contains(token)) target.Add(token);
        }

        private static bool CandidateHasSubject(
            MemoryRecallCandidateSnapshot candidate,
            string subjectKind,
            string subjectId)
        {
            for (int index = 0; candidate?.exactRoutes != null
                && index < candidate.exactRoutes.Count; index++)
            {
                MemoryRecallRouteIdentity route = candidate.exactRoutes[index];
                if (route != null
                    && route.subjectKind == subjectKind
                    && route.subjectId == subjectId) return true;
            }
            return false;
        }

        private static SavedMemoryBlock CurrentThreadProjection(
            SavedMemoryThreadRoot root,
            MemoryPolicySnapshot policy,
            MemoryRecallQueryV2 query)
        {
            SavedMemoryBlock rolling = root?.rollingSummaryBlock;
            SavedMemoryBlock visible = LatestVisibleBlock(root?.visibleBlocks);
            int enabledMask = Math.Max(0, policy?.memoryCategoryMask ?? 0);
            int rollingMask = rolling?.summaryPayload == null
                ? 0
                : rolling.summaryPayload.derivedCategoryMask & enabledMask;
            bool rollingUsable = rolling != null && !rolling.suppressed && rollingMask != 0;
            long rollingTick = rollingUsable
                ? LatestEnabledSummaryTick(rolling.summaryPayload, rollingMask)
                : -1;
            int visibleBit = visible == null ? 0 : MemoryCategoryBits.ForToken(visible.category);
            bool visibleUsable = visible != null && !visible.suppressed
                && (visibleBit & enabledMask) != 0
                && !MemoryThreadReducer.IsExpired(
                    query?.repetitionPolicy?.currentTick ?? 0,
                    visible.originalEventTick,
                    visible.ageUnknown,
                    visible.importance,
                    policy?.minorMemoryLifetimeTicks ?? 0,
                    policy?.regularMemoryLifetimeTicks ?? 0);
            return MemoryThreadLookupPolicy.UseRollingCurrentProjection(
                    rollingUsable, rollingTick, visibleUsable,
                    visible?.originalEventTick ?? -1)
                ? rolling
                : visible;
        }

        private static SavedMemoryBlock LatestVisibleBlock(List<SavedMemoryBlock> blocks)
        {
            SavedMemoryBlock best = null;
            for (int index = 0; blocks != null && index < blocks.Count; index++)
            {
                SavedMemoryBlock candidate = blocks[index];
                if (candidate == null) continue;
                if (best == null
                    || candidate.originalEventTick > best.originalEventTick
                    || (candidate.originalEventTick == best.originalEventTick
                        && string.CompareOrdinal(candidate.recordId, best.recordId) > 0))
                    best = candidate;
            }
            return best;
        }

        private static void AddExcludedSelectedSources(
            MemoryRecallQueryV2 query,
            MemoryRecallSelectionResultV2 selection)
        {
            for (int index = 0; selection?.selected != null
                && index < selection.selected.Count; index++)
            {
                MemoryRecallCandidateSnapshot candidate = selection.selected[index]?.candidate;
                AddExcludedSource(query, candidate?.sourceOccurrenceId);
                for (int sourceIndex = 0; candidate?.representedSourceOccurrenceIds != null
                    && sourceIndex < candidate.representedSourceOccurrenceIds.Count; sourceIndex++)
                {
                    AddExcludedSource(
                        query,
                        candidate.representedSourceOccurrenceIds[sourceIndex]);
                }
            }
        }

        private static void AddExcludedSource(MemoryRecallQueryV2 query, string source)
        {
            if (!string.IsNullOrWhiteSpace(source)
                && !query.excludedSourceOccurrenceIds.Contains(source))
                query.excludedSourceOccurrenceIds.Add(source);
        }

        private void CacheFrozenRecallSelection(
            DiaryEvent diaryEvent,
            string povRole,
            MemoryRecallSelectionResultV2 selection)
        {
            string encoded = MemoryFrozenRecallSelectionCodec.Encode(selection);
            diaryEvent?.SetFrozenMemoryRecallSelectionForRole(povRole, encoded);
            string key = RecallSelectionKey(diaryEvent?.eventId, povRole);
            if (!memoryRecallV2FrozenSelectionCache.ContainsKey(key)
                && memoryRecallV2FrozenSelectionCache.Count >= MaximumFrozenRecallSelections)
            {
                // Preserve older pending events. The new event still owns its persisted encoding and
                // can decode it on demand; no unrelated shortlist is evicted wholesale.
                return;
            }
            memoryRecallV2FrozenSelectionCache[key] =
                selection ?? new MemoryRecallSelectionResultV2();
        }

        private MemoryRecallSelectionResultV2 CachedFrozenRecallSelection(
            DiaryEvent diaryEvent,
            string povRole)
        {
            MemoryRecallSelectionResultV2 selection;
            string key = RecallSelectionKey(diaryEvent?.eventId, povRole);
            if (memoryRecallV2FrozenSelectionCache.TryGetValue(key, out selection))
                return selection;
            selection = MemoryFrozenRecallSelectionCodec.Decode(
                diaryEvent?.FrozenMemoryRecallSelectionForRole(povRole));
            if (selection != null
                && memoryRecallV2FrozenSelectionCache.Count < MaximumFrozenRecallSelections)
                memoryRecallV2FrozenSelectionCache[key] = selection;
            return selection;
        }

        /// <summary>Releases event-bound shortlist/projection data once dispatch has frozen receipts.</summary>
        private void ClearMemoryRecallV2EventRole(
            DiaryEvent diaryEvent, string povRole)
        {
            string eventId = diaryEvent?.eventId ?? string.Empty;
            memoryRecallV2FrozenSelectionCache.Remove(RecallSelectionKey(eventId, povRole));
            diaryEvent?.SetFrozenMemoryRecallSelectionForRole(povRole, string.Empty);
            ClearMemoryRecallV2Projections(eventId, povRole);
        }

        private void ClearMemoryRecallV2Projections(string eventId, string povRole)
        {
            string prefix = (eventId ?? string.Empty) + "\n" + (povRole ?? string.Empty) + "\n";
            var keys = new List<string>();
            foreach (string key in memoryRecallV2ProjectionCache.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            for (int index = 0; index < keys.Count; index++)
                memoryRecallV2ProjectionCache.Remove(keys[index]);
        }

        private void CacheRecallProjection(
            string eventId,
            string povRole,
            string writingFormat,
            MemoryRecallPromptProjection projection)
        {
            string key = RecallProjectionKey(eventId, povRole, writingFormat);
            if (!memoryRecallV2ProjectionCache.ContainsKey(key)
                && memoryRecallV2ProjectionCache.Count >= MaximumFrozenRecallProjections)
            {
                // Detached player-entry drafts may project without freezing a selection. Bound that
                // path directly without deleting unrelated pending event-time shortlists.
                memoryRecallV2ProjectionCache.Clear();
            }
            memoryRecallV2ProjectionCache[key] =
                projection ?? new MemoryRecallPromptProjection();
        }

        private MemoryRecallPromptProjection CachedRecallProjection(
            string eventId,
            string povRole,
            string writingFormat)
        {
            MemoryRecallPromptProjection projection;
            return memoryRecallV2ProjectionCache.TryGetValue(
                RecallProjectionKey(eventId, povRole, writingFormat),
                out projection)
                    ? projection
                    : null;
        }

        private static string RecallProjectionKey(
            string eventId,
            string povRole,
            string writingFormat)
        {
            return (eventId ?? string.Empty) + "\n"
                + (povRole ?? string.Empty) + "\n"
                + (writingFormat ?? string.Empty);
        }

        private static string RecallSelectionKey(string eventId, string povRole)
        {
            return (eventId ?? string.Empty) + "\n" + (povRole ?? string.Empty);
        }

        private static string OneLine(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static int NonNegative(int value)
        {
            return Math.Max(0, value);
        }

        private sealed class MemoryRecallReservationView
        {
            public readonly HashSet<string> evidenceRecordIds =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> guardTuples =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, SavedMemoryRepetitionGuardRow> savedGuardRows =
                new Dictionary<string, SavedMemoryRepetitionGuardRow>(StringComparer.Ordinal);
        }
    }
}
