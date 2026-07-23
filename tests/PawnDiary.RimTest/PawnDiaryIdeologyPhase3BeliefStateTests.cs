// Loaded-game coverage for Phase 3 passive belief tracking. These tests exercise real Pawn_IdeoTracker
// state and the production scanner seam while proving that observation never creates a diary page.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Verifies baseline, accumulation, no-DLC reset, scan caps, and dev-safe diagnostics.</summary>
    [TestSuite]
    public static class PawnDiaryIdeologyPhase3BeliefStateTests
    {
        private static readonly FieldInfo DiariesByIdField = typeof(DiaryGameComponent).GetField(
            "diariesById", BindingFlags.Instance | BindingFlags.NonPublic);
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial");
            pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>First observation is silent; successive small live offsets merge into one debt.</summary>
        [Test]
        public static void LiveTrackerBaselinesAccumulatesAndLogsOnlySafeTokens()
        {
            int now = Find.TickManager.TicksGame;
            BeliefObservationDecision baseline = null;
            scope.RequireNoNewEvent(() =>
                baseline = scope.Component.ObservePawnBeliefForTests(pawn, now));
            PawnBeliefState state = DiaryFor(pawn).beliefState;

            if (!ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null)
            {
                PawnDiaryRimTestScope.Require(
                    baseline.action == BeliefObservationActionTokens.ResetPending
                    && state.baselineOnNextScan && !state.hasPendingCertainty
                    && !state.pendingIdeologyChange,
                    "Inactive Ideology did not cleanly reset Phase 3 state and request a baseline.");
                return;
            }

            PawnDiaryRimTestScope.Require(
                baseline.action == BeliefObservationActionTokens.Baseline
                    && baseline.recordBaseline && !baseline.createReflectionDebt,
                "The first valid loaded tracker observation was not a silent baseline.");

            // Put the disposable pawn in the middle of the scalar range so both 0.03 movements remain
            // exact and cannot be hidden by vanilla's [0,1] certainty clamp.
            scope.RequireNoNewEvent(() => pawn.ideo.OffsetCertainty(0.50f - pawn.ideo.Certainty));
            state.baselineOnNextScan = true;
            scope.RequireNoNewEvent(() =>
                baseline = scope.Component.ObservePawnBeliefForTests(pawn, now));

            BeliefObservationDecision minor = null;
            scope.RequireNoNewEvent(() =>
            {
                pawn.ideo.OffsetCertainty(-0.03f);
                minor = scope.Component.ObservePawnBeliefForTests(pawn, now);
            });
            PawnDiaryRimTestScope.Require(
                minor.action == BeliefObservationActionTokens.NoChange
                    && state.hasPendingCertainty && !minor.createReflectionDebt,
                "One sub-threshold live certainty movement did not remain pending and non-emitting.");

            BeliefObservationDecision accumulated = null;
            scope.RequireNoNewEvent(() =>
            {
                pawn.ideo.OffsetCertainty(-0.03f);
                accumulated = scope.Component.ObservePawnBeliefForTests(pawn, now);
            });
            PawnDiaryRimTestScope.Require(
                accumulated.action == BeliefObservationActionTokens.CertaintyChanged
                    && accumulated.createReflectionDebt
                    && Math.Abs(state.pendingCertaintyBefore - 0.50f) < 0.0001f
                    && Math.Abs(state.pendingCertaintyAfter - 0.44f) < 0.0001f,
                "Successive live certainty movements did not merge across the XML threshold.");

            string diagnostics = scope.Component.BeliefStateDiagnosticsForDev(pawn);
            PawnDiaryRimTestScope.Require(
                diagnostics.Contains("certainty_band=")
                    && diagnostics.Contains("pending_trend=falling")
                    && diagnostics.Contains("reflection_block=")
                    && (string.IsNullOrEmpty(state.lastIdeologyName)
                        || !diagnostics.Contains(state.lastIdeologyName)),
                "Belief diagnostics omitted mechanical tokens or leaked the authored ideology name.");
        }

        /// <summary>The rotating production pass never exceeds the XML pawn-work cap or emits pages.</summary>
        [Test]
        public static void ScheduledPassIsSpreadBoundedAndPageSilent()
        {
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            List<Pawn> extra = new List<Pawn>();
            for (int i = 0; i < policy.maximumBeliefPawnsPerScan + 2; i++)
            {
                Pawn added = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(added);
                extra.Add(added);
            }

            int processed = -1;
            scope.Component.ResetBeliefScannerForTests();
            scope.RequireNoNewEvent(() =>
                processed = scope.Component.ScanPawnBeliefsForTests(Find.TickManager.TicksGame));
            PawnDiaryRimTestScope.Require(processed == policy.maximumBeliefPawnsPerScan,
                "The elapsed belief scanner did not stop at the XML pawn-work cap.");

        }

        private static PawnDiaryRecord DiaryFor(Pawn target)
        {
            Dictionary<string, PawnDiaryRecord> diaries = DiariesByIdField?.GetValue(scope.Component)
                as Dictionary<string, PawnDiaryRecord>;
            PawnDiaryRecord result = null;
            PawnDiaryRimTestScope.Require(diaries != null
                    && diaries.TryGetValue(target.GetUniqueLoadID(), out result) && result != null,
                "The Phase 3 fixture could not find the pawn's diary record.");
            return result;
        }
    }
}
