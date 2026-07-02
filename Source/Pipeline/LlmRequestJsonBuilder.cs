// Pure serializer for LLM generation request bodies.
//
// LlmClient owns the HTTP transport, retries, auth, and background threading. This helper owns only
// the small mode-specific JSON shape, so serialization behavior can be tested without RimWorld,
// Verse, Unity, or an HTTP endpoint. It deliberately builds JSON by hand because RimWorld's Mono
// runtime may not provide System.Text.Json.
using System;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Plain request-shape snapshot for serializing one LLM generation HTTP body.
    /// </summary>
    public class LlmRequestJsonInput
    {
        public ApiCompatibilityMode apiMode;
        public string modelName;
        public string systemPrompt;
        public string rawText;
        public string reasoningEffort;
        public int maxTokens;
        public float temperature;
    }

    /// <summary>
    /// Builds JSON request bodies for the supported OpenAI-compatible generation modes.
    /// </summary>
    public static class LlmRequestJsonBuilder
    {
        /// <summary>
        /// Returns the JSON body for one generation request in the selected compatibility mode.
        /// </summary>
        public static string Build(LlmRequestJsonInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            switch (ApiEndpointPolicy.NormalizeApiMode(input.apiMode))
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return BuildOpenAIResponsesRequestJson(input);
                default:
                    return BuildOpenAIChatRequestJson(input);
            }
        }

        private static string BuildOpenAIChatRequestJson(LlmRequestJsonInput input)
        {
            string json = "{"
                + "\"model\":\"" + JsonEscape(input.modelName) + "\","
                + "\"messages\":[" + BuildMessagesJson(input) + "],"
                + "\"temperature\":" + JsonNumber(input.temperature) + ","
                + "\"max_tokens\":" + input.maxTokens;

            string reasoningEffort = ApiEndpointPolicy.NormalizeReasoningEffort(input.reasoningEffort);
            if (ShouldEmitChatReasoningEffort(reasoningEffort))
            {
                json += ",\"reasoning_effort\":\"" + JsonEscape(reasoningEffort) + "\"";
            }

            return json + "}";
        }

        private static string BuildOpenAIResponsesRequestJson(LlmRequestJsonInput input)
        {
            string json = "{"
                + "\"model\":\"" + JsonEscape(input.modelName) + "\","
                + "\"input\":\"" + JsonEscape(input.rawText) + "\","
                + "\"temperature\":" + JsonNumber(input.temperature) + ","
                + "\"max_output_tokens\":" + MaxOutputTokensForRequest(input);

            if (!string.IsNullOrWhiteSpace(input.systemPrompt))
            {
                json += ",\"instructions\":\"" + JsonEscape(input.systemPrompt.Trim()) + "\"";
            }

            string reasoningEffort = ApiEndpointPolicy.NormalizeReasoningEffort(input.reasoningEffort);
            if (HasExplicitReasoningEffort(reasoningEffort))
            {
                json += ",\"reasoning\":{\"effort\":\"" + JsonEscape(reasoningEffort) + "\"}";
            }

            return json + "}";
        }

        /// <summary>
        /// Constructs the JSON array of message objects for Chat Completions mode.
        /// </summary>
        private static string BuildMessagesJson(LlmRequestJsonInput input)
        {
            string userMessage = "{\"role\":\"user\",\"content\":\"" + JsonEscape(input.rawText) + "\"}";
            if (string.IsNullOrWhiteSpace(input.systemPrompt))
            {
                return userMessage;
            }

            return "{\"role\":\"system\",\"content\":\"" + JsonEscape(input.systemPrompt.Trim()) + "\"},"
                + userMessage;
        }

        private static string JsonNumber(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool HasExplicitReasoningEffort(string effort)
        {
            return !string.Equals(ApiEndpointPolicy.NormalizeReasoningEffort(effort), ApiEndpointPolicy.DefaultReasoningEffort, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether to emit a <c>reasoning_effort</c> field on a Chat Completions request body.
        /// Unlike OpenAI Responses (where "none" is a real wire value the server honors),
        /// Chat Completions has no "off" token: sending <c>reasoning_effort:"none"</c> makes
        /// OpenAI-compatible gateways try to apply a thinking budget, which non-reasoning models
        /// reject -- e.g. Google's endpoint returns HTTP 400 "Thinking budget is not supported
        /// for this model." for Gemma. "None" here means "reasoning off", expressed by omitting
        /// the field, the same as "default". Other explicit efforts (minimal/low/medium/high/...)
        /// are still sent so reasoning-capable models on the same lane keep working.
        /// </summary>
        private static bool ShouldEmitChatReasoningEffort(string normalizedEffort)
        {
            if (!HasExplicitReasoningEffort(normalizedEffort))
            {
                return false;
            }

            return !string.Equals(normalizedEffort, "none", StringComparison.Ordinal);
        }

        private static int MaxOutputTokensForRequest(LlmRequestJsonInput input)
        {
            string reasoningEffort = ApiEndpointPolicy.NormalizeReasoningEffort(input.reasoningEffort);
            if (ApiEndpointPolicy.NormalizeApiMode(input.apiMode) == ApiCompatibilityMode.OpenAIResponses
                && HasExplicitReasoningEffort(reasoningEffort)
                && !string.Equals(reasoningEffort, "none", StringComparison.Ordinal))
            {
                // Responses counts hidden reasoning tokens against max_output_tokens. Keep the
                // saved diary text locally capped to maxTokens, but give reasoning models enough
                // room to think and still produce visible text.
                return Math.Max(input.maxTokens + 128, input.maxTokens * 3);
            }

            return input.maxTokens;
        }

        /// <summary>
        /// Minimal JSON string escaper for prompt text and settings values.
        /// </summary>
        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
