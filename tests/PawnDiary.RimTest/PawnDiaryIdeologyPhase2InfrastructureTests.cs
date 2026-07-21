// Loaded-game fixtures for the first Ideology Phase 2 infrastructure slice. These tests invoke the
// real Pawn_IdeoTracker methods patched by production, inspect their detached before/after facts, and
// prove exact ability ownership leaves the downstream PlayLog interaction as the sole diary page.
// Every cache, tuning value, setting, PlayLog row, temporary Harmony patch, and disposable pawn is
// restored by finally-backed teardown or an explicit try/finally block.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises optional-DLC wiring, mutation coalescing, failure isolation, and ownership.</summary>
    [TestSuite]
    public static class PawnDiaryIdeologyPhase2InfrastructureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Ideology Phase 2 infrastructure] ";
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static Pawn otherPawn;
        private static DiaryTuningDef tuning;
        private static float originalMinimumChance;
        private static float originalMaximumChance;
        private static float originalGenerationWeight;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("conversion", "heartfelt", "abilityUsed");
            pawn = scope.CreateAdultColonist();
            otherPawn = scope.CreateAdultColonist();
            BeliefMutationCache.Reset();
            scope.RegisterCleanup(BeliefMutationCache.Reset);

            tuning = DiaryTuning.Current;
            originalMinimumChance = tuning.abilityUseMinChance;
            originalMaximumChance = tuning.abilityUseMaxChance;
            originalGenerationWeight = PawnDiaryMod.Settings.generationChanceWeight;
            tuning.abilityUseMinChance = 1f;
            tuning.abilityUseMaxChance = 1f;
            PawnDiaryMod.Settings.generationChanceWeight = 1f;
            scope.RegisterCleanup(() =>
            {
                if (tuning != null)
                {
                    tuning.abilityUseMinChance = originalMinimumChance;
                    tuning.abilityUseMaxChance = originalMaximumChance;
                }
                if (PawnDiaryMod.Settings != null)
                    PawnDiaryMod.Settings.generationChanceWeight = originalGenerationWeight;
            });
        }

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
                otherPawn = null;
                tuning = null;
            }
        }

        /// <summary>
        /// Verifies all three exact signatures are owned by the production patch when Ideology is
        /// active, and that registration plus live projection remain inert in a no-DLC profile.
        /// </summary>
        [Test]
        public static void ExactHarmonyWiringMatchesOptionalDlcState()
        {
            MethodBase conversion = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.IdeoConversionAttempt),
                new[] { typeof(float), typeof(Ideo), typeof(bool) });
            MethodBase offset = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.OffsetCertainty), new[] { typeof(float) });
            MethodBase setIdeology = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.SetIdeo), new[] { typeof(Ideo) });
            PawnDiaryRimTestScope.Require(conversion != null && offset != null && setIdeology != null,
                "The installed RimWorld assembly did not expose the verified Ideology signatures.");

            bool active = ModsConfig.IdeologyActive;
            PawnDiaryRimTestScope.Require(
                DiaryIdeologyMutationPatches.ConversionAttemptHookReady == active
                    && DiaryIdeologyMutationPatches.OffsetCertaintyHookReady == active
                    && DiaryIdeologyMutationPatches.SetIdeologyHookReady == active,
                "Ideology mutation readiness did not match ModsConfig.IdeologyActive.");
            AssertOwnedPatchState(conversion, active);
            AssertOwnedPatchState(offset, active);
            AssertOwnedPatchState(setIdeology, active);

            BeliefMutationState state;
            bool captured = DlcContext.TryCaptureBeliefMutationState(pawn.ideo, out state);
            if (!active)
            {
                PawnDiaryRimTestScope.Require(!captured && state == null && BeliefMutationCache.Count == 0,
                    "The mutation adapter/cache must be inert without Ideology.");
                AssertNoDlcConvertAbilityKeepsOrdinaryRoute();
                return;
            }

            PawnDiaryRimTestScope.Require(captured && state != null,
                "An Ideology-active pawn did not project a detached mutation state.");
        }

        /// <summary>
        /// Invokes real SetIdeo and OffsetCertainty methods. Nested calls must coalesce to one exact
        /// earliest-before/latest-after row; a later sequential call remains a separate row.
        /// </summary>
        [Test]
        public static void RealTrackerCallsCaptureBeforeAfterAndCoalesceOnlyNestedWork()
        {
            if (!RequireActiveTracker()) return;
            Ideo beforeIdeology = EnsureCurrentIdeology();
            Ideo afterIdeology = FindDifferentIdeology(beforeIdeology);
            if (afterIdeology == null)
            {
                Log.Message(LogPrefix + "before/after SetIdeo: no second loaded ideoligion available.");
                return;
            }

            BeliefMutationCache.Reset();
            pawn.ideo.SetIdeo(afterIdeology);
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            BeliefMutationSnapshot changed = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(changed != null && changed.ideologyChanged,
                "Real SetIdeo did not leave a detached ideology-change row.");
            PawnDiaryRimTestScope.Require(changed.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && changed.afterIdeologyId == afterIdeology.GetUniqueLoadID(),
                "SetIdeo did not preserve the actual before/after ideoligion identities.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "Nested work inside one SetIdeo call did not coalesce to one row.");

            float offset = pawn.ideo.Certainty > 0.5f ? -0.05f : 0.05f;
            float certaintyBeforeOffset = pawn.ideo.Certainty;
            pawn.ideo.OffsetCertainty(offset);
            BeliefMutationSnapshot certainty = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(certainty != null && certainty.certaintyChanged,
                "Real OffsetCertainty did not leave a detached certainty-change row.");
            PawnDiaryRimTestScope.Require(
                Math.Abs(certainty.beforeCertainty - certaintyBeforeOffset) < 0.0001f
                    && Math.Abs(certainty.afterCertainty - pawn.ideo.Certainty) < 0.0001f,
                "OffsetCertainty did not preserve its actual before/after certainty facts.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 2,
                "A sequential same-tick certainty action was incorrectly merged into SetIdeo.");
        }

        /// <summary>
        /// Calls the real conversion boundary and verifies its return value, attempted Ideo, earliest
        /// before state, and final live after state survive any nested certainty/SetIdeo calls as one row.
        /// </summary>
        [Test]
        public static void RealConversionAttemptOwnsOneCoalescedMechanicalResult()
        {
            if (!RequireActiveTracker()) return;
            Ideo beforeIdeology = EnsureCurrentIdeology();
            Ideo attemptedIdeology = FindDifferentIdeology(beforeIdeology);
            if (attemptedIdeology == null)
            {
                Log.Message(LogPrefix + "conversion attempt: no second loaded ideoligion available.");
                return;
            }

            BeliefMutationCache.Reset();
            float beforeCertainty = pawn.ideo.Certainty;
            bool result = pawn.ideo.IdeoConversionAttempt(0.05f, attemptedIdeology,
                applyCertaintyFactor: false);
            BeliefMutationSnapshot mutation = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, DiaryBeliefPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null && mutation.conversionSucceeded == result,
                "Real IdeoConversionAttempt did not retain its exact bool result.");
            PawnDiaryRimTestScope.Require(
                mutation.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && mutation.attemptedIdeologyId == attemptedIdeology.GetUniqueLoadID(),
                "Conversion capture lost its actual before/attempted ideoligion identities.");
            PawnDiaryRimTestScope.Require(Math.Abs(mutation.beforeCertainty - beforeCertainty) < 0.0001f
                    && mutation.afterIdeologyId == pawn.ideo.Ideo.GetUniqueLoadID()
                    && Math.Abs(mutation.afterCertainty - pawn.ideo.Certainty) < 0.0001f,
                "Conversion capture did not preserve its final live ideology/certainty facts.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.ConversionAttempt),
                "Nested conversion work did not coalesce under one conversion-owned row.");
        }

        /// <summary>
        /// Forces the DlcContext mutation projection seam to throw during a real vanilla method call.
        /// Vanilla certainty must still change, no partial cache state may survive, and the temporary
        /// patch is always removed in finally.
        /// </summary>
        [Test]
        public static void ProjectionFailurePreservesVanillaMutationAndLeavesNoPartialState()
        {
            if (!RequireActiveTracker()) return;
            EnsureCurrentIdeology();
            BeliefMutationCache.Reset();
            MethodInfo target = AccessTools.Method(typeof(DlcContext),
                nameof(DlcContext.TryCaptureBeliefMutationState),
                new[] { typeof(Pawn_IdeoTracker), typeof(BeliefMutationState).MakeByRefType() });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the guarded mutation projection seam.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.IdeologyMutationFailure."
                + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(PawnDiaryIdeologyPhase2InfrastructureTests), nameof(ThrowMutationProjection)));
                float before = pawn.ideo.Certainty;
                float offset = before > 0.5f ? -0.05f : 0.05f;
                pawn.ideo.OffsetCertainty(offset);
                PawnDiaryRimTestScope.Require(Math.Abs(pawn.ideo.Certainty - before) > 0.0001f,
                    "A Pawn Diary projection failure prevented vanilla OffsetCertainty.");
                PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                    "A failed projection left a partial mutation cache row.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
        }

        /// <summary>
        /// The exact Convert ability route is dropped before random sampling, while its real vanilla
        /// Convert_Success PlayLog row creates exactly one pair page through the existing Harmony hook.
        /// </summary>
        [Test]
        public static void ConvertAbilityAndDownstreamInteractionProduceOneCanonicalPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "canonical Convert route: not applicable (Ideology inactive). ");
                return;
            }
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("Convert_Success");
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Convert_Success.");
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Convert"));
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, otherPawn);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the downstream Convert_Success PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid));
                Find.PlayLog.Add(entry);
            }, "Convert_Success", pawn, otherPawn, rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, pawn, otherPawn);

            Rand.PushState(847231);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(847231);
            try
            {
                scope.RequireNoNewEvent(() => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)));
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "The downstream-covered ability consumed Rand before it was dropped.");
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void AssertNoDlcConvertAbilityKeepsOrdinaryRoute()
        {
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Convert"));
            DiaryEvent page = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)),
                "Convert", pawn, null, rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, pawn);
        }

        private static void AssertOwnedPatchState(MethodBase target, bool expected)
        {
            Patches info = Harmony.GetPatchInfo(target);
            bool prefix = info?.Prefixes.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            bool postfix = info?.Postfixes.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            bool finalizer = info?.Finalizers.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            PawnDiaryRimTestScope.Require(prefix == expected && postfix == expected && finalizer == expected,
                "Production Ideology mutation patch ownership did not match DLC availability for "
                    + target.Name + ".");
        }

        private static bool RequireActiveTracker()
        {
            if (ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(pawn?.ideo != null,
                    "An Ideology-active fixture pawn did not have a tracker.");
                return true;
            }
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                "The mutation cache must remain empty without Ideology.");
            Log.Message(LogPrefix + "tracker mutation: not applicable (Ideology inactive). ");
            return false;
        }

        private static Ideo EnsureCurrentIdeology()
        {
            Ideo current = pawn.ideo.Ideo;
            if (current != null) return current;
            Ideo loaded = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(value => value != null);
            PawnDiaryRimTestScope.Require(loaded != null,
                "The Ideology-active fixture did not expose an ideoligion.");
            pawn.ideo.SetIdeo(loaded);
            BeliefMutationCache.Reset();
            return loaded;
        }

        private static Ideo FindDifferentIdeology(Ideo current)
        {
            return Find.IdeoManager?.IdeosListForReading?
                .FirstOrDefault(value => value != null && !ReferenceEquals(value, current));
        }

        private static AbilityDef BuildSyntheticAbilityDef(string defName)
        {
            return new AbilityDef
            {
                defName = defName,
                label = "ideology infrastructure fixture",
                abilityClass = typeof(Ability),
                comps = new List<AbilityCompProperties>(),
                cooldownTicksRange = new IntRange(2500, 2500),
                hostile = false,
                verbProperties = new VerbProperties
                {
                    verbClass = typeof(Verb_CastAbility),
                    isPrimary = true
                }
            };
        }

        private static bool ThrowMutationProjection()
        {
            throw new InvalidOperationException("Synthetic Ideology mutation projection failure.");
        }
    }
}
