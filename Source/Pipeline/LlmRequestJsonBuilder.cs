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
            if (HasExplicitReasoningEffort(reasoningEffort))
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
