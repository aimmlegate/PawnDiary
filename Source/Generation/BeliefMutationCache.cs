// Runtime owner for the pure belief-mutation buffer. Exact Harmony boundaries write detached rows;
// later event enrichment may peek at them. This sidecar cannot create DiaryEvents and is reset at
// every game boundary, so it is neither scribed nor retroactive for old saves.
namespace PawnDiary
{
    /// <summary>Process-static, game-boundary-reset sidecar for transient mutation correlation.</summary>
    internal static class BeliefMutationCache
    {
        private static readonly BeliefMutationBuffer Buffer = new BeliefMutationBuffer();
        private static long sequence;

        public static int Count => Buffer.Count;

        /// <summary>Returns a monotonic call-boundary order used to coalesce nested tracker methods.</summary>
        public static long NextSequence()
        {
            sequence = sequence == long.MaxValue ? 1L : sequence + 1L;
            return sequence;
        }

        /// <summary>Coalesces one detached mutation under the current XML bounds.</summary>
        public static void RecordOrMerge(BeliefMutationSnapshot mutation, BeliefPolicySnapshot policy)
        {
            if (mutation == null || policy == null || !policy.enabled) return;
            Buffer.RecordOrMerge(mutation, mutation.capturedTick,
                policy.maximumMutationCorrelationEntries, policy.mutationCorrelationWindowTicks);
        }

        /// <summary>Returns the newest nearby row for an exact pawn without consuming it.</summary>
        public static BeliefMutationSnapshot PeekLatest(
            string pawnId,
            int eventTick,
            BeliefPolicySnapshot policy)
        {
            return policy == null
                ? null
                : Buffer.PeekLatest(pawnId, eventTick, policy.mutationCorrelationWindowTicks);
        }

        /// <summary>Clears every row and ordering token at a game/test boundary.</summary>
        public static void Reset()
        {
            Buffer.Clear();
            sequence = 0L;
        }
    }
}
