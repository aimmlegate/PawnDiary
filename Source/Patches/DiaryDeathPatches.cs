// Death-related Harmony patches. These hooks capture kill context before RimWorld mutates pawn
// death state, then provide a fallback neutral death page when vanilla does not emit a death Tale.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Fires at the exact moment a pawn is killed. TaleRecorder later tells us that a colonist death
    // should become a diary event, but the Tale no longer carries the killing DamageInfo or culprit
    // hediff. This prefix caches those facts while they are still available.
    /// <summary>
    /// Captures pawn death details around Pawn.Kill so death diary entries keep cause context.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class PawnKillPatch
    {
        /// <summary>
        /// Harmony Prefix for Pawn.Kill. Captures death cause details for colonists before vanilla
        /// mutates death/corpse state and records the corresponding Tale.
        /// </summary>
        public static void Prefix(
            Pawn __instance,
            DamageInfo? dinfo,
            Hediff exactCulprit,
            out bool __state)
        {
            bool scopeStarted = false;
            DiaryPatchSafety.Run("PawnKillPatch.Prefix", () =>
            {
                if (ModsConfig.BiotechActive && __instance?.mechanitor != null)
                {
                    MechanitorDeathScope.Begin(__instance);
                    scopeStarted = true;
                }
                DeathContextCache.Capture(__instance, dinfo, exactCulprit);
                if (ModsConfig.BiotechActive && __instance?.RaceProps?.IsMechanoid == true)
                    DiaryGameComponent.Instance?.OnControlledMechDying(__instance);
            });
            __state = scopeStarted;
        }

        /// <summary>
        /// Harmony Postfix for Pawn.Kill. Records a neutral final death entry when vanilla did not
        /// emit a death Tale for this kill path, such as some condition/need deaths.
        /// </summary>
        public static void Postfix(Pawn __instance)
        {
            DiaryPatchSafety.Run("PawnKillPatch.Postfix", () =>
            {
                // Pawn.Kill fires for every death — animals, raiders, mechs — but only a humanlike
                // colonist can ever get a death page. Gate before building/submitting the signal so
                // the common non-colonist kill does no allocation or bus work. The decider re-checks
                // eligibility, so this is a fast pre-filter, not the authority. (Capture in the Prefix
                // is left unconditional: it is cheap and only read back for an eligible pawn.)
                if (!DiaryGameComponent.IsDeathDescriptionEligible(__instance))
                {
                    return;
                }

                DiaryEvents.Submit(new DeathFallbackSignal(__instance));
            });
        }

        /// <summary>Guarantees the death scope closes even if vanilla or another patch throws.</summary>
        public static System.Exception Finalizer(
            Pawn __instance,
            bool __state,
            System.Exception __exception)
        {
            if (__state) MechanitorDeathScope.End(__instance);
            return __exception;
        }
    }
}
