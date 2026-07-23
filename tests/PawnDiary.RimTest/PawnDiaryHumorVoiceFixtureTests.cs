// Prompt-capture fixture for Pawn Diary's §5.3 HUMOR cue system (design/TEST_COVERAGE_PLAN.md §5).
//
// Humor is a hidden, always-on feature: on some fraction of eligible first-person entries the
// production pipeline folds ONE structural "writing-license" sentence (a humor cue) into the same
// voice block as the psychotype/writing-style text (DiaryPipelineAdapters.CombinedVoiceBlock ->
// HumorVoiceBlock, wrapped by the localized "PawnDiary.Prompt.HumorVoice" frame). The selector
// (Source/Generation/HumorCues.CueFor) rolls a stable per-event+POV seed against an effective chance
// of DiaryTuning.HumorChance * a non-cumulative temperament multiplier, then weighted-picks a cue
// from the Light (mundane) or Gallows (high-stakes) tier. This suite drives that end to end under
// Prompt Test Mode, which renders and STORES the exact prompt and stops before any LlmClient.Enqueue,
// so nothing leaves the game.
//
// Determinism (never a probabilistic retry loop): the effective chance is forced to exactly 1 or 0 by
// setting the XML-tuned DiaryTuningDef fields directly (humorChance plus the elevated/reduced
// multipliers and trait-key lists). With chance clamped to 1, Rand.Chance always fires a cue; clamped
// to 0 it never does. Temperament is likewise forced: the elevated case drives the multiplier from the
// pawn's OWN traits (copied into humorElevatedTraitKeys) with Social passion cleared so only the trait
// path elevates; the reduced case does the same into humorReducedTraitKeys with a 0 multiplier so the
// effective chance collapses to 0. Every mutated tuning field is snapshotted in SetUp and restored via
// RegisterCleanup; the temperament changes live on the test-owned pawn, which the harness destroys.
//
// Coverage (design/TEST_COVERAGE_PLAN.md §5.3): base forced-on emits a cue; forced-off emits none; an
// elevated-temperament writer's multiplier raises the effective chance to certainty; a reduced
// writer's multiplier suppresses it; and a template that opts out of persona (ArrivalDescription)
// carries no humor cue even with the chance forced to 1.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the humor-cue selector's forced-on / forced-off behavior and its temperament multiplier
    /// through real captured prompts: with the tuned chance forced to 1 a solo entry's prompt carries
    /// the localized humor-cue frame, with it forced to 0 it does not, an elevated-trait writer reaches
    /// certainty while a reduced-trait writer is suppressed, and a persona-opt-out template shows no cue
    /// even at chance 1. Requires a loaded game; Prompt Test Mode guarantees no LLM request leaves it.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryHumorVoiceFixtureTests
    {
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";
        private const string InspirationReason = "Struck by sudden RimTest inspiration";

        // A deterministic founding-arrival context for the opt-out template case. "arrival_source=
        // game_start" marks it a starting arrival so the ArrivalDescription template is selected; the
        // exact facts are irrelevant here (we only assert the humor cue is absent).
        private const string StartingArrivalContext =
            "arrival_source=game_start; scenario_name=TestCrashlanded; childhood_backstory=TestWanderer";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Snapshot of the humor tuning fields this suite mutates, captured per test in SetUp and
        // restored in teardown so the developer's real tuning is untouched after the suite.
        private static float originalHumorChance;
        private static float originalElevatedMultiplier;
        private static float originalReducedMultiplier;
        private static List<string> originalElevatedTraitKeys;
        private static List<string> originalReducedTraitKeys;

        /// <summary>
        /// Opens a scope with the inspiration and arrival groups enabled, turns on prompt capture BEFORE
        /// creating any pawn (so even the founding-arrival hook is captured prompt-only), generates one
        /// generating colonist, and snapshots the humor tuning fields for restore in teardown.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey, ArrivalSignal.ArrivalGroupKey);
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateGeneratingAdultColonist();

            // The temperament multiplier reads the writer's LIVE traits, so the pawn must be resolvable by
            // generation's live-pawn lookup (otherwise the multiplier defaults to 1 and the elevated/reduced
            // cases can't move it); spawning it as a colonist makes it both findable and eligible to start
            // the inspiration this suite fires.
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryRimTestScope.MakeCreativityInspirationEligible(pawn);
            SnapshotHumorTuning();
        }

        /// <summary>
        /// Restores DevMode/promptTestMode/contextDetailLevel and the humor tuning fields (via the
        /// registered cleanup) and audits that no test-owned event, diary, or log row survived.
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
                originalElevatedTraitKeys = null;
                originalReducedTraitKeys = null;
            }
        }

        /// <summary>
        /// §5.3. With the effective humor chance forced to 1 (base rate 1 and both temperament
        /// multipliers 1, so the writer's traits cannot change the outcome), a fired solo event's
        /// captured prompt carries the localized humor-cue frame — a cue was selected and folded into
        /// the voice block.
        /// </summary>
        [Test]
        public static void ForcedChanceEmitsHumorCue()
        {
            // Base rate 1, both multipliers 1 => effective chance is 1 for ANY temperament.
            SetHumorTuning(1f, 1f, 1f, null, null);

            string prompt = FireInspirationAndCapture(pawn);
            RequireContainsHumorFrame(prompt, "a forced humor chance of 1");
        }

        /// <summary>
        /// §5.3. With the base humor chance forced to 0, the same solo capture path renders a real
        /// prompt but no humor-cue frame — the selector rolled out and folded nothing into the voice
        /// block. This is the negative control for <see cref="ForcedChanceEmitsHumorCue"/>.
        /// </summary>
        [Test]
        public static void ZeroChanceEmitsNoHumorCue()
        {
            // Base rate 0 => effective chance is 0 regardless of the temperament multiplier.
            SetHumorTuning(0f, 1f, 1f, null, null);

            string prompt = FireInspirationAndCapture(pawn);
            RequireExcludesHumorFrame(prompt, "a forced humor chance of 0");
        }

        /// <summary>
        /// §5.3. An elevated-temperament writer's multiplier raises the effective chance above the base
        /// rate. Base 0.5 alone is not certainty, but the elevated ×2 multiplier clamps the effective
        /// chance to 1, so the captured prompt reliably carries the humor-cue frame. The elevation is
        /// driven purely by the trait path: the pawn's own trait keys are copied into the elevated list,
        /// the reduced list is emptied, and Social passion is cleared so no other qualifier interferes.
        /// </summary>
        [Test]
        public static void ElevatedTemperamentRaisesEffectiveChanceToCertainty()
        {
            ForceSocialPassionNone(pawn);
            List<string> traitKeys = RequireTraitKeys(pawn);

            // upbeatTrait=true (elevated list == pawn's traits), dourTrait=false (reduced list empty),
            // socialPassion=false => multiplier is the elevated 2. 0.5 * 2 -> clamped to 1.
            SetHumorTuning(0.5f, 2f, 1f, new List<string>(traitKeys), new List<string>());

            string prompt = FireInspirationAndCapture(pawn);
            RequireContainsHumorFrame(prompt, "an elevated-temperament writer at effective chance 1");
        }

        /// <summary>
        /// §5.3. A reduced-temperament writer's multiplier lowers the effective chance. With the base
        /// rate forced to 1 — which would otherwise always emit — the reduced ×0 multiplier collapses the
        /// effective chance to 0, so the captured prompt carries no humor-cue frame. The reduction is
        /// driven purely by the trait path (the pawn's own trait keys copied into the reduced list, the
        /// elevated list emptied, Social passion cleared), the mirror image of the elevated case.
        /// </summary>
        [Test]
        public static void ReducedTemperamentSuppressesHumorCue()
        {
            ForceSocialPassionNone(pawn);
            List<string> traitKeys = RequireTraitKeys(pawn);

            // dourTrait=true (reduced list == pawn's traits), upbeatTrait=false (elevated list empty),
            // socialPassion=false => multiplier is the reduced 0. 1 * 0 = 0.
            SetHumorTuning(1f, 1f, 0f, new List<string>(), new List<string>(traitKeys));

            string prompt = FireInspirationAndCapture(pawn);
            RequireExcludesHumorFrame(prompt, "a reduced-temperament writer at effective chance 0");
        }

        /// <summary>
        /// §5.3. Templates that opt out of the first-person voice apparatus never carry a humor cue, even
        /// with the effective chance forced to 1. The neutral ArrivalDescription template sets
        /// includePersona=false, so ComposeSystem drops the entire combined voice block (psychotype +
        /// writing style + humor) — the captured neutral prompt shows no humor-cue frame.
        /// </summary>
        [Test]
        public static void OptOutTemplateSuppressesHumorCue()
        {
            // Force the chance to certainty so the ONLY reason a cue would be absent is the opt-out.
            SetHumorTuning(1f, 1f, 1f, null, null);

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, StartingArrivalContext)),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);

            // The arrival neutral prompt is captured synchronously on emit.
            string prompt = scope.CapturedPrompt(arrival, DiaryEvent.NeutralRole);
            RequireExcludesHumorFrame(prompt, "the persona-opt-out ArrivalDescription template");

            // The mechanism: the template opts out of persona text, which drops the whole voice block.
            DiaryPromptTemplateDef template = DiaryPromptTemplates.ForKey(DiaryPromptTemplates.ArrivalDescription);
            PawnDiaryRimTestScope.Require(
                template != null,
                "The ArrivalDescription prompt template was not loaded.");
            PawnDiaryRimTestScope.Require(
                !template.includePersona,
                "The ArrivalDescription template must set includePersona=false so no humor/voice block reaches the model.");
        }

        // ----- helpers -------------------------------------------------------------------------------

        // Fires one real inspiration on the given writer and returns the prompt the runtime rendered and
        // stored for the initiator POV. Inspiration is a solo, internal-state event (SoloInternalState
        // template, includePersona=true), so its prompt carries the voice block where a humor cue lands.
        // The started inspiration is ended in cleanup.
        private static string FireInspirationAndCapture(Pawn writer)
        {
            InspirationDef inspirationDef = RequireDef<InspirationDef>(InspirationDefName);
            scope.RegisterCleanup(() => EndInspirationSafely(writer, inspirationDef));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = writer.mindState.inspirationHandler.TryStartInspiration(
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
                writer,
                null);

            return scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
        }

        // Asserts the captured prompt contains the localized humor-cue frame (i.e. a cue was folded in).
        private static void RequireContainsHumorFrame(string prompt, string forWhat)
        {
            string marker = HumorFrameMarker();
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(marker, StringComparison.Ordinal) >= 0,
                "Expected a humor cue in the prompt for " + forWhat + ", but the humor-cue frame ('"
                + marker + "') was absent.");
        }

        // Asserts the captured prompt does NOT contain the humor-cue frame.
        private static void RequireExcludesHumorFrame(string prompt, string forWhat)
        {
            string marker = HumorFrameMarker();
            PawnDiaryRimTestScope.Require(
                prompt.IndexOf(marker, StringComparison.Ordinal) < 0,
                "Expected NO humor cue in the prompt for " + forWhat + ", but the humor-cue frame ('"
                + marker + "') was present.");
        }

        // The stable, language-independent fixed prefix of the "PawnDiary.Prompt.HumorVoice" frame, i.e.
        // the text that precedes the {0} cue placeholder. Resolving it from the live keyed string (rather
        // than hardcoding a translated paragraph) keeps the assertion valid in any active language while
        // still matching only the humor frame, which is the sole place this text appears in a prompt.
        private static string HumorFrameMarker()
        {
            const string sentinel = "PAWNDIARY_HUMOR_CUE_SENTINEL";
            string frame = "PawnDiary.Prompt.HumorVoice".Translate(sentinel).Resolve();
            int placeholder = frame.IndexOf(sentinel, StringComparison.Ordinal);
            string prefix = (placeholder > 0 ? frame.Substring(0, placeholder) : frame).Trim();
            PawnDiaryRimTestScope.Require(
                prefix.Length >= 12,
                "The resolved humor-cue frame prefix was too short to be a reliable marker; "
                + "the 'PawnDiary.Prompt.HumorVoice' keyed string may have changed shape.");
            return prefix;
        }

        // Clears the writer's Social skill passion so it cannot elevate the humor chance, isolating the
        // trait-key path under test. The writer is a test-owned pawn destroyed in teardown, so this needs
        // no restore.
        private static void ForceSocialPassionNone(Pawn writer)
        {
            SkillRecord social = writer.skills?.GetSkill(SkillDefOf.Social);
            PawnDiaryRimTestScope.Require(
                social != null, "The test pawn had no Social skill record to neutralize.");
            social.passion = Passion.None;
        }

        // Returns the writer's own trait defNames (a non-empty list is required, so the temperament
        // multiplier tests have a concrete trait to match). A generated adult colonist always rolls at
        // least one trait; the explicit guard fails loudly rather than flaking if that ever changes.
        private static List<string> RequireTraitKeys(Pawn writer)
        {
            List<string> keys = new List<string>();
            List<Trait> traits = writer.story?.traits?.allTraits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    string defName = traits[i]?.def?.defName;
                    if (!string.IsNullOrEmpty(defName))
                    {
                        keys.Add(defName);
                    }
                }
            }

            PawnDiaryRimTestScope.Require(
                keys.Count > 0,
                "The generated test pawn had no traits to drive the temperament multiplier.");
            return keys;
        }

        // Snapshots the humor tuning fields and registers their restore. Called in SetUp so every test
        // starts from the real tuned values and teardown returns them, whatever the test changed.
        private static void SnapshotHumorTuning()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            originalHumorChance = tuning.humorChance;
            originalElevatedMultiplier = tuning.humorElevatedChanceMultiplier;
            originalReducedMultiplier = tuning.humorReducedChanceMultiplier;
            originalElevatedTraitKeys = tuning.humorElevatedTraitKeys;
            originalReducedTraitKeys = tuning.humorReducedTraitKeys;

            scope.RegisterCleanup(() =>
            {
                DiaryTuningDef current = DiaryTuning.Current;
                current.humorChance = originalHumorChance;
                current.humorElevatedChanceMultiplier = originalElevatedMultiplier;
                current.humorReducedChanceMultiplier = originalReducedMultiplier;
                current.humorElevatedTraitKeys = originalElevatedTraitKeys;
                current.humorReducedTraitKeys = originalReducedTraitKeys;
            });
        }

        // Forces the humor tuning fields for one test. Passing null for either trait-key list leaves the
        // snapshotted list in place (used when the base rate alone determines the outcome).
        private static void SetHumorTuning(
            float chance,
            float elevatedMultiplier,
            float reducedMultiplier,
            List<string> elevatedTraitKeys,
            List<string> reducedTraitKeys)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            tuning.humorChance = chance;
            tuning.humorElevatedChanceMultiplier = elevatedMultiplier;
            tuning.humorReducedChanceMultiplier = reducedMultiplier;
            if (elevatedTraitKeys != null)
            {
                tuning.humorElevatedTraitKeys = elevatedTraitKeys;
            }

            if (reducedTraitKeys != null)
            {
                tuning.humorReducedTraitKeys = reducedTraitKeys;
            }
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
