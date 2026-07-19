// Royalty Phase-5 succession and explicit heir-appointment orchestration. Harmony adapters provide
// exact live observations; this component commits detached facts, advances truth before optional
// dispatch, and gives the canonical heir page ownership of matching title/bestowing side effects.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Opens the exact outer death boundary in which vanilla may evaluate inheritance.</summary>
        internal RoyalSuccessionDeathScope BeginRoyalSuccessionDeath(Pawn deceased)
        {
            if (!RoyaltyProgressionRuntimeReady() || deceased == null) return null;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            return RoyalSuccessionCorrelation.Open(
                deceased.GetUniqueLoadID(), Find.TickManager?.TicksGame ?? 0,
                policy.maximumPendingSuccessions);
        }

        /// <summary>Adds candidate evidence; it cannot authorize a page until the outer commit arrives.</summary>
        internal void ObserveRoyalSuccessionCandidate(RoyalSuccessionCandidateSnapshot candidate)
        {
            if (!RoyaltyProgressionRuntimeReady() || candidate == null) return;
            RoyalSuccessionCorrelation.AddCandidate(
                candidate, DiaryRoyaltyPolicy.Snapshot().maximumPendingSuccessions);
        }

        /// <summary>
        /// Closes one death boundary, persists only exact committed edges, emits one heir-POV page per
        /// edge, and releases title callbacks unchanged when no commit proved succession ownership.
        /// </summary>
        internal void CompleteRoyalSuccessionDeath(Pawn deceased, RoyalSuccessionDeathScope scope)
        {
            RoyalSuccessionDeathScope closed = RoyalSuccessionCorrelation.Close(scope);
            if (closed == null) return;
            int now = closed.openedTick;
            HashSet<RoyalSuccessionStagedTitleMutation> claimed =
                new HashSet<RoyalSuccessionStagedTitleMutation>();
            try
            {
                if (deceased == null || !ModsConfig.RoyaltyActive) return;
                now = Find.TickManager?.TicksGame ?? closed.openedTick;
                // Resolve mutable XML policy only after entering the release guard. A malformed
                // compatibility Def must not strand title callbacks after the scope was closed.
                RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                for (int i = 0; i < closed.candidates.Count; i++)
                {
                    RoyalSuccessionCandidateSnapshot candidate = closed.candidates[i];
                    // One malformed/modded edge must not prevent the remaining candidates or the
                    // staged-release finally block from completing.
                    DiaryPatchSafety.Run("Royalty.Succession.Candidate", () =>
                        CompleteRoyalSuccessionCandidate(
                            deceased, candidate, closed.stagedTitleMutations,
                            claimed, now, policy));
                }
            }
            finally
            {
                // Closing the scope transfers responsibility here. Always release every unclaimed
                // callback, even if capture, persistence, translation, or dispatch failed above.
                for (int i = 0; i < closed.stagedTitleMutations.Count; i++)
                {
                    RoyalSuccessionStagedTitleMutation staged = closed.stagedTitleMutations[i];
                    if (staged == null || claimed.Contains(staged)) continue;
                    RoyalTitleMutationSnapshot mutation = staged.mutation;
                    DiaryPatchSafety.Run("Royalty.Succession.Release", () =>
                        ReleaseOrdinaryRoyalTitleMutation(mutation, now));
                }
            }
        }

        private void CompleteRoyalSuccessionCandidate(
            Pawn deceased,
            RoyalSuccessionCandidateSnapshot candidate,
            IList<RoyalSuccessionStagedTitleMutation> stagedMutations,
            ISet<RoyalSuccessionStagedTitleMutation> claimed,
            int now,
            RoyaltyPolicySnapshot policy)
        {
            RoyalSuccessionCommitObservation observation =
                DlcContext.CaptureSuccessionCommit(deceased, candidate, now);
            RoyalSuccessionFact fact = RoyalSuccessionPolicy.Commit(candidate, observation);
            if (fact == null) return;

            List<RoyalSuccessionStagedTitleMutation> owned =
                new List<RoyalSuccessionStagedTitleMutation>();
            for (int i = 0; i < (stagedMutations?.Count ?? 0); i++)
            {
                RoyalSuccessionStagedTitleMutation staged = stagedMutations[i];
                if (!string.Equals(staged?.correlationId, fact.correlationId,
                        StringComparison.Ordinal)) continue;
                RoyalSuccessionFact advanced = RoyalSuccessionPolicy.AdvanceMutation(
                    fact, staged?.mutation, now);
                if (advanced == null) continue;
                fact = advanced;
                owned.Add(staged);
            }

            // Attempt the canonical page exactly once even if the group is disabled or dispatch
            // rejects it. A terminal target needs no saved proof after its exact edge is owned.
            fact.pageClaimed = true;
            if (!fact.titleMutationClaimed) AddRoyalSuccessionFact(fact, now, policy);

            for (int i = 0; i < owned.Count; i++)
            {
                RoyalSuccessionStagedTitleMutation staged = owned[i];
                claimed.Add(staged);
                RememberRoyalSuccessionTitleClaim(staged.mutation, now, policy);
                RoyalTitleMutationSnapshot mutation = staged.mutation;
                DiaryPatchSafety.Run("Royalty.Succession.TitleThought", () =>
                    ClaimSuccessionTitleThought(mutation, now, policy));
            }

            Pawn heir = FindLiveRoyaltyPawn(fact.heirPawnId);
            if (heir != null && IsDiaryEligible(heir)) EmitRoyalSuccession(heir, fact, policy);
        }

        /// <summary>Cancels an interrupted death scope; vanilla exceptions retain their own behavior.</summary>
        internal void CancelRoyalSuccessionDeath(RoyalSuccessionDeathScope scope)
        {
            RoyalSuccessionCorrelation.Cancel(scope);
            int now = Find.TickManager?.TicksGame ?? scope?.openedTick ?? 0;
            for (int i = 0; i < (scope?.stagedTitleMutations?.Count ?? 0); i++)
            {
                RoyalTitleMutationSnapshot mutation = scope.stagedTitleMutations[i]?.mutation;
                DiaryPatchSafety.Run("Royalty.Succession.CancelRelease", () =>
                    ReleaseOrdinaryRoyalTitleMutation(mutation, now));
            }
        }

        /// <summary>
        /// Gives an active or persisted committed succession first refusal over an exact title callback.
        /// The caller still advances the saved title observation before returning.
        /// </summary>
        private bool TryClaimRoyalSuccessionTitle(RoyalTitleMutationSnapshot mutation, int now)
        {
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            if (RoyalSuccessionCorrelation.StageTitle(mutation, policy.maximumPendingSuccessions)) return true;
            if (RoyalSuccessionCorrelation.WasClaimedRecently(mutation, now)) return true;
            for (int i = (royaltyPendingSuccessions?.Count ?? 0) - 1; i >= 0; i--)
            {
                RoyalSuccessionState state = royaltyPendingSuccessions[i];
                RoyalSuccessionFact fact = state?.ToSnapshot();
                if (fact == null) continue;
                RoyalSuccessionMutationDisposition disposition = RoyalSuccessionPolicy.ClassifyMutation(
                    fact, mutation, fact.correlationId, now);
                if (disposition == RoyalSuccessionMutationDisposition.Unrelated) continue;
                if (disposition == RoyalSuccessionMutationDisposition.Invalidate)
                {
                    royaltyPendingSuccessions.RemoveAt(i);
                    continue;
                }

                RoyalSuccessionFact advanced = RoyalSuccessionPolicy.AdvanceMutation(fact, mutation, now);
                if (advanced == null) continue;
                if (advanced.titleMutationClaimed) royaltyPendingSuccessions.RemoveAt(i);
                else royaltyPendingSuccessions[i] = RoyalSuccessionState.FromSnapshot(advanced);
                RememberRoyalSuccessionTitleClaim(mutation, now, policy);
                ClaimSuccessionTitleThought(mutation, now, policy);
                return true;
            }
            return false;
        }

        /// <summary>Emits only a proven ChangeRoyalHeir quest appointment; automatic SetHeir stays silent.</summary>
        internal bool ObserveRoyalHeirAppointment(Pawn heir, RoyalHeirAppointmentSnapshot appointment)
        {
            if (!RoyaltyProgressionRuntimeReady() || heir == null || !IsDiaryEligible(heir)
                || !RoyalSuccessionPolicy.ValidAppointment(appointment)) return false;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            string context = RoyalSuccessionContextFormatter.FormatAppointment(
                appointment, policy.maximumRoyaltyContextCharacters);
            ProgressionEventData data = ProgressionData(
                heir, ProgressionEventData.RoyalHeirAppointedDefName, "royal_heir",
                appointment.titleLabel, appointment.previousHeirPawnName,
                appointment.heirPawnName, context);
            string label = "PawnDiary.Event.RoyalHeirAppointed.Label"
                .Translate(appointment.titleLabel, appointment.factionName).Resolve();
            string text = "PawnDiary.Event.RoyalHeirAppointed.Text"
                .Translate(appointment.heirPawnName, appointment.titleHolderPawnName,
                    appointment.titleLabel, appointment.factionName).Resolve();
            return DispatchProgression(
                heir, data, label, text, majorArcCandidate: false,
                dedupKey: "royal-heir-appointment|" + appointment.titleHolderPawnId + "|"
                    + appointment.heirPawnId + "|" + appointment.factionId + "|"
                    + appointment.titleDefName + "|" + appointment.observedTick,
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks,
                narrativeEvidence: RoyalSuccessionEvidence(
                    heir, "heir_appointed", ProgressionEventData.RoyalHeirAppointedDefName, false));
        }

        private bool EmitRoyalSuccession(
            Pawn heir,
            RoyalSuccessionFact fact,
            RoyaltyPolicySnapshot policy)
        {
            string context = RoyalSuccessionContextFormatter.Format(
                fact, policy.maximumRoyaltyContextCharacters);
            ProgressionEventData data = ProgressionData(
                heir, ProgressionEventData.RoyalSuccessionDefName, "royal_succession",
                fact.inheritedTitleLabel, fact.previousHeirTitleLabel,
                fact.inheritedTitleLabel, context);
            string label = "PawnDiary.Event.RoyalSuccession.Label"
                .Translate(fact.inheritedTitleLabel, fact.factionName).Resolve();
            string text = "PawnDiary.Event.RoyalSuccession.Text"
                .Translate(fact.heirPawnName, fact.deceasedPawnName,
                    fact.inheritedTitleLabel, fact.factionName).Resolve();
            return DispatchProgression(
                heir, data, label, text, majorArcCandidate: true,
                dedupKey: "royal-succession|" + RoyalSuccessionPolicy.EdgeKey(fact),
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks,
                narrativeEvidence: RoyalSuccessionEvidence(
                    heir, "succession", ProgressionEventData.RoyalSuccessionDefName, true));
        }

        private static List<NarrativeEvidence> RoyalSuccessionEvidence(
            Pawn heir,
            string phase,
            string sourceDefName,
            bool includesDeath)
        {
            List<string> topics = new List<string> { "authority", "status", "duty" };
            if (includesDeath) topics.Add("death");
            return new List<NarrativeEvidence>
            {
                new NarrativeEvidence
                {
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = phase,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = heir.GetUniqueLoadID(),
                    subjectLabel = heir.LabelShortCap,
                    beliefTopics = topics,
                    salience = includesDeath ? NarrativeSalienceTokens.Major : NarrativeSalienceTokens.Meaningful,
                    pawnCanKnow = true,
                    sourceDomain = "royalty_succession",
                    sourceDefName = sourceDefName
                }
            };
        }

        private void ReleaseOrdinaryRoyalTitleMutation(RoyalTitleMutationSnapshot mutation, int now)
        {
            if (mutation == null) return;
            Pawn pawn = FindLiveRoyaltyPawn(mutation.pawnId);
            if (pawn == null || !IsDiaryEligible(pawn)) return;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool richerOwner = RoyalMutationCorrelation.HasRicherTitleOwner(
                mutation.pawnId, mutation.factionId, now, policy.titleCorrelationTicks);
            RoyalTitleTransitionDecision route = RoyalTitleTransitionPolicy.Classify(
                mutation.previousTitle, mutation.newTitle, richerOwner, true, policy);
            bool outputEnabled = RoyalProgressionGroupEnabled(
                RoyalTitleDefNameForTransition(route.transitionToken));
            RoyalTitleTransitionDecision decision = RoyalTitleTransitionPolicy.Classify(
                mutation.previousTitle, mutation.newTitle, richerOwner, outputEnabled, policy);
            if (!decision.shouldEmit) return;
            RoyalMutationBatchSnapshot batch = TitleBatch(
                pawn, mutation.previousTitle, mutation.newTitle,
                RoyalMutationCauseTokens.Unknown, now);
            if (EmitRoyalTitleTransition(pawn, batch, decision)) ClaimRoyalTitleThoughts(batch, now, policy);
        }

        private Pawn FindLiveRoyaltyPawn(string pawnId)
        {
            List<Pawn> pawns = SnapshotLiveRoyaltyColonists();
            for (int i = 0; i < pawns.Count; i++)
                if (string.Equals(pawns[i]?.GetUniqueLoadID(), pawnId, StringComparison.Ordinal)) return pawns[i];
            return null;
        }

        private void AddRoyalSuccessionFact(
            RoyalSuccessionFact fact,
            int now,
            RoyaltyPolicySnapshot policy)
        {
            List<RoyalSuccessionFact> rows = RoyalSuccessionSnapshots();
            rows.Add(fact);
            SetRoyalSuccessionSnapshots(RoyalSuccessionPolicy.Normalize(
                rows, now, policy.maximumPendingSuccessions));
        }

        private List<RoyalSuccessionFact> RoyalSuccessionSnapshots()
        {
            List<RoyalSuccessionFact> rows = new List<RoyalSuccessionFact>();
            for (int i = 0; i < (royaltyPendingSuccessions?.Count ?? 0); i++)
                if (royaltyPendingSuccessions[i] != null) rows.Add(royaltyPendingSuccessions[i].ToSnapshot());
            return rows;
        }

        private void SetRoyalSuccessionSnapshots(IList<RoyalSuccessionFact> rows)
        {
            royaltyPendingSuccessions = new List<RoyalSuccessionState>();
            for (int i = 0; i < (rows?.Count ?? 0); i++)
                royaltyPendingSuccessions.Add(RoyalSuccessionState.FromSnapshot(rows[i]));
        }

        private static void ClaimSuccessionTitleThought(
            RoyalTitleMutationSnapshot mutation,
            int now,
            RoyaltyPolicySnapshot policy)
        {
            if (mutation == null) return;
            RoyalTitleThoughtCorrelation.Claim(
                mutation.pawnId, mutation.previousTitle?.titleDefName,
                mutation.newTitle?.titleDefName, now, policy.titleThoughtCorrelationTicks);
        }

        private static void RememberRoyalSuccessionTitleClaim(
            RoyalTitleMutationSnapshot mutation,
            int now,
            RoyaltyPolicySnapshot policy)
        {
            RoyalSuccessionCorrelation.RememberClaim(
                mutation, now, policy.successionCorrelationTicks,
                policy.maximumPendingSuccessions);
        }
    }
}
