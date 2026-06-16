// Discovery patches — known non-functional and intentionally not registered.
//
// These hooks were an experimental attempt to generate diary pages for map discoveries such as
// ancient danger reveals, ancient mech threats, hidden revealed things, and void/fallen monolith
// investigation. They did not reliably fire in RimWorld 1.6, so the code is isolated here without
// [HarmonyPatch] attributes. Keeping it compiled makes the old approach easy to inspect later while
// preventing PatchAll from registering broken discovery behavior.
//
// New to C#/RimWorld? Methods named Prefix/Postfix only become Harmony patches when a [HarmonyPatch]
// attribute, or a manual harmony.Patch call, registers them. These classes have neither on purpose.
// They also guard on DiaryGameComponent.DiscoveryEventsEnabled, which is false, so a future manual
// registration cannot accidentally create diary entries before the feature is repaired.
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Disabled reference hook for fog-based map reveals. Known non-functional.
    /// </summary>
    internal static class DisabledFloodFillerFogFloodUnfogPatch
    {
        public static void Postfix(FloodUnfogResult __result, IntVec3 __0, Map __1)
        {
            if (!DiaryGameComponent.DiscoveryEventsEnabled)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordAreaRevealed(null, __1, __0, __result);
        }
    }

    /// <summary>
    /// Disabled reference hook for ancient-danger unfog triggers. Known non-functional.
    /// </summary>
    internal static class DisabledTriggerUnfoggedActivatedPatch
    {
        public static void Prefix(TriggerUnfogged __instance)
        {
            if (!DiaryGameComponent.DiscoveryEventsEnabled)
            {
                return;
            }

            if (__instance == null)
            {
                return;
            }

            Map map = __instance.Map ?? __instance.MapHeld;
            DiaryGameComponent.Current?.RecordAncientDangerRevealed(null, map, __instance.PositionHeld, __instance);
        }
    }

    /// <summary>
    /// Disabled reference hook for direct monolith investigation. Known non-functional.
    /// </summary>
    internal static class DisabledVoidMonolithInvestigatedPatch
    {
        public static void Postfix(Building_VoidMonolith __instance, Pawn __0)
        {
            if (!DiaryGameComponent.DiscoveryEventsEnabled)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMonolithInvestigated(__0, __instance);
        }
    }

    /// <summary>
    /// Disabled reference hook for study-driven monolith discovery. Known non-functional.
    /// </summary>
    internal static class DisabledCompStudiableMonolithStudyPatch
    {
        public static void Postfix(CompStudiableMonolith __instance, Pawn __0)
        {
            if (!DiaryGameComponent.DiscoveryEventsEnabled)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMonolithInvestigated(__0, __instance?.parent);
        }
    }

    /// <summary>
    /// Disabled reference hook for monolith activation. Known non-functional.
    /// </summary>
    internal static class DisabledVoidMonolithActivatedPatch
    {
        public static void Postfix(Building_VoidMonolith __instance, Pawn __0)
        {
            if (!DiaryGameComponent.DiscoveryEventsEnabled)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMonolithActivated(__0, __instance);
        }
    }
}
