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
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for a temporary-thought event. Filled by DiaryGameComponent.RecordThought from
    /// the live Pawn + Thought_Memory, then handed to Decide() for the pure decision.
    /// </summary>
    internal class ThoughtEventData : DiaryEventData
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
        /// True only when an exact XML ownership rule matched and its downstream group was effectively
        /// enabled at capture time. Failed-conversion thoughts use this to avoid a second page beside
        /// the canonical conversion interaction while remaining available if that group is disabled.
        /// </summary>
        public bool DownstreamCovered;

        /// <summary>
        /// Pure decision for a thought event. The original RecordThought token/magnitude order remains
        /// intact after the exact canonical-owner gate:
        /// 1. eligibility/signal/user gating,
        /// 2. exact downstream-owner drop,
        /// 3. permanent-thought drop (durationDays &lt;= 0),
        /// 4. ignore-token drop,
        /// 5. magnitude threshold (bypass tokens skip it; eating tokens use the higher bar),
        /// 6. ambient routing when ambient policy enabled + ambient token matches,
        /// 7. otherwise record as a solo event.
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

            // Step 2: a demonstrable enabled downstream event owns the canonical page.
            if (data.DownstreamCovered)
            {
                return CaptureDecision.Drop;
            }

            // Step 3: permanent thoughts (traits, etc.) — never expire, never diary-worthy as an event.
            if (data.DurationDays <= 0f)
            {
                return CaptureDecision.Drop;
            }

            ThoughtCapturePolicy policy = data.Policy;

            // Step 4: ignore-token filter. These defNames are configured in XML as "never diary this".
            if (policy != null && MatchesAny(data.DefName, policy.IgnoreTokens))
            {
                return CaptureDecision.Drop;
            }

            // Step 5: magnitude threshold. Bypass tokens (death, banishment, ...) skip it; eating
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

            // Step 6: ambient routing. Low-stakes thoughts batch into one per-pawn day note instead
            // of becoming their own entry. Both the ambient policy and an ambient-token match are
            // required; otherwise the thought becomes a normal solo event.
            if (ctx.AmbientSignalEnabled
                && policy != null
                && MatchesAny(data.DefName, policy.AmbientTokens))
            {
                return CaptureDecision.RouteAmbient;
            }

            // Step 7: default — record as a solo event from this pawn's POV.
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// The transient dedup key for this thought event (raw, source-prefixed). Lifted verbatim out
        /// of the old RecordThought so the shared recent-events store keys it exactly as before:
        /// one window per pawn + thought defName.
        /// </summary>
        public string DedupKey()
        {
            return "thought|" + PawnId + "|" + DefName;
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

        /// <summary>
        /// Pure assembly of the thought's game-context marker string. Inputs must already be cleaned
        /// (RecordThought runs DiaryLineCleaner.CleanLine on the label before calling). The format
        /// is load-bearing: the UI parses the leading "thought=" marker to classify the event into
        /// the Thought domain, and the LLM reads the rest as prompt evidence. Keeping the format in a
        /// pure helper means tests can lock it down — a future migration that drifts the format (e.g.
        /// changes the F1 precision, reorders fields, renames a key) will fail the format tests.
        /// </summary>
        public static string BuildGameContext(
            string defName, string label, string moodImpact, float moodOffset, float durationDays)
        {
            return "thought=" + defName
                + "; label=" + label
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset.ToString("F1", CultureInfo.InvariantCulture)
                + "; duration_days=" + durationDays.ToString("F1", CultureInfo.InvariantCulture);
        }
    }
}
