// Pawn-memory reset boundary. Vanilla Brainwipe erases a pawn's remembered experiences several hours
// after its psychic ritual finishes; this partial keeps the corresponding saved Pawn Diary mutation
// inside the component that owns those records. Voice/settings survive because they describe how the
// pawn writes after the wipe, while pages and narrative-memory bookkeeping do not.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Forgets every personal diary page and narrative memory owned by one pawn while preserving
        /// their player-configured voice and generation settings. Shared events remain available to
        /// other pawns; only events no diary references anymore are removed from the hot repository.
        /// Returns whether the reset removed the pawn's exact player-authored background singleton, so
        /// the post-Brainwipe adapter can show a truthful non-blocking notice only when relevant.
        /// </summary>
        internal bool ForgetDiaryHistory(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            int memoryBoundaryTick = Find.TickManager?.TicksGame ?? 0;
            // Observe the player-authored singleton before the M2 fence clears the unified envelope.
            // The public Brainwipe notice must remain truthful even though dispatch invalidation now
            // deliberately happens before the rest of the historical cleanup.
            bool removedPlayerBackground = HasCanonicalBackgroundMemory(pawnId);
            AdvanceMemoryDispatchFenceForBrainwipe(pawn, pawnId);
            ResetProgressionMemoryForPawn(pawnId, memoryBoundaryTick);
            ResetSharedArcMemoryForPawn(pawnId, memoryBoundaryTick);
            RebaselineDayStartOpinionsForPawn(pawn);
            RebaselineThoughtProgressionsForPawn(pawn);
            RebaselineHediffProgressionsForPawn(pawn);
            // These process-static correlation caches contain detached primitive facts only. Clear the
            // exact target even with every DLC disabled: a later save/expiry flush must not recreate
            // title, thought, or belief prose captured before the memory boundary.
            RoyalMutationCorrelation.ForgetPawn(pawnId);
            RoyalTitleThoughtCorrelation.ForgetPawn(pawnId);
            BeliefMutationCache.ForgetPawn(pawnId);
            BeliefHistoryCorrelationCache.ForgetPawn(pawnId);
            // A row may be accepted before its source page creates a diary record, so cancel the wiped
            // pawn's future autobiographical reflection before the missing-diary early return below.
            // Rows where this pawn is only the subject belong to another pawn and intentionally survive.
            CancelPendingSocialReflectionsForWriter(pawnId);
            ForgetSocialReflectionCooldownsForPawn(pawnId);
            SettlePendingMemoryWritersForPawn(pawnId);
            AnomalyRecentStudyCache.ForgetStudier(pawnId);
            ForgetRecentEventsForPawn(pawnId);
            RemovePendingDayDigestForPawn(pawnId);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null)
            {
                return false;
            }

            diary.eventIds?.Clear();
            diary.favoriteEntryKeys?.Clear();
            diary.hasUnreadGeneratedEntry = false;
            diary.unreadGeneratedEntryCount = 0;
            diary.acknowledgedGeneratedEntryCount = 0;

            // Culture provenance describes identity rather than an episodic memory, so retain it while
            // dropping the durable important-event records and player background used as factual prompts.
            PawnKnowledgeState knowledge = diary.KnowledgeStateOrNull();
            knowledge?.records?.Clear();

            // Reset every scheduler/cache whose values refer to now-forgotten pages. Fresh reflection
            // and belief state request silent baselines before they can write about post-wipe changes.
            diary.arcSchedule = new PawnArcScheduleState();
            diary.beliefState = new PawnBeliefState();
            diary.reflectionState = new PawnReflectionState();
            // AdvanceMemoryDispatchFenceForBrainwipe published the new owner epoch before cleanup.
            // Replacing the reflection scheduler must preserve that same fence; otherwise its blank
            // defaults can make old-epoch reflection work look current again after the wipe.
            string currentOwnerEpoch =
                knowledge?.autobiographicalEpochToken ?? string.Empty;
            diary.reflectionState.memoryReflectionSchemaVersion =
                string.IsNullOrEmpty(currentOwnerEpoch) ? 0 : 1;
            diary.reflectionState.memoryOwnerEpochToken = currentOwnerEpoch;

            archive.RemoveForPawn(pawnId);
            // A surviving partner's compact page must not retain a clickable preview of memories the
            // wiped pawn no longer owns. This is display linkage only; the partner's own page survives.
            archive.ClearLinksToPawn(pawnId);

            HashSet<string> survivingHotEventIds = CollectHotReferencedEventIds();
            DetachForgottenRolesFromSharedEvents(pawnId, survivingHotEventIds);
            events.RetainOnly(survivingHotEventIds);
            SetCachedCommandStatus(pawnId, 0, 0, 0);
            DiaryStateVersion.Bump();
            return removedPlayerBackground;
        }

        /// <summary>
        /// M2 hard fence: allocate a fresh owner epoch, advance target cancellation, clear every
        /// old-epoch request/prompt, and publish an empty current envelope before ordinary Brainwipe
        /// cleanup runs. No new memory is captured; M11 owns the first post-wipe Landmark.
        /// </summary>
        private void AdvanceMemoryDispatchFenceForBrainwipe(Pawn pawn, string pawnId)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            // T11.5 forbids the historical missing-diary early return from skipping the epoch fence.
            PawnDiaryRecord ensured = FindDiary(pawn, true);
            if (ensured == null)
            {
                return;
            }

            CollectAndPublishAllocatorCarriers();
            List<string> liveEpochs = SnapshotAutobiographicalEpochCarriers();
            MemoryEpochAllocationPlan epochPlan = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = pawnId,
                    lastIssuedSequence = lastIssuedAutobiographicalEpochSequence,
                    fallbackChain = lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                    liveEpochCarriers = liveEpochs,
                    isTargetBrainwipe = true
                });
            if (!epochPlan.canMutate)
            {
                // A target wipe must not reuse an epoch, but allocator corruption is never permission
                // to retain the autobiography being wiped. Publish an empty unenrolled current
                // envelope; old work cannot match its blank epoch, and later facts may retry normal
                // enrollment after the allocator state is repaired.
                RecordMemoryDiagnostic("other", "owner");
                ClearBrainwipeMemoryWithoutFreshEpoch(pawnId, ensured);
                CancelOldEpochDispatchRows(pawnId);
                ResetMemoryMaintenanceTransient(true);
                RebuildMemorySizeIndexes();
                return;
            }

            long greatestCancellation = 0;
            List<PawnDiaryRecord> holders = new List<PawnDiaryRecord>();
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary == null
                    || (!string.Equals(diary.pawnId, pawnId, StringComparison.Ordinal)
                        && !string.Equals(
                            diary.knowledgeState?.pawnId,
                            pawnId,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                holders.Add(diary);
                if (diary.knowledgeState != null
                    && diary.knowledgeState.requestCancellationGeneration
                        > greatestCancellation)
                {
                    greatestCancellation =
                        diary.knowledgeState.requestCancellationGeneration;
                }
            }
            GreatestRequestCancellation(
                pawnId, activeMemoryCoordinatorRequests, ref greatestCancellation);
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                GreatestRequestCancellation(
                    pawnId,
                    diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole),
                    ref greatestCancellation);
                GreatestRequestCancellation(
                    pawnId,
                    diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.RecipientRole),
                    ref greatestCancellation);
                GreatestRequestCancellation(
                    pawnId,
                    diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.NeutralRole),
                    ref greatestCancellation);
            }

            long nextCancellation = greatestCancellation == long.MaxValue
                ? 1
                : Math.Max(1, greatestCancellation + 1);

            MemoryBrainwipeDirectoryPlan directoryPlan = PlanBrainwipeDirectoryAdmission(pawnId);
            if (directoryPlan.requiresDisplacement)
            {
                if (string.IsNullOrEmpty(directoryPlan.displacedOwnerPawnId))
                {
                    // A malformed save may already violate the reserved invariant. The wipe still
                    // clears/fences its target, but it never converts that corruption into permission
                    // to delete another autobiography.
                    RecordMemoryDiagnosticOnce("brainwipe_capacity", "component");
                }
                else
                {
                    DisplaceEmptyBrainwipeFence(directoryPlan.displacedOwnerPawnId);
                }
            }

            // Publish allocator/fence fields before old autobiographical payload is cleared.
            lastIssuedAutobiographicalEpochSequence = epochPlan.nextSequence;
            lastIssuedAutobiographicalEpochFallbackChain =
                epochPlan.nextFallbackChain ?? string.Empty;
            if (unresolvedArchiveReattributionGeneration < long.MaxValue)
            {
                unresolvedArchiveReattributionGeneration++;
            }
            else
            {
                unresolvedArchiveReattributionDisabled = true;
            }

            for (int index = 0; index < holders.Count; index++)
            {
                PawnDiaryRecord holder = holders[index];
                PawnKnowledgeState state = holder.knowledgeState
                    ?? PawnKnowledgeState.CreateCurrent(pawnId);
                holder.knowledgeState = state;
                ClearUnifiedMemoryEnvelope(state);
                state.schemaVersion = PawnKnowledgeState.CurrentSchemaVersion;
                state.pawnId = pawnId;
                state.completedDiaryEntryOrdinal = 1;
                if (index == 0)
                {
                    state.autobiographicalEpochToken = epochPlan.epochToken;
                    state.epochFenceOnly = true;
                    state.requestCancellationGeneration = nextCancellation;
                    state.structuralRevision = 1;
                    state.statusRevision = 1;
                }

                PawnReflectionState reflection = holder.reflectionState;
                if (reflection != null)
                {
                    reflection.memoryReflectionSchemaVersion = index == 0 ? 1 : 0;
                    reflection.memoryOwnerEpochToken = index == 0
                        ? epochPlan.epochToken
                        : string.Empty;
                    reflection.lastQuietMemoryEvaluatedAbsoluteDay = -1;
                    reflection.lastQuietMemoryActivatedAbsoluteQuadrum = -1;
                    reflection.lastQuietMemoryDecisionKey = string.Empty;
                }
            }

            CancelOldEpochDispatchRows(pawnId);
            ResetMemoryMaintenanceTransient(true);
            RebuildMemorySizeIndexes();
        }

        private MemoryBrainwipeDirectoryPlan PlanBrainwipeDirectoryAdmission(string targetPawnId)
        {
            int fenceCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 1, 1001, 4001);
            var holdersByOwner = new SortedDictionary<string, List<PawnDiaryRecord>>(
                StringComparer.Ordinal);
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                PawnKnowledgeState state = diary?.knowledgeState;
                string ownerId = diary?.pawnId ?? string.Empty;
                if (state == null || !state.IsCurrentSchema() || state.archiveOnly
                    || string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)
                    || string.IsNullOrWhiteSpace(ownerId)) continue;
                List<PawnDiaryRecord> group;
                if (!holdersByOwner.TryGetValue(ownerId, out group))
                {
                    group = new List<PawnDiaryRecord>();
                    holdersByOwner.Add(ownerId, group);
                }
                group.Add(diary);
            }

            var candidates = new List<MemoryBrainwipeFenceCandidate>();
            foreach (KeyValuePair<string, List<PawnDiaryRecord>> pair in holdersByOwner)
                candidates.Add(SnapshotBrainwipeFenceCandidate(pair.Key, pair.Value));
            return MemoryBrainwipeHeadroomPolicy.PlanDirectoryAdmission(
                targetPawnId,
                holdersByOwner.ContainsKey(targetPawnId ?? string.Empty),
                holdersByOwner.Count,
                fenceCap,
                candidates);
        }

        private MemoryBrainwipeFenceCandidate SnapshotBrainwipeFenceCandidate(
            string ownerPawnId,
            List<PawnDiaryRecord> holders)
        {
            MemoryBrainwipeFenceCandidate candidate = new MemoryBrainwipeFenceCandidate
            {
                ownerPawnId = ownerPawnId ?? string.Empty,
                currentSchema = true,
                epochFenceOnly = true,
                hasEpoch = true,
                hasActiveRequestOrOpportunity = HasBrainwipeFenceActiveWork(ownerPawnId)
            };
            for (int index = 0; holders != null && index < holders.Count; index++)
            {
                PawnDiaryRecord diary = holders[index];
                PawnKnowledgeState state = diary?.knowledgeState;
                candidate.currentSchema &= state != null && state.IsCurrentSchema();
                candidate.archiveOnly |= state?.archiveOnly == true;
                candidate.epochFenceOnly &= state?.epochFenceOnly == true;
                candidate.hasEpoch &= !string.IsNullOrWhiteSpace(
                    state?.autobiographicalEpochToken);
                candidate.hasAutobiographicalPayload |= HasBrainwipeFencePayload(state);
                candidate.hasPageOrNonMemoryState |= (diary?.eventIds?.Count ?? 0) > 0
                    || (diary?.favoriteEntryKeys?.Count ?? 0) > 0
                    || diary?.hasUnreadGeneratedEntry == true
                    || (diary?.unreadGeneratedEntryCount ?? 0) > 0
                    || HasNonMemoryReflectionState(diary?.reflectionState);
            }
            return candidate;
        }

        private bool HasBrainwipeFenceActiveWork(string ownerPawnId)
        {
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
                if (activeMemoryCoordinatorRequests[index]?.ownerPawnId == ownerPawnId) return true;
            for (int index = 0; summaryWordingOpportunities != null
                && index < summaryWordingOpportunities.Count; index++)
                if (summaryWordingOpportunities[index]?.ownerPawnId == ownerPawnId) return true;
            IReadOnlyList<DiaryEvent> hot = events?.AllEvents;
            for (int index = 0; hot != null && index < hot.Count; index++)
            {
                DiaryEvent diaryEvent = hot[index];
                if (RequestBelongsToOwner(
                        diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole),
                        ownerPawnId)
                    || RequestBelongsToOwner(
                        diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.RecipientRole),
                        ownerPawnId)
                    || RequestBelongsToOwner(
                        diaryEvent?.ActiveMemoryLogicalRequestForRole(DiaryEvent.NeutralRole),
                        ownerPawnId)) return true;
            }
            return false;
        }

        private static bool RequestBelongsToOwner(
            SavedActiveLogicalRequestV1 request, string ownerPawnId)
        {
            return request != null && string.Equals(
                request.ownerPawnId, ownerPawnId, StringComparison.Ordinal);
        }

        private static bool HasBrainwipeFencePayload(PawnKnowledgeState state)
        {
            return state == null
                || (state.records?.Count ?? 0) > 0
                || (state.standaloneBlocks?.Count ?? 0) > 0
                || (state.threadRoots?.Count ?? 0) > 0
                || !string.IsNullOrWhiteSpace(state.playerBackground)
                || (state.ownerAwarenessSnapshots?.Count ?? 0) > 0
                || (state.openCaptureEpisodes?.Count ?? 0) > 0
                || (state.repetitionGuardRows?.Count ?? 0) > 0
                || (state.importedArchiveRows?.Count ?? 0) > 0;
        }

        private static bool HasNonMemoryReflectionState(PawnReflectionState state)
        {
            if (state == null) return false;
            return !state.baselineOnNextOpportunity
                || !state.linkedBaselineOnNextOpportunity
                || state.lastReflectionTick != -1
                || state.lastMajorArcTick != -1
                || state.lastCrossArcTick != -1
                || state.lastBeliefTick != -1
                || state.lastQuadrumTick != -1
                || state.lastDayTick != -1
                || state.pendingMajorArc
                || state.pendingMajorArcRequestedTick != -1
                || !string.IsNullOrEmpty(state.pendingMajorArcAvoidEventId);
        }

        private void DisplaceEmptyBrainwipeFence(string ownerPawnId)
        {
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary == null || !string.Equals(
                        diary.pawnId, ownerPawnId, StringComparison.Ordinal)) continue;
                PawnKnowledgeState state = diary.knowledgeState;
                ClearUnifiedMemoryEnvelope(state);
                state.schemaVersion = PawnKnowledgeState.CurrentSchemaVersion;
                state.pawnId = ownerPawnId;
                state.completedDiaryEntryOrdinal = 1;
                PawnReflectionState reflection = diary.reflectionState;
                if (reflection == null) continue;
                reflection.memoryReflectionSchemaVersion = 0;
                reflection.memoryOwnerEpochToken = string.Empty;
                reflection.lastQuietMemoryEvaluatedAbsoluteDay = -1;
                reflection.lastQuietMemoryActivatedAbsoluteQuadrum = -1;
                reflection.lastQuietMemoryDecisionKey = string.Empty;
            }
        }

        private void ClearBrainwipeMemoryWithoutFreshEpoch(
            string pawnId,
            PawnDiaryRecord ensured)
        {
            bool clearedEnsured = false;
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord holder = diaries[index];
                if (holder == null
                    || (!string.Equals(holder.pawnId, pawnId, StringComparison.Ordinal)
                        && !string.Equals(
                            holder.knowledgeState?.pawnId, pawnId, StringComparison.Ordinal))) continue;
                PawnKnowledgeState state = holder.knowledgeState
                    ?? PawnKnowledgeState.CreateCurrent(pawnId);
                holder.knowledgeState = state;
                ClearUnifiedMemoryEnvelope(state);
                state.schemaVersion = PawnKnowledgeState.CurrentSchemaVersion;
                state.pawnId = pawnId;
                clearedEnsured |= ReferenceEquals(holder, ensured);
            }
            if (!clearedEnsured)
            {
                PawnKnowledgeState state = ensured.knowledgeState
                    ?? PawnKnowledgeState.CreateCurrent(pawnId);
                ensured.knowledgeState = state;
                ClearUnifiedMemoryEnvelope(state);
                state.schemaVersion = PawnKnowledgeState.CurrentSchemaVersion;
                state.pawnId = pawnId;
            }
        }

        private static void ClearUnifiedMemoryEnvelope(PawnKnowledgeState state)
        {
            state.autobiographicalEpochToken = string.Empty;
            state.archiveOnly = false;
            state.epochFenceOnly = false;
            state.requestCancellationGeneration = 0;
            state.structuralRevision = 0;
            state.statusRevision = 0;
            state.completedDiaryEntryOrdinal = 1;
            state.records?.Clear();
            state.standaloneBlocks?.Clear();
            state.threadRoots?.Clear();
            state.playerBackground = string.Empty;
            state.ownerAwarenessSnapshots?.Clear();
            state.openCaptureEpisodes?.Clear();
            state.repetitionGuardRows?.Clear();
            state.importedArchiveRows?.Clear();
            state.migrationDiagnosticFlags = 0;
        }

        private void CancelOldEpochDispatchRows(string pawnId)
        {
            long terminalTick = Math.Max(1, Find.TickManager?.TicksGame ?? 0);
            for (int index = 0; activeMemoryCoordinatorRequests != null
                && index < activeMemoryCoordinatorRequests.Count; index++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[index];
                if (request == null || !string.Equals(
                    request.ownerPawnId, pawnId, StringComparison.Ordinal)) continue;
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    request.logicalRequestId);
                invokedGenerationCutoffs.Settle(request.logicalRequestId);
                AppendTerminalMemoryAttemptAudits(
                    request,
                    MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(request)
                        ? MemoryDispatchTokens.CancelledPostInvocation
                        : MemoryDispatchTokens.CancelledPreInvocation,
                    terminalTick);
            }
            activeMemoryCoordinatorRequests?.RemoveAll(
                request => request != null
                    && string.Equals(request.ownerPawnId, pawnId, StringComparison.Ordinal));
            summaryWordingOpportunities?.RemoveAll(
                opportunity => opportunity != null
                    && string.Equals(
                        opportunity.ownerPawnId,
                        pawnId,
                        StringComparison.Ordinal));

            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                if (diaryEvent == null) continue;
                CancelOldEpochEventRole(diaryEvent, DiaryEvent.InitiatorRole, pawnId);
                CancelOldEpochEventRole(diaryEvent, DiaryEvent.RecipientRole, pawnId);
                CancelOldEpochEventRole(diaryEvent, DiaryEvent.NeutralRole, pawnId);
            }
        }

        private void CancelOldEpochEventRole(
            DiaryEvent diaryEvent,
            string povRole,
            string pawnId)
        {
            SavedActiveLogicalRequestV1 request =
                diaryEvent.ActiveMemoryLogicalRequestForRole(povRole);
            bool ownsRole = string.Equals(
                diaryEvent.PawnIdForRole(povRole), pawnId, StringComparison.Ordinal);
            bool ownsRequest = request != null
                && string.Equals(request.ownerPawnId, pawnId, StringComparison.Ordinal);
            if (!ownsRole && !ownsRequest) return;

            if (request != null)
            {
                MemoryDispatchRuntimeBridge.ReleaseLogicalRequestSendEnvelopes(
                    request.logicalRequestId);
                invokedGenerationCutoffs.Settle(request.logicalRequestId);
                AppendTerminalMemoryAttemptAudits(
                    request,
                    MemoryDispatchSavedAdapter.LoadedRequestMayHaveBeenInvoked(request)
                        ? MemoryDispatchTokens.CancelledPostInvocation
                        : MemoryDispatchTokens.CancelledPreInvocation,
                    Math.Max(1, Find.TickManager?.TicksGame ?? 0));
            }
            diaryEvent.SetActiveMemoryLogicalRequestForRole(povRole, null);
            diaryEvent.SetAcceptedPromptPair(povRole, string.Empty, string.Empty);
        }

        private static void GreatestRequestCancellation(
            string pawnId,
            List<SavedActiveLogicalRequestV1> requests,
            ref long greatest)
        {
            for (int index = 0; requests != null && index < requests.Count; index++)
                GreatestRequestCancellation(pawnId, requests[index], ref greatest);
        }

        private static void GreatestRequestCancellation(
            string pawnId,
            SavedActiveLogicalRequestV1 request,
            ref long greatest)
        {
            if (request != null
                && string.Equals(request.ownerPawnId, pawnId, StringComparison.Ordinal)
                && request.ownerCancellationGeneration > greatest)
            {
                greatest = request.ownerCancellationGeneration;
            }
        }

        private bool HasCanonicalBackgroundMemory(string ownerPawnId)
        {
            if (string.IsNullOrWhiteSpace(ownerPawnId))
            {
                return false;
            }

            // Duplicate legacy containers can survive until migration. The Brainwipe notice is
            // truthful if any exact owner holder contains either the current unified singleton or
            // the pre-current canonical record; scanning one lookup winner can miss the other.
            for (int holderIndex = 0; diaries != null && holderIndex < diaries.Count; holderIndex++)
            {
                PawnDiaryRecord holder = diaries[holderIndex];
                PawnKnowledgeState state = holder?.knowledgeState;
                bool exactOwner = string.Equals(
                        holder?.pawnId, ownerPawnId, StringComparison.Ordinal)
                    || string.Equals(
                        state?.pawnId, ownerPawnId, StringComparison.Ordinal);
                if (!exactOwner || state == null) continue;
                if (!string.IsNullOrWhiteSpace(state.playerBackground)) return true;

                for (int recordIndex = 0;
                    state.records != null && recordIndex < state.records.Count;
                    recordIndex++)
                {
                    ImportantMemoryRecord record = state.records[recordIndex];
                    if (record != null && PlayerMemoryPolicy.IsCanonicalBackstory(
                        ownerPawnId,
                        record.recordId,
                        record.dedupKey,
                        record.eventKind,
                        record.sourceKind,
                        record.recallScope))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Settles or removes every delayed writer-owned source that captured facts before Brainwipe.
        /// A shared interaction is reduced to the other pawn's solo POV instead of being discarded;
        /// all remaining stores belong to exactly one writer and are pruned without emitting a page.
        /// Same-day guards are removed too, allowing genuinely post-wipe facts to start a fresh note.
        /// </summary>
        private void SettlePendingMemoryWritersForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            // Pair batches carry two writer POVs. Consume only batches that involve the wiped pawn and
            // ask the normal flush path to exclude that writer; its existing survivor branch preserves
            // the other pawn's independent memory as a solo event.
            List<string> interactionKeys = new List<string>();
            foreach (KeyValuePair<string, PendingInteractionBatch> pair in pendingInteractionBatches)
            {
                PendingInteractionBatch batch = pair.Value;
                if (batch != null
                    && (string.Equals(batch.initiatorPawnId, pawnId, StringComparison.Ordinal)
                        || string.Equals(batch.recipientPawnId, pawnId, StringComparison.Ordinal)))
                {
                    interactionKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < interactionKeys.Count; i++)
            {
                PendingInteractionBatch batch;
                if (pendingInteractionBatches.TryGetValue(interactionKeys[i], out batch))
                {
                    string interactionKey = interactionKeys[i];
                    long registrationBefore = events.RegistrationVersion;
                    try
                    {
                        // FlushInteractionBatch removes the source key before it resolves any labels or
                        // persists the survivor. A broken modded label/formatter therefore cannot replay
                        // the captured pre-wipe source even if the survivor projection itself faults.
                        FlushInteractionBatch(interactionKey, batch, pawnId);
                    }
                    catch (Exception exception)
                    {
                        // Brainwipe is a destructive memory boundary: one compatibility fault must never
                        // abort the remaining cleanup and leave the wiped diary intact. Remove again as a
                        // defensive no-op, report once, and audit if persistence had already begun.
                        pendingInteractionBatches.Remove(interactionKey);
                        bool registrationBegan = events.RegistrationVersion > registrationBefore;
                        Log.ErrorOnce(
                            "[Pawn Diary] Brainwipe skipped one malformed pending interaction survivor"
                            + (registrationBegan ? " after event persistence began" : string.Empty)
                            + ": " + exception,
                            "PawnDiary.Brainwipe.PendingInteractionSurvivor".GetHashCode());
                        if (registrationBegan)
                        {
                            RunDiaryIntegrityAudit(
                                "brainwipe_pending_interaction_exception",
                                reportPersistentIssues: true);
                        }
                    }
                }
            }

            List<string> ownedKeys = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                if (pair.Value != null
                    && string.Equals(pair.Value.pawnId, pawnId, StringComparison.Ordinal))
                {
                    ownedKeys.Add(pair.Key);
                }
            }
            RemoveKeys(pendingAmbientInteractionNotes, ownedKeys);

            ownedKeys.Clear();
            foreach (KeyValuePair<string, PendingAmbientThoughtNote> pair in pendingAmbientThoughtNotes)
            {
                if (pair.Value != null
                    && string.Equals(pair.Value.pawnId, pawnId, StringComparison.Ordinal))
                {
                    ownedKeys.Add(pair.Key);
                }
            }
            RemoveKeys(pendingAmbientThoughtNotes, ownedKeys);

            ownedKeys.Clear();
            foreach (KeyValuePair<string, PendingTaleBatch> pair in pendingTaleBatches)
            {
                if (pair.Value != null
                    && string.Equals(pair.Value.pawnId, pawnId, StringComparison.Ordinal))
                {
                    ownedKeys.Add(pair.Key);
                }
            }
            RemoveKeys(pendingTaleBatches, ownedKeys);

            ownedKeys.Clear();
            foreach (string key in pendingDayHediffs.Keys)
            {
                if (DailyEmissionGuardPolicy.IsPawnDayKey(key, pawnId))
                {
                    ownedKeys.Add(key);
                }
            }
            RemoveKeys(pendingDayHediffs, ownedKeys);

            writtenAmbientInteractionNotes.RemoveWhere(
                key => DailyEmissionGuardPolicy.IsInteractionKeyForPawn(key, pawnId));
            writtenAmbientThoughtNotes.RemoveWhere(
                key => DailyEmissionGuardPolicy.IsThoughtKeyForPawn(key, pawnId));
            writtenDayReflections.RemoveWhere(
                key => DailyEmissionGuardPolicy.IsPawnDayKey(key, pawnId));
            writtenQuadrumReflections.RemoveWhere(
                key => PawnScopedTransientKeyPolicy.StartsWithPawnToken(key, pawnId));
            rejectedAmbientInteractionFrequencyKeys?.RemoveAll(
                key => DailyEmissionGuardPolicy.IsInteractionKeyForPawn(key, pawnId));
            acceptedAmbientInteractionFrequencyKeys?.RemoveAll(
                key => DailyEmissionGuardPolicy.IsInteractionKeyForPawn(key, pawnId));

            // These queues are saved because the corresponding Biotech choice can remain open across
            // save/load. They hold detached facts, so clear them even when Biotech is not currently active:
            // a later DLC-enabled load must never replay pre-wipe autobiography. Births can carry two
            // independent writers, and the pure policy preserves the other adult plus only their context.
            // Apply the detached result in place: the component's saved queue remains the one canonical
            // collection while observers already holding that list see the boundary atomically.
            List<PendingBiotechGrowthMoment> survivingGrowth =
                BiotechPendingWriterResetPolicy.RemoveGrowthWriter(
                    pendingBiotechGrowthMoments,
                    pawnId);
            pendingBiotechGrowthMoments.Clear();
            pendingBiotechGrowthMoments.AddRange(survivingGrowth);

            List<PendingBiotechBirthState> survivingBirths =
                BiotechPendingWriterResetPolicy.RemoveBirthWriter(
                    pendingBiotechBirths,
                    pawnId);
            pendingBiotechBirths.Clear();
            pendingBiotechBirths.AddRange(survivingBirths);
        }

        /// <summary>
        /// Retires the wiped pawn's role only on hot masters still owned by another diary. Solo masters
        /// are removed by repository retention; shared masters keep the partner's independent memory.
        /// </summary>
        private void DetachForgottenRolesFromSharedEvents(
            string pawnId,
            HashSet<string> survivingEventIds)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || survivingEventIds == null)
            {
                return;
            }

            foreach (string eventId in survivingEventIds)
            {
                DiaryEvent diaryEvent = events.FindEvent(eventId);
                if (diaryEvent != null
                    && !diaryEvent.solo
                    && DiaryEvent.RoleIsInitiatorOrRecipient(diaryEvent.RoleForPawn(pawnId)))
                {
                    DetachRetiredSharedRole(diaryEvent, pawnId);
                }
            }
        }

        /// <summary>
        /// Terminalizes and severs one pawn's role on a shared master event. Brainwipe and the dev full
        /// purge share this boundary so late LLM results, public lookups, link previews, and reflection
        /// scans cannot rediscover retired autobiography while the other role stays usable.
        /// </summary>
        private bool DetachRetiredSharedRole(DiaryEvent diaryEvent, string pawnId)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            string povRole = diaryEvent.RoleForPawn(pawnId);
            if (!DiaryEvent.RoleIsInitiatorOrRecipient(povRole))
            {
                return false;
            }

            bool retiredInitiator = DiaryEvent.RoleEquals(povRole, DiaryEvent.InitiatorRole);
            diaryEvent.MarkSkipped(povRole, string.Empty);
            if (retiredInitiator)
            {
                diaryEvent.initiatorTitleStatus = DiaryEvent.SkippedStatus;
            }
            else
            {
                diaryEvent.recipientTitleStatus = DiaryEvent.SkippedStatus;
            }

            // Publish the terminal status while integrations can still identify its old owner, then
            // blank that owner ID. ApplyLlmResult treats any later completion for this role as obsolete.
            NotifyEntryStatusChanged(diaryEvent, povRole);
            if (retiredInitiator)
            {
                diaryEvent.initiatorPawnId = string.Empty;
            }
            else
            {
                diaryEvent.recipientPawnId = string.Empty;
            }

            string workKey = diaryEvent.eventId + "|" + povRole;
            delayedRaidGenerationReadyTicks.Remove(workKey);
            orphanCandidatesLastScan.Remove(workKey);
            DiaryStateVersion.Bump();

            // Sequential pairs ordinarily wait for initiator prose. When that role is retired, release
            // a surviving recipient from the base pair prompt; CanQueueGeneration prevents duplicates.
            // Prompt construction can touch third-party pawn/context providers. The autobiographical
            // reset is already committed at this point, so a compatibility failure in this optional
            // survivor release must not escape and suppress the new Brainwipe boundary signal.
            if (retiredInitiator && !diaryEvent.solo)
            {
                try
                {
                    QueueSequentialPairwiseRewrite(diaryEvent);
                }
                catch (Exception exception)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Brainwipe could not release one surviving shared-event writer; "
                        + "the wiped role remains detached and ordinary recovery may retry it: "
                        + exception,
                        "PawnDiary.Brainwipe.SharedRoleSurvivorRelease".GetHashCode());
                }
            }

            return true;
        }

        /// <summary>
        /// Replaces only the wiped pawn's outbound day-start opinions with current values. Otherwise a
        /// relationship change from before Brainwipe could become a post-wipe daily reflection; inbound
        /// rows remain other colonists' memories and are deliberately preserved.
        /// </summary>
        private void RebaselineDayStartOpinionsForPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<string> outboundKeys = new List<string>();
            foreach (string key in dayStartOpinions.Keys)
            {
                if (DailyEmissionGuardPolicy.IsOutboundOpinionKeyForPawn(key, pawnId))
                {
                    outboundKeys.Add(key);
                }
            }

            for (int i = 0; i < outboundKeys.Count; i++)
            {
                dayStartOpinions.Remove(outboundKeys[i]);
            }

            if (pawn.relations == null)
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other == pawn)
                {
                    continue;
                }

                int opinion;
                if (TryReadOpinion(pawn, other, out opinion))
                {
                    dayStartOpinions[pawnId + "|" + other.GetUniqueLoadID()] = opinion;
                }
            }
        }

        /// <summary>Removes an already-snapshotted key list from one transient writer store.</summary>
        private static void RemoveKeys<T>(Dictionary<string, T> source, List<string> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                source.Remove(keys[i]);
            }
        }

        /// <summary>Removes saved and indexed daily digest facts collected before the brainwipe.</summary>
        private void RemovePendingDayDigestForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            if (dayDigestStates != null)
            {
                dayDigestStates.RemoveAll(state =>
                    state != null && string.Equals(state.pawnId, pawnId, StringComparison.Ordinal));
            }

            // The dictionary is a transient index over dayDigestStates. Rebuilding it is less fragile
            // than duplicating the composite pawn/day key rules here.
            RebuildDayDigestIndex();
        }
    }
}
