// In-game inspiration-capture tests for Pawn Diary's InspirationHandler.TryStartInspiration hook.
// A newly started pawn inspiration flows: vanilla accepts it -> InspirationStartPatch (Postfix on
// InspirationHandler.TryStartInspiration) -> InspirationSignal -> DiaryGameComponent.AddSoloEvent.
// The signal stores the InspirationDef's own defName on the DiaryEvent (payload.DefName, e.g.
// "Inspired_Creativity"), NOT the "inspiration" group name — verified in InspirationSignal.Emit.
// All isolation/teardown lives in the shared PawnDiaryRimTestScope harness; each test only fires a
// real vanilla trigger and asserts the persisted DiaryEvent.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-05 Inspiration. This suite proves the solo page
// with reason/context, the disabled-group negative gate, and that ending the inspiration in cleanup
// leaves no test state behind (the harness no-leak audit is the machine check).
using System;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a real vanilla inspiration start reaches Pawn Diary's persisted event store as one
    /// solo page, that disabling the inspiration group drops it, and that ending the inspiration during
    /// cleanup leaves the colony free of test state. Requires a loaded game because the production
    /// capture pipeline intentionally ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryInspirationFlowTests
    {
        // The inspiration domain group's XML defName (DiaryInteractionGroupDefs.xml). Enabling it in
        // SetUp forces the user-enable gate on for the positive case; the disabled test flips it off.
        private const string InspirationGroupKey = "inspiration";

        // A reliably-loaded core InspirationDef. RequireDef fails loudly if the base game did not load
        // it, so the test never silently passes against a missing trigger.
        private const string InspirationDefName = "Inspired_Creativity";

        // A plain-ASCII reason so DiaryLineCleaner leaves it intact and it round-trips into the
        // game-context "reason=" field.
        private const string InspirationReason = "Struck by sudden RimTest inspiration";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a fresh test scope with the inspiration group enabled and creates one isolated adult
        /// colonist (generation disabled, so no inspiration capture can ever reach an LLM request).
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey);
            pawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation (including the inspiration each test starts, ended below via
        /// RegisterCleanup) and audits that no test-owned event, diary, or log row survived.
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
        /// EVT-05. Starts a real vanilla inspiration and verifies the InspirationHandler Harmony listener
        /// records one solo diary page carrying the inspiration's own defName plus the reason/context.
        /// </summary>
        [Test]
        public static void InspirationStartCreatesSoloEvent()
        {
            InspirationDef inspirationDef = RequireDef<InspirationDef>(InspirationDefName);

            // End the inspiration while the pawn is still alive (custom cleanups run before pawn destroy),
            // so no live inspiration lingers and the harness no-leak audit runs against a clean pawn.
            scope.RegisterCleanup(() => EndInspirationSafely(pawn, inspirationDef));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = pawn.mindState.inspirationHandler.TryStartInspiration(
                        inspirationDef,
                        InspirationReason,
                        true);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the forced inspiration.");
                },
                InspirationDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(
                        "inspiration=" + InspirationDefName, StringComparison.OrdinalIgnoreCase) >= 0,
                "The inspiration event context did not identify the started InspirationDef.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("reason=", StringComparison.OrdinalIgnoreCase) >= 0,
                "The inspiration event context did not surface the vanilla start reason.");
        }

        /// <summary>
        /// EVT-05. Disables the inspiration group and verifies the production capture path drops the
        /// event: a started inspiration with its group off records no diary page for the test pawn.
        /// </summary>
        [Test]
        public static void DisabledInspirationGroupDropsEvent()
        {
            InspirationDef inspirationDef = RequireDef<InspirationDef>(InspirationDefName);

            // Turn the group off for this test. The harness snapshots group settings at Begin() and
            // fully restores them in teardown, so this override never leaks past the test.
            PawnDiaryMod.Settings.SetGroupEnabled(InspirationGroupKey, false);

            // The inspiration still starts in vanilla (only the diary capture is gated), so it must be
            // ended in cleanup exactly like the positive case.
            scope.RegisterCleanup(() => EndInspirationSafely(pawn, inspirationDef));

            scope.RequireNoNewEvent(() =>
            {
                bool started = pawn.mindState.inspirationHandler.TryStartInspiration(
                    inspirationDef,
                    InspirationReason,
                    true);
                PawnDiaryRimTestScope.Require(
                    started, "Vanilla refused to start the forced inspiration.");
            });
        }

        /// <summary>
        /// Ends the pawn's active inspiration if one is present, matching the def the test started.
        /// Guarded on <see cref="InspirationHandler.Inspired"/> so ending is a no-op when nothing is
        /// active (e.g. the positive assertion threw before the inspiration took hold).
        /// </summary>
        private static void EndInspirationSafely(Pawn subject, InspirationDef inspirationDef)
        {
            InspirationHandler handler = subject?.mindState?.inspirationHandler;
            if (handler != null && inspirationDef != null && handler.Inspired)
            {
                handler.EndInspiration(inspirationDef);
            }
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required vanilla " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }
    }
}
