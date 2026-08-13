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
using PawnDiary.Ingestion;
using RimWorld;
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
        private static readonly MethodInfo PrunePendingMajorMethod =
            typeof(DiaryGameComponent).GetMethod(
                "PrunePendingMajorReflectionsWithoutRestOwner",
                NonPublicInstance);
        private static readonly FieldInfo EventsField = typeof(DiaryGameComponent).GetField(
            "events", NonPublicInstance);
        private static readonly FieldInfo ArchiveField = typeof(DiaryGameComponent).GetField(
            "archive", NonPublicInstance);

        private static PawnDiaryRimTestScope scope;
        private static DiaryTuningDef tuning;
        private static bool savedDaySummaryEnabled;
        private static bool savedQuadrumReflectionEnabled;
        private static bool savedArcReflectionEnabled;
        private static int savedArcMinMemoriesPreferred;
        private static int savedArcMinMemoriesForced;
        private static int savedArcMemoryShortfallRetryTicks;
        private static int savedArcTerminalRetryMaxTicks;

        [BeforeEach]
        public static void SetUp()
        {
            // IsReflectionGroupEnabled resolves the Reflection-domain row that claims each kind, and
            // every kind now owns one: day/quadrum moved out of the Interaction domain and belief
            // split off for the Ideology color. The coordinator arbitrates across all of them.
            scope = PawnDiaryRimTestScope.Begin(
                "reflection", "dayreflection", "quadrumreflection", "reflectionBelief");
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
        /// A terminal-owned request that initially has no non-recap memory remains pending through the
        /// real rest coordinator, then writes once after a later memory satisfies the floor.
        /// </summary>
        [Test]
        public static void TerminalMajorRetriesAfterShortfallThenWritesWithoutRecappingOwner()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: true);
            scope.EnablePromptCapture();
            // A terminal major event is an opportunistic schedule entry unless the annual cadence
            // itself is forced, so this fixture must lower the preferred and forced floors together.
            tuning.arcReflectionMinMemoriesPreferred = 1;
            tuning.arcReflectionMinMemoriesForced = 1;
            tuning.arcReflectionMemoryShortfallRetryTicks = GenDate.TicksPerDay;
            tuning.arcReflectionTerminalRetryMaxTicks =
                GenDate.TicksPerDay * GenDate.DaysPerYear;

            Pawn pawn = scope.CreateGeneratingAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            string ownerId = "rimtest-terminal-owner|" + pawn.GetUniqueLoadID();
            InsertHotArcMemory(pawn, diary, ownerId, "The terminal chapter closed.");
            reflection.QueueMajorArc(Find.TickManager.TicksGame, ownerId);

            bool firstDispatch = true;
            scope.RequireNoNewEvent(() => firstDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(
                !firstDispatch
                    && reflection.pendingMajorArc
                    && reflection.pendingMajorArcAvoidEventId == ownerId
                    && diary.EnsureArcSchedule().lastArcMemoryShortfallTick
                        == Find.TickManager.TicksGame,
                "The first shortfall did not preserve and back off the terminal-owned request.");

            // Remove the one-day retry delay for this fixture instead of advancing the live game's clock.
            tuning.arcReflectionMemoryShortfallRetryTicks = 0;
            string supportingId = "rimtest-terminal-support|" + pawn.GetUniqueLoadID();
            InsertHotArcMemory(pawn, diary, supportingId, "A later memory gave the chapter perspective.");

            bool dispatched = false;
            DiaryEvent reflectionPage = scope.FireAndRequireEvent(
                () => dispatched = Arbitrate(pawn),
                ArcReflectionEventData.DefNameToken,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);

            PawnDiaryRimTestScope.Require(
                dispatched
                    && !reflection.pendingMajorArc
                    && diary.EnsureArcSchedule().recentlyUsedEventIds.Contains(supportingId)
                    && !diary.EnsureArcSchedule().recentlyUsedEventIds.Contains(ownerId),
                "The retried terminal request did not write once from only non-recap supporting memory.");
            scope.RequireSoloRef(reflectionPage, pawn);
            PawnDiaryRimTestScope.Require(
                (reflectionPage.gameContext ?? string.Empty).Contains("selected_memories=1"),
                "The retried terminal reflection did not report its one non-recap memory.");
        }

        /// <summary>
        /// A terminal-owned major arc skipped by frequency still consumes its selected supporting memory,
        /// closes the pending request, and advances cadence. The false return means “no page”, not “retry”.
        /// </summary>
        [Test]
        public static void FrequencyRejectedMajorArcSettlesPendingRequestWithoutPage()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: true);
            tuning.arcReflectionMinMemoriesPreferred = 1;
            tuning.arcReflectionMinMemoriesForced = 1;
            tuning.arcReflectionMemoryShortfallRetryTicks = 0;
            tuning.arcReflectionTerminalRetryMaxTicks =
                GenDate.TicksPerDay * GenDate.DaysPerYear;

            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            string ownerId = "rimtest-frequency-major-owner|" + pawn.GetUniqueLoadID();
            string supportingId = "rimtest-frequency-major-support|" + pawn.GetUniqueLoadID();
            InsertHotArcMemory(pawn, diary, ownerId, "The terminal chapter closed.");
            InsertHotArcMemory(pawn, diary, supportingId, "A separate memory put it in perspective.");
            reflection.QueueMajorArc(Find.TickManager.TicksGame, ownerId);
            ForceFrequencyRejection("reflection");
            int nowTick = Find.TickManager.TicksGame;

            bool dispatched = true;
            scope.RequireNoNewEvent(() => dispatched = Arbitrate(pawn));

            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            PawnDiaryRimTestScope.Require(!dispatched && !reflection.pendingMajorArc,
                "A frequency-rejected major arc was reported as a page or retained pending debt.");
            PawnDiaryRimTestScope.Require(
                schedule.recentlyUsedEventIds.Contains(supportingId)
                    && !schedule.recentlyUsedEventIds.Contains(ownerId)
                    && reflection.lastMajorArcTick == nowTick
                    && reflection.lastReflectionTick == nowTick,
                "The frequency-rejected major arc did not settle its selection and cadence exactly once.");

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "A settled major-arc frequency skip rerolled on the next rest opportunity.");
        }

        /// <summary>
        /// A terminal request blocked by the annual schedule is backed off instead of being discarded or
        /// re-evaluated on every 250-tick sleep scan.
        /// </summary>
        [Test]
        public static void TerminalMajorBlockedByScheduleRemainsBoundedAndBackedOff()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: true);
            tuning.arcReflectionMemoryShortfallRetryTicks = GenDate.TicksPerDay;
            tuning.arcReflectionTerminalRetryMaxTicks =
                GenDate.TicksPerDay * GenDate.DaysPerYear;

            // The year cap prevents dispatch before generation is considered, so this fixture does
            // not need prompt capture or a generation-enabled pawn.
            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            int currentYear = GenDate.Year(Find.TickManager.TicksAbs, 0f);
            schedule.lastArcEntryYear = currentYear;
            schedule.arcEntriesThisYear = Math.Max(1, tuning.arcReflectionMaxEntriesPerYear);
            string ownerId = "rimtest-schedule-blocked-terminal|" + pawn.GetUniqueLoadID();
            reflection.QueueMajorArc(Find.TickManager.TicksGame, ownerId);

            bool dispatched = true;
            scope.RequireNoNewEvent(() => dispatched = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(
                !dispatched
                    && reflection.pendingMajorArc
                    && reflection.pendingMajorArcAvoidEventId == ownerId
                    && schedule.lastArcMemoryShortfallTick == Find.TickManager.TicksGame,
                "A schedule-blocked terminal request was cleared or failed to enter bounded backoff.");
        }

        /// <summary>
        /// If a queued owner dies, leaves, or otherwise disappears from the map-rest snapshot, the saved
        /// request is removed before it can keep an otherwise-disabled full-colony scan alive forever.
        /// </summary>
        [Test]
        public static void MissingRestOwnerPrunesSavedTerminalDebt()
        {
            Pawn pawn = scope.CreateAdultColonist();
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            reflection.QueueMajorArc(
                Find.TickManager.TicksGame,
                "rimtest-missing-rest-owner|" + pawn.GetUniqueLoadID());

            PrunePendingMajorMethod.Invoke(
                scope.Component,
                new object[] { new List<Pawn>() });

            PawnDiaryRimTestScope.Require(
                !reflection.pendingMajorArc,
                "A diary without a map-rest owner retained permanent terminal reflection debt.");
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
        /// A frequency skip settles the one reflection selected by priority without writing a page or
        /// falling through to a lower-priority belief opportunity. The selected cross-arc cadence closes
        /// exactly as it does after a page, so a second rest cannot reroll the same linked memories.
        /// </summary>
        [Test]
        public static void FrequencyRejectedCrossArcSettlesWithoutLowerPriorityFallthrough()
        {
            ConfigureReflectionKinds(day: false, quadrum: false, arc: true);
            Pawn pawn = scope.CreateAdultColonist();
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            bool beliefDue = SeedPendingIdeologyChangeWhenAvailable(pawn, diary);
            string archiveEventId = InsertLinkedHotAndArchiveRows(pawn, diary);
            scope.RegisterCleanup(() => ArchiveRepository().RemoveForEventIds(
                new HashSet<string>(new[] { archiveEventId }, StringComparer.OrdinalIgnoreCase)));
            ForceFrequencyRejection("reflection");
            int nowTick = Find.TickManager.TicksGame;

            bool dispatched = true;
            scope.RequireNoNewEvent(() => dispatched = Arbitrate(pawn));

            PawnDiaryRimTestScope.Require(!dispatched,
                "A frequency-rejected cross-arc reflection was reported as a written page.");
            PawnDiaryRimTestScope.Require(
                reflection.lastCrossArcTick == nowTick
                    && reflection.lastReflectionTick == nowTick,
                "The frequency-rejected cross-arc opportunity did not settle its kind/global cadence.");
            if (beliefDue)
            {
                PawnBeliefState belief = diary.EnsureBeliefState();
                PawnDiaryRimTestScope.Require(
                    belief.pendingIdeologyChange && reflection.lastBeliefTick < 0,
                    "Frequency rejection fell through and consumed the lower-priority belief opportunity.");
            }

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "A settled cross-arc frequency skip rerolled on the next rest opportunity.");
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
            // The resolved belief block itself is guaranteed: the candidate refuses to dispatch without
            // one. It is what the reflection is actually about, so assert it unconditionally.
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(page.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The dispatched belief reflection did not freeze its resolved belief context.");
            // The N3-I lens on top of it is optional live doctrine, not a property of this path: the
            // adapter only freezes a narrative fact when the pawn's own ideoligion resolved a
            // high-confidence precept interpretation, which a random colony ideoligion need not have.
            // PawnDiaryIdeologyPhase1FixtureTests owns the deterministic source-precept coverage; here
            // the contract is only that whatever the page did freeze reaches the captured prompt.
            string narrativeContext = page.NarrativeContextForRole(DiaryEvent.InitiatorRole);
            string prompt = scope.CapturedPrompt(page, DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(narrativeContext)
                    || prompt.IndexOf(narrativeContext, StringComparison.Ordinal) >= 0,
                "The saved belief-reflection narrative fact did not reach the captured prompt.");

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "The global cooldown allowed a second belief reflection in the same rest.");
        }

        /// <summary>
        /// With Ideology active, frequency rejection settles the selected belief debt and both cadence
        /// rows without creating a page. DLC-off behavior remains covered by the existing no-op fixture.
        /// </summary>
        [Test]
        public static void FrequencyRejectedBeliefReflectionSettlesDebtWithoutPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return;
            }

            ConfigureReflectionKinds(day: false, quadrum: false, arc: false);
            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRecord diary = DiaryFor(pawn);
            PawnReflectionState reflection = AlreadyBaselined(diary);
            PawnDiaryRimTestScope.Require(
                SeedPendingIdeologyChangeWhenAvailable(pawn, diary),
                "Ideology is active, but the fixture pawn had no projectable belief tracker.");
            PawnBeliefState belief = diary.EnsureBeliefState();
            ForceFrequencyRejection("reflectionBelief");
            int nowTick = Find.TickManager.TicksGame;

            bool dispatched = true;
            scope.RequireNoNewEvent(() => dispatched = Arbitrate(pawn));

            PawnDiaryRimTestScope.Require(
                !dispatched
                    && !belief.pendingIdeologyChange
                    && belief.lastReflectionTick == nowTick
                    && reflection.lastBeliefTick == nowTick
                    && reflection.lastReflectionTick == nowTick,
                "The frequency-rejected belief opportunity did not settle its debt and cadence without a page.");

            bool repeatedDispatch = true;
            scope.RequireNoNewEvent(() => repeatedDispatch = Arbitrate(pawn));
            PawnDiaryRimTestScope.Require(!repeatedDispatch,
                "A settled belief frequency skip rerolled on the next rest opportunity.");
        }

        /// <summary>
        /// The quiet policy has already used the belief group's multiplier and deterministic roll before
        /// arbitration, so its accepted signal bypasses a later central 0x check. A stronger non-quiet
        /// trigger does not carry that marker and remains subject to normal central admission.
        /// </summary>
        [Test]
        public static void QuietAdmissionBypassesCentralFrequencyButNonQuietStillUsesIt()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return;
            }

            Pawn quietPawn = scope.CreateAdultColonist();
            Pawn nonQuietPawn = scope.CreateAdultColonist();
            ForceFrequencyRejection(BeliefReflectionEventData.FrequencyGroupKey);
            BeliefReflectionSignal quietSignal = BeliefSignalForFrequencyContract(
                quietPawn,
                BeliefReflectionTriggerTokens.Quiet,
                frequencySettledUpstream: true);
            BeliefReflectionSignal nonQuietSignal = BeliefSignalForFrequencyContract(
                nonQuietPawn,
                BeliefReflectionTriggerTokens.IdeologyChange,
                frequencySettledUpstream: false);

            CaptureContext quietContext = quietSignal.BuildContext();
            CaptureContext nonQuietContext = nonQuietSignal.BuildContext();
            PawnDiaryRimTestScope.Require(
                quietContext.FrequencyGroupKey == BeliefReflectionEventData.FrequencyGroupKey
                    && nonQuietContext.FrequencyGroupKey == BeliefReflectionEventData.FrequencyGroupKey,
                "Belief reflection signals did not retain the exact reflectionBelief frequency owner.");
            PawnDiaryRimTestScope.Require(
                quietContext.BypassFrequency && !nonQuietContext.BypassFrequency,
                "The upstream-settled marker bypassed the wrong belief trigger.");

            DiaryDispatchOutcome quietOutcome = DiaryDispatchOutcome.Rejected;
            scope.FireAndRequireEvent(
                () => quietOutcome = scope.Component.DispatchWithOutcome(quietSignal),
                BeliefReflectionEventData.DefNameToken,
                quietPawn,
                null,
                rejectOtherTestPawnEvents: true);
            PawnDiaryRimTestScope.Require(quietOutcome == DiaryDispatchOutcome.Accepted,
                "An upstream-settled quiet reflection was sampled a second time by central admission.");

            DiaryDispatchOutcome nonQuietOutcome = DiaryDispatchOutcome.Rejected;
            scope.RequireNoNewEvent(() =>
                nonQuietOutcome = scope.Component.DispatchWithOutcome(nonQuietSignal));
            PawnDiaryRimTestScope.Require(
                nonQuietOutcome == DiaryDispatchOutcome.FrequencyRejected,
                "A non-quiet belief trigger bypassed its required central frequency admission.");
        }

        /// <summary>
        /// Belief cadence is persisted on the same TicksGame clock used by Normalize. Disabled debt
        /// advances that boundary without erasing the source ids that prevent repetition after re-enable.
        /// </summary>
        [Test]
        public static void BeliefCadenceUsesGameTickClockAndDisabledDebtKeepsDedupHistory()
        {
            int nowTick = Find.TickManager.TicksGame;
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            PawnBeliefState belief = new PawnBeliefState();
            belief.MarkReflection(
                nowTick,
                new List<string> { "rimtest-belief-source" },
                new BeliefStanceResolution(),
                policy);

            int expectedGameDay = nowTick / GenDate.TicksPerDay;
            PawnDiaryRimTestScope.Require(
                belief.lastReflectionTick == nowTick
                    && belief.lastReflectionDay == expectedGameDay
                    && belief.lastReflectionQuadrum == expectedGameDay / GenDate.DaysPerQuadrum
                    && belief.reflectionsThisQuadrum == 1,
                "Belief reflection cadence was not recorded in TicksGame-derived day units.");

            belief.Normalize(nowTick, policy);
            PawnDiaryRimTestScope.Require(
                belief.lastReflectionTick == nowTick && belief.reflectionsThisQuadrum == 1,
                "Normalize erased a valid belief cooldown/quadrum counter after MarkReflection.");

            belief.pendingIdeologyChange = true;
            belief.pendingPreviousIdeologyId = "rimtest-belief-before";
            belief.pendingCurrentIdeologyId = "rimtest-belief-after";
            belief.AdvanceDisabledReflectionDebt(nowTick, policy);
            PawnDiaryRimTestScope.Require(
                !belief.pendingIdeologyChange
                    && belief.lastReflectedSourceIds.Contains("rimtest-belief-source"),
                "Disabled belief debt did not clear pending work while preserving source dedup history.");
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

            DiaryEvent archiveSource = new DiaryEvent
            {
                eventId = archiveEventId,
                tick = Math.Max(0, nowTick - 1),
                date = "RimTest later phase",
                interactionDefName = "RimTestNarrativePhase",
                interactionLabel = "later journey phase",
                solo = true,
                initiatorPawnId = pawnId,
                initiatorName = pawn.LabelShortCap,
                initiatorText = "The fixture journey reached its destination."
            };
            archiveSource.ApplyNarrativeContext(DiaryEvent.InitiatorRole, new NarrativeContextBuildResult
            {
                evidence = new List<NarrativeEvidence>
                {
                    ExactJourneyEvidence(
                        archiveEventId,
                        pawnId,
                        archiveSource.tick,
                        "landed",
                        arcKey,
                        subjectId)
                },
                selection = new NarrativeContextSelection()
            });

            // Exercise the production compaction constructor, then remove the source from the hot store
            // exactly as retention does. The cross-arc collector must recover its references only from
            // the compact row and the archive's transient narrative index.
            EventRepository().Register(archiveSource);
            diary.eventIds.Add(archiveEventId);
            DiaryEntryView archiveView = archiveSource.ToViewFor(pawnId, archivedForScans: true);
            ArchivedDiaryEntry archived = ArchivedDiaryEntry.FromEvent(
                archiveSource,
                pawnId,
                archiveView,
                forceFallback: false);
            PawnDiaryRimTestScope.Require(
                archived != null
                    && archived.narrativeReferences != null
                    && archived.narrativeReferences.Count > 0,
                "The real compaction constructor dropped the source page's exact references.");
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(archived),
                "The fixture could not insert its compact archive-only memory.");
            diary.eventIds.Remove(archiveEventId);
            EventRepository().RemoveEvent(archiveEventId);

            PawnDiaryRimTestScope.Require(
                EventRepository().FindEvent(archiveEventId) == null
                    && ArchiveRepository().NarrativeEntriesForPawn(pawnId).Count > 0,
                "The fixture did not become archive-only through the indexed compaction path.");
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

        private static void InsertHotArcMemory(
            Pawn pawn,
            PawnDiaryRecord diary,
            string eventId,
            string text)
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = eventId,
                tick = Find.TickManager.TicksGame,
                date = "RimTest terminal retry",
                interactionDefName = "RimTestArcMemory",
                interactionLabel = "terminal retry memory",
                solo = true,
                initiatorPawnId = pawn.GetUniqueLoadID(),
                initiatorName = pawn.LabelShortCap,
                initiatorText = text
            };
            EventRepository().Register(diaryEvent);
            diary.eventIds.Add(eventId);
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
                    && PrunePendingMajorMethod != null
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
            savedArcMinMemoriesPreferred = tuning.arcReflectionMinMemoriesPreferred;
            savedArcMinMemoriesForced = tuning.arcReflectionMinMemoriesForced;
            savedArcMemoryShortfallRetryTicks = tuning.arcReflectionMemoryShortfallRetryTicks;
            savedArcTerminalRetryMaxTicks = tuning.arcReflectionTerminalRetryMaxTicks;
            scope.RegisterCleanup(() =>
            {
                tuning.daySummaryEnabled = savedDaySummaryEnabled;
                tuning.quadrumReflectionEnabled = savedQuadrumReflectionEnabled;
                tuning.arcReflectionEnabled = savedArcReflectionEnabled;
                tuning.arcReflectionMinMemoriesPreferred = savedArcMinMemoriesPreferred;
                tuning.arcReflectionMinMemoriesForced = savedArcMinMemoriesForced;
                tuning.arcReflectionMemoryShortfallRetryTicks =
                    savedArcMemoryShortfallRetryTicks;
                tuning.arcReflectionTerminalRetryMaxTicks =
                    savedArcTerminalRetryMaxTicks;
            });
        }

        private static void ConfigureReflectionKinds(bool day, bool quadrum, bool arc)
        {
            tuning.daySummaryEnabled = day;
            tuning.quadrumReflectionEnabled = quadrum;
            tuning.arcReflectionEnabled = arc;
        }

        private static void ForceFrequencyRejection(string groupKey)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (group == null || settings?.groupFrequencyOverrides == null)
            {
                throw new AssertionException(
                    "Could not configure the reflection frequency-rejection fixture for '"
                    + groupKey + "'.");
            }

            float savedValue;
            bool hadSavedValue = settings.groupFrequencyOverrides.TryGetValue(
                group.defName, out savedValue);
            settings.groupFrequencyOverrides[group.defName] = 0f;
            scope.RegisterCleanup(() =>
            {
                if (hadSavedValue)
                {
                    settings.groupFrequencyOverrides[group.defName] = savedValue;
                }
                else
                {
                    settings.groupFrequencyOverrides.Remove(group.defName);
                }
            });
        }

        private static BeliefReflectionSignal BeliefSignalForFrequencyContract(
            Pawn pawn,
            string trigger,
            bool frequencySettledUpstream)
        {
            int nowTick = Find.TickManager.TicksGame;
            BeliefReflectionEventData data = new BeliefReflectionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = nowTick,
                DefName = BeliefReflectionEventData.DefNameToken,
                Trigger = trigger,
                HasBeliefContext = true,
                AlreadyWritten = false
            };
            BeliefContextBuildResult prepared = new BeliefContextBuildResult
            {
                evaluated = true,
                fullContext = "belief_fixture=true",
                resolution = new BeliefStanceResolution()
            };
            return new BeliefReflectionSignal(
                data,
                pawn,
                "belief reflection fixture",
                "A private belief reflection.",
                "Write the prepared belief reflection.",
                BeliefReflectionEventData.BuildGameContext(trigger),
                prepared,
                frequencySettledUpstream);
        }
    }
}
