// Death-related Harmony patches. These hooks capture kill context before RimWorld mutates pawn
// death state, then provide a fallback neutral death page when vanilla does not emit a death Tale.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
using System.Collections.Generic;
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
        /// <summary>Tracks independent Biotech and Royalty scopes across one Pawn.Kill call.</summary>
        internal sealed class KillPatchState
        {
            public bool mechanitorScopeStarted;
            public PersonaKillCorrelationScope personaKillScope;
        }

        /// <summary>
        /// Harmony Prefix for Pawn.Kill. Captures death cause details for colonists before vanilla
        /// mutates death/corpse state and records the corresponding Tale.
        /// </summary>
        public static void Prefix(
            Pawn __instance,
            DamageInfo? dinfo,
            Hediff exactCulprit,
            out KillPatchState __state)
        {
            // Pawn.Kill also covers animal/raider/mech deaths that can never produce a diary death
            // page. Avoid allocating the state and capture closure for those common no-op calls,
            // while retaining the Royalty candidate path for a colonist persona wielder killing a
            // non-humanlike target (the qualifying major-threat Tale is emitted before SetDead).
            Pawn killer = dinfo.HasValue ? dinfo.Value.Instigator as Pawn : null;
            bool humanlikeColonist = __instance?.RaceProps?.Humanlike == true && __instance.IsColonist;
            bool mechanoidVictim = ModsConfig.BiotechActive
                && __instance?.RaceProps?.IsMechanoid == true;
            bool personaCandidate = ModsConfig.RoyaltyActive
                && killer?.equipment?.bondedWeapon is ThingWithComps;
            if (!humanlikeColonist && !mechanoidVictim && !personaCandidate)
            {
                __state = null;
                return;
            }

            // A mechanoid victim still needs the notification below, but it does not use a scope
            // state; allocate one only when a colonist death or persona correlation can close it.
            KillPatchState state = humanlikeColonist || personaCandidate
                ? new KillPatchState()
                : null;
            DiaryPatchSafety.Run("PawnKillPatch.Prefix", () =>
            {
                if (humanlikeColonist && ModsConfig.BiotechActive && __instance?.mechanitor != null)
                {
                    MechanitorDeathScope.Begin(__instance);
                    state.mechanitorScopeStarted = true;
                }
                if (humanlikeColonist)
                    DeathContextCache.Capture(__instance, dinfo, exactCulprit);
                List<string> killThoughtDefNames;
                if (DlcContext.TryCapturePersonaKillThoughtDefNames(killer, out killThoughtDefNames))
                {
                    RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                    state.personaKillScope = PersonaKillThoughtCorrelation.Begin(
                        __instance,
                        killer,
                        killThoughtDefNames,
                        Find.TickManager?.TicksGame ?? 0,
                        policy.killThoughtCorrelationTicks);
                }
                if (ModsConfig.BiotechActive && __instance?.RaceProps?.IsMechanoid == true)
                    DiaryGameComponent.Instance?.OnControlledMechDying(__instance);
            });
            __state = state;
        }

        /// <summary>
        /// Harmony Postfix for Pawn.Kill. Records a neutral final death entry when vanilla did not
        /// emit a death Tale for this kill path, such as some condition/need deaths.
        /// </summary>
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            DiaryPatchSafety.Run("PawnKillPatch.Postfix", () =>
            {
                // Knowledge death fan-out (MEMORY_SYSTEM_REDESIGN_PLAN §2.1): a pawn instigator
                // and the deceased's close family keep lifelong records. Runs for ANY humanlike
                // death — the victim may be a raider killed by a colonist — while relations are
                // still readable. The capture itself filters to diaried owners.
                if (__instance?.RaceProps?.Humanlike == true)
                {
                    DiaryGameComponent.Instance?.CaptureDeathKnowledge(__instance, dinfo);
                }

                // Pawn.Kill fires for every death — animals, raiders, mechs — but only a humanlike
                // colonist can ever get a death page. Gate before building/submitting the signal so
                // the common non-colonist kill does no allocation or bus work. The decider re-checks
                // eligibility, so this is a fast pre-filter, not the authority. The Prefix already
                // avoids the death snapshot for non-colonists while retaining mech/persona hooks.
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
            KillPatchState __state,
            System.Exception __exception)
        {
            DiaryPatchSafety.Run("PawnKillPatch.Finalizer", () =>
            {
                if (__state?.mechanitorScopeStarted == true) MechanitorDeathScope.End(__instance);
                PersonaKillThoughtCorrelation.End(__state?.personaKillScope);
            });
            return __exception;
        }
    }
}
