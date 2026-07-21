// Loaded-game coverage for Ideology Phase 1's guarded projection and non-emitting correlation seam.
// These fixtures use disposable pawns and real loaded Ideo/Precept/Thought/HistoryEvent objects, but
// assert only on the detached contracts that production code exposes. No reflection behavior or
// Phase 2 mutation page is exercised here.
using System;
using System.Collections.Generic;
using System.Linq;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves exact Thought.sourcePrecept capture, one event-time context write, and a bounded
    /// HistoryEventsManager observer that enriches evidence without creating a diary page.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryIdeologyPhase1FixtureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Ideology Phase 1] ";
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            pawn = scope.CreateAdultColonist();
            BeliefHistoryCorrelationCache.Reset();
            scope.RegisterCleanup(BeliefHistoryCorrelationCache.Reset);
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

        /// <summary>
        /// Builds a real precept-backed Thought_Memory. The signal must freeze exact source identity,
        /// and its one emitted page must retain a bounded event-time belief block.
        /// </summary>
        [Test]
        public static void ExactThoughtSourceReachesOneFrozenEventContext()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(!DlcContext.CaptureBeliefSnapshot(pawn).ideologyActive,
                    "The Phase 1 snapshot must be inactive without Ideology.");
                Log.Message(LogPrefix + "exact thought source: not applicable (Ideology inactive). ");
                return;
            }

            Precept sourcePrecept;
            ThoughtDef thoughtDef;
            Ideo fixtureIdeo = FindIdeoWithThoughtPrecept(out sourcePrecept, out thoughtDef);
            if (fixtureIdeo == null)
            {
                Log.Message(LogPrefix + "exact thought source: no loaded ideoligion exposed a typed "
                    + "PreceptComp_Thought; guarded empty behavior remains covered.");
                return;
            }

            PawnDiaryRimTestScope.Require(pawn.ideo != null,
                "An Ideology-active colonist must have a Pawn_IdeoTracker.");
            pawn.ideo.SetIdeo(fixtureIdeo);
            Thought_Memory thought = ThoughtMaker.MakeThought(thoughtDef, sourcePrecept);
            thought.pawn = pawn;
            ThoughtSignal signal = new ThoughtSignal(pawn, thought);
            BeliefEventEvidence evidence = signal.CapturedBeliefEvidence;
            PawnDiaryRimTestScope.Require(evidence != null
                    && evidence.sourcePreceptInstanceId == sourcePrecept.GetUniqueLoadID()
                    && evidence.sourcePreceptDefName == sourcePrecept.def.defName,
                "ThoughtSignal did not freeze Thought.sourcePrecept's exact instance and Def identity.");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => signal.Emit(scope.Component, CaptureDecision.GenerateSolo),
                thoughtDef.defName,
                pawn,
                null);
            string context = diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(context)
                    && context.Contains("relevant precept:")
                    && context.Length <= DiaryBeliefPolicy.Snapshot().maximumTotalCharacters,
                "The exact source-precept event did not retain one bounded event-time belief context.");
            PawnDiaryRimTestScope.Require(
                context == BeliefContextFormatter.NormalizeSaved(context, DiaryBeliefPolicy.Snapshot()),
                "The event-time belief block was not already in its stable saved form.");
        }

        /// <summary>
        /// Calls the real vanilla manager. The Harmony observer must add only one plain cache row and
        /// never emit a DiaryEvent; when an exact live correlation exists, the next authorized builder
        /// pass must consume that identity as structural evidence.
        /// </summary>
        [Test]
        public static void HistoryObserverIsBoundedNonEmittingAndStructurallyUseful()
        {
            HistoryEventDef historyDef = DefDatabase<HistoryEventDef>.AllDefsListForReading
                .FirstOrDefault(def => def != null && !string.IsNullOrWhiteSpace(def.defName));
            PawnDiaryRimTestScope.Require(historyDef != null && Find.HistoryEventsManager != null,
                "The loaded game must expose a HistoryEventDef and HistoryEventsManager.");

            if (ModsConfig.IdeologyActive)
            {
                Ideo fixtureIdeo = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(ideo => ideo != null);
                if (fixtureIdeo != null && pawn.ideo != null)
                {
                    pawn.ideo.SetIdeo(fixtureIdeo);
                    BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn);
                    string correlatedDef = snapshot.precepts
                        .Where(precept => precept?.correlations != null)
                        .SelectMany(precept => precept.correlations)
                        .FirstOrDefault(correlation => correlation != null
                            && correlation.kind == BeliefCorrelationKindTokens.HistoryEvent)?.defName;
                    HistoryEventDef correlated = string.IsNullOrWhiteSpace(correlatedDef)
                        ? null
                        : DefDatabase<HistoryEventDef>.GetNamedSilentFail(correlatedDef);
                    if (correlated != null) historyDef = correlated;
                }
            }

            HistoryEvent observed = new HistoryEvent(
                historyDef,
                pawn.Named(HistoryEventArgsNames.Doer));
            scope.RequireNoNewEvent(() => Find.HistoryEventsManager.RecordEvent(observed));

            if (!ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null)
            {
                PawnDiaryRimTestScope.Require(BeliefHistoryCorrelationCache.Count == 0,
                    "The history observer must remain inert without an active pawn ideoligion.");
                Log.Message(LogPrefix + "history observer enrichment: not applicable (no active pawn ideoligion). ");
                return;
            }

            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            List<string> nearby = BeliefHistoryCorrelationCache.NearbyDefNames(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(BeliefHistoryCorrelationCache.Count == 1
                    && nearby.Count == 1 && nearby[0] == historyDef.defName,
                "The non-emitting history observer did not retain one exact nearby identity.");

            BeliefSnapshot current = DlcContext.CaptureBeliefSnapshot(pawn);
            bool liveCorrelation = current.precepts.Any(precept => precept?.correlations != null
                && precept.correlations.Any(correlation => correlation != null
                    && correlation.kind == BeliefCorrelationKindTokens.HistoryEvent
                    && correlation.defName == historyDef.defName));
            if (liveCorrelation)
            {
                BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                    pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, "synthetic",
                    "HistoryObserverFixture", "initiator", "ordinary fixture", string.Empty);
                BeliefContextBuildResult built = BeliefContextBuilder.Build(
                    pawn, evidence, "history-observer-fixture", Find.TickManager.TicksGame,
                    DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(built.evaluated && built.resolution.stances.Count > 0
                        && built.resolution.stances[0].relevanceSource
                            == BeliefRelevanceSourceTokens.HistoryCorrelation,
                    "An exact cached history identity did not outrank lexical inference.");
            }
            else
            {
                Log.Message(LogPrefix + "history observer exact resolver branch: no loaded current "
                    + "precept correlated the available HistoryEventDef; cache/non-emission still passed.");
            }
        }

        private static Ideo FindIdeoWithThoughtPrecept(out Precept sourcePrecept, out ThoughtDef thoughtDef)
        {
            sourcePrecept = null;
            thoughtDef = null;
            List<Ideo> ideos = Find.IdeoManager?.IdeosListForReading;
            if (ideos == null) return null;
            for (int i = 0; i < ideos.Count; i++)
            {
                Ideo ideo = ideos[i];
                List<Precept> precepts = ideo?.PreceptsListForReading;
                if (precepts == null) continue;
                for (int j = 0; j < precepts.Count; j++)
                {
                    Precept precept = precepts[j];
                    List<PreceptComp> components = precept?.def?.comps;
                    if (components == null) continue;
                    for (int k = 0; k < components.Count; k++)
                    {
                        PreceptComp_Thought thought = components[k] as PreceptComp_Thought;
                        if (thought?.thought == null || !typeof(Thought_Memory).IsAssignableFrom(
                            thought.thought.ThoughtClass)) continue;
                        sourcePrecept = precept;
                        thoughtDef = thought.thought;
                        return ideo;
                    }
                }
            }
            return null;
        }
    }
}
