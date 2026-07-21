// Runtime owner for the pure bounded history buffer. Harmony observation writes plain rows here;
// event-time belief building reads exact nearby Def identities. No method can create a DiaryEvent.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Process-static, game-boundary-reset sidecar for non-emitting HistoryEvent evidence.</summary>
    internal static class BeliefHistoryCorrelationCache
    {
        private static readonly BeliefHistoryCorrelationBuffer Buffer =
            new BeliefHistoryCorrelationBuffer();

        public static int Count => Buffer.Count;

        public static void Observe(BeliefHistoryObservation observation, BeliefPolicySnapshot policy)
        {
            if (observation == null || policy == null || !policy.enabled) return;
            Buffer.Observe(observation, observation.tick,
                policy.maximumHistoryCorrelationEntries, policy.historyCorrelationWindowTicks);
        }

        public static List<string> NearbyDefNames(
            string pawnId,
            int eventTick,
            BeliefPolicySnapshot policy)
        {
            return policy == null
                ? new List<string>()
                : Buffer.NearbyDefNames(pawnId, eventTick, policy.historyCorrelationWindowTicks);
        }

        public static void Reset()
        {
            Buffer.Clear();
        }
    }
}
