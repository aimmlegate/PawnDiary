// Pure runtime policy applied after the provider response codec has decoded bounded fields.
//
// Keeping this decision outside HttpClient makes the subtle "normal finish reason plus valid text"
// case independently testable without loading RimWorld or contacting a provider.
using System;

namespace PawnDiary
{
    /// <summary>Formats safe native-provider failure detail for the impure transport adapter.</summary>
    internal static class LlmProtocolRuntimePolicy
    {
        /// <summary>
        /// Returns an empty string for a valid text response. Otherwise formats only already-bounded
        /// structured provider fields, including a Gemini finish message when no text is usable.
        /// </summary>
        public static string NativeProviderFailureDetail(LlmProtocolParseResult result)
        {
            if (result == null)
            {
                return "The provider returned no structured error details.";
            }

            string detail = result.ProviderError ?? string.Empty;
            if (string.IsNullOrWhiteSpace(detail)
                && !string.IsNullOrWhiteSpace(result.Text))
            {
                return string.Empty;
            }

            string finishReason = (result.FinishReason ?? string.Empty).Trim();
            string finishMessage = (result.FinishMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(finishReason)
                && string.IsNullOrWhiteSpace(finishMessage))
            {
                return detail;
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = "The provider returned no usable message content.";
            }

            string finish = string.IsNullOrWhiteSpace(finishReason)
                ? string.Empty
                : "finish_reason=" + finishReason;
            if (!string.IsNullOrWhiteSpace(finishMessage))
            {
                finish += (finish.Length == 0 ? string.Empty : ", ")
                    + "message=" + finishMessage;
            }

            return detail + " (" + finish + ")";
        }
    }
}
