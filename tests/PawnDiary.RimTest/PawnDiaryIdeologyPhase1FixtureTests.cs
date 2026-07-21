// Loaded-game coverage for Ideology Phase 1's guarded projection and non-emitting correlation seam.
// These fixtures use disposable pawns and real loaded Ideo/Precept/Thought/HistoryEvent objects, but
// assert only on the detached contracts that production code exposes. No reflection behavior or
// Phase 2 mutation page is exercised here.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial");
            pawn = scope.CreateAdultColonist();
            BeliefHistoryCorrelationCache.Reset();
            BeliefMutationCache.Reset();
            scope.RegisterCleanup(BeliefHistoryCorrelationCache.Reset);
            scope.RegisterCleanup(BeliefMutationCache.Reset);
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

        /// <summary>Locks the shared same-language policy snapshot used by frequent history hooks.</summary>
        [Test]
        public static void BeliefPolicySnapshotIsCachedForTheActiveLanguage()
        {
            BeliefPolicySnapshot first = DiaryBeliefPolicy.Snapshot();
            BeliefPolicySnapshot second = DiaryBeliefPolicy.Snapshot();
            PawnDiaryRimTestScope.Require(ReferenceEquals(first, second),
                "Belief policy rebuilt its deep-copied snapshot inside one active language.");
            PawnDiaryRimTestScope.Require(DiaryBeliefPolicy.Enabled == first.enabled,
                "The fast Enabled gate disagreed with the cached belief-policy snapshot.");
        }

        /// <summary>
        /// Forces the guarded live snapshot boundary to throw and proves optional enrichment cannot
        /// cancel the already-authorized ordinary body-change page.
        /// </summary>
        [Test]
        public static void BeliefEnrichmentFailureKeepsTheOrdinaryEvent()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "enrichment failure isolation: not applicable (Ideology inactive). ");
                return;
            }

            Ideo fixture = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(ideo => ideo != null);
            PawnDiaryRimTestScope.Require(fixture != null && pawn.ideo != null,
                "The failure-isolation fixture needs one loaded ideoligion.");
            pawn.ideo.SetIdeo(fixture);
            HediffDef pegLeg = DefDatabase<HediffDef>.GetNamedSilentFail("PegLeg");
            BodyPartRecord leg = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Leg");
            PawnDiaryRimTestScope.Require(pegLeg != null && leg != null,
                "The failure-isolation fixture needs the vanilla peg leg and a leg.");
            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.health?.hediffSet?.hediffs?.Contains(hediff) == true)
                    pawn.health.RemoveHediff(hediff);
            });

            System.Reflection.MethodInfo target = AccessTools.Method(
                typeof(DlcContext),
                nameof(DlcContext.CaptureBeliefSnapshot),
                new[] { typeof(Pawn), typeof(BeliefPolicySnapshot) });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the guarded belief snapshot overload for the failure fixture.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.BeliefFailure." + Guid.NewGuid().ToString("N"));
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(PawnDiaryIdeologyPhase1FixtureTests), nameof(ThrowBeliefSnapshotFixture)));
            try
            {
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => pawn.health.AddHediff(hediff, leg), pegLeg.defName, pawn, null);
                PawnDiaryRimTestScope.Require(
                    string.IsNullOrEmpty(diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                    "A failed optional belief snapshot left partial context on the ordinary page.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
        }

        /// <summary>
        /// Drives the real AddHediff hook with vanilla situational precepts whose opposite workers share
        /// one doctrine. Exact active ThoughtDef truth must retain the legacy approval/rejection cue.
        /// </summary>
        [Test]
        public static void VanillaBodyModificationWorkersPreserveAttitudeParity()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "body-mod stance parity: not applicable (Ideology inactive). ");
                return;
            }

            AssertBodyModificationAttitude("BodyMod_Approved", "approves");
        }

        /// <summary>Companion negative worker fixture for vanilla body-mod rejection.</summary>
        [Test]
        public static void VanillaBodyModificationNegativeWorkerPreservesAttitudeParity()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "negative body-mod stance parity: not applicable (Ideology inactive). ");
                return;
            }

            AssertBodyModificationAttitude("BodyMod_Disapproved", "despises");
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

        private static bool ThrowBeliefSnapshotFixture()
        {
            throw new InvalidOperationException("Synthetic belief snapshot failure.");
        }

        private static void AssertBodyModificationAttitude(string preceptDefName, string expectedAttitude)
        {
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail(preceptDefName);
            HediffDef pegLeg = DefDatabase<HediffDef>.GetNamedSilentFail("PegLeg");
            BodyPartRecord leg = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Leg");
            PawnDiaryRimTestScope.Require(target?.issue != null && pegLeg != null && leg != null,
                "The vanilla body-modification fixture Defs/body part were not loaded.");

            IdeoGenerationParms parms = new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            };
            Ideo fixture = IdeoGenerator.GenerateIdeo(parms);
            PawnDiaryRimTestScope.Require(fixture != null,
                "RimWorld did not generate a disposable Ideology fixture.");
            Precept existing = fixture.PreceptsListForReading
                .FirstOrDefault(precept => precept?.def?.issue == target.issue);
            if (existing != null) fixture.RemovePrecept(existing, false);
            fixture.AddPrecept(PreceptMaker.MakePrecept(target), false, Faction.OfPlayer.def, null);

            // Traits have stronger intentional precedence than ideology. Remove either vanilla body
            // trait from this disposable pawn so the assertion isolates the precept worker path.
            pawn.story.traits.allTraits.RemoveAll(trait =>
                trait?.def?.defName == "Transhumanist" || trait?.def?.defName == "BodyPurist");
            pawn.ideo.SetIdeo(fixture);
            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.health?.hediffSet?.hediffs?.Contains(hediff) == true)
                    pawn.health.RemoveHediff(hediff);
            });

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => pawn.health.AddHediff(hediff, leg), pegLeg.defName, pawn, null);
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    DiaryContextFields.Value(diaryEvent.gameContext, "body_attitude"),
                    expectedAttitude,
                    StringComparison.Ordinal),
                preceptDefName + " did not preserve body_attitude=" + expectedAttitude
                    + " through the real AddHediff capture path.");
        }
    }
}
