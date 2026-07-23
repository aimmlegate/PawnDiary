// Prompt-only template-selection fixture for Pawn Diary's PAIR prompt shapes
// (design/TEST_COVERAGE_PLAN.md §4.2 — PairDefault, PairImportant, PairCombat, PairBatched).
//
// This suite pins the load-bearing facts about the pairwise prompt matrix: which template key the
// production planner picks for a two-POV event, which structured fields each shape carries
// (relationship / weapon / hidden-initiator entry), whether the hidden-initiator direct-speech
// instruction is emitted, and the raised 200-token response cap the important/combat shapes drive.
// The selection logic lives in DiaryPromptPlanner.TemplateKeyFor: for a non-solo event it routes to
// PairCombat when the group is combat, else PairBatched when a `batch=` marker is present, else
// PairImportant vs PairDefault by group importance (so combat > batch > importance).
//
// APPROACH (reported in the schema `gotchas`): this uses the sanctioned BuildPromptRequest fallback,
// NOT the fire-a-vanilla-event + scope.CapturedPrompt path. Driving a real vanilla pair event to each
// of the four branches on demand is impractical: PairDefault needs a non-important group, PairCombat
// needs a combat group, and PairBatched needs a synthetic `batch=` context — no single trigger
// produces all four deterministically, and a freshly captured prompt omits empty optional fields
// (fresh isolated pawns have no relationship/weapon), so field inclusion could not be asserted from a
// live capture. Instead each test constructs a pair DiaryEvent with injected sentinel field values and
// a controlled gameContext, runs the exact production adapter (DiaryPipelineAdapters.BuildPromptRequest)
// so the REAL loaded XML templates + token caps + localized direct-speech text are resolved, steers the
// importance/combat/batch inputs on the returned request, then runs the pure renderer
// (DiaryPromptPlanner.Build) and asserts the returned plan. BuildPromptRequest + Build are pure
// builders: they render and return a plan object and NEVER enqueue an LlmClient request, so no prompt
// can leave the game. Field inclusion is asserted with injected sentinels (language-independent) rather
// than translated field labels; the template key and integer token caps are language-independent by
// construction. The direct-speech instruction is detected by its stable "[[speech]]" markup token.
//
// Recipient POV: the pair-recipient "hidden initiator entry" field only ever holds a value on the
// RECIPIENT plan (priorInitiatorEntry), so the "hidden initiator text only where allowed" contract is
// asserted by rendering the recipient plan directly through the pure builder. In the real fire +
// scope.CapturedPrompt path the recipient POV may not capture synchronously in prompt-test mode (it can
// await initiator completion); this fixture sidesteps that limitation by rendering the recipient plan
// through BuildPromptRequest instead of a captured event (see `gotchas`).
//
// The scope is opened purely for the loaded-game precondition, RNG isolation, and the no-leak audit; no
// pawns are created and no settings/Defs are mutated, so teardown is trivially clean.
using System;
using System.Collections.Generic;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves DiaryPromptPlanner routes a pair event to the correct §4.2 template and that each shape
    /// carries the right fields/tokens: a matched non-important group yields the uncapped PairDefault
    /// (relationship field, no weapon/summary/hidden-initiator); a matched important group yields
    /// PairImportant with the 200-token cap and the recipient hidden-initiator field; a combat group
    /// yields PairCombat with the weapon + "you" summary fields and the 200-token cap and wins over a
    /// batch marker; a non-combat batch marker yields the uncapped PairBatched, which drops the
    /// relationship and hidden-initiator fields; and the hidden-initiator direct-speech instruction is
    /// emitted only for real interaction shapes. Uses the pure BuildPromptRequest/Build builders, so
    /// nothing is ever enqueued.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPairTemplateFixtureTests
    {
        // The important/combat pair shapes share one raised response cap (1.6/Defs/DiaryPromptTemplateDefs.xml:
        // PairImportant/PairCombat maxTokens=200). PairDefault/PairBatched carry no override and resolve
        // to 0, meaning "use the player's normal cap" downstream.
        private const int ImportantMaxTokens = 200;
        private const int NoTemplateCap = 0;

        // Distinctive values injected into the constructed event so field inclusion can be asserted from
        // the rendered prompt without depending on any translated field label.
        private const string PovTextSentinel = "PDTEST_POVTEXT_9d1a";
        private const string RelationshipSentinel = "PDTEST_RELATIONSHIP_9d1a";
        private const string WeaponSentinel = "PDTEST_WEAPON_9d1a";
        private const string PawnSummarySentinel = "PDTEST_SUMMARY_9d1a";
        private const string HiddenInitiatorSentinel = "PDTEST_HIDDENINIT_9d1a";

        // Stable direct-speech markup token emitted by the hidden-initiator/direct-speech instruction
        // (PawnDiary.Prompt.PairDirectSpeechInstruction.*). Language-independent: the markup is literal.
        private const string DirectSpeechToken = "[[speech]]";

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
        /// §4.2 PairDefault shape. A matched, non-important, non-combat pair event with no batch marker
        /// selects PairDefault, which is uncapped and carries the relationship field but — unlike the
        /// important/combat shapes — none of the weapon, "you" (PawnSummary), or hidden-initiator fields.
        /// Because it is a real interaction shape, the initiator prompt still emits the direct-speech
        /// instruction.
        /// </summary>
        [Test]
        public static void NonImportantPairSelectsDefaultTemplateWithRelationshipOnly()
        {
            DiaryEvent diaryEvent = MakePairEvent(InteractionContext(), AnyInteractionDefName());

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = false;
                request.policy.group.combat = false;
            });

            RequireTemplate(plan, DiaryPipelineTemplates.PairDefault,
                "a matched non-important non-combat pair should select PairDefault.");
            RequireMaxTokens(plan, NoTemplateCap, "PairDefault carries no template token cap.");
            RequireContains(plan.userPrompt, PovTextSentinel,
                "PairDefault should render the 'what you saw' text.");
            RequireContains(plan.userPrompt, RelationshipSentinel,
                "PairDefault includes the relationship field, so the injected relationship must render.");
            RequireNotContains(plan.userPrompt, WeaponSentinel,
                "PairDefault has no weapon field, so the injected weapon must not render.");
            RequireNotContains(plan.userPrompt, PawnSummarySentinel,
                "PairDefault omits the 'you' (PawnSummary) field, so the injected summary must not render.");
            RequireContains(plan.userPrompt, DirectSpeechToken,
                "PairDefault is a social interaction shape, so its initiator prompt emits the direct-speech instruction.");

            // The recipient hidden-initiator field is not part of the default shape, so a supplied prior
            // initiator entry must NOT surface even on the recipient plan.
            DiaryPromptPlan recipientPlan = BuildRecipientPlan(diaryEvent, request =>
            {
                request.policy.group.important = false;
                request.policy.group.combat = false;
            });
            RequireTemplate(recipientPlan, DiaryPipelineTemplates.PairDefault,
                "the recipient plan of a non-important pair should also select PairDefault.");
            RequireNotContains(recipientPlan.userPrompt, HiddenInitiatorSentinel,
                "PairDefault has no hidden-initiator field, so the prior initiator entry must not render.");
        }

        /// <summary>
        /// §4.2 PairImportant shape + 200-token rule + hidden-initiator field. A matched important,
        /// non-combat pair with no batch marker selects PairImportant, carries the raised 200-token cap,
        /// swaps in a different system prompt than PairDefault, keeps the relationship field but not the
        /// weapon field, and — on the recipient plan — renders the hidden-initiator entry ("hidden
        /// initiator text only where allowed").
        /// </summary>
        [Test]
        public static void ImportantPairSelectsImportantTemplateWithRaisedCapAndHiddenInitiator()
        {
            DiaryEvent diaryEvent = MakePairEvent(InteractionContext(), AnyInteractionDefName());

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = false;
            });

            RequireTemplate(plan, DiaryPipelineTemplates.PairImportant,
                "a matched important non-combat pair should select PairImportant.");
            RequireMaxTokens(plan, ImportantMaxTokens,
                "the important pair shape must carry the raised 200-token cap.");
            RequireContains(plan.userPrompt, RelationshipSentinel,
                "PairImportant includes the relationship field.");
            RequireNotContains(plan.userPrompt, WeaponSentinel,
                "PairImportant has no weapon field (only PairCombat does).");
            RequireContains(plan.userPrompt, DirectSpeechToken,
                "PairImportant is a social interaction shape, so its initiator prompt emits the direct-speech instruction.");

            // The important shape swaps the system prompt relative to the default shape.
            DiaryPromptPlan defaultPlan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = false;
                request.policy.group.combat = false;
            });
            PawnDiaryRimTestScope.Require(
                !string.Equals(plan.systemPrompt, defaultPlan.systemPrompt, StringComparison.Ordinal),
                "PairImportant should render a different system prompt than PairDefault.");

            // Hidden-initiator entry: allowed for the important shape, so a supplied prior initiator entry
            // renders on the recipient plan.
            DiaryPromptPlan recipientPlan = BuildRecipientPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = false;
            });
            RequireTemplate(recipientPlan, DiaryPipelineTemplates.PairImportant,
                "the recipient plan of an important pair should also select PairImportant.");
            RequireContains(recipientPlan.userPrompt, HiddenInitiatorSentinel,
                "PairImportant includes the hidden-initiator field, so the prior initiator entry renders on the recipient plan.");
        }

        /// <summary>
        /// §4.2 PairCombat shape + weapon field + 200-token rule + combat priority. A combat pair selects
        /// PairCombat (even over a batch marker on the same event), carries the raised 200-token cap, and
        /// renders the weapon, relationship, and "you" (PawnSummary) fields. On the recipient plan the
        /// hidden-initiator entry renders (allowed for the combat shape).
        /// </summary>
        [Test]
        public static void CombatPairSelectsCombatTemplateWithWeaponAndRaisedCap()
        {
            // A batch marker is present too, to prove combat wins over batch (combat is checked first).
            DiaryEvent diaryEvent = MakePairEvent(BatchInteractionContext(), AnyInteractionDefName());

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = true;
            });

            RequireTemplate(plan, DiaryPipelineTemplates.PairCombat,
                "a combat pair should select PairCombat, even with a batch marker present (combat > batch).");
            RequireMaxTokens(plan, ImportantMaxTokens,
                "the combat pair shape must carry the raised 200-token cap.");
            RequireContains(plan.userPrompt, WeaponSentinel,
                "PairCombat includes the weapon field, so the injected weapon must render.");
            RequireContains(plan.userPrompt, RelationshipSentinel,
                "PairCombat includes the relationship field.");
            RequireContains(plan.userPrompt, PawnSummarySentinel,
                "PairCombat includes the 'you' (PawnSummary) field, so the injected summary must render.");
            RequireContains(plan.userPrompt, DirectSpeechToken,
                "PairCombat is a social interaction shape, so its initiator prompt emits the direct-speech instruction.");

            // Hidden-initiator entry: allowed for the combat shape.
            DiaryPromptPlan recipientPlan = BuildRecipientPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = true;
            });
            RequireTemplate(recipientPlan, DiaryPipelineTemplates.PairCombat,
                "the recipient plan of a combat pair should also select PairCombat.");
            RequireContains(recipientPlan.userPrompt, HiddenInitiatorSentinel,
                "PairCombat includes the hidden-initiator field, so the prior initiator entry renders on the recipient plan.");
        }

        /// <summary>
        /// §4.2 PairBatched shape. A non-combat pair carrying a `batch=` context marker (and matched
        /// group) selects PairBatched, which is uncapped and — unlike the other pair shapes — drops the
        /// relationship field, and (like PairDefault) has no weapon, "you", or hidden-initiator field. It
        /// still renders the batched "what you saw" evidence and emits the direct-speech instruction.
        /// </summary>
        [Test]
        public static void BatchMarkerPairSelectsBatchedTemplateWithoutRelationship()
        {
            DiaryEvent diaryEvent = MakePairEvent(BatchInteractionContext(), AnyInteractionDefName());

            DiaryPromptPlan plan = BuildInitiatorPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = false;
            });

            RequireTemplate(plan, DiaryPipelineTemplates.PairBatched,
                "a non-combat pair with a batch marker should select PairBatched.");
            RequireMaxTokens(plan, NoTemplateCap, "PairBatched carries no template token cap.");
            RequireContains(plan.userPrompt, PovTextSentinel,
                "PairBatched should render its batched evidence in the 'what you saw' body.");
            RequireNotContains(plan.userPrompt, RelationshipSentinel,
                "PairBatched drops the relationship field, so the injected relationship must not render.");
            RequireNotContains(plan.userPrompt, WeaponSentinel,
                "PairBatched has no weapon field.");
            RequireContains(plan.userPrompt, DirectSpeechToken,
                "PairBatched is a social interaction shape, so its initiator prompt emits the direct-speech instruction.");

            // Hidden-initiator entry is not part of the batched shape.
            DiaryPromptPlan recipientPlan = BuildRecipientPlan(diaryEvent, request =>
            {
                request.policy.group.important = true;
                request.policy.group.combat = false;
            });
            RequireTemplate(recipientPlan, DiaryPipelineTemplates.PairBatched,
                "the recipient plan of a batched pair should also select PairBatched.");
            RequireNotContains(recipientPlan.userPrompt, HiddenInitiatorSentinel,
                "PairBatched has no hidden-initiator field, so the prior initiator entry must not render.");
        }

        /// <summary>
        /// §4.2 direct-speech instruction "only where allowed". The hidden-initiator direct-speech
        /// instruction is emitted for a real interaction pair shape but is absent for a non-interaction
        /// pair shape (an unrecognized interaction defName with no `batch=interaction` marker), whose
        /// resolved directSpeechInstruction is empty — so the "[[speech]]" markup never reaches the user
        /// prompt.
        /// </summary>
        [Test]
        public static void DirectSpeechInstructionOnlyForInteractionPairShapes()
        {
            // Interaction shape: instruction present.
            DiaryEvent interactionEvent = MakePairEvent(InteractionContext(), AnyInteractionDefName());
            DiaryPromptRequest interactionRequest = BuildRequest(interactionEvent, DiaryEvent.InitiatorRole, string.Empty);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(interactionRequest.directSpeechInstruction),
                "A real interaction pair should resolve a non-empty direct-speech instruction.");
            DiaryPromptPlan interactionPlan = DiaryPromptPlanner.Build(interactionRequest);
            RequireContains(interactionPlan.userPrompt, DirectSpeechToken,
                "A real interaction pair's initiator user prompt should carry the direct-speech instruction.");

            // Non-interaction shape: unrecognized defName, no batch=interaction marker => not an
            // interaction prompt => empty direct-speech instruction => no "[[speech]]" in the user prompt.
            DiaryEvent nonInteractionEvent = MakePairEvent("mood_impact=neutral", "PawnDiaryTest_NotAnInteraction");
            DiaryPromptRequest nonInteractionRequest = BuildRequest(nonInteractionEvent, DiaryEvent.InitiatorRole, string.Empty);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(nonInteractionRequest.directSpeechInstruction),
                "A non-interaction pair shape should resolve an EMPTY direct-speech instruction.");
            DiaryPromptPlan nonInteractionPlan = DiaryPromptPlanner.Build(nonInteractionRequest);
            RequireNotContains(nonInteractionPlan.userPrompt, DirectSpeechToken,
                "A non-interaction pair's user prompt must not carry the direct-speech instruction.");
        }

        // ----- builders + assertions --------------------------------------------------------------

        /// <summary>
        /// Builds an in-memory pair <see cref="DiaryEvent"/> with sentinel POV text / relationship /
        /// weapon / summary and the given context/defName. It is never submitted to the component, so it
        /// leaves no state to clean up.
        /// </summary>
        private static DiaryEvent MakePairEvent(string gameContext, string interactionDefName)
        {
            return new DiaryEvent
            {
                eventId = "PDTest_Pair_" + interactionDefName,
                tick = 123456,
                date = "1st of Aprimay, 5500",
                solo = false,
                interactionDefName = interactionDefName,
                interactionLabel = "test exchange",
                gameContext = gameContext,
                instruction = "note the exchange",
                initiatorPawnId = "PDTest_pawn_init",
                initiatorName = "PDTestInitiator",
                initiatorText = PovTextSentinel,
                initiatorPawnSummary = PawnSummarySentinel,
                initiatorSurroundings = "PDTEST_SETTING",
                initiatorContinuity = RelationshipSentinel,
                initiatorWeapon = WeaponSentinel,
                initiatorLastOpener = string.Empty,
                recipientPawnId = "PDTest_pawn_recip",
                recipientName = "PDTestRecipient",
            };
        }

        /// <summary>
        /// A minimal interaction gameContext (no batch marker), so a real interaction defName routes to
        /// the non-batched pair shapes.
        /// </summary>
        private static string InteractionContext()
        {
            return "mood_impact=neutral";
        }

        /// <summary>
        /// An interaction gameContext carrying the `batch=interaction` marker, which both keeps the event
        /// an interaction prompt and drives the batched routing branch.
        /// </summary>
        private static string BatchInteractionContext()
        {
            return "mood_impact=neutral;batch=interaction";
        }

        /// <summary>
        /// Returns the defName of an arbitrary loaded vanilla InteractionDef, so the constructed event is
        /// recognized as an interaction prompt (IsInteractionPrompt) without hardcoding a specific def.
        /// </summary>
        private static string AnyInteractionDefName()
        {
            List<InteractionDef> defs = DefDatabase<InteractionDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (defs[i] != null && !string.IsNullOrWhiteSpace(defs[i].defName))
                    {
                        return defs[i].defName;
                    }
                }
            }

            throw new AssertionException("No InteractionDef is loaded, so no interaction pair shape can be built.");
        }

        /// <summary>
        /// Runs the production adapter for a role to resolve the real XML templates / token caps /
        /// localized direct-speech text for the event. Never enqueues an LLM request.
        /// </summary>
        private static DiaryPromptRequest BuildRequest(DiaryEvent diaryEvent, string role, string priorInitiatorEntry)
        {
            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                role,
                personaRule: string.Empty,
                psychotypeRule: string.Empty,
                promptEnchantment: string.Empty,
                humorCue: string.Empty,
                priorInitiatorEntry: priorInitiatorEntry,
                entryText: string.Empty,
                titleRequest: false,
                maxTokens: 0,
                contextDetailLevel: PromptContextDetailLevel.Full);

            if (request?.policy?.group == null)
            {
                throw new AssertionException("BuildPromptRequest returned no policy group to steer.");
            }

            return request;
        }

        /// <summary>
        /// Builds an initiator plan for the event, applying the caller's steering (importance / combat)
        /// to the returned request before running the pure renderer.
        /// </summary>
        private static DiaryPromptPlan BuildInitiatorPlan(DiaryEvent diaryEvent, Action<DiaryPromptRequest> steer)
        {
            DiaryPromptRequest request = BuildRequest(diaryEvent, DiaryEvent.InitiatorRole, string.Empty);
            steer?.Invoke(request);
            return RunPlanner(request);
        }

        /// <summary>
        /// Builds a recipient plan for the event, supplying a sentinel prior initiator entry so the
        /// hidden-initiator field renders on the shapes that include it. Applies the caller's steering.
        /// </summary>
        private static DiaryPromptPlan BuildRecipientPlan(DiaryEvent diaryEvent, Action<DiaryPromptRequest> steer)
        {
            DiaryPromptRequest request = BuildRequest(diaryEvent, DiaryEvent.RecipientRole, HiddenInitiatorSentinel);
            steer?.Invoke(request);
            return RunPlanner(request);
        }

        private static DiaryPromptPlan RunPlanner(DiaryPromptRequest request)
        {
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
