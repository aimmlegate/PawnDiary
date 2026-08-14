// Pure provider-native request JSON serialization.
//
// OpenAI modes delegate to the established serializer so their output remains byte-for-byte
// unchanged. Native modes use small hand-written shapes because RimWorld's Mono runtime does not
// ship System.Text.Json or another supported serializer.
using System;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Builds one generation request body for the selected wire protocol.</summary>
    internal static class LlmProtocolRequestJson
    {
        // A defensive wire cap, not a tuning choice. It prevents a corrupt save or integration input
        // from emitting an absurd integer while staying above every provider's practical output cap.
        private const int MaximumWireTokens = 1024 * 1024;
        private const float MaximumOllamaTemperature = 2f;

        /// <summary>Returns compact JSON for one provider generation request.</summary>
        public static string Build(LlmProtocolRequestInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            switch (LlmProtocolDispatcher.NormalizeMode(input.Mode))
            {
                case LlmProtocolMode.OpenAIResponses:
                    return BuildOpenAI(input, ApiCompatibilityMode.OpenAIResponses);
                case LlmProtocolMode.AnthropicMessages:
                    return BuildAnthropic(input);
                case LlmProtocolMode.GeminiGenerateContent:
                    return BuildGemini(input);
                case LlmProtocolMode.OllamaChat:
                    return BuildOllama(input);
                default:
                    return BuildOpenAI(input, ApiCompatibilityMode.OpenAIChatCompletions);
            }
        }

        private static string BuildOpenAI(LlmProtocolRequestInput input, ApiCompatibilityMode mode)
        {
            return LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = mode,
                modelName = input.ModelName,
                systemPrompt = input.SystemPrompt,
                rawText = input.UserText,
                reasoningEffort = input.ReasoningEffort,
                maxTokens = input.MaxTokens,
                temperature = input.Temperature
            });
        }

        private static string BuildAnthropic(LlmProtocolRequestInput input)
        {
            string json = "{"
                + "\"model\":\"" + JsonEscape(input.ModelName) + "\","
                + "\"max_tokens\":" + SafeTokenLimit(input);

            if (!string.IsNullOrWhiteSpace(input.SystemPrompt))
            {
                json += ",\"system\":\"" + JsonEscape(input.SystemPrompt.Trim()) + "\"";
            }

            // Native thinking is intentionally not enabled. max_tokens therefore remains Pawn
            // Diary's visible-output budget, and response parsing accepts text blocks only.
            return json
                + ",\"messages\":[{\"role\":\"user\",\"content\":\""
                + JsonEscape(input.UserText)
                + "\"}]}";
        }

        private static string BuildGemini(LlmProtocolRequestInput input)
        {
            string json = "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\""
                + JsonEscape(input.UserText)
                + "\"}]}]";

            if (!string.IsNullOrWhiteSpace(input.SystemPrompt))
            {
                json += ",\"systemInstruction\":{\"parts\":[{\"text\":\""
                    + JsonEscape(input.SystemPrompt.Trim())
                    + "\"}]}";
            }

            json += ",\"generationConfig\":{\"maxOutputTokens\":" + SafeTokenLimit(input);
            if (TryGeminiTemperature(input, out float temperature))
            {
                json += ",\"temperature\":" + JsonNumber(temperature);
            }

            return json + "}}";
        }

        private static string BuildOllama(LlmProtocolRequestInput input)
        {
            string messages = BuildOllamaMessages(input);
            return "{"
                + "\"model\":\"" + JsonEscape(input.ModelName) + "\","
                + "\"messages\":[" + messages + "],"
                + "\"stream\":false,"
                + "\"options\":{"
                + "\"temperature\":" + JsonNumber(OllamaTemperature(input.Temperature)) + ","
                + "\"num_predict\":" + SafeTokenLimit(input)
                + "}}";
        }

        private static string BuildOllamaMessages(LlmProtocolRequestInput input)
        {
            string user = "{\"role\":\"user\",\"content\":\""
                + JsonEscape(input.UserText)
                + "\"}";
            if (string.IsNullOrWhiteSpace(input.SystemPrompt))
            {
                return user;
            }

            return "{\"role\":\"system\",\"content\":\""
                + JsonEscape(input.SystemPrompt.Trim())
                + "\"},"
                + user;
        }

        private static int SafeTokenLimit(LlmProtocolRequestInput input)
        {
            int maxTokens = input.MaxTokens;
            if (maxTokens < 1)
            {
                maxTokens = 1;
            }
            if (maxTokens > MaximumWireTokens)
            {
                maxTokens = MaximumWireTokens;
            }

            if (input.ProviderMaximumOutputTokens.HasValue
                && input.ProviderMaximumOutputTokens.Value > 0
                && maxTokens > input.ProviderMaximumOutputTokens.Value)
            {
                maxTokens = input.ProviderMaximumOutputTokens.Value;
            }
            return maxTokens;
        }

        private static bool TryGeminiTemperature(LlmProtocolRequestInput input, out float temperature)
        {
            temperature = 0f;
            if (!input.ProviderMaximumTemperature.HasValue
                || double.IsNaN(input.ProviderMaximumTemperature.Value)
                || double.IsInfinity(input.ProviderMaximumTemperature.Value)
                || input.ProviderMaximumTemperature.Value < 0d
                || float.IsNaN(input.Temperature)
                || float.IsInfinity(input.Temperature))
            {
                return false;
            }

            double maximum = input.ProviderMaximumTemperature.Value;
            double chosen = input.Temperature;
            if (chosen < 0d)
            {
                chosen = 0d;
            }
            if (chosen > maximum)
            {
                chosen = maximum;
            }

            // A JSON float cannot represent maxima beyond Single.MaxValue. Such metadata is not a
            // meaningful sampling range, so omit rather than emitting Infinity.
            if (chosen > float.MaxValue)
            {
                return false;
            }

            temperature = (float)chosen;
            return !float.IsNaN(temperature) && !float.IsInfinity(temperature);
        }

        private static float OllamaTemperature(float temperature)
        {
            if (float.IsNaN(temperature) || float.IsInfinity(temperature))
            {
                return 1f;
            }
            if (temperature < 0f)
            {
                return 0f;
            }
            return temperature > MaximumOllamaTemperature
                ? MaximumOllamaTemperature
                : temperature;
        }

        private static string JsonNumber(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Escapes one JSON string using only APIs available in Unity Mono.</summary>
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
