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
            bool removedPlayerBackground = HasCanonicalBackgroundMemory(knowledge, pawnId);
            knowledge?.records?.Clear();

            // Reset every scheduler/cache whose values refer to now-forgotten pages. Fresh reflection
            // and belief state request silent baselines before they can write about post-wipe changes.
            diary.arcSchedule = new PawnArcScheduleState();
            diary.beliefState = new PawnBeliefState();
            diary.reflectionState = new PawnReflectionState();

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

        private static bool HasCanonicalBackgroundMemory(
            PawnKnowledgeState state,
            string ownerPawnId)
        {
            if (state?.records == null)
            {
                return false;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
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
