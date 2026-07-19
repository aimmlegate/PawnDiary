// Royalty Phase-4 title/psylink orchestration. Guarded adapters supply detached snapshots; this
// component advances saved truth before optional dispatch, arbitrates exact cause owners, and keeps
// the slow per-faction scanner as the missing-hook/modded fallback.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
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

            PawnProgressionState progression = FindDiary(pawn, true)?.EnsureProgressionState();
            if (progression != null)
            {
                EnsureRoyaltyObservationReady(pawn, progression);
                if (title != null && TitleChanged(beforeTitle, afterTitle))
                    AdvanceRoyalTitleObservation(progression, afterTitle, beforeTitle, Math.Max(0, now));
                progression.highestPsylinkLevelRecorded = Math.Max(
                    progression.highestPsylinkLevelRecorded, afterPsylink);
            }

            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool staged = RoyalMutationCorrelation.Complete(
                batch, title, psylink, policy.maximumPendingRoyalMutations);
            bool neuroformer = batch.causeToken == RoyalMutationCauseTokens.Neuroformer;
            if (neuroformer || !staged)
            {
                if (RoyalMutationProgressionEnabled(batch))
                    EmitRoyalMutationProgression(pawn, batch, fallbackOwner: !neuroformer);
            }
            return batch;
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
            bool richerOwner = RoyalMutationCorrelation.HasRicherTitleOwner(
                pawn.GetUniqueLoadID(), identity.factionId, now, policy.titleCorrelationTicks);
            string defName = RoyalTitleDefNameForTransition(
                RoyalTitleTransitionPolicy.Classify(previous, current, richerOwner, true, policy).transitionToken);
            bool outputEnabled = RoyalProgressionGroupEnabled(defName);
            RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                previous, current, richerOwner, outputEnabled, policy);

            // The callback is authoritative even when output is disabled or a ritual owns the page.
            AdvanceRoyalTitleObservation(progression, current, previous, now);
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
            RoyalTitleThoughtCorrelation.Maintain(now, policy.titleThoughtCorrelationTicks);

            RoyalMutationBatchSnapshot expired = RoyalMutationCorrelation.TakeExpiredFallback(
                pawn.GetUniqueLoadID(), now, outputEnabled, policy);
            if (expired != null) EmitRoyalMutationProgression(pawn, expired, fallbackOwner: true);

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
                        fallbackOwner: true);
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
        private void MarkRoyaltyObservationUnavailable()
        {
            if (ModsConfig.RoyaltyActive || diaries == null) return;
            for (int i = 0; i < diaries.Count; i++)
            {
                RoyaltyPawnProgressionState royalty = diaries[i]?.progressionState?.royaltyObservationState;
                if (royalty != null) royalty.observationAvailable = false;
            }
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
            bool fallbackOwner)
        {
            if (pawn == null || batch == null) return false;
            RoyalTitleMutationSnapshot title = batch.titleMutation;
            if (title != null && TitleChanged(title.previousTitle, title.newTitle))
            {
                RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                    title.previousTitle, title.newTitle, false, true, DiaryRoyaltyPolicy.Snapshot());
                bool emitted = EmitRoyalTitleTransition(pawn, batch, decision);
                if (emitted) ClaimRoyalTitleThoughts(batch, Find.TickManager?.TicksGame ?? 0,
                    DiaryRoyaltyPolicy.Snapshot());
                return emitted;
            }
            RoyalPsychicMutationSnapshot psylink = batch.psylinkMutation;
            if (psylink == null || psylink.previousPsylinkLevel == psylink.newPsylinkLevel) return false;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            string context = RoyalMutationContextFormatter.Format(
                batch, RoyalTitleTransitionTokens.Invalid,
                policy.maximumRoyaltyContextCharacters, policy.maximumDutyCategoryTokens,
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
            return DispatchProgression(
                pawn, data, label, text,
                majorArcCandidate: IsMajorArcPsylinkLevel(psylink.newPsylinkLevel),
                dedupKey: "royalty-psylink|" + pawn.GetUniqueLoadID() + "|" + batch.openedTick,
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks);
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
            string label = RoyalTitleLabelForUi(decision.transitionToken, currentLabel, factionName);
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

        private static bool RoyalMutationProgressionEnabled(RoyalMutationBatchSnapshot batch)
        {
            if (batch?.titleMutation != null)
            {
                RoyalTitleMutationSnapshot title = batch.titleMutation;
                RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                    title.previousTitle, title.newTitle, false, true, DiaryRoyaltyPolicy.Snapshot());
                return RoyalProgressionGroupEnabled(
                    RoyalTitleDefNameForTransition(decision.transitionToken));
            }
            return batch?.psylinkMutation != null
                && RoyalProgressionGroupEnabled(ProgressionEventData.PsylinkLevelDefName);
        }

        private static bool RoyaltyProgressionRuntimeReady()
        {
            return ModsConfig.RoyaltyActive && GamePlaying && Scribe.mode == LoadSaveMode.Inactive;
        }
    }
}
