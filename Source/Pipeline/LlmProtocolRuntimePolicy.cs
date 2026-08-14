// Pure runtime policy applied after the provider response codec has decoded structured fields.
//
// Keeping this decision outside HttpClient makes the subtle "normal finish reason plus valid text"
// case independently testable without loading RimWorld or contacting a provider. This boundary also
// sanitizes every diagnostic defensively because integration callers can construct parse results.
using System;
using System.Text;

namespace PawnDiary
{
    /// <summary>Formats safe native-provider failure detail for the impure transport adapter.</summary>
    internal static class LlmProtocolRuntimePolicy
    {
        // Keep persisted/status diagnostics aligned with the response codec's defensive field cap.
        private const int MaximumFailureDetailChars = 512;

        /// <summary>
        /// Returns an empty string for a valid text response. Otherwise formats a bounded, one-line
        /// diagnostic from structured provider fields, including finish metadata when available.
        /// </summary>
        public static string NativeProviderFailureDetail(LlmProtocolParseResult result)
        {
            if (result == null)
            {
                return "The provider returned no structured error details.";
            }

            string detail = BoundedOneLine(result.ProviderError);
            string text = BoundedOneLine(result.Text);
            if (detail.Length == 0)
            {
                if (text.Length > 0)
                {
                    return string.Empty;
                }

                detail = "The provider returned no usable message content.";
            }

            string finishReason = BoundedOneLine(result.FinishReason);
            string finishMessage = BoundedOneLine(result.FinishMessage);
            if (finishReason.Length == 0 && finishMessage.Length == 0)
            {
                return detail;
            }

            string finish = finishReason.Length == 0
                ? string.Empty
                : "finish_reason=" + finishReason;
            if (finishMessage.Length > 0)
            {
                finish += (finish.Length == 0 ? string.Empty : ", ")
                    + "message=" + finishMessage;
            }

            // Cap the composed value too: individually safe fields can still exceed the persistence
            // budget when concatenated, especially for directly constructed integration results.
            return BoundedOneLine(detail + " (" + finish + ")");
        }

        /// <summary>Collapses whitespace/control runs and caps one untrusted diagnostic value.</summary>
        private static string BoundedOneLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(
                Math.Min(value.Length, MaximumFailureDetailChars));
            bool pendingSpace = false;
            for (int i = 0; i < value.Length && builder.Length < MaximumFailureDetailChars; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace && builder.Length < MaximumFailureDetailChars)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                if (builder.Length < MaximumFailureDetailChars)
                {
                    builder.Append(c);
                }
            }

            // A hard UTF-16 cap must not leave a dangling high surrogate in persisted diagnostics.
            if (builder.Length > 0 && char.IsHighSurrogate(builder[builder.Length - 1]))
            {
                builder.Length--;
            }
            return builder.ToString().Trim();
        }
    }
}
