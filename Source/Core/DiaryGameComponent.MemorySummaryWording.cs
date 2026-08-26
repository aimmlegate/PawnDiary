// DiaryGameComponent.MemorySummaryWording.cs — M10 main-thread optional-memory coordinator adapter.
//
// This is not a scheduler. The existing natural-rest ReflectionCoordinator asks this adapter for
// detached meaningful, quiet, and Summary candidates. Any winner stages work through the ordinary
// LlmClient queue, commits its complete saved owner row, and only then activates transport visibility.
// Deterministic Summary facts/wording always exist first and remain canonical on every failure path.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string OptionalMemoryReflectionDefName = "MemoryReflection";
        private readonly MemoryInvokedGenerationCutoffTable invokedGenerationCutoffs =
            new MemoryInvokedGenerationCutoffTable();
        private readonly ConditionalWeakTable<SavedMemoryBlock, SummaryWakeProjectionCache>
            summaryWakeProjectionCache =
                new ConditionalWeakTable<SavedMemoryBlock, SummaryWakeProjectionCache>();

        /// <summary>
        /// Transient memo for the cheap wake hint. Facts/revision/policy changes invalidate it; stable
        /// summaries avoid rebuilding contribution lists and SHA-256 fingerprints every 250 ticks.
        /// </summary>
        private sealed class SummaryWakeProjectionCache
        {
            public SavedMemoryThreadRoot root;
            public long rootStructuralRevision;
            public long factsRevision;
            public int reducerRevision;
            public long formatRevision;
            public int derivedCategoryMask;
            public int policyCategoryMask;
            public bool playerEdited;
            public string lastSettledFingerprint = string.Empty;
            public int lastSettledReducerRevision;
            public long lastSettledFormatRevision;
            public bool changed;
        }

        /// <summary>
        /// Adds all M10 classes to the one existing candidate list. LegacyShadow returns immediately,
        /// so compiling this complete path cannot change public behavior before M11.
        /// </summary>
        private void AddOptionalMemoryCoordinatorCandidates(
            List<ReflectionRuntimeCandidate> candidates,
            Pawn pawn,
            PawnDiaryRecord diary,
            PawnReflectionState reflectionState,
            NarrativePolicySnapshot narrativePolicy,
            MemoryPolicySnapshot memoryPolicy,
            int nowTick)
        {
            if (!MemorySystemActivationGate.IsCurrentRelease
                || candidates == null || pawn == null || diary == null
                || reflectionState == null || memoryPolicy == null
                || !MemoryPolicyIsReconciled()) return;

            PawnKnowledgeState owner = diary.KnowledgeStateOrNull();
            if (owner == null || !owner.IsCurrentSchema()
                || !string.Equals(owner.pawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal))
                return;

            DiaryKnowledgeTuningDef tuning = MemoryOptionalTuning();
            RefreshSummaryWordingOpportunity(owner, memoryPolicy, tuning, nowTick);

            SavedMemoryBlock meaningful = SelectOptionalReflectionBlock(
                owner, memoryPolicy, true, nowTick, tuning,
                optionalMeaningfulEligibilityBaselineTick);
            if (meaningful != null)
            {
                candidates.Add(PrepareMemoryReflectionCandidate(
                    pawn, owner, meaningful, memoryPolicy, tuning, nowTick, false, null));
            }

            SavedMemoryBlock quiet = SelectOptionalReflectionBlock(
                owner, memoryPolicy, false, nowTick, tuning,
                optionalMeaningfulEligibilityBaselineTick);
            MemoryQuietCadencePlan quietPlan = PrepareQuietCadence(
                owner, reflectionState, narrativePolicy, memoryPolicy, tuning,
                quiet != null, nowTick);
            if (quiet != null && quietPlan?.candidateEligible == true)
            {
                candidates.Add(PrepareMemoryReflectionCandidate(
                    pawn, owner, quiet, memoryPolicy, tuning, nowTick, true, quietPlan));
            }

            SavedSummaryWordingOpportunityV1 summary = FindSummaryOpportunity(
                owner.pawnId, owner.autobiographicalEpochToken);
            if (summary != null)
            {
                candidates.Add(PrepareSummaryWordingCandidate(
                    owner, summary, memoryPolicy, tuning, nowTick));
            }
        }

        /// <summary>Evaluates today's private quiet stream without inspecting normal/meaningful work.</summary>
        private static MemoryQuietCadencePlan PrepareQuietCadence(
            PawnKnowledgeState owner,
            PawnReflectionState state,
            NarrativePolicySnapshot narrativePolicy,
            MemoryPolicySnapshot memoryPolicy,
            DiaryKnowledgeTuningDef tuning,
            bool hasEligibleMemory,
            int nowTick)
        {
            int absoluteDay = CurrentDayIndex;
            int absoluteQuadrum = absoluteDay < 0 ? -1 : absoluteDay / 15;
            MemoryQuietCadencePlan plan = MemoryQuietCadencePolicy.Plan(
                new MemoryQuietCadenceRequest
                {
                    ownerPawnId = owner?.pawnId ?? string.Empty,
                    ownerEpochToken = owner?.autobiographicalEpochToken ?? string.Empty,
                    absoluteDay = absoluteDay,
                    absoluteQuadrum = absoluteQuadrum,
                    lastEvaluatedAbsoluteDay = state?.lastQuietMemoryEvaluatedAbsoluteDay ?? -1,
                    lastActivatedAbsoluteQuadrum =
                        state?.lastQuietMemoryActivatedAbsoluteQuadrum ?? -1,
                    lastDecisionKey = state?.lastQuietMemoryDecisionKey ?? string.Empty,
                    chanceBasisPoints = tuning?.quietReflectionChanceBasisPoints ?? 200,
                    standardReflectionEnabled = narrativePolicy?.enabled == true,
                    useMemoriesInWriting = memoryPolicy?.useMemoriesInWriting == true,
                    allowExtraMemoryAiRequests =
                        memoryPolicy?.allowExtraMemoryAiRequests == true,
                    occasionalMemoryReflections =
                        memoryPolicy?.occasionalMemoryReflections == true,
                    optionalRequestInvalidationGeneration =
                        memoryPolicy?.optionalRequestInvalidationGeneration ?? 0,
                    hasEligibleMemory = hasEligibleMemory
                });
            if (state != null && plan.valid && plan.evaluatedNow)
            {
                state.memoryReflectionSchemaVersion = 1;
                state.memoryOwnerEpochToken = owner.autobiographicalEpochToken;
                state.lastQuietMemoryEvaluatedAbsoluteDay = plan.evaluatedAbsoluteDay;
                state.lastQuietMemoryDecisionKey = plan.decisionKey;
            }
            return plan;
        }

        private ReflectionRuntimeCandidate PrepareMemoryReflectionCandidate(
            Pawn pawn,
            PawnKnowledgeState owner,
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            int nowTick,
            bool quiet,
            MemoryQuietCadencePlan quietPlan)
        {
            bool authoritative = CaptureRuleOwnsAuthoritativePage(block.captureRuleId);
            long requestedTick = quiet
                ? Math.Max(0L, nowTick)
                : Math.Max(0L, block.originalEventTick);
            long delay = quiet || !authoritative
                ? 0L
                : Math.Max(1, tuning?.meaningfulMemoryDelayTicks ?? 60000);
            long dueTick = SaturatingAdd(requestedTick, delay);
            long expiryTick = SaturatingAdd(
                dueTick, Math.Max(1, tuning?.optionalMemoryOpportunityExpiryTicks ?? 120000));
            string timing = quiet
                ? MemoryReflectionTimingTokens.Quiet
                : authoritative
                    ? MemoryReflectionTimingTokens.Delayed
                    : MemoryReflectionTimingTokens.Immediate;
            string opportunityKey = MemoryReflectionOpportunityKey(
                owner, block, timing, requestedTick, dueTick, expiryTick,
                policy.optionalRequestInvalidationGeneration);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = CoordinatorOpportunityKindTokens.MemoryReflection,
                    workClass = quiet
                        ? ReflectionWorkClassTokens.QuietMemory
                        : ReflectionWorkClassTokens.MeaningfulMemory,
                    timing = timing,
                    opportunityKey = opportunityKey,
                    pawnId = owner.pawnId,
                    nowTick = nowTick,
                    candidateMemoryCount = 1,
                    importance = block.importance,
                    due = nowTick >= dueTick && nowTick < expiryTick,
                    cooldownSatisfied = true,
                    groupEnabled = true,
                    usesBoundedTiming = true,
                    requestedTick = requestedTick,
                    dueTick = dueTick,
                    expiryTick = expiryTick,
                    configuredPriority = quiet
                        ? tuning?.quietMemoryPriority ?? 50
                        : tuning?.meaningfulMemoryPriority ?? 100,
                    salience = ReflectionBlockSalience(block)
                }
            };
            bool activated = false;
            runtime.dispatch = () =>
            {
                activated = QueueOptionalMemoryRequest(
                    pawn, owner, block, runtime.opportunity,
                    policy, tuning, nowTick);
                return activated
                    ? DiaryDispatchOutcome.ConsumedWithoutPage
                    : DiaryDispatchOutcome.Rejected;
            };
            runtime.consumeAfterDispatch = () =>
            {
                if (!quiet || !activated || quietPlan == null) return;
                int quadrum;
                if (MemoryQuietCadencePolicy.TryCommitActivatedQuadrum(
                        quietPlan, true, true, out quadrum))
                {
                    PawnReflectionState current = FindDiaryByPawnId(owner.pawnId)
                        ?.EnsureReflectionState();
                    if (current != null
                        && string.Equals(current.memoryOwnerEpochToken,
                            owner.autobiographicalEpochToken, StringComparison.Ordinal))
                        current.lastQuietMemoryActivatedAbsoluteQuadrum = quadrum;
                }
            };
            return runtime;
        }

        private ReflectionRuntimeCandidate PrepareSummaryWordingCandidate(
            PawnKnowledgeState owner,
            SavedSummaryWordingOpportunityV1 saved,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            int nowTick)
        {
            SummaryWordingOpportunitySnapshot snapshot = ToSummaryOpportunity(saved);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = CoordinatorOpportunityKindTokens.SummaryWording,
                    workClass = ReflectionWorkClassTokens.SummaryWording,
                    opportunityKey = saved.opportunityKey,
                    pawnId = saved.ownerPawnId,
                    nowTick = nowTick,
                    candidateMemoryCount = 0,
                    due = nowTick >= saved.dueTick && nowTick < saved.expiryTick,
                    cooldownSatisfied = true,
                    groupEnabled = true,
                    usesBoundedTiming = true,
                    requestedTick = saved.requestedTick,
                    dueTick = saved.dueTick,
                    expiryTick = saved.expiryTick,
                    configuredPriority = saved.configuredPriority,
                    salience = saved.salience
                }
            };
            runtime.dispatch = () =>
            {
                SavedMemoryThreadRoot root;
                SavedMemoryBlock block;
                if (!TryFindCurrentSummary(snapshot, out root, out block))
                {
                    RemoveSummaryOpportunity(saved);
                    return DiaryDispatchOutcome.Rejected;
                }
                bool queued = QueueOptionalMemoryRequest(
                    FindLivePawnByLoadId(owner.pawnId), owner, block,
                    runtime.opportunity, policy, tuning, nowTick);
                return queued
                    ? DiaryDispatchOutcome.ConsumedWithoutPage
                    : DiaryDispatchOutcome.Rejected;
            };
            runtime.settleIneligible = () =>
            {
                if (nowTick >= saved.expiryTick)
                {
                    ApplySummaryTerminal(snapshot,
                        MemoryOptionalWordingDispositionTokens.Expired);
                    RemoveSummaryOpportunity(saved);
                }
            };
            return runtime;
        }

        /// <summary>
        /// Uses the common endpoint selection, queue, permit, receipt, and completion service. The
        /// component row commits Activated before LlmClient.Activate can expose work to a worker.
        /// </summary>
        private bool QueueOptionalMemoryRequest(
            Pawn pawn,
            PawnKnowledgeState owner,
            SavedMemoryBlock block,
            ReflectionOpportunity opportunity,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            int nowTick)
        {
            if (pawn == null || owner == null || block == null || opportunity == null
                || !MemoryOptionalAiPolicy.CanStageOptionalRequest(
                    MemoryPolicyIsReconciled(),
                    policy?.AllowsOptionalRequests == true,
                    globalOptionalRequestCancellationGeneration,
                    policy?.optionalRequestInvalidationGeneration ?? 0))
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            List<ApiEndpointConfig> targets = settings?.ActiveEndpoints();
            if (settings == null || targets == null || targets.Count == 0)
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }
            long nextSequence;
            if (!TryPlanNextMemoryLogicalRequestSequence(out nextSequence))
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }

            string reason;
            bool forcePrimary;
            ApiEndpointConfig primary = SelectApiTarget(
                null, DiaryEvent.InitiatorRole, targets, null, string.Empty,
                settings.apiRoutingMode, out reason, out forcePrimary);
            List<ApiEndpointConfig> failovers = DeduplicateOptionalFailovers(
                primary,
                BuildFailoverTargets(targets, primary));
            List<ApiEndpointConfig> lanes = new List<ApiEndpointConfig> { primary };
            lanes.AddRange(failovers);
            string purpose = opportunity.workClass == ReflectionWorkClassTokens.SummaryWording
                ? MemoryDispatchTokens.SummaryWording
                : MemoryDispatchTokens.MemoryReflection;
            MemoryEvidenceIdentity evidence = new MemoryEvidenceIdentity
            {
                recordId = block.recordId,
                sourceOccurrenceId = block.sourceOccurrenceId,
                rootIdOrEmpty = block.rootId ?? string.Empty
            };
            MemoryOptionalRequestBuildInput build = new MemoryOptionalRequestBuildInput
            {
                logicalRequestSequence = nextSequence,
                requestPurposeToken = purpose,
                sessionId = LlmClient.CurrentSessionId,
                opportunityKey = opportunity.opportunityKey,
                povRoleToken = DiaryEvent.InitiatorRole,
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                ownerCancellationGeneration = owner.requestCancellationGeneration,
                globalCancellationGeneration = globalOptionalRequestCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    policy.optionalRequestInvalidationGeneration
            };
            string system = purpose == MemoryDispatchTokens.SummaryWording
                ? tuning?.summaryWordingSystemPrompt ?? string.Empty
                : tuning?.memoryReflectionSystemPrompt ?? string.Empty;
            string baseUser;
            if (purpose == MemoryDispatchTokens.SummaryWording)
            {
                SummaryWordingOpportunitySnapshot summaryIdentity;
                if (!MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                        opportunity.opportunityKey, out summaryIdentity)
                    || !TryBuildSummaryWordingPrompt(
                        block, summaryIdentity, tuning, out baseUser))
                {
                    SettleRejectedOptionalOpportunity(opportunity);
                    return false;
                }
            }
            else
            {
                baseUser = BuildMemoryReflectionPrompt(block, tuning);
            }
            Dictionary<ApiLaneIdentity, LlmPromptVariant> promptVariants =
                new Dictionary<ApiLaneIdentity, LlmPromptVariant>();
            build.variants.Add(new MemoryOptionalPromptVariantInput
            {
                templateIdentity = purpose + ":v1",
                contextDetailIdentity = "optional-memory:v1",
                systemPrompt = system,
                userPrompt = baseUser,
                evidence = new List<MemoryEvidenceIdentity> { evidence }
            });
            for (int index = 0; index < lanes.Count; index++)
            {
                ApiEndpointConfig lane = lanes[index];
                if (lane == null)
                {
                    SettleRejectedOptionalOpportunity(opportunity);
                    return false;
                }
                ApiLaneIdentity laneKey = ApiLaneIdentity.ForGate(
                    lane.url, lane.model, lane.apiMode, lane.authMode,
                    lane.customAuthHeaderName, lane.apiKey);
                if (laneKey.Empty || promptVariants.ContainsKey(laneKey)) continue;
                promptVariants[laneKey] = new LlmPromptVariant
                {
                    systemPrompt = system,
                    rawText = baseUser,
                    contextDetailLevel = settings.EffectiveContextDetailLevel(lane)
                };
            }

            MemoryLogicalRequestSnapshot snapshot;
            SavedActiveLogicalRequestV1 savedRequest;
            if (!MemoryOptionalAiPolicy.TryBuildLogicalRequest(build, out snapshot)
                || !MemoryDispatchSavedAdapter.TryCreateSavedRequest(snapshot, out savedRequest))
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }
            int maxTokens = purpose == MemoryDispatchTokens.SummaryWording
                ? Math.Max(1, tuning?.summaryWordingMaxTokens ?? 80)
                : Math.Max(1, tuning?.memoryReflectionMaxTokens ?? 220);
            LlmGenerationRequest request = new LlmGenerationRequest
            {
                eventId = opportunity.opportunityKey,
                povRole = DiaryEvent.InitiatorRole,
                systemPrompt = system,
                rawText = baseUser,
                endpointUrl = primary.url,
                modelName = primary.model,
                apiKey = primary.apiKey,
                authMode = primary.authMode,
                customAuthHeaderName = primary.customAuthHeaderName,
                apiMode = primary.apiMode,
                reasoningEffort = primary.reasoningEffort,
                reasoningTag = primary.reasoningTag,
                providerModelFamily = primary.ProviderModelFamilyForCurrentLane(),
                forcePrimaryLane = forcePrimary,
                failoverTargets = failovers,
                timeoutSeconds = settings.timeoutSeconds,
                maxTokens = maxTokens,
                lowThinkingHeadroomTokens = DiaryTuning.LowThinkingHeadroomTokens,
                temperature = settings.temperature,
                responseRules = DiaryResponseRules.ForRequest(
                    opportunity.opportunityKey, DiaryEvent.InitiatorRole, false, maxTokens),
                promptVariants = promptVariants
            };
            if (!TryBindMemoryTransportContext(request, savedRequest, promptVariants)
                || !CanAdmitActiveMemoryRequest(savedRequest))
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }

            LlmStagedGenerationRequest staged;
            if (LlmClient.TryStage(request, out staged) != LlmRequestStageOutcome.Staged)
            {
                SettleRejectedOptionalOpportunity(opportunity);
                return false;
            }

            // Commit every canonical owner before publishing transport visibility.
            if (purpose == MemoryDispatchTokens.SummaryWording)
            {
                SummaryWordingOpportunitySnapshot summary;
                if (!MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                        opportunity.opportunityKey, out summary)
                    || !ApplySummaryTerminal(
                        summary, MemoryOptionalWordingDispositionTokens.Activated))
                {
                    LlmClient.CancelStaged(staged);
                    SettleRejectedOptionalOpportunity(opportunity);
                    return false;
                }
                RemoveSummaryOpportunityByKey(opportunity.opportunityKey);
            }
            activeMemoryCoordinatorRequests.Add(savedRequest);
            lastIssuedMemoryLogicalRequestSequence = nextSequence;
            if (!MemoryDispatchSavedAdapter.TryActivate(savedRequest))
            {
                activeMemoryCoordinatorRequests.Remove(savedRequest);
                LlmClient.CancelStaged(staged);
                SettleRejectedOptionalOpportunity(opportunity);
                RebuildMemorySizeIndexes();
                return false;
            }
            RebuildMemorySizeIndexes();
            if (!LlmClient.Activate(staged))
            {
                activeMemoryCoordinatorRequests.Remove(savedRequest);
                // Activate normally cancels a row when the session changed. Calling this again is
                // idempotent and also releases any still-invisible reservation on another refusal.
                LlmClient.CancelStaged(staged);
                AppendTerminalMemoryAttemptAudits(
                    savedRequest, MemoryDispatchTokens.ActivationFailed,
                    Math.Max(1, nowTick));
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    savedRequest.logicalRequestId);
                SettleRejectedOptionalOpportunity(opportunity);
                RebuildMemorySizeIndexes();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Collapses identical provider lanes for optional work. A duplicate settings row is not a
        /// distinct frozen prompt variant or a reason to make the same physical attempt twice.
        /// </summary>
        private static List<ApiEndpointConfig> DeduplicateOptionalFailovers(
            ApiEndpointConfig primary,
            List<ApiEndpointConfig> failovers)
        {
            List<ApiEndpointConfig> result = new List<ApiEndpointConfig>();
            HashSet<ApiLaneIdentity> seen = new HashSet<ApiLaneIdentity>();
            if (primary != null)
            {
                ApiLaneIdentity primaryKey = ApiLaneIdentity.ForGate(
                    primary.url, primary.model, primary.apiMode, primary.authMode,
                    primary.customAuthHeaderName, primary.apiKey);
                if (!primaryKey.Empty) seen.Add(primaryKey);
            }
            for (int index = 0; failovers != null && index < failovers.Count; index++)
            {
                ApiEndpointConfig lane = failovers[index];
                if (lane == null) continue;
                ApiLaneIdentity key = ApiLaneIdentity.ForGate(
                    lane.url, lane.model, lane.apiMode, lane.authMode,
                    lane.customAuthHeaderName, lane.apiKey);
                if (!key.Empty && seen.Add(key)) result.Add(lane);
            }
            return result;
        }

        /// <summary>Routes component-owned completion before the ordinary DiaryEvent lookup.</summary>
        private bool TryApplyMemoryCoordinatorResult(
            LlmGenerationResult result,
            out DiaryTelemetryOutcome telemetry)
        {
            telemetry = DiaryTelemetryOutcome.LlmResultInvalid;
            SavedActiveLogicalRequestV1 saved = FindActiveCoordinatorRequest(
                result?.memoryLogicalRequestId);
            if (saved == null) return false;
            telemetry = DiaryTelemetryOutcome.LlmResultApplied;
            if (!TryBeginMemoryResultApply(saved, result))
            {
                SettleCoordinatorRequest(saved, MemoryDispatchTokens.Invalid, result);
                return true;
            }

            if (saved.requestPurposeToken == MemoryDispatchTokens.SummaryWording)
            {
                ApplySummaryCoordinatorResult(saved, result);
            }
            else if (saved.requestPurposeToken == MemoryDispatchTokens.MemoryReflection)
            {
                ApplyMemoryReflectionCoordinatorResult(saved, result);
            }
            SettleCoordinatorRequest(
                saved,
                result.success ? MemoryDispatchTokens.Success : MemoryDispatchTokens.ProviderError,
                result);
            return true;
        }

        private void ApplySummaryCoordinatorResult(
            SavedActiveLogicalRequestV1 saved,
            LlmGenerationResult result)
        {
            SummaryWordingOpportunitySnapshot opportunity;
            if (!MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                    saved.eventIdOrOpportunityKey, out opportunity)) return;
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            if (!TryFindCurrentSummary(opportunity, out root, out block)) return;
            SummaryWordingCurrentSnapshot current = CurrentSummarySnapshot(root, block,
                MemoryEffectivePolicyProvider.Current);
            SummaryWordingResultPlan plan = MemoryOptionalAiPolicy.PlanSummaryResult(
                opportunity,
                current,
                result.success,
                result.generatedText,
                Math.Max(1, MemoryOptionalTuning()?.fallbackSummaryMaxChars ?? 240));
            if (!plan.identityMatched) return;
            SavedMemorySummaryPayload payload = block.summaryPayload;
            payload.lastWordingDispositionToken = plan.dispositionToken;
            if (plan.applyOptionalWording)
            {
                payload.optionalLlmWording = plan.optionalWording;
                payload.optionalLlmFingerprint = opportunity.projectionFingerprint;
                payload.optionalLlmFormatRevision = opportunity.expectedFormatRevision;
                payload.optionalLlmCategoryMask = opportunity.expectedCategoryMask;
            }
            AdvanceSummaryStatusRevision(FindCurrentMemoryEnvelope(saved.ownerPawnId), root);
        }

        private void ApplyMemoryReflectionCoordinatorResult(
            SavedActiveLogicalRequestV1 saved,
            LlmGenerationResult result)
        {
            if (!result.success || string.IsNullOrWhiteSpace(result.generatedText)) return;
            Pawn pawn = FindLivePawnByLoadId(saved.ownerPawnId);
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(saved.ownerPawnId);
            if (pawn == null || owner == null
                || !string.Equals(owner.autobiographicalEpochToken,
                    saved.ownerEpochToken, StringComparison.Ordinal)) return;
            DiaryKnowledgeTuningDef tuning = MemoryOptionalTuning();
            DiaryEvent page = AddSoloEvent(
                pawn,
                null,
                OptionalMemoryReflectionDefName,
                tuning?.memoryReflectionLabel ?? string.Empty,
                string.Empty,
                tuning?.memoryReflectionInstruction ?? string.Empty,
                "generated_memory_reflection=true");
            if (page != null)
            {
                // Direct application does not parse prose as facts and this Def owns no capture rule,
                // so generated reflections cannot recursively create memory opportunities.
                ApplyExternalDirectEntryText(
                    page, DiaryEvent.InitiatorRole, result.generatedText, string.Empty, false);
            }
        }

        private void SettleCoordinatorRequest(
            SavedActiveLogicalRequestV1 saved,
            string outcome,
            LlmGenerationResult result)
        {
            AppendTerminalMemoryAttemptAudits(
                saved, outcome, Math.Max(1, Verse.Find.TickManager?.TicksGame ?? 0));
            activeMemoryCoordinatorRequests.Remove(saved);
            invokedGenerationCutoffs.Settle(saved.logicalRequestId);
            if (result?.memoryInvocationPermit != null)
                MemoryDispatchRuntimeBridge.ReleaseSendEnvelope(result.memoryInvocationPermit);
            else
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    saved.logicalRequestId);
            RebuildMemorySizeIndexes();
        }

        private SavedActiveLogicalRequestV1 FindActiveCoordinatorRequest(string requestId)
        {
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 row = activeMemoryCoordinatorRequests[index];
                if (row != null && string.Equals(
                    row.logicalRequestId, requestId, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private void RefreshSummaryWordingOpportunity(
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            DiaryKnowledgeTuningDef tuning,
            long nowTick)
        {
            if (owner == null || policy == null || tuning == null || nowTick < 0) return;
            SavedSummaryWordingOpportunityV1 existing = FindSummaryOpportunity(
                owner.pawnId, owner.autobiographicalEpochToken);
            SummaryWordingOpportunitySnapshot winner = existing == null
                ? null : ToSummaryOpportunity(existing);
            List<SummaryWordingTerminalDecision> terminal =
                new List<SummaryWordingTerminalDecision>();
            if (winner != null)
            {
                SummaryWordingSlotPlan retained = MemoryOptionalAiPolicy.PlanOwnerSlot(
                    winner, null, nowTick);
                if (!retained.valid)
                {
                    winner = null;
                }
                else
                {
                    winner = retained.winner;
                    // Expiry settles before scanning current projections. Otherwise the same
                    // unchanged fingerprint could be recreated in this pass before its terminal
                    // marker became visible.
                    for (int index = 0; index < retained.terminal.Count; index++)
                        ApplySummaryTerminal(
                            retained.terminal[index].opportunity,
                            retained.terminal[index].dispositionToken);
                    SavedMemoryThreadRoot retainedRoot;
                    SavedMemoryBlock retainedBlock;
                    if (winner != null && !TryFindCurrentSummary(
                            winner, out retainedRoot, out retainedBlock)) winner = null;
                }
            }
            foreach (SummaryWordingCurrentSnapshot current in CurrentOwnerSummaries(owner, policy))
            {
                SavedMemoryThreadRoot root;
                SavedMemoryBlock block;
                if (!TryFindSummary(owner, current.rootId, current.summaryRecordId,
                        out root, out block)) continue;
                SavedMemorySummaryPayload payload = block.summaryPayload;
                bool unchanged = string.Equals(
                        payload.lastSettledWordingFingerprint,
                        current.projectionFingerprint,
                        StringComparison.Ordinal)
                    && payload.lastSettledWordingReducerRevision == current.reducerRevision
                    && payload.lastSettledWordingFormatRevision == current.formatRevision;
                bool winnerTargetsCurrent = winner != null
                    && MemoryOptionalAiPolicy.TargetsCurrentSummaryProjection(winner, current);
                if (unchanged)
                {
                    if (winnerTargetsCurrent) winner = null;
                    continue;
                }
                if (!policy.AllowsOptionalRequests || current.suppressed)
                {
                    ApplySummaryTerminal(current,
                        MemoryOptionalWordingDispositionTokens.Disabled);
                    if (winnerTargetsCurrent) winner = null;
                    continue;
                }
                // A retained saved row already represents this exact projection. Recreating it with
                // today's tick would continually displace itself and falsely settle the fingerprint.
                if (winnerTargetsCurrent) continue;
                SummaryWordingOpportunitySnapshot incoming = NewSummaryOpportunity(
                    owner, current, tuning, nowTick, policy,
                    globalOptionalRequestCancellationGeneration);
                if (incoming == null) continue;
                SummaryWordingSlotPlan slot = MemoryOptionalAiPolicy.PlanOwnerSlot(
                    winner, incoming, nowTick);
                if (!slot.valid) continue;
                winner = slot.winner;
                terminal.AddRange(slot.terminal);
            }
            for (int index = 0; index < terminal.Count; index++)
                ApplySummaryTerminal(
                    terminal[index].opportunity, terminal[index].dispositionToken);
            ReplaceSummaryOpportunity(owner.pawnId, owner.autobiographicalEpochToken, winner);
        }

        /// <summary>Baselines current summaries on enable, preventing any catch-up opportunity.</summary>
        private void BaselineOptionalSummariesWithoutCatchUp(MemoryPolicySnapshot policy)
        {
            HashSet<PawnKnowledgeState> seen = new HashSet<PawnKnowledgeState>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnKnowledgeState owner = diaries[index]?.KnowledgeStateOrNull();
                if (owner == null || !owner.IsCurrentSchema() || !seen.Add(owner)) continue;
                foreach (SummaryWordingCurrentSnapshot summary in CurrentOwnerSummaries(owner, policy))
                    ApplySummaryTerminal(summary, MemoryOptionalWordingDispositionTokens.Disabled);
            }
            summaryWordingOpportunities.Clear();
        }

        /// <summary>Enabling quiet work observes today without rolling or creating a catch-up page.</summary>
        private void BaselineQuietCadenceWithoutCatchUp()
        {
            int day = CurrentDayIndex;
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                PawnKnowledgeState owner = diary?.KnowledgeStateOrNull();
                if (owner == null || !owner.IsCurrentSchema()) continue;
                PawnReflectionState reflection = diary.EnsureReflectionState();
                reflection.memoryReflectionSchemaVersion = 1;
                reflection.memoryOwnerEpochToken = owner.autobiographicalEpochToken;
                reflection.lastQuietMemoryEvaluatedAbsoluteDay = day;
                reflection.lastQuietMemoryDecisionKey =
                    MemoryDeterministicRngV1.CreateDecisionKey(
                        owner.pawnId, owner.autobiographicalEpochToken, day, false);
            }
        }

        /// <summary>Deterministically repairs duplicate/malformed saved owner slots after load.</summary>
        private void RepairLoadedSummaryWordingOpportunities()
        {
            Dictionary<string, SummaryWordingOpportunitySnapshot> winners =
                new Dictionary<string, SummaryWordingOpportunitySnapshot>(StringComparer.Ordinal);
            List<SummaryWordingTerminalDecision> terminal =
                new List<SummaryWordingTerminalDecision>();
            long nowTick = Math.Max(0, Verse.Find.TickManager?.TicksGame ?? 0);
            for (int index = 0; summaryWordingOpportunities != null
                && index < summaryWordingOpportunities.Count; index++)
            {
                SummaryWordingOpportunitySnapshot row =
                    ToSummaryOpportunity(summaryWordingOpportunities[index]);
                if (!MemoryOptionalAiPolicy.IsValidSummaryOpportunity(row)) continue;
                SummaryWordingSlotPlan admitted = MemoryOptionalAiPolicy.PlanOwnerSlot(
                    null, row, nowTick);
                if (!admitted.valid) continue;
                terminal.AddRange(admitted.terminal);
                row = admitted.winner;
                if (row == null) continue;
                SavedMemoryThreadRoot currentRoot;
                SavedMemoryBlock currentBlock;
                if (!TryFindCurrentSummary(row, out currentRoot, out currentBlock)) continue;
                SavedMemorySummaryPayload currentPayload = currentBlock.summaryPayload;
                if (currentPayload != null
                    && string.Equals(currentPayload.lastSettledWordingFingerprint,
                        row.projectionFingerprint, StringComparison.Ordinal)
                    && currentPayload.lastSettledWordingReducerRevision
                        == row.expectedReducerRevision
                    && currentPayload.lastSettledWordingFormatRevision
                        == row.expectedFormatRevision) continue;
                string ownerKey = OrdinalSegmentCodec.Segment(row.ownerPawnId)
                    + OrdinalSegmentCodec.Segment(row.ownerEpochToken);
                SummaryWordingOpportunitySnapshot prior;
                if (!winners.TryGetValue(ownerKey, out prior))
                {
                    winners.Add(ownerKey, row);
                    continue;
                }
                SummaryWordingSlotPlan plan = MemoryOptionalAiPolicy.PlanOwnerSlot(
                    prior, row, nowTick);
                if (!plan.valid) continue;
                winners[ownerKey] = plan.winner;
                terminal.AddRange(plan.terminal);
            }
            for (int index = 0; index < terminal.Count; index++)
                ApplySummaryTerminal(
                    terminal[index].opportunity, terminal[index].dispositionToken);
            summaryWordingOpportunities.Clear();
            foreach (KeyValuePair<string, SummaryWordingOpportunitySnapshot> pair in winners)
            {
                bool pendingChanged;
                if (pair.Value != null && ApplySummaryPending(pair.Value, out pendingChanged))
                    summaryWordingOpportunities.Add(FromSummaryOpportunity(pair.Value));
            }
            summaryWordingOpportunities.Sort(CompareSavedSummaryOpportunities);
            RebuildMemorySizeIndexes();
        }

        /// <summary>
        /// Expires component-owned Summary rows even when their owner is no longer a resting colonist.
        /// This is ordinary main-thread coordinator maintenance, not a ticker or worker queue.
        /// </summary>
        private void ExpireSummaryWordingOpportunities(long nowTick)
        {
            bool changed = false;
            for (int index = (summaryWordingOpportunities?.Count ?? 0) - 1;
                index >= 0; index--)
            {
                SavedSummaryWordingOpportunityV1 saved = summaryWordingOpportunities[index];
                SummaryWordingOpportunitySnapshot row = ToSummaryOpportunity(saved);
                if (MemoryOptionalAiPolicy.IsValidSummaryOpportunity(row)
                    && nowTick < row.expiryTick) continue;
                if (MemoryOptionalAiPolicy.IsValidSummaryOpportunity(row))
                    ApplySummaryTerminal(
                        row, MemoryOptionalWordingDispositionTokens.Expired);
                summaryWordingOpportunities.RemoveAt(index);
                changed = true;
            }
            if (changed) RebuildMemorySizeIndexes();
        }

        private static int CompareSavedSummaryOpportunities(
            SavedSummaryWordingOpportunityV1 left,
            SavedSummaryWordingOpportunityV1 right)
        {
            int compared = string.CompareOrdinal(left?.ownerPawnId, right?.ownerPawnId);
            if (compared != 0) return compared;
            compared = string.CompareOrdinal(left?.ownerEpochToken, right?.ownerEpochToken);
            return compared != 0
                ? compared : string.CompareOrdinal(left?.opportunityKey, right?.opportunityKey);
        }

        private bool ApplySummaryTerminal(
            SummaryWordingOpportunitySnapshot opportunity,
            string disposition)
        {
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            if (!MemoryOptionalWordingDispositionTokens.IsKnown(disposition)
                || !TryFindCurrentSummary(opportunity, out root, out block)) return false;
            SavedMemorySummaryPayload payload = block.summaryPayload;
            payload.lastSettledWordingFingerprint = opportunity.projectionFingerprint;
            payload.lastSettledWordingReducerRevision = opportunity.expectedReducerRevision;
            payload.lastSettledWordingFormatRevision = opportunity.expectedFormatRevision;
            payload.lastWordingDispositionToken = disposition;
            payload.optionalLlmWording = string.Empty;
            payload.optionalLlmFingerprint = string.Empty;
            payload.optionalLlmFormatRevision = 0;
            payload.optionalLlmCategoryMask = 0;
            AdvanceSummaryStatusRevision(
                FindCurrentMemoryEnvelope(opportunity.ownerPawnId), root);
            return true;
        }

        /// <summary>
        /// Marks a retained saved row pending without settling its fingerprint. This distinction is
        /// what lets the first activation commit the terminal fingerprint exactly once.
        /// </summary>
        private bool ApplySummaryPending(
            SummaryWordingOpportunitySnapshot opportunity,
            out bool changed)
        {
            changed = false;
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            if (!TryFindCurrentSummary(opportunity, out root, out block)) return false;
            SavedMemorySummaryPayload payload = block.summaryPayload;
            changed = !string.Equals(payload.lastWordingDispositionToken,
                    MemoryOptionalWordingDispositionTokens.Pending, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(payload.optionalLlmWording)
                || !string.IsNullOrEmpty(payload.optionalLlmFingerprint)
                || payload.optionalLlmFormatRevision != 0
                || payload.optionalLlmCategoryMask != 0;
            if (!changed) return true;
            payload.lastWordingDispositionToken =
                MemoryOptionalWordingDispositionTokens.Pending;
            payload.optionalLlmWording = string.Empty;
            payload.optionalLlmFingerprint = string.Empty;
            payload.optionalLlmFormatRevision = 0;
            payload.optionalLlmCategoryMask = 0;
            AdvanceSummaryStatusRevision(
                FindCurrentMemoryEnvelope(opportunity.ownerPawnId), root);
            return true;
        }

        private bool ApplySummaryTerminal(
            SummaryWordingCurrentSnapshot current,
            string disposition)
        {
            SavedMemoryThreadRoot root;
            SavedMemoryBlock block;
            if (current == null
                || !TryFindSummary(FindCurrentMemoryEnvelope(current.ownerPawnId),
                    current.rootId, current.summaryRecordId, out root, out block)) return false;
            SavedMemorySummaryPayload payload = block.summaryPayload;
            payload.lastSettledWordingFingerprint = current.projectionFingerprint;
            payload.lastSettledWordingReducerRevision = current.reducerRevision;
            payload.lastSettledWordingFormatRevision = current.formatRevision;
            payload.lastWordingDispositionToken = disposition;
            payload.optionalLlmWording = string.Empty;
            payload.optionalLlmFingerprint = string.Empty;
            payload.optionalLlmFormatRevision = 0;
            payload.optionalLlmCategoryMask = 0;
            AdvanceSummaryStatusRevision(
                FindCurrentMemoryEnvelope(current.ownerPawnId), root);
            return true;
        }

        private void SettleRejectedOptionalOpportunity(ReflectionOpportunity opportunity)
        {
            if (opportunity?.workClass != ReflectionWorkClassTokens.SummaryWording) return;
            SummaryWordingOpportunitySnapshot summary;
            if (MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                    opportunity.opportunityKey, out summary))
                ApplySummaryTerminal(summary, MemoryOptionalWordingDispositionTokens.Failed);
            RemoveSummaryOpportunityByKey(opportunity.opportunityKey);
        }

        private static void AdvanceSummaryStatusRevision(
            PawnKnowledgeState owner,
            SavedMemoryThreadRoot root)
        {
            bool changed = false;
            long next;
            if (owner != null && TryIncrement(owner.statusRevision, out next))
            {
                owner.statusRevision = next;
                changed = true;
            }
            if (root != null && TryIncrement(root.statusRevision, out next))
            {
                root.statusRevision = next;
                changed = true;
            }
            // The Library's detached owner cache fingerprints include these revisions. Advance the
            // common loaded-state fence so its next ordinary update rebuilds the changed wording row.
            if (changed) DiaryStateVersion.Bump();
        }

        private static SummaryWordingOpportunitySnapshot NewSummaryOpportunity(
            PawnKnowledgeState owner,
            SummaryWordingCurrentSnapshot current,
            DiaryKnowledgeTuningDef tuning,
            long nowTick,
            MemoryPolicySnapshot policy,
            long globalCancellationGeneration)
        {
            if (globalCancellationGeneration <= 0
                || globalCancellationGeneration == long.MaxValue) return null;
            long expiry = SaturatingAdd(
                nowTick, Math.Max(1, tuning.optionalMemoryOpportunityExpiryTicks));
            if (expiry <= nowTick) return null;
            SummaryWordingOpportunitySnapshot row = new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                ownerCancellationGeneration = owner.requestCancellationGeneration,
                globalCancellationGeneration = globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    policy.optionalRequestInvalidationGeneration,
                rootId = current.rootId,
                summaryRecordId = current.summaryRecordId,
                expectedRootStructuralRevision = current.rootStructuralRevision,
                expectedSummaryFactsRevision = current.summaryFactsRevision,
                expectedReducerRevision = current.reducerRevision,
                expectedFormatRevision = current.formatRevision,
                expectedCategoryMask = current.categoryMask,
                projectionFingerprint = current.projectionFingerprint,
                requestedTick = nowTick,
                dueTick = nowTick,
                expiryTick = expiry,
                configuredPriority = tuning.summaryWordingPriority,
                salience = SummarySalience(current)
            };
            string key;
            return MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(row, out key)
                ? SetOpportunityKey(row, key) : null;
        }

        private static SummaryWordingOpportunitySnapshot SetOpportunityKey(
            SummaryWordingOpportunitySnapshot row,
            string key)
        {
            row.opportunityKey = key;
            return row;
        }

        private IEnumerable<SummaryWordingCurrentSnapshot> CurrentOwnerSummaries(
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy)
        {
            List<SummaryWordingCurrentSnapshot> result =
                new List<SummaryWordingCurrentSnapshot>();
            for (int rootIndex = 0; owner?.threadRoots != null
                && rootIndex < owner.threadRoots.Count; rootIndex++)
            {
                SavedMemoryThreadRoot root = owner.threadRoots[rootIndex];
                AddCurrentSummary(result, root, root?.rollingSummaryBlock, policy);
                for (int blockIndex = 0; root?.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                    AddCurrentSummary(result, root, root.visibleBlocks[blockIndex], policy);
            }
            result.Sort((left, right) =>
            {
                int compared = string.CompareOrdinal(left.rootId, right.rootId);
                return compared != 0
                    ? compared : string.CompareOrdinal(left.summaryRecordId, right.summaryRecordId);
            });
            return result;
        }

        private static void AddCurrentSummary(
            List<SummaryWordingCurrentSnapshot> result,
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy)
        {
            SummaryWordingCurrentSnapshot current = CurrentSummarySnapshot(root, block, policy);
            if (current != null) result.Add(current);
        }

        private static SummaryWordingCurrentSnapshot CurrentSummarySnapshot(
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy)
        {
            if (root == null || block?.summaryPayload == null
                || block.kind != MemoryContractTokens.KindSummary
                || block.playerEdited
                || policy == null) return null;
            int mask = block.summaryPayload.derivedCategoryMask & policy.memoryCategoryMask;
            if (mask <= 0) return null;
            List<MemorySummaryFingerprintContribution> contributions =
                SummaryFingerprintContributions(block.summaryPayload, mask);
            string fingerprint;
            long formatRevision = Math.Max(1, block.formatRevision);
            if (!MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                    block.summaryPayload.reducerRevision,
                    formatRevision,
                    mask,
                    contributions,
                    out fingerprint)) return null;
            return new SummaryWordingCurrentSnapshot
            {
                ownerPawnId = block.ownerPawnId,
                ownerEpochToken = block.ownerEpochToken,
                rootId = root.rootId,
                summaryRecordId = block.recordId,
                rootStructuralRevision = root.structuralRevision,
                summaryFactsRevision = block.summaryPayload.factsRevision,
                reducerRevision = block.summaryPayload.reducerRevision,
                formatRevision = formatRevision,
                categoryMask = mask,
                projectionFingerprint = fingerprint,
                suppressed = block.suppressed,
                deterministicWording = block.summaryPayload.deterministicWording ?? string.Empty
            };
        }

        private static List<MemorySummaryFingerprintContribution> SummaryFingerprintContributions(
            SavedMemorySummaryPayload payload,
            int categoryMask)
        {
            List<MemorySummaryFingerprintContribution> result =
                new List<MemorySummaryFingerprintContribution>();
            for (int bucketIndex = 0; payload?.factBuckets != null
                && bucketIndex < payload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = payload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution row = bucket.contributions[index];
                    int bit = MemoryCategoryBits.ForToken(row?.category);
                    if (row == null || bit == 0 || (categoryMask & bit) == 0) continue;
                    result.Add(new MemorySummaryFingerprintContribution
                    {
                        contributionId = row.contributionId,
                        originRecordId = row.originRecordId,
                        originFactOrdinal = row.originFactOrdinal,
                        originFactId = row.originFactId,
                        originalEventTick = row.originalEventTick,
                        ageUnknown = row.ageUnknown,
                        category = row.category,
                        importance = row.importance,
                        canonicalValue = row.canonicalValue,
                        majorTurningPoint = row.majorTurningPoint,
                        reversal = row.reversal,
                        subjectRefIds = new List<string>(row.subjectRefIds
                            ?? new List<string>()),
                        provenanceRefIds = new List<string>(row.provenanceRefIds
                            ?? new List<string>())
                    });
                }
            }
            return result;
        }

        private static bool TryBuildSummaryWordingPrompt(
            SavedMemoryBlock block,
            SummaryWordingOpportunitySnapshot opportunity,
            DiaryKnowledgeTuningDef tuning,
            out string prompt)
        {
            prompt = string.Empty;
            string deterministicProjection;
            if (block?.summaryPayload == null || opportunity == null
                || !MemoryThreadReducer.TryBuildDeterministicCategoryProjection(
                    ToReducerSummary(block.summaryPayload),
                    opportunity.expectedCategoryMask,
                    Math.Max(1, tuning?.fallbackSummaryMaxChars ?? 240),
                    out deterministicProjection)) return false;
            StringBuilder text = new StringBuilder();
            text.AppendLine(tuning?.summaryWordingInstruction ?? string.Empty);
            text.Append("deterministic_summary=").AppendLine(deterministicProjection);
            text.Append("projection_fingerprint=").AppendLine(
                opportunity.projectionFingerprint ?? string.Empty);
            text.Append("category_mask=").Append(
                opportunity.expectedCategoryMask.ToString(CultureInfo.InvariantCulture));
            prompt = text.ToString();
            return true;
        }

        private static string BuildMemoryReflectionPrompt(
            SavedMemoryBlock block,
            DiaryKnowledgeTuningDef tuning)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(tuning?.memoryReflectionInstruction ?? string.Empty);
            text.Append("memory_record=").AppendLine(block?.recordId ?? string.Empty);
            text.Append("event_tick=").AppendLine(
                (block?.originalEventTick ?? 0).ToString(CultureInfo.InvariantCulture));
            text.Append("importance=").AppendLine(block?.importance ?? string.Empty);
            text.Append("memory=").Append(
                !string.IsNullOrWhiteSpace(block?.playerWording)
                    ? block.playerWording
                    : block?.automaticWording ?? string.Empty);
            return text.ToString();
        }

        private SavedMemoryBlock SelectOptionalReflectionBlock(
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            bool meaningful,
            long nowTick,
            DiaryKnowledgeTuningDef tuning,
            long meaningfulEligibilityBaselineTick)
        {
            List<SavedMemoryBlock> candidates = new List<SavedMemoryBlock>();
            AddReflectionBlocks(candidates, owner?.standaloneBlocks);
            for (int rootIndex = 0; owner?.threadRoots != null
                && rootIndex < owner.threadRoots.Count; rootIndex++)
                AddReflectionBlocks(candidates, owner.threadRoots[rootIndex]?.visibleBlocks);
            candidates.RemoveAll(block => !EligibleReflectionBlock(
                block, owner, policy, meaningful, nowTick, tuning,
                meaningfulEligibilityBaselineTick)
                || IsCoordinatorEvidenceReserved(block));
            candidates.Sort((left, right) =>
            {
                int compared = ReflectionBlockSalience(right)
                    .CompareTo(ReflectionBlockSalience(left));
                if (compared != 0) return compared;
                compared = right.originalEventTick.CompareTo(left.originalEventTick);
                return compared != 0
                    ? compared : string.CompareOrdinal(left.recordId, right.recordId);
            });
            return candidates.Count == 0 ? null : candidates[0];
        }

        private bool IsCoordinatorEvidenceReserved(SavedMemoryBlock block)
        {
            for (int requestIndex = 0; activeMemoryCoordinatorRequests != null
                && requestIndex < activeMemoryCoordinatorRequests.Count; requestIndex++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[requestIndex];
                for (int evidenceIndex = 0; request?.reservedEvidenceEntries != null
                    && evidenceIndex < request.reservedEvidenceEntries.Count; evidenceIndex++)
                    if (string.Equals(
                        request.reservedEvidenceEntries[evidenceIndex]?.recordId,
                        block?.recordId,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void AddReflectionBlocks(
            List<SavedMemoryBlock> result,
            List<SavedMemoryBlock> source)
        {
            for (int index = 0; source != null && index < source.Count; index++)
                if (source[index] != null) result.Add(source[index]);
        }

        /// <summary>
        /// Read-only wake hint for the common rest pass. It does not evaluate quiet RNG, repair rows,
        /// expire work, or suppress another class; the coordinator remains the sole priority decision.
        /// </summary>
        private bool HasOptionalMemoryCandidateSource(
            MemoryPolicySnapshot policy,
            long nowTick,
            DiaryKnowledgeTuningDef tuning)
        {
            if (!MemoryPolicyIsReconciled()
                || policy?.AllowsOptionalRequests != true
                || nowTick < 0) return false;
            for (int diaryIndex = 0; diaries != null && diaryIndex < diaries.Count; diaryIndex++)
            {
                PawnKnowledgeState owner = diaries[diaryIndex]?.KnowledgeStateOrNull();
                if (owner == null || !owner.IsCurrentSchema()) continue;
                for (int index = 0; owner.standaloneBlocks != null
                    && index < owner.standaloneBlocks.Count; index++)
                    if (CouldWakeOptionalMemory(
                            owner.standaloneBlocks[index], owner, policy,
                            optionalMeaningfulEligibilityBaselineTick,
                            nowTick,
                            tuning))
                        return true;
                for (int rootIndex = 0; owner.threadRoots != null
                    && rootIndex < owner.threadRoots.Count; rootIndex++)
                {
                    SavedMemoryThreadRoot root = owner.threadRoots[rootIndex];
                    if (ChangedSummaryProjection(root, root?.rollingSummaryBlock, policy))
                        return true;
                    for (int index = 0; root?.visibleBlocks != null
                        && index < root.visibleBlocks.Count; index++)
                    {
                        if (ChangedSummaryProjection(root, root.visibleBlocks[index], policy))
                            return true;
                        if (CouldWakeOptionalMemory(
                                root.visibleBlocks[index], owner, policy,
                                optionalMeaningfulEligibilityBaselineTick,
                                nowTick,
                                tuning))
                            return true;
                    }
                }
            }
            return false;
        }

        private bool ChangedSummaryProjection(
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            MemoryPolicySnapshot policy)
        {
            SavedMemorySummaryPayload payload = block?.summaryPayload;
            if (root == null || payload == null || policy == null) return false;
            SummaryWakeProjectionCache cached;
            if (summaryWakeProjectionCache.TryGetValue(block, out cached)
                && ReferenceEquals(cached.root, root)
                && cached.rootStructuralRevision == root.structuralRevision
                && cached.factsRevision == payload.factsRevision
                && cached.reducerRevision == payload.reducerRevision
                && cached.formatRevision == block.formatRevision
                && cached.derivedCategoryMask == payload.derivedCategoryMask
                && cached.policyCategoryMask == policy.memoryCategoryMask
                && cached.playerEdited == block.playerEdited
                && cached.lastSettledReducerRevision
                    == payload.lastSettledWordingReducerRevision
                && cached.lastSettledFormatRevision
                    == payload.lastSettledWordingFormatRevision
                && string.Equals(cached.lastSettledFingerprint,
                    payload.lastSettledWordingFingerprint, StringComparison.Ordinal))
                return cached.changed;

            SummaryWordingCurrentSnapshot current = CurrentSummarySnapshot(root, block, policy);
            bool changed = current != null
                && (!string.Equals(payload.lastSettledWordingFingerprint,
                        current.projectionFingerprint, StringComparison.Ordinal)
                    || payload.lastSettledWordingReducerRevision != current.reducerRevision
                    || payload.lastSettledWordingFormatRevision != current.formatRevision);
            if (cached == null)
            {
                cached = new SummaryWakeProjectionCache();
                summaryWakeProjectionCache.Add(block, cached);
            }
            cached.root = root;
            cached.rootStructuralRevision = root.structuralRevision;
            cached.factsRevision = payload.factsRevision;
            cached.reducerRevision = payload.reducerRevision;
            cached.formatRevision = block.formatRevision;
            cached.derivedCategoryMask = payload.derivedCategoryMask;
            cached.policyCategoryMask = policy.memoryCategoryMask;
            cached.playerEdited = block.playerEdited;
            cached.lastSettledFingerprint = payload.lastSettledWordingFingerprint ?? string.Empty;
            cached.lastSettledReducerRevision = payload.lastSettledWordingReducerRevision;
            cached.lastSettledFormatRevision = payload.lastSettledWordingFormatRevision;
            cached.changed = changed;
            return changed;
        }

        private static bool CouldWakeOptionalMemory(
            SavedMemoryBlock block,
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            long meaningfulEligibilityBaselineTick,
            long nowTick,
            DiaryKnowledgeTuningDef tuning)
        {
            if (block == null || owner == null || policy == null
                || block.kind == MemoryContractTokens.KindSummary
                || block.suppressed || block.playerEdited
                || !string.Equals(block.ownerPawnId, owner.pawnId, StringComparison.Ordinal)
                || !string.Equals(block.ownerEpochToken,
                    owner.autobiographicalEpochToken, StringComparison.Ordinal)
                || !string.Equals(block.providerExposureState, "not_sent",
                    StringComparison.Ordinal)
                || !policy.AllowsRecall(MemoryCategoryBits.ForToken(block.category))) return false;
            bool meaningful = block.importance == MemoryContractTokens.ImportanceImportant
                || HasTurningPoint(block) || HasReversal(block);
            if (!meaningful) return policy.AllowsOccasionalReflections;
            long delay = CaptureRuleOwnsAuthoritativePage(block.captureRuleId)
                ? Math.Max(1, tuning?.meaningfulMemoryDelayTicks ?? 60000)
                : 0;
            return MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(
                    block.originalEventTick, meaningfulEligibilityBaselineTick)
                && MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(
                    block.originalEventTick,
                    delay,
                    Math.Max(1, tuning?.optionalMemoryOpportunityExpiryTicks ?? 120000),
                    nowTick);
        }

        private static bool EligibleReflectionBlock(
            SavedMemoryBlock block,
            PawnKnowledgeState owner,
            MemoryPolicySnapshot policy,
            bool meaningful,
            long nowTick,
            DiaryKnowledgeTuningDef tuning,
            long meaningfulEligibilityBaselineTick)
        {
            if (block == null || owner == null || policy == null
                || block.kind == MemoryContractTokens.KindSummary
                || block.suppressed || block.playerEdited
                || !string.Equals(block.ownerPawnId, owner.pawnId, StringComparison.Ordinal)
                || !string.Equals(block.ownerEpochToken,
                    owner.autobiographicalEpochToken, StringComparison.Ordinal)
                || !string.Equals(block.providerExposureState, "not_sent",
                    StringComparison.Ordinal)
                || !policy.AllowsRecall(MemoryCategoryBits.ForToken(block.category))) return false;
            bool salient = block.importance == MemoryContractTokens.ImportanceImportant
                || HasTurningPoint(block) || HasReversal(block);
            if (meaningful != salient) return false;
            if (meaningful
                && !MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(
                    block.originalEventTick, meaningfulEligibilityBaselineTick)) return false;
            if (!meaningful)
            {
                long dormantAfter = SaturatingAdd(
                    Math.Max(0, block.originalEventTick),
                    Math.Max(1, tuning?.meaningfulMemoryDelayTicks ?? 60000));
                return nowTick >= dormantAfter;
            }
            long requested = Math.Max(0, block.originalEventTick);
            long due = CaptureRuleOwnsAuthoritativePage(block.captureRuleId)
                ? SaturatingAdd(requested,
                    Math.Max(1, tuning?.meaningfulMemoryDelayTicks ?? 60000))
                : requested;
            long expiry = SaturatingAdd(
                due, Math.Max(1, tuning?.optionalMemoryOpportunityExpiryTicks ?? 120000));
            return nowTick >= due && nowTick < expiry;
        }

        private static bool HasTurningPoint(SavedMemoryBlock block)
        {
            for (int index = 0; block?.facts != null && index < block.facts.Count; index++)
                if (block.facts[index]?.majorTurningPoint == true) return true;
            return false;
        }

        private static bool HasReversal(SavedMemoryBlock block)
        {
            for (int index = 0; block?.facts != null && index < block.facts.Count; index++)
                if (block.facts[index]?.reversal == true) return true;
            return false;
        }

        private static int ReflectionBlockSalience(SavedMemoryBlock block)
        {
            if (HasTurningPoint(block)) return 4;
            if (HasReversal(block)) return 3;
            return block?.importance == MemoryContractTokens.ImportanceImportant ? 2 : 1;
        }

        private static int SummarySalience(SummaryWordingCurrentSnapshot current)
        {
            return current?.categoryMask ?? 0;
        }

        private static bool CaptureRuleOwnsAuthoritativePage(string captureRuleId)
        {
            DiaryImportantEventDef rule = DefDatabase<DiaryImportantEventDef>
                .GetNamedSilentFail(captureRuleId);
            return rule?.authoritativePageOwned == true;
        }

        private static string MemoryReflectionOpportunityKey(
            PawnKnowledgeState owner,
            SavedMemoryBlock block,
            string timing,
            long requested,
            long due,
            long expiry,
            long optionalGeneration)
        {
            return OrdinalSegmentCodec.Segment("memory-reflection-opportunity-v1")
                + OrdinalSegmentCodec.Segment(owner?.pawnId ?? string.Empty)
                + OrdinalSegmentCodec.Segment(owner?.autobiographicalEpochToken ?? string.Empty)
                + OrdinalSegmentCodec.Segment(block?.recordId ?? string.Empty)
                + OrdinalSegmentCodec.Segment(timing ?? string.Empty)
                + OrdinalSegmentCodec.Segment(requested.ToString(CultureInfo.InvariantCulture))
                + OrdinalSegmentCodec.Segment(due.ToString(CultureInfo.InvariantCulture))
                + OrdinalSegmentCodec.Segment(expiry.ToString(CultureInfo.InvariantCulture))
                + OrdinalSegmentCodec.Segment(optionalGeneration.ToString(
                    CultureInfo.InvariantCulture));
        }

        private bool TryPlanNextMemoryLogicalRequestSequence(out long next)
        {
            next = 0;
            long highWater = Math.Max(0, lastIssuedMemoryLogicalRequestSequence);
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
                highWater = Math.Max(
                    highWater, activeMemoryCoordinatorRequests[index]?.logicalRequestSequence ?? 0);
            for (int index = 0; memoryAttemptAuditRows != null
                && index < memoryAttemptAuditRows.Count; index++)
            {
                long parsed;
                if (MemoryIdentityCodec.TryParseLogicalRequestId(
                    memoryAttemptAuditRows[index]?.logicalRequestId, out parsed))
                    highWater = Math.Max(highWater, parsed);
            }
            IReadOnlyList<DiaryEvent> hot = events?.AllEvents;
            for (int index = 0; hot != null && index < hot.Count; index++)
            {
                highWater = Math.Max(highWater, RequestSequence(
                    hot[index]?.ActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole)));
                highWater = Math.Max(highWater, RequestSequence(
                    hot[index]?.ActiveMemoryLogicalRequestForRole(DiaryEvent.RecipientRole)));
                highWater = Math.Max(highWater, RequestSequence(
                    hot[index]?.ActiveMemoryLogicalRequestForRole(DiaryEvent.NeutralRole)));
            }
            if (highWater == long.MaxValue) return false;
            next = highWater + 1;
            return next > 0;
        }

        private static long RequestSequence(SavedActiveLogicalRequestV1 request)
        {
            return request?.logicalRequestSequence ?? 0;
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (left < 0 || right < 0 || left > long.MaxValue - right) return long.MaxValue;
            return left + right;
        }

        private static DiaryKnowledgeTuningDef MemoryOptionalTuning()
        {
            return DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail("Diary_Knowledge");
        }

        private SavedSummaryWordingOpportunityV1 FindSummaryOpportunity(
            string ownerPawnId,
            string ownerEpochToken)
        {
            for (int index = 0; summaryWordingOpportunities != null
                && index < summaryWordingOpportunities.Count; index++)
            {
                SavedSummaryWordingOpportunityV1 row = summaryWordingOpportunities[index];
                if (row != null
                    && string.Equals(row.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    && string.Equals(row.ownerEpochToken, ownerEpochToken,
                        StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private void ReplaceSummaryOpportunity(
            string ownerPawnId,
            string ownerEpochToken,
            SummaryWordingOpportunitySnapshot winner)
        {
            SavedSummaryWordingOpportunityV1 existing = FindSummaryOpportunity(
                ownerPawnId, ownerEpochToken);
            bool pendingChanged = false;
            if (winner != null)
            {
                if (!string.Equals(winner.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    || !string.Equals(winner.ownerEpochToken, ownerEpochToken,
                        StringComparison.Ordinal)
                    || !MemoryOptionalAiPolicy.IsValidSummaryOpportunity(winner)
                    || !ApplySummaryPending(winner, out pendingChanged))
                {
                    winner = null;
                }
            }
            if (existing == null && winner == null)
            {
                if (pendingChanged) RebuildMemorySizeIndexes();
                return;
            }
            if (existing != null && winner != null
                && string.Equals(existing.opportunityKey, winner.opportunityKey,
                    StringComparison.Ordinal))
            {
                if (pendingChanged) RebuildMemorySizeIndexes();
                return;
            }

            List<SavedSummaryWordingOpportunityV1> replacement =
                new List<SavedSummaryWordingOpportunityV1>();
            for (int index = 0; summaryWordingOpportunities != null
                && index < summaryWordingOpportunities.Count; index++)
            {
                SavedSummaryWordingOpportunityV1 row = summaryWordingOpportunities[index];
                if (row != null
                    && string.Equals(row.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    && string.Equals(row.ownerEpochToken, ownerEpochToken,
                        StringComparison.Ordinal)) continue;
                replacement.Add(row);
            }
            if (winner != null) replacement.Add(FromSummaryOpportunity(winner));
            replacement.Sort(CompareSavedSummaryOpportunities);
            summaryWordingOpportunities = replacement;
            RebuildMemorySizeIndexes();
        }

        private void RemoveSummaryOpportunity(SavedSummaryWordingOpportunityV1 row)
        {
            if (row != null) summaryWordingOpportunities.Remove(row);
            RebuildMemorySizeIndexes();
        }

        private void RemoveSummaryOpportunityByKey(string key)
        {
            summaryWordingOpportunities.RemoveAll(row => row != null
                && string.Equals(row.opportunityKey, key, StringComparison.Ordinal));
            RebuildMemorySizeIndexes();
        }

        private static bool TryFindSummary(
            PawnKnowledgeState owner,
            string rootId,
            string summaryRecordId,
            out SavedMemoryThreadRoot root,
            out SavedMemoryBlock block)
        {
            root = null;
            block = null;
            for (int index = 0; owner?.threadRoots != null
                && index < owner.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot candidate = owner.threadRoots[index];
                if (candidate == null || !string.Equals(
                    candidate.rootId, rootId, StringComparison.Ordinal)) continue;
                if (candidate.rollingSummaryBlock != null
                    && string.Equals(candidate.rollingSummaryBlock.recordId,
                        summaryRecordId, StringComparison.Ordinal))
                {
                    root = candidate;
                    block = candidate.rollingSummaryBlock;
                    return true;
                }
                for (int blockIndex = 0; candidate.visibleBlocks != null
                    && blockIndex < candidate.visibleBlocks.Count; blockIndex++)
                {
                    SavedMemoryBlock visible = candidate.visibleBlocks[blockIndex];
                    if (visible != null && string.Equals(
                        visible.recordId, summaryRecordId, StringComparison.Ordinal))
                    {
                        root = candidate;
                        block = visible;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryFindCurrentSummary(
            SummaryWordingOpportunitySnapshot opportunity,
            out SavedMemoryThreadRoot root,
            out SavedMemoryBlock block)
        {
            root = null;
            block = null;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(opportunity?.ownerPawnId);
            if (owner == null || opportunity == null
                || !string.Equals(owner.autobiographicalEpochToken,
                    opportunity.ownerEpochToken, StringComparison.Ordinal)
                || !TryFindSummary(owner, opportunity.rootId,
                    opportunity.summaryRecordId, out root, out block)) return false;
            SummaryWordingCurrentSnapshot current = CurrentSummarySnapshot(
                root, block, MemoryEffectivePolicyProvider.Current);
            return MemoryOptionalAiPolicy.MatchesCurrentSummary(opportunity, current);
        }

        private static SummaryWordingOpportunitySnapshot ToSummaryOpportunity(
            SavedSummaryWordingOpportunityV1 row)
        {
            return row == null ? null : new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = row.ownerPawnId,
                ownerEpochToken = row.ownerEpochToken,
                ownerCancellationGeneration = row.ownerCancellationGeneration,
                globalCancellationGeneration = row.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    row.optionalRequestInvalidationGeneration,
                rootId = row.rootId,
                summaryRecordId = row.summaryRecordId,
                expectedRootStructuralRevision = row.expectedRootStructuralRevision,
                expectedSummaryFactsRevision = row.expectedSummaryFactsRevision,
                expectedReducerRevision = row.expectedReducerRevision,
                expectedFormatRevision = row.expectedFormatRevision,
                expectedCategoryMask = row.expectedCategoryMask,
                projectionFingerprint = row.projectionFingerprint,
                requestedTick = row.requestedTick,
                dueTick = row.dueTick,
                expiryTick = row.expiryTick,
                configuredPriority = row.configuredPriority,
                salience = row.salience,
                opportunityKey = row.opportunityKey
            };
        }

        private static SavedSummaryWordingOpportunityV1 FromSummaryOpportunity(
            SummaryWordingOpportunitySnapshot row)
        {
            return new SavedSummaryWordingOpportunityV1
            {
                schemaVersion = 1,
                ownerPawnId = row.ownerPawnId,
                ownerEpochToken = row.ownerEpochToken,
                ownerCancellationGeneration = row.ownerCancellationGeneration,
                globalCancellationGeneration = row.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    row.optionalRequestInvalidationGeneration,
                rootId = row.rootId,
                summaryRecordId = row.summaryRecordId,
                expectedRootStructuralRevision = row.expectedRootStructuralRevision,
                expectedSummaryFactsRevision = row.expectedSummaryFactsRevision,
                expectedReducerRevision = row.expectedReducerRevision,
                expectedFormatRevision = row.expectedFormatRevision,
                expectedCategoryMask = row.expectedCategoryMask,
                projectionFingerprint = row.projectionFingerprint,
                requestedTick = row.requestedTick,
                dueTick = row.dueTick,
                expiryTick = row.expiryTick,
                configuredPriority = row.configuredPriority,
                salience = row.salience,
                opportunityKey = row.opportunityKey
            };
        }
    }
}
