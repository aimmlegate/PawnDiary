// Biotech Phase-8 Harmony boundaries. Every prefix freezes detached truth before vanilla mutates
// the pawn; every postfix verifies the exact result; finalizers release staged generic signals on
// failure. All hooks are inert when Biotech is unavailable.
using System;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Owns one canonical recursive Gene_PsychicBonding.BondTo pair transition.</summary>
    [HarmonyPatch(typeof(Gene_PsychicBonding), nameof(Gene_PsychicBonding.BondTo),
        new[] { typeof(Pawn) })]
    internal static class BiotechPsychicBondFormationPatch
    {
        private static void Prefix(
            Gene_PsychicBonding __instance,
            Pawn newBond,
            ref BiotechBondCallState __state)
        {
            if (!ModsConfig.BiotechActive || __instance?.pawn == null || newBond == null) return;
            BiotechBondCallState captured = null;
            DiaryPatchSafety.Run("BiotechPsychicBond.BondTo.Prefix", () =>
            {
                Pawn owner = __instance.pawn;
                DiaryGameComponent component = DiaryGameComponent.Instance;
                int tick = Find.TickManager?.TicksGame ?? 0;
                PsychicBondMutationSnapshot snapshot = DlcContext.CapturePsychicBondMutation(
                    owner,
                    newBond,
                    component?.NextPsychicBondEpoch(owner, newBond) ?? 1,
                    PsychicBondPhaseTokens.Formed,
                    PsychicBondCauseTokens.Unknown,
                    DlcContext.AreMutuallyPsychicBonded(owner, newBond),
                    mutuallyBondedAfter: false,
                    tick: tick);
                captured = BiotechPsychicBondCorrelation.Begin(owner, newBond, snapshot);
            });
            __state = captured;
        }

        private static void Postfix(BiotechBondCallState __state)
        {
            if (__state == null || !__state.isRoot) return;
            DiaryPatchSafety.Run("BiotechPsychicBond.BondTo.Postfix", () =>
            {
                __state.snapshot.mutuallyBondedAfter =
                    DlcContext.AreMutuallyPsychicBonded(__state.firstPawn, __state.secondPawn);
                if (DiaryGameComponent.Instance?.CompletePsychicBondTransition(
                    __state.firstPawn,
                    __state.secondPawn,
                    __state.snapshot) == true)
                {
                    BiotechBondDeathrestPolicySnapshot policy =
                        DiaryBiotechPolicy.Snapshot().bondDeathrest;
                    BiotechPsychicBondCorrelation.Commit(
                        __state,
                        Find.TickManager?.TicksGame ?? 0,
                        policy.psychicBondCorrelationExpiryTicks);
                }
            });
        }

        private static Exception Finalizer(Exception __exception, BiotechBondCallState __state)
        {
            DiaryPatchSafety.Run(
                "BiotechPsychicBond.BondTo.Finalizer",
                () => BiotechPsychicBondCorrelation.Close(__state));
            return __exception;
        }
    }

    /// <summary>Owns one frozen pair across Gene_PsychicBonding.RemoveBond recursion.</summary>
    [HarmonyPatch(typeof(Gene_PsychicBonding), nameof(Gene_PsychicBonding.RemoveBond),
        new Type[] { })]
    internal static class BiotechPsychicBondRupturePatch
    {
        private static void Prefix(
            Gene_PsychicBonding __instance,
            ref BiotechBondCallState __state)
        {
            if (!ModsConfig.BiotechActive || __instance?.pawn == null) return;
            BiotechBondCallState captured = null;
            DiaryPatchSafety.Run("BiotechPsychicBond.RemoveBond.Prefix", () =>
            {
                Pawn owner = __instance.pawn;
                Pawn partner = DlcContext.PsychicBondMutationPartner(__instance)
                    ?? DlcContext.PsychicBondPartner(owner);
                if (partner == null) return;
                PsychicBondPair pair = PsychicBondPairPolicy.Create(
                    owner.GetUniqueLoadID(),
                    partner.GetUniqueLoadID());
                string cause = PsychicBondLifecyclePolicy.ExactRuptureCause(
                    owner.Dead || partner.Dead,
                    BiotechPsychicBondGeneRemovalScope.Owns(pair));
                int tick = Find.TickManager?.TicksGame ?? 0;
                DiaryGameComponent component = DiaryGameComponent.Instance;
                PsychicBondMutationSnapshot snapshot = DlcContext.CapturePsychicBondMutation(
                    owner,
                    partner,
                    component?.CurrentPsychicBondEpoch(owner, partner) ?? 1,
                    PsychicBondPhaseTokens.Ruptured,
                    cause,
                    DlcContext.AreMutuallyPsychicBonded(owner, partner),
                    mutuallyBondedAfter: true,
                    tick: tick);
                captured = BiotechPsychicBondCorrelation.Begin(owner, partner, snapshot);
            });
            __state = captured;
        }

        private static void Postfix(BiotechBondCallState __state)
        {
            if (__state == null || !__state.isRoot) return;
            DiaryPatchSafety.Run("BiotechPsychicBond.RemoveBond.Postfix", () =>
            {
                __state.snapshot.mutuallyBondedAfter =
                    DlcContext.AreMutuallyPsychicBonded(__state.firstPawn, __state.secondPawn);
                if (DiaryGameComponent.Instance?.CompletePsychicBondTransition(
                    __state.firstPawn,
                    __state.secondPawn,
                    __state.snapshot) == true)
                {
                    BiotechPsychicBondCorrelation.Commit(
                        __state,
                        Find.TickManager?.TicksGame ?? 0,
                        DiaryBiotechPolicy.Snapshot()
                            .bondDeathrest.psychicBondCorrelationExpiryTicks);
                }
            });
        }

        private static Exception Finalizer(Exception __exception, BiotechBondCallState __state)
        {
            DiaryPatchSafety.Run(
                "BiotechPsychicBond.RemoveBond.Finalizer",
                () => BiotechPsychicBondCorrelation.Close(__state));
            return __exception;
        }
    }

    /// <summary>Marks only removal of the owning psychic-bond gene as a truthful rupture cause.</summary>
    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.RemoveGene),
        new[] { typeof(Gene) })]
    internal static class BiotechPsychicBondGeneRemovalPatch
    {
        private static void Prefix(Gene gene, ref string __state)
        {
            if (ModsConfig.BiotechActive && gene is Gene_PsychicBonding)
                __state = BiotechPsychicBondGeneRemovalScope.Begin(gene.pawn);
        }

        private static Exception Finalizer(Exception __exception, string __state)
        {
            BiotechPsychicBondGeneRemovalScope.End(__state);
            return __exception;
        }
    }

    /// <summary>
    /// Owns the exact bond transition made when the last sustaining psychic-bond gene is removed.
    /// </summary>
    [HarmonyPatch(
        typeof(Gene_PsychicBonding),
        nameof(Gene_PsychicBonding.Notify_MyOrPartnersGeneRemoved),
        new Type[] { })]
    internal static class BiotechPsychicBondGeneRemovedTransitionPatch
    {
        private static void Prefix(
            Gene_PsychicBonding __instance,
            ref BiotechBondCallState __state)
        {
            if (!ModsConfig.BiotechActive || __instance?.pawn == null) return;
            BiotechBondCallState captured = null;
            DiaryPatchSafety.Run("BiotechPsychicBond.GeneRemoved.Prefix", () =>
            {
                Pawn owner = __instance.pawn;
                Pawn partner = DlcContext.PsychicBondMutationPartner(__instance);
                if (partner == null) return;
                PsychicBondPair pair = PsychicBondPairPolicy.Create(
                    owner.GetUniqueLoadID(),
                    partner.GetUniqueLoadID());
                if (!BiotechPsychicBondGeneRemovalScope.Owns(pair)) return;
                int tick = Find.TickManager?.TicksGame ?? 0;
                DiaryGameComponent component = DiaryGameComponent.Instance;
                PsychicBondMutationSnapshot snapshot = DlcContext.CapturePsychicBondMutation(
                    owner,
                    partner,
                    component?.CurrentPsychicBondEpoch(owner, partner) ?? 1,
                    PsychicBondPhaseTokens.Ruptured,
                    PsychicBondCauseTokens.GeneRemoved,
                    DlcContext.AreMutuallyPsychicBonded(owner, partner),
                    mutuallyBondedAfter: true,
                    tick: tick);
                captured = BiotechPsychicBondCorrelation.Begin(owner, partner, snapshot);
            });
            __state = captured;
        }

        private static void Postfix(BiotechBondCallState __state)
        {
            if (__state == null || !__state.isRoot) return;
            DiaryPatchSafety.Run("BiotechPsychicBond.GeneRemoved.Postfix", () =>
            {
                __state.snapshot.mutuallyBondedAfter =
                    DlcContext.AreMutuallyPsychicBonded(__state.firstPawn, __state.secondPawn);
                if (DiaryGameComponent.Instance?.CompletePsychicBondTransition(
                    __state.firstPawn,
                    __state.secondPawn,
                    __state.snapshot) == true)
                {
                    BiotechPsychicBondCorrelation.Commit(
                        __state,
                        Find.TickManager?.TicksGame ?? 0,
                        DiaryBiotechPolicy.Snapshot()
                            .bondDeathrest.psychicBondCorrelationExpiryTicks);
                }
            });
        }

        private static Exception Finalizer(Exception __exception, BiotechBondCallState __state)
        {
            DiaryPatchSafety.Run(
                "BiotechPsychicBond.GeneRemoved.Finalizer",
                () => BiotechPsychicBondCorrelation.Close(__state));
            return __exception;
        }
    }

    /// <summary>Captures pre-Wake progress and owns only a confirmed interrupted deathrest.</summary>
    [HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.Wake), new Type[] { })]
    internal static class BiotechInterruptedDeathrestPatch
    {
        private static void Prefix(Gene_Deathrest __instance, ref BiotechDeathrestCallState __state)
        {
            if (!ModsConfig.BiotechActive || __instance == null) return;
            BiotechDeathrestCallState captured = null;
            DiaryPatchSafety.Run("BiotechDeathrest.Wake.Prefix", () =>
            {
                Pawn pawn;
                DeathrestMutationSnapshot snapshot;
                if (DlcContext.TryCaptureDeathrest(
                    __instance,
                    Find.TickManager?.TicksGame ?? 0,
                    out pawn,
                    out snapshot))
                {
                    captured = BiotechDeathrestCorrelation.Begin(pawn, snapshot);
                }
            });
            __state = captured;
        }

        private static void Postfix(BiotechDeathrestCallState __state)
        {
            if (__state == null) return;
            DiaryPatchSafety.Run("BiotechDeathrest.Wake.Postfix", () =>
            {
                __state.snapshot.interruptedHediffAfter =
                    DlcContext.HasInterruptedDeathrest(__state.pawn);
                if (DiaryGameComponent.Instance?.CompleteInterruptedDeathrest(
                    __state.pawn,
                    __state.snapshot) == true)
                {
                    BiotechDeathrestCorrelation.Commit(
                        __state,
                        Find.TickManager?.TicksGame ?? 0,
                        DiaryBiotechPolicy.Snapshot()
                            .bondDeathrest.psychicBondCorrelationExpiryTicks);
                }
            });
        }

        private static Exception Finalizer(
            Exception __exception,
            BiotechDeathrestCallState __state)
        {
            DiaryPatchSafety.Run(
                "BiotechDeathrest.Wake.Finalizer",
                () => BiotechDeathrestCorrelation.Close(__state));
            return __exception;
        }
    }
}
