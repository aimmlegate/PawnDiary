// Prompt-capture fixture for Pawn Diary's §4.2 NEUTRAL templates (design/TEST_COVERAGE_PLAN.md §4).
//
// Three "chronicle" shapes deliberately opt out of the first-person voice machinery so their output
// is a factual note rather than a diary entry in someone's style:
//   * ArrivalDescription — the neutral first page of a colonist's diary ("how they joined").
//   * DeathDescription   — the neutral final page ("how they died"), never a first-person entry.
//   * Title              — the tiny follow-up that titles an existing entry, using ONLY the entry text.
// All three set includePersona=false / includePromptEnchantment=false / appendDirectSpeechInstruction=false
// in XML (1.6/Defs/DiaryPromptTemplateDefs.xml), so no writing-style/psychotype/humor block and no
// direct-speech instruction ever reaches the model for them.
//
// Mechanism: Prompt Test Mode capture. EnablePromptCapture() flips DevMode + promptTestMode, so the real
// production pipeline renders and STORES each event's prompt and stops before any LlmClient.Enqueue — the
// captured string is exactly what would have been sent, with zero network. Arrival and Death both queue
// their neutral prompt SYNCHRONOUSLY on emit (ArrivalSignal.Emit -> QueueArrivalDescriptionFor and
// DeathFallbackSignal.Emit -> QueueDeathDescriptionFor), and the neutral POV is ungated per-pawn
// (PawnIdForRole(Neutral) is empty, so DiaryGenerationEnabledFor short-circuits to true), so firing the
// signal captures on the NeutralRole immediately. Titles are different: QueueTitleRequest bails out under
// promptTestMode, so titles never capture end-to-end. Per the plan we instead drive the pure title path
// directly — DiaryPipelineAdapters.BuildPromptRequest(titleRequest:true) + DiaryPromptPlanner.Build — and
// assert on the returned plan/request.
//
// No LLM request can leave the game: prompt-test mode stays on for the whole scope (the harness restores
// DevMode/promptTestMode/contextDetailLevel in teardown) and we never turn it off while generation is
// enabled. The death case kills a colonist, so it registers corpse/world-pawn cleanup before the Kill.
using System;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the neutral chronicle/title templates render as boundary-fact notes with NO voice side
    /// channels: the captured arrival and death prompts carry the pawn's boundary facts on the neutral
    /// POV while their templates opt out of persona/enchantment/direct-speech, and the title path uses the
    /// entry text alone under a 40-token cap. Requires a loaded game (the capture pipeline ignores events
    /// at the main menu); the death case additionally needs a loaded map. Prompt-test mode guarantees no
    /// LLM request leaves the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryNeutralTemplateFixtureTests
    {
        // A deterministic starting-arrival context. "arrival_source=game_start" marks it a founding
        // arrival (ArrivalEventData.IsStartingArrival) and the extra keys flow into the rendered
        // "arrival facts" field (DiaryPromptPlanner.BuildArrivalFacts reads arrival_source /
        // scenario_name / childhood_backstory), so our fact assertions are exact rather than depending
        // on whatever backstory RimWorld happened to roll for the generated pawn.
        private const string StartingArrivalContext =
            "arrival_source=game_start; scenario_name=TestCrashlanded; childhood_backstory=TestWanderer";

        // The title-cap the production title follow-up uses (DiaryGameComponent.TitleMaxTokens is a
        // private const 40; mirrored here because the plan carries whatever cap the caller requests).
        private const int TitleMaxTokens = 40;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a scope with the arrival group and the Tale-combat group (which classifies the neutral
        /// death fallback) enabled, turns on prompt capture BEFORE creating any pawn, and generates one
        /// isolated generation-ENABLED colonist. Prompt-test mode makes every fired event capture prompt-only.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(ArrivalSignal.ArrivalGroupKey, "talecombat");
            scope.EnablePromptCapture();
            pawn = scope.CreateGeneratingAdultColonist();
        }

        /// <summary>
        /// Restores every mutation (DevMode/promptTestMode/contextDetailLevel, group toggles) and audits
        /// that no test-owned event, diary, corpse, or world-pawn survived — even when a test threw partway.
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
        /// §4.2 ArrivalDescription. Firing the real arrival unit captures a NEUTRAL-role prompt that carries
        /// the arrival boundary facts (the joining colonist under the "colonist" field, the starting-arrival
        /// facts under "arrival facts"), and the ArrivalDescription template opts out of persona, prompt
        /// enchantment, and direct speech — so no writing-style/voice block or first-person speech
        /// instruction can reach the model.
        /// </summary>
        [Test]
        public static void ArrivalDescriptionCapturesNeutralBoundaryFactsWithoutVoice()
        {
            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, StartingArrivalContext)),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);

            // The neutral arrival prompt is captured synchronously on emit. CapturedPrompt asserts the
            // neutral POV reached prompt-only status (proving the role and the no-network guarantee) and
            // returns the combined system+user prompt.
            string prompt = scope.CapturedPrompt(arrival, DiaryEvent.NeutralRole);

            // The capture wrapper always frames both halves, so these markers confirm we read the rendered
            // request (DiaryPromptCapture.Format), not a raw stored fragment.
            RequireContains(prompt, DiaryPromptCapture.SystemHeader, "arrival capture");
            RequireContains(prompt, DiaryPromptCapture.UserHeader, "arrival capture");

            // Boundary facts: the joining colonist and the starting-arrival facts. The pawn label is
            // cleaned the same way the signal cleaned it before storing it as arrival_pawn.
            string label = DiaryLineCleaner.CleanLine(pawn.LabelShortCap);
            RequireContains(prompt, "colonist:", "arrival pawn field label");
            RequireContains(prompt, label, "arrival pawn label");
            RequireContains(prompt, "arrival facts:", "arrival facts field label");
            RequireContains(prompt, "source=game_start", "arrival source fact");
            RequireContains(prompt, "childhood=TestWanderer", "arrival childhood fact");

            // The template opts out of the entire first-person voice apparatus.
            RequireNeutralNoVoiceContract(DiaryPromptTemplates.ArrivalDescription);
        }

        /// <summary>
        /// §4.2 DeathDescription. Killing a colonist with no DamageInfo records the neutral death page and
        /// captures a NEUTRAL-role prompt that names the deceased under the "deceased" field, while the
        /// DeathDescription template opts out of persona, prompt enchantment, and direct speech. The dead
        /// pawn's corpse/world-pawn state is cleaned up before the destructive Kill so a failure can't strand it.
        /// </summary>
        [Test]
        public static void DeathDescriptionCapturesNeutralBoundaryFactsWithoutVoice()
        {
            // Vanilla death handling reads the colony's home map, so gate on a loaded map like the EVT-10
            // death suite does. The failure-safe TearDown still runs and pops the RNG state.
            if (Find.CurrentMap == null)
            {
                throw new AssertionException("§4.2 death capture requires a loaded map with a colony.");
            }

            string label = DiaryLineCleaner.CleanLine(pawn.LabelShortCap);

            // Register the dead-pawn cleanup BEFORE the destructive Kill so a failing assertion below can
            // never strand a corpse Thing or world-pawn entry in the developer's colony.
            RegisterDeadPawnCleanup(pawn);

            // Kill(null) carries no DamageInfo, so vanilla records no death Tale and the page comes from the
            // Pawn.Kill fallback (DeathFallbackSignal), which queues the neutral death prompt synchronously.
            DiaryEvent death = scope.FireAndRequireEvent(
                () => pawn.Kill(null),
                DeathFallbackSignal.DeathFallbackDefName,
                pawn,
                null);

            string prompt = scope.CapturedPrompt(death, DiaryEvent.NeutralRole);

            RequireContains(prompt, DiaryPromptCapture.SystemHeader, "death capture");
            RequireContains(prompt, DiaryPromptCapture.UserHeader, "death capture");

            // Boundary fact: the deceased colonist named under the neutral "deceased" field. (A no-DamageInfo
            // death carries no weapon/instigator cause facts, so the "death facts" field may be empty; the
            // deceased identity is the always-present boundary fact.)
            RequireContains(prompt, "deceased:", "death victim field label");
            RequireContains(prompt, label, "death victim label");

            RequireNeutralNoVoiceContract(DiaryPromptTemplates.DeathDescription);
        }

        /// <summary>
        /// §4.2 Title. Titles do not capture end-to-end (QueueTitleRequest bails out under prompt-test mode),
        /// so the title path is exercised directly: BuildPromptRequest(titleRequest:true) selects the Title
        /// template, DiaryPromptPlanner.Build renders it, and we assert the plan uses ONLY the entry text
        /// (no persona/context side channels leak from the carrier event), carries the 40-token cap, and the
        /// Title template opts out of voice.
        /// </summary>
        [Test]
        public static void TitleRequestUsesEntryTextOnlyWithCapAndNoVoice()
        {
            // Any real event works as the payload carrier; titleRequest short-circuits template selection to
            // Title before the carrier's shape matters, and the entry text is supplied explicitly below.
            DiaryEvent carrier = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, StartingArrivalContext)),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);

            const string entryText = "PawnDiary RimTest known diary body about the long walk home.";

            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                carrier,
                DiaryEvent.NeutralRole,
                string.Empty, // personaRule
                string.Empty, // psychotypeRule
                string.Empty, // promptEnchantment
                string.Empty, // humorCue
                null,         // priorInitiatorEntry
                entryText,
                true,         // titleRequest
                TitleMaxTokens);

            // A title request always resolves to the Title template regardless of the carrier event's shape.
            PawnDiaryRimTestScope.Require(
                string.Equals(DiaryPromptPlanner.TemplateKeyFor(request), DiaryPromptTemplates.Title, StringComparison.OrdinalIgnoreCase),
                "A title request did not select the Title template.");

            // No voice side channels enter the request: no direct-speech instruction, no persona/voice block.
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(request.directSpeechInstruction),
                "A title request should carry no direct-speech instruction.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(request.personaVoiceBlock),
                "A title request should carry no persona/voice block.");

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            PawnDiaryRimTestScope.Require(plan != null, "The title plan was null.");

            // Entry text only: the diary body is present under the Title field label, and none of the
            // carrier's arrival context (facts, colonist field) leaks into the title user prompt.
            RequireContains(plan.userPrompt, entryText, "title entry text");

            // The title field's label doubles as leading context for the smallest models ("to title" stops
            // them continuing the entry instead of titling it), so it must reach the prompt. Resolve it from
            // the same field list the planner renders from rather than hard-coding a copy that can rot, and
            // assert it is the shipped label: a Title template whose XML <fields> failed to load would fall
            // back to the weaker code-default "entry" label, and this catches that instead of masking it.
            string titleFieldLabel = TitleEntryFieldLabel();
            PawnDiaryRimTestScope.Require(
                string.Equals(titleFieldLabel, "diary entry to title", StringComparison.Ordinal),
                "The loaded Title template's EntryText field label was '" + (titleFieldLabel ?? "<none>")
                + "', expected the shipped 'diary entry to title' (its XML <fields> may not be loading). "
                + TitleTemplateDump());
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(titleFieldLabel)
                    && plan.userPrompt.IndexOf(titleFieldLabel, StringComparison.Ordinal) >= 0,
                "The title user prompt did not render the entry under its '" + titleFieldLabel
                + "' field label. Rendered user prompt:\n" + plan.userPrompt);
            PawnDiaryRimTestScope.Require(
                plan.userPrompt.IndexOf("arrival facts", StringComparison.Ordinal) < 0
                    && plan.userPrompt.IndexOf("source=game_start", StringComparison.Ordinal) < 0,
                "The title user prompt leaked event context; it must use the entry text only.");

            // The 40-token cap and title flag ride on the plan's response rules.
            PawnDiaryRimTestScope.Require(
                plan.responseRules != null && plan.responseRules.maxTokens == TitleMaxTokens,
                "The title plan did not carry the 40-token cap.");
            PawnDiaryRimTestScope.Require(
                plan.responseRules.isTitle,
                "The title plan's response rules were not marked as a title request.");

            // The Title template opts out of the first-person voice apparatus.
            RequireNeutralNoVoiceContract(DiaryPromptTemplates.Title);
        }

        // ----- helpers -------------------------------------------------------------------------------

        /// <summary>
        /// Asserts the XML template for <paramref name="templateKey"/> opts out of every first-person voice
        /// channel: no persona/writing-style block appended to the system prompt (includePersona=false), no
        /// live hediff prompt enchantment (includePromptEnchantment=false), and no appended direct-speech
        /// instruction (appendDirectSpeechInstruction=false). This is the shipped contract the render honors
        /// (DiaryPipelineAdapters.AddTemplate copies these flags into the plan's DiaryTemplatePolicy).
        /// </summary>
        private static void RequireNeutralNoVoiceContract(string templateKey)
        {
            DiaryPromptTemplateDef template = DiaryPromptTemplates.ForKey(templateKey);
            PawnDiaryRimTestScope.Require(
                template != null,
                "The '" + templateKey + "' prompt template was not loaded.");
            PawnDiaryRimTestScope.Require(
                !template.includePersona,
                "The '" + templateKey + "' template must set includePersona=false (no writing-style/voice block).");
            PawnDiaryRimTestScope.Require(
                !template.includePromptEnchantment,
                "The '" + templateKey + "' template must set includePromptEnchantment=false.");
            PawnDiaryRimTestScope.Require(
                !template.appendDirectSpeechInstruction,
                "The '" + templateKey + "' template must set appendDirectSpeechInstruction=false (no direct-speech instruction).");
        }

        // Reads the Title template's EntryText field label from the same field list the planner renders
        // from (DiaryPromptTemplates.FieldsFor), so the assertion tracks the shipped def rather than a
        // hard-coded copy. Returns null if no EntryText field is configured.
        private static string TitleEntryFieldLabel()
        {
            var fields = DiaryPromptTemplates.FieldsFor(DiaryPromptTemplates.Title);
            if (fields != null)
            {
                foreach (DiaryPromptFieldDef field in fields)
                {
                    if (field != null && string.Equals(field.source, "EntryText", StringComparison.OrdinalIgnoreCase))
                    {
                        return field.label;
                    }
                }
            }

            return null;
        }

        // Dumps the loaded Title template's identity plus both its raw def fields and the effective
        // FieldsFor list, so a failure shows whether ForKey matched the shipped def, whether its XML
        // <fields> populated, and what the planner actually renders from.
        private static string TitleTemplateDump()
        {
            DiaryPromptTemplateDef def = DiaryPromptTemplates.ForKey(DiaryPromptTemplates.Title);
            string defPart = def == null
                ? "<null>"
                : "defName=" + def.defName + " templateKey=" + def.templateKey
                    + " rawFields=" + DescribeFields(def.fields);
            return "[ForKey: " + defPart + "; FieldsFor="
                + DescribeFields(DiaryPromptTemplates.FieldsFor(DiaryPromptTemplates.Title)) + "]";
        }

        private static string DescribeFields(System.Collections.Generic.List<DiaryPromptFieldDef> fields)
        {
            if (fields == null)
            {
                return "<null>";
            }

            if (fields.Count == 0)
            {
                return "<empty>";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(fields.Count).Append(":");
            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldDef f = fields[i];
                sb.Append(" ").Append(f?.label ?? "?").Append("|").Append(f?.source ?? "?");
            }

            return sb.ToString();
        }

        private static void RequireContains(string haystack, string needle, string what)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "Expected the " + what + " to contain '" + needle + "'.");
        }

        /// <summary>
        /// Registers cleanup for the state a killed pawn leaves that the harness does not already own: a
        /// (possibly unspawned) Corpse holder and a world-pawn registry entry. Registered so the corpse is
        /// destroyed first (freeing the pawn), then any world-pawn entry is dropped; the harness then
        /// destroys the pawn itself. Mirrors PawnDiaryDeathFlowTests.
        /// </summary>
        private static void RegisterDeadPawnCleanup(Pawn deadPawn)
        {
            // Registered first -> runs last: drop a world-pawn entry only if one survived the corpse step.
            scope.RegisterCleanup(() =>
            {
                if (deadPawn != null
                    && !deadPawn.Destroyed
                    && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(deadPawn))
                {
                    Find.WorldPawns.RemovePawn(deadPawn);
                }
            });

            // Registered last -> runs first: destroy the corpse holder so no corpse Thing leaks onto a map.
            scope.RegisterCleanup(() =>
            {
                Corpse corpse = deadPawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.Vanish);
                }
            });
        }
    }
}
