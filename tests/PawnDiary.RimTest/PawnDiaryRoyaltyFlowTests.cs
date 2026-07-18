// Loaded-game acceptance for Royalty Phase 2. The suite drives vanilla CompBladelinkWeapon methods,
// proves late-visible historical bonds are adopted silently, verifies current narrative context
// against live coded-weapon truth, and audits every exact Harmony seam used by the lifecycle.
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
    /// <summary>Loaded exact-hook and reconciliation acceptance for persona weapons.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo PersonaBondsField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaBonds", PrivateInstance);
        private static readonly MethodInfo ReconcileMethod =
            typeof(DiaryGameComponent).GetMethod("ReconcileRoyaltyPersonaBonds", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("personaWeaponLifecycle");
            pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            scope.RegisterCleanup(() => RemovePersonaRows(string.Empty, pawn?.GetUniqueLoadID()));
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// Codes a real vanilla persona weapon, removes its saved row to model an old caravan/map that
        /// was absent at the global baseline, then proves reconciliation silently adopts it. UnCode
        /// must immediately remove that stale saved relationship from current narrative context.
        /// </summary>
        [Test]
        public static void RealCodingLateVisibilityAndUncodeFollowLiveTruth()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealCodingLateVisibilityAndUncodeFollowLiveTruth))) return;
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);

            DiaryEvent formed = scope.FireAndRequireEvent(
                () => comp.CodeFor(pawn),
                PersonaWeaponEventData.BondFormedDefName,
                pawn,
                null);
            scope.RequireSoloRef(formed, pawn);
            List<PersonaWeaponSnapshot> visible = DlcContext.CapturePersonaWeapons(pawn);
            PawnDiaryRimTestScope.Require(visible.Count == 1
                    && visible[0].weaponThingId == weapon.GetUniqueLoadID(),
                "Vanilla coding did not expose the exact bonded weapon through the pawn tracker.");
            PawnDiaryRimTestScope.Require(
                scope.Component.RoyaltyNarrativeSnapshotFor(pawn, formed.tick).personaBonds.Count == 1,
                "A newly coded live persona bond was missing from current narrative context.");

            string weaponId = weapon.GetUniqueLoadID();
            RemovePersonaRows(weaponId, pawn.GetUniqueLoadID());
            scope.RequireNoNewEvent(() => ReconcileMethod?.Invoke(scope.Component, null));
            PersonaBondState adopted = PersonaRows().SingleOrDefault(row =>
                row != null && row.weaponThingId == weaponId);
            PawnDiaryRimTestScope.Require(adopted != null
                    && adopted.phaseToken == PersonaBondPhaseTokens.Active
                    && adopted.firstConsequentialKillObserved,
                "Late-visible historical persona bond was not silently adopted as a consumed baseline.");

            scope.RequireNoNewEvent(() => comp.UnCode());
            PawnDiaryRimTestScope.Require(
                scope.Component.RoyaltyNarrativeSnapshotFor(pawn, formed.tick).personaBonds.Count == 0,
                "UnCode left a saved-only persona bond in current narrative context.");
        }

        /// <summary>Audits all six exact RimWorld 1.6 methods and the fail-safe owner ID.</summary>
        [Test]
        public static void ExactPersonaWeaponHooksAreRegistered()
        {
            if (!RequireRoyaltyOrSkip(nameof(ExactPersonaWeaponHooksAreRegistered))) return;
            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.HooksReady,
                "Pawn Diary reported an incomplete Royalty persona hook set.");
            Type type = typeof(CompBladelinkWeapon);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.CodeFor),
                    new[] { typeof(Pawn) }),
                "CodeForPrefix", "CodeForPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_Equipped),
                    new[] { typeof(Pawn) }),
                null, "EquippedPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_EquipmentLost),
                    new[] { typeof(Pawn) }),
                null, "EquipmentLostPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.PostDestroy),
                    new[] { typeof(DestroyMode), typeof(Map) }),
                "DestroyPrefix", null);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_MapRemoved),
                    Type.EmptyTypes),
                "MapRemovedPrefix", null);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.UnCode), Type.EmptyTypes),
                "UncodePrefix", null);
        }

        private static void CreatePersonaWeapon(
            out ThingWithComps weapon,
            out CompBladelinkWeapon comp)
        {
            weapon = null;
            comp = null;
            List<ThingDef> candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.comps != null
                    && def.comps.Any(properties =>
                        properties?.compClass == typeof(CompBladelinkWeapon)))
                .OrderBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < candidates.Count && weapon == null; i++)
            {
                ThingDef def = candidates[i];
                ThingWithComps created = ThingMaker.MakeThing(
                    def,
                    def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) as ThingWithComps;
                CompBladelinkWeapon createdComp = created?.TryGetComp<CompBladelinkWeapon>();
                if (createdComp != null)
                {
                    weapon = created;
                    comp = createdComp;
                }
                else if (created != null && !created.Destroyed)
                {
                    created.Destroy(DestroyMode.Vanish);
                }
            }

            PawnDiaryRimTestScope.Require(weapon != null && comp != null,
                "Royalty is active but no constructible CompBladelinkWeapon Def was available.");
            ThingWithComps cleanupWeapon = weapon;
            CompBladelinkWeapon cleanupComp = comp;
            scope.RegisterCleanup(() =>
            {
                if (cleanupComp.CodedPawn != null) cleanupComp.UnCode();
                if (!cleanupWeapon.Destroyed) cleanupWeapon.Destroy(DestroyMode.Vanish);
            });
        }

        private static List<PersonaBondState> PersonaRows()
        {
            List<PersonaBondState> rows = PersonaBondsField?.GetValue(scope.Component)
                as List<PersonaBondState>;
            PawnDiaryRimTestScope.Require(rows != null,
                "Could not read DiaryGameComponent.royaltyPersonaBonds for fixture cleanup/assertion.");
            return rows;
        }

        private static void RemovePersonaRows(string weaponId, string pawnId)
        {
            if (scope?.Component == null || PersonaBondsField == null) return;
            List<PersonaBondState> rows = PersonaBondsField.GetValue(scope.Component)
                as List<PersonaBondState>;
            rows?.RemoveAll(row => row != null
                && ((!string.IsNullOrEmpty(weaponId) && row.weaponThingId == weaponId)
                    || (!string.IsNullOrEmpty(pawnId) && row.currentPawnId == pawnId)));
        }

        private static void RequireOwnedPatch(
            MethodBase target,
            string prefixName,
            string postfixName)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && patches != null,
                "Expected a patched Royalty persona target, but the exact method was unavailable: "
                    + target + ".");
            if (prefixName != null)
            {
                PawnDiaryRimTestScope.Require(patches.Prefixes.Any(row =>
                        row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == typeof(DiaryRoyaltyPatches)
                        && row.PatchMethod.Name == prefixName),
                    "Expected Pawn Diary's " + prefixName + " on " + target + ".");
            }
            if (postfixName != null)
            {
                PawnDiaryRimTestScope.Require(patches.Postfixes.Any(row =>
                        row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == typeof(DiaryRoyaltyPatches)
                        && row.PatchMethod.Name == postfixName),
                    "Expected Pawn Diary's " + postfixName + " on " + target + ".");
            }
        }

        private static bool RequireRoyaltyOrSkip(string fixtureName)
        {
            if (ModsConfig.RoyaltyActive) return true;
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Royalty is not active in this test profile.");
            return false;
        }
    }
}
