// Royalty persona-weapon lifecycle hooks. They are registered manually by exact signatures so a
// RimWorld update can disable only this optional feature. Every callback starts with the DLC/play/
// Scribe gates and forwards the live observation to the component's guarded collector without
// changing vanilla behavior; no live object crosses into pure policy or saved state.
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Defensively registers the Phase-2 CompBladelinkWeapon lifecycle seams.</summary>
    internal static class DiaryRoyaltyPatches
    {
        private sealed class CodingPatchState
        {
            public ThingWithComps weapon;
            public Pawn pawn;
            public PersonaWeaponSnapshot before;
            public RoyaltyPersonaThoughtCorrelation.Scope thoughtScope;
        }

        /// <summary>True when every required persona lifecycle seam was registered.</summary>
        public static bool HooksReady { get; private set; }

        public static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            if (harmony == null || !ModsConfig.RoyaltyActive) return;
            Type type = typeof(CompBladelinkWeapon);
            bool ready = true;
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.CodeFor), new[] { typeof(Pawn) }),
                nameof(CodeForPrefix), nameof(CodeForPostfix), nameof(CodeForFinalizer));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_Equipped), new[] { typeof(Pawn) }),
                null, nameof(EquippedPostfix));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_EquipmentLost), new[] { typeof(Pawn) }),
                null, nameof(EquipmentLostPostfix));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.PostDestroy),
                    new[] { typeof(DestroyMode), typeof(Map) }),
                nameof(DestroyPrefix), null);
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_MapRemoved), Type.EmptyTypes),
                nameof(MapRemovedPrefix), null);
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.UnCode), Type.EmptyTypes),
                nameof(UncodePrefix), null);
            HooksReady = ready;
            if (!ready)
            {
                Log.Warning("[Pawn Diary] One or more Royalty persona-weapon lifecycle methods changed; "
                    + "the affected diary observation is disabled while vanilla behavior remains untouched.");
            }
        }

        private static bool Patch(
            Harmony harmony,
            MethodBase target,
            string prefixName,
            string postfixName,
            string finalizerName = null)
        {
            if (target == null) return false;
            try
            {
                harmony.Patch(
                    target,
                    prefix: prefixName == null ? null : new HarmonyMethod(typeof(DiaryRoyaltyPatches), prefixName),
                    postfix: postfixName == null ? null : new HarmonyMethod(typeof(DiaryRoyaltyPatches), postfixName),
                    finalizer: finalizerName == null
                        ? null
                        : new HarmonyMethod(typeof(DiaryRoyaltyPatches), finalizerName));
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning("[Pawn Diary] Could not register Royalty persona hook for " + target.Name
                    + ": " + exception.Message);
                return false;
            }
        }

        private static void CodeForPrefix(
            CompBladelinkWeapon __instance,
            Pawn pawn,
            ref CodingPatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || __instance == null || pawn == null) return;
            CodingPatchState captured = null;
            DiaryPatchSafety.Run("Royalty.Persona.CodeFor.Prefix", () =>
            {
                ThingWithComps weapon = __instance.parent as ThingWithComps;
                if (weapon == null) return;
                captured = new CodingPatchState
                {
                    weapon = weapon,
                    pawn = pawn,
                    before = DiaryGameComponent.Instance?.BeginRoyaltyPersonaCoding(weapon, pawn),
                    thoughtScope = RoyaltyPersonaThoughtCorrelation.Begin(
                        pawn.GetUniqueLoadID(), DlcContext.CapturePersonaBondedThoughtDefNames(weapon))
                };
            });
            __state = captured;
        }

        private static void CodeForPostfix(CodingPatchState __state)
        {
            if (__state == null) return;
            bool accepted = false;
            if (RuntimeReady())
            {
                DiaryPatchSafety.Run("Royalty.Persona.CodeFor.Postfix", () =>
                {
                    accepted = DiaryGameComponent.Instance?.CompleteRoyaltyPersonaCoding(
                        __state.weapon, __state.pawn, __state.before) == true;
                });
            }
            DiaryPatchSafety.Run("Royalty.Persona.CodeFor.ThoughtClose", () =>
                RoyaltyPersonaThoughtCorrelation.Close(__state.thoughtScope, accepted));
        }

        /// <summary>
        /// Releases the exact thought scope when vanilla coding throws. On the normal path the
        /// postfix already closed it, and Close is deliberately idempotent.
        /// </summary>
        private static Exception CodeForFinalizer(Exception __exception, CodingPatchState __state)
        {
            if (__exception != null && __state != null)
                DiaryPatchSafety.Run("Royalty.Persona.CodeFor.Finalizer", () =>
                    RoyaltyPersonaThoughtCorrelation.Close(__state.thoughtScope, false));
            return __exception;
        }

        private static void EquippedPostfix(CompBladelinkWeapon __instance, Pawn pawn)
        {
            ObserveEquipment(__instance, pawn, "Royalty.Persona.Equipped");
        }

        private static void EquipmentLostPostfix(CompBladelinkWeapon __instance, Pawn pawn)
        {
            ObserveEquipment(__instance, pawn, "Royalty.Persona.EquipmentLost");
        }

        private static void ObserveEquipment(CompBladelinkWeapon comp, Pawn pawn, string patchName)
        {
            if (!RuntimeReady() || comp == null || pawn == null) return;
            DiaryPatchSafety.Run(patchName, () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaEquipment(
                    comp.parent as ThingWithComps, pawn);
            });
        }

        private static void DestroyPrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.Destroy", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaDestroyed(
                    __instance.parent as ThingWithComps);
            });
        }

        private static void MapRemovedPrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.MapRemoved", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaMapRemoved(
                    __instance.parent as ThingWithComps);
            });
        }

        private static void UncodePrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.UnCode", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaUncode(
                    __instance.parent as ThingWithComps);
            });
        }

        private static bool RuntimeReady()
        {
            return ModsConfig.RoyaltyActive
                && Verse.Current.ProgramState == ProgramState.Playing
                && Scribe.mode == LoadSaveMode.Inactive;
        }
    }
}
