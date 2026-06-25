// Non-social gameplay signal Harmony patches. These hooks forward tales, mental states,
// inspirations, map conditions, raids, rituals, and ability use into DiaryGameComponent; the richer
// capture decisions stay in the component and pure Event Catalog helpers.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PawnDiary
{
    // Fires whenever RimWorld accepts a notable "tale" for history/art generation. Tales cover
    // many non-social events, such as wounds, deaths, surgeries, births, recruitment, research,
    // construction/crafting milestones, raids, and disasters.
    /// <summary>
    /// Captures successful vanilla Tales for diary event classification.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    public static class TaleRecorderPatch
    {
        /// <summary>
        /// Harmony Postfix for TaleRecorder.RecordTale. The returned Tale is null when vanilla
        /// chose not to record it, so only successful tales are forwarded to DiaryGameComponent.
        /// </summary>
        public static void Postfix(Tale __result, TaleDef def)
        {
            if (__result == null || def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordTale(__result, def);
        }
    }

    // Fires whenever a pawn enters a mental state: social fights (pairwise) and mental
    // breaks (solo). This is the single choke point for all mental states, so it catches
    // them regardless of how they were triggered.
    /// <summary>
    /// Captures successful mental-state starts for social-fight and mental-break diary entries.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class MentalStateStartPatch
    {
        // Reflection accessor for the private MentalStateHandler.pawn field so we can read the subject pawn.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(MentalStateHandler), "pawn");

        static MentalStateStartPatch()
        {
            if (PawnField == null)
            {
                Log.Warning("[PawnDiary] Could not find MentalStateHandler.pawn; mental-state diary events will not be captured.");
            }
        }

        /// <summary>
        /// Harmony Postfix for MentalStateHandler.TryStartMentalState. Forwards successful
        /// mental state transitions to DiaryGameComponent for diary recording.
        /// </summary>
        public static void Postfix(bool __result, MentalStateHandler __instance, MentalStateDef stateDef, string reason, Pawn otherPawn)
        {
            if (!__result || stateDef == null || __instance == null)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMentalState(pawn, stateDef, otherPawn, reason);
        }
    }

    // Fires whenever RimWorld accepts a pawn inspiration, whether it came from the random inspiration
    // system or another vanilla source such as a psycast, ritual, or drug effect.
    /// <summary>
    /// Captures accepted pawn inspirations after vanilla applies them.
    /// </summary>
    [HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
    public static class InspirationStartPatch
    {
        /// <summary>
        /// Harmony Postfix for InspirationHandler.TryStartInspiration. Records only successful
        /// inspiration starts, after vanilla has accepted and applied the inspiration.
        /// </summary>
        public static void Postfix(bool __result, InspirationHandler __instance, InspirationDef def, string reason)
        {
            if (!__result || __instance == null || __instance.pawn == null || def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordInspiration(__instance.pawn, def, reason);
        }
    }

    // Fires when a mood-affecting GameCondition starts (aurora, eclipse, psychic drone,
    // toxic fallout, etc.). The patch iterates eligible colonists on affected maps and records
    // a solo diary event for each one, so colonists can note how they feel about the event.
    /// <summary>
    /// Captures mood-affecting game conditions when they are registered.
    /// </summary>
    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition))]
    public static class GameConditionStartPatch
    {
        /// <summary>
        /// Harmony Postfix for GameConditionManager.RegisterCondition. Detects
        /// mood-affecting game conditions (aurora, eclipse, psychic drone, etc.) and
        /// forwards each affected colonist to DiaryGameComponent.RecordMoodEvent.
        /// </summary>
        public static void Postfix(GameCondition cond)
        {
            if (cond == null || cond.def == null)
            {
                return;
            }

            // TODO: ProblemCauser conditions are too complex to handle correctly — skipped for now.
            if (cond.def.defName == "ProblemCauser")
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMoodEvent(cond);
        }
    }

    // Fires once per raid-like incident (RaidEnemy / RaidFriendly / RaidBeacon / Infestation).
    // IncidentWorker.TryExecute is the single public entry point every incident flows through, so we
    // filter in the postfix (after pawns/threats have spawned) and forward the IncidentParms +
    // IncidentDef. Infestation is detected by worker type name so the raid diary path stays based on
    // plain strings rather than extra incident-worker dependencies.
    /// <summary>
    /// Captures successful raid-like incident execution after spawned threats are available.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class RaidExecutePatch
    {
        /// <summary>
        /// Harmony Postfix for IncidentWorker.TryExecute. Forwards successful raid-like incidents to
        /// DiaryGameComponent.RecordRaid, which fans out to each eligible colonist on the target map.
        /// Non-raid incidents and failed executions are skipped.
        /// </summary>
        public static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            if (!__result || !IsRaidLikeIncident(__instance) || parms == null || __instance.def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRaid(parms, __instance.def);
        }

        private static bool IsRaidLikeIncident(IncidentWorker worker)
        {
            if (worker is IncidentWorker_Raid)
            {
                return true;
            }

            string workerTypeName = worker?.GetType().Name;
            return !string.IsNullOrWhiteSpace(workerTypeName)
                && workerTypeName.IndexOf("Infestation", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Fires after an Ideology ritual applies its outcome. The hook records the finished ritual once,
    // then DiaryGameComponent fans it out to author/target/participant/spectator solo entries.
    /// <summary>
    /// Captures completed Ideology ritual outcomes after vanilla applies their effects.
    /// </summary>
    [HarmonyPatch(typeof(LordJob_Ritual), "ApplyOutcome")]
    public static class RitualOutcomePatch
    {
        /// <summary>
        /// Harmony Postfix for LordJob_Ritual.ApplyOutcome. Runs after vanilla outcome effects and
        /// skips canceled rituals, so only completed ritual events become diary pages.
        /// </summary>
        public static void Postfix(LordJob_Ritual __instance, float progress, bool cancelled)
        {
            if (__instance == null || cancelled)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRitualFinished(__instance, progress, cancelled);
        }
    }

    // Fires after an Anomaly psychic ritual finishes. Graph.End can run for nested graph transitions;
    // RitualCompleted is the LordToil completion point, after the ritual has actually finished.
    /// <summary>
    /// Captures completed Anomaly psychic rituals at the LordToil completion point.
    /// </summary>
    [HarmonyPatch(typeof(LordToil_PsychicRitual), "RitualCompleted")]
    public static class PsychicRitualCompletedPatch
    {
        /// <summary>
        /// Harmony Postfix for LordToil_PsychicRitual.RitualCompleted. Forwards the completed
        /// psychic ritual instance to the diary after vanilla confirms completion.
        /// </summary>
        public static void Postfix(LordToil_PsychicRitual __instance)
        {
            PsychicRitual psychicRitual = __instance?.RitualData?.psychicRitual;
            if (psychicRitual == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordPsychicRitualFinished(psychicRitual, success: true);
        }
    }

    // Fires after a pawn ability successfully activates on a local map target. Ability covers
    // Royalty psycasts/permits, Biotech/Anomaly powers, and modded abilities that use vanilla defs.
    /// <summary>
    /// Captures successful ability activations that target local map cells/things.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
    public static class AbilityActivateLocalPatch
    {
        /// <summary>
        /// Harmony Postfix for Ability.Activate(LocalTargetInfo, LocalTargetInfo). Only successful
        /// activations are forwarded; the component handles sampling and prompt policy.
        /// </summary>
        public static void Postfix(Ability __instance, LocalTargetInfo target, LocalTargetInfo dest, bool __result)
        {
            if (!__result || __instance == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordAbilityUsed(__instance, target, dest);
        }
    }

    // Fires after a pawn ability successfully activates on a world target.
    /// <summary>
    /// Captures successful ability activations that target the world map.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new[] { typeof(GlobalTargetInfo) })]
    public static class AbilityActivateGlobalPatch
    {
        /// <summary>
        /// Harmony Postfix for Ability.Activate(GlobalTargetInfo). Only successful activations are
        /// forwarded; the component handles sampling and prompt policy.
        /// </summary>
        public static void Postfix(Ability __instance, GlobalTargetInfo target, bool __result)
        {
            if (!__result || __instance == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordAbilityUsed(__instance, target);
        }
    }
}
