// In-game thought-capture tests for Pawn Diary's MemoryThoughtHandler.TryGainMemory hook. A gained
// temporary memory flows: vanilla accepts it -> ThoughtGainPatch (Postfix on
// MemoryThoughtHandler.TryGainMemory(Thought_Memory, Pawn)) -> ThoughtSignal -> the shared Dispatch
// pipeline (guard + pure ThoughtEventData.Decide + dedup) -> DiaryGameComponent.AddSoloEvent. The
// signal stores the ThoughtDef's OWN defName on the DiaryEvent (payload.DefName, e.g.
// "NewColonyOptimism"), NOT the "thoughtPositive" group name — verified in ThoughtSignal.Emit, which
// calls AddSoloEvent(pawn, null, payload.DefName, ...). All isolation/teardown lives in the shared
// PawnDiaryRimTestScope harness; each test only fires a real vanilla trigger and asserts the
// persisted DiaryEvent.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-03 Thought immediate/ambient. This suite proves
// the solo page for a vanilla-accepted configured memory (with its mood/context facts), the
// below-threshold negative gate (a thought Pawn Diary is configured to ignore), and that gaining the
// same thought twice inside the dedup window does not double the diary event.
//
// Determinism notes:
//   - NewColonyOptimism is a plain non-social Thought_Memory (Core Thoughts_Memory_Misc.xml) with
//     durationDays=8 and a single stage of baseMoodEffect=+10. +10 clears the Thought policy's
//     minMoodOffset (5) with a 2x margin and, being > MoodImpact.MeaningfulThreshold (0.5), classifies
//     as "positive" — so the positive route needs no randomness or state injection.
//   - The below-threshold case raises the Thought policy's minMoodOffset above +10 (restored in
//     cleanup) so the magnitude gate deterministically drops the same thought.
//   - The duplicate case removes the gained vanilla memory between the two gains so vanilla ACCEPTS the
//     re-gain (the postfix fires with a valid memory); the second diary event is then dropped by Pawn
//     Diary's own transient dedup key ("thought|<pawnId>|NewColonyOptimism"), not by vanilla stacking.
using System;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a real vanilla temporary-memory gain reaches Pawn Diary's persisted event store as
    /// one solo page carrying the thought's defName and mood facts, that a thought below the configured
    /// magnitude threshold is dropped, and that a duplicate gain inside the dedup window does not
    /// double. Requires a loaded game because the production capture pipeline intentionally ignores
    /// events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryThoughtFlowTests
    {
        // The Thought-domain group's XML defName (DiaryInteractionGroupDefs.xml). Enabling it in SetUp
        // forces the user-enable gate on: IsThoughtEnabled classifies NewColonyOptimism into this group
        // (it is listed in its matchDefNames) and checks that the group is enabled.
        private const string ThoughtGroupKey = "thoughtPositive";

        // A reliably-loaded core Thought_Memory: non-social, durationDays=8, single +10 stage, and
        // listed under thoughtPositive.matchDefNames. RequireDef fails loudly if the base game did not
        // load it, so the test never silently passes against a missing trigger.
        private const string ThoughtDefName = "NewColonyOptimism";

        // The Thought signal policy Def whose minMoodOffset the below-threshold case raises. Its fields
        // are public and read live by DiarySignalPolicies.ForKey, so setting one is a valid, restorable
        // determinism lever (TEST_COVERAGE_PLAN.md §3 guidance).
        private const string ThoughtPolicyDefName = "DiarySignalPolicy_Thought";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a fresh test scope with the positive-thought group enabled and creates one isolated
        /// adult colonist (generation disabled, so no thought capture can ever reach an LLM request).
        /// Registers removal of any gained test memory so no memory lingers on the pawn before teardown.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(ThoughtGroupKey);
            pawn = scope.CreateAdultColonist();

            // Every case gains the same memory; strip it before the harness destroys the pawn so the
            // gained thought never outlives the test (the pawn Vanish would drop it anyway, but this
            // keeps the mutation explicitly reversed per rule 6). Resolved lazily so a missing Def does
            // not throw in cleanup.
            scope.RegisterCleanup(() => RemoveThoughtMemories(pawn, ThoughtDefName));
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived —
        /// even when the test above threw partway through.
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
            }
        }

        /// <summary>
        /// EVT-03. Gains a vanilla-accepted configured memory and verifies the TryGainMemory Harmony
        /// listener records one solo diary page carrying the thought's own defName plus its mood-impact
        /// and mood-offset context facts.
        /// </summary>
        [Test]
        public static void ConfiguredThoughtCreatesSoloEvent()
        {
            ThoughtDef thoughtDef = RequireDef<ThoughtDef>(ThoughtDefName);
            PawnDiaryRimTestScope.Require(
                thoughtDef.durationDays > 0f,
                "Test precondition: " + ThoughtDefName + " must be an expiring memory (durationDays > 0).");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GainThought(pawn, thoughtDef),
                ThoughtDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);

            // The thought was a positive memory (+10 stage), so both the persisted moodImpact token and
            // the parsed "mood_impact=" context field must read "positive".
            PawnDiaryRimTestScope.Require(
                string.Equals(diaryEvent.moodImpact, MoodImpact.Positive, StringComparison.OrdinalIgnoreCase),
                "The thought event's persisted moodImpact was not the expected positive direction.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(
                        "thought=" + ThoughtDefName, StringComparison.OrdinalIgnoreCase) >= 0,
                "The thought event context did not identify the gained ThoughtDef.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(
                        "mood_impact=" + MoodImpact.Positive, StringComparison.OrdinalIgnoreCase) >= 0,
                "The thought event context did not carry the positive mood-impact fact.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("mood_offset=", StringComparison.OrdinalIgnoreCase) >= 0,
                "The thought event context did not carry the mood-offset fact.");
        }

        /// <summary>
        /// EVT-03. Raises the Thought policy's magnitude threshold above the memory's mood offset and
        /// verifies the production capture path ignores it: a thought Pawn Diary is configured not to
        /// record (its mood change is below the bar) creates no diary page for the test pawn.
        /// </summary>
        [Test]
        public static void BelowThresholdThoughtIsIgnored()
        {
            ThoughtDef thoughtDef = RequireDef<ThoughtDef>(ThoughtDefName);
            DiarySignalPolicyDef policy = RequireDef<DiarySignalPolicyDef>(ThoughtPolicyDefName);

            // Register the restore BEFORE mutating so a failing assertion cannot strand the raised
            // threshold on the shared Def. The policy field is read live at signal construction via
            // DiarySignalPolicies.ForKey, so raising it here drops the very next gain at Decide step 4.
            float originalMinMoodOffset = policy.minMoodOffset;
            scope.RegisterCleanup(() => policy.minMoodOffset = originalMinMoodOffset);
            policy.minMoodOffset = 100000f;

            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
        }

        /// <summary>
        /// EVT-03. Gains the same configured memory twice inside the dedup window and verifies it does
        /// not double: the first gain records one solo page, and the second — re-accepted by vanilla
        /// after the gained memory is stripped — is dropped by Pawn Diary's transient dedup key.
        /// </summary>
        [Test]
        public static void DuplicateThoughtDoesNotDouble()
        {
            ThoughtDef thoughtDef = RequireDef<ThoughtDef>(ThoughtDefName);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GainThought(pawn, thoughtDef),
                ThoughtDefName,
                pawn,
                null);
            scope.RequireSoloRef(diaryEvent, pawn);

            // Strip the vanilla memory the first gain left on the pawn so vanilla accepts a fresh re-gain
            // (its postfix then fires with a real, non-null memory). Ticks do not advance in a test, so
            // the re-gain lands well inside the Thought dedup window and Pawn Diary drops the duplicate
            // by its "thought|<pawnId>|<defName>" key — this isolates the mod's dedup from vanilla's
            // memory stacking.
            RemoveThoughtMemories(pawn, ThoughtDefName);

            scope.RequireNoNewEvent(() => GainThought(pawn, thoughtDef));
        }

        /// <summary>
        /// Fires the real vanilla trigger: adds a temporary memory of <paramref name="thoughtDef"/> to
        /// the pawn, exactly as the mod's own debug action does. The ThoughtGainPatch postfix observes
        /// this call and submits a <see cref="ThoughtSignal"/> for accepted memories.
        /// </summary>
        private static void GainThought(Pawn subject, ThoughtDef thoughtDef)
        {
            // Mirror the three-argument form the mod's own debug action uses
            // (PawnDiaryDebugActions.cs) so this binds to the same TryGainMemory(ThoughtDef, Pawn, ...)
            // overload that already compiles against this game's Assembly-CSharp.
            subject.needs.mood.thoughts.memories.TryGainMemory(thoughtDef, null, null);
        }

        /// <summary>
        /// Removes every memory of the named ThoughtDef from the pawn. Guarded on the whole chain so it
        /// is a no-op if the pawn/needs/Def is missing (e.g. a positive assertion threw before the gain).
        /// </summary>
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
