// Prompt-capture fixture for Pawn Diary's psychotype (outlook) voice layer (design/TEST_COVERAGE_PLAN.md §5.2).
//
// The psychotype layer contributes ONE optional block to the first-person system prompt: a 1-2 sentence
// "outlook" rule describing how a pawn judges events. Three sources can supply that rule, and the pure
// PsychotypeResolutionPolicy picks a winner by a fixed priority (Source/Pipeline/PsychotypeResolutionPolicy.cs):
//
//     External API override  >  Pawn custom rule  >  Base psychotype Def
//
// This suite proves the precedence END TO END through the live generation pipeline (not just the pure
// policy): for one generating colonist we set each layer, fire a real solo event, capture the rendered
// prompt, and assert the WINNING layer's text is what reaches the model while shadowed layers do not.
// It also pins down two rendering contracts the small-model prompt depends on:
//   * Combined-block ORDER: the psychotype lens block is emitted BEFORE the writing-style block
//     (DiaryPipelineAdapters.CombinedVoiceBlock joins outlook -> style -> humor in that fixed order, so
//     the harder mechanical writing-style constraint stays last where small models weight it most).
//   * No leak: the resolved outlook rule rides only in the SYSTEM prompt, never the USER prompt, and the
//     psychotype LABEL is never emitted at all (PsychotypeLensBlock deliberately passes the rule without
//     any "label: " prefix — the outlook must show THROUGH the entry, never be named).
// Finally it confirms the neutral ArrivalDescription template drops the whole voice block even when a
// non-empty outlook resolves, so a chronicle note never carries an outlook cue.
//
// Mechanism: Prompt Test Mode capture (EnablePromptCapture flips DevMode + promptTestMode). A fired solo
// event renders and STORES its real system+user prompt and stops before any LlmClient.Enqueue, so every
// assertion runs on exactly what would have been sent with zero network. The psychotype layer is forced
// ON for the scope (enablePsychotypes) and restored in teardown; all override rules written onto the
// pawn's PawnDiaryRecord are cleared via RegisterCleanup (belt-and-suspenders — the harness also deletes
// the whole test record), so the no-leak audit passes.
using System;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves psychotype precedence (external override &gt; pawn custom &gt; base type) end to end through
    /// the captured prompt, that the psychotype lens block is rendered before the writing-style block, that
    /// the outlook rule/label never leak into the USER prompt, and that the neutral arrival template carries
    /// no psychotype block. Requires a loaded game (the capture pipeline ignores events at the main menu).
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPsychotypeVoiceFixtureTests
    {
        // A solo, first-person-eligible event whose template DOES include the voice block. Firing an
        // inspiration drives the same real capture seam the §4.3 context-detail fixture uses.
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";
        private const string InspirationReason = "Struck by sudden RimTest psychotype inspiration";

        // A deterministic starting-arrival context so the arrival unit is recognized as a founding arrival
        // and captures on the NEUTRAL role synchronously (mirrors the §4.2 neutral-template fixture).
        private const string StartingArrivalContext =
            "arrival_source=game_start; scenario_name=TestCrashlanded; childhood_backstory=TestWanderer";

        // Distinctive marker rules, one per source layer, so the winning layer is identifiable by an exact
        // substring in the captured prompt without matching a whole translated paragraph.
        private const string CustomOutlookMarker =
            "PSYCHOOUTLOOKCUSTOM weighs every quiet setback as a private omen of worse to come.";
        private const string ExternalOutlookMarker =
            "PSYCHOOUTLOOKEXTERNAL reads each passing event as fate deliberately closing in.";
        private const string WritingStyleMarker =
            "PSYCHOSTYLEMARKER always writes in short, clipped, telegraphic fragments.";

        // Owner id for the external override layer (the adapter sourceId that owns/clears it).
        private const string ExternalSourceId = "PawnDiary.RimTest.PsychotypeVoiceSuite";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static bool originalEnablePsychotypes;

        /// <summary>
        /// Opens a scope with the inspiration and arrival groups enabled, forces the psychotype layer ON
        /// (restored in teardown), turns on prompt capture at the Full preset BEFORE any pawn is created,
        /// and generates one generation-ENABLED colonist. Prompt-test mode makes every fired event capture
        /// prompt-only, so no LLM request can leave the game.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey, ArrivalSignal.ArrivalGroupKey);
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);

            // The base-type and pawn-custom layers only contribute a block while the psychotype feature is
            // enabled (external overrides apply either way). Force it deterministically instead of trusting
            // the developer's live setting, and restore the exact prior value in teardown.
            if (PawnDiaryMod.Settings == null)
            {
                throw new AssertionException("Pawn Diary settings must be loaded for the psychotype voice suite.");
            }

            originalEnablePsychotypes = PawnDiaryMod.Settings.enablePsychotypes;
            PawnDiaryMod.Settings.enablePsychotypes = true;
            scope.RegisterCleanup(() =>
            {
                if (PawnDiaryMod.Settings != null)
                {
                    PawnDiaryMod.Settings.enablePsychotypes = originalEnablePsychotypes;
                }
            });

            pawn = scope.CreateGeneratingAdultColonist();

            // Guarantee the forced Inspired_Creativity this suite fires can start (a random colonist often
            // fails its skill gate, and forceStartAnyway does not bypass it). The psychotype and
            // writing-style layers this suite asserts resolve from the saved record, not live pawn state,
            // so no WorldPawns registration is needed here.
            PawnDiaryRimTestScope.MakeCreativityInspirationEligible(pawn);
        }

        /// <summary>
        /// Restores every mutation (DevMode/promptTestMode/contextDetailLevel + group toggles via the
        /// harness, enablePsychotypes and all override rules via RegisterCleanup) and audits for leaks —
        /// even when a test threw partway through.
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
        /// §5.2 base layer. With no pawn-custom rule and no external override, a pinned base psychotype Def
        /// is the effective outlook: the resolution reports BaseType, its rule equals the Def's rule, and
        /// that rule text appears in the captured prompt. The Def's LABEL never appears as a "label: rule"
        /// prefix — psychotype labels are picker text, never prompt text.
        /// </summary>
        [Test]
        public static void BaseTypeRuleWinsWhenNoOverrides()
        {
            DiaryPsychotypeDef baseDef = ForceBasePsychotype();

            PsychotypeResolution resolution = scope.Component.ResolvePsychotypeFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == PsychotypeRuleSource.BaseType,
                "With no custom rule or external override, the base psychotype Def should be the effective source.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.rule, DiaryPsychotypes.RuleFor(baseDef.defName), StringComparison.Ordinal),
                "The effective base outlook rule did not match the selected Def's rule.");

            string prompt = FireInspirationAndCapture();
            RequireContains(prompt, resolution.rule, "base outlook rule");

            // A label leak would render exactly as DiaryPersonaDef.RuleFor does ("label: rule"); assert that
            // precise signature is absent so an incidental label word elsewhere cannot false-fail this.
            string labelLeakSignature = baseDef.label + ": " + DiaryPsychotypes.RuleFor(baseDef.defName);
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(labelLeakSignature, StringComparison.Ordinal) < 0,
                "The psychotype label leaked into the prompt as a 'label: rule' prefix.");
        }

        /// <summary>
        /// §5.2 middle layer. A player-authored custom outlook rule wins over the base psychotype Def: the
        /// resolution reports PawnCustom with the custom rule, the captured prompt carries the custom rule,
        /// and the (still-set) base Def's rule is shadowed out of the prompt entirely.
        /// </summary>
        [Test]
        public static void PawnCustomRuleWinsOverBaseType()
        {
            DiaryPsychotypeDef baseDef = ForceBasePsychotype();
            string baseRule = DiaryPsychotypes.RuleFor(baseDef.defName);
            SetCustomOutlook(CustomOutlookMarker);

            PsychotypeResolution resolution = scope.Component.ResolvePsychotypeFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == PsychotypeRuleSource.PawnCustom,
                "A non-blank pawn custom rule should outrank the base psychotype Def.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.rule, CustomOutlookMarker, StringComparison.Ordinal),
                "The effective outlook rule should be the pawn custom rule.");

            string prompt = FireInspirationAndCapture();
            RequireContains(prompt, CustomOutlookMarker, "pawn custom outlook rule");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(baseRule) && prompt.IndexOf(baseRule, StringComparison.Ordinal) < 0,
                "The base psychotype rule leaked into the prompt even though the pawn custom rule should win.");
        }

        /// <summary>
        /// §5.2 top layer. A source-owned external override wins over the pawn custom rule: the resolution
        /// reports ExternalApiOverride with the external rule, the pure policy flags the custom rule as
        /// suppressed, the captured prompt carries the external rule, and the custom rule is shadowed out.
        /// </summary>
        [Test]
        public static void ExternalOverrideWinsOverPawnCustom()
        {
            SetCustomOutlook(CustomOutlookMarker);
            SetExternalOutlook(ExternalOutlookMarker);

            PsychotypeResolution resolution = scope.Component.ResolvePsychotypeFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == PsychotypeRuleSource.ExternalApiOverride,
                "An active external override should outrank the pawn custom rule.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.rule, ExternalOutlookMarker, StringComparison.Ordinal),
                "The effective outlook rule should be the external override rule.");
            PawnDiaryRimTestScope.Require(
                PsychotypeResolutionPolicy.CustomSuppressedByOverride(resolution),
                "The resolution should report the pawn custom rule as suppressed by the external override.");

            string prompt = FireInspirationAndCapture();
            RequireContains(prompt, ExternalOutlookMarker, "external override outlook rule");
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(CustomOutlookMarker, StringComparison.Ordinal) < 0,
                "The pawn custom rule leaked into the prompt even though the external override should win.");
        }

        /// <summary>
        /// §5.2 combined-block order. When both a psychotype outlook and a writing-style rule apply, the
        /// captured prompt places the psychotype lens block BEFORE the writing-style block
        /// (CombinedVoiceBlock order: outlook -> style -> humor).
        /// </summary>
        [Test]
        public static void PsychotypeLensPrecedesWritingStyleBlock()
        {
            SetCustomOutlook(CustomOutlookMarker);
            SetWritingStyle(WritingStyleMarker);

            string prompt = FireInspirationAndCapture();
            int outlookIndex = prompt.IndexOf(CustomOutlookMarker, StringComparison.Ordinal);
            int styleIndex = prompt.IndexOf(WritingStyleMarker, StringComparison.Ordinal);

            PawnDiaryRimTestScope.Require(
                outlookIndex >= 0, "The psychotype outlook block was missing from the captured prompt.");
            PawnDiaryRimTestScope.Require(
                styleIndex >= 0, "The writing-style block was missing from the captured prompt.");
            PawnDiaryRimTestScope.Require(
                outlookIndex < styleIndex,
                "The psychotype lens block must be rendered before the writing-style block.");
        }

        /// <summary>
        /// §5.2 no leak. The resolved outlook rule rides only in the SYSTEM prompt half of the capture,
        /// never the USER prompt half — the user prompt carries the event's facts, not voice steering.
        /// </summary>
        [Test]
        public static void PsychotypeRuleNeverLeaksIntoUserPrompt()
        {
            SetCustomOutlook(CustomOutlookMarker);

            string prompt = FireInspirationAndCapture();
            int userHeaderIndex = prompt.IndexOf(DiaryPromptCapture.UserHeader, StringComparison.Ordinal);
            PawnDiaryRimTestScope.Require(
                userHeaderIndex >= 0,
                "The capture wrapper did not include the USER PROMPT header, so the halves cannot be split.");

            string systemHalf = prompt.Substring(0, userHeaderIndex);
            string userHalf = prompt.Substring(userHeaderIndex);

            RequireContains(systemHalf, CustomOutlookMarker, "outlook rule in the system prompt");
            PawnDiaryRimTestScope.Require(
                userHalf.IndexOf(CustomOutlookMarker, StringComparison.Ordinal) < 0,
                "The psychotype outlook rule leaked into the USER prompt; it belongs to the system prompt only.");
        }

        /// <summary>
        /// §5.2 neutral suppression. The neutral ArrivalDescription template opts out of the whole voice
        /// block (includePersona=false), so even when a non-empty outlook still resolves for the pawn, the
        /// captured neutral arrival prompt carries no psychotype block.
        /// </summary>
        [Test]
        public static void NeutralArrivalPromptHasNoPsychotypeBlock()
        {
            SetCustomOutlook(CustomOutlookMarker);

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, StartingArrivalContext)),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);

            // Captured synchronously on the neutral POV; the assert also proves the no-network guarantee.
            string prompt = scope.CapturedPrompt(arrival, DiaryEvent.NeutralRole);
            RequireContains(prompt, DiaryPromptCapture.SystemHeader, "arrival capture");

            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(CustomOutlookMarker, StringComparison.Ordinal) < 0,
                "The neutral arrival template must suppress the psychotype block, but the outlook rule appeared.");

            // Absence must be because the neutral template drops the block, NOT because the outlook was
            // blank: the same pawn's custom outlook still resolves as the effective rule.
            PsychotypeResolution resolution = scope.Component.ResolvePsychotypeFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == PsychotypeRuleSource.PawnCustom
                    && string.Equals(resolution.rule, CustomOutlookMarker, StringComparison.Ordinal),
                "The custom outlook should still resolve for this pawn, proving the arrival suppression is template-driven.");
        }

        // ----- helpers -------------------------------------------------------------------------------

        // Selects a non-Neutral adult psychotype Def with a non-empty rule, assigns it as the pawn's base
        // type, and pins it so EnsureVoiceStage never re-rolls the base out from under the assertions.
        private static DiaryPsychotypeDef ForceBasePsychotype()
        {
            DiaryPsychotypeDef baseDef = RequireNonNeutralBaseDef();
            PawnDiaryRimTestScope.Require(
                scope.Component.SetPsychotype(pawn, baseDef.defName),
                "Failed to assign the base psychotype Def to the test pawn.");
            scope.Component.SetPsychotypePinned(pawn, true);
            return baseDef;
        }

        private static DiaryPsychotypeDef RequireNonNeutralBaseDef()
        {
            foreach (DiaryPsychotypeDef def in DiaryPsychotypes.PickerDefsFor("adult"))
            {
                if (def != null
                    && !string.Equals(def.defName, DiaryPsychotypes.NeutralDefName, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(DiaryPsychotypes.RuleFor(def.defName)))
                {
                    return def;
                }
            }

            throw new AssertionException(
                "The psychotype voice suite needs at least one non-Neutral adult psychotype Def with a rule.");
        }

        private static void SetCustomOutlook(string rule)
        {
            PawnDiaryRimTestScope.Require(
                scope.Component.SetCustomPsychotypeRule(pawn, rule),
                "Failed to set the pawn custom psychotype rule.");
            scope.RegisterCleanup(() => scope.Component.SetCustomPsychotypeRule(pawn, string.Empty));
        }

        private static void SetExternalOutlook(string rule)
        {
            PawnDiaryRimTestScope.Require(
                scope.Component.SetExternalPsychotypeOverride(pawn, ExternalSourceId, rule),
                "Failed to set the external psychotype override.");
            scope.RegisterCleanup(() => scope.Component.ResetExternalPsychotypeOverride(pawn, ExternalSourceId));
        }

        private static void SetWritingStyle(string rule)
        {
            PawnDiaryRimTestScope.Require(
                scope.Component.SetCustomWritingStyleRule(pawn, rule),
                "Failed to set the pawn custom writing-style rule.");
            scope.RegisterCleanup(() => scope.Component.SetCustomWritingStyleRule(pawn, string.Empty));
        }

        // Fires one real inspiration on the generating pawn and returns the prompt the runtime rendered and
        // stored for the initiator POV. The started inspiration is ended in cleanup.
        private static string FireInspirationAndCapture()
        {
            InspirationDef inspirationDef = RequireDef<InspirationDef>(InspirationDefName);
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

        private static void RequireContains(string haystack, string needle, string what)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "Expected the captured prompt to contain the " + what + ".");
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
