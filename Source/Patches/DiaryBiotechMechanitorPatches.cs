// Exact Biotech Phase-6 Harmony hooks. Every callback remains behind ModsConfig.BiotechActive and
// delegates live reads to DlcContext/component orchestration, so a base-game-only profile cleanly
// no-ops even though RimWorld ships the DLC types in Assembly-CSharp.dll.
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Tracks Pawn.Kill nesting so death-driven mechlink removal cannot create a removal page.</summary>
    internal static class MechanitorDeathScope
    {
        private static readonly Dictionary<Pawn, int> Depth = new Dictionary<Pawn, int>();

        /// <summary>Marks entry into one possibly nested Pawn.Kill call.</summary>
        public static void Begin(Pawn pawn)
        {
            if (pawn == null) return;
            int depth;
            Depth.TryGetValue(pawn, out depth);
            Depth[pawn] = depth + 1;
        }

        /// <summary>Releases one possibly nested Pawn.Kill call.</summary>
        public static void End(Pawn pawn)
        {
            if (pawn == null) return;
            int depth;
            if (!Depth.TryGetValue(pawn, out depth)) return;
            if (depth <= 1) Depth.Remove(pawn);
            else Depth[pawn] = depth - 1;
        }

        /// <summary>Returns whether vanilla is currently killing this exact pawn.</summary>
        public static bool IsDying(Pawn pawn)
        {
            return pawn != null && Depth.ContainsKey(pawn);
        }
    }

    /// <summary>Captures exact successful mechlink installation.</summary>
    [HarmonyPatch(typeof(Hediff_Mechlink), nameof(Hediff_Mechlink.PostAdd))]
    internal static class MechanitorMechlinkAddedPatch
    {
        /// <summary>Forwards the successfully added mechlink after vanilla creates its tracker.</summary>
        public static void Postfix(Hediff_Mechlink __instance)
        {
            DiaryPatchSafety.Run("MechanitorMechlinkAddedPatch", () =>
            {
                if (ModsConfig.BiotechActive)
                    DiaryGameComponent.Instance?.OnMechanitorMechlinkInstalled(__instance?.pawn);
            });
        }
    }

    /// <summary>Captures removal before vanilla disconnects and clears the controller's mechs.</summary>
    [HarmonyPatch(typeof(Hediff_Mechlink), nameof(Hediff_Mechlink.PostRemoved))]
    internal static class MechanitorMechlinkRemovedPatch
    {
        /// <summary>Snapshots removal while the controller tracker and relations still exist.</summary>
        public static void Prefix(Hediff_Mechlink __instance)
        {
            DiaryPatchSafety.Run("MechanitorMechlinkRemovedPatch", () =>
            {
                if (ModsConfig.BiotechActive)
                    DiaryGameComponent.Instance?.OnMechanitorMechlinkRemoving(__instance?.pawn);
            });
        }
    }

    /// <summary>Call-local correlation from the item user to vanilla's confirmed bossgroup callback.</summary>
    internal static class MechanitorBossCallCorrelation
    {
        private sealed class Scope
        {
            public Pawn caller;
            public BossgroupDef bossgroup;
            public bool confirmed;
        }

        private static readonly List<Scope> Scopes = new List<Scope>();

        /// <summary>Begins one synchronous item-use correlation.</summary>
        public static void Open(Pawn caller, BossgroupDef bossgroup)
        {
            Scopes.Add(new Scope { caller = caller, bossgroup = bossgroup });
        }

        /// <summary>Claims the newest matching caller only after vanilla confirms the group.</summary>
        public static void Confirm(BossgroupDef bossgroup)
        {
            for (int i = Scopes.Count - 1; i >= 0; i--)
            {
                Scope scope = Scopes[i];
                if (scope.confirmed || scope.bossgroup != bossgroup) continue;
                scope.confirmed = true;
                DiaryGameComponent.Instance?.OnMechanitorBossCalled(scope.caller, bossgroup);
                return;
            }
        }

        /// <summary>Closes the newest synchronous item-use correlation.</summary>
        public static void Close()
        {
            if (Scopes.Count > 0) Scopes.RemoveAt(Scopes.Count - 1);
        }
    }

    /// <summary>Remembers the exact caller while the bossgroup use effect resolves synchronously.</summary>
    [HarmonyPatch(typeof(CompUseEffect_CallBossgroup), nameof(CompUseEffect_CallBossgroup.DoEffect))]
    internal static class MechanitorBossUsePatch
    {
        /// <summary>Remembers the exact pawn and bossgroup before the use effect resolves.</summary>
        public static void Prefix(CompUseEffect_CallBossgroup __instance, Pawn usedBy)
        {
            MechanitorBossCallCorrelation.Open(usedBy, __instance?.Props?.bossgroupDef);
        }

        /// <summary>Always closes correlation, while preserving vanilla's exception unchanged.</summary>
        public static Exception Finalizer(Exception __exception)
        {
            MechanitorBossCallCorrelation.Close();
            return __exception;
        }
    }

    /// <summary>Confirms that vanilla accepted the boss call before a diary page is owned.</summary>
    [HarmonyPatch(typeof(GameComponent_Bossgroup), nameof(GameComponent_Bossgroup.Notify_BossgroupCalled))]
    internal static class MechanitorBossCalledPatch
    {
        /// <summary>Confirms the matching in-flight caller after vanilla accepts the bossgroup.</summary>
        public static void Postfix(BossgroupDef bossgroupDef)
        {
            DiaryPatchSafety.Run("MechanitorBossCalledPatch", () =>
            {
                if (ModsConfig.BiotechActive) MechanitorBossCallCorrelation.Confirm(bossgroupDef);
            });
        }
    }

    /// <summary>Captures the exact pawn instance created for one previously accepted boss call.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup), new[] { typeof(Map), typeof(bool) })]
    internal static class MechanitorBossSpawnedPatch
    {
        /// <summary>Type-narrows the spawn hot path before consulting saved boss-call ownership.</summary>
        public static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            // Existing bosses in an old save have no trustworthy call-to-instance mapping. Leave them
            // unassigned so the death path can use its unique-only legacy fallback rather than guessing
            // from map load order. A boss arriving after load uses the ordinary false path and is exact.
            if (!ModsConfig.BiotechActive || respawningAfterLoad
                || __instance?.RaceProps?.IsMechanoid != true) return;
            DiaryPatchSafety.Run("MechanitorBossSpawnedPatch", () =>
                DiaryGameComponent.Instance?.OnMechanitorBossSpawned(__instance));
        }
    }

    /// <summary>Confirms the boss pawn's defeat without claiming who delivered the final blow.</summary>
    [HarmonyPatch(typeof(GameComponent_Bossgroup), nameof(GameComponent_Bossgroup.Notify_PawnKilled))]
    internal static class MechanitorBossDefeatedPatch
    {
        /// <summary>Forwards the defeated boss pawn after vanilla updates its manager state.</summary>
        public static void Postfix(Pawn pawn)
        {
            DiaryPatchSafety.Run("MechanitorBossDefeatedPatch", () =>
            {
                if (ModsConfig.BiotechActive)
                    DiaryGameComponent.Instance?.OnMechanitorBossDefeated(pawn);
            });
        }
    }
}
