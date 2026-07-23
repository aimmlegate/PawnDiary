// Prompt-capture fixture for Pawn Diary's context-detail presets (design/TEST_COVERAGE_PLAN.md §4.3).
//
// This suite exercises the harness prompt-capture seam end to end: with Prompt Test Mode enabled the
// production generation pipeline resolves the template, renders the real system + user prompt, stamps
// it on the event, and STOPS before any network call. A fired event on a generating colonist therefore
// yields the exact prompt the runtime would have sent — captured with zero LLM requests.
//
// Scope note: this is the seam-validation slice of §4.3. It proves the Full and Compact render paths
// both produce a real prompt through the live resolver. The exhaustive field-omission and
// Full>=Balanced>=Compact trim assertions (which need per-field, per-template codegraph verification)
// are authored in the companion policy/template fixtures.
using System;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the prompt-capture harness path renders a real prompt for a solo event under the Full and
    /// Compact context-detail presets, without sending any LLM request. Requires a loaded game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryContextDetailFixtureTests
    {
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";
        private const string InspirationReason = "Struck by sudden RimTest inspiration";

        // Every rendered template opens with a multi-sentence system prompt, so any real capture clears
        // this floor comfortably; it guards against an empty/whitespace capture masquerading as success.
        private const int SubstantialPromptChars = 100;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a scope with the inspiration group enabled, turns on prompt capture at the Full preset,
        /// and creates one generating colonist. Prompt capture is enabled BEFORE the generating pawn is
        /// created, so even the founding-arrival hook is captured prompt-only and can never send a request.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey);
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateGeneratingAdultColonist();
        }

        /// <summary>
        /// Restores DevMode/promptTestMode/contextDetailLevel (via the harness) and audits for leaks.
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
        /// §4.3. Under the Full preset, a fired solo event renders and stores a substantial prompt, and
        /// the POV is marked prompt-only (no LLM request left the game).
        /// </summary>
        [Test]
        public static void FullPresetCapturesSubstantialPrompt()
        {
            string prompt = FireInspirationAndCapture(PromptContextDetailLevel.Full);
            PawnDiaryRimTestScope.Require(
                prompt.Length >= SubstantialPromptChars,
                "The Full-preset captured prompt was unexpectedly short (" + prompt.Length + " chars).");
        }

        /// <summary>
        /// §4.3. The Compact preset still renders a real prompt through the same resolver: mandatory
        /// context is always kept, so a captured Compact prompt is never empty.
        /// </summary>
        [Test]
        public static void CompactPresetStillCapturesPrompt()
        {
            string prompt = FireInspirationAndCapture(PromptContextDetailLevel.Compact);
            PawnDiaryRimTestScope.Require(
                prompt.Length >= SubstantialPromptChars,
                "The Compact-preset captured prompt was unexpectedly short (" + prompt.Length + " chars).");
        }

        // Fires one real inspiration on the generating pawn at the given preset and returns the prompt the
        // runtime rendered and stored for the initiator POV. The started inspiration is ended in cleanup.
        private static string FireInspirationAndCapture(PromptContextDetailLevel level)
        {
            InspirationDef inspirationDef = RequireDef<InspirationDef>(InspirationDefName);
            PawnDiaryMod.Settings.contextDetailLevel = level;
            scope.RegisterCleanup(() => EndInspirationSafely(pawn, inspirationDef));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = pawn.mindState.inspirationHandler.TryStartInspiration(
                        inspirationDef,
                        InspirationReason,
                        // The third arg is sendLetter, not a force flag: keep it false so a real
                        // inspiration letter never lands in the player's game. The diary event is captured
                        // by the TryStartInspiration postfix regardless of the letter.
                        false);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the inspiration.");
                },
                InspirationDefName,
                pawn,
                null);

            return scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
        }

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
