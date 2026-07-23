// Loaded-game fixtures for Narrative N4's unified rest-time reflection coordinator.
//
// These tests invoke the same private per-pawn arbitration seam used by the natural sleep scan. Prompt
// Test Mode keeps successful dispatches local and prompt-only, so the fixture never contacts an LLM.
// Exact hot/archive rows and saved reflection debt are inserted only for isolated test pawns, and the
// shared scope plus explicit archive cleanup removes every row afterward.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves upgrade baselining, hot/archive cross-arc dispatch, cooldown/priority consumption, and the
    /// Ideology-active versus DLC-off belief-reflection boundary through the loaded runtime adapter.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryNarrativeN4RestFlowTests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MethodInfo ArbitrateMethod = typeof(DiaryGameComponent).GetMethod(
            "ArbitrateReflectionsForPawn",
            NonPublicInstance,
            null,
            new[] { typeof(Pawn) },
            null);
        private static readonly MethodInfo FindDiaryMethod = typeof(DiaryGameComponent).GetMethod(
            "FindDiary",
            NonPublicInstance,
            null,
            new[] { typeof(Pawn), typeof(bool) },
            null);
        private static readonly FieldInfo EventsField = typeof(DiaryGameComponent).GetField(
            "events", NonPublicInstance);
        private static readonly FieldInfo ArchiveField = typeof(DiaryGameComponent).GetField(
            "archive", NonPublicInstance);

        private static PawnDiaryRimTestScope scope;
        private static DiaryTuningDef tuning;
        private static bool savedDaySummaryEnabled;
        private static bool savedQuadrumReflectionEnabled;
        private static bool savedArcReflectionEnabled;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("reflection");
            RequireRuntimeSeams();
            SnapshotReflectionTuning();
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
                tuning = null;
            }
        }

        /// <summary>
        /// A pre-N4 save's default-true state consumes historical reflection and belief debt on its first
        /// rest without creating a page. Both the original N4.1 and additive linked-memory baselines settle.
        /// </summary>
        [Test]
        public static void OldSaveFirstRestSilentlyBaselinesAllNarrativeDebt()
        {
            Pawn pawn = scope.CreateAdultColonist();
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = new PawnReflectionState();
            reflection.QueueMajorArc(Find.TickManager.TicksGame, "rimtest-pre-upgrade-major");
            diary.reflectionState = reflection;

            PawnBeliefState belief = diary.EnsureBeliefState();
            belief.baselineOnNextScan = false;
            belief.hasLastObservation = true;
            belief.lastIdeologyId = "rimtest-old-ideology";
            belief.lastScanTick = Find.TickManager.TicksGame;
            belief.pendingIdeologyChange = true;
            belief.pendingPreviousIdeologyId = "rimtest-old-ideology";
            belief.pendingCurrentIdeologyId = "rimtest-new-ideology";

            bool dispatched = true;
            scope.RequireNoNewEvent(() => dispatched = Arbitrate(pawn));

            PawnDiaryRimTestScope.Require(!dispatched,
                "The first rest after an N4 upgrade must baseline silently.");
            PawnDiaryRimTestScope.Require(
                !reflection.baselineOnNextOpportunity
                    && !reflection.linkedBaselineOnNextOpportunity,
                "The first rest did not settle both additive reflection baselines.");
            PawnDiaryRimTestScope.Require(
                !reflection.pendingMajorArc
                    && !belief.pendingIdeologyChange
                    && reflection.lastCrossArcTick >= 0
                    && reflection.lastBeliefTick >= 0,
                "The silent baseline did not bound historical major/cross-arc/belief debt.");
        }

        /// <summary>
        /// One hot exact reference plus one compact archive-only exact reference reaches the real
        /// coordinator. Cross-arc outranks a simultaneously due belief opportunity, writes once, consumes
        /// only its own cooldown state, and an immediate second rest is blocked globally.
        /// </summary>
        [Test]
        public static void LinkedHotAndArchiveMemoriesWriteOnePrioritizedCrossArcReflection()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: true);
            scope.EnablePromptCapture();
            Pawn pawn = scope.CreateGeneratingAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            bool beliefDue = SeedPendingIdeologyChangeWhenAvailable(pawn, diary);
            string archiveEventId = InsertLinkedHotAndArchiveRows(pawn, diary);
            // The archive row has its own source id and therefore is not covered by hot-event cleanup.
            // Register the exact id before any assertion can fail instead of clearing the whole archive.
            scope.RegisterCleanup(() => ArchiveRepository().RemoveForEventIds(
                new HashSet<string>(new[] { archiveEventId }, StringComparer.OrdinalIgnoreCase)));

            bool dispatched = false;
            DiaryEvent page = scope.FireAndRequireEvent(
                () => dispatched = Arbitrate(pawn),
                ArcReflectionEventData.DefNameToken,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);

            PawnDiaryRimTestScope.Require(dispatched,
                "The qualified hot/archive cross-arc opportunity did not dispatch.");
            scope.RequireSoloRef(page, pawn);
            string context = page.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("cross_arc_reflection=true", StringComparison.OrdinalIgnoreCase) >= 0
                    && context.IndexOf("linked_memories=2", StringComparison.Ordinal) >= 0,
                "The dispatched page did not preserve its qualified cross-arc memory counts.");
            PawnDiaryRimTestScope.Require(
                reflection.lastCrossArcTick == Find.TickManager.TicksGame
                    && reflection.lastBeliefTick < 0,
                "Cross-arc priority did not consume only the selected reflection kind.");
            if (beliefDue)
            {
                PawnDiaryRimTestScope.Require(
                    diary.EnsureBeliefState().pendingIdeologyChange,
                    "A lower-priority belief opportunity was consumed by the cross-arc dispatch.");
            }

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "The global cooldown allowed a second reflection in the same rest.");
        }

        /// <summary>
        /// With Ideology active, one saved ideology change becomes one prompt-only belief reflection and
        /// is consumed only after success. With Ideology absent, the identical detached debt is silently
        /// bounded and creates no page, proving the optional-DLC no-op branch in the loaded adapter.
        /// </summary>
        [Test]
        public static void IdeologyChangeWritesOnceOrCleanlyNoOpsWithoutDlc()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: false);
            scope.EnablePromptCapture();
            Pawn pawn = scope.CreateGeneratingAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            bool hasLiveBelief = SeedPendingIdeologyChangeWhenAvailable(pawn, diary);
            PawnBeliefState belief = diary.EnsureBeliefState();

            if (!ModsConfig.IdeologyActive)
            {
                SeedDetachedBeliefDebtForDlcOffFixture(belief);
                bool dispatchedWithoutDlc = true;
                scope.RequireNoNewEvent(() => dispatchedWithoutDlc = Arbitrate(pawn));
                PawnDiaryRimTestScope.Require(
                    !dispatchedWithoutDlc
                        && !belief.pendingIdeologyChange
                        && belief.lastReflectionTick >= 0
                        && reflection.lastBeliefTick < 0,
                    "The no-Ideology branch did not silently bound belief debt without a page.");
                return;
            }

            PawnDiaryRimTestScope.Require(hasLiveBelief,
                "Ideology is active, but the isolated colonist had no projectable belief tracker.");
            bool dispatched = false;
            DiaryEvent page = scope.FireAndRequireEvent(
                () => dispatched = Arbitrate(pawn),
                BeliefReflectionEventData.DefNameToken,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);

            PawnDiaryRimTestScope.Require(
                dispatched
                    && !belief.pendingIdeologyChange
                    && reflection.lastBeliefTick == Find.TickManager.TicksGame,
                "A successful belief reflection did not consume exactly its saved ideology-change debt.");
            scope.RequireSoloRef(page, pawn);
            PawnDiaryRimTestScope.Require(
                (page.gameContext ?? string.Empty).IndexOf(
                    "belief_reflection=true", StringComparison.OrdinalIgnoreCase) >= 0,
                "The dispatched page was not marked as a belief reflection.");

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "The global cooldown allowed a second belief reflection in the same rest.");
        }

        private static PawnReflectionState AlreadyBaselined(PawnDiaryRecord diary)
        {
            PawnReflectionState reflection = diary.EnsureReflectionState();
            reflection.baselineOnNextOpportunity = false;
            reflection.linkedBaselineOnNextOpportunity = false;
            reflection.lastReflectionTick = -1;
            reflection.lastMajorArcTick = -1;
            reflection.lastCrossArcTick = -1;
            reflection.lastBeliefTick = -1;
            reflection.lastQuadrumTick = -1;
            reflection.lastDayTick = -1;
            reflection.ClearPendingMajorArc();
            return reflection;
        }

        private static bool SeedPendingIdeologyChangeWhenAvailable(
            Pawn pawn,
            PawnDiaryRecord diary)
        {
            BeliefTrackerObservation current;
            if (!DlcContext.TryCaptureBeliefTrackerObservation(pawn, out current)
                || current == null
                || !current.hasCurrent)
            {
                return false;
            }

            PawnBeliefState belief = diary.EnsureBeliefState();
            belief.baselineOnNextScan = false;
            belief.hasLastObservation = true;
            belief.lastIdeologyId = current.ideologyId;
            belief.lastIdeologyName = current.ideologyName;
            belief.lastCertainty = current.certainty;
            belief.lastScanTick = Find.TickManager.TicksGame;
            belief.lastReflectionTick = -1;
            belief.lastReflectionDay = -1;
            belief.lastReflectionQuadrum = -1;
            belief.reflectionsThisQuadrum = 0;
            belief.pendingIdeologyChange = true;
            belief.pendingPreviousIdeologyId = "rimtest-previous-ideology";
            belief.pendingPreviousIdeologyName = "Previous fixture belief";
            belief.pendingCurrentIdeologyId = current.ideologyId;
            belief.pendingCurrentIdeologyName = current.ideologyName;
            return true;
        }

        private static void SeedDetachedBeliefDebtForDlcOffFixture(PawnBeliefState belief)
        {
            belief.baselineOnNextScan = false;
            belief.hasLastObservation = true;
            belief.lastIdeologyId = "rimtest-inactive-current";
            belief.lastIdeologyName = "Inactive fixture belief";
            belief.lastScanTick = Find.TickManager.TicksGame;
            belief.pendingIdeologyChange = true;
            belief.pendingPreviousIdeologyId = "rimtest-inactive-previous";
            belief.pendingCurrentIdeologyId = "rimtest-inactive-current";
        }

        private static string InsertLinkedHotAndArchiveRows(Pawn pawn, PawnDiaryRecord diary)
        {
            string pawnId = pawn.GetUniqueLoadID();
            int nowTick = Find.TickManager.TicksGame;
            string unique = pawnId + "|" + nowTick;
            string hotEventId = "rimtest-n4-hot|" + unique;
            string archiveEventId = "rimtest-n4-archive|" + unique;
            const string arcKey = "journey|rimtest-n4-ship";
            const string subjectId = "rimtest-n4-ship";

            DiaryEvent hot = new DiaryEvent
            {
                eventId = hotEventId,
                tick = Math.Max(0, nowTick - 2),
                date = "RimTest earlier phase",
                interactionDefName = "RimTestNarrativePhase",
                interactionLabel = "earlier journey phase",
                solo = true,
                initiatorPawnId = pawnId,
                initiatorName = pawn.LabelShortCap,
                initiatorText = "The fixture journey began."
            };
            hot.ApplyNarrativeContext(DiaryEvent.InitiatorRole, new NarrativeContextBuildResult
            {
                evidence = new List<NarrativeEvidence>
                {
                    ExactJourneyEvidence(
                        hotEventId, pawnId, hot.tick, "departed", arcKey, subjectId)
                },
                selection = new NarrativeContextSelection()
            });
            EventRepository().Register(hot);
            diary.eventIds.Add(hotEventId);

            ArchivedDiaryEntry archived = new ArchivedDiaryEntry
            {
                eventId = archiveEventId,
                pawnId = pawnId,
                povRole = DiaryEvent.InitiatorRole,
                tick = Math.Max(0, nowTick - 1),
                date = "RimTest later phase",
                text = "The fixture journey reached its destination.",
                interactionDefName = "RimTestNarrativePhase",
                interactionLabel = "later journey phase",
                narrativeReferences = NarrativeStatePersistence.FromReferences(
                    new List<NarrativeReference>
                    {
                        new NarrativeReference
                        {
                            facet = NarrativeFacetTokens.JourneyChapter,
                            phase = "landed",
                            subjectKind = NarrativeSubjectKindTokens.Ship,
                            subjectId = subjectId,
                            arcKey = arcKey,
                            sourceEventId = archiveEventId,
                            sourceTick = Math.Max(0, nowTick - 1)
                        }
                    })
            };
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(archived),
                "The fixture could not insert its compact archive-only memory.");
            return archiveEventId;
        }

        private static NarrativeEvidence ExactJourneyEvidence(
            string eventId,
            string pawnId,
            int tick,
            string phase,
            string arcKey,
            string subjectId)
        {
            return new NarrativeEvidence
            {
                eventId = eventId,
                tick = tick,
                povPawnId = pawnId,
                facet = NarrativeFacetTokens.JourneyChapter,
                phase = phase,
                subjectKind = NarrativeSubjectKindTokens.Ship,
                subjectId = subjectId,
                arcKey = arcKey,
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = "rimtest",
                sourceDefName = "RimTestNarrativePhase"
            };
        }

        private static bool Arbitrate(Pawn pawn)
        {
            return (bool)ArbitrateMethod.Invoke(scope.Component, new object[] { pawn });
        }

        private static PawnDiaryRecord DiaryFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiaryMethod.Invoke(
                scope.Component, new object[] { pawn, true }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the isolated pawn's diary record.");
            }

            return diary;
        }

        private static DiaryEventRepository EventRepository()
        {
            return EventsField.GetValue(scope.Component) as DiaryEventRepository;
        }

        private static DiaryArchiveRepository ArchiveRepository()
        {
            return ArchiveField.GetValue(scope.Component) as DiaryArchiveRepository;
        }

        private static void RequireRuntimeSeams()
        {
            PawnDiaryRimTestScope.Require(
                ArbitrateMethod != null
                    && FindDiaryMethod != null
                    && EventsField != null
                    && ArchiveField != null,
                "Narrative N4 RimTest could not resolve the private runtime seams it exercises.");
            PawnDiaryRimTestScope.Require(
                EventRepository() != null && ArchiveRepository() != null,
                "Narrative N4 RimTest could not resolve the loaded event/archive repositories.");
        }

        private static void SnapshotReflectionTuning()
        {
            tuning = DiaryTuning.Current;
            savedDaySummaryEnabled = tuning.daySummaryEnabled;
            savedQuadrumReflectionEnabled = tuning.quadrumReflectionEnabled;
            savedArcReflectionEnabled = tuning.arcReflectionEnabled;
            scope.RegisterCleanup(() =>
            {
                tuning.daySummaryEnabled = savedDaySummaryEnabled;
                tuning.quadrumReflectionEnabled = savedQuadrumReflectionEnabled;
                tuning.arcReflectionEnabled = savedArcReflectionEnabled;
            });
        }

        private static void ConfigureReflectionKinds(bool day, bool quadrum, bool arc)
        {
            tuning.daySummaryEnabled = day;
            tuning.quadrumReflectionEnabled = quadrum;
            tuning.arcReflectionEnabled = arc;
        }
    }
}
