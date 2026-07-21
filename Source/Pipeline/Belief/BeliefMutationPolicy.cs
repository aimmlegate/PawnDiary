// Pure construction policy for Ideology before/after mutation facts. Runtime Harmony adapters hand
// this file detached states only; it contains no RimWorld, Verse, Unity, Def, or DLC dependency.
using System;

namespace PawnDiary
{
    /// <summary>Stable mechanical cause tokens. They identify hooked methods, never doctrine policy.</summary>
    internal static class BeliefMutationCauseTokens
    {
        public const string ConversionAttempt = "conversion_attempt";
        public const string CertaintyOffset = "certainty_offset";
        public const string SetIdeology = "set_ideology";

        /// <summary>Returns true only for a stable method-boundary token owned by this slice.</summary>
        public static bool IsKnown(string value)
        {
            return value == ConversionAttempt || value == CertaintyOffset || value == SetIdeology;
        }
    }

    /// <summary>Builds one sanitized mutation boundary from two detached states.</summary>
    internal static class BeliefMutationPolicy
    {
        private const float CertaintyEpsilon = 0.0001f;

        /// <summary>Creates one detached call boundary or returns null for malformed/unknown input.</summary>
        public static BeliefMutationSnapshot Create(
            BeliefMutationState before,
            BeliefMutationState after,
            BeliefMutationState attempted,
            string causeToken,
            bool? conversionSucceeded,
            long startedSequence,
            long completedSequence)
        {
            if (before == null || after == null
                || string.IsNullOrWhiteSpace(before.pawnId)
                || !string.Equals(before.pawnId, after.pawnId, StringComparison.Ordinal))
                return null;

            BeliefMutationSnapshot result = new BeliefMutationSnapshot
            {
                pawnId = before.pawnId,
                capturedTick = Math.Max(0, before.capturedTick),
                beforeIdeologyId = before.ideologyId ?? string.Empty,
                beforeIdeologyName = before.ideologyName ?? string.Empty,
                afterIdeologyId = after.ideologyId ?? string.Empty,
                afterIdeologyName = after.ideologyName ?? string.Empty,
                attemptedIdeologyId = attempted?.ideologyId ?? string.Empty,
                attemptedIdeologyName = attempted?.ideologyName ?? string.Empty,
                hasBeforeCertainty = before.hasCertainty,
                beforeCertainty = before.certainty,
                hasAfterCertainty = after.hasCertainty,
                afterCertainty = after.certainty,
                conversionSucceeded = conversionSucceeded,
                startedSequence = Math.Max(0L, startedSequence),
                completedSequence = Math.Max(0L, completedSequence)
            };
            result.ideologyChanged = result.beforeIdeologyId.Length > 0
                && result.afterIdeologyId.Length > 0
                && !string.Equals(result.beforeIdeologyId, result.afterIdeologyId, StringComparison.Ordinal);
            result.certaintyChanged = result.hasBeforeCertainty && result.hasAfterCertainty
                && Math.Abs(result.afterCertainty - result.beforeCertainty) > CertaintyEpsilon;
            if (BeliefMutationCauseTokens.IsKnown(causeToken)) result.causeTokens.Add(causeToken);
            result.observedMutation = result.ideologyChanged || result.certaintyChanged
                || result.conversionSucceeded.HasValue;

            // A no-op outer boundary is returned so it can absorb and cancel transient nested changes.
            // The buffer discards it when the coalesced earliest-before/latest-after facts have no net
            // mutation. Unknown causes have no such ownership value and fail closed here.
            return result.observedMutation || result.causeTokens.Count > 0 ? result : null;
        }

        /// <summary>Recomputes derived flags after a coalescer replaces either endpoint.</summary>
        internal static void RefreshDerivedFacts(BeliefMutationSnapshot value)
        {
            if (value == null) return;
            value.ideologyChanged = value.beforeIdeologyId.Length > 0
                && value.afterIdeologyId.Length > 0
                && !string.Equals(value.beforeIdeologyId, value.afterIdeologyId, StringComparison.Ordinal);
            value.certaintyChanged = value.hasBeforeCertainty && value.hasAfterCertainty
                && Math.Abs(value.afterCertainty - value.beforeCertainty) > CertaintyEpsilon;
            value.observedMutation = value.ideologyChanged || value.certaintyChanged
                || value.conversionSucceeded.HasValue;
        }
    }
}
