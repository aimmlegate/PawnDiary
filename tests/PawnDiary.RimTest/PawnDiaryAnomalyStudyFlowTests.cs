// Focused in-game Anomaly A1.2 fixtures. These call the real CompStudyUnlocks.OnStudied method so
// Harmony, DlcContext, component history, catalog/group settings, event creation, exact Tale
// suppression, and Scribe all participate. Anomaly-inactive profiles assert inert registration and
// log explicit skips for DLC-owned positive paths.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises the live study hook at each frozen A1.2 transition boundary.</summary>
    [TestSuite]
    public static class PawnDiaryAnomalyStudyFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
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
        private static readonly MethodInfo ExposeAnomalyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeAnomalyData", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn studier;
        private static RawAnomalyState originalState;
        private static List<AnomalyStudyTaleClaim> originalClaims;
        private static List<AnomalyRecentStudyFact> originalRecentStudies;
        private static HashSet<Letter> originalLetters;
        private static DiaryGameComponent scribeTarget;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("anomalyStudyBreakthrough");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            studier = scope.CreateAdultColonist();
            RequireReflectionWiring();
            originalState = RawAnomalyState.Capture(scope.Component);
            originalClaims = AnomalyStudySuppressionCache.SnapshotForTests();
            originalRecentStudies = AnomalyRecentStudyCache.SnapshotForTests();
            originalLetters = new HashSet<Letter>(
                Find.LetterStack?.LettersListForReading ?? new List<Letter>());
            SetCleanState(firstBreakthroughObserved: false);
            AnomalyStudySuppressionCache.Clear();
            AnomalyRecentStudyCache.Clear();
        }

        [AfterEach]
        public static void TearDown()
        {
            Exception firstFailure = null;
            try
            {
                RemoveFixtureLetters();
                originalState?.Restore(scope?.Component);
                if (originalClaims != null)
                    AnomalyStudySuppressionCache.RestoreForTests(originalClaims);
                if (originalRecentStudies != null)
                    AnomalyRecentStudyCache.RestoreForTests(originalRecentStudies);
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }

            try
            {
                scope?.TearDown();
            }
            catch (Exception exception)
            {
                if (firstFailure == null) firstFailure = exception;
            }
            finally
            {
                scribeTarget = null;
                originalLetters = null;
                originalClaims = null;
                originalRecentStudies = null;
                originalState = null;
                studier = null;
                scope = null;
            }

            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves registration is absent off-DLC and owns both exact callbacks on-DLC.</summary>
        [Test]
        public static void StudyHookRegistrationMatchesAnomalyAvailability()
        {
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.StudyHookReady == ModsConfig.AnomalyActive,
                "The Anomaly study hook health flag did not match DLC availability.");
            if (!ModsConfig.AnomalyActive) return;

            MethodBase target = AccessTools.DeclaredMethod(
                typeof(CompStudyUnlocks),
                "OnStudied",
                new[] { typeof(Pawn), typeof(float), typeof(KnowledgeCategoryDef) });
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && patches != null
                    && patches.Prefixes.Any(OwnedStudyPatch)
                    && patches.Postfixes.Any(OwnedStudyPatch),
                "The exact CompStudyUnlocks.OnStudied method lacks Pawn Diary's prefix/postfix.");
        }

        /// <summary>A study call below every note threshold must leave history and pages untouched.</summary>
        [Test]
        public static void BelowThresholdCreatesNoMilestone()
        {
            if (!RequireAnomalyOrSkip(nameof(BelowThresholdCreatesNoMilestone))) return;
            CompStudyUnlocks study = StudyComp("BelowThreshold", 9f, 10f, 20f);
            scope.RequireNoNewEvent(() => study.OnStudied(studier, 1f, null));
            PawnDiaryRimTestScope.Require(study.Progress == 0
                    && !scope.Component.AnomalyStateSnapshotForTests().firstStudyBreakthroughObserved
                    && AnomalyRecentStudyCache.Matches(
                        study.parent.GetUniqueLoadID(),
                        studier.GetUniqueLoadID(),
                        Find.TickManager?.TicksGame ?? 0,
                        DiaryAnomalyPolicy.Snapshot().recentStudierMaxAgeTicks),
                "A below-threshold call invented progress/history or lost exact recent-study evidence.");
        }

        /// <summary>One crossed note creates one exact-author page and only then a Tale claim.</summary>
        [Test]
        public static void OneThresholdCreatesExactResearcherPage()
        {
            if (!RequireAnomalyOrSkip(nameof(OneThresholdCreatesExactResearcherPage))) return;
            CompStudyUnlocks study = StudyComp("OneThreshold", 10f, 10f, 20f);
            DiaryEvent page = scope.FireAndRequireEvent(
                () => study.OnStudied(studier, 1f, null),
                AnomalyEventDefNames.StudyBreakthrough,
                studier,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "study_stage=first_breakthrough");
            PawnDiaryRimTestScope.Require(study.Progress == 1
                    && AnomalyStudySuppressionCache.CountForTests == 1,
                "One threshold did not commit exactly one progress row and accepted Tale claim.");
        }

        /// <summary>A missing visible subject label uses localized neutral prose, never its raw Def name.</summary>
        [Test]
        public static void MissingVisibleStudyLabelUsesLocalizedNeutralSubject()
        {
            if (!RequireAnomalyOrSkip(nameof(MissingVisibleStudyLabelUsesLocalizedNeutralSubject))) return;
            const string rawDefName = "PawnDiary_RimTestInternalStudyDef";
            AnomalyStudyFacts facts = new AnomalyStudyFacts
            {
                studiedEntityId = "Thing_PawnDiary_RimTestMissingStudyLabel",
                studiedDefName = rawDefName,
                studiedLabel = string.Empty,
                studierPawnId = studier.GetUniqueLoadID(),
                studierEligible = true,
                tick = Find.TickManager?.TicksGame ?? 0,
                noteThresholdsCrossed = 1
            };
            AnomalyStudyPlan plan = new AnomalyStudyPlan
            {
                disposition = AnomalyStudyDisposition.Generate,
                stageToken = AnomalyStudyStageTokens.FirstBreakthrough
            };
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                AnomalyEventDefNames.StudyBreakthrough);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new AnomalyStudySignal(
                    studier, facts, plan, DiaryAnomalyPolicy.Snapshot(), group)),
                AnomalyEventDefNames.StudyBreakthrough,
                studier,
                null,
                rejectOtherTestPawnEvents: true);
            string neutralSubject = "PawnDiary.Event.Anomaly.Study.UnknownSubject"
                .Translate().Resolve();
            string expectedFallback = "PawnDiary.Event.Anomaly.Study.Fallback"
                .Translate(studier.LabelShortCap, neutralSubject).Resolve();
            PawnDiaryRimTestScope.Require(string.Equals(
                    page.initiatorText,
                    expectedFallback,
                    StringComparison.Ordinal)
                    && page.initiatorText.IndexOf(rawDefName, StringComparison.Ordinal) < 0,
                "Localized study fallback exposed a raw Def name instead of the neutral subject.");
        }

        /// <summary>A single vanilla call jumping across several notes still creates only one page.</summary>
        [Test]
        public static void MultiNoteJumpCreatesOneMilestonePage()
        {
            if (!RequireAnomalyOrSkip(nameof(MultiNoteJumpCreatesOneMilestonePage))) return;
            CompStudyUnlocks study = StudyComp("MultiJump", 30f, 10f, 20f, 30f);
            DiaryEvent page = scope.FireAndRequireEvent(
                () => study.OnStudied(studier, 1f, null),
                AnomalyEventDefNames.StudyBreakthrough,
                studier,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "study_stage=first_breakthrough");
            PawnDiaryRimTestScope.Require(study.Progress == 3 && study.Completed,
                "The synthetic vanilla call did not cross all three configured notes.");
        }

        /// <summary>With first already observed, the first completed subject kind owns the page.</summary>
        [Test]
        public static void CompletionCreatesCompletedKindMilestone()
        {
            if (!RequireAnomalyOrSkip(nameof(CompletionCreatesCompletedKindMilestone))) return;
            SetCleanState(firstBreakthroughObserved: true);
            string subjectDefName;
            CompStudyUnlocks study = StudyComp("Completion", 10f, out subjectDefName, 10f);
            DiaryEvent page = scope.FireAndRequireEvent(
                () => study.OnStudied(studier, 1f, null),
                AnomalyEventDefNames.StudyBreakthrough,
                studier,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "study_stage=completed_kind");
            AnomalyPersistentStateSnapshot state = scope.Component.AnomalyStateSnapshotForTests();
            PawnDiaryRimTestScope.Require(study.Completed
                    && state.completedStudyDefNames.Contains(subjectDefName),
                "Completion did not persist the exact studied subject kind.");
        }

        /// <summary>Disabling the XML-owned group suppresses output but still advances history.</summary>
        [Test]
        public static void DisabledStudyGroupAdvancesHistoryWithoutClaimingTale()
        {
            if (!RequireAnomalyOrSkip(nameof(DisabledStudyGroupAdvancesHistoryWithoutClaimingTale))) return;
            PawnDiaryMod.Settings.SetGroupEnabled("anomalyStudyBreakthrough", false);
            CompStudyUnlocks study = StudyComp("Disabled", 10f, 10f, 20f);
            scope.RequireNoNewEvent(() => study.OnStudied(studier, 1f, null));
            PawnDiaryRimTestScope.Require(
                scope.Component.AnomalyStateSnapshotForTests().firstStudyBreakthroughObserved
                    && AnomalyStudySuppressionCache.CountForTests == 0,
                "Disabled output lost history or incorrectly claimed the generic Tale.");
        }

        /// <summary>Real Scribe preserves committed history, preventing a post-load false first page.</summary>
        [Test]
        public static void CommittedStudyHistorySurvivesScribeWithoutReplay()
        {
            if (!RequireAnomalyOrSkip(nameof(CommittedStudyHistorySurvivesScribeWithoutReplay))) return;
            CompStudyUnlocks first = StudyComp("BeforeSave", 10f, 10f, 20f);
            scope.FireAndRequireEvent(
                () => first.OnStudied(studier, 1f, null),
                AnomalyEventDefNames.StudyBreakthrough,
                studier,
                null,
                rejectOtherTestPawnEvents: true);

            RoundTripActualAnomalyState();
            AnomalyStudySuppressionCache.Clear();
            CompStudyUnlocks afterLoad = StudyComp("AfterLoad", 10f, 10f, 20f);
            scope.RequireNoNewEvent(() => afterLoad.OnStudied(studier, 1f, null));
            PawnDiaryRimTestScope.Require(
                scope.Component.AnomalyStateSnapshotForTests().firstStudyBreakthroughObserved,
                "The real Scribe round-trip lost committed first-study history.");
        }

        /// <summary>Exact StudiedEntity ownership consumes once; the next unmatched Tale stays generic.</summary>
        [Test]
        public static void ExactStudiedEntitySuppressionConsumesOnce()
        {
            if (!RequireAnomalyOrSkip(nameof(ExactStudiedEntitySuppressionConsumesOnce))) return;
            Pawn studiedPawn = scope.CreateAdultColonist();
            int tick = Find.TickManager?.TicksGame ?? 0;
            PawnDiaryRimTestScope.Require(AnomalyStudySuppressionCache.Register(
                    new AnomalyStudyTaleClaim
                    {
                        studierPawnId = studier.GetUniqueLoadID(),
                        studiedEntityId = studiedPawn.GetUniqueLoadID(),
                        studiedDefName = studiedPawn.def.defName,
                        acceptedTick = tick
                    },
                    tick,
                    DiaryAnomalyPolicy.Snapshot().studyTaleSuppressionTicks),
                "Could not seed the exact accepted-page ownership claim.");

            Tale_DoublePawn exact = new Tale_DoublePawn(studier, studiedPawn)
            {
                def = TaleDefOf.StudiedEntity
            };
            TaleSignal owned = new TaleSignal(exact, TaleDefOf.StudiedEntity);
            PawnDiaryRimTestScope.Require(owned.Payload == null
                    && AnomalyStudySuppressionCache.CountForTests == 0,
                "The exact StudiedEntity Tale was not suppressed and consumed once.");

            TaleSignal unmatched = new TaleSignal(exact, TaleDefOf.StudiedEntity);
            PawnDiaryRimTestScope.Require(unmatched.Payload != null,
                "A consumed claim incorrectly suppressed a later generic StudiedEntity Tale.");
        }

        /// <summary>
        /// A known vanilla study job remains authoritative beyond the fallback tick window, while
        /// cache pruning still bounds unrelated job-less compatibility claims.
        /// </summary>
        [Test]
        public static void SlowExactStudyJobRetainsTaleOwnership()
        {
            AnomalyStudyTaleClaim slow = new AnomalyStudyTaleClaim
            {
                studierPawnId = "Pawn_SlowResearcher",
                studiedEntityId = "Entity_SlowSubject",
                studiedDefName = "Entity_Fleshbeast",
                studyJobId = "Job_42",
                acceptedTick = 100
            };
            PawnDiaryRimTestScope.Require(
                AnomalyStudySuppressionCache.Register(slow, 100, 10),
                "Could not seed the exact slow-job ownership claim.");

            AnomalyStudyTaleClaim later = new AnomalyStudyTaleClaim
            {
                studierPawnId = "Pawn_Other",
                studiedEntityId = "Entity_Other",
                studiedDefName = "Entity_Other",
                acceptedTick = 10000
            };
            PawnDiaryRimTestScope.Require(
                AnomalyStudySuppressionCache.Register(later, 10000, 10)
                    && AnomalyStudySuppressionCache.CountForTests == 2,
                "Pruning discarded an exact live-job claim solely because study work was slow.");

            PawnDiaryRimTestScope.Require(
                AnomalyStudySuppressionCache.TryConsume(
                    new AnomalyStudiedTaleFacts
                    {
                        studierPawnId = slow.studierPawnId,
                        studiedEntityId = slow.studiedEntityId,
                        studiedDefName = slow.studiedDefName,
                        studyJobId = slow.studyJobId,
                        tick = 10000
                    },
                    10)
                    && AnomalyStudySuppressionCache.CountForTests == 1,
                "The exact slow study job did not consume its delayed generic Tale.");
        }

        /// <summary>An unverified monolith level must not advance baseline or consume saved context.</summary>
        [Test]
        public static void MissingMonolithLevelLeavesKnowledgeUntouched()
        {
            AnomalyMonolithKnowledgeState knowledge = new AnomalyMonolithKnowledgeState
            {
                researcherPawnId = studier.GetUniqueLoadID(),
                studyStage = AnomalyStudyStageTokens.Promoted,
                tick = Find.TickManager?.TicksGame ?? 0,
                reachedProgress = 2,
                consumed = false
            };
            SetRawState(
                scope.Component,
                AnomalyPersistencePolicy.CurrentSchemaVersion,
                firstBreakthrough: true,
                completedDefNames: new List<string>(),
                promotionKeys: new List<string>(),
                monolithBaseline: "Awakened",
                monolithSnapshot: knowledge);

            Thing harmlessThing = ThingMaker.MakeThing(ThingDefOf.Steel);
            scope.Component.RecordEventWindowVoidMonolithActivation(
                harmlessThing,
                string.Empty,
                string.Empty,
                studier,
                recordPage: false);

            AnomalyPersistentStateSnapshot state = scope.Component.AnomalyStateSnapshotForTests();
            PawnDiaryRimTestScope.Require(
                state.monolithBaselineLevelDefName == "Awakened"
                    && state.lastMonolithKnowledgeSnapshot != null
                    && !state.lastMonolithKnowledgeSnapshot.consumed,
                "An unverified reached level advanced the baseline or consumed study context.");
        }

        private static bool OwnedStudyPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(DiaryAnomalyPatches);
        }

        private static CompStudyUnlocks StudyComp(
            string suffix,
            float knowledgeGained,
            params float[] thresholds)
        {
            string ignored;
            return StudyComp(suffix, knowledgeGained, out ignored, thresholds);
        }

        private static CompStudyUnlocks StudyComp(
            string suffix,
            float knowledgeGained,
            out string defName,
            params float[] thresholds)
        {
            defName = "PawnDiary_RimTestAnomalyStudy_" + suffix;
            CompProperties_StudyUnlocks unlocks = new CompProperties_StudyUnlocks();
            for (int i = 0; i < thresholds.Length; i++)
            {
                unlocks.studyNotes.Add(new StudyNote
                {
                    threshold = thresholds[i],
                    label = "Pawn Diary RimTest study note",
                    text = "Pawn Diary RimTest study note"
                });
            }

            ThingDef def = new ThingDef
            {
                defName = defName,
                label = "Pawn Diary RimTest study subject",
                category = ThingCategory.Item,
                thingClass = typeof(ThingWithComps),
                selectable = true,
                comps = new List<CompProperties>
                {
                    new CompProperties_Studiable(),
                    unlocks
                }
            };
            ThingWithComps thing = ThingMaker.MakeThing(def) as ThingWithComps;
            CompStudyUnlocks study = thing?.GetComp<CompStudyUnlocks>();
            CompStudiable studiable = thing?.GetComp<CompStudiable>();
            PawnDiaryRimTestScope.Require(study != null && studiable != null,
                "The synthetic study subject did not initialize both vanilla comps.");
            studiable.anomalyKnowledgeGained = knowledgeGained;
            return study;
        }

        private static void SetCleanState(bool firstBreakthroughObserved)
        {
            SetRawState(
                scope.Component,
                AnomalyPersistencePolicy.CurrentSchemaVersion,
                firstBreakthroughObserved,
                new List<string>(),
                new List<string>(),
                DlcContext.CurrentAnomalyMonolithLevelDefName(),
                null);
        }

        private static void RoundTripActualAnomalyState()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_anomaly_study_" + Guid.NewGuid().ToString("N") + ".xml");
            scribeTarget = scope.Component;
            try
            {
                ActualAnomalyStateAdapter save = new ActualAnomalyStateAdapter();
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref save, "obj");
                Scribe.saver.FinalizeSaving();
                SetCleanState(firstBreakthroughObserved: false);

                ActualAnomalyStateAdapter loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
                PawnDiaryRimTestScope.Require(loaded != null,
                    "The actual Anomaly state adapter loaded null.");
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort temp cleanup must not hide the Scribe assertion.
                }
            }
        }

        private sealed class ActualAnomalyStateAdapter : IExposable
        {
            public void ExposeData()
            {
                PawnDiaryRimTestScope.Require(scribeTarget != null && ExposeAnomalyDataMethod != null,
                    "The actual Anomaly Scribe method has no live component target.");
                ExposeAnomalyDataMethod.Invoke(scribeTarget, null);
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
                        as AnomalyMonolithKnowledgeState
                };
            }

            public void Restore(DiaryGameComponent component)
            {
                if (component == null) return;
                SetRawState(component, schemaVersion, firstBreakthrough, completedDefNames,
                    promotionKeys, monolithBaseline, monolithSnapshot);
            }
        }

        private static void SetRawState(
            DiaryGameComponent component,
            int schemaVersion,
            bool firstBreakthrough,
            List<string> completedDefNames,
            List<string> promotionKeys,
            string monolithBaseline,
            AnomalyMonolithKnowledgeState monolithSnapshot)
        {
            SchemaVersionField.SetValue(component, schemaVersion);
            FirstBreakthroughField.SetValue(component, firstBreakthrough);
            CompletedDefNamesField.SetValue(component, completedDefNames);
            PromotionKeysField.SetValue(component, promotionKeys);
            MonolithBaselineField.SetValue(component, monolithBaseline);
            MonolithSnapshotField.SetValue(component, monolithSnapshot);
        }

        private static void RemoveFixtureLetters()
        {
            if (Verse.Current.Game == null || originalLetters == null) return;
            LetterStack stack = Find.LetterStack;
            if (stack?.LettersListForReading == null) return;
            for (int i = stack.LettersListForReading.Count - 1; i >= 0; i--)
            {
                Letter letter = stack.LettersListForReading[i];
                if (!originalLetters.Contains(letter)) stack.RemoveLetter(letter);
            }
        }

        private static void RequireReflectionWiring()
        {
            PawnDiaryRimTestScope.Require(SchemaVersionField != null
                    && FirstBreakthroughField != null
                    && CompletedDefNamesField != null
                    && PromotionKeysField != null
                    && MonolithBaselineField != null
                    && MonolithSnapshotField != null
                    && ExposeAnomalyDataMethod != null,
                "Could not resolve the production Anomaly state/Scribe wiring.");
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Expected Anomaly study context fragment '" + fragment + "', got '"
                    + (diaryEvent?.gameContext ?? "<null>") + "'.");
        }

        private static bool RequireAnomalyOrSkip(string fixtureName)
        {
            if (ModsConfig.AnomalyActive) return true;
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Anomaly is not active in this test profile.");
            return false;
        }
    }
}
