// Non-emitting Ideology history observer. RecordEvent remains owned by RimWorld; this postfix only
// snapshots explicit visible pawn arguments into a bounded plain sidecar. It never submits a signal,
// calls an event factory, changes capture eligibility, or persists its transient rows.
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Observes exact recent history identities for later correlation to authorized pages.</summary>
    [HarmonyPatch(typeof(HistoryEventsManager), nameof(HistoryEventsManager.RecordEvent))]
    internal static class HistoryEventObserverPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HistoryEvent historyEvent)
        {
            DiaryPatchSafety.Run("HistoryEventObserverPatch", historyEvent, observedEvent =>
            {
                // This is a frequently used vanilla choke point. Gate before inspecting arguments or
                // allocating a policy snapshot in base/no-Ideology games.
                if (!ModsConfig.IdeologyActive || !DiaryBeliefPolicy.Enabled) return;
                BeliefHistoryObservation observation;
                if (!DlcContext.TryCaptureBeliefHistoryObservation(observedEvent, out observation)) return;
                BeliefHistoryCorrelationCache.Observe(observation, DiaryBeliefPolicy.Snapshot());
            });
        }
    }
}
