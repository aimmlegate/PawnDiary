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
        // Loaded tests inject only at named main-thread transaction boundaries. Production never sets
        // this delegate; keeping the seam instance-local prevents one test Game leaking into another.
        private Action<string> memoryPolicyReconciliationFaultForTests;

        /// <summary>Installs or clears the loaded-test reconciliation fault seam.</summary>
        internal void SetMemoryPolicyReconciliationFaultForTests(Action<string> fault)
        {
            memoryPolicyReconciliationFaultForTests = fault;
        }

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
            List<SavedActiveLogicalRequestV1> purgedCoordinatorRequests =
                PreparePurgedOptionalCoordinatorRequests(retainedRequests);
            List<PendingEventRequestPurge> eventRequestPurges =
                plan.purgeUnsentOptionalWork
                    ? PrepareUnsentOptionalEventRequestPurges()
                    : new List<PendingEventRequestPurge>();
            List<PendingEpisodeReplacement> episodeReplacements =
                PrepareEpisodeReplacements(plan.captureGenerationMismatchMask);
            int baselinedSummaryCount = 0;
            List<PendingSummaryBaselineReplacement> summaryReplacements =
                new List<PendingSummaryBaselineReplacement>();
            if (baselineOptionalSummaries)
            {
                summaryReplacements = PrepareSummaryBaselineReplacements(
                    policy, out baselinedSummaryCount);
            }
            List<PendingQuietCadenceReplacement> quietReplacements =
                !priorQuiet && nextQuiet
                    ? PrepareQuietCadenceReplacements()
                    : new List<PendingQuietCadenceReplacement>();

            bool replaceSummaryOpportunities =
                plan.purgeUnsentOptionalWork || baselineOptionalSummaries;
            List<SavedSummaryWordingOpportunityV1> nextSummaryOpportunities =
                replaceSummaryOpportunities
                    ? new List<SavedSummaryWordingOpportunityV1>()
                    : summaryWordingOpportunities;
            bool replaceCutoffs = plan.advanceGlobalOptionalCancellation
                || plan.purgeUnsentOptionalWork;
            MemoryInvokedGenerationCutoffTable nextCutoffs = replaceCutoffs
                ? invokedGenerationCutoffs.Clone()
                : invokedGenerationCutoffs;
            long nextGlobalCancellationGeneration =
                globalOptionalRequestCancellationGeneration;
            if (plan.advanceGlobalOptionalCancellation)
            {
                // Seal the exact old generation before publishing its successor. Only requests whose
                // invocation permit already committed may use this bounded invocation-wins exception.
                nextCutoffs.SealGeneration(
                    LlmClient.CurrentSessionId,
                    globalOptionalRequestCancellationGeneration,
                    memoryInvocationSequenceForSession);
                // Max is the permanent nonallocating sentinel. Reconciliation still settles every
                // unsent row and publishes the applied marker so normal capture/recall remain usable.
                nextGlobalCancellationGeneration =
                    MemoryPolicyNormalizer.AdvanceSaturatingGeneration(
                        globalOptionalRequestCancellationGeneration);
            }
            if (plan.purgeUnsentOptionalWork)
            {
                for (int index = 0; index < purgedCoordinatorRequests.Count; index++)
                    nextCutoffs.Settle(purgedCoordinatorRequests[index].logicalRequestId);
                for (int index = 0; index < eventRequestPurges.Count; index++)
                    nextCutoffs.Settle(eventRequestPurges[index].request.logicalRequestId);
            }

            long nextEligibilityBaseline =
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    policy?.AllowsOptionalRequests == true,
                    baselineOptionalSummaries,
                    optionalMeaningfulEligibilityBaselineTick,
                    Find.TickManager?.TicksGame ?? 0);
            bool markMaintenanceDirty = false;
            if (plan.markLifetimeMaintenanceDirty || plan.markThreadTargetMaintenanceDirty)
            {
                int oldMinor = applied?.minorMemoryLifetimeDays ?? int.MaxValue;
                int oldRegular = applied?.regularMemoryLifetimeDays ?? int.MaxValue;
                int oldTarget = applied?.memoryThreadTarget ?? int.MaxValue;
                markMaintenanceDirty = MemoryMaintenancePolicy.SettingsChangeMakesDirty(
                    MemoryPolicyNormalizer.DaysToTicks(oldMinor),
                    MemoryPolicyNormalizer.DaysToTicks(oldRegular),
                    oldTarget,
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks,
                    policy.memoryThreadTarget);
            }

            MemorySettingsPolicyFieldsV1 current = policy.ToFields();
            SavedMemoryAppliedPolicyStateV1 nextAppliedState =
                ToSavedAppliedPolicy(current);
            MemoryPolicyReconciliationRollback rollback =
                new MemoryPolicyReconciliationRollback(
                    this,
                    episodeReplacements,
                    summaryReplacements,
                    quietReplacements,
                    eventRequestPurges);
            bool indexesTouched = false;
            try
            {
                InvokeMemoryPolicyReconciliationFaultForTests("after_prepare");

                if (replaceCutoffs) invokedGenerationCutoffs = nextCutoffs;
                globalOptionalRequestCancellationGeneration =
                    nextGlobalCancellationGeneration;
                if (plan.purgeUnsentOptionalWork)
                    activeMemoryCoordinatorRequests = retainedRequests;
                if (replaceSummaryOpportunities)
                    summaryWordingOpportunities = nextSummaryOpportunities;
                for (int index = 0; index < episodeReplacements.Count; index++)
                {
                    PendingEpisodeReplacement replacement = episodeReplacements[index];
                    replacement.owner.openCaptureEpisodes = replacement.episodes;
                }
                for (int index = 0; index < summaryReplacements.Count; index++)
                {
                    PendingSummaryBaselineReplacement replacement = summaryReplacements[index];
                    replacement.owner.threadRoots = replacement.threadRoots;
                    replacement.owner.statusRevision = replacement.statusRevision;
                }
                for (int index = 0; index < quietReplacements.Count; index++)
                {
                    PendingQuietCadenceReplacement replacement = quietReplacements[index];
                    replacement.diary.reflectionState = replacement.reflectionState;
                }
                for (int index = 0; index < eventRequestPurges.Count; index++)
                {
                    PendingEventRequestPurge purge = eventRequestPurges[index];
                    purge.diaryEvent.SetActiveMemoryLogicalRequestForRole(purge.role, null);
                    purge.diaryEvent.SetAcceptedPromptPair(
                        purge.role, string.Empty, string.Empty);
                }
                optionalMeaningfulEligibilityBaselineTick = nextEligibilityBaseline;
                if (markMaintenanceDirty)
                {
                    memoryMaintenanceDirty = true;
                    memoryMaintenanceNextItemIndex = 0;
                    memoryMaintenanceHandles.Clear();
                    memoryMaintenanceLegacyDoneForCycle = false;
                    memoryMaintenanceAwaitingPressure = false;
                }

                // The hook sits after every saved/transient swap but before indexes and the retry
                // marker. A fault here must restore the complete old component state.
                InvokeMemoryPolicyReconciliationFaultForTests("after_saved_swap");
                memoryM4IndexesDirty = true;
                indexesTouched = true;
                RebuildMemorySizeIndexes();

                // The applied marker is the linearization point and remains the final fallible-domain
                // write. A retry sees either the complete old tuple or this complete new tuple.
                lastAppliedMemoryPolicyState = nextAppliedState;
                lastAppliedMemoryPolicyFingerprint = policy.fingerprint;
                lastAppliedMemoryPolicyRevision = plan.nextAppliedRevision;
            }
            catch
            {
                rollback.Restore(this);
                if (indexesTouched)
                {
                    try
                    {
                        memoryM4IndexesDirty = true;
                        RebuildMemorySizeIndexes();
                        memoryM4IndexesDirty = rollback.MemoryM4IndexesDirty;
                    }
                    catch (Exception rollbackException)
                    {
                        // The saved marker is still old, so every memory consumer remains fail-closed.
                        // Log only the exception type: derivative rebuild errors must not expose data.
                        Log.ErrorOnce(
                            "[Pawn Diary] Memory policy rollback index rebuild failed ("
                                + rollbackException.GetType().Name + ").",
                            "PawnDiary.Memory.PolicyRollbackIndexes".GetHashCode());
                    }
                }
                throw;
            }

            for (int index = 0; index < baselinedSummaryCount; index++)
                DiaryStateVersion.Bump();
            ReleasePurgedOptionalRuntimeRequests(
                purgedCoordinatorRequests, eventRequestPurges);
            return true;
        }

        /// <summary>
        /// Establishes the saved meaningful-work boundary. Enabling optional work or changing its
        /// category projection baselines current truth; ordinary reconciliation and reload preserve
        /// that exact tick so already-admitted delayed work remains bounded and eligible.
        /// </summary>
        private void EnsureOptionalMeaningfulEligibilityBaseline(
            MemoryPolicySnapshot policy,
            bool force)
        {
            optionalMeaningfulEligibilityBaselineTick =
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    policy?.AllowsOptionalRequests == true,
                    force,
                    optionalMeaningfulEligibilityBaselineTick,
                    Find.TickManager?.TicksGame ?? 0);
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

        /// <summary>Returns the coordinator rows excluded from the detached retained list.</summary>
        private List<SavedActiveLogicalRequestV1> PreparePurgedOptionalCoordinatorRequests(
            List<SavedActiveLogicalRequestV1> retained)
        {
            List<SavedActiveLogicalRequestV1> purged =
                new List<SavedActiveLogicalRequestV1>();
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[index];
                if (request == null || retained.Contains(request)) continue;
                purged.Add(request);
            }
            return purged;
        }

        /// <summary>
        /// Captures event-owned unsent rows without mutating the event. Runtime callback/send release
        /// is deferred until the saved component transaction has committed.
        /// </summary>
        private List<PendingEventRequestPurge> PrepareUnsentOptionalEventRequestPurges()
        {
            List<PendingEventRequestPurge> result =
                new List<PendingEventRequestPurge>();
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                if (diaryEvent == null) continue;
                AddUnsentOptionalEventRequestPurge(
                    result, diaryEvent, DiaryEvent.InitiatorRole);
                AddUnsentOptionalEventRequestPurge(
                    result, diaryEvent, DiaryEvent.RecipientRole);
                AddUnsentOptionalEventRequestPurge(
                    result, diaryEvent, DiaryEvent.NeutralRole);
            }
            return result;
        }

        private static void AddUnsentOptionalEventRequestPurge(
            List<PendingEventRequestPurge> result,
            DiaryEvent diaryEvent,
            string role)
        {
            SavedActiveLogicalRequestV1 request =
                diaryEvent.ActiveMemoryLogicalRequestForRole(role);
            if (request == null
                || !MemoryDispatchTokens.IsOptionalPurpose(request.requestPurposeToken)
                || MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(request)) return;
            result.Add(new PendingEventRequestPurge
            {
                diaryEvent = diaryEvent,
                role = role,
                request = request,
                acceptedSystemPrompt = diaryEvent.AcceptedSystemPromptForRole(role),
                acceptedUserPrompt = diaryEvent.PromptForRole(role)
            });
        }

        /// <summary>
        /// Releases runtime-only claims after saved publication. The published process gate already
        /// rejects superseded optional work, and each adapter is isolated so a cleanup fault cannot
        /// turn a committed component tuple back into a retrying partial transaction.
        /// </summary>
        private void ReleasePurgedOptionalRuntimeRequests(
            List<SavedActiveLogicalRequestV1> coordinatorRequests,
            List<PendingEventRequestPurge> eventRequests)
        {
            HashSet<string> released = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; coordinatorRequests != null
                && index < coordinatorRequests.Count; index++)
                ReleasePurgedOptionalRuntimeRequest(coordinatorRequests[index], released);
            for (int index = 0; eventRequests != null && index < eventRequests.Count; index++)
                ReleasePurgedOptionalRuntimeRequest(eventRequests[index].request, released);
        }

        private void ReleasePurgedOptionalRuntimeRequest(
            SavedActiveLogicalRequestV1 request,
            HashSet<string> released)
        {
            if (request == null) return;
            string identity = string.IsNullOrWhiteSpace(request.logicalRequestId)
                ? (request.eventIdOrOpportunityKey ?? string.Empty) + "\n"
                    + (request.povRoleToken ?? string.Empty)
                : request.logicalRequestId;
            if (!released.Add(identity)) return;
            try
            {
                LlmClient.CancelQueued(
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken);
            }
            catch (Exception exception)
            {
                RecordMemoryRuntimeReleaseFailure("queue", identity, exception);
            }
            try
            {
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    request.logicalRequestId);
            }
            catch (Exception exception)
            {
                RecordMemoryRuntimeReleaseFailure("envelope", identity, exception);
            }
        }

        private static void RecordMemoryRuntimeReleaseFailure(
            string stage,
            string identity,
            Exception exception)
        {
            Log.ErrorOnce(
                "[Pawn Diary] Optional memory runtime release failed at " + stage + " ("
                    + exception.GetType().Name + ").",
                ("PawnDiary.Memory.PolicyRuntimeRelease:" + stage + ":" + identity).GetHashCode());
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

        /// <summary>
        /// Baselines Summary payloads only on detached root copies. The caller later swaps every
        /// changed owner together with the other prepared component state.
        /// </summary>
        private List<PendingSummaryBaselineReplacement> PrepareSummaryBaselineReplacements(
            MemoryPolicySnapshot policy,
            out int changedSummaryCount)
        {
            changedSummaryCount = 0;
            List<PendingSummaryBaselineReplacement> result =
                new List<PendingSummaryBaselineReplacement>();
            HashSet<PawnKnowledgeState> seen = new HashSet<PawnKnowledgeState>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnKnowledgeState owner = diaries[index]?.KnowledgeStateOrNull();
                if (owner == null || !owner.IsCurrentSchema() || !seen.Add(owner)) continue;
                PawnKnowledgeState detached = new PawnKnowledgeState
                {
                    pawnId = owner.pawnId,
                    statusRevision = owner.statusRevision,
                    threadRoots = CloneSavedRootsExact(owner.threadRoots)
                };
                int ownerChanges = 0;
                foreach (SummaryWordingCurrentSnapshot summary in
                    CurrentOwnerSummaries(detached, policy))
                {
                    if (ApplySummaryTerminal(
                            detached,
                            summary,
                            MemoryOptionalWordingDispositionTokens.Disabled,
                            false)) ownerChanges++;
                }
                if (ownerChanges == 0) continue;
                changedSummaryCount += ownerChanges;
                result.Add(new PendingSummaryBaselineReplacement
                {
                    owner = owner,
                    threadRoots = detached.threadRoots,
                    statusRevision = detached.statusRevision
                });
            }
            return result;
        }

        private static List<SavedMemoryThreadRoot> CloneSavedRootsExact(
            List<SavedMemoryThreadRoot> values)
        {
            List<SavedMemoryThreadRoot> result = new List<SavedMemoryThreadRoot>();
            for (int index = 0; values != null && index < values.Count; index++)
                result.Add(values[index] == null ? null : CloneSavedRoot(values[index]));
            return result;
        }

        /// <summary>Prepares quiet-cadence baselines without touching live diary records.</summary>
        private List<PendingQuietCadenceReplacement> PrepareQuietCadenceReplacements()
        {
            int day = CurrentDayIndex;
            List<PendingQuietCadenceReplacement> result =
                new List<PendingQuietCadenceReplacement>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                PawnKnowledgeState owner = diary?.KnowledgeStateOrNull();
                if (owner == null || !owner.IsCurrentSchema()) continue;
                PawnReflectionState reflection =
                    CloneReflectionState(diary.reflectionState) ?? new PawnReflectionState();
                reflection.Normalize();
                reflection.memoryReflectionSchemaVersion = 1;
                reflection.memoryOwnerEpochToken = owner.autobiographicalEpochToken;
                reflection.lastQuietMemoryEvaluatedAbsoluteDay = day;
                reflection.lastQuietMemoryDecisionKey =
                    MemoryDeterministicRngV1.CreateDecisionKey(
                        owner.pawnId, owner.autobiographicalEpochToken, day, false);
                result.Add(new PendingQuietCadenceReplacement
                {
                    diary = diary,
                    originalReflectionState = diary.reflectionState,
                    reflectionState = reflection
                });
            }
            return result;
        }

        private void InvokeMemoryPolicyReconciliationFaultForTests(string stage)
        {
            memoryPolicyReconciliationFaultForTests?.Invoke(stage);
        }

        private sealed class PendingEpisodeReplacement
        {
            internal PawnKnowledgeState owner;
            internal List<SavedMemoryCaptureEpisode> episodes;
        }

        private sealed class PendingSummaryBaselineReplacement
        {
            internal PawnKnowledgeState owner;
            internal List<SavedMemoryThreadRoot> threadRoots;
            internal long statusRevision;
        }

        private sealed class PendingQuietCadenceReplacement
        {
            internal PawnDiaryRecord diary;
            internal PawnReflectionState originalReflectionState;
            internal PawnReflectionState reflectionState;
        }

        private sealed class PendingEventRequestPurge
        {
            internal DiaryEvent diaryEvent;
            internal string role = string.Empty;
            internal SavedActiveLogicalRequestV1 request;
            internal string acceptedSystemPrompt = string.Empty;
            internal string acceptedUserPrompt = string.Empty;
        }

        /// <summary>
        /// Direct-reference rollback for the short assignment-only publication window. Every mutable
        /// object was prepared detached, so restoration needs no deep allocation on the failure path.
        /// </summary>
        private sealed class MemoryPolicyReconciliationRollback
        {
            private readonly MemoryInvokedGenerationCutoffTable cutoffs;
            private readonly long globalCancellationGeneration;
            private readonly List<SavedActiveLogicalRequestV1> activeRequests;
            private readonly List<SavedSummaryWordingOpportunityV1> summaryOpportunities;
            private readonly long eligibilityBaselineTick;
            private readonly SavedMemoryAppliedPolicyStateV1 appliedState;
            private readonly string appliedFingerprint;
            private readonly long appliedRevision;
            private readonly bool maintenanceDirty;
            private readonly int maintenanceNextItemIndex;
            private readonly bool maintenanceLegacyDone;
            private readonly bool maintenanceAwaitingPressure;
            private readonly List<MemoryMaintenanceHandle> maintenanceHandles;
            private readonly List<OwnerRollback> owners = new List<OwnerRollback>();
            private readonly List<PendingQuietCadenceReplacement> reflections;
            private readonly List<PendingEventRequestPurge> eventRequests;

            internal bool MemoryM4IndexesDirty { get; private set; }

            internal MemoryPolicyReconciliationRollback(
                DiaryGameComponent component,
                List<PendingEpisodeReplacement> episodes,
                List<PendingSummaryBaselineReplacement> summaries,
                List<PendingQuietCadenceReplacement> quiet,
                List<PendingEventRequestPurge> eventsToPurge)
            {
                cutoffs = component.invokedGenerationCutoffs;
                globalCancellationGeneration =
                    component.globalOptionalRequestCancellationGeneration;
                activeRequests = component.activeMemoryCoordinatorRequests;
                summaryOpportunities = component.summaryWordingOpportunities;
                eligibilityBaselineTick = component.optionalMeaningfulEligibilityBaselineTick;
                appliedState = component.lastAppliedMemoryPolicyState;
                appliedFingerprint = component.lastAppliedMemoryPolicyFingerprint;
                appliedRevision = component.lastAppliedMemoryPolicyRevision;
                maintenanceDirty = component.memoryMaintenanceDirty;
                maintenanceNextItemIndex = component.memoryMaintenanceNextItemIndex;
                maintenanceLegacyDone = component.memoryMaintenanceLegacyDoneForCycle;
                maintenanceAwaitingPressure = component.memoryMaintenanceAwaitingPressure;
                maintenanceHandles = new List<MemoryMaintenanceHandle>(
                    component.memoryMaintenanceHandles);
                MemoryM4IndexesDirty = component.memoryM4IndexesDirty;
                reflections = quiet ?? new List<PendingQuietCadenceReplacement>();
                eventRequests = eventsToPurge ?? new List<PendingEventRequestPurge>();

                HashSet<PawnKnowledgeState> seen = new HashSet<PawnKnowledgeState>();
                for (int index = 0; episodes != null && index < episodes.Count; index++)
                    AddOwner(episodes[index]?.owner, seen);
                for (int index = 0; summaries != null && index < summaries.Count; index++)
                    AddOwner(summaries[index]?.owner, seen);
            }

            private void AddOwner(PawnKnowledgeState owner, HashSet<PawnKnowledgeState> seen)
            {
                if (owner == null || !seen.Add(owner)) return;
                owners.Add(new OwnerRollback
                {
                    owner = owner,
                    threadRoots = owner.threadRoots,
                    openCaptureEpisodes = owner.openCaptureEpisodes,
                    statusRevision = owner.statusRevision
                });
            }

            internal void Restore(DiaryGameComponent component)
            {
                component.invokedGenerationCutoffs = cutoffs;
                component.globalOptionalRequestCancellationGeneration =
                    globalCancellationGeneration;
                component.activeMemoryCoordinatorRequests = activeRequests;
                component.summaryWordingOpportunities = summaryOpportunities;
                component.optionalMeaningfulEligibilityBaselineTick = eligibilityBaselineTick;
                component.lastAppliedMemoryPolicyState = appliedState;
                component.lastAppliedMemoryPolicyFingerprint = appliedFingerprint;
                component.lastAppliedMemoryPolicyRevision = appliedRevision;
                component.memoryMaintenanceDirty = maintenanceDirty;
                component.memoryMaintenanceNextItemIndex = maintenanceNextItemIndex;
                component.memoryMaintenanceLegacyDoneForCycle = maintenanceLegacyDone;
                component.memoryMaintenanceAwaitingPressure = maintenanceAwaitingPressure;
                component.memoryMaintenanceHandles.Clear();
                component.memoryMaintenanceHandles.AddRange(maintenanceHandles);
                component.memoryM4IndexesDirty = MemoryM4IndexesDirty;
                for (int index = 0; index < owners.Count; index++)
                {
                    OwnerRollback owner = owners[index];
                    owner.owner.threadRoots = owner.threadRoots;
                    owner.owner.openCaptureEpisodes = owner.openCaptureEpisodes;
                    owner.owner.statusRevision = owner.statusRevision;
                }
                for (int index = 0; index < reflections.Count; index++)
                {
                    PendingQuietCadenceReplacement reflection = reflections[index];
                    reflection.diary.reflectionState = reflection.originalReflectionState;
                }
                for (int index = 0; index < eventRequests.Count; index++)
                {
                    PendingEventRequestPurge request = eventRequests[index];
                    request.diaryEvent.SetActiveMemoryLogicalRequestForRole(
                        request.role, request.request);
                    request.diaryEvent.RestoreAcceptedPromptPairExact(
                        request.role,
                        request.acceptedSystemPrompt,
                        request.acceptedUserPrompt);
                }
            }

            private sealed class OwnerRollback
            {
                internal PawnKnowledgeState owner;
                internal List<SavedMemoryThreadRoot> threadRoots;
                internal List<SavedMemoryCaptureEpisode> openCaptureEpisodes;
                internal long statusRevision;
            }
        }
    }
}
