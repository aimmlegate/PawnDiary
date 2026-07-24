// Phase 3 passive Ideology observation. The elapsed scanner spreads minimal identity/certainty reads
// across colonists, updates saved state, and deliberately never submits a DiarySignal or creates a page.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private int nextBeliefScanTick;
        private int beliefScanCursor;

        /// <summary>
        /// Advances a bounded number of eligible colonists from a rotating cursor. Missing trackers are
        /// still processed because they must clear pending debt and request a new baseline.
        /// </summary>
        private int ScanPawnBeliefs(int nowTick)
        {
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            List<Pawn> pawns = SnapshotFreeColonists();
            if (pawns == null || pawns.Count == 0)
            {
                beliefScanCursor = 0;
                return 0;
            }

            if (beliefScanCursor < 0 || beliefScanCursor >= pawns.Count) beliefScanCursor = 0;
            int inspected = 0;
            int processed = 0;
            while (inspected < pawns.Count && processed < policy.maximumBeliefPawnsPerScan)
            {
                Pawn pawn = pawns[beliefScanCursor];
                beliefScanCursor = (beliefScanCursor + 1) % pawns.Count;
                inspected++;
                if (!IsDiaryEligible(pawn)) continue;

                PawnDiaryRecord diary = FindDiary(pawn, true);
                if (diary == null) continue;
                ObservePawnBelief(pawn, diary, Math.Max(0, nowTick), policy);
                processed++;
            }
            return processed;
        }

        /// <summary>Runs the pure reducer for one pawn and copies its detached result into saved state.</summary>
        private static BeliefObservationDecision ObservePawnBelief(
            Pawn pawn,
            PawnDiaryRecord diary,
            int nowTick,
            BeliefPolicySnapshot policy)
        {
            PawnBeliefState saved = diary?.EnsureBeliefState();
            if (saved == null) return new BeliefObservationDecision();
            saved.Normalize(nowTick, policy);
            BeliefTrackerObservation observation;
            bool available = DlcContext.TryCaptureBeliefTrackerObservation(pawn, out observation);
            BeliefObservationTransition transition = BeliefReflectionPolicy.Advance(
                saved.ToScanState(), observation, available, nowTick, policy);
            saved.Apply(transition.state);
            return transition.decision;
        }

        /// <summary>
        /// Synchronously clears stale pending evidence on load when Ideology, the pawn, or its tracker is
        /// absent. Valid pending evidence survives the save/load boundary untouched.
        /// </summary>
        private void NormalizeBeliefStatesForLoadedSave()
        {
            // Loaded games normally always have a TickManager. If an unusual fixture calls this
            // earlier, preserve pending evidence until a real tick exists instead of repairing it
            // against a made-up zero timestamp.
            if (Find.TickManager == null) return;

            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            Dictionary<string, Pawn> livePawns = SnapshotLivePawnsByLoadId();
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                PawnBeliefState state = diary?.EnsureBeliefState();
                if (state == null) continue;
                state.Normalize(now, policy);

                Pawn pawn;
                BeliefTrackerObservation ignored;
                bool available = policy.enabled && livePawns.TryGetValue(diary.pawnId ?? string.Empty, out pawn)
                    && DlcContext.TryCaptureBeliefTrackerObservation(pawn, out ignored);
                if (available) continue;
                BeliefObservationTransition reset = BeliefReflectionPolicy.Advance(
                    state.ToScanState(), null, false, now, policy);
                state.Apply(reset.state);
            }
        }

        /// <summary>Test seam for one exact loaded pawn; it performs no scheduling or event submission.</summary>
        internal BeliefObservationDecision ObservePawnBeliefForTests(Pawn pawn, int nowTick)
        {
            if (pawn == null) return new BeliefObservationDecision();
            PawnDiaryRecord diary = FindDiary(pawn, true);
            return ObservePawnBelief(pawn, diary, nowTick, DiaryBeliefPolicy.Snapshot());
        }

        /// <summary>Test seam proving one scheduled pass obeys the XML work cap.</summary>
        internal int ScanPawnBeliefsForTests(int nowTick)
        {
            return ScanPawnBeliefs(nowTick);
        }

        /// <summary>Refreshes the shared per-tick pawn snapshot before a loaded scanner fixture.</summary>
        internal void ResetBeliefScannerForTests()
        {
            beliefScanCursor = 0;
            ResetFreeColonistSnapshot();
        }

        /// <summary>
        /// Returns dev-safe mechanical tokens only: no ideology descriptions, authored doctrine text,
        /// pawn names, or prompt prose are logged by the diagnostics action.
        /// </summary>
        internal string BeliefStateDiagnosticsForDev(Pawn pawn)
        {
            // FindDiary applies lazy defaults to every nested saved state. Diagnostics instead use the
            // raw index lookup so merely opening a dev report cannot change the save.
            PawnDiaryRecord diary = pawn == null
                ? null
                : LookupDiaryByPawnId(pawn.GetUniqueLoadID());
            if (diary == null) return "status=no_diary";
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            // A dev inspection action must not create old-save state or normalize pending evidence.
            PawnBeliefState state = diary.BeliefStateOrNull();
            if (state == null) return "status=no_belief_state";
            int now = Find.TickManager?.TicksGame ?? 0;

            string trend = BeliefCertaintyTrendTokens.Unknown;
            string magnitude = BeliefCertaintyMagnitudeTokens.Unknown;
            if (state.hasPendingCertainty)
                BeliefCertaintyPolicy.Trend(state.pendingCertaintyBefore, state.pendingCertaintyAfter,
                    policy, out trend, out magnitude);
            BeliefReflectionRequest request = new BeliefReflectionRequest
            {
                hasBeliefContext = state.hasLastObservation,
                nowTick = now,
                lastReflectionTick = state.lastReflectionTick,
                reflectionsThisQuadrum = state.reflectionsThisQuadrum,
                pendingIdeologyChange = state.pendingIdeologyChange,
                pendingMajorCertaintyShift = magnitude == BeliefCertaintyMagnitudeTokens.Major,
                hasPendingCertaintyDrift = state.hasPendingCertainty
            };
            BeliefReflectionDecision reflection = BeliefReflectionPolicy.Plan(request, policy);
            BeliefCertaintyBand band = BeliefCertaintyPolicy.BandFor(state.lastCertainty, policy);

            StringBuilder text = new StringBuilder();
            text.Append("status=ok");
            text.Append("; baseline_pending=").Append(state.baselineOnNextScan ? "true" : "false");
            text.Append("; ideology_id=").Append(state.lastIdeologyId);
            text.Append("; certainty_percent=").Append(
                Math.Round(state.lastCertainty * 100f, MidpointRounding.AwayFromZero)
                    .ToString(CultureInfo.InvariantCulture));
            text.Append("; certainty_band=").Append(band.token);
            text.Append("; pending_trend=").Append(trend);
            text.Append("; pending_magnitude=").Append(magnitude);
            text.Append("; pending_ideology_change=")
                .Append(state.pendingIdeologyChange ? "true" : "false");
            text.Append("; reflection_trigger=").Append(reflection.trigger);
            text.Append("; reflection_block=").Append(reflection.blockReason);
            text.Append("; reflected_source_count=").Append(state.lastReflectedSourceIds?.Count ?? 0);
            text.Append("; recent_precept_count=").Append(state.recentSelectedPreceptDefNames?.Count ?? 0);
            text.Append("; recent_meme_count=").Append(state.recentSelectedMemeDefNames?.Count ?? 0);
            return text.ToString();
        }
    }
}
