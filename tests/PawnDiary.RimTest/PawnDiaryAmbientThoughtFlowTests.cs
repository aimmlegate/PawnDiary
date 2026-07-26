// In-game ambient-thought tests for Pawn Diary's real MemoryThoughtHandler.TryGainMemory hook.
// A configured low-stakes memory flows through vanilla acceptance, ThoughtGainPatch, ThoughtSignal,
// the shared capture policy, and RecordAmbientThought. Unlike an important thought, it should wait in
// one pawn/day batch and create exactly one ThoughtAmbientDay page only after production flushes it.
//
// These tests use the core KindWordsMood memory because it is a plain, expiring +5 Thought_Memory and
// is listed in both thoughtPositive.matchDefNames and the Thought policy's ambientTokens. The fixture
// temporarily disables thought-source dedup so two real accepted gains can reach the same batch without
// advancing the loaded colony's clock; every policy field and gained memory is restored in teardown.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-03 Thought immediate/ambient.
using System;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves ambient thought accumulation, no-premature-page behavior, batch flush, same-day duplicate
    /// protection, below-minimum dropping, and the immediate-page fallback when ambient routing is off.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryAmbientThoughtFlowTests
    {
        private const string ThoughtGroupKey = "thoughtPositive";
        private const string ThoughtDefName = "KindWordsMood";
        private const string ThoughtPolicyDefName = "DiarySignalPolicy_Thought";
        private const string AmbientPolicyDefName = "DiarySignalPolicy_AmbientThought";
        private const string AmbientDiaryDefName = "ThoughtAmbientDay";

        private static readonly MethodInfo FlushAllAmbientThoughtNotesMethod =
            typeof(DiaryGameComponent).GetMethod(
                "FlushAllAmbientThoughtNotes",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static DiarySignalPolicyDef ambientPolicy;

        /// <summary>
        /// Opens an isolated loaded-game scope, enables the positive-thought group, creates a disposable
        /// colonist, and pins the two signal policies to deterministic values for these batching tests.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(ThoughtGroupKey);
            pawn = scope.CreateAdultColonist();

            DiarySignalPolicyDef thoughtPolicy =
                RequireDef<DiarySignalPolicyDef>(ThoughtPolicyDefName);
            ambientPolicy = RequireDef<DiarySignalPolicyDef>(AmbientPolicyDefName);
            PawnDiaryRimTestScope.Require(
                FlushAllAmbientThoughtNotesMethod != null,
                "Could not resolve DiaryGameComponent.FlushAllAmbientThoughtNotes.");

            bool originalThoughtEnabled = thoughtPolicy.enabled;
            int originalDedupTicks = thoughtPolicy.dedupTicks;
            float originalMinMoodOffset = thoughtPolicy.minMoodOffset;
            bool originalAmbientEnabled = ambientPolicy.enabled;
            int originalMinEvents = ambientPolicy.ambientMinEventsToWrite;
            scope.RegisterCleanup(() =>
            {
                thoughtPolicy.enabled = originalThoughtEnabled;
                thoughtPolicy.dedupTicks = originalDedupTicks;
                thoughtPolicy.minMoodOffset = originalMinMoodOffset;
                ambientPolicy.enabled = originalAmbientEnabled;
                ambientPolicy.ambientMinEventsToWrite = originalMinEvents;
            });
            scope.RegisterCleanup(() => RemoveThoughtMemories(pawn, ThoughtDefName));

            // The loaded Def already uses these values except dedup=2500. Pinning all of them makes the
            // intent explicit and keeps XML overrides from turning a deterministic integration test flaky.
            thoughtPolicy.enabled = true;
            thoughtPolicy.dedupTicks = 0;
            thoughtPolicy.minMoodOffset = 0f;
            ambientPolicy.enabled = true;
            ambientPolicy.ambientMinEventsToWrite = 2;
        }

        /// <summary>
        /// Restores the signal policies and exact ambient-store baselines, removes the gained memories and
        /// fixture page, destroys the pawn, then runs the shared no-leak audit.
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
                pawn = null;
                ambientPolicy = null;
            }
        }

        /// <summary>
        /// EVT-03. Two real accepted memories accumulate without an immediate page; the production
        /// pre-save flush promotes them to one ThoughtAmbientDay page, and the written-day guard rejects
        /// another accepted memory for the same pawn/day.
        /// </summary>
        [Test]
        public static void AmbientThoughtBatchAccumulatesFlushesOnceAndGuardsTheDay()
        {
            ThoughtDef thoughtDef = RequireAmbientThoughtDef();

            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 1, expectedWrittenKeys: 0);

            // Remove the vanilla memory so the next TryGainMemory call is unquestionably accepted. The
            // production batch is independent of the pawn's live memory list and must retain moment one.
            RemoveThoughtMemories(pawn, ThoughtDefName);
            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 1, expectedWrittenKeys: 0);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                FlushAllAmbientThoughtNotes,
                AmbientDiaryDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    diaryEvent.moodImpact,
                    MoodImpact.Positive,
                    StringComparison.OrdinalIgnoreCase),
                "The ambient thought page did not retain the batch's positive mood direction.");
            PawnDiaryRimTestScope.Require(
                ContainsContext(diaryEvent, "thought=" + AmbientDiaryDefName)
                    && ContainsContext(diaryEvent, "batch=ambient_day_note")
                    && ContainsContext(diaryEvent, "events=2"),
                "The ambient thought page did not carry its synthetic source, batch kind, and count.");
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 0, expectedWrittenKeys: 1);

            // Dedup is disabled, so vanilla and the capture dispatcher both accept this third gain. Its
            // silence therefore proves RecordAmbientThought's written pawn/day guard, not source dedup.
            RemoveThoughtMemories(pawn, ThoughtDefName);
            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 0, expectedWrittenKeys: 1);
        }

        /// <summary>
        /// EVT-03. One real ambient memory is too thin for the configured two-event minimum; flushing
        /// removes the pending note without creating a page or marking the pawn/day as written.
        /// </summary>
        [Test]
        public static void BelowMinimumAmbientThoughtBatchIsDropped()
        {
            ThoughtDef thoughtDef = RequireAmbientThoughtDef();

            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 1, expectedWrittenKeys: 0);

            scope.RequireNoNewEvent(FlushAllAmbientThoughtNotes);
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 0, expectedWrittenKeys: 0);
        }

        /// <summary>
        /// EVT-03. Ambient routing is optional, not a master filter: disabling only that signal sends the
        /// same qualifying vanilla memory through the ordinary immediate Thought page path.
        /// </summary>
        [Test]
        public static void DisabledAmbientRoutingFallsBackToImmediateThoughtPage()
        {
            ThoughtDef thoughtDef = RequireAmbientThoughtDef();
            ambientPolicy.enabled = false;

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GainThought(pawn, thoughtDef),
                ThoughtDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                ContainsContext(diaryEvent, "thought=" + ThoughtDefName),
                "The ambient-disabled fallback page did not identify the original ThoughtDef.");
            scope.RequireAmbientThoughtState(pawn, expectedPendingKeys: 0, expectedWrittenKeys: 0);
        }

        /// <summary>Invokes the same private flush used immediately before Pawn Diary saves its state.</summary>
        private static void FlushAllAmbientThoughtNotes()
        {
            FlushAllAmbientThoughtNotesMethod.Invoke(scope.Component, null);
        }

        /// <summary>Fires RimWorld's real temporary-memory gain choke point observed by Harmony.</summary>
        private static void GainThought(Pawn subject, ThoughtDef thoughtDef)
        {
            subject.needs.mood.thoughts.memories.TryGainMemory(thoughtDef, null, null);
        }

        private static ThoughtDef RequireAmbientThoughtDef()
        {
            ThoughtDef thoughtDef = RequireDef<ThoughtDef>(ThoughtDefName);
            PawnDiaryRimTestScope.Require(
                thoughtDef.durationDays > 0f,
                "Test precondition: " + ThoughtDefName + " must be an expiring memory.");
            return thoughtDef;
        }

        private static bool ContainsContext(DiaryEvent diaryEvent, string value)
        {
            return diaryEvent?.gameContext != null
                && diaryEvent.gameContext.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Removes all fixture memories without assuming the positive test reached its trigger.</summary>
        private static void RemoveThoughtMemories(Pawn subject, string thoughtDefName)
        {
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            MemoryThoughtHandler memories = subject?.needs?.mood?.thoughts?.memories;
            if (thoughtDef != null && memories != null)
            {
                memories.RemoveMemoriesOfDef(thoughtDef);
            }
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }
    }
}
