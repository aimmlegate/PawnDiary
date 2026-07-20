// In-game Anomaly A1.1/A2.0 persistence fixtures. These tests use the live DiaryGameComponent's
// actual ExposeAnomalyData method, exact save keys, deep monolith/creepjoiner rows, guarded legacy
// baseline adapter, and transient reset. Pure normalization remains in DiarySaveNormalizationTests.
//
// Why an adapter? RimWorld's Scribe expects an IExposable root. The adapter delegates every Scribe
// phase to the already-running component instead of constructing a second DiaryGameComponent, which
// would disturb the live LLM session owned by the loaded test colony.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "DLC-safety") and docs/lore/scribe-saving.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves actual Anomaly Scribe wiring, old-save defaults, and DLC-aware bootstrap.</summary>
    [TestSuite]
    public static class PawnDiaryAnomalyStateFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ExposeAnomalyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeAnomalyData", PrivateInstance);
        private static readonly MethodInfo BootstrapAnomalyMethod =
            typeof(DiaryGameComponent).GetMethod("BootstrapAnomalyForLoadedSave", PrivateInstance);
        private static readonly FieldInfo SchemaVersionField =
            typeof(DiaryGameComponent).GetField("anomalySupportSchemaVersion", PrivateInstance);
        private static readonly FieldInfo FirstBreakthroughField =
            typeof(DiaryGameComponent).GetField(
                "anomalyFirstStudyBreakthroughObserved", PrivateInstance);
        private static readonly FieldInfo CompletedDefNamesField =
            typeof(DiaryGameComponent).GetField("anomalyCompletedStudyDefNames", PrivateInstance);
        private static readonly FieldInfo PromotionKeysField =
            typeof(DiaryGameComponent).GetField(
                "anomalyPromotedStudyMilestoneKeys", PrivateInstance);
        private static readonly FieldInfo MonolithBaselineField =
            typeof(DiaryGameComponent).GetField(
                "anomalyMonolithBaselineLevelDefName", PrivateInstance);
        private static readonly FieldInfo MonolithSnapshotField =
            typeof(DiaryGameComponent).GetField(
                "anomalyLastMonolithKnowledgeSnapshot", PrivateInstance);
        private static readonly FieldInfo CreepJoinerArcsField =
            typeof(DiaryGameComponent).GetField("anomalyCreepJoinerArcs", PrivateInstance);

        private static DiaryGameComponent anomalyScribeTarget;
        private static PawnDiaryRimTestScope scope;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                AnomalyStudySuppressionCache.Clear();
                scope?.TearDown();
            }
            finally
            {
                anomalyScribeTarget = null;
                scope = null;
            }
        }

        /// <summary>
        /// All seven production keys round-trip through the actual component method. PostLoadInit also
        /// sorts/de-duplicates histories, removes malformed rows, trims values, and deep-loads
        /// independent monolith knowledge and creepjoiner arc objects.
        /// </summary>
        [Test]
        public static void ActualComponentRoundTripsAllSevenAnomalyKeys()
        {
            RequireProductionWiring();
            RawAnomalyState original = RawAnomalyState.Capture(scope.Component);
            anomalyScribeTarget = scope.Component;
            try
            {
                SetRawState(
                    scope.Component,
                    AnomalyPersistencePolicy.CurrentSchemaVersion,
                    true,
                    new List<string> { " Entity_B ", "Entity_A", "Entity_A", "bad|entity" },
                    new List<string>
                    {
                        " Entity_B|20|late_notes ", "Entity_A|10|early_notes", "bad"
                    },
                    " Waking ",
                    new AnomalyMonolithKnowledgeState
                    {
                        researcherPawnId = " Pawn_A ",
                        studyStage = AnomalyStudyStageTokens.Promoted,
                        tick = 500,
                        reachedProgress = 40,
                        becameActivatable = true
                    },
                    new List<CreepJoinerArcState>
                    {
                        new CreepJoinerArcState
                        {
                            pawnId = " Pawn_Creep ",
                            arrivalEventId = " Arrival_1 ",
                            joinedTick = 300,
                            lastVisiblePhase = AnomalyOutcomeTokens.Departed,
                            lastVisibleEventId = " Outcome_1 ",
                            terminal = true,
                            schemaVersion = 1
                        }
                    });

                AnomalyMonolithKnowledgeState savedReference =
                    MonolithSnapshotField.GetValue(scope.Component) as AnomalyMonolithKnowledgeState;
                CreepJoinerArcState savedArcReference =
                    (CreepJoinerArcsField.GetValue(scope.Component) as List<CreepJoinerArcState>)[0];
                ScribeRoundTripWithAfterSave(
                    new ActualAnomalyComponentStateAdapter(),
                    () => SetRawState(scope.Component, -1, false, null, null, "stale", null, null));

                AnomalyPersistentStateSnapshot loaded = scope.Component.AnomalyStateSnapshotForTests();
                Require(loaded.schemaVersion == AnomalyPersistencePolicy.CurrentSchemaVersion
                        && loaded.firstStudyBreakthroughObserved,
                    "actual component lost the Anomaly schema/first-study values.");
                Require(loaded.completedStudyDefNames.Count == 2
                        && loaded.completedStudyDefNames[0] == "Entity_A"
                        && loaded.completedStudyDefNames[1] == "Entity_B",
                    "actual component did not normalize the completed-study value list on load.");
                Require(loaded.promotedStudyMilestoneKeys.Count == 2,
                    "actual component did not normalize the promotion-key value list on load.");
                Require(loaded.monolithBaselineLevelDefName == "Waking",
                    "actual component lost or failed to trim the monolith baseline level.");
                Require(loaded.lastMonolithKnowledgeSnapshot != null
                        && loaded.lastMonolithKnowledgeSnapshot.researcherPawnId == "Pawn_A"
                        && loaded.lastMonolithKnowledgeSnapshot.reachedProgress == 40,
                    "actual component lost the deep monolith knowledge snapshot.");
                Require(!ReferenceEquals(
                        savedReference, MonolithSnapshotField.GetValue(scope.Component)),
                    "deep Scribe reused the pre-save monolith knowledge object reference.");
                Require(loaded.creepJoinerArcs.Count == 1
                        && loaded.creepJoinerArcs[0].pawnId == "Pawn_Creep"
                        && loaded.creepJoinerArcs[0].arrivalEventId == "Arrival_1"
                        && loaded.creepJoinerArcs[0].lastVisiblePhase == AnomalyOutcomeTokens.Departed
                        && loaded.creepJoinerArcs[0].terminal,
                    "actual component lost or failed to normalize the deep creepjoiner arc.");
                Require(!ReferenceEquals(savedArcReference,
                        (CreepJoinerArcsField.GetValue(scope.Component)
                            as List<CreepJoinerArcState>)[0]),
                    "deep Scribe reused the pre-save creepjoiner arc object reference.");
            }
            finally
            {
                original.Restore(scope.Component);
                anomalyScribeTarget = null;
            }
        }

        /// <summary>
        /// A pre-A1 save with none of the seven keys loads as schema zero with safe empty collections.
        /// This drives the real Scribe defaults rather than a mirror of the production contract.
        /// </summary>
        [Test]
        public static void MissingAnomalyKeysLoadAsLegacyPendingState()
        {
            RequireProductionWiring();
            RawAnomalyState original = RawAnomalyState.Capture(scope.Component);
            anomalyScribeTarget = scope.Component;
            try
            {
                ScribeRoundTripWithAfterSave(
                    new LegacyMissingAnomalyComponentStateAdapter(),
                    () => SetRawState(
                        scope.Component,
                        99,
                        true,
                        new List<string> { "stale" },
                        new List<string> { "Entity|10|stale" },
                        "stale",
                        new AnomalyMonolithKnowledgeState
                        {
                            researcherPawnId = "Pawn_Stale",
                            studyStage = AnomalyStudyStageTokens.Promoted,
                            tick = 1,
                            reachedProgress = 10
                        }));

                AnomalyPersistentStateSnapshot loaded = scope.Component.AnomalyStateSnapshotForTests();
                Require(loaded.schemaVersion == 0 && !loaded.firstStudyBreakthroughObserved,
                    "missing old-save Anomaly keys did not retain the pending schema-zero marker.");
                Require(loaded.completedStudyDefNames != null
                        && loaded.completedStudyDefNames.Count == 0
                        && loaded.promotedStudyMilestoneKeys != null
                        && loaded.promotedStudyMilestoneKeys.Count == 0,
                    "missing old-save Anomaly collections did not normalize to safe empty lists.");
                Require(loaded.monolithBaselineLevelDefName.Length == 0
                        && loaded.lastMonolithKnowledgeSnapshot == null,
                    "missing old-save Anomaly keys invented monolith history.");
                Require(loaded.creepJoinerArcs != null && loaded.creepJoinerArcs.Count == 0,
                    "missing old-save Anomaly arc key did not normalize to a safe empty list.");
            }
            finally
            {
                original.Restore(scope.Component);
                anomalyScribeTarget = null;
            }
        }

        /// <summary>
        /// The real loaded-game bootstrap clears transient ownership on every load. Without Anomaly it
        /// leaves schema zero for a later re-enabled load; with Anomaly it runs the guarded map scan,
        /// advances once, and conservatively suppresses a false historical “first.”
        /// </summary>
        [Test]
        public static void LoadedGameBootstrapDefersOrBaselinesByAnomalyAvailability()
        {
            RequireProductionWiring();
            RawAnomalyState original = RawAnomalyState.Capture(scope.Component);
            try
            {
                SetRawState(
                    scope.Component,
                    0,
                    false,
                    new List<string>(),
                    new List<string>(),
                    string.Empty,
                    null);
                Require(AnomalyStudySuppressionCache.Register(
                        new AnomalyStudyTaleClaim
                        {
                            studierPawnId = "Pawn_LoadFixture",
                            studiedEntityId = "Thing_LoadFixture",
                            studiedDefName = "Entity_LoadFixture",
                            acceptedTick = 10
                        },
                        10,
                        100),
                    "could not seed the transient Anomaly claim before the load boundary.");

                BootstrapAnomalyMethod.Invoke(scope.Component, null);
                Require(AnomalyStudySuppressionCache.CountForTests == 0,
                    "loaded-game bootstrap retained a transient study/Tale claim.");
                AnomalyPersistentStateSnapshot loaded = scope.Component.AnomalyStateSnapshotForTests();
                if (ModsConfig.AnomalyActive)
                {
                    Require(loaded.schemaVersion == AnomalyPersistencePolicy.CurrentSchemaVersion,
                        "Anomaly-active old save did not advance to the current schema.");
                    Require(loaded.firstStudyBreakthroughObserved,
                        "incomplete loaded-map history did not suppress a false future first.");

                    BootstrapAnomalyMethod.Invoke(scope.Component, null);
                    AnomalyPersistentStateSnapshot second =
                        scope.Component.AnomalyStateSnapshotForTests();
                    Require(second.schemaVersion == loaded.schemaVersion
                            && second.firstStudyBreakthroughObserved
                                == loaded.firstStudyBreakthroughObserved,
                        "current-schema Anomaly state was baselined a second time.");
                }
                else
                {
                    Require(loaded.schemaVersion == 0
                            && !loaded.firstStudyBreakthroughObserved
                            && loaded.completedStudyDefNames.Count == 0,
                        "Anomaly-inactive load consumed or altered the pending legacy baseline.");
                    Require(DlcContext.CurrentAnomalyMonolithLevelDefName().Length == 0,
                        "Anomaly-inactive live context returned a monolith level.");
                }
            }
            finally
            {
                AnomalyStudySuppressionCache.Clear();
                original.Restore(scope.Component);
            }
        }

        private sealed class ActualAnomalyComponentStateAdapter : IExposable
        {
            public void ExposeData()
            {
                InvokeActualAnomalyExposeData();
            }
        }

        private sealed class LegacyMissingAnomalyComponentStateAdapter : IExposable
        {
            public void ExposeData()
            {
                if (Scribe.mode != LoadSaveMode.Saving)
                    InvokeActualAnomalyExposeData();
            }
        }

        private sealed class RawAnomalyState
        {
            public int schemaVersion;
            public bool firstBreakthrough;
            public List<string> completedDefNames;
            public List<string> promotionKeys;
            public string monolithBaseline;
            public AnomalyMonolithKnowledgeState monolithSnapshot;
            public List<CreepJoinerArcState> creepJoinerArcs;

            public static RawAnomalyState Capture(DiaryGameComponent component)
            {
                return new RawAnomalyState
                {
                    schemaVersion = (int)SchemaVersionField.GetValue(component),
                    firstBreakthrough = (bool)FirstBreakthroughField.GetValue(component),
                    completedDefNames = CompletedDefNamesField.GetValue(component) as List<string>,
                    promotionKeys = PromotionKeysField.GetValue(component) as List<string>,
                    monolithBaseline = MonolithBaselineField.GetValue(component) as string,
                    monolithSnapshot = MonolithSnapshotField.GetValue(component)
                        as AnomalyMonolithKnowledgeState,
                    creepJoinerArcs = CreepJoinerArcsField.GetValue(component)
                        as List<CreepJoinerArcState>
                };
            }

            public void Restore(DiaryGameComponent component)
            {
                SetRawState(
                    component,
                    schemaVersion,
                    firstBreakthrough,
                    completedDefNames,
                    promotionKeys,
                    monolithBaseline,
                    monolithSnapshot,
                    creepJoinerArcs);
            }
        }

        private static void RequireProductionWiring()
        {
            Require(scope?.Component != null
                    && ExposeAnomalyDataMethod != null
                    && BootstrapAnomalyMethod != null
                    && SchemaVersionField != null
                    && FirstBreakthroughField != null
                    && CompletedDefNamesField != null
                    && PromotionKeysField != null
                    && MonolithBaselineField != null
                    && MonolithSnapshotField != null
                    && CreepJoinerArcsField != null,
                "Could not resolve the component's actual Anomaly persistence wiring.");
        }

        private static void SetRawState(
            DiaryGameComponent component,
            int schemaVersion,
            bool firstBreakthrough,
            List<string> completedDefNames,
            List<string> promotionKeys,
            string monolithBaseline,
            AnomalyMonolithKnowledgeState monolithSnapshot,
            List<CreepJoinerArcState> creepJoinerArcs = null)
        {
            SchemaVersionField.SetValue(component, schemaVersion);
            FirstBreakthroughField.SetValue(component, firstBreakthrough);
            CompletedDefNamesField.SetValue(component, completedDefNames);
            PromotionKeysField.SetValue(component, promotionKeys);
            MonolithBaselineField.SetValue(component, monolithBaseline);
            MonolithSnapshotField.SetValue(component, monolithSnapshot);
            CreepJoinerArcsField.SetValue(component, creepJoinerArcs);
        }

        private static void InvokeActualAnomalyExposeData()
        {
            Require(anomalyScribeTarget != null && ExposeAnomalyDataMethod != null,
                "the actual Anomaly component Scribe adapter has no target.");
            ExposeAnomalyDataMethod.Invoke(anomalyScribeTarget, null);
        }

        private static T ScribeRoundTripWithAfterSave<T>(T original, Action afterSave)
            where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_anomaly_component_" + Guid.NewGuid().ToString("N") + ".xml");
            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = original;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();
                afterSave?.Invoke();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup must not hide the actual Scribe assertion.
                }
            }

            Require(loaded != null, "the Anomaly component Scribe adapter loaded null.");
            return loaded;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
