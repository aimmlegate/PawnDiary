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
}
