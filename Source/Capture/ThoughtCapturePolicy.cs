// Frozen snapshot of the tunable Thought policy (token lists + magnitude thresholds). The sink
// (DiaryGameComponent.RecordThought) copies these values out of DiarySignalPolicies once, right
// before invoking the pure Decider, so the Decider itself never reaches into the RimWorld
// DefDatabase. This is the seam that keeps ThoughtEventData.Decide unit-testable.
//
// Tokens are matched by case-insensitive substring against the thought's defName — the same rule used
// before this refactor — so existing XML tuning in DiarySignalPolicyDef keeps working unchanged.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Read-only view of the Thought signal policy at the moment an event is being decided. The
    /// caller fills this from DiarySignalPolicies.* getters.
    /// </summary>
    internal class ThoughtCapturePolicy
    {
        /// <summary>Thoughts whose defName contains any of these tokens are dropped outright.</summary>
        public IReadOnlyList<string> IgnoreTokens;

        /// <summary>Thoughts whose defName contains any of these tokens skip the magnitude threshold
        /// (death of a friend, banishment, etc. — always recorded).</summary>
        public IReadOnlyList<string> BypassThresholdTokens;

        /// <summary>Thoughts whose defName contains any of these tokens use the higher eating
        /// threshold instead of the general one.</summary>
        public IReadOnlyList<string> EatingTokens;

        /// <summary>Low-stakes thoughts routed to the ambient day-note batcher when ambient routing
        /// is enabled. defName substring match.</summary>
        public IReadOnlyList<string> AmbientTokens;

        /// <summary>Minimum |moodOffset| for a non-eating, non-bypass thought to be recorded.</summary>
        public float MinMoodOffset;

        /// <summary>Minimum |moodOffset| for an eating thought to be recorded (higher bar, because
        /// eating thoughts fire constantly).</summary>
        public float EatingMinMoodOffset;
    }
}
