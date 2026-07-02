// Health-related Harmony patches. This file only forwards live hediff additions; the importance
// rules, per-day accumulation, and progression scans live in DiaryGameComponent and XML policy.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Fires whenever a hediff is added to any pawn (injuries, diseases, addictions, implants...).
    // We forward colonist additions so a major new affliction can feed that pawn's end-of-day
    // reflection. The "is this major enough?" filter and per-day accumulation live in
    // DiaryGameComponent (DaySummary.cs), keeping this hook thin and the threshold XML-tunable.
    // AddHediff has overloads, so the argument-type array selects the canonical Hediff overload
    // every add path routes through. hediff.pawn is already assigned by the time the postfix runs.
    /// <summary>
    /// Forwards newly-added colonist hediffs to the diary health/day-summary capture flow.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class HealthTrackerAddHediffPatch
    {
        /// <summary>
        /// Harmony Postfix for Pawn_HealthTracker.AddHediff. Forwards a colonist's newly-added
        /// hediff to the diary so the day-summary builder can decide whether it is worth remembering.
        /// </summary>
        public static void Postfix(Hediff hediff)
        {
            // Hot hook: AddHediff fires for every wound on every pawn in combat. Both Run calls use
            // the state-passing overload so neither lambda captures a local — a capturing lambda here
            // would allocate a closure per hediff even on the common non-colonist early-out.
            DiaryPatchSafety.Run("HealthTrackerAddHediffPatch", hediff, h =>
            {
                Pawn pawn = h?.pawn;
                // RimWorld rolls old-age injuries onto starting pawns during generation, before the game
                // is playing; skip those so RecordHediffAppeared does not read TicksAbs (via CurrentDayIndex)
                // before the world clock exists. Starting hediffs are baselined by the first scan instead.
                if (pawn == null || !pawn.IsColonist || !DiaryGameComponent.GamePlaying)
                {
                    return;
                }

                DiaryGameComponent component = DiaryGameComponent.Current;
                if (component == null)
                {
                    return;
                }

                // Isolate the optional event-window signal so a failure there cannot skip the mature
                // hediff capture below (both ran in this one DiaryPatchSafety.Run lambda before).
                DiaryPatchSafety.Run("HealthTrackerAddHediffPatch.EventWindow", (component, pawn, hediff: h),
                    s => s.component.RecordEventWindowHediffAdded(s.pawn, s.hediff));
                DiaryEvents.Submit(new HediffSignal(pawn, h, HediffSignalSource.Appeared));
            });
        }
    }
}
