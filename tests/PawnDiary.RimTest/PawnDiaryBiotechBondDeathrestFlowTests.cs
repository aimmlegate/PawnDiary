// Compiled loaded-game acceptance for Biotech Phase 8. These fixtures exercise the real RimWorld
// method boundaries, but agents only build this assembly: the player runs RimTest in a disposable
// test colony when loaded-game acceptance is scheduled.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Real-boundary, baseline, routine-silence, and DLC-off fixtures for Phase 8.</summary>
    [TestSuite]
    public static class PawnDiaryBiotechBondDeathrestFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn first;
        private static Pawn second;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("biotechPsychicBondLifecycle");
            DiarySignalPolicyDef progression =
                DiarySignalPolicies.ForKey(DiarySignalPolicies.Progression);
            bool originalProgression = progression.enabled;
            progression.enabled = true;
            scope.RegisterCleanup(() => progression.enabled = originalProgression);
            EnableGroupForScope("biotechDeathrestInterrupted");
            first = scope.CreateAdultColonist();
            second = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(first);
            scope.SpawnAsLiveColonist(second);
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                scope = null;
                first = null;
                second = null;
                BiotechPsychicBondCorrelation.Clear();
                BiotechDeathrestCorrelation.Clear();
            }
        }

        /// <summary>Calls the real recursive BondTo and RemoveBond methods and expects one pair page each.</summary>
        [Test]
        public static void RealPsychicBondFormationAndRuptureEmitOneCanonicalPairEach()
        {
            if (!RequireBiotechOrSkip(
                nameof(RealPsychicBondFormationAndRuptureEmitOneCanonicalPairEach))) return;
            Gene_PsychicBonding firstGene;
            Gene_PsychicBonding secondGene;
            AddPsychicBondGenes(out firstGene, out secondGene);
            Pawn sortedFirst;
            Pawn sortedSecond;
            SortPawns(first, second, out sortedFirst, out sortedSecond);

            DiaryEvent formed = scope.FireAndRequireEvent(
                () => firstGene.BondTo(second),
                BiotechBondDeathrestEventDefNames.PsychicBondFormed,
                sortedFirst,
                sortedSecond,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(formed, sortedFirst, sortedSecond);
            RequireContext(formed, "psychic_bond=formed");
            scope.RequireNoEventForTestPawns("PsychicBond");

            DiaryEvent ruptured = scope.FireAndRequireEvent(
                () => firstGene.RemoveBond(),
                BiotechBondDeathrestEventDefNames.PsychicBondRuptured,
                sortedFirst,
                sortedSecond,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(ruptured, sortedFirst, sortedSecond);
            RequireContext(ruptured, "psychic_bond=ruptured");
            PawnDiaryRimTestScope.Require(
                ruptured.gameContext.IndexOf("cause=", StringComparison.Ordinal) < 0,
                "A direct RemoveBond call invented a rupture cause.");
            scope.RequireNoEventForTestPawns("PsychicBondTorn");
        }

        /// <summary>A partner without the gene still forms the two reciprocal hediffs vanilla requires.</summary>
        [Test]
        public static void RealSingleGeneFormationVerifiesMutualBond()
        {
            if (!RequireBiotechOrSkip(nameof(RealSingleGeneFormationVerifiesMutualBond))) return;
            Gene_PsychicBonding firstGene;
            Gene_PsychicBonding secondGene;
            AddPsychicBondGenes(out firstGene, out secondGene);
            second.genes.RemoveGene(secondGene);
            Pawn sortedFirst;
            Pawn sortedSecond;
            SortPawns(first, second, out sortedFirst, out sortedSecond);

            DiaryEvent formed = scope.FireAndRequireEvent(
                () => firstGene.BondTo(second),
                BiotechBondDeathrestEventDefNames.PsychicBondFormed,
                sortedFirst,
                sortedSecond,
                rejectOtherTestPawnEvents: true);
            PawnDiaryRimTestScope.Require(
                DlcContext.AreMutuallyPsychicBonded(first, second),
                "The one-gene boundary did not verify both reciprocal PsychicBond hediffs.");
            RequireContext(formed, "psychic_bond=formed");
        }

        /// <summary>Removing the owning gene proves gene_removed; unrelated removal scopes do not.</summary>
        [Test]
        public static void RealOwningGeneRemovalSuppliesExactCause()
        {
            if (!RequireBiotechOrSkip(nameof(RealOwningGeneRemovalSuppliesExactCause))) return;
            Gene_PsychicBonding firstGene;
            Gene_PsychicBonding secondGene;
            AddPsychicBondGenes(out firstGene, out secondGene);
            firstGene.BondTo(second);
            Pawn sortedFirst;
            Pawn sortedSecond;
            SortPawns(first, second, out sortedFirst, out sortedSecond);

            // One remaining psychic-bond gene still sustains the pair. Only removing the final
            // owner reaches vanilla's exact teardown boundary and proves gene_removed.
            scope.RequireNoNewEvent(() => first.genes.RemoveGene(firstGene));
            DiaryEvent ruptured = scope.FireAndRequireEvent(
                () => second.genes.RemoveGene(secondGene),
                BiotechBondDeathrestEventDefNames.PsychicBondRuptured,
                sortedFirst,
                sortedSecond,
                rejectOtherTestPawnEvents: true);
            RequireContext(ruptured, "cause=gene_removed");
        }

        /// <summary>Old-save migration observes a live mutual pair without creating a replay page.</summary>
        [Test]
        public static void ExistingMutualPairBaselinesSilently()
        {
            if (!RequireBiotechOrSkip(nameof(ExistingMutualPairBaselinesSilently))) return;
            Gene_PsychicBonding firstGene;
            Gene_PsychicBonding secondGene;
            AddPsychicBondGenes(out firstGene, out secondGene);
            firstGene.BondTo(second);
            BiotechPawnProgressionState state = BiotechState(first);
            state.bondObservationVersion = 0;
            state.psychicBondObservations.Clear();
            MethodInfo baseline = AccessTools.Method(
                typeof(DiaryGameComponent),
                "BaselineBiotechPsychicBondsOnLoad");
            PawnDiaryRimTestScope.Require(baseline != null,
                "Could not locate the Phase-8 loaded-save bond baseline.");

            scope.RequireNoNewEvent(() => baseline.Invoke(DiaryGameComponent.Instance, null));
            PawnDiaryRimTestScope.Require(
                state.bondObservationVersion == PsychicBondLifecyclePolicy.CurrentObservationVersion
                    && state.psychicBondObservations.Any(row =>
                        row != null
                        && row.partnerPawnId == second.GetUniqueLoadID()
                        && row.bonded),
                "The existing mutual pair was not silently baselined.");
        }

        /// <summary>Real Wake emits for a severe interruption and stays silent for a completed wake.</summary>
        [Test]
        public static void RealDeathrestWakeDistinguishesSevereInterruptionFromCompletion()
        {
            if (!RequireBiotechOrSkip(
                nameof(RealDeathrestWakeDistinguishesSevereInterruptionFromCompletion))) return;
            Gene_Deathrest severeGene = AddActiveDeathrest(first, deathrestTicks: 1);
            DiaryEvent interrupted = scope.FireAndRequireEvent(
                () => severeGene.Wake(),
                BiotechBondDeathrestEventDefNames.DeathrestInterrupted,
                first,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(interrupted, first);
            RequireContext(interrupted, "deathrest=interrupted");
            scope.RequireNoEventForTestPawns("InterruptedDeathrest");

            Gene_Deathrest completedGene = AddActiveDeathrest(second, deathrestTicks: int.MaxValue);
            scope.RequireNoNewEvent(() => completedGene.Wake());
        }

        /// <summary>Audits the exact installed RimWorld 1.6 method boundaries and Pawn Diary owners.</summary>
        [Test]
        public static void ExactPhase8HooksAreRegistered()
        {
            PawnDiaryRimTestScope.Require(
                AccessTools.DeclaredField(typeof(Gene_PsychicBonding), "bondedPawn") != null,
                "The installed 1.6 Gene_PsychicBonding.bondedPawn field is unavailable.");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(
                    typeof(Gene_PsychicBonding),
                    nameof(Gene_PsychicBonding.BondTo),
                    new[] { typeof(Pawn) }),
                typeof(BiotechPsychicBondFormationPatch));
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(
                    typeof(Gene_PsychicBonding),
                    nameof(Gene_PsychicBonding.RemoveBond),
                    Type.EmptyTypes),
                typeof(BiotechPsychicBondRupturePatch));
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(
                    typeof(Pawn_GeneTracker),
                    nameof(Pawn_GeneTracker.RemoveGene),
                    new[] { typeof(Gene) }),
                typeof(BiotechPsychicBondGeneRemovalPatch));
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(
                    typeof(Gene_PsychicBonding),
                    nameof(Gene_PsychicBonding.Notify_MyOrPartnersGeneRemoved),
                    Type.EmptyTypes),
                typeof(BiotechPsychicBondGeneRemovedTransitionPatch));
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(
                    typeof(Gene_Deathrest),
                    nameof(Gene_Deathrest.Wake),
                    Type.EmptyTypes),
                typeof(BiotechInterruptedDeathrestPatch));
        }

        /// <summary>Base-only profiles prove all guarded live adapters are ordinary inert calls.</summary>
        [Test]
        public static void DlcOffPhase8AdaptersAreInert()
        {
            if (ModsConfig.BiotechActive)
            {
                Log.Message("[Pawn Diary RimTest] SKIP DlcOffPhase8AdaptersAreInert: Biotech active.");
                return;
            }
            Pawn pawn;
            DeathrestMutationSnapshot deathrest;
            PawnDiaryRimTestScope.Require(DlcContext.PsychicBondPartner(first) == null,
                "The psychic-bond adapter returned a partner with Biotech inactive.");
            PawnDiaryRimTestScope.Require(
                DlcContext.PsychicBondMutationPartner(null) == null,
                "The private mutation adapter returned a partner with Biotech inactive.");
            PawnDiaryRimTestScope.Require(
                DlcContext.CapturePsychicBondMutation(
                    first,
                    second,
                    1,
                    PsychicBondPhaseTokens.Formed,
                    PsychicBondCauseTokens.Unknown,
                    false,
                    true,
                    Find.TickManager?.TicksGame ?? 0) == null,
                "The bond snapshot adapter produced DLC facts with Biotech inactive.");
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCaptureDeathrest(null, 0, out pawn, out deathrest)
                    && deathrest == null,
                "The deathrest snapshot adapter produced DLC facts with Biotech inactive.");
        }

        private static void AddPsychicBondGenes(
            out Gene_PsychicBonding firstGene,
            out Gene_PsychicBonding secondGene)
        {
            GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail("PsychicBonding");
            PawnDiaryRimTestScope.Require(def != null,
                "Biotech is active but the PsychicBonding GeneDef is unavailable.");
            firstGene = first.genes.AddGene(def, xenogene: true) as Gene_PsychicBonding;
            secondGene = second.genes.AddGene(def, xenogene: true) as Gene_PsychicBonding;
            PawnDiaryRimTestScope.Require(firstGene != null && secondGene != null,
                "Could not add both PsychicBonding gene fixtures.");
            Gene_PsychicBonding cleanupFirst = firstGene;
            Gene_PsychicBonding cleanupSecond = secondGene;
            scope.RegisterCleanup(() =>
            {
                if (DlcContext.PsychicBondPartner(first) != null) cleanupFirst?.RemoveBond();
                if (first?.genes?.GenesListForReading.Contains(cleanupFirst) == true)
                    first.genes.RemoveGene(cleanupFirst);
                if (second?.genes?.GenesListForReading.Contains(cleanupSecond) == true)
                    second.genes.RemoveGene(cleanupSecond);
            });
        }

        private static Gene_Deathrest AddActiveDeathrest(Pawn pawn, int deathrestTicks)
        {
            GeneDef geneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Deathrest");
            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("Deathrest");
            PawnDiaryRimTestScope.Require(geneDef != null && hediffDef != null,
                "Biotech is active but the Deathrest gene/hediff Def is unavailable.");
            Gene_Deathrest gene = pawn.genes.AddGene(geneDef, xenogene: true) as Gene_Deathrest;
            PawnDiaryRimTestScope.Require(gene != null,
                "Could not add the Deathrest gene fixture.");
            pawn.health.AddHediff(HediffMaker.MakeHediff(hediffDef, pawn));
            FieldInfo ticksField = AccessTools.Field(typeof(Gene_Deathrest), "deathrestTicks");
            PawnDiaryRimTestScope.Require(ticksField != null,
                "Gene_Deathrest.deathrestTicks changed from the verified 1.6 seam.");
            ticksField.SetValue(gene, deathrestTicks);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.genes?.GenesListForReading.Contains(gene) == true)
                    pawn.genes.RemoveGene(gene);
            });
            return gene;
        }

        private static BiotechPawnProgressionState BiotechState(Pawn pawn)
        {
            MethodInfo findDiary = AccessTools.Method(
                typeof(DiaryGameComponent),
                "FindDiary",
                new[] { typeof(Pawn), typeof(bool) });
            PawnDiaryRecord diary = findDiary?.Invoke(
                DiaryGameComponent.Instance,
                new object[] { pawn, false }) as PawnDiaryRecord;
            BiotechPawnProgressionState state =
                diary?.EnsureProgressionState().EnsureBiotechState();
            PawnDiaryRimTestScope.Require(state != null,
                "Expected saved Biotech state for the fixture pawn.");
            return state;
        }

        private static void EnableGroupForScope(string groupKey)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool original = settings.IsGroupEnabled(groupKey);
            settings.SetGroupEnabled(groupKey, true);
            scope.RegisterCleanup(() => settings.SetGroupEnabled(groupKey, original));
        }

        private static void SortPawns(
            Pawn left,
            Pawn right,
            out Pawn sortedFirst,
            out Pawn sortedSecond)
        {
            if (string.CompareOrdinal(left.GetUniqueLoadID(), right.GetUniqueLoadID()) <= 0)
            {
                sortedFirst = left;
                sortedSecond = right;
            }
            else
            {
                sortedFirst = right;
                sortedSecond = left;
            }
        }

        private static void RequireOwnedPatch(MethodBase target, Type patchType)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            IEnumerable<Patch> prefixes =
                patches == null ? Enumerable.Empty<Patch>() : patches.Prefixes;
            PawnDiaryRimTestScope.Require(
                prefixes.Any(row => row.owner == "aimml.pawndiary"
                    && row.PatchMethod?.DeclaringType == patchType),
                "Expected Pawn Diary's " + patchType.Name + " prefix on " + target + ".");
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Phase-8 event context omitted '" + fragment + "'.");
        }

        private static bool RequireBiotechOrSkip(string fixtureName)
        {
            if (ModsConfig.BiotechActive) return true;
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Biotech is not active in this test profile.");
            return false;
        }
    }
}
