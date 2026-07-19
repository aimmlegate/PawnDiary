// Royalty Phase-4 title/psylink orchestration and Phase-5 succession arbitration. Guarded adapters
// supply detached snapshots; this component advances saved truth before optional dispatch,
// arbitrates exact cause owners, and keeps the slow per-faction scanner as the missing-hook/modded
// fallback.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Opens one exact bestowing/anima/neuroformer boundary with copied before facts.</summary>
        internal RoyalMutationBatchSnapshot BeginRoyalMutationCause(
            Pawn pawn,
            Faction faction,
            string causeToken)
        {
            if (!RoyaltyProgressionRuntimeReady() || pawn == null || !IsDiaryEligible(pawn)) return null;
            PawnDiaryRecord diary = FindDiary(pawn, true);
            PawnProgressionState progression = diary?.EnsureProgressionState();
            if (progression == null) return null;
            EnsureRoyaltyObservationReady(pawn, progression);
            int now = Find.TickManager?.TicksGame ?? 0;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            RoyalTitleSnapshot previousTitle = faction == null
                ? null
                : DlcContext.CaptureRoyalTitleForFaction(pawn, faction);
            return RoyalMutationCorrelation.Open(
                pawn.GetUniqueLoadID(),
                DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                faction?.GetUniqueLoadID(),
                causeToken,
                Math.Max(0, now),
                previousTitle,
                DlcContext.CurrentPsylinkLevel(pawn),
                policy.maximumPendingRoyalMutations);
        }

        /// <summary>
        /// Closes an exact cause boundary, advances saved observations immediately, and either stages
        /// a ritual-owned batch or emits the single cause-aware progression owner.
        /// </summary>
        internal RoyalMutationBatchSnapshot CompleteRoyalMutationCause(
            RoyalMutationBatchSnapshot batch,
            Pawn pawn,
            Faction faction)
        {
            if (batch == null || pawn == null || !ModsConfig.RoyaltyActive) return null;
            int now = Find.TickManager?.TicksGame ?? batch.openedTick;
            string pawnId = pawn.GetUniqueLoadID() ?? string.Empty;
            if (!string.Equals(batch.pawnId, pawnId, StringComparison.Ordinal))
            {
                RoyalMutationCorrelation.Cancel(batch);
                return null;
            }
            // Harmony already guards the normal path, but this component seam is also used by loaded
            // tests and defensive finalizers. A second close must not reinterpret "not active" as a
            // bounded-queue overflow and create another Progression page.
            if (!RoyalMutationCorrelation.IsActive(batch)) return null;

            bool correlationClosed = false;
            PawnProgressionState progression = null;
            RoyaltyObservationCheckpoint observationCheckpoint = null;
            try
            {
                RoyalTitleSnapshot beforeTitle = batch.titleMutation?.previousTitle;
                RoyalTitleSnapshot afterTitle = faction == null
                    ? null
                    : DlcContext.CaptureRoyalTitleForFaction(pawn, faction);
                int beforePsylink = batch.psylinkMutation?.previousPsylinkLevel
                    ?? DlcContext.CurrentPsylinkLevel(pawn);
                int afterPsylink = DlcContext.CurrentPsylinkLevel(pawn);
                string correlationId = batch.scope?.correlationId ?? batch.batchId;
                RoyalTitleMutationSnapshot title = faction == null ? null : new RoyalTitleMutationSnapshot
                {
                    pawnId = pawnId,
                    factionId = faction.GetUniqueLoadID() ?? string.Empty,
                    previousTitle = beforeTitle,
                    newTitle = afterTitle,
                    causeToken = batch.causeToken,
                    tick = Math.Max(0, now),
                    correlationId = correlationId
                };
                RoyalPsychicMutationSnapshot psylink = new RoyalPsychicMutationSnapshot
                {
                    pawnId = pawnId,
                    previousPsylinkLevel = Math.Max(0, beforePsylink),
                    newPsylinkLevel = Math.Max(0, afterPsylink),
                    causeToken = batch.causeToken,
                    tick = Math.Max(0, now),
                    correlationId = correlationId
                };

                // Inheritance may run inside vanilla's instant bestowing/title award callback. Once
                // the exact committed succession owns that title edge, keep the ceremony's psylink
                // side effect but remove the title from the competing ritual/progression batch.
                if (title != null && TryClaimRoyalSuccessionTitle(title, Math.Max(0, now))) title = null;

                RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                bool ritualCause = batch.causeToken == RoyalMutationCauseTokens.ImperialBestowing
                    || batch.causeToken == RoyalMutationCauseTokens.AnimaLinking;
                bool allowRitualOwner = !ritualCause
                    || RitualFanoutSignal.RoyalMutationOwnerEnabled(batch.causeToken, policy);

                progression = FindDiary(pawn, true)?.EnsureProgressionState();
                if (progression != null)
                {
                    // Capture every Royalty-owned saved scalar/list before advancing it. If any later
                    // correlation bookkeeping throws, the finally block restores this checkpoint so
                    // the versioned scanner can still observe the live change truthfully.
                    observationCheckpoint = RoyaltyObservationCheckpoint.Capture(progression);
                    EnsureRoyaltyObservationReady(pawn, progression);
                    if (title != null && TitleChanged(beforeTitle, afterTitle))
                        AdvanceRoyalTitleObservation(progression, afterTitle, beforeTitle, Math.Max(0, now));
                    progression.highestPsylinkLevelRecorded = Math.Max(
                        progression.highestPsylinkLevelRecorded, afterPsylink);
                }

                bool mutationHandled = RoyalMutationCorrelation.Complete(
                    batch, title, psylink, policy.maximumPendingRoyalMutations, allowRitualOwner);
                correlationClosed = true;
                if (ritualCause && !allowRitualOwner)
                {
                    // The canonical ritual route was intentionally filtered. Consume any exact title
                    // memory too, otherwise its delayed ordinary-Thought fallback would leak the same
                    // disabled ceremony through a different group after the correlation window.
                    ClaimRoyalTitleThoughts(batch, Math.Max(0, now), policy);
                }
                bool neuroformer = batch.causeToken == RoyalMutationCauseTokens.Neuroformer;
                if (neuroformer || !mutationHandled)
                {
                    // Emit performs the one selection pass and quietly returns false when every
                    // possible route is disabled. This avoids deriving the same kind twice.
                    EmitRoyalMutationProgression(pawn, batch, policy);
                }
                return batch;
            }
            finally
            {
                // Harmony marks the patch scope completed before calling here so a finalizer cannot
                // retry after partial dispatch. If capture/bookkeeping throws before Complete consumes
                // the active row, restore the saved observation first and cancel the transient scope;
                // the versioned scanner then remains the truthful fallback.
                if (!correlationClosed)
                {
                    try { observationCheckpoint?.Restore(progression); }
                    finally { RoyalMutationCorrelation.Cancel(batch); }
                }
            }
        }

        /// <summary>Handles the defensively registered private post-title callback.</summary>
        internal void ObserveRoyalTitleHook(
            Pawn pawn,
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current)
        {
            if (!RoyaltyProgressionRuntimeReady() || pawn == null || !IsDiaryEligible(pawn)) return;
            PawnProgressionState progression = FindDiary(pawn, true)?.EnsureProgressionState();
            if (progression == null) return;
            EnsureRoyaltyObservationReadyForHook(pawn, progression, previous, current);
            int now = Find.TickManager?.TicksGame ?? 0;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            RoyalTitleSnapshot identity = current ?? previous;
            if (identity == null) return;
            RoyalTitleMutationSnapshot mutation = new RoyalTitleMutationSnapshot
            {
                pawnId = pawn.GetUniqueLoadID() ?? string.Empty,
                factionId = identity.factionId,
                previousTitle = previous,
                newTitle = current,
                causeToken = RoyalMutationCauseTokens.Unknown,
                tick = Math.Max(0, now)
            };
            bool successionOwner = TryClaimRoyalSuccessionTitle(mutation, Math.Max(0, now));
            bool richerOwner = RoyalMutationCorrelation.HasRicherTitleOwner(
                pawn.GetUniqueLoadID(), identity.factionId, now, policy.titleCorrelationTicks);
            string defName = RoyalTitleDefNameForTransition(
                RoyalTitleTransitionPolicy.Classify(previous, current, richerOwner, true, policy).transitionToken);
            bool outputEnabled = RoyalProgressionGroupEnabled(defName);
            RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                previous, current, richerOwner, outputEnabled, policy);

            // The callback is authoritative even when output is disabled or a ritual owns the page.
            AdvanceRoyalTitleObservation(progression, current, previous, now);
            if (successionOwner) return;
            if (!decision.shouldEmit) return;
            RoyalMutationBatchSnapshot batch = TitleBatch(
                pawn, previous, current, RoyalMutationCauseTokens.Unknown, now);
            if (EmitRoyalTitleTransition(pawn, batch, decision))
                ClaimRoyalTitleThoughts(batch, now, policy);
        }

        /// <summary>
        /// Reconciles exact per-faction titles, psylink truth, pending fallbacks, and title memories.
        /// This runs whether or not Progression output is enabled.
        /// </summary>
        private void ObserveRoyaltyProgression(
            Pawn pawn,
            PawnProgressionState progression,
            bool outputEnabled)
        {
            if (!ModsConfig.RoyaltyActive || pawn == null || progression == null) return;
            EnsureRoyaltyObservationReady(pawn, progression);
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            int now = Find.TickManager?.TicksGame ?? 0;
            RoyalMutationBatchSnapshot expired = RoyalMutationCorrelation.TakeExpiredFallback(
                pawn.GetUniqueLoadID(), now, outputEnabled, policy);
            if (expired != null) EmitRoyalMutationProgression(pawn, expired, policy);

            int currentPsylink = DlcContext.CurrentPsylinkLevel(pawn);
            int previousPsylink = progression.highestPsylinkLevelRecorded;
            if (currentPsylink > previousPsylink)
            {
                progression.highestPsylinkLevelRecorded = currentPsylink;
                if (outputEnabled)
                {
                    EmitRoyalMutationProgression(
                        pawn,
                        PsylinkBatch(pawn, previousPsylink, currentPsylink,
                            RoyalMutationCauseTokens.Unknown, now),
                        policy);
                }
            }

            RoyaltyPawnProgressionState royalty = progression.EnsureRoyaltyState();
            List<RoyalTitleSnapshot> currentTitles = DlcContext.CaptureRoyalTitles(pawn);
            List<RoyalTitleMutationSnapshot> changes = RoyalTitleObservationPolicy.Diff(
                pawn.GetUniqueLoadID(), TitleObservationSnapshots(royalty.titleObservations),
                currentTitles, now);

            // Advance the whole list before dispatch so a disabled/rejected page cannot replay later.
            SetRoyalTitleObservations(royalty, currentTitles, now);
            UpdateLegacyRoyalTitleBaseline(progression, currentTitles);
            for (int i = 0; i < changes.Count; i++)
            {
                RoyalTitleMutationSnapshot mutation = changes[i];
                if (TryClaimRoyalSuccessionTitle(mutation, Math.Max(0, now))) continue;
                RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                    mutation.previousTitle,
                    mutation.newTitle,
                    false,
                    outputEnabled && RoyalProgressionGroupEnabled(RoyalTitleDefNameForTransition(
                        RoyalTitleTransitionPolicy.Classify(
                            mutation.previousTitle, mutation.newTitle, false, true, policy).transitionToken)),
                    policy);
                if (!decision.shouldEmit) continue;
                RoyalMutationBatchSnapshot batch = TitleBatch(
                    pawn, mutation.previousTitle, mutation.newTitle,
                    RoyalMutationCauseTokens.Unknown, now);
                if (EmitRoyalTitleTransition(pawn, batch, decision))
                    ClaimRoyalTitleThoughts(batch, now, policy);
            }
        }

        /// <summary>Marks saved adapters unavailable without deleting any title or psylink truth.</summary>
        internal void MarkRoyaltyObservationUnavailable()
        {
            if (ModsConfig.RoyaltyActive || diaries == null) return;
            for (int i = 0; i < diaries.Count; i++)
            {
                RoyaltyPawnProgressionState royalty = diaries[i]?.progressionState?.royaltyObservationState;
                if (royalty != null && royalty.observationAvailable)
                    royalty.observationAvailable = false;
            }
        }

        /// <summary>
        /// Prunes expired mutation owners whose pawn is genuinely absent from the live eligible roster.
        /// Per-pawn observation and title-memory release run later in a deliberate two-phase order.
        /// </summary>
        private void MaintainRoyaltyTransientProgression()
        {
            if (!ModsConfig.RoyaltyActive) return;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            int now = Find.TickManager?.TicksGame ?? 0;
            if ((royaltyPendingSuccessions?.Count ?? 0) > 0) NormalizeRoyalSuccessionFacts();
            if (!RoyalMutationCorrelation.HasPending) return;
            List<Pawn> liveColonists = SnapshotLiveRoyaltyColonists();
            HashSet<string> livePawnIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < liveColonists.Count; i++)
                livePawnIds.Add(liveColonists[i].GetUniqueLoadID());
            RoyalMutationCorrelation.PruneExpiredMissingOwners(livePawnIds, now, policy);
        }

        /// <summary>
        /// Releases unmatched title memories only after mutation fallbacks and title observers have
        /// had their chance to claim the same exact action during this scanner pass.
        /// </summary>
        private void MaintainRoyalTitleThoughtsAfterRoyaltyObservation()
        {
            if (!ModsConfig.RoyaltyActive) return;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            RoyalTitleThoughtCorrelation.Maintain(
                Find.TickManager?.TicksGame ?? 0, policy.titleThoughtCorrelationTicks);
        }

        /// <summary>
        /// Reconciles live pawns with staged title memories before the transient queue is flushed into
        /// a save. A scanner-owned title change becomes its rich page now; a genuinely unmatched memory
        /// remains pending and is released unchanged by <see cref="RoyalTitleThoughtCorrelation"/>.
        /// </summary>
        private void ReconcileRoyaltyOwnersBeforeSave()
        {
            if (!ModsConfig.RoyaltyActive
                || (!RoyalMutationCorrelation.HasPending && !RoyalTitleThoughtCorrelation.HasPending)) return;
            bool progressionEnabled = PawnDiaryMod.Settings != null
                && DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression);
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            List<Pawn> liveColonists = SnapshotLiveRoyaltyColonists();
            for (int i = 0; i < liveColonists.Count; i++)
            {
                Pawn pawn = liveColonists[i];
                string pawnId = pawn.GetUniqueLoadID();
                bool pendingMutationOwner = RoyalMutationCorrelation.HasPendingForPawn(pawnId);
                bool pendingTitleMemory = RoyalTitleThoughtCorrelation.HasPendingForPawn(pawnId);
                if (!pendingMutationOwner && !pendingTitleMemory) continue;
                RoyalMutationBatchSnapshot pendingMutation;
                while ((pendingMutation = RoyalMutationCorrelation.TakePendingForSave(
                    pawnId)) != null)
                {
                    EmitRoyalMutationProgression(pawn, pendingMutation, policy);
                }
                if (!pendingTitleMemory) continue;
                PawnProgressionState progression = FindDiary(pawn, true)?.EnsureProgressionState();
                if (progression != null)
                    ObserveRoyaltyProgression(pawn, progression, progressionEnabled);
            }
        }

        /// <summary>
        /// Gives live caravan/travelling owners the same expiry reconciliation as map colonists before
        /// global title-memory release. Ordinary progression scanning remains map-scoped; this narrow
        /// pass runs only while one of the exact Royalty queues names the off-map pawn.
        /// </summary>
        private void ReconcileOffMapRoyaltyOwners(IList<Pawn> mapColonists, bool outputEnabled)
        {
            if (!ModsConfig.RoyaltyActive
                || (!RoyalMutationCorrelation.HasPending && !RoyalTitleThoughtCorrelation.HasPending)) return;
            HashSet<string> scannedPawnIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (mapColonists?.Count ?? 0); i++)
            {
                string pawnId = mapColonists[i]?.GetUniqueLoadID();
                if (!string.IsNullOrWhiteSpace(pawnId)) scannedPawnIds.Add(pawnId);
            }

            List<Pawn> liveColonists = SnapshotLiveRoyaltyColonists();
            for (int i = 0; i < liveColonists.Count; i++)
            {
                Pawn pawn = liveColonists[i];
                string pawnId = pawn.GetUniqueLoadID();
                if (scannedPawnIds.Contains(pawnId)
                    || (!RoyalMutationCorrelation.HasPendingForPawn(pawnId)
                        && !RoyalTitleThoughtCorrelation.HasPendingForPawn(pawnId))) continue;
                PawnProgressionState progression = FindDiary(pawn, true)?.EnsureProgressionState();
                if (progression != null)
                    ObserveRoyaltyProgression(pawn, progression, outputEnabled);
            }
        }

        /// <summary>
        /// Snapshots eligible colonists across maps, caravans, and travelling transporters. Map-only
        /// free-colonist lists are not a liveness test: a ritual target can leave in a caravan while
        /// its short mutation-ownership window is still open.
        /// </summary>
        private List<Pawn> SnapshotLiveRoyaltyColonists()
        {
            List<Pawn> result = new List<Pawn>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<Pawn> candidates = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (candidates == null) return result;
            foreach (Pawn pawn in candidates)
            {
                if (!IsDiaryEligible(pawn)) continue;
                string pawnId = pawn.GetUniqueLoadID();
                if (string.IsNullOrWhiteSpace(pawnId) || !seen.Add(pawnId)) continue;
                result.Add(pawn);
            }
            return result;
        }

        private void EnsureRoyaltyObservationReady(Pawn pawn, PawnProgressionState progression)
        {
            RoyaltyPawnProgressionState royalty = progression.EnsureRoyaltyState();
            if (royalty.observationVersion >= RoyaltyStatePersistence.CurrentObservationVersion
                && royalty.observationAvailable) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            List<RoyalTitleSnapshot> titles = DlcContext.CaptureRoyalTitles(pawn);
            royalty.Baseline(titles, DlcContext.CurrentPsylinkLevel(pawn), now, progression);
            UpdateLegacyRoyalTitleBaseline(progression, titles);
        }

        private void EnsureRoyaltyObservationReadyForHook(
            Pawn pawn,
            PawnProgressionState progression,
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current)
        {
            RoyaltyPawnProgressionState royalty = progression.EnsureRoyaltyState();
            if (royalty.observationVersion >= RoyaltyStatePersistence.CurrentObservationVersion
                && royalty.observationAvailable) return;
            List<RoyalTitleSnapshot> before = DlcContext.CaptureRoyalTitles(pawn);
            string factionId = (current ?? previous)?.factionId ?? string.Empty;
            for (int i = before.Count - 1; i >= 0; i--)
                if (string.Equals(before[i]?.factionId, factionId, StringComparison.Ordinal)) before.RemoveAt(i);
            if (previous != null) before.Add(previous);
            royalty.Baseline(before, DlcContext.CurrentPsylinkLevel(pawn),
                Find.TickManager?.TicksGame ?? 0, progression);
            UpdateLegacyRoyalTitleBaseline(progression, before);
        }

        private static List<RoyalTitleSnapshot> TitlesFromObservations(
            string pawnId,
            IList<RoyalTitleObservationState> observations)
        {
            List<RoyalTitleSnapshot> result = new List<RoyalTitleSnapshot>();
            for (int i = 0; i < (observations?.Count ?? 0); i++)
            {
                RoyalTitleObservationState row = observations[i];
                if (row == null) continue;
                result.Add(new RoyalTitleSnapshot
                {
                    pawnId = pawnId ?? string.Empty,
                    factionId = row.factionId ?? string.Empty,
                    factionName = row.factionName ?? string.Empty,
                    titleDefName = row.titleDefName ?? string.Empty,
                    titleLabel = row.titleLabel ?? string.Empty,
                    seniority = Math.Max(0, row.seniority)
                });
            }
            return result;
        }

        private static bool TitleChanged(RoyalTitleSnapshot previous, RoyalTitleSnapshot current)
        {
            string before = previous?.titleDefName ?? string.Empty;
            string after = current?.titleDefName ?? string.Empty;
            return !string.Equals(before, after, StringComparison.Ordinal);
        }

        private static void SetRoyalTitleObservations(
            RoyaltyPawnProgressionState royalty,
            IList<RoyalTitleSnapshot> titles,
            int tick)
        {
            List<RoyalTitleObservationSnapshot> rows = RoyaltyStatePersistence.BaselineTitles(titles, tick);
            royalty.titleObservations = new List<RoyalTitleObservationState>();
            for (int i = 0; i < rows.Count; i++)
                royalty.titleObservations.Add(RoyalTitleObservationState.FromSnapshot(rows[i]));
            royalty.observationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            royalty.observationAvailable = true;
        }

        private static void AdvanceRoyalTitleObservation(
            PawnProgressionState progression,
            RoyalTitleSnapshot current,
            RoyalTitleSnapshot previous,
            int tick)
        {
            RoyaltyPawnProgressionState royalty = progression.EnsureRoyaltyState();
            List<RoyalTitleObservationSnapshot> rows = RoyalTitleObservationPolicy.Advance(
                TitleObservationSnapshots(royalty.titleObservations), previous, current, tick);
            royalty.titleObservations = new List<RoyalTitleObservationState>();
            for (int i = 0; i < rows.Count; i++)
                royalty.titleObservations.Add(RoyalTitleObservationState.FromSnapshot(rows[i]));
            royalty.observationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            royalty.observationAvailable = true;
            UpdateLegacyRoyalTitleBaseline(progression, TitlesFromObservations(
                current?.pawnId ?? previous?.pawnId, royalty.titleObservations));
        }

        private static List<RoyalTitleObservationSnapshot> TitleObservationSnapshots(
            IList<RoyalTitleObservationState> source)
        {
            List<RoyalTitleObservationSnapshot> result = new List<RoyalTitleObservationSnapshot>();
            for (int i = 0; i < (source?.Count ?? 0); i++)
                if (source[i] != null) result.Add(source[i].ToSnapshot());
            return result;
        }

        private static void UpdateLegacyRoyalTitleBaseline(
            PawnProgressionState progression,
            IList<RoyalTitleSnapshot> titles)
        {
            RoyalTitleSnapshot mostSenior = null;
            for (int i = 0; i < (titles?.Count ?? 0); i++)
                if (titles[i] != null && (mostSenior == null || titles[i].seniority > mostSenior.seniority))
                    mostSenior = titles[i];
            progression.lastObservedRoyalTitleDefName = mostSenior?.titleDefName ?? string.Empty;
            progression.lastObservedRoyalTitleLabel = mostSenior?.titleLabel ?? string.Empty;
        }

        private bool EmitRoyalMutationProgression(
            Pawn pawn,
            RoyalMutationBatchSnapshot batch,
            RoyaltyPolicySnapshot policy = null)
        {
            if (pawn == null || batch == null) return false;
            RoyaltyPolicySnapshot effective = policy ?? DiaryRoyaltyPolicy.Snapshot();
            string selectedKind = RoyalMutationProgressionKind(batch, effective);
            RoyalTitleMutationSnapshot title = batch.titleMutation;
            if (selectedKind == RoyalMutationKindTokens.Title)
            {
                RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                    title.previousTitle, title.newTitle, false, true, effective);
                bool emitted = EmitRoyalTitleTransition(pawn, batch, decision);
                if (emitted) ClaimRoyalTitleThoughts(batch, Find.TickManager?.TicksGame ?? 0,
                    effective);
                return emitted;
            }
            if (selectedKind != RoyalMutationKindTokens.Psylink) return false;
            RoyalPsychicMutationSnapshot psylink = batch.psylinkMutation;
            string context = RoyalMutationContextFormatter.Format(
                batch, RoyalTitleTransitionTokens.Invalid,
                effective.maximumRoyaltyContextCharacters, effective.maximumDutyCategoryTokens,
                includeOptionalDuties: false);
            string label = "PawnDiary.Event.ProgressionPsylinkLabel"
                .Translate(psylink.newPsylinkLevel).Resolve();
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.PsylinkLevelDefName,
                "psylink",
                label,
                psylink.previousPsylinkLevel.ToString(),
                psylink.newPsylinkLevel.ToString(),
                context);
            string text = "PawnDiary.Event.ProgressionPsylinkText"
                .Translate(pawn.LabelShortCap, psylink.newPsylinkLevel).Resolve();
            bool emittedPsylink = DispatchProgression(
                pawn, data, label, text,
                majorArcCandidate: IsMajorArcPsylinkLevel(psylink.newPsylinkLevel),
                dedupKey: "royalty-psylink|" + pawn.GetUniqueLoadID() + "|" + batch.openedTick,
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks);
            if (emittedPsylink && title != null)
            {
                // A combined batch can legitimately select psylink when its title route is disabled.
                // The page still owns the whole exact action, so consume the title memory too.
                ClaimRoyalTitleThoughts(
                    batch, Find.TickManager?.TicksGame ?? 0, effective);
            }
            return emittedPsylink;
        }

        private bool EmitRoyalTitleTransition(
            Pawn pawn,
            RoyalMutationBatchSnapshot batch,
            RoyalTitleTransitionDecision decision)
        {
            if (pawn == null || batch?.titleMutation == null || decision == null
                || !RoyalTitleTransitionTokens.IsNarrative(decision.transitionToken)) return false;
            RoyalTitleSnapshot before = batch.titleMutation.previousTitle;
            RoyalTitleSnapshot after = batch.titleMutation.newTitle;
            RoyalTitleSnapshot identity = after ?? before;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            string previousLabel = TitleLabelForUi(before);
            string currentLabel = TitleLabelForUi(after);
            string factionName = string.IsNullOrWhiteSpace(identity?.factionName)
                ? "PawnDiary.Event.RoyalTitle.UnknownFaction".Translate().Resolve()
                : identity.factionName;
            string defName = RoyalTitleDefNameForTransition(decision.transitionToken);
            string context = RoyalMutationContextFormatter.Format(
                batch, decision.transitionToken, policy.maximumRoyaltyContextCharacters,
                policy.maximumDutyCategoryTokens, includeOptionalDuties: true);
            ProgressionEventData data = ProgressionData(
                pawn, defName, "royal_title", currentLabel,
                previousLabel, currentLabel, context);
            string labelTitle = decision.transitionToken == RoyalTitleTransitionTokens.Loss
                ? previousLabel
                : currentLabel;
            string label = RoyalTitleLabelForUi(decision.transitionToken, labelTitle, factionName);
            string text = RoyalTitleTextForUi(
                decision.transitionToken, pawn.LabelShortCap, previousLabel, currentLabel, factionName);
            return DispatchProgression(
                pawn, data, label, text,
                majorArcCandidate: decision.transitionToken == RoyalTitleTransitionTokens.Loss,
                dedupKey: RoyalTitleTransitionPolicy.BuildEventDedupKey(
                    before, after, decision.transitionToken, batch.openedTick),
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks,
                narrativeEvidence: RoyalTitleNarrativeEvidence(pawn, decision.transitionToken, identity));
        }

        private static List<NarrativeEvidence> RoyalTitleNarrativeEvidence(
            Pawn pawn,
            string transition,
            RoyalTitleSnapshot title)
        {
            if (pawn == null || title == null) return null;
            List<string> topics = new List<string> { "identity", "authority", "status" };
            if (title.dutyCategoryTokens != null && title.dutyCategoryTokens.Count > 0) topics.Add("duty");
            return new List<NarrativeEvidence>
            {
                new NarrativeEvidence
                {
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = transition,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = pawn.GetUniqueLoadID(),
                    subjectLabel = pawn.LabelShortCap,
                    beliefTopics = topics,
                    salience = transition == RoyalTitleTransitionTokens.Loss
                        ? NarrativeSalienceTokens.Major
                        : NarrativeSalienceTokens.Meaningful,
                    pawnCanKnow = true,
                    sourceDomain = "royalty_title",
                    sourceDefName = RoyalTitleDefNameForTransition(transition)
                }
            };
        }

        private static RoyalMutationBatchSnapshot TitleBatch(
            Pawn pawn,
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current,
            string cause,
            int tick)
        {
            RoyalTitleSnapshot identity = current ?? previous;
            return new RoyalMutationBatchSnapshot
            {
                batchId = "title|" + (pawn?.GetUniqueLoadID() ?? string.Empty) + "|"
                    + (identity?.factionId ?? string.Empty) + "|" + tick,
                pawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                pawnName = DiaryLineCleaner.CleanLine(pawn?.LabelShortCap),
                causeToken = cause,
                openedTick = tick,
                titleMutation = new RoyalTitleMutationSnapshot
                {
                    pawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                    factionId = identity?.factionId ?? string.Empty,
                    previousTitle = previous,
                    newTitle = current,
                    causeToken = cause,
                    tick = tick
                }
            };
        }

        private static RoyalMutationBatchSnapshot PsylinkBatch(
            Pawn pawn,
            int previous,
            int current,
            string cause,
            int tick)
        {
            return new RoyalMutationBatchSnapshot
            {
                batchId = "psylink|" + (pawn?.GetUniqueLoadID() ?? string.Empty) + "|" + tick,
                pawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                pawnName = DiaryLineCleaner.CleanLine(pawn?.LabelShortCap),
                causeToken = cause,
                openedTick = tick,
                psylinkMutation = new RoyalPsychicMutationSnapshot
                {
                    pawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                    previousPsylinkLevel = previous,
                    newPsylinkLevel = current,
                    causeToken = cause,
                    tick = tick
                }
            };
        }

        private static void ClaimRoyalTitleThoughts(
            RoyalMutationBatchSnapshot batch,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            RoyalTitleMutationSnapshot title = batch?.titleMutation;
            if (title == null) return;
            RoyalTitleThoughtCorrelation.Claim(
                batch.pawnId,
                title.previousTitle?.titleDefName,
                title.newTitle?.titleDefName,
                tick,
                policy.titleThoughtCorrelationTicks);
        }

        internal static string RoyalTitleDefNameForTransition(string transition)
        {
            if (transition == RoyalTitleTransitionTokens.FirstTitle) return ProgressionEventData.RoyalTitleGainedDefName;
            if (transition == RoyalTitleTransitionTokens.Promotion) return ProgressionEventData.RoyalTitlePromotedDefName;
            if (transition == RoyalTitleTransitionTokens.Demotion) return ProgressionEventData.RoyalTitleDemotedDefName;
            if (transition == RoyalTitleTransitionTokens.Loss) return ProgressionEventData.RoyalTitleLostDefName;
            return ProgressionEventData.RoyalTitleChangedDefName;
        }

        private static string TitleLabelForUi(RoyalTitleSnapshot title)
        {
            return title == null || string.IsNullOrWhiteSpace(title.titleLabel)
                ? "PawnDiary.Event.RoyalTitle.None".Translate().Resolve()
                : title.titleLabel;
        }

        private static string RoyalTitleLabelForUi(string transition, string title, string faction)
        {
            string key = transition == RoyalTitleTransitionTokens.FirstTitle
                ? "PawnDiary.Event.RoyalTitle.Gained.Label"
                : transition == RoyalTitleTransitionTokens.Promotion
                    ? "PawnDiary.Event.RoyalTitle.Promoted.Label"
                    : transition == RoyalTitleTransitionTokens.Demotion
                        ? "PawnDiary.Event.RoyalTitle.Demoted.Label"
                        : "PawnDiary.Event.RoyalTitle.Lost.Label";
            return key.Translate(title, faction).Resolve();
        }

        private static string RoyalTitleTextForUi(
            string transition,
            string pawn,
            string previous,
            string current,
            string faction)
        {
            string key = transition == RoyalTitleTransitionTokens.FirstTitle
                ? "PawnDiary.Event.RoyalTitle.Gained.Text"
                : transition == RoyalTitleTransitionTokens.Promotion
                    ? "PawnDiary.Event.RoyalTitle.Promoted.Text"
                    : transition == RoyalTitleTransitionTokens.Demotion
                        ? "PawnDiary.Event.RoyalTitle.Demoted.Text"
                        : "PawnDiary.Event.RoyalTitle.Lost.Text";
            return key.Translate(pawn, previous, current, faction).Resolve();
        }

        private static bool RoyalProgressionGroupEnabled(string defName)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyProgression(defName);
            return group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName)
                && DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression);
        }

        private static string RoyalMutationProgressionKind(
            RoyalMutationBatchSnapshot batch,
            RoyaltyPolicySnapshot policy)
        {
            if (batch == null) return string.Empty;
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled) return string.Empty;
            RoyalTitleMutationSnapshot title = batch.titleMutation;
            bool titleChanged = title != null && TitleChanged(title.previousTitle, title.newTitle);
            bool titleEnabled = false;
            if (titleChanged)
            {
                RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                    title.previousTitle, title.newTitle, false, true, effective);
                titleEnabled = RoyalTitleTransitionTokens.IsNarrative(decision.transitionToken)
                    && RoyalProgressionGroupEnabled(
                        RoyalTitleDefNameForTransition(decision.transitionToken));
            }
            RoyalPsychicMutationSnapshot psylink = batch.psylinkMutation;
            bool psylinkChanged = psylink != null
                && psylink.previousPsylinkLevel != psylink.newPsylinkLevel;
            bool psylinkEnabled = psylinkChanged
                && RoyalProgressionGroupEnabled(ProgressionEventData.PsylinkLevelDefName);
            return RoyalMutationPageSelectionPolicy.Select(
                effective.enabled, titleChanged, titleEnabled, psylinkChanged, psylinkEnabled);
        }

        /// <summary>
        /// Detached rollback copy of the saved Royalty fields touched while a mutation boundary closes.
        /// This is intentionally component-side: it snapshots persistence models, never live DLC data.
        /// </summary>
        private sealed class RoyaltyObservationCheckpoint
        {
            private int highestPsylinkLevelRecorded;
            private string lastObservedRoyalTitleDefName;
            private string lastObservedRoyalTitleLabel;
            private bool hadRoyaltyState;
            private RoyaltyPawnProgressionState originalRoyaltyState;
            private int observationVersion;
            private bool observationAvailable;
            private List<RoyalTitleObservationState> titleObservations;

            /// <summary>Copies the current Royalty-owned progression fields without normalizing them.</summary>
            public static RoyaltyObservationCheckpoint Capture(PawnProgressionState progression)
            {
                if (progression == null) return null;
                RoyaltyPawnProgressionState royalty = progression.royaltyObservationState;
                return new RoyaltyObservationCheckpoint
                {
                    highestPsylinkLevelRecorded = progression.highestPsylinkLevelRecorded,
                    lastObservedRoyalTitleDefName = progression.lastObservedRoyalTitleDefName,
                    lastObservedRoyalTitleLabel = progression.lastObservedRoyalTitleLabel,
                    hadRoyaltyState = royalty != null,
                    originalRoyaltyState = royalty,
                    observationVersion = royalty?.observationVersion ?? 0,
                    observationAvailable = royalty?.observationAvailable ?? false,
                    titleObservations = CloneTitleObservations(royalty?.titleObservations)
                };
            }

            /// <summary>Restores the exact fields copied before mutation bookkeeping began.</summary>
            public void Restore(PawnProgressionState progression)
            {
                if (progression == null) return;
                progression.highestPsylinkLevelRecorded = highestPsylinkLevelRecorded;
                progression.lastObservedRoyalTitleDefName = lastObservedRoyalTitleDefName;
                progression.lastObservedRoyalTitleLabel = lastObservedRoyalTitleLabel;
                if (!hadRoyaltyState)
                {
                    progression.royaltyObservationState = null;
                    return;
                }

                originalRoyaltyState.observationVersion = observationVersion;
                originalRoyaltyState.observationAvailable = observationAvailable;
                originalRoyaltyState.titleObservations = CloneTitleObservations(titleObservations);
                progression.royaltyObservationState = originalRoyaltyState;
            }

            private static List<RoyalTitleObservationState> CloneTitleObservations(
                IList<RoyalTitleObservationState> source)
            {
                if (source == null) return null;
                List<RoyalTitleObservationState> copy = new List<RoyalTitleObservationState>();
                for (int i = 0; i < source.Count; i++)
                {
                    RoyalTitleObservationState row = source[i];
                    copy.Add(row == null ? null : new RoyalTitleObservationState
                    {
                        factionId = row.factionId,
                        factionName = row.factionName,
                        titleDefName = row.titleDefName,
                        titleLabel = row.titleLabel,
                        seniority = row.seniority,
                        lastObservedTick = row.lastObservedTick
                    });
                }
                return copy;
            }
        }

        private static bool RoyaltyProgressionRuntimeReady()
        {
            return ModsConfig.RoyaltyActive && GamePlaying && Scribe.mode == LoadSaveMode.Inactive;
        }
    }
}
