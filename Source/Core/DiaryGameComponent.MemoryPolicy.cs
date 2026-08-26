// DiaryGameComponent.MemoryPolicy.cs — applies one published M5 policy tuple to saved game state.
//
// Settings are process-global, while cancellation generations, capture episodes, and maintenance
// cursors belong to one save. This adapter asks the pure reconciler for the exact delta, prepares
// replacement collections first, then publishes the saved applied-policy marker last. Repeating the
// same fingerprint is therefore a no-op across settings writes and load callbacks.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Applies one immutable effective policy to this save. Returns false only when a compatibility
        /// or saturated-counter boundary requires memory consumers to remain fail-closed.
        /// </summary>
        internal bool ReconcilePublishedMemoryPolicy(MemoryPolicySnapshot published = null)
        {
            if (published == null && MemoryPolicyIsReconciled())
            {
                EnsureOptionalMeaningfulEligibilityBaseline(
                    MemoryEffectivePolicyProvider.Current,
                    false);
                return true;
            }
            MemoryPolicySnapshot policy = published ?? MemoryEffectivePolicyProvider.Current;
            MemorySettingsPolicyFieldsV1 applied = AppliedMemoryPolicyFields();
            MemoryPolicyReconciliationPlan plan = MemoryPolicyNormalizer.PlanReconciliation(
                applied,
                lastAppliedMemoryPolicyFingerprint,
                lastAppliedMemoryPolicyRevision,
                policy);
            if (!plan.valid || plan.revisionSaturated)
            {
                RecordMemoryDiagnosticOnce("other", "policy_reconciling");
                return false;
            }
            if (plan.alreadyApplied)
            {
                EnsureOptionalMeaningfulEligibilityBaseline(policy, false);
                return true;
            }
            bool priorOptional = applied != null
                && applied.useMemoriesInWriting
                && applied.allowExtraMemoryAiRequests
                && applied.optionalRequestInvalidationGeneration > 0
                && applied.optionalRequestInvalidationGeneration < long.MaxValue;
            bool nextOptional = policy.AllowsOptionalRequests;
            bool priorQuiet = priorOptional && applied.occasionalMemoryReflections;
            bool nextQuiet = policy.AllowsOccasionalReflections;
            bool baselineOptionalSummaries = applied == null
                || priorOptional != nextOptional
                || applied.memoryCategoryMask != policy.memoryCategoryMask;
            List<SavedActiveLogicalRequestV1> retainedRequests =
                PrepareRetainedOptionalRequests(plan.purgeUnsentOptionalWork);
            List<SavedSummaryWordingOpportunityV1> retainedOpportunities =
                plan.purgeUnsentOptionalWork
                    ? new List<SavedSummaryWordingOpportunityV1>()
                    : new List<SavedSummaryWordingOpportunityV1>(
                        summaryWordingOpportunities
                            ?? new List<SavedSummaryWordingOpportunityV1>());
            List<PendingEpisodeReplacement> episodeReplacements =
                PrepareEpisodeReplacements(plan.captureGenerationMismatchMask);

            if (plan.advanceGlobalOptionalCancellation)
            {
                // Seal the exact old generation before publishing its successor. Only requests whose
                // invocation permit already committed may use this bounded invocation-wins exception.
                invokedGenerationCutoffs.SealGeneration(
                    LlmClient.CurrentSessionId,
                    globalOptionalRequestCancellationGeneration,
                    memoryInvocationSequenceForSession);
                // Max is the permanent nonallocating sentinel. Reconciliation still settles every
                // unsent row and publishes the applied marker so normal capture/recall remain usable.
                globalOptionalRequestCancellationGeneration =
                    MemoryPolicyNormalizer.AdvanceSaturatingGeneration(
                        globalOptionalRequestCancellationGeneration);
            }
            if (plan.purgeUnsentOptionalWork)
            {
                ReleasePurgedOptionalCoordinatorRequests(retainedRequests);
                activeMemoryCoordinatorRequests = retainedRequests;
                summaryWordingOpportunities = retainedOpportunities;
                PurgeUnsentOptionalEventRequests();
            }
            for (int index = 0; index < episodeReplacements.Count; index++)
            {
                PendingEpisodeReplacement replacement = episodeReplacements[index];
                replacement.owner.openCaptureEpisodes = replacement.episodes;
            }

            if (baselineOptionalSummaries)
            {
                // Turning work on or changing its category projection observes current deterministic
                // summaries as already settled. A later natural fact change is the only creator.
                BaselineOptionalSummariesWithoutCatchUp(policy);
                EnsureOptionalMeaningfulEligibilityBaseline(policy, true);
            }
            else EnsureOptionalMeaningfulEligibilityBaseline(policy, false);
            if (!priorQuiet && nextQuiet)
            {
                BaselineQuietCadenceWithoutCatchUp();
            }

            MemorySettingsPolicyFieldsV1 current = policy.ToFields();
            if (plan.markLifetimeMaintenanceDirty || plan.markThreadTargetMaintenanceDirty)
            {
                int oldMinor = applied?.minorMemoryLifetimeDays ?? int.MaxValue;
                int oldRegular = applied?.regularMemoryLifetimeDays ?? int.MaxValue;
                int oldTarget = applied?.memoryThreadTarget ?? int.MaxValue;
                MarkMemoryMaintenanceDirtyForSettingsChange(
                    MemoryPolicyNormalizer.DaysToTicks(oldMinor),
                    MemoryPolicyNormalizer.DaysToTicks(oldRegular),
                    oldTarget,
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks,
                    policy.memoryThreadTarget);
            }

            lastAppliedMemoryPolicyState = ToSavedAppliedPolicy(current);
            lastAppliedMemoryPolicyFingerprint = policy.fingerprint;
            lastAppliedMemoryPolicyRevision = plan.nextAppliedRevision;
            memoryM4IndexesDirty = true;
            RebuildMemorySizeIndexes();
            return true;
        }

        /// <summary>
        /// Establishes the unsaved meaningful-work boundary. A game/load transition resets the
        /// derivative to -1, so the first reconciled pass baselines current truth and cannot rederive a
        /// provider backlog. Enabling optional work or changing its category projection forces the same
        /// baseline; later policy passes leave it untouched.
        /// </summary>
        private void EnsureOptionalMeaningfulEligibilityBaseline(
            MemoryPolicySnapshot policy,
            bool force)
        {
            if (policy?.AllowsOptionalRequests != true)
            {
                optionalMeaningfulEligibilityBaselineTick = -1;
                return;
            }
            if (force || optionalMeaningfulEligibilityBaselineTick < 0)
            {
                optionalMeaningfulEligibilityBaselineTick =
                    Math.Max(0L, Find.TickManager?.TicksGame ?? 0);
            }
        }

        /// <summary>True only after this save has applied the currently published fingerprint.</summary>
        internal bool MemoryPolicyIsReconciled()
        {
            MemoryPolicySnapshot current = MemoryEffectivePolicyProvider.Current;
            return current != null
                && !current.compatibilityFailClosed
                && string.Equals(
                    lastAppliedMemoryPolicyFingerprint,
                    current.fingerprint,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Future capture seam: category switches gate episodic admission only after reconciliation.
        /// Current-state baseline observers deliberately do not call this gate and remain active.
        /// </summary>
        internal bool MemoryCategoryAllowsCapture(string categoryToken, out string statusToken)
        {
            if (!MemoryPolicyIsReconciled())
            {
                statusToken = "PolicyReconciling";
                return false;
            }
            int bit = MemoryCategoryBits.ForToken(categoryToken);
            bool allowed = MemoryEffectivePolicyProvider.Current.AllowsCapture(bit);
            statusToken = allowed ? "Allowed" : bit == 0 ? "InvalidCategory" : "Disabled";
            return allowed;
        }

        /// <summary>Future natural-recall seam; Library browsing intentionally ignores this gate.</summary>
        internal bool MemoryCategoryAllowsRecall(string categoryToken, out string statusToken)
        {
            if (!MemoryPolicyIsReconciled())
            {
                statusToken = "PolicyReconciling";
                return false;
            }
            int bit = MemoryCategoryBits.ForToken(categoryToken);
            bool allowed = MemoryEffectivePolicyProvider.Current.AllowsRecall(bit);
            statusToken = allowed ? "Allowed" : bit == 0 ? "InvalidCategory" : "Disabled";
            return allowed;
        }

        private MemorySettingsPolicyFieldsV1 AppliedMemoryPolicyFields()
        {
            SavedMemoryAppliedPolicyStateV1 value = lastAppliedMemoryPolicyState;
            if (value == null || value.schemaVersion != 1) return null;
            return new MemorySettingsPolicyFieldsV1
            {
                saveNewMemories = value.saveNewMemories,
                useMemoriesInWriting = value.useMemoriesInWriting,
                usePawnBackground = value.usePawnBackground,
                allowExtraMemoryAiRequests = value.allowExtraMemoryAiRequests,
                occasionalMemoryReflections = value.occasionalMemoryReflections,
                memoryCategoryMask = value.memoryCategoryMask,
                captureInvalidationGenerationPersonal =
                    value.captureInvalidationGenerationPersonal,
                captureInvalidationGenerationRelationships =
                    value.captureInvalidationGenerationRelationships,
                captureInvalidationGenerationFamily =
                    value.captureInvalidationGenerationFamily,
                captureInvalidationGenerationFactions =
                    value.captureInvalidationGenerationFactions,
                optionalRequestInvalidationGeneration =
                    value.optionalRequestInvalidationGeneration,
                minorMemoryLifetimeDays = value.minorMemoryLifetimeDays,
                regularMemoryLifetimeDays = value.regularMemoryLifetimeDays,
                memoryThreadTarget = value.memoryThreadTarget,
                memoryReuseDays = value.memoryReuseDays,
                memoryRevisitEntryCount = value.memoryRevisitEntryCount
            };
        }

        private static SavedMemoryAppliedPolicyStateV1 ToSavedAppliedPolicy(
            MemorySettingsPolicyFieldsV1 value)
        {
            return new SavedMemoryAppliedPolicyStateV1
            {
                schemaVersion = 1,
                saveNewMemories = value.saveNewMemories,
                useMemoriesInWriting = value.useMemoriesInWriting,
                usePawnBackground = value.usePawnBackground,
                allowExtraMemoryAiRequests = value.allowExtraMemoryAiRequests,
                occasionalMemoryReflections = value.occasionalMemoryReflections,
                memoryCategoryMask = value.memoryCategoryMask,
                captureInvalidationGenerationPersonal =
                    value.captureInvalidationGenerationPersonal,
                captureInvalidationGenerationRelationships =
                    value.captureInvalidationGenerationRelationships,
                captureInvalidationGenerationFamily =
                    value.captureInvalidationGenerationFamily,
                captureInvalidationGenerationFactions =
                    value.captureInvalidationGenerationFactions,
                optionalRequestInvalidationGeneration =
                    value.optionalRequestInvalidationGeneration,
                minorMemoryLifetimeDays = value.minorMemoryLifetimeDays,
                regularMemoryLifetimeDays = value.regularMemoryLifetimeDays,
                memoryThreadTarget = value.memoryThreadTarget,
                memoryReuseDays = value.memoryReuseDays,
                memoryRevisitEntryCount = value.memoryRevisitEntryCount
            };
        }

        private List<SavedActiveLogicalRequestV1> PrepareRetainedOptionalRequests(bool purge)
        {
            List<SavedActiveLogicalRequestV1> retained =
                new List<SavedActiveLogicalRequestV1>();
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[index];
                if (request == null) continue;
                bool remove = purge
                    && MemoryDispatchTokens.IsOptionalPurpose(request.requestPurposeToken)
                    && !MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(request);
                if (!remove) retained.Add(request);
            }
            return retained;
        }

        /// <summary>
        /// Releases runtime-only callback/send claims for component rows removed by a settings fence.
        /// Invoked rows are present in <paramref name="retained"/> and deliberately survive here so
        /// their sealed generation can finish under invocation-wins.
        /// </summary>
        private void ReleasePurgedOptionalCoordinatorRequests(
            List<SavedActiveLogicalRequestV1> retained)
        {
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[index];
                if (request == null || retained.Contains(request)) continue;
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    request.logicalRequestId);
                invokedGenerationCutoffs.Settle(request.logicalRequestId);
            }
        }

        private void PurgeUnsentOptionalEventRequests()
        {
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                if (diaryEvent == null) continue;
                PurgeUnsentOptionalEventRole(diaryEvent, DiaryEvent.InitiatorRole);
                PurgeUnsentOptionalEventRole(diaryEvent, DiaryEvent.RecipientRole);
                PurgeUnsentOptionalEventRole(diaryEvent, DiaryEvent.NeutralRole);
            }
        }

        private static void PurgeUnsentOptionalEventRole(DiaryEvent diaryEvent, string role)
        {
            SavedActiveLogicalRequestV1 request =
                diaryEvent.ActiveMemoryLogicalRequestForRole(role);
            if (request == null
                || !MemoryDispatchTokens.IsOptionalPurpose(request.requestPurposeToken)
                || MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(request)) return;
            MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(request.logicalRequestId);
            diaryEvent.SetActiveMemoryLogicalRequestForRole(role, null);
            diaryEvent.SetAcceptedPromptPair(role, string.Empty, string.Empty);
        }

        private List<PendingEpisodeReplacement> PrepareEpisodeReplacements(int categoryMask)
        {
            List<PendingEpisodeReplacement> result = new List<PendingEpisodeReplacement>();
            if ((categoryMask & MemoryCategoryBits.KnownMask) == 0) return result;
            HashSet<PawnKnowledgeState> seen = new HashSet<PawnKnowledgeState>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnKnowledgeState owner = diaries[index]?.knowledgeState;
                if (owner == null || !owner.IsCurrentSchema() || !seen.Add(owner)) continue;
                List<SavedMemoryCaptureEpisode> retained =
                    new List<SavedMemoryCaptureEpisode>();
                for (int episodeIndex = 0; owner.openCaptureEpisodes != null
                    && episodeIndex < owner.openCaptureEpisodes.Count; episodeIndex++)
                {
                    SavedMemoryCaptureEpisode episode = owner.openCaptureEpisodes[episodeIndex];
                    if (episode == null) continue;
                    int bit = MemoryCategoryBits.ForToken(episode.category);
                    if (bit == 0 || (categoryMask & bit) == 0) retained.Add(episode);
                }
                result.Add(new PendingEpisodeReplacement
                {
                    owner = owner,
                    episodes = retained
                });
            }
            return result;
        }

        private sealed class PendingEpisodeReplacement
        {
            internal PawnKnowledgeState owner;
            internal List<SavedMemoryCaptureEpisode> episodes;
        }
    }
}
