// Arrival-related Harmony patches. Pawn.SetFaction is the common vanilla/DLC/modded convergence
// point for pawn joins, so this file keeps that capture edge separate from other Harmony hooks.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Fires whenever a pawn changes faction. Many vanilla and DLC join paths eventually converge
    // here: prisoner recruitment, wanderer/quest joins, creepjoiners, and modded arrivals.
    /// <summary>
    /// Captures player-colonist arrivals at the Pawn.SetFaction convergence point.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class PawnSetFactionPatch
    {
        /// <summary>
        /// Harmony Prefix for Pawn.SetFaction. Captures the pre-change faction and recruiter before
        /// vanilla mutates the pawn into a colonist.
        /// </summary>
        public static void Prefix(Pawn __instance, Faction newFaction, Pawn recruiter)
        {
            DiaryPatchSafety.Run("PawnSetFactionPatch.Prefix", () =>
            {
                ArrivalContextCache.Capture(__instance, newFaction, recruiter);
            });
        }

        /// <summary>
        /// Harmony Postfix for Pawn.SetFaction. Records the arrival only after vanilla confirms the
        /// pawn is now a player colonist.
        /// </summary>
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            DiaryPatchSafety.Run("PawnSetFactionPatch.Postfix", () =>
            {
                // Scenario setup flips starting pawns to the player faction during generation, before the
                // game is playing; skip those so we don't record arrivals (and read TicksAbs) too early.
                // Founding colonists are recorded by the first-playing-tick scan instead.
                // GamePlaying is checked before reading the player faction so Faction.OfPlayerSilentFail
                // (null-safe, no log spam) is used instead of the logging Faction.OfPlayer getter.
                if (__instance == null || !DiaryGameComponent.GamePlaying)
                {
                    return;
                }

                Faction player = Faction.OfPlayerSilentFail;
                if (player == null || newFaction != player || !__instance.IsColonist)
                {
                    return;
                }

                DiaryEvents.Submit(new ArrivalSignal(__instance,
                    ArrivalContextCache.ConsumeOrBuild(__instance, "set_faction")));
            });
        }
    }
}
