// Prompt-capture fixture for Pawn Diary's writing-style precedence (TEST_COVERAGE_PLAN.md §5.2).
//
// The writing-style (persona) layer resolves through a fixed priority:
//     External API override > Hediff override > Pawn custom prompt > Base style Def.
// (WritingStyleResolutionPolicy.Resolve; the runtime adapter is HediffPersonaOverrides.ResolveWritingStyle,
// and the winning rule reaches the model through DiaryPipelineAdapters.PersonaVoiceBlock, injected into
// the "PawnDiary.Prompt.PersonaVoice" system-prompt line as its {0} argument.)
//
// This suite drives that precedence on ONE generating colonist under Prompt Test Mode, so every fired
// event stamps the assembled prompt and STOPS before any network call. At each layer it (1) reads the
// effective WritingStyleResolution through the same seam the UI uses (component.ResolveWritingStyleFor)
// and asserts BOTH the winning rule text and the source metadata (WritingStyleRuleSource), then (2) fires
// a real solo event (an inspiration, whose SoloInternalState template keeps includePersona=true)
// and asserts the effective rule reaches the captured prompt verbatim. It then REMOVES the winning layer
// and shows the next layer down becomes effective WITHOUT ever changing the saved base picker.
//
// Determinism: the pawn's base style is a RANDOM first roll, so SetUp pins it to a single known Def and
// captures the resulting base defName; every base-layer assertion compares against that captured value
// and against DiaryPersonas.RuleFor(...), never against hand-copied XML prose. The hediff layer is forced
// with a core, DLC-safe hediff (AlcoholHigh at the "drunk" stage), which the shipped
// DiaryHediffPersonaOverrideDefs map to DiaryPersona_DrunkRambling. Every override written to the pawn's
// PawnDiaryRecord (base persona + pin, custom prompt, external override) is undone through RegisterCleanup
// so the harness no-leak audit passes even though the whole record is also removed at teardown.
using System;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the writing-style precedence External &gt; Hediff &gt; PawnCustom &gt; BaseStyle: at each layer
    /// the effective rule (both its text and its <see cref="WritingStyleRuleSource"/>) is correct and reaches
    /// the captured prompt, and removing the top layer promotes the next one without touching the saved base
    /// picker. Also covers external-override one-line sanitization and source-owned reset. Requires a loaded game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryWritingStyleVoiceFixtureTests
    {
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";
        private const string InspirationReason = "Struck by sudden RimTest inspiration";

        // A single, distinctive base style so base-layer assertions never depend on the pawn's random roll.
        private const string KnownBaseStyleDefName = "DiaryPersona_StoicSurvivor";

        // A core, DLC-safe hediff at its "drunk" stage (>= the override's 0.4 minSeverity, below the 0.7
        // "hammered"/0.9 "blackout" stages so the pawn stays conscious). The shipped
        // DiaryHediffPersonaOverrideDefs map AlcoholHigh -> DiaryPersona_DrunkRambling.
        private const string DrunkHediffDefName = "AlcoholHigh";
        private const float DrunkSeverity = 0.5f;
        private const string DrunkPersonaDefName = "DiaryPersona_DrunkRambling";

        // Free-form override sentinels. Each carries a rare token so a substring assertion cannot collide
        // with ordinary prompt prose.
        private const string ExternalSourceId = "RimTest.WritingStyleFixture";
        private const string ExternalToken = "saltpetre";
        private const string ExternalSentinelRule = "External override sentinel: end every diary entry with the word saltpetre.";
        private const string CustomToken = "gearbox";
        private const string CustomSentinelRule = "Custom pawn sentinel: begin every diary entry with the word gearbox.";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Base picker captured AFTER pinning the known style, plus the pawn's original picker/pin for restore.
        private static string knownBaseStyleDefName;
        private static string originalBaseStyleDefName;
        private static bool originalWritingStylePinned;

        /// <summary>
        /// Opens a scope with the inspiration group enabled, turns on prompt capture at the Full preset,
        /// creates one generating colonist, then pins a single known base style so the base layer is
        /// deterministic. The original picker/pin are captured and restored in teardown.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey);
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateGeneratingAdultColonist();

            // The hediff writing-style override layer reads LIVE pawn health, so the pawn must be
            // resolvable by generation's live-pawn lookup or that layer silently drops out of the captured
            // prompt; spawning it as a colonist makes it both findable and eligible (SetPersona below needs
            // a live colonist too) and able to start the inspiration this suite fires.
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRimTestScope.MakeCreativityInspirationEligible(pawn);

            // Snapshot the rolled base picker/pin BEFORE mutating, so cleanup can restore them exactly.
            WritingStyleResolution seeded = scope.Component.ResolveWritingStyleFor(pawn);
            originalBaseStyleDefName = seeded.baseStyleDefName;
            originalWritingStylePinned = scope.Component.WritingStylePinnedFor(pawn);

            // Pin a known, distinctive base style. Pinning stops EnsureVoiceStage from ever re-rolling it
            // during generation, so the base layer stays fixed across every layer transition below.
            PawnDiaryRimTestScope.Require(
                scope.Component.SetPersona(pawn, KnownBaseStyleDefName),
                "Failed to assign the known base writing style to the test pawn.");
            PawnDiaryRimTestScope.Require(
                scope.Component.SetWritingStylePinned(pawn, true),
                "Failed to pin the test pawn's base writing style.");

            // Read back the authoritative stored base defName (SetPersona resolves unknown names to the
            // default), so equality checks are robust even if the known Def name ever changes.
            knownBaseStyleDefName = scope.Component.ResolveWritingStyleFor(pawn).baseStyleDefName;

            scope.RegisterCleanup(RestoreBasePicker);
        }

        /// <summary>
        /// Restores DevMode/promptTestMode/contextDetailLevel and the group settings (via the harness),
        /// undoes every override this suite wrote, and audits for leaks.
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
                knownBaseStyleDefName = null;
                originalBaseStyleDefName = null;
                originalWritingStylePinned = false;
            }
        }

        /// <summary>
        /// §5.2. The external API override outranks an active hediff style, a saved pawn-custom prompt, and
        /// the base Def all at once: the resolution reports ExternalApiOverride and its sentinel rule reaches
        /// the captured prompt.
        /// </summary>
        [Test]
        public static void ExternalOverrideOutranksAllLowerLayers()
        {
            AddDrunkHediff();
            SetCustomOverride(CustomSentinelRule);
            SetExternalOverride(ExternalSentinelRule);

            WritingStyleResolution resolution = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == WritingStyleRuleSource.ExternalApiOverride,
                "Expected the external API override to win, but the source was " + resolution.source + ".");
            PawnDiaryRimTestScope.Require(
                resolution.rule.IndexOf(ExternalToken, StringComparison.Ordinal) >= 0,
                "The effective rule was not the external sentinel.");

            string prompt = FireInspirationAndCapture();
            RequirePromptCarriesRule(prompt, resolution.rule);
            RequirePromptContains(prompt, ExternalToken, "external sentinel token");
        }

        /// <summary>
        /// §5.2. Removing the winning external override (through its owning source) promotes the active
        /// hediff style to effective — the resolution flips to HediffOverride and the drunk-style rule reaches
        /// the prompt — while the saved base picker is untouched.
        /// </summary>
        [Test]
        public static void HediffOverrideBecomesEffectiveWhenExternalReset()
        {
            AddDrunkHediff();
            SetCustomOverride(CustomSentinelRule);
            SetExternalOverride(ExternalSentinelRule);

            // Source-owned removal of the top layer.
            PawnDiaryRimTestScope.Require(
                scope.Component.ResetExternalWritingStyleOverride(pawn, ExternalSourceId),
                "The owning source failed to reset its external writing-style override.");

            WritingStyleResolution resolution = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == WritingStyleRuleSource.HediffOverride,
                "Expected the hediff override to become effective, but the source was " + resolution.source + ".");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.hediffStyleDefName, DrunkPersonaDefName, StringComparison.Ordinal),
                "The hediff override did not resolve to the drunk writing style.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.rule, DiaryPersonas.RuleFor(DrunkPersonaDefName), StringComparison.Ordinal),
                "The effective rule was not the drunk style's rule.");
            RequireBasePickerUnchanged(resolution);

            string prompt = FireInspirationAndCapture();
            RequirePromptCarriesRule(prompt, resolution.rule);
        }

        /// <summary>
        /// §5.2. Removing the active hediff promotes the saved pawn-custom prompt to effective — the
        /// resolution flips to PawnCustom and the custom sentinel reaches the prompt — while the saved base
        /// picker is untouched.
        /// </summary>
        [Test]
        public static void PawnCustomBecomesEffectiveWhenHediffRemoved()
        {
            Hediff drunk = AddDrunkHediff();
            SetCustomOverride(CustomSentinelRule);

            RemoveHediffIfPresent(pawn, drunk);

            WritingStyleResolution resolution = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == WritingStyleRuleSource.PawnCustom,
                "Expected the pawn-custom prompt to become effective, but the source was " + resolution.source + ".");
            PawnDiaryRimTestScope.Require(
                resolution.rule.IndexOf(CustomToken, StringComparison.Ordinal) >= 0,
                "The effective rule was not the pawn-custom sentinel.");
            RequireBasePickerUnchanged(resolution);

            string prompt = FireInspirationAndCapture();
            RequirePromptCarriesRule(prompt, resolution.rule);
            RequirePromptContains(prompt, CustomToken, "pawn-custom sentinel token");
        }

        /// <summary>
        /// §5.2. Clearing the pawn-custom prompt falls through to the saved base style — the resolution flips
        /// to BaseStyle, its defName is still the pinned known style, and the base rule reaches the prompt.
        /// The base picker was never changed by any of the higher-layer edits.
        /// </summary>
        [Test]
        public static void BaseStyleBecomesEffectiveWhenCustomCleared()
        {
            SetCustomOverride(CustomSentinelRule);

            // Clearing custom text is a source-owned reset of the pawn-custom layer.
            PawnDiaryRimTestScope.Require(
                scope.Component.SetCustomWritingStyleRule(pawn, string.Empty),
                "Failed to clear the pawn-custom writing-style prompt.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(scope.Component.CustomWritingStyleRuleFor(pawn)),
                "The pawn-custom writing-style prompt was not actually cleared.");

            WritingStyleResolution resolution = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == WritingStyleRuleSource.BaseStyle,
                "Expected the base style to become effective, but the source was " + resolution.source + ".");
            RequireBasePickerUnchanged(resolution);
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.rule, DiaryPersonas.RuleFor(knownBaseStyleDefName), StringComparison.Ordinal),
                "The effective rule was not the pinned base style's rule.");

            string prompt = FireInspirationAndCapture();
            RequirePromptCarriesRule(prompt, resolution.rule);
        }

        /// <summary>
        /// §5.2. An external override supplied with embedded line breaks and a tab is sanitized to a single
        /// line before it is stored or used, and that single-line form is exactly what reaches the prompt.
        /// </summary>
        [Test]
        public static void ExternalOverrideRuleIsSanitizedToOneLine()
        {
            // Two rare tokens on separate physical lines, plus a tab, to prove the multi-line input collapses.
            const string firstToken = "STYLEONE-alpha";
            const string secondToken = "STYLETWO-beta";
            string rawMultiline = firstToken + "\n\t" + secondToken;
            SetExternalOverride(rawMultiline);

            WritingStyleResolution resolution = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                resolution.source == WritingStyleRuleSource.ExternalApiOverride,
                "Expected the sanitized external override to be effective, but the source was " + resolution.source + ".");
            PawnDiaryRimTestScope.Require(
                resolution.externalRule.IndexOf('\n') < 0
                    && resolution.externalRule.IndexOf('\r') < 0
                    && resolution.externalRule.IndexOf('\t') < 0,
                "The stored external override was not collapsed to a single line.");
            PawnDiaryRimTestScope.Require(
                resolution.externalRule.IndexOf(firstToken, StringComparison.Ordinal) >= 0
                    && resolution.externalRule.IndexOf(secondToken, StringComparison.Ordinal) >= 0,
                "Sanitizing the external override dropped one of its content tokens.");

            string prompt = FireInspirationAndCapture();
            // The sanitized single line is embedded verbatim; the raw newline-joined form must not appear.
            RequirePromptCarriesRule(prompt, resolution.rule);
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(firstToken + "\n", StringComparison.Ordinal) < 0,
                "The un-sanitized multi-line override text leaked into the prompt.");
        }

        /// <summary>
        /// §5.2. An external override is source-owned: a DIFFERENT source cannot clear it (the override
        /// persists), but the owning source can — after which the writing style falls back to the base layer.
        /// </summary>
        [Test]
        public static void ExternalOverrideResetIsSourceOwned()
        {
            SetExternalOverride(ExternalSentinelRule);

            // A foreign source must not be able to remove another adapter's active override.
            PawnDiaryRimTestScope.Require(
                !scope.Component.ResetExternalWritingStyleOverride(pawn, "RimTest.OtherSource"),
                "A foreign source was wrongly allowed to reset another source's override.");
            WritingStyleResolution stillOwned = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                stillOwned.source == WritingStyleRuleSource.ExternalApiOverride
                    && stillOwned.rule.IndexOf(ExternalToken, StringComparison.Ordinal) >= 0,
                "The external override did not survive a foreign source's reset attempt.");

            // The owning source clears it and the style falls back to the base layer (no custom/hediff set).
            PawnDiaryRimTestScope.Require(
                scope.Component.ResetExternalWritingStyleOverride(pawn, ExternalSourceId),
                "The owning source failed to reset its own external override.");
            WritingStyleResolution afterReset = scope.Component.ResolveWritingStyleFor(pawn);
            PawnDiaryRimTestScope.Require(
                afterReset.source == WritingStyleRuleSource.BaseStyle,
                "After the owner reset, the base style should be effective, but the source was " + afterReset.source + ".");
            RequireBasePickerUnchanged(afterReset);

            string prompt = FireInspirationAndCapture();
            RequirePromptCarriesRule(prompt, afterReset.rule);
        }

        // ----- layer setters (each undone through RegisterCleanup) --------------------------------------

        private static void SetExternalOverride(string rule)
        {
            PawnDiaryRimTestScope.Require(
                scope.Component.SetExternalWritingStyleOverride(pawn, ExternalSourceId, rule),
                "Failed to set the external writing-style override.");
            scope.RegisterCleanup(() => scope.Component.ResetExternalWritingStyleOverride(pawn, ExternalSourceId));
        }

        private static void SetCustomOverride(string rule)
        {
            PawnDiaryRimTestScope.Require(
                scope.Component.SetCustomWritingStyleRule(pawn, rule),
                "Failed to set the pawn-custom writing-style prompt.");
            scope.RegisterCleanup(() => scope.Component.SetCustomWritingStyleRule(pawn, string.Empty));
        }

        private static Hediff AddDrunkHediff()
        {
            HediffDef drunkDef = RequireDef<HediffDef>(DrunkHediffDefName);
            Hediff hediff = HediffMaker.MakeHediff(drunkDef, pawn);
            hediff.Severity = DrunkSeverity;
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));
            pawn.health.AddHediff(hediff);
            return hediff;
        }

        private static void RestoreBasePicker()
        {
            // Both the record and these edits are removed at teardown; restoring is defensive so no test
            // owned base-picker mutation can survive if the record-removal step is ever skipped.
            scope.Component.SetPersona(pawn, originalBaseStyleDefName);
            scope.Component.SetWritingStylePinned(pawn, originalWritingStylePinned);
        }

        // ----- shared assertions/helpers ----------------------------------------------------------------

        private static void RequireBasePickerUnchanged(WritingStyleResolution resolution)
        {
            PawnDiaryRimTestScope.Require(
                string.Equals(resolution.baseStyleDefName, knownBaseStyleDefName, StringComparison.Ordinal),
                "The saved base writing-style picker changed while a higher layer was active/removed.");
        }

        // The winning rule is injected verbatim as the {0} argument of "PawnDiary.Prompt.PersonaVoice", so
        // the exact (trimmed) rule string must be a substring of the assembled prompt. On failure the whole
        // captured prompt and the effective rule are dumped, so it is visible whether the persona voice
        // block is absent (empty personaRule) or carries a different rule than generation resolved.
        private static void RequirePromptCarriesRule(string prompt, string effectiveRule)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(effectiveRule)
                    && prompt.IndexOf(effectiveRule.Trim(), StringComparison.Ordinal) >= 0,
                "The effective writing-style rule did not reach the captured prompt's persona voice block.\n"
                + "Effective rule: [" + (effectiveRule ?? "<null>") + "]\n"
                + "Captured prompt:\n" + (prompt ?? "<null>"));
        }

        private static void RequirePromptContains(string prompt, string needle, string label)
        {
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "The captured prompt did not contain the " + label + ".");
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

        private static void RemoveHediffIfPresent(Pawn subject, Hediff hediff)
        {
            if (subject?.health?.hediffSet?.hediffs != null
                && hediff != null
                && subject.health.hediffSet.hediffs.Contains(hediff))
            {
                subject.health.RemoveHediff(hediff);
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
