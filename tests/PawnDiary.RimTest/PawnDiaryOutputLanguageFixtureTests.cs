// Loaded-game fixture for the B1 output-language directive (design/QUALITY_WAVE_IMPLEMENTATION_PLAN.md §3.1).
//
// Every LLM request must end its SYSTEM prompt with one localized line naming the language the model has
// to write in — including English. Without it a small instruction-following model infers the output
// language from the prompt's own wording, which is how a Russian install could still receive English
// diary pages.
//
// The line is resolved by the impure adapter (DiaryPipelineAdapters.OutputLanguageDirective) on the main
// thread, frozen onto DiaryPromptRequest.outputLanguageDirective, and appended by the pure planner
// (DiaryPromptPlanner.AppendOutputLanguageDirective) as the very last line of whatever ComposeSystem
// produced. LlmClient — which runs on a background thread, where .Translate() is unsafe — never resolves
// it at all.
//
// APPROACH: this is the half a pure test cannot reach — the live LanguageDatabase, the real Keyed lookup,
// and the loaded DiaryTuningDef. So the suite drives the internal adapter + planner directly on
// constructed events (the same seam PawnDiaryPromptPolicyFixtureTests uses) instead of firing gameplay,
// and never sends anything: building a plan does not enqueue a request.
//
// The expected directive is DERIVED from the active language at runtime rather than hardcoded, so the
// same assertions hold whichever localization the tester runs — an English run proves the English case
// and a Russian run proves the non-English case. The EN/RU Keyed parity of the frame itself is pinned
// deterministically by the pure suite (DiaryPipelineTests.TestOutputLanguageDirectiveReachesSystemPrompt).
//
// The only mutation is the loaded Diary_Tuning def's outputLanguageDirectiveEnabled flag, snapshotted and
// restored through failure-safe cleanup.
using System;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the output-language directive reaches the built plan's system prompt for the active
    /// language, names that language natively, appears exactly once, is carried by the Title template
    /// too, and disappears entirely when the XML toggle is off. Requires a loaded game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryOutputLanguageFixtureTests
    {
        private static PawnDiaryRimTestScope scope;

        // DiaryTuning.Current returns the loaded Diary_Tuning def (or a shared fallback); either way it is
        // the live object the adapter reads, so the toggle test mutates it and puts it back.
        private static DiaryTuningDef tuning;
        private static bool originalDirectiveEnabled;

        /// <summary>
        /// Opens a bare scope (no groups, no pawn: nothing here fires gameplay) and snapshots the tuning
        /// toggle so a mid-test failure still restores it.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            tuning = DiaryTuning.Current;
            originalDirectiveEnabled = tuning.outputLanguageDirectiveEnabled;
            scope.RegisterCleanup(() => tuning.outputLanguageDirectiveEnabled = originalDirectiveEnabled);
        }

        /// <summary>Restores every mutation through the harness and audits for leaks.</summary>
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
                originalDirectiveEnabled = false;
            }
        }

        /// <summary>
        /// The adapter resolves the active language's own name for itself and the planner puts the
        /// resulting sentence on the LAST line of the system prompt, exactly once, without disturbing the
        /// composed prompt above it and without leaking into the user prompt.
        /// </summary>
        [Test]
        public static void ActiveLanguageDirectiveIsTheLastLineOfTheSystemPrompt()
        {
            string expected = ExpectedDirective();
            DiaryPromptRequest request = BuildRequest(SoloEvent(), titleRequest: false);

            PawnDiaryRimTestScope.Require(
                string.Equals(request.outputLanguageDirective, expected, StringComparison.Ordinal),
                "The adapter resolved a different directive than the active language implies.\n"
                + "Expected: [" + expected + "]\nActual:   [" + (request.outputLanguageDirective ?? "<null>") + "]");

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            PawnDiaryRimTestScope.Require(
                plan.systemPrompt.EndsWith("\n" + expected, StringComparison.Ordinal),
                "The directive is not the last line of the system prompt.\nSystem prompt:\n" + plan.systemPrompt);
            PawnDiaryRimTestScope.Require(
                CountOccurrences(plan.systemPrompt, expected) == 1,
                "The directive must appear exactly once, but appeared "
                + CountOccurrences(plan.systemPrompt, expected) + " time(s).");
            PawnDiaryRimTestScope.Require(
                plan.userPrompt.IndexOf(expected, StringComparison.Ordinal) < 0,
                "The directive leaked into the user prompt; it belongs to the system prompt only.");
        }

        /// <summary>
        /// The directive names the active language the way that language names itself ("Русский", not
        /// "Russian"), and the frame is a real translation rather than an unresolved key or a TODO
        /// placeholder in the language the tester is running.
        /// </summary>
        [Test]
        public static void DirectiveNamesTheActiveLanguageNatively()
        {
            LoadedLanguage language = LanguageDatabase.activeLanguage;
            PawnDiaryRimTestScope.Require(
                language != null, "A loaded game must have an active language.");

            string directive = BuildRequest(SoloEvent(), titleRequest: false).outputLanguageDirective;
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(directive),
                "The active language produced no directive at all.");
            PawnDiaryRimTestScope.Require(
                directive.IndexOf(language.FriendlyNameNative, StringComparison.Ordinal) >= 0,
                "The directive does not name the active language natively ('"
                + language.FriendlyNameNative + "'): [" + directive + "]");

            // An untranslated key comes back as the key itself; a stub translation is literally "TODO".
            PawnDiaryRimTestScope.Require(
                directive.IndexOf("PawnDiary.Prompt.OutputLanguage", StringComparison.Ordinal) < 0
                    && directive.IndexOf("TODO", StringComparison.Ordinal) < 0,
                "The output-language frame is missing or is a placeholder in the active language: ["
                + directive + "]");

            // The {0} slot must have been consumed by the splice, never shipped to the model as-is.
            PawnDiaryRimTestScope.Require(
                directive.IndexOf("{0}", StringComparison.Ordinal) < 0,
                "The directive still contains an unfilled {0} slot: [" + directive + "]");
        }

        /// <summary>
        /// A page and its title must never disagree about language, so the Title template carries the
        /// directive too even though it opts out of persona text entirely.
        /// </summary>
        [Test]
        public static void TitleRequestCarriesTheSameDirective()
        {
            string expected = ExpectedDirective();
            DiaryEvent diaryEvent = SoloEvent();
            diaryEvent.initiatorGeneratedText = "The generator finally turned over.";

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(BuildRequest(diaryEvent, titleRequest: true));

            PawnDiaryRimTestScope.Require(
                string.Equals(plan.templateKey, DiaryPipelineTemplates.Title, StringComparison.Ordinal),
                "Expected the Title template, got '" + plan.templateKey + "'.");
            PawnDiaryRimTestScope.Require(
                plan.systemPrompt.EndsWith("\n" + expected, StringComparison.Ordinal),
                "The title system prompt does not end with the directive.\nSystem prompt:\n" + plan.systemPrompt);
            PawnDiaryRimTestScope.Require(
                CountOccurrences(plan.systemPrompt, expected) == 1,
                "The title system prompt must carry the directive exactly once.");
        }

        /// <summary>
        /// Turning the XML toggle off restores the old implicit behavior byte-for-byte: the adapter
        /// resolves nothing and the planner leaves the composed system prompt untouched, with no dangling
        /// newline where the directive used to be.
        /// </summary>
        [Test]
        public static void DisabledTuningToggleRemovesTheDirectiveEntirely()
        {
            string withDirective = DiaryPromptPlanner
                .Build(BuildRequest(SoloEvent(), titleRequest: false)).systemPrompt;

            tuning.outputLanguageDirectiveEnabled = false;

            DiaryPromptRequest request = BuildRequest(SoloEvent(), titleRequest: false);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(request.outputLanguageDirective),
                "The disabled toggle still resolved a directive: [" + request.outputLanguageDirective + "]");

            string withoutDirective = DiaryPromptPlanner.Build(request).systemPrompt;
            PawnDiaryRimTestScope.Require(
                withoutDirective.IndexOf(ExpectedDirective(), StringComparison.Ordinal) < 0,
                "The directive survived the disabled toggle.\nSystem prompt:\n" + withoutDirective);
            PawnDiaryRimTestScope.Require(
                string.Equals(withDirective, withoutDirective + "\n" + ExpectedDirective(), StringComparison.Ordinal),
                "Disabling the toggle changed more than the trailing directive line.\n"
                + "Enabled:\n" + withDirective + "\nDisabled:\n" + withoutDirective);
        }

        // ----- helpers ----------------------------------------------------------------------------------

        // Rebuilds what the adapter should produce, from the same live inputs it reads: the Keyed frame
        // resolved arg-free (so the grammar resolver cannot sentence-case the spliced name) and the active
        // language's own name for itself.
        private static string ExpectedDirective()
        {
            LoadedLanguage language = LanguageDatabase.activeLanguage;
            PawnDiaryRimTestScope.Require(
                language != null, "A loaded game must have an active language.");
            return "PawnDiary.Prompt.OutputLanguage".Translate().Resolve()
                .Replace("{0}", language.FriendlyNameNative.Trim());
        }

        // A minimal solo interaction event. Nothing is registered with the component, so there is no state
        // to clean up and the harness's no-leak audit stays quiet.
        private static DiaryEvent SoloEvent()
        {
            return new DiaryEvent
            {
                eventId = "pawndiary-test-output-language",
                interactionDefName = "DeepTalk",
                interactionLabel = "deep talk",
                gameContext = string.Empty,
                solo = true,
                initiatorPawnId = "pawndiary-test-pawn",
                initiatorName = "Alice",
                initiatorText = "Alice sat with the generator until it caught."
            };
        }

        private static DiaryPromptRequest BuildRequest(DiaryEvent diaryEvent, bool titleRequest)
        {
            return DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                string.Empty,   // personaRule
                string.Empty,   // psychotypeRule
                string.Empty,   // promptEnchantment
                string.Empty,   // humorCue
                null,           // priorInitiatorEntry
                titleRequest ? diaryEvent.DisplayTextForRole(DiaryEvent.InitiatorRole) : null,
                titleRequest,
                0);             // maxTokens
        }

        private static int CountOccurrences(string text, string fragment)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fragment))
            {
                return 0;
            }

            int count = 0;
            int index = text.IndexOf(fragment, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
