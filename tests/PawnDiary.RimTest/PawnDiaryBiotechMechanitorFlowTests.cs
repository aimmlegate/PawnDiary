// Loaded-game acceptance for Biotech Phase 6. The suite proves the real mechlink lifecycle enters
// Pawn Diary through vanilla Hediff methods and audits every verified Harmony seam. Pure name,
// tenure, baseline, Tale-role, cap, and boss-state decisions live in DiaryBiotechPolicyTests.
using System;
using System.Collections;
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

        /// <summary>
        /// Exercises the real Overseer relation and Tale hooks with per-pawn generation disabled.
        /// Observation must still consume the exact first-control milestone, while a same-faction kill
        /// must not consume the hostile-combat milestone.
        /// </summary>
        [Test]
        public static void DisabledOutputConsumesExactMilestonesButFriendlyFireDoesNotConsumeCombat()
        {
            if (!RequireBiotechOrSkip(
                nameof(DisabledOutputConsumesExactMilestonesButFriendlyFireDoesNotConsumeCombat))) return;
            PawnKindDef mechKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Lifter");
            PawnDiaryRimTestScope.Require(mechKind != null,
                "Biotech is active but the base-game Mech_Lifter PawnKindDef is unavailable.");
            Pawn mech = scope.CreateTrackedPawn(mechKind, Faction.OfPlayer);
            Pawn friendlyTarget = scope.CreateAdultColonist();
            Faction hostileFaction = Find.FactionManager?.AllFactionsListForReading?
                .FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer));
            PawnDiaryRimTestScope.Require(hostileFaction != null,
                "The loaded Biotech test colony has no faction hostile to the player.");
            Pawn hostileTarget = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, hostileFaction);
            Hediff mechlink = HediffMaker.MakeHediff(HediffDefOf.MechlinkImplant, controller);
            controller.health.AddHediff(mechlink);

            controller.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            MechanitorObservationState state = MechanitorState(controller);
            PawnDiaryRimTestScope.Require(state.firstControlledPageConsumed,
                "The exact first Overseer relation was not consumed while output was disabled.");
            PawnDiaryRimTestScope.Require(!state.firstControlledCombatPageConsumed,
                "Adding an Overseer relation unexpectedly consumed the first-combat milestone.");

            Tale friendlyTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(friendlyTale));
            friendlyTale = TaleRecorder.RecordTale(TaleDefOf.KilledMelee, mech, friendlyTarget);
            PawnDiaryRimTestScope.Require(!state.firstControlledCombatPageConsumed,
                "A same-faction mech kill consumed the hostile first-combat milestone.");

            Tale hostileTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(hostileTale));
            hostileTale = TaleRecorder.RecordTale(TaleDefOf.KilledMelee, mech, hostileTarget);
            PawnDiaryRimTestScope.Require(state.firstControlledCombatPageConsumed,
                "The exact hostile first-combat Tale was not consumed while output was disabled.");
        }

        /// <summary>Proves one nested kill release cannot erase its still-active outer death scope.</summary>
        [Test]
        public static void NestedMechanitorDeathScopeRequiresBalancedRelease()
        {
            MechanitorDeathScope.Begin(controller);
            MechanitorDeathScope.Begin(controller);
            try
            {
                MechanitorDeathScope.End(controller);
                PawnDiaryRimTestScope.Require(MechanitorDeathScope.IsDying(controller),
                    "One nested release erased the outer mechanitor death scope.");
                MechanitorDeathScope.End(controller);
                PawnDiaryRimTestScope.Require(!MechanitorDeathScope.IsDying(controller),
                    "Balanced nested releases left a stale mechanitor death scope.");
            }
            finally
            {
                MechanitorDeathScope.End(controller);
                MechanitorDeathScope.End(controller);
            }
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
                AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup),
                    new[] { typeof(Map), typeof(bool) }),
                typeof(MechanitorBossSpawnedPatch),
                prefix: false);
            RequireOwnedPatch(
                AccessTools.Method(typeof(GameComponent_Bossgroup),
                    nameof(GameComponent_Bossgroup.Notify_PawnKilled), new[] { typeof(Pawn) }),
                typeof(MechanitorBossDefeatedPatch),
                prefix: false);
        }

        private static MechanitorObservationState MechanitorState(Pawn pawn)
        {
            MethodInfo findDiary = AccessTools.Method(
                typeof(DiaryGameComponent),
                "FindDiary",
                new[] { typeof(Pawn), typeof(bool) });
            PawnDiaryRecord diary = findDiary?.Invoke(
                DiaryGameComponent.Instance,
                new object[] { pawn, false }) as PawnDiaryRecord;
            MechanitorObservationState state = diary?.progressionState?.EnsureBiotechState()
                .EnsureMechanitorObservation();
            PawnDiaryRimTestScope.Require(state != null,
                "Expected a saved mechanitor observation row for " + pawn?.LabelShortCap + ".");
            return state;
        }

        private static void RemoveRecordedTale(Tale tale)
        {
            if (tale == null || Find.TaleManager == null) return;
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                Type fieldType = fields[i].FieldType;
                if (!typeof(IList).IsAssignableFrom(fieldType) || !fieldType.IsGenericType
                    || fieldType.GetGenericArguments()[0] != typeof(Tale)) continue;
                (fields[i].GetValue(Find.TaleManager) as IList)?.Remove(tale);
                return;
            }
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
