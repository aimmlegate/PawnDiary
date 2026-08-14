// Pure decoding of provider generation responses.
//
// Only structured JSON fields are surfaced. Raw bodies never become error text, and every provider
// detail is whitespace-normalized and bounded before the impure transport can log or display it.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>Decodes generation text, provider errors, and finish state for every wire mode.</summary>
    internal static class LlmProtocolResponseCodec
    {
        private const int MaximumProviderDetailChars = 512;

        /// <summary>
        /// Parses one complete (non-streaming) response body. <paramref name="httpStatusCode"/> is
        /// used only for retry disposition; HTTP ownership and status exceptions remain in LlmClient.
        /// </summary>
        public static LlmProtocolParseResult ParseGeneration(
            string responseJson,
            LlmProtocolMode mode,
            int httpStatusCode)
        {
            LlmProtocolParseResult result = new LlmProtocolParseResult();
            LlmProtocolMode normalized = LlmProtocolDispatcher.NormalizeMode(mode);
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(responseJson ?? string.Empty) as Dictionary<string, object>;
            }
            catch
            {
                result.ProviderError = "The endpoint returned malformed JSON.";
                result.FailureDisposition = FailureDisposition(
                    normalized,
                    httpStatusCode,
                    string.Empty);
                return result;
            }

            if (root == null)
            {
                result.ProviderError = "The endpoint did not return a JSON object.";
                result.FailureDisposition = FailureDisposition(
                    normalized,
                    httpStatusCode,
                    string.Empty);
                return result;
            }

            result.ParsedJsonObject = true;
            string errorType;
            switch (normalized)
            {
                case LlmProtocolMode.OpenAIResponses:
                    ParseOpenAI(root, LlmResponseMode.OpenAIResponses, result, out errorType);
                    break;
                case LlmProtocolMode.AnthropicMessages:
                    ParseAnthropic(root, result, out errorType);
                    break;
                case LlmProtocolMode.GeminiGenerateContent:
                    ParseGemini(root, result, out errorType);
                    break;
                case LlmProtocolMode.OllamaChat:
                    ParseOllama(root, result, out errorType);
                    break;
                default:
                    ParseOpenAI(root, LlmResponseMode.OpenAIChatCompletions, result, out errorType);
                    break;
            }

            bool httpSuccess = httpStatusCode >= 200 && httpStatusCode <= 299;
            if (!httpSuccess && string.IsNullOrWhiteSpace(result.ProviderError))
            {
                result.ProviderError = "The provider returned an HTTP error without structured details.";
            }

            if (string.IsNullOrWhiteSpace(result.ProviderError)
                && string.IsNullOrWhiteSpace(result.Text))
            {
                result.ProviderError = NoContentError(normalized);
            }

            if (!string.IsNullOrWhiteSpace(result.ProviderError))
            {
                result.ProviderError = BoundedDetail(result.ProviderError);
                result.FailureDisposition = FailureDisposition(
                    normalized,
                    httpStatusCode,
                    errorType);
            }
            else
            {
                result.FailureDisposition = LlmProtocolFailureDisposition.None;
            }

            return result;
        }

        /// <summary>Convenience overload for a successful HTTP response.</summary>
        public static LlmProtocolParseResult ParseGeneration(string responseJson, LlmProtocolMode mode)
        {
            return ParseGeneration(responseJson, mode, 200);
        }

        private static void ParseOpenAI(
            Dictionary<string, object> root,
            LlmResponseMode responseMode,
            LlmProtocolParseResult result,
            out string errorType)
        {
            result.Text = LlmResponseParser.ParseGeneratedText(root, responseMode) ?? string.Empty;
            result.ProviderError = LlmResponseParser.ExtractProviderError(
                root,
                responseMode,
                !string.IsNullOrWhiteSpace(result.Text)) ?? string.Empty;
            errorType = ErrorType(root);

            if (responseMode == LlmResponseMode.OpenAIResponses)
            {
                result.FinishReason = NormalizeFinishReason(StringField(root, "status"), false);
                Dictionary<string, object> incomplete = ObjectField(root, "incomplete_details");
                string incompleteReason = NormalizeFinishReason(StringField(incomplete, "reason"), false);
                result.Truncated = string.Equals(result.FinishReason, "incomplete", StringComparison.Ordinal)
                    || incompleteReason.IndexOf("max", StringComparison.Ordinal) >= 0;

                string refusal = OpenAIResponsesRefusal(root);
                if (!string.IsNullOrWhiteSpace(refusal))
                {
                    result.Refused = true;
                    if (string.IsNullOrWhiteSpace(result.Text)
                        && string.IsNullOrWhiteSpace(result.ProviderError))
                    {
                        result.ProviderError = "API refusal: " + BoundedDetail(refusal);
                    }
                }
                return;
            }

            Dictionary<string, object> choice = FirstObject(ArrayField(root, "choices"));
            result.FinishReason = NormalizeFinishReason(StringField(choice, "finish_reason"), false);
            result.Truncated = string.Equals(result.FinishReason, "length", StringComparison.Ordinal);
            result.Refused = string.Equals(result.FinishReason, "content_filter", StringComparison.Ordinal);
            Dictionary<string, object> message = ObjectField(choice, "message");
            string refusalText = StringField(message, "refusal");
            if (!string.IsNullOrWhiteSpace(refusalText))
            {
                result.Refused = true;
                if (string.IsNullOrWhiteSpace(result.Text)
                    && string.IsNullOrWhiteSpace(result.ProviderError))
                {
                    result.ProviderError = "API refusal: " + BoundedDetail(refusalText);
                }
            }
        }

        private static void ParseAnthropic(
            Dictionary<string, object> root,
            LlmProtocolParseResult result,
            out string errorType)
        {
            errorType = ErrorType(root);
            result.ProviderError = ProviderObjectError(root, "Anthropic", true);
            result.FinishReason = NormalizeFinishReason(StringField(root, "stop_reason"), false);
            result.Truncated = string.Equals(result.FinishReason, "max_tokens", StringComparison.Ordinal)
                || string.Equals(result.FinishReason, "model_context_window_exceeded", StringComparison.Ordinal);
            result.Refused = string.Equals(result.FinishReason, "refusal", StringComparison.Ordinal);

            StringBuilder text = new StringBuilder();
            object[] content = ArrayField(root, "content");
            for (int i = 0; i < content.Length; i++)
            {
                Dictionary<string, object> block = content[i] as Dictionary<string, object>;
                if (block == null
                    || !string.Equals(StringField(block, "type"), "text", StringComparison.OrdinalIgnoreCase))
                {
                    // Thinking, redacted_thinking, tool-use, and future block types never become
                    // saved diary prose.
                    continue;
                }

                AppendTextBlock(text, StringField(block, "text"));
            }
            result.Text = text.ToString();

            if (result.Refused && string.IsNullOrWhiteSpace(result.ProviderError))
            {
                string stopDetail = AnthropicStopDetail(ObjectField(root, "stop_details"));
                result.ProviderError = string.IsNullOrWhiteSpace(stopDetail)
                    ? "Anthropic refused the request."
                    : "Anthropic refusal: " + stopDetail;
            }
        }

        private static void ParseGemini(
            Dictionary<string, object> root,
            LlmProtocolParseResult result,
            out string errorType)
        {
            errorType = ErrorType(root);
            result.ProviderError = GeminiProviderError(root);

            Dictionary<string, object> promptFeedback = ObjectField(root, "promptFeedback");
            string blockReason = StringField(promptFeedback, "blockReason").Trim();
            if (!string.IsNullOrWhiteSpace(blockReason)
                && !string.Equals(blockReason, "BLOCK_REASON_UNSPECIFIED", StringComparison.OrdinalIgnoreCase))
            {
                result.Refused = true;
                if (string.IsNullOrWhiteSpace(result.ProviderError))
                {
                    result.ProviderError = "Gemini blocked the prompt (blockReason="
                        + BoundedDetail(blockReason) + ").";
                }
            }

            Dictionary<string, object> candidate = FirstObject(ArrayField(root, "candidates"));
            result.FinishReason = NormalizeFinishReason(StringField(candidate, "finishReason"), true);
            result.FinishMessage = BoundedDetail(StringField(candidate, "finishMessage"));
            result.Truncated = string.Equals(result.FinishReason, "MAX_TOKENS", StringComparison.Ordinal);
            if (IsGeminiRefusalReason(result.FinishReason))
            {
                result.Refused = true;
                if (string.IsNullOrWhiteSpace(result.ProviderError))
                {
                    result.ProviderError = "Gemini stopped the response (finishReason="
                        + result.FinishReason
                        + (string.IsNullOrEmpty(result.FinishMessage)
                            ? ")."
                            : ", message=" + result.FinishMessage + ").");
                }
            }

            Dictionary<string, object> content = ObjectField(candidate, "content");
            object[] parts = ArrayField(content, "parts");
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                Dictionary<string, object> part = parts[i] as Dictionary<string, object>;
                if (part == null || BoolField(part, "thought"))
                {
                    continue;
                }
                AppendTextBlock(text, StringField(part, "text"));
            }
            result.Text = text.ToString();
        }

        private static void ParseOllama(
            Dictionary<string, object> root,
            LlmProtocolParseResult result,
            out string errorType)
        {
            errorType = string.Empty;
            if (root.TryGetValue("error", out object errorObject) && errorObject != null)
            {
                result.ProviderError = "Ollama error: " + ErrorDetail(errorObject);
                Dictionary<string, object> errorFields = errorObject as Dictionary<string, object>;
                errorType = StringField(errorFields, "type");
            }

            Dictionary<string, object> message = ObjectField(root, "message");
            // message.thinking is intentionally ignored even when a model emits it unexpectedly.
            result.Text = StringField(message, "content");
            result.FinishReason = NormalizeFinishReason(StringField(root, "done_reason"), false);
            result.Truncated = string.Equals(result.FinishReason, "length", StringComparison.Ordinal)
                || string.Equals(result.FinishReason, "max_tokens", StringComparison.Ordinal);

            if (root.TryGetValue("done", out object doneObject)
                && doneObject is bool
                && !(bool)doneObject
                && string.IsNullOrWhiteSpace(result.ProviderError))
            {
                result.ProviderError = "Ollama returned an incomplete non-streaming response.";
            }
        }

        private static string ProviderObjectError(
            Dictionary<string, object> root,
            string provider,
            bool includeRequestId)
        {
            Dictionary<string, object> error = ObjectField(root, "error");
            if (error == null)
            {
                return string.Empty;
            }

            string type = BoundedDetail(StringField(error, "type"));
            if (string.IsNullOrEmpty(type))
            {
                type = BoundedDetail(StringField(error, "status"));
            }
            string message = BoundedDetail(StringField(error, "message"));
            string detail;
            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(message))
            {
                detail = type + ": " + message;
            }
            else
            {
                detail = string.IsNullOrEmpty(message) ? type : message;
            }
            if (string.IsNullOrEmpty(detail))
            {
                detail = "unspecified provider error";
            }

            if (includeRequestId)
            {
                string requestId = BoundedDetail(StringField(root, "request_id"));
                if (!string.IsNullOrEmpty(requestId))
                {
                    detail += " (request_id=" + requestId + ")";
                }
            }

            return provider + " error: " + detail;
        }

        /// <summary>
        /// Adds only documented, scalar Gemini error-detail fields. Arbitrary metadata dictionaries
        /// are deliberately ignored so a provider cannot smuggle a raw request or credential-shaped
        /// object into logs or status UI.
        /// </summary>
        private static string GeminiProviderError(Dictionary<string, object> root)
        {
            string errorText = ProviderObjectError(root, "Gemini", false);
            Dictionary<string, object> error = ObjectField(root, "error");
            StringBuilder summary = new StringBuilder();
            object[] details = ArrayField(error, "details");
            for (int i = 0; i < details.Length; i++)
            {
                Dictionary<string, object> detail = details[i] as Dictionary<string, object>;
                if (detail == null)
                {
                    continue;
                }

                AppendNamedDetail(summary, "reason", BoundedDetail(StringField(detail, "reason")));
                AppendNamedDetail(summary, "domain", BoundedDetail(StringField(detail, "domain")));

                // ErrorInfo keeps these useful routing hints inside metadata. Read only the two
                // documented scalar keys; arbitrary metadata remains deliberately invisible.
                Dictionary<string, object> metadata = ObjectField(detail, "metadata");
                AppendNamedDetail(summary, "service", BoundedDetail(StringField(metadata, "service")));
                AppendNamedDetail(summary, "method", BoundedDetail(StringField(metadata, "method")));

                object[] violations = ArrayField(detail, "fieldViolations");
                for (int j = 0; j < violations.Length; j++)
                {
                    Dictionary<string, object> violation = violations[j] as Dictionary<string, object>;
                    AppendNamedDetail(summary, "field", BoundedDetail(StringField(violation, "field")));
                    AppendNamedDetail(
                        summary,
                        "description",
                        BoundedDetail(StringField(violation, "description")));
                }
            }

            if (summary.Length == 0)
            {
                return errorText;
            }

            return errorText + " (details: " + summary + ")";
        }

        private static void AppendNamedDetail(StringBuilder builder, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }
            builder.Append(name);
            builder.Append("=");
            builder.Append(value);
        }

        private static LlmProtocolFailureDisposition FailureDisposition(
            LlmProtocolMode mode,
            int statusCode,
            string errorType)
        {
            if (statusCode == 408
                || statusCode == 429
                || (statusCode >= 500 && statusCode <= 599)
                || (mode == LlmProtocolMode.AnthropicMessages && statusCode == 409))
            {
                return LlmProtocolFailureDisposition.Transient;
            }

            string normalized = (errorType ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.IndexOf("rate_limit", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("resource_exhausted", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("overloaded", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("unavailable", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("timeout", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("deadline_exceeded", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("aborted", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("server_error", StringComparison.Ordinal) >= 0)
            {
                return LlmProtocolFailureDisposition.Transient;
            }

            return LlmProtocolFailureDisposition.Permanent;
        }

        private static string NoContentError(LlmProtocolMode mode)
        {
            switch (mode)
            {
                case LlmProtocolMode.AnthropicMessages:
                    return "Anthropic returned no text content.";
                case LlmProtocolMode.GeminiGenerateContent:
                    return "Gemini returned no text content.";
                case LlmProtocolMode.OllamaChat:
                    return "Ollama returned no message content.";
                default:
                    return "The endpoint returned no message content.";
            }
        }

        private static bool IsGeminiRefusalReason(string finishReason)
        {
            if (string.IsNullOrEmpty(finishReason))
            {
                return false;
            }

            switch (finishReason)
            {
                case "SAFETY":
                case "RECITATION":
                case "LANGUAGE":
                case "BLOCKLIST":
                case "PROHIBITED_CONTENT":
                case "IMAGE_SAFETY":
                case "IMAGE_PROHIBITED_CONTENT":
                case "IMAGE_RECITATION":
                case "SPII":
                case "ESCALATION":
                    return true;
                default:
                    return false;
            }
        }

        private static string OpenAIResponsesRefusal(Dictionary<string, object> root)
        {
            object[] output = ArrayField(root, "output");
            for (int i = 0; i < output.Length; i++)
            {
                Dictionary<string, object> item = output[i] as Dictionary<string, object>;
                object[] content = ArrayField(item, "content");
                for (int j = 0; j < content.Length; j++)
                {
                    Dictionary<string, object> part = content[j] as Dictionary<string, object>;
                    if (part != null
                        && string.Equals(StringField(part, "type"), "refusal", StringComparison.OrdinalIgnoreCase))
                    {
                        string refusal = StringField(part, "refusal");
                        return string.IsNullOrWhiteSpace(refusal)
                            ? StringField(part, "text")
                            : refusal;
                    }
                }
            }
            return string.Empty;
        }

        private static string ErrorType(Dictionary<string, object> root)
        {
            Dictionary<string, object> error = ObjectField(root, "error");
            string type = StringField(error, "type");
            return string.IsNullOrWhiteSpace(type) ? StringField(error, "status") : type;
        }

        private static string ErrorDetail(object value)
        {
            string text = value as string;
            if (text != null)
            {
                return BoundedDetail(text);
            }

            Dictionary<string, object> fields = value as Dictionary<string, object>;
            if (fields == null)
            {
                return string.Empty;
            }

            string type = BoundedDetail(StringField(fields, "type"));
            string message = BoundedDetail(StringField(fields, "message"));
            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(message))
            {
                return type + ": " + message;
            }
            return string.IsNullOrEmpty(message) ? type : message;
        }

        /// <summary>
        /// Formats Anthropic's documented refusal metadata without accepting arbitrary nested
        /// objects or obsolete message-shaped fixtures as provider diagnostics.
        /// </summary>
        private static string AnthropicStopDetail(Dictionary<string, object> fields)
        {
            if (fields == null)
            {
                return string.Empty;
            }

            StringBuilder summary = new StringBuilder();
            AppendNamedDetail(summary, "type", BoundedDetail(StringField(fields, "type")));
            AppendNamedDetail(summary, "category", BoundedDetail(StringField(fields, "category")));
            AppendNamedDetail(summary, "explanation", BoundedDetail(StringField(fields, "explanation")));
            return BoundedDetail(summary.ToString());
        }

        private static void AppendTextBlock(StringBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            if (builder.Length > 0)
            {
                builder.Append("\n");
            }
            builder.Append(text);
        }

        private static Dictionary<string, object> ObjectField(
            Dictionary<string, object> fields,
            string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return null;
            }
            return value as Dictionary<string, object>;
        }

        private static object[] ArrayField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return new object[0];
            }
            return value as object[] ?? new object[0];
        }

        private static Dictionary<string, object> FirstObject(object[] values)
        {
            return values != null && values.Length > 0
                ? values[0] as Dictionary<string, object>
                : null;
        }

        private static string StringField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return string.Empty;
            }
            return value as string ?? string.Empty;
        }

        private static bool BoolField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            string text = value as string;
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bounds provider-owned finish metadata and keeps it on one line before applying the
        /// casing expected by each protocol's comparisons and diagnostics.
        /// </summary>
        private static string NormalizeFinishReason(string value, bool upperCase)
        {
            string normalized = BoundedDetail(value);
            return upperCase ? normalized.ToUpperInvariant() : normalized.ToLowerInvariant();
        }

        private static string BoundedDetail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(Math.Min(value.Length, MaximumProviderDetailChars));
            bool pendingSpace = false;
            for (int i = 0; i < value.Length && builder.Length < MaximumProviderDetailChars; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace && builder.Length < MaximumProviderDetailChars)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                if (builder.Length < MaximumProviderDetailChars)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString().Trim();
        }
    }
}
