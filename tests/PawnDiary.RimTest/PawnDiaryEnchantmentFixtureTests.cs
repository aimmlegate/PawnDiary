// Prompt-capture fixture for Pawn Diary's §5.1 PROMPT ENCHANTMENTS (TEST_COVERAGE_PLAN.md §5).
//
// A prompt enchantment adds ONE optional "important context" line to a first-person diary prompt,
// carrying a live health/status hint (a hediff, a low capacity, or a DLC-safe royal/ideoligion status).
// XML (1.6/Defs/DiaryPromptEnchantmentDefs.xml) owns which sources are eligible and how strongly each
// is weighted; the runtime collects matching candidates from the live pawn, rolls each one's chance,
// then a weighted pick chooses the single line that reaches the model. Neutral chronicle/title
// templates set includePromptEnchantment=false, so no enchantment line ever reaches those prompts.
//
// This suite has two kinds of test:
//   (1) LOADED-DEF VALIDATION over DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading: every
//       shipped def has a unique, non-blank identity; a valid source; an actionable rule (a source that
//       can actually produce a line); chance/weight/severity in range; and DLC-safe string references
//       (every matcher is a plain string, so collecting against a real pawn never crashes without DLC).
//   (2) LIVE CAPTURE on a generating pawn: force one enchantment to fire deterministically, capture the
//       first-person prompt, and assert its cue text appears; then confirm the neutral arrival template
//       (includePromptEnchantment=false) captures NO enchantment cue even with the same forced state.
//
// SELECTION SEAM used to make the live fire deterministic (never retry-until-random-success):
//   * enablePromptEnchantments is forced true (snapshot/restored).
//   * The target def (DiaryEnchant_AmbrosiaHigh) has NO hediffSeverityTiers, so its own `chance` is the
//     effective chance (PromptEnchantmentCollector.TuningFor -> ResolvedChance). We set chance=1 (and
//     frequency=-1 so the alias cannot shadow it) and visibleOnly=false, so the candidate ALWAYS passes
//     its roll regardless of the harness's isolated RNG. All three fields are restored in cleanup.
//   * The pawn is given exactly one matching visible hediff (AmbrosiaHigh). An otherwise-healthy
//     generated colonist has no other matching hediff, no low-consciousness capacity candidate, and no
//     royal/ideoligion status, so the weighted pool holds exactly ONE candidate -> PickWeighted returns
//     it deterministically and its cue text is guaranteed to render.
//
// No LLM request can leave the game: prompt-test mode (EnablePromptCapture) renders and stores each
// prompt and stops before any network call; the harness restores DevMode/promptTestMode/contextDetailLevel
// and audits for leaks in teardown. Every mutation here (settings flag, the three def fields, the added
// hediff, the started inspiration) is undone through RegisterCleanup.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves §5.1 prompt enchantments: the shipped enchantment Defs are well-formed and DLC-safe, a
    /// forced live hediff enchantment renders its cue into the captured first-person prompt, and the
    /// neutral arrival template emits no enchantment cue. Requires a loaded game (the capture pipeline
    /// ignores events at the main menu). Prompt-test mode guarantees no LLM request leaves the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryEnchantmentFixtureTests
    {
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";
        private const string InspirationReason = "Struck by sudden RimTest inspiration";

        // The forced enchantment: DiaryEnchant_AmbrosiaHigh has explicit conditionKey + cueKeys and NO
        // severity tiers, so def.chance is the effective chance and its cue text is stable, unique, and
        // enchantment-only (it never appears in the neutral/vanilla context fields).
        private const string EnchantDefName = "DiaryEnchant_AmbrosiaHigh";
        private const string EnchantHediffDefName = "AmbrosiaHigh";

        // A cue phrase produced ONLY by the DiaryEnchant_AmbrosiaHigh enchantment
        // (PawnDiary.Prompt.Health.Cue.SoftMoodLift in the English Keyed file). Asserting on this
        // enchantment-only phrase avoids false positives from the pawn's ordinary health/mood context.
        private const string EnchantCueNeedle = "soft mood lift";

        // A deterministic founding-arrival context (mirrors the §4.2 neutral fixture) so the neutral
        // capture is a stable boundary-fact note rather than depending on the pawn's rolled backstory.
        private const string StartingArrivalContext =
            "arrival_source=game_start; scenario_name=TestCrashlanded; childhood_backstory=TestWanderer";

        // The four source kinds the collector understands (PromptEnchantmentCollector.IsCapacitySource /
        // IsRoyalTitleSource / IsIdeologyRoleSource; "Hediff" is the default for everything else).
        private static readonly HashSet<string> ValidSources =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hediff", "Capacity", "RoyalTitle", "IdeologyRole" };

        // The fixed hediff-severity tier levels the collector honors (PromptEnchantmentCollector.TierForHediffSeverity).
        private static readonly HashSet<string> ValidTierLevels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minor", "moderate", "major", "critical" };

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a scope with the inspiration group (drives the first-person capture) and the arrival
        /// group (drives the neutral capture), turns on prompt capture at the Full preset BEFORE creating
        /// any pawn, and generates one generation-ENABLED colonist. Prompt-test mode makes every fired
        /// event capture prompt-only, so no request can leave the game.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey, ArrivalSignal.ArrivalGroupKey);
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateGeneratingAdultColonist();
        }

        /// <summary>
        /// Restores every mutation (DevMode/promptTestMode/contextDetailLevel, group toggles, the forced
        /// settings flag / def fields, the added hediff, the started inspiration) and audits for leaks —
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
        /// §5.1 loaded-Def validation. Every shipped DiaryPromptEnchantmentDef has a unique non-blank
        /// defName, a valid source, an actionable rule (a source that can produce a context line),
        /// chance/weight/severity within range, well-formed severity tiers, and DLC-safe string
        /// references — proven behaviorally by collecting candidates for every def against a real pawn
        /// without an exception (all matchers are plain strings, so absent DLC content is simply inert).
        /// </summary>
        [Test]
        public static void LoadedEnchantmentDefsAreWellFormedAndDlcSafe()
        {
            List<DiaryPromptEnchantmentDef> defs = DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading;
            PawnDiaryRimTestScope.Require(
                defs != null && defs.Count > 0,
                "No DiaryPromptEnchantmentDefs were loaded; the enchantment layer would be inert.");

            HashSet<string> seenDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < defs.Count; i++)
            {
                DiaryPromptEnchantmentDef def = defs[i];
                PawnDiaryRimTestScope.Require(def != null, "A null DiaryPromptEnchantmentDef was loaded.");

                // Unique, non-blank identity.
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(def.defName),
                    "A DiaryPromptEnchantmentDef has a blank defName.");
                PawnDiaryRimTestScope.Require(
                    seenDefNames.Add(def.defName),
                    "Duplicate DiaryPromptEnchantmentDef defName '" + def.defName + "'.");

                // Valid source.
                string source = string.IsNullOrWhiteSpace(def.source) ? "Hediff" : def.source;
                PawnDiaryRimTestScope.Require(
                    ValidSources.Contains(source),
                    "Enchantment '" + def.defName + "' has an unknown source '" + def.source + "'.");

                // Actionable rule: the def's source must be able to produce a context line. Capacity and
                // important-event (royal/ideoligion) sources carry their own text; a hediff source is
                // dead unless it lists at least one non-blank hediff defName to match.
                bool isCapacity = !string.IsNullOrWhiteSpace(def.capacityDefName)
                    || string.Equals(source, "Capacity", StringComparison.OrdinalIgnoreCase);
                bool isImportantEvent = string.Equals(source, "RoyalTitle", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source, "IdeologyRole", StringComparison.OrdinalIgnoreCase);
                if (!isCapacity && !isImportantEvent)
                {
                    PawnDiaryRimTestScope.Require(
                        def.hediffDefNames != null && def.hediffDefNames.Count > 0,
                        "Hediff enchantment '" + def.defName + "' lists no hediffDefNames and can never match.");
                    for (int h = 0; h < def.hediffDefNames.Count; h++)
                    {
                        PawnDiaryRimTestScope.Require(
                            !string.IsNullOrWhiteSpace(def.hediffDefNames[h]),
                            "Hediff enchantment '" + def.defName + "' has a blank hediffDefNames entry.");
                    }
                }

                // chance / weight / severity in range. The effective chance is frequency when set (>=0),
                // else chance; both must stay a probability. weight/severity are multipliers (>= 0).
                float effectiveChance = def.frequency >= 0f ? def.frequency : def.chance;
                RequireProbability(effectiveChance, "Enchantment '" + def.defName + "' effective chance");
                PawnDiaryRimTestScope.Require(
                    def.weight >= 0f && !float.IsNaN(def.weight),
                    "Enchantment '" + def.defName + "' has a negative/NaN weight.");
                PawnDiaryRimTestScope.Require(
                    def.severity >= 0f && !float.IsNaN(def.severity),
                    "Enchantment '" + def.defName + "' has a negative/NaN severity.");
                PawnDiaryRimTestScope.Require(
                    def.minHediffSeverity >= 0f && !float.IsNaN(def.minHediffSeverity),
                    "Enchantment '" + def.defName + "' has a negative/NaN minHediffSeverity.");

                // Capacity windows, when both bounds are set, must form a real (min < max) range.
                if (def.minCapacity >= 0f && def.maxCapacity >= 0f)
                {
                    PawnDiaryRimTestScope.Require(
                        def.minCapacity < def.maxCapacity,
                        "Enchantment '" + def.defName + "' has an inverted capacity window.");
                }

                // Severity tiers: known level names and probability-valid overrides.
                if (def.hediffSeverityTiers != null)
                {
                    for (int t = 0; t < def.hediffSeverityTiers.Count; t++)
                    {
                        PromptEnchantmentSeverityTier tier = def.hediffSeverityTiers[t];
                        PawnDiaryRimTestScope.Require(
                            tier != null && ValidTierLevels.Contains(tier.level ?? string.Empty),
                            "Enchantment '" + def.defName + "' has a severity tier with an unknown level '"
                            + (tier == null ? "<null>" : tier.level) + "'.");

                        // Negative tier fields mean "inherit the parent Def"; only set values are checked.
                        if (tier.chance >= 0f)
                        {
                            RequireProbability(tier.chance, "Enchantment '" + def.defName + "' tier chance");
                        }

                        if (tier.frequency >= 0f)
                        {
                            RequireProbability(tier.frequency, "Enchantment '" + def.defName + "' tier frequency");
                        }

                        PawnDiaryRimTestScope.Require(
                            (tier.weight < 0f || !float.IsNaN(tier.weight))
                            && (tier.severity < 0f || !float.IsNaN(tier.severity)),
                            "Enchantment '" + def.defName + "' has a NaN severity-tier multiplier.");
                    }
                }
            }

            // DLC-safe behavioral proof: collecting candidates for EVERY loaded def against a real pawn
            // resolves labels, body parts, capacities, DLC status helpers, and translation keys. Because
            // every matcher is a plain string and the DLC helpers are guarded, this must never throw even
            // when a referenced hediff/DLC is absent. includeImportantEventContext:true also exercises the
            // royal/ideoligion sources' DlcContext lookups.
            try
            {
                PromptEnchantmentCollector.Collect(pawn, defs, true, DiaryTuning.PromptEnchantmentTuning);
            }
            catch (Exception exception)
            {
                throw new AssertionException(
                    "Collecting prompt-enchantment candidates threw (a non-DLC-safe reference): " + exception);
            }
        }

        /// <summary>
        /// §5.1 live capture. Forces DiaryEnchant_AmbrosiaHigh to fire (layer enabled, chance=1,
        /// visibleOnly=false, one matching AmbrosiaHigh hediff = a single-candidate pool), fires a
        /// first-person inspiration event, and asserts the captured initiator prompt carries the
        /// enchantment's unique cue text.
        /// </summary>
        [Test]
        public static void ForcedHediffEnchantmentCueAppearsInFirstPersonPrompt()
        {
            SeedForcedAmbrosiaEnchantment();

            string prompt = FireInspirationAndCapture();
            RequireContains(prompt, EnchantCueNeedle, "first-person enchantment cue");
        }

        /// <summary>
        /// §5.1 neutral gate. With the SAME forced enchantment state active, the neutral arrival template
        /// (includePromptEnchantment=false) captures NO enchantment cue: firing a real arrival yields a
        /// neutral prompt that must not contain the AmbrosiaHigh cue, and the template flag is asserted
        /// directly as the shipped contract the render honors.
        /// </summary>
        [Test]
        public static void NeutralArrivalTemplateEmitsNoEnchantmentCue()
        {
            SeedForcedAmbrosiaEnchantment();

            // The ArrivalDescription template opts out of prompt enchantment in XML.
            DiaryPromptTemplateDef arrivalTemplate = DiaryPromptTemplates.ForKey(DiaryPromptTemplates.ArrivalDescription);
            PawnDiaryRimTestScope.Require(
                arrivalTemplate != null,
                "The ArrivalDescription prompt template was not loaded.");
            PawnDiaryRimTestScope.Require(
                !arrivalTemplate.includePromptEnchantment,
                "The ArrivalDescription template must set includePromptEnchantment=false.");

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, StartingArrivalContext)),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);

            // The neutral arrival prompt is captured synchronously on emit; assert the enchantment cue is
            // absent even though the pawn carries the forced-firing hediff.
            string prompt = scope.CapturedPrompt(arrival, DiaryEvent.NeutralRole);
            RequireNotContains(prompt, EnchantCueNeedle, "neutral arrival prompt");
        }

        // ----- seam / helpers ------------------------------------------------------------------------

        /// <summary>
        /// Deterministically arms exactly one prompt enchantment on the test pawn: enables the layer,
        /// forces DiaryEnchant_AmbrosiaHigh to chance=1 / visibleOnly=false (it has no severity tiers, so
        /// its own chance is the effective chance), and adds one matching AmbrosiaHigh hediff. All four
        /// mutations are undone in reverse order through RegisterCleanup.
        /// </summary>
        private static void SeedForcedAmbrosiaEnchantment()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            PawnDiaryRimTestScope.Require(settings != null, "Pawn Diary settings must be loaded.");

            bool originalEnabled = settings.enablePromptEnchantments;
            settings.enablePromptEnchantments = true;
            scope.RegisterCleanup(() => settings.enablePromptEnchantments = originalEnabled);

            DiaryPromptEnchantmentDef def = RequireDef<DiaryPromptEnchantmentDef>(EnchantDefName);
            float originalChance = def.chance;
            float originalFrequency = def.frequency;
            bool originalVisibleOnly = def.visibleOnly;
            def.chance = 1f;        // effective chance (no severity tiers) -> the candidate always passes
            def.frequency = -1f;    // keep the frequency alias from shadowing chance
            def.visibleOnly = false; // remove any dependency on the hediff's Visible flag
            scope.RegisterCleanup(() =>
            {
                def.chance = originalChance;
                def.frequency = originalFrequency;
                def.visibleOnly = originalVisibleOnly;
            });

            HediffDef ambrosia = RequireDef<HediffDef>(EnchantHediffDefName);
            Hediff hediff = HediffMaker.MakeHediff(ambrosia, pawn);
            hediff.Severity = 0.5f;
            pawn.health.AddHediff(hediff);
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));
        }

        // Fires one real inspiration on the generating pawn and returns the prompt the runtime rendered
        // and stored for the initiator POV. The started inspiration is ended in cleanup.
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
                        true);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the forced inspiration.");
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

        private static void RequireProbability(float value, string what)
        {
            PawnDiaryRimTestScope.Require(
                !float.IsNaN(value) && value >= 0f && value <= 1f,
                what + " (" + value + ") is not within [0, 1].");
        }

        private static void RequireContains(string haystack, string needle, string what)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(haystack)
                    && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0,
                "Expected the " + what + " to contain '" + needle + "'.");
        }

        private static void RequireNotContains(string haystack, string needle, string what)
        {
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(haystack)
                    || haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0,
                "Expected the " + what + " NOT to contain '" + needle + "'.");
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
