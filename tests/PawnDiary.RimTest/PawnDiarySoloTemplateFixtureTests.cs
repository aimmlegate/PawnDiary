// Prompt-only template-selection fixture for Pawn Diary's SOLO prompt shapes
// (design/TEST_COVERAGE_PLAN.md §4.2 — SoloDefault, SoloImportant, SoloInternalState, SoloBatched).
//
// This suite asserts the single most load-bearing thing about the solo prompt matrix: which template
// key the production planner picks for a given solo event, and the token rule that key carries. The
// selection logic lives in DiaryPromptPlanner.TemplateKeyFor, which reads three signals — the event's
// context markers (internal-state / batch), whether a group matched (missing group => important
// fallback), and whether the group is combat — and routes to exactly one solo template.
//
// APPROACH (reported in the schema `gotchas`): this uses the sanctioned BuildPromptRequest fallback,
// NOT the fire-a-vanilla-event + scope.CapturedPrompt path. Driving a *real* solo event to each of the
// four branches deterministically is impractical: SoloDefault needs a non-important group, SoloImportant
// needs a MISSING group, and SoloBatched needs a synthetic `batch=` context with a non-combat group —
// none of which a single vanilla trigger produces on demand. Instead each test constructs a solo
// DiaryEvent with a controlled gameContext, runs the exact production adapter
// (DiaryPipelineAdapters.BuildPromptRequest) so the real XML templates + token caps are resolved, then
// steers the importance/combat/missing-group inputs on the returned request before running the pure
// renderer (DiaryPromptPlanner.Build) and asserting the resulting plan. BuildPromptRequest + Build are
// pure builders: they render and return a plan object and NEVER enqueue an LlmClient request, so no
// prompt can leave the game even though generation is conceptually "on". Content is asserted with
// injected sentinel values (language-independent) rather than translated field labels; the template
// key and integer token caps are language-independent by construction.
//
// The scope is opened purely for the loaded-game precondition, RNG isolation, and the no-leak audit;
// no pawns are created and no settings/Defs are mutated, so teardown is trivially clean.
using System;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves DiaryPromptPlanner routes a solo event to the correct §4.2 template: an internal-state
    /// marker selects SoloInternalState (even over the important fallback), a non-combat batch marker
    /// selects SoloBatched while a combat batch escalates to SoloImportant, a missing group falls back
    /// to SoloImportant with its 200-token cap, and a matched non-important group yields the uncapped
    /// SoloDefault. Uses the pure BuildPromptRequest/Build builders, so nothing is ever enqueued.
    /// </summary>
    [TestSuite]
    public static class PawnDiarySoloTemplateFixtureTests
    {
        // The important/combat solo shapes share one raised response cap (1.6/Defs/DiaryPromptTemplateDefs.xml:
        // SoloImportant maxTokens=200). SoloDefault/SoloInternalState/SoloBatched carry no override and
        // resolve to 0, meaning "use the player's normal cap" downstream.
        private const int ImportantMaxTokens = 200;
        private const int NoTemplateCap = 0;

        // Distinctive values injected into the constructed event so field inclusion can be asserted from
        // the rendered user prompt without depending on any translated field label.
        private const string PovTextSentinel = "PDTEST_POVTEXT_7f3a2c";
        private const string PawnSummarySentinel = "PDTEST_SUMMARY_7f3a2c";

        private static PawnDiaryRimTestScope scope;

        /// <summary>
        /// Opens a fresh scope for the loaded-game guard, RNG isolation, and the no-leak audit. This
        /// fixture builds prompts from constructed events through the pure pipeline, so it neither
        /// enables capture nor creates pawns.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
        }

        /// <summary>
        /// Restores RNG/group-setting snapshots and audits that no test-owned state survived — even when
        /// a test above threw partway through.
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
            }
        }

        /// <summary>
        /// §4.2 internal marker selection. Each of the five internal-state context markers
        /// (mood_event / thought / inspiration / work / hediff) routes a solo event to SoloInternalState,
        /// and it does so even when the group is MISSING (which would otherwise be the important
        /// fallback) — the internal-state branch is tested before importance. The injected "what
        /// happened" text is carried through to the rendered prompt.
        /// </summary>
        [Test]
        public static void InternalStateMarkerSelectsInternalStateTemplateOverImportant()
        {
            string[] internalMarkers = { "mood_event=", "thought=", "inspiration=", "work=", "hediff=" };
            for (int i = 0; i < internalMarkers.Length; i++)
            {
                string marker = internalMarkers[i];
                DiaryEvent diaryEvent = MakeSoloEvent(marker + "PDTestValue", "PawnDiaryTest_Internal_" + i);

                // Null the resolved group so, absent the marker, TemplateKeyFor would take the
                // important fallback. The internal-state marker must win regardless.
                DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request => request.policy.group = null);

                RequireTemplate(plan, DiaryPipelineTemplates.SoloInternalState,
                    "internal-state marker '" + marker + "' should select SoloInternalState");
                RequireContains(plan.userPrompt, PovTextSentinel,
                    "SoloInternalState should render the 'what happened' text for marker '" + marker + "'.");
                RequireMaxTokens(plan, NoTemplateCap,
                    "SoloInternalState carries no template token cap for marker '" + marker + "'.");
            }
        }

        /// <summary>
        /// §4.2 batch evidence + combat token rule. A non-combat solo event carrying a `batch=` context
        /// marker (and no internal-state marker) selects SoloBatched and renders its batched evidence
        /// through the "what happened" body; flipping the group to combat escalates the same event to the
        /// SoloImportant shape with the raised 200-token cap (the solo combat routing rule).
        /// </summary>
        [Test]
        public static void BatchMarkerSelectsBatchedTemplateAndCombatEscalatesToImportant()
        {
            DiaryEvent diaryEvent = MakeSoloEvent("batch=interaction", "PawnDiaryTest_Batched");

            // Non-combat batch => SoloBatched.
            DiaryPromptPlan batchedPlan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = false;
            });
            RequireTemplate(batchedPlan, DiaryPipelineTemplates.SoloBatched,
                "a non-combat batch marker should select SoloBatched.");
            RequireContains(batchedPlan.userPrompt, PovTextSentinel,
                "SoloBatched should render its batched evidence in the 'what happened' body.");
            RequireMaxTokens(batchedPlan, NoTemplateCap, "SoloBatched carries no template token cap.");

            // Combat batch => SoloImportant with the raised cap.
            DiaryPromptPlan combatPlan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = true;
            });
            RequireTemplate(combatPlan, DiaryPipelineTemplates.SoloImportant,
                "a combat batch should escalate to the SoloImportant shape.");
            RequireMaxTokens(combatPlan, ImportantMaxTokens,
                "the combat/important solo shape must carry the raised 200-token cap.");
        }

        /// <summary>
        /// §4.2 missing-group important fallback + important token rule. A solo event whose group did not
        /// resolve (policy.group == null), with no internal-state or batch marker, falls back to
        /// SoloImportant, carries the raised 200-token cap, and its shape differs from SoloDefault: it
        /// includes the "you" (PawnSummary) field and swaps in the important system prompt.
        /// </summary>
        [Test]
        public static void MissingGroupFallsBackToImportantTemplateWithRaisedTokenCap()
        {
            DiaryEvent diaryEvent = MakeSoloEvent("note=quiet", "PawnDiaryTest_MissingGroup");

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request => request.policy.group = null);

            RequireTemplate(plan, DiaryPipelineTemplates.SoloImportant,
                "a missing group with no marker should fall back to SoloImportant.");
            RequireMaxTokens(plan, ImportantMaxTokens,
                "the SoloImportant fallback must carry the raised 200-token cap.");
            RequireContains(plan.userPrompt, PawnSummarySentinel,
                "SoloImportant includes the 'you' (PawnSummary) field, so the injected summary renders.");

            // The important shape swaps the system prompt relative to the default shape; compare against a
            // SoloDefault plan for the same event (matched, non-important group) to prove they differ.
            DiaryPromptPlan defaultPlan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = false;
                request.policy.group.combat = false;
            });
            RequireTemplate(defaultPlan, DiaryPipelineTemplates.SoloDefault,
                "a matched non-important group should select SoloDefault.");
            PawnDiaryRimTestScope.Require(
                !string.Equals(plan.systemPrompt, defaultPlan.systemPrompt, StringComparison.Ordinal),
                "SoloImportant should render a different system prompt than SoloDefault.");
        }

        /// <summary>
        /// §4.2 SoloDefault shape. A matched, non-important, non-combat solo event with no internal-state
        /// or batch marker selects SoloDefault, which is uncapped (no template token override) and — unlike
        /// SoloImportant — omits the "you" (PawnSummary) field even when a summary is supplied.
        /// </summary>
        [Test]
        public static void NonImportantGroupSelectsDefaultTemplateWithoutPawnSummary()
        {
            DiaryEvent diaryEvent = MakeSoloEvent("note=quiet", "PawnDiaryTest_Default");

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = false;
                request.policy.group.combat = false;
            });

            RequireTemplate(plan, DiaryPipelineTemplates.SoloDefault,
                "a matched non-important group should select SoloDefault.");
            RequireMaxTokens(plan, NoTemplateCap,
                "SoloDefault carries no template token cap.");
            RequireContains(plan.userPrompt, PovTextSentinel,
                "SoloDefault should render the 'what happened' text.");
            RequireNotContains(plan.userPrompt, PawnSummarySentinel,
                "SoloDefault omits the PawnSummary field, so the injected summary must not render.");
        }

        // ----- builders + assertions --------------------------------------------------------------

        /// <summary>
        /// Builds an in-memory solo <see cref="DiaryEvent"/> with sentinel POV text/summary and the given
        /// context/defName. It is never submitted to the component, so it leaves no state to clean up.
        /// </summary>
        private static DiaryEvent MakeSoloEvent(string gameContext, string interactionDefName)
        {
            return new DiaryEvent
            {
                eventId = "PDTest_" + interactionDefName,
                tick = 123456,
                date = "1st of Aprimay, 5500",
                solo = true,
                interactionDefName = interactionDefName,
                interactionLabel = "test moment",
                gameContext = gameContext,
                instruction = "note the moment",
                initiatorPawnId = "PDTest_pawn_1",
                initiatorName = "PDTestColonist",
                initiatorText = PovTextSentinel,
                initiatorPawnSummary = PawnSummarySentinel,
                initiatorSurroundings = "PDTEST_SETTING",
                initiatorContinuity = "none",
                initiatorLastOpener = string.Empty,
                recipientPawnId = string.Empty,
                recipientName = string.Empty,
            };
        }

        /// <summary>
        /// Runs the production adapter to resolve real XML templates/token caps for the event, applies
        /// the caller's steering to the returned request (importance / combat / missing group), then runs
        /// the pure renderer. Neither call enqueues an LLM request.
        /// </summary>
        private static DiaryPromptPlan BuildInitiatorPlan(DiaryEvent diaryEvent, Action<DiaryPromptRequest> steer)
        {
            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                personaRule: string.Empty,
                psychotypeRule: string.Empty,
                promptEnchantment: string.Empty,
                humorCue: string.Empty,
                priorInitiatorEntry: string.Empty,
                entryText: string.Empty,
                titleRequest: false,
                maxTokens: 0,
                contextDetailLevel: PromptContextDetailLevel.Full);

            if (request?.policy?.group == null)
            {
                throw new AssertionException("BuildPromptRequest returned no policy group to steer.");
            }

            steer?.Invoke(request);

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            if (plan == null)
            {
                throw new AssertionException("DiaryPromptPlanner.Build returned no plan.");
            }

            return plan;
        }

        private static void RequireTemplate(DiaryPromptPlan plan, string expectedKey, string context)
        {
            PawnDiaryRimTestScope.Require(
                string.Equals(plan.templateKey, expectedKey, StringComparison.Ordinal),
                "Expected template '" + expectedKey + "' but planner chose '" + plan.templateKey + "' (" + context + ").");
        }

        private static void RequireMaxTokens(DiaryPromptPlan plan, int expected, string context)
        {
            int actual = plan.responseRules == null ? -1 : plan.responseRules.maxTokens;
            PawnDiaryRimTestScope.Require(
                actual == expected,
                "Expected response max tokens " + expected + " but got " + actual + " (" + context + ").");
        }

        private static void RequireContains(string prompt, string expected, string message)
        {
            PawnDiaryRimTestScope.Require(
                prompt != null && prompt.IndexOf(expected, StringComparison.Ordinal) >= 0,
                message + " (expected substring '" + expected + "' in the rendered prompt).");
        }

        private static void RequireNotContains(string prompt, string unexpected, string message)
        {
            PawnDiaryRimTestScope.Require(
                prompt == null || prompt.IndexOf(unexpected, StringComparison.Ordinal) < 0,
                message + " (unexpected substring '" + unexpected + "' in the rendered prompt).");
        }
    }
}
