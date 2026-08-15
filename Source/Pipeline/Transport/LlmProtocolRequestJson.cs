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

        // Gemini 2.5 Pro cannot turn thinking off, but the provider documents 128 tokens as its
        // minimum supported budget. Reserve those tokens in addition to Pawn Diary's visible-text
        // budget so small title and connection-test requests can still produce an answer.
        private const int Gemini25ProThinkingTokens = 128;

        // Safe fallback when the main-thread XML tuning snapshot is absent/corrupt. The actual policy
        // value is owned by DiaryTuningDef; this pure serializer never reads Verse/DefDatabase.
        internal const int DefaultLowThinkingHeadroomTokens = 1024;

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

            int thinkingHeadroom = GeminiThinkingHeadroom(input);
            json += ",\"generationConfig\":{\"maxOutputTokens\":"
                + SafeTokenLimit(input, thinkingHeadroom);
            AppendGeminiThinkingConfig(ref json, input.ModelName);
            if (TryGeminiTemperature(input, out float temperature))
            {
                json += ",\"temperature\":" + JsonNumber(temperature);
            }

            return json + "}}";
        }

        private static string BuildOllama(LlmProtocolRequestInput input)
        {
            string messages = BuildOllamaMessages(input);
            bool gptOss = IsGptOssModel(input.ModelName, input.ProviderModelFamily);
            return "{"
                + "\"model\":\"" + JsonEscape(input.ModelName) + "\","
                + "\"messages\":[" + messages + "],"
                + "\"stream\":false,"
                // Ollama enables thinking by default for capable models. Most accept false; GPT-OSS
                // ignores booleans and requires one of its supported string levels instead.
                + "\"think\":" + (gptOss ? "\"low\"" : "false") + ","
                + "\"options\":{"
                + "\"temperature\":" + JsonNumber(OllamaTemperature(input.Temperature)) + ","
                + "\"num_predict\":" + SafeTokenLimit(
                    input,
                    gptOss ? LowThinkingHeadroom(input) : 0)
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
            return SafeTokenLimit(input, 0);
        }

        /// <summary>
        /// Returns the provider's total generation cap after reserving any required hidden-thinking
        /// room. Long arithmetic prevents a corrupt input from overflowing before the defensive and
        /// provider-advertised caps are applied.
        /// </summary>
        private static int SafeTokenLimit(LlmProtocolRequestInput input, int thinkingHeadroom)
        {
            long maxTokens = input.MaxTokens;
            if (maxTokens < 1)
            {
                maxTokens = 1;
            }

            if (thinkingHeadroom > 0)
            {
                maxTokens += (long)thinkingHeadroom;
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
            return (int)maxTokens;
        }

        /// <summary>Returns hidden-token headroom for Gemini models that cannot disable thinking.</summary>
        private static int GeminiThinkingHeadroom(LlmProtocolRequestInput input)
        {
            if (IsGemini25ProThinkingModel(input.ModelName))
            {
                return Gemini25ProThinkingTokens;
            }
            if (IsGemini3MinimalThinkingModel(input.ModelName)
                || IsGemini3LowThinkingModel(input.ModelName))
            {
                return LowThinkingHeadroom(input);
            }
            return 0;
        }

        /// <summary>Normalizes the detached XML policy snapshot under the defensive wire cap.</summary>
        private static int LowThinkingHeadroom(LlmProtocolRequestInput input)
        {
            int value = input.LowThinkingHeadroomTokens;
            if (value <= 0)
            {
                return DefaultLowThinkingHeadroomTokens;
            }

            return value > MaximumWireTokens ? MaximumWireTokens : value;
        }

        /// <summary>Adds only the thinking control supported by the selected Gemini model family.</summary>
        private static void AppendGeminiThinkingConfig(ref string json, string modelName)
        {
            if (IsGeminiRoboticsThinkingModel(modelName))
            {
                json += ",\"thinkingConfig\":{\"thinkingBudget\":0}";
                return;
            }

            if (IsGemini25ProThinkingModel(modelName))
            {
                json += ",\"thinkingConfig\":{\"thinkingBudget\":"
                    + Gemini25ProThinkingTokens
                    + "}";
                return;
            }

            if (IsGemini25FlashThinkingModel(modelName))
            {
                // Flash and Flash-Lite support zero; unlike Pro, no hidden-token headroom is needed.
                json += ",\"thinkingConfig\":{\"thinkingBudget\":0}";
                return;
            }

            if (IsGemini3MinimalThinkingModel(modelName))
            {
                // Current Flash Image variants reject low and accept minimal as their cheapest level.
                json += ",\"thinkingConfig\":{\"thinkingLevel\":\"minimal\"}";
                return;
            }

            if (IsGemini3LowThinkingModel(modelName))
            {
                json += ",\"thinkingConfig\":{\"thinkingLevel\":\"low\"}";
            }
        }

        /// <summary>True for Gemini 2.5 Pro text variants that support an exact thinking budget.</summary>
        private static bool IsGemini25ProThinkingModel(string modelName)
        {
            return IsGeminiModelFamily(modelName, "gemini-2.5-pro")
                && !IsGemini25SpecializedNonThinkingModel(modelName);
        }

        /// <summary>True for Gemini 2.5 Flash/Flash-Lite text variants that can disable thinking.</summary>
        private static bool IsGemini25FlashThinkingModel(string modelName)
        {
            return IsGeminiModelFamily(modelName, "gemini-2.5-flash")
                && !IsGemini25SpecializedNonThinkingModel(modelName);
        }

        /// <summary>
        /// Gemini's image, TTS, native-audio, and Live derivatives share a text-model prefix but do
        /// not share its thinkingBudget contract. Leave those specialized variants untouched.
        /// </summary>
        private static bool IsGemini25SpecializedNonThinkingModel(string modelName)
        {
            string candidate = GeminiModelId(modelName);
            return candidate.IndexOf("-image", StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.IndexOf("-tts", StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.IndexOf("-native-audio", StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.IndexOf("-live", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Robotics-ER 1.6 documents the same zero-budget opt-out as Gemini 2.5 Flash.</summary>
        private static bool IsGeminiRoboticsThinkingModel(string modelName)
        {
            return IsGeminiModelFamily(modelName, "gemini-robotics-er-1.6-preview");
        }

        /// <summary>Current Gemini 3 Flash Image variants accept minimal/high but reject low.</summary>
        private static bool IsGemini3MinimalThinkingModel(string modelName)
        {
            return IsGeminiModelFamily(modelName, "gemini-3.1-flash-image")
                || IsGeminiModelFamily(modelName, "gemini-3.1-flash-lite-image");
        }

        /// <summary>Returns true only when the selected Gemini 3 variant accepts the low level.</summary>
        private static bool IsGemini3LowThinkingModel(string modelName)
        {
            if (!IsGemini3VersionOrLatestAlias(modelName))
            {
                return false;
            }

            string candidate = GeminiModelId(modelName);
            return candidate.IndexOf("-image", StringComparison.OrdinalIgnoreCase) < 0
                && candidate.IndexOf("-tts", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Matches a canonical Gemini family plus its dated/preview variants. The optional models/
        /// prefix is accepted because integrations may submit the provider's raw discovery ID.
        /// </summary>
        private static bool IsGeminiModelFamily(string modelName, string family)
        {
            string candidate = GeminiModelId(modelName);

            if (!candidate.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (candidate.Length == family.Length)
            {
                return true;
            }

            char separator = candidate[family.Length];
            return separator == '-' || separator == '.';
        }

        /// <summary>
        /// Recognizes versioned Gemini 3 IDs and the three documented latest aliases that currently
        /// resolve to Gemini 3 models. Unknown aliases stay untouched because older families reject
        /// thinkingLevel.
        /// </summary>
        private static bool IsGemini3VersionOrLatestAlias(string modelName)
        {
            return IsGeminiModelFamily(modelName, "gemini-3")
                || IsExactGeminiModel(modelName, "gemini-flash-latest")
                || IsExactGeminiModel(modelName, "gemini-flash-lite-latest")
                || IsExactGeminiModel(modelName, "gemini-pro-latest");
        }

        /// <summary>Matches one exact Gemini ID, accepting the provider's optional models/ prefix.</summary>
        private static bool IsExactGeminiModel(string modelName, string expected)
        {
            return string.Equals(GeminiModelId(modelName), expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns a trimmed Gemini ID without the discovery API's optional models/ prefix.</summary>
        private static string GeminiModelId(string modelName)
        {
            string candidate = (modelName ?? string.Empty).Trim();
            const string ModelsPrefix = "models/";
            if (candidate.StartsWith(ModelsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(ModelsPrefix.Length);
            }
            return candidate;
        }

        /// <summary>
        /// Detects Ollama GPT-OSS from its conventional tag or provider-advertised GGUF family, so a
        /// player-created alias still receives the string thinking level that architecture requires.
        /// </summary>
        private static bool IsGptOssModel(string modelName, string providerFamily)
        {
            if ((modelName ?? string.Empty).IndexOf(
                    "gpt-oss",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string family = (providerFamily ?? string.Empty).Trim()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);
            return string.Equals(family, "gptoss", StringComparison.OrdinalIgnoreCase);
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
