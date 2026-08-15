// Pure input-boundary policy for one-shot LLM completions. Requests from other mods are untrusted
// and keep the public 4,000-character safety cap; prompts assembled by Pawn Diary itself have already
// passed the ordinary field-selection budgets and must reach transport intact.
using System;

namespace PawnDiary
{
    /// <summary>Separates public adapter limits from trusted, internally assembled prompt envelopes.</summary>
    internal static class LlmCompletionInputPolicy
    {
        // Stable defensive schema limit for public integrations, not player prompt tuning.
        public const int PublicMaxInputCharacters = 4000;

        /// <summary>Returns a null-safe, Unicode-safe capped public-adapter string.</summary>
        public static string ForPublicAdapter(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length > PublicMaxInputCharacters
                ? TextTruncation.SafePrefix(value, PublicMaxInputCharacters)
                : value;
        }

        /// <summary>
        /// Returns Pawn Diary's own assembled prompt unchanged. Internal callers remain responsible for
        /// applying their normal structured-context budgets before this transport boundary.
        /// </summary>
        public static string ForTrustedInternalPrompt(string value)
        {
            return value ?? string.Empty;
        }
    }

    /// <summary>
    /// Pure admission policy for the shared public/internal one-shot completion table. Public callers
    /// may use the paid-work capacity except for a small internal reserve; every caller still obeys
    /// the same fixed total cap.
    /// </summary>
    internal static class LlmCompletionCapacityPolicy
    {
        /// <summary>
        /// True when one request can enter the table without exceeding either the total hard cap or
        /// the public partition. Counts are snapshots taken under the service's existing lock.
        /// </summary>
        public static bool CanAccept(
            int totalTracked,
            int publicTracked,
            bool trustedInternal,
            int maximumTracked,
            int reservedForInternal)
        {
            if (maximumTracked < 1
                || totalTracked < 0
                || publicTracked < 0
                || publicTracked > totalTracked
                || totalTracked >= maximumTracked)
            {
                return false;
            }

            int reserve = reservedForInternal;
            if (reserve < 0)
            {
                reserve = 0;
            }
            else if (reserve > maximumTracked)
            {
                reserve = maximumTracked;
            }

            return trustedInternal || publicTracked < maximumTracked - reserve;
        }
    }
}
