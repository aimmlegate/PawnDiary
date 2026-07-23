// In-game mood-event tests for Pawn Diary's mood-affecting GameCondition source (EVT-14,
// design/TEST_COVERAGE_PLAN.md §3).
//
// The production trigger is GameConditionManager.RegisterCondition -> GameConditionStartPatch ->
// DiaryEvents.Submit(new MoodEventFanoutSignal(cond)). The fan-out is colony-wide: its PerPawnSignals()
// walks Condition.AffectedMaps -> map.mapPawns.FreeColonists and yields one MoodEventPawnSignal per
// eligible spawned colonist, each of which becomes a solo diary page (see MoodEventSignal.cs). That map
// iteration needs SPAWNED colonists on real maps, which the mapless harness does not build, so — exactly
// like the Work suite drives WorkSignal per pawn rather than the whole periodic scan — these tests drive
// the fan-out's per-unit work directly:
//
//   var fanout = new MoodEventFanoutSignal(condition);          // condition-level capture + user gate
//   DiaryEvents.Submit(new MoodEventPawnSignal(fanout, pawn, pawnId));  // the exact per-colonist unit
//
// This keeps every test deterministic and mapless. The conditions are fabricated GameConditionDefs the
// test fully owns (never added to the DefDatabase; the mood path only reads condition.def), so no vanilla
// or DLC content is required and no ThoughtDef references them — which pins the numeric mood offset to 0
// and lets each test choose the classification through the XML-tuned known-condition lists
// (DiaryTuning.Current.positive/negativeMoodConditionDefNames, snapshotted + restored via RegisterCleanup)
// instead of looping until a random roll. The one place the REAL colony fan-out signal is driven is the
// disabled-group test, which asserts the fan-out's constructor gate (an empty ColonyDedupKey) — the exact
// point where a disabled mood group stops every colonist from getting a page.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-14 Mood event — affected-map fan-out (one page per
// colonist), mood classification (positive / negative / gender-unaffected neutral), unaffected exclusion
// (the per-pawn gender gate stands in for the map exclusion in this mapless harness), and disabled group.
// These tests require a loaded game because the capture pipeline ignores events at the main menu; they
// never enable per-pawn generation, so no LLM request can leave the game.
using System;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a mood-affecting GameCondition, sampled through the real MoodEventPawnSignal capture
    /// path, records one solo mood page per colonist carrying the right mood classification and context;
    /// that a colonist unaffected by a gender-targeted condition is classified neutral; and that a mood
    /// group toggled off invalidates the whole fan-out so no colonist gets a page.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryMoodConditionFlowTests
    {
        // Every fabricated condition defName classifies (by name) to the MoodEvent catch-all group, so
        // this is the single group the suite enables/disables. Enabling it in SetUp turns on the
        // per-def user gate the fan-out constructor checks (IsMoodEventEnabled).
        private const string MoodGroupKey = "moodeventOther";

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh test scope with the mood catch-all group enabled and creates two isolated adult
        /// colonists (generation disabled, so no mood capture can ever reach an LLM request).
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(MoodGroupKey);
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation (including the temporary known-condition list entries each test adds)
        /// and audits that no test-owned event, diary, or dedup key survived — even when a test above
        /// threw partway through.
        /// </summary>
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
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// EVT-14. A positive mood condition fans out one solo page per affected colonist: driving the
        /// per-colonist unit for both test pawns records two distinct solo pages, each classified positive
        /// with the mood-event context marker the UI parses.
        /// </summary>
        [Test]
        public static void PositiveMoodConditionFansOutOnePagePerColonist()
        {
            const string ConditionDefName = "PawnDiaryTest_MoodPositive";
            ForcePositiveClassification(ConditionDefName);
            MoodEventFanoutSignal fanout = MakeFanout(ConditionDefName, "test aurora");

            RequireColonistMoodPage(fanout, ConditionDefName, firstPawn, MoodImpact.Positive);
            RequireColonistMoodPage(fanout, ConditionDefName, secondPawn, MoodImpact.Positive);
        }

        /// <summary>
        /// EVT-14. A negative mood condition classifies the affected colonist's page as negative and
        /// carries the negative direction in both the saved DiaryEvent.moodImpact field and the context.
        /// </summary>
        [Test]
        public static void NegativeMoodConditionClassifiesNegative()
        {
            const string ConditionDefName = "PawnDiaryTest_MoodNegative";
            ForceNegativeClassification(ConditionDefName);
            MoodEventFanoutSignal fanout = MakeFanout(ConditionDefName, "test eclipse");

            RequireColonistMoodPage(fanout, ConditionDefName, firstPawn, MoodImpact.Negative);
        }

        /// <summary>
        /// EVT-14. Unaffected-colonist exclusion. A gender-targeted condition
        /// (GameCondition_PsychicEmanation) leaves a colonist of the other gender unaffected: the mood
        /// classifier returns neutral before any known-condition list is consulted. In the real fan-out a
        /// colonist not on an affected map is skipped entirely; the mapless harness cannot place colonists
        /// on distinct maps, so this exercises the analogous per-pawn "this colonist is not affected" gate,
        /// which is the only unaffected-exclusion branch reachable without a spawned multi-map colony.
        /// </summary>
        [Test]
        public static void GenderUnaffectedColonistClassifiesNeutral()
        {
            const string ConditionDefName = "PawnDiaryTest_MoodGenderTargeted";
            GameConditionDef conditionDef = MakeConditionDef(ConditionDefName, "test psychic drone");

            // Target the OPPOSITE gender to the pawn, so DetermineMoodImpact's gender check short-circuits
            // to neutral. No known-list forcing is needed: the gender gate returns before the fallbacks.
            GameCondition_PsychicEmanation condition = new GameCondition_PsychicEmanation
            {
                def = conditionDef,
                gender = OppositeGender(firstPawn.gender),
            };
            MoodEventFanoutSignal fanout = new MoodEventFanoutSignal(condition);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(fanout.ColonyDedupKey),
                "A gender-targeted mood condition should still build a valid fan-out when the group is on.");

            RequireColonistMoodPage(fanout, ConditionDefName, firstPawn, MoodImpact.Neutral);
        }

        /// <summary>
        /// EVT-14. Disabled group. Toggling the classifying mood group off makes the fan-out constructor's
        /// user gate fail, so the fan-out is invalid (empty ColonyDedupKey) and submitting the real colony
        /// signal fans out to no colonist — no diary page for any test pawn. The enabled fan-out built
        /// first proves the toggle, not some unrelated condition, is what invalidates capture.
        /// </summary>
        [Test]
        public static void DisabledMoodGroupCapturesNothing()
        {
            MoodEventFanoutSignal enabledFanout = MakeFanout("PawnDiaryTest_MoodEnabled", "test aurora");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(enabledFanout.ColonyDedupKey),
                "With the mood group enabled the fan-out should be valid (non-empty colony dedup key).");

            // Turn the classifying group off. The harness snapshots group settings at Begin() and fully
            // restores them in teardown, so this override never leaks past the test.
            PawnDiaryMod.Settings.SetGroupEnabled(MoodGroupKey, false);

            MoodEventFanoutSignal disabledFanout = MakeFanout("PawnDiaryTest_MoodDisabled", "test aurora");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(disabledFanout.ColonyDedupKey),
                "With the mood group disabled the fan-out should be invalid (empty colony dedup key).");

            // Driving the real colony fan-out with the group disabled must record nothing for any colonist.
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(disabledFanout));
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Submits the per-colonist mood unit the fan-out would yield for <paramref name="pawn"/> and
        /// asserts it recorded exactly one solo page classified as <paramref name="expectedImpact"/>, with
        /// the load-bearing "mood_event=" / "mood_impact=" context markers and the moodImpact field set.
        /// </summary>
        private static void RequireColonistMoodPage(
            MoodEventFanoutSignal fanout, string conditionDefName, Pawn pawn, string expectedImpact)
        {
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new MoodEventPawnSignal(fanout, pawn, pawn.GetUniqueLoadID())),
                conditionDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                string.Equals(diaryEvent.moodImpact, expectedImpact, StringComparison.Ordinal),
                "The mood event's moodImpact was not '" + expectedImpact + "'.");
            RequireContextContains(diaryEvent, "mood_event=" + conditionDefName);
            RequireContextContains(diaryEvent, "mood_impact=" + expectedImpact);
        }

        /// <summary>
        /// Builds a fabricated mood GameConditionDef the test fully controls and wraps it in a plain
        /// GameCondition, then constructs the production fan-out signal from it. The def is never added to
        /// the DefDatabase (the mood path only reads condition.def), so it needs no cleanup, and no
        /// ThoughtDef references it, so DetermineMoodImpact's measured offset is 0.
        /// </summary>
        private static MoodEventFanoutSignal MakeFanout(string defName, string label)
        {
            return new MoodEventFanoutSignal(new GameCondition { def = MakeConditionDef(defName, label) });
        }

        private static GameConditionDef MakeConditionDef(string defName, string label)
        {
            return new GameConditionDef { defName = defName, label = label };
        }

        /// <summary>
        /// Adds a condition defName to the XML-tuned always-positive list for this test, so the name-based
        /// fallback in DetermineMoodImpact classifies it positive (the fabricated def has no mood-thought
        /// offset). The entry is removed in teardown, restoring the developer's live tuning.
        /// </summary>
        private static void ForcePositiveClassification(string defName)
        {
            System.Collections.Generic.List<string> list = DiaryTuning.Current.positiveMoodConditionDefNames;
            list.Add(defName);
            scope.RegisterCleanup(() => list.Remove(defName));
        }

        /// <summary>Same as <see cref="ForcePositiveClassification"/> but for the always-negative list.</summary>
        private static void ForceNegativeClassification(string defName)
        {
            System.Collections.Generic.List<string> list = DiaryTuning.Current.negativeMoodConditionDefNames;
            list.Add(defName);
            scope.RegisterCleanup(() => list.Remove(defName));
        }

        private static Gender OppositeGender(Gender gender)
        {
            return gender == Gender.Female ? Gender.Male : Gender.Female;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The mood event context did not contain the expected fragment '" + expectedFragment + "'.");
        }
    }
}
