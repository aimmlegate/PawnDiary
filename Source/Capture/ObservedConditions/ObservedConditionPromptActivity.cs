// Observed conditions, prompt-activity gate: a small pure helper that decides whether a saved
// condition row is still allowed to bias prompts. The lifecycle policy may keep an ended row around
// briefly so the impure adapter can retry an optional end diary page; this helper prevents that
// retry bookkeeping from keeping the prompt override alive after the condition has resolved.
//
// New to C#/RimWorld? See AGENTS.md. This file deliberately uses no Verse/RimWorld types so the
// behavior stays covered by the standalone observed-condition tests.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Pure prompt gate for saved observed-condition rows.
    /// </summary>
    internal static class ObservedConditionPromptActivity
    {
        /// <summary>
        /// Returns true while a condition has really started and is either still observed or still inside
        /// its configured missing/end debounce. Once that debounce elapses, prompt bias stops even if the
        /// saved row remains for an optional end-page retry.
        /// </summary>
        public static bool IsPromptActive(bool startRecorded, bool endRecorded, int firstMissingTick,
            int now, int endDebounceTicks)
        {
            if (!startRecorded || endRecorded)
            {
                return false;
            }

            if (firstMissingTick < 0)
            {
                return true;
            }

            int safeEndDebounceTicks = Math.Max(0, endDebounceTicks);
            return now - firstMissingTick < safeEndDebounceTicks;
        }
    }
}
