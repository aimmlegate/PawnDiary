// Loaded-game acceptance for Biotech Phase 6. The suite proves the real mechlink lifecycle enters
// Pawn Diary through vanilla Hediff methods and audits every verified Harmony seam. Pure name,
// tenure, baseline, Tale-role, cap, and boss-state decisions live in DiaryBiotechPolicyTests.
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
    /// <summary>Loaded exact-hook and no-DLC acceptance for the mechanitor lifecycle.</summary>
    [TestSuite]
    public static class PawnDiaryBiotechMechanitorFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn controller;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("progressionMechanitorLifecycle");
            DiarySignalPolicyDef progression = DiarySignalPolicies.ForKey(DiarySignalPolicies.Progression);
            bool original = progression.enabled;
            progression.enabled = true;
            scope.RegisterCleanup(() => progression.enabled = original);
            controller = scope.CreateAdultColonist();
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                scope = null;
                controller = null;
            }
        }

        /// <summary>
        /// Calls the real HediffSet add/remove APIs. Their vanilla virtual dispatch reaches
        /// Hediff_Mechlink.PostAdd/PostRemoved, and the production Harmony hooks must create exactly
        /// one canonical controller page for each successful lifecycle transition.
        /// </summary>
        [Test]
        public static void RealMechlinkInstallAndRemovalEmitCanonicalPages()
        {
            if (!RequireBiotechOrSkip(nameof(RealMechlinkInstallAndRemovalEmitCanonicalPages))) return;
            PawnDiaryRimTestScope.Require(HediffDefOf.MechlinkImplant != null,
                "Biotech is active but HediffDefOf.MechlinkImplant is unavailable.");

            Hediff mechlink = HediffMaker.MakeHediff(HediffDefOf.MechlinkImplant, controller);
            DiaryEvent installed = scope.FireAndRequireEvent(
                () => controller.health.AddHediff(mechlink),
                MechanitorEventDefNames.MechlinkInstalled,
                controller,
                null);
            scope.RequireSoloRef(installed, controller);
            RequireContext(installed, "mechanitor_moment=mechlink_installed");

            DiaryEvent removed = scope.FireAndRequireEvent(
                () => controller.health.RemoveHediff(mechlink),
                MechanitorEventDefNames.MechlinkRemoved,
                controller,
                null);
            scope.RequireSoloRef(removed, controller);
            RequireContext(removed, "mechanitor_moment=mechlink_removed");
        }

        /// <summary>Audits the exact verified RimWorld 1.6 seams and our fail-safe owner ID.</summary>
        [Test]
        public static void ExactMechanitorHooksAreRegistered()
        {
            RequireOwnedPatch(
                AccessTools.Method(typeof(Hediff_Mechlink), nameof(Hediff_Mechlink.PostAdd),
                    new[] { typeof(DamageInfo?) }),
                typeof(MechanitorMechlinkAddedPatch),
                prefix: false);
            RequireOwnedPatch(
                AccessTools.Method(typeof(Hediff_Mechlink), nameof(Hediff_Mechlink.PostRemoved)),
                typeof(MechanitorMechlinkRemovedPatch),
                prefix: true);
            RequireOwnedPatch(
                AccessTools.Method(typeof(Pawn_RelationsTracker),
                    nameof(Pawn_RelationsTracker.AddDirectRelation),
                    new[] { typeof(PawnRelationDef), typeof(Pawn) }),
                typeof(PawnRelationAddPatch),
                prefix: true);
            RequireOwnedPatch(
                AccessTools.Method(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale)),
                typeof(TaleRecorderPatch),
                prefix: false);
            RequireOwnedPatch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill),
                    new[] { typeof(DamageInfo?), typeof(Hediff) }),
                typeof(PawnKillPatch),
                prefix: true);
            RequireOwnedPatch(
                AccessTools.Method(typeof(CompUseEffect_CallBossgroup),
                    nameof(CompUseEffect_CallBossgroup.DoEffect), new[] { typeof(Pawn) }),
                typeof(MechanitorBossUsePatch),
                prefix: true);
            RequireOwnedPatch(
                AccessTools.Method(typeof(GameComponent_Bossgroup),
                    nameof(GameComponent_Bossgroup.Notify_BossgroupCalled),
                    new[] { typeof(BossgroupDef) }),
                typeof(MechanitorBossCalledPatch),
                prefix: false);
            RequireOwnedPatch(
                AccessTools.Method(typeof(GameComponent_Bossgroup),
                    nameof(GameComponent_Bossgroup.Notify_PawnKilled), new[] { typeof(Pawn) }),
                typeof(MechanitorBossDefeatedPatch),
                prefix: false);
        }

        private static void RequireOwnedPatch(MethodBase target, Type patchType, bool prefix)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            IEnumerable<Patch> rows = patches == null
                ? Enumerable.Empty<Patch>()
                : prefix ? patches.Prefixes : patches.Postfixes;
            PawnDiaryRimTestScope.Require(rows.Any(row => row.owner == "aimml.pawndiary"
                    && row.PatchMethod?.DeclaringType == patchType),
                "Expected Pawn Diary's " + patchType.Name + " on " + target + ".");
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Mechanitor event context omitted '" + fragment + "'.");
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
