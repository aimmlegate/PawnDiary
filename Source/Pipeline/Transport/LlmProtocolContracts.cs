// Plain contracts for provider-native LLM wire protocols.
//
// These types deliberately contain no HTTP, settings, Verse, or Unity objects. The background
// transport can take a snapshot of one configured lane, call the pure planners/codecs, and then
// apply the returned URL, fixed non-secret headers, JSON, or parsed result to HttpClient.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stable protocol identity used by the pure wire planner. Values mirror the persisted API-mode
    /// ordinals, including the retired ordinal 2 so an old numeric value can never become Anthropic.
    /// </summary>
    internal enum LlmProtocolMode
    {
        OpenAIChatCompletions = 0,
        OpenAIResponses = 1,
        ReservedLegacyNativeOllama = 2,
        AnthropicMessages = 3,
        GeminiGenerateContent = 4,
        OllamaChat = 5
    }

    /// <summary>Whether a parsed failure should be retried by the impure transport.</summary>
    internal enum LlmProtocolFailureDisposition
    {
        None,
        Permanent,
        Transient
    }

    /// <summary>One mandatory, non-secret provider header.</summary>
    internal sealed class LlmProtocolHeader
    {
        public readonly string Name;
        public readonly string Value;

        public LlmProtocolHeader(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Fixed provider headers and auth defaults. <see cref="RequiredHeaders"/> never contains an API
    /// key; the impure <c>ApiRequestAuth</c> adapter remains the only owner of secret attachment.
    /// </summary>
    internal sealed class LlmProtocolHeadersPlan
    {
        public readonly List<LlmProtocolHeader> RequiredHeaders;
        public readonly ApiAuthMode RecommendedAuthMode;
        public readonly string RecommendedCustomHeaderName;
        public readonly bool HasSecretHeaderCollision;
        public readonly string CollisionHeaderName;

        public LlmProtocolHeadersPlan(
            List<LlmProtocolHeader> requiredHeaders,
            ApiAuthMode recommendedAuthMode,
            string recommendedCustomHeaderName,
            bool hasSecretHeaderCollision,
            string collisionHeaderName)
        {
            RequiredHeaders = requiredHeaders ?? new List<LlmProtocolHeader>();
            RecommendedAuthMode = recommendedAuthMode;
            RecommendedCustomHeaderName = recommendedCustomHeaderName ?? string.Empty;
            HasSecretHeaderCollision = hasSecretHeaderCollision;
            CollisionHeaderName = collisionHeaderName ?? string.Empty;
        }
    }

    /// <summary>Primitive snapshot used to build one provider request body.</summary>
    internal sealed class LlmProtocolRequestInput
    {
        public LlmProtocolMode Mode;
        public string ModelName;
        public string SystemPrompt;
        public string UserText;
        public string ReasoningEffort;
        public int MaxTokens;
        public float Temperature;

        /// <summary>
        /// Optional provider-advertised architecture family for a discovered model. Ollama exposes
        /// this independently from its freely editable model alias; other protocols leave it blank.
        /// </summary>
        public string ProviderModelFamily;

        /// <summary>
        /// Optional positive output cap advertised by the selected native-provider model. Missing,
        /// zero, or corrupt values leave <see cref="MaxTokens"/> unchanged; OpenAI modes ignore it.
        /// </summary>
        public int? ProviderMaximumOutputTokens;

        /// <summary>
        /// Fetched provider maximum for the selected model. Gemini temperature is emitted only when
        /// this is a finite, non-negative value; null means the model's safe range is unknown.
        /// </summary>
        public double? ProviderMaximumTemperature;
    }

    /// <summary>Pure result of decoding one generation response body.</summary>
    internal sealed class LlmProtocolParseResult
    {
        public string Text = string.Empty;
        public string ProviderError = string.Empty;
        public string FinishReason = string.Empty;
        public string FinishMessage = string.Empty;
        public bool Truncated;
        public bool Refused;
        public bool ParsedJsonObject;
        public LlmProtocolFailureDisposition FailureDisposition;

        /// <summary>True when a usable text response was decoded without a provider error.</summary>
        public bool Success
        {
            get
            {
                return ParsedJsonObject
                    && string.IsNullOrWhiteSpace(ProviderError)
                    && !string.IsNullOrWhiteSpace(Text);
            }
        }
    }

    /// <summary>Provider-neutral metadata for one model-list row.</summary>
    internal sealed class LlmProtocolModelEntry
    {
        public string Id = string.Empty;
        public int MaxOutputTokens;
        public double? MaxTemperature;
        public ModelReasoningCapability ReasoningCapability;
        public string ProviderFamily = string.Empty;
    }

    /// <summary>
    /// One decoded model-list page. Pagination cursors are bounded and name the provider query
    /// parameter explicitly so the HTTP adapter never has to infer a schema from the token.
    /// </summary>
    internal sealed class LlmProtocolModelPageResult
    {
        public readonly List<LlmProtocolModelEntry> Models = new List<LlmProtocolModelEntry>();
        public string NextPageParameterName = string.Empty;
        public string NextPageCursor = string.Empty;
        public string ProviderError = string.Empty;
        public bool ParsedJsonObject;
        public bool ModelLimitReached;

        public bool HasNextPage
        {
            get
            {
                return !string.IsNullOrEmpty(NextPageParameterName)
                    && !string.IsNullOrEmpty(NextPageCursor);
            }
        }
    }
}
