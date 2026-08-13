// In-game flow tests for Pawn Diary's ability-activation source (design/TEST_COVERAGE_PLAN.md §3, EVT-06).
//
// The production hook is a Harmony patch on Ability.Activate that does exactly one thing:
//   DiaryEvents.Submit(new AbilitySignal(ability, target, destination));
// (see Source/Ingestion/Sources/AbilitySignal.cs and Source/Patches — AbilityActivateLocalPatch).
// Activating a *real* ability in a headless RimTest would need a spawned caster, a live map, a valid
// target and — for every non-base-game power — an active DLC. So instead of casting an ability, these
// tests drive the identical entry point the patch drives: they build a real RimWorld Ability and hand
// its AbilitySignal to DiaryEvents.Submit. That exercises the whole capture path (eligibility → group
// classification → source dedup → semantic decision → shared frequency admission → AddSoloEvent)
// with no map and no DLC.
//
// To stay base-game-safe and fully deterministic the caster's ability is a synthetic AbilityDef built
// in code (never registered in DefDatabase, so nothing to leak). It is a plain non-psycast, non-hostile
// power, so InteractionGroups.ClassifyAbility routes it to the "abilityUsed" catch-all group. It carries
// a valid verbProperties + empty comps list because RimWorld's Ability.Initialize builds the primary
// verb and enumerates comps during construction; a null comps list or a missing verb would NullRef.
//
// Determinism (design/TEST_COVERAGE_PLAN.md §3, "inject a known seed or set effective chance to 0/1"):
// SetUp pins this group's shared multiplier to 1, and each test pins the native tuning min/max chance
// to 0 or 1. The shared gate is therefore forced without retry loops; cleanup restores every value.
//
// New to C#/RimWorld? See AGENTS.md. Everything the tests mutate is snapshot/restored by the shared
// PawnDiaryRimTestScope harness or by a RegisterCleanup below, so a failed assertion leaves no trace.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a successful ability activation reaches Pawn Diary's persisted event store as one
    /// solo page carrying the caster and target facts, that the cooldown-weighted sampling gate drops
    /// an activation when its effective chance is zero, that frequency rejection remains retryable,
    /// and that same-tick duplicate activations dedup down to a single event. Requires a loaded game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryAbilityFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn caster;
        private static Pawn target;

        // The synthetic power the caster "activates". Built once per test; never registered in a
        // DefDatabase, so it cannot leak into the loaded game or a save.
        private static AbilityDef abilityDef;

        // The live tuning def whose ability sampling knobs we pin for determinism, plus the values we
        // must put back. DiaryTuning.Current returns the loaded Diary_Tuning def (or a shared fallback);
        // either way it is the exact instance AbilitySignal reads, so mutating it forces the gate.
        private static DiaryTuningDef tuningDef;
        private static float originalMinChance;
        private static float originalMaxChance;
        private static Dictionary<string, float> originalFrequencyOverrides;

        // The stable defName the AbilitySignal stamps onto the DiaryEvent (Emit passes the AbilityDef's
        // defName straight to AddSoloEvent as interactionDefName — verified against production).
        private const string AbilityDefName = "PawnDiary_RimTest_Ability";

        /// <summary>
        /// Opens a scope with the Ability catch-all group enabled, creates an isolated caster and target,
        /// builds the synthetic ability, and forces deterministic shared admission (group multiplier 1,
        /// native tuning chances restored in cleanup).
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            // "abilityUsed" is the Ability-domain catch-all a plain utility power classifies to.
            scope = PawnDiaryRimTestScope.Begin("abilityUsed");
            caster = scope.CreateAdultColonist();
            target = scope.CreateAdultColonist();

            abilityDef = BuildSyntheticAbilityDef();

            // The shared harness snapshots enable toggles, not the new per-group frequency map. Freeze
            // this exact group at 1x so native 0/1 chances remain deterministic under any player preset.
            Dictionary<string, float> currentOverrides = PawnDiaryMod.Settings.groupFrequencyOverrides;
            originalFrequencyOverrides = currentOverrides == null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(currentOverrides, StringComparer.OrdinalIgnoreCase);
            tuningDef = DiaryTuning.Current;
            originalMinChance = tuningDef.abilityUseMinChance;
            originalMaxChance = tuningDef.abilityUseMaxChance;

            scope.RegisterCleanup(() =>
            {
                if (tuningDef != null)
                {
                    tuningDef.abilityUseMinChance = originalMinChance;
                    tuningDef.abilityUseMaxChance = originalMaxChance;
                }

                if (PawnDiaryMod.Settings != null)
                {
                    PawnDiaryMod.Settings.groupFrequencyOverrides = new Dictionary<string, float>(
                        originalFrequencyOverrides,
                    StringComparer.OrdinalIgnoreCase);
                }
            });

            PawnDiaryMod.Settings.SetGroupFrequencyOverride("abilityUsed", 1f);
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived —
        /// even when the test above threw partway through.
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
                caster = null;
                target = null;
                abilityDef = null;
                tuningDef = null;
                originalFrequencyOverrides = null;
            }
        }

        /// <summary>
        /// EVT-06. Submits one successful ability activation (effective chance forced to 1) and verifies
        /// the ability capture path records exactly one solo diary event for the caster whose context
        /// carries both the ability identity and the named target.
        /// </summary>
        [Test]
        public static void SuccessfulActivationRecordsSoloEventWithCasterAndTargetFacts()
        {
            ForceEffectiveChance(1f);
            Ability ability = new Ability(caster, abilityDef);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(
                    new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid)),
                AbilityDefName,
                caster,
                null);

            // Solo shape + caster refs (initiator is the caster, no recipient, diary index links it).
            scope.RequireSoloRef(diaryEvent, caster);

            // Caster fact: the ability identity marker leads the saved context.
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("ability=" + AbilityDefName, StringComparison.OrdinalIgnoreCase) >= 0,
                "The ability event context did not identify the activated ability.");

            // Target fact: the caster acted on the target pawn, so its label rides the context.
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("ability_target=", StringComparison.OrdinalIgnoreCase) >= 0
                    && diaryEvent.gameContext.IndexOf(target.LabelShortCap, StringComparison.OrdinalIgnoreCase) >= 0,
                "The ability event context did not record the activation's target pawn.");
        }

        /// <summary>
        /// EVT-06. With the cooldown-weighted effective chance forced to 0, the sampling gate must drop
        /// the activation: no diary event referencing a test pawn is created.
        /// </summary>
        [Test]
        public static void ZeroChanceSamplingGateDropsActivation()
        {
            ForceEffectiveChance(0f);
            Ability ability = new Ability(caster, abilityDef);

            scope.RequireNoNewEvent(
                () => DiaryEvents.Submit(
                    new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid)));
        }

        /// <summary>
        /// The signal must pass its exact classified group and unmodified cooldown chance into the
        /// shared gate, and a frequency miss must not consume its source dedup window.
        /// </summary>
        [Test]
        public static void SignalCarriesExactFrequencyContextAndRetryContract()
        {
            ForceEffectiveChance(0.25f);
            Ability ability = new Ability(caster, abilityDef);
            AbilitySignal signal = new AbilitySignal(
                ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid);
            CaptureContext context = signal.BuildContext();

            PawnDiaryRimTestScope.Require(context != null
                    && context.FrequencyGroupKey == "abilityUsed"
                    && context.FrequencyTier == "routine"
                    && Math.Abs(context.NativeCaptureChance - 0.25f) < 0.0001f,
                "AbilitySignal did not preserve its exact group and native cooldown chance.");
            PawnDiaryRimTestScope.Require(!signal.FrequencyRejectionConsumesDedup,
                "Ability frequency rejection unexpectedly consumes its dedup window.");
        }

        /// <summary>
        /// A zero-chance miss followed by a one-chance retry in the same tick must still record. If the
        /// first frequency rejection marked dedup, the second signal would be rejected as a duplicate.
        /// </summary>
        [Test]
        public static void FrequencyRejectionCanRetryWithinSameDedupWindow()
        {
            Ability ability = new Ability(caster, abilityDef);
            ForceEffectiveChance(0f);
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(
                new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid)));

            ForceEffectiveChance(1f);
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(
                    new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid)),
                AbilityDefName,
                caster,
                null);
            scope.RequireSoloRef(diaryEvent, caster);
        }

        /// <summary>
        /// EVT-06. Two identical activations in the same tick share the ability dedup key, so the second
        /// is dropped before it can record: the pair collapses to exactly one solo diary event.
        /// </summary>
        [Test]
        public static void DuplicateSameTickActivationsDedupToOneEvent()
        {
            ForceEffectiveChance(1f);
            Ability ability = new Ability(caster, abilityDef);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    // No tick advances between these two submits, so both signals build the same
                    // tick-stamped dedup key; the dispatcher's dedup CHECK drops the second.
                    DiaryEvents.Submit(
                        new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid));
                    DiaryEvents.Submit(
                        new AbilitySignal(ability, new LocalTargetInfo(target), LocalTargetInfo.Invalid));
                },
                AbilityDefName,
                caster,
                null);

            scope.RequireSoloRef(diaryEvent, caster);
        }

        // ----- helpers ----------------------------------------------------------------------------

        /// <summary>
        /// Pins the ability sampling chance deterministically. CooldownWeightedChance collapses to the
        /// shared value when min == max, so the cooldown length no longer matters. With the exact
        /// group multiplier pinned to 1 in SetUp, shared admission receives <paramref name="chance"/>.
        /// </summary>
        private static void ForceEffectiveChance(float chance)
        {
            tuningDef.abilityUseMinChance = chance;
            tuningDef.abilityUseMaxChance = chance;
        }

        /// <summary>
        /// Builds an in-memory, non-registered AbilityDef for a plain (non-psycast, non-hostile) power.
        /// The empty comps list and a Verb_CastAbility verbProperties are required so RimWorld's
        /// Ability.Initialize can enumerate comps and wire up the primary verb without a NullReference.
        /// No category/groupDef is set, so it classifies to the "abilityUsed" catch-all group.
        /// </summary>
        private static AbilityDef BuildSyntheticAbilityDef()
        {
            return new AbilityDef
            {
                defName = AbilityDefName,
                label = "test focus",
                abilityClass = typeof(Ability),
                comps = new List<AbilityCompProperties>(),
                cooldownTicksRange = new IntRange(2500, 2500),
                hostile = false,
                verbProperties = new VerbProperties
                {
                    verbClass = typeof(Verb_CastAbility),
                    isPrimary = true,
                },
            };
        }
    }
}
