// Death-related Harmony patches. These hooks capture kill context before RimWorld mutates pawn
// death state, then provide a fallback neutral death page when vanilla does not emit a death Tale.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
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
    public static class PawnKillPatch
    {
        /// <summary>
        /// Harmony Prefix for Pawn.Kill. Captures death cause details for colonists before vanilla
        /// mutates death/corpse state and records the corresponding Tale.
        /// </summary>
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            DeathContextCache.Capture(__instance, dinfo, exactCulprit);
        }

        /// <summary>
        /// Harmony Postfix for Pawn.Kill. Records a neutral final death entry when vanilla did not
        /// emit a death Tale for this kill path, such as some condition/need deaths.
        /// </summary>
        public static void Postfix(Pawn __instance)
        {
            DiaryGameComponent.Current?.RecordDeathFallback(__instance);
        }
    }
}
