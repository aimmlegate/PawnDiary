// Payload + pure decision for a "temporary thought gained" event (the MemoryThoughtHandler
// .TryGainMemory hook). This is the first source migrated to the Event Catalog pattern and the
// richest one we ship in this slice: it exercises token filters, magnitude thresholds (general +
// eating), bypass tokens, ambient routing, and disabled-policy gating.
//
// The decision logic in Decide() is intentionally pure: it only reads primitives off the payload
// (DefName, MoodOffset, DurationDays, MoodImpact) and the snapshot context (CaptureContext +
// ThoughtCapturePolicy). The RimWorld-facing assembly (label, instruction, game-context text, LLM
// queue) stays in DiaryGameComponent.RecordThought, which calls Decide() and then performs the
// impure side-effects the decision requests. Splitting "should we?" (here, pure) from "how to
// describe it?" (in RecordThought, impure) is what makes this unit-testable without RimWorld.
//
// Token matching (MatchesAny) is a case-insensitive substring check against the thought's defName,
// identical to the pre-refactor MatchesAnyToken helper. It moved here so the test suite can exercise
// it directly without linking RimWorld assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for a temporary-thought event. Filled by DiaryGameComponent.RecordThought from
    /// the live Pawn + Thought_Memory, then handed to Decide() for the pure decision.
    /// </summary>
    public class ThoughtEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Thought;

        /// <summary>The thought's defName (e.g. "AteWithoutTable"). Used for token matching and the
        /// dedup key.</summary>
        public string DefName;

        /// <summary>Current-stage mood offset of the memory. Decides the magnitude gate and feeds the
        /// game-context marker.</summary>
        public float MoodOffset;

        /// <summary>The thought's duration in days. Thoughts with durationDays &lt;= 0 are permanent
        /// traits and are dropped.</summary>
        public float DurationDays;

        /// <summary>Pre-classified mood direction ("positive"/"negative"/"neutral"). Classification
        /// needs MoodImpact.Classify which lives in Verse-using code, so the caller computes this
        /// before calling Decide() and passes the result here.</summary>
        public string MoodImpact;

        /// <summary>Frozen snapshot of Thought policy (tokens + thresholds). Caller copies it from
        /// DiarySignalPolicies.* so Decide() does not touch the DefDatabase.</summary>
        public ThoughtCapturePolicy Policy;

        /// <summary>
        /// Pure decision for a thought event. Order of checks matches the pre-refactor RecordThought
        /// body so observable behavior is byte-for-byte identical:
        /// 1. eligibility/signal/user gating,
        /// 2. permanent-thought drop (durationDays &lt;= 0),
        /// 3. ignore-token drop,
        /// 4. magnitude threshold (bypass tokens skip it; eating tokens use the higher bar),
        /// 5. ambient routing when ambient policy enabled + ambient token matches,
        /// 6. otherwise record as a solo event.
        /// </summary>
        public static CaptureDecision Decide(ThoughtEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            // Step 1: gates that live on CaptureContext (computed from Pawn + Settings + DefDatabase
            // by the caller). Failing any of them drops the event before any token work.
            if (!ctx.Eligible || !ctx.SignalEnabled || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            // Step 2: permanent thoughts (traits, etc.) — never expire, never diary-worthy as an event.
            if (data.DurationDays <= 0f)
            {
                return CaptureDecision.Drop;
            }

            ThoughtCapturePolicy policy = data.Policy;

            // Step 3: ignore-token filter. These defNames are configured in XML as "never diary this".
            if (policy != null && MatchesAny(data.DefName, policy.IgnoreTokens))
            {
                return CaptureDecision.Drop;
            }

            // Step 4: magnitude threshold. Bypass tokens (death, banishment, ...) skip it; eating
            // tokens use the higher bar so we don't diary every meal.
            bool bypassThreshold = policy != null && MatchesAny(data.DefName, policy.BypassThresholdTokens);
            if (!bypassThreshold)
            {
                bool isEatingThought = policy != null && MatchesAny(data.DefName, policy.EatingTokens);
                float minMoodOffset = isEatingThought
                    ? (policy != null ? policy.EatingMinMoodOffset : 0f)
                    : (policy != null ? policy.MinMoodOffset : 0f);
                if (Math.Abs(data.MoodOffset) < minMoodOffset)
                {
                    return CaptureDecision.Drop;
                }
            }

            // Step 5: ambient routing. Low-stakes thoughts batch into one per-pawn day note instead
            // of becoming their own entry. Both the ambient policy and an ambient-token match are
            // required; otherwise the thought becomes a normal solo event.
            if (ctx.AmbientSignalEnabled
                && policy != null
                && MatchesAny(data.DefName, policy.AmbientTokens))
            {
                return CaptureDecision.RouteAmbient;
            }

            // Step 6: default — record as a solo event from this pawn's POV.
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Case-insensitive substring match: returns true if defName contains any of the tokens.
        /// Empty/null defName or token lists never match. Moved here from the old
        /// DiaryGameComponent.Thoughts.cs MatchesAnyToken helper unchanged.
        /// </summary>
        internal static bool MatchesAny(string defName, IReadOnlyList<string> tokens)
        {
            if (string.IsNullOrEmpty(defName) || tokens == null)
            {
                return false;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrEmpty(token)
                    && defName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
