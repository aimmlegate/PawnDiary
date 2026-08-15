// Pure provider-preset policy for the settings UI. XML Defs are copied into these plain snapshots
// before this code sees them, so catalog validation and lane creation stay testable without Verse,
// Unity, HTTP, or mutable RimWorld settings objects.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>How the global temperature setting reaches one provider protocol.</summary>
    internal enum ApiTemperatureBehavior
    {
        Sent,
        Omitted,
        ConditionalModelCapability
    }

    /// <summary>Detached XML-owned defaults for one entry in the Add API preset menu.</summary>
    internal sealed class ApiProviderPresetSnapshot
    {
        public string presetKey = string.Empty;
        public int displayOrder;
        public ApiCompatibilityMode apiMode;
        public string baseUrl = string.Empty;
        public ApiAuthMode authMode;
        public string customAuthHeaderName = string.Empty;
    }

    /// <summary>Plain initial values used to create a new API lane from a selected preset.</summary>
    internal sealed class ApiProviderLaneDefaults
    {
        public string url = string.Empty;
        public string model = string.Empty;
        public string apiKey = string.Empty;
        public ApiAuthMode authMode;
        public string customAuthHeaderName = string.Empty;
        public bool enabled;
        public ApiCompatibilityMode apiMode;
    }

    /// <summary>Provider-specific visibility and sampling behavior consumed by the lane editor.</summary>
    internal sealed class ApiProviderModeUiSnapshot
    {
        public readonly bool showOpenAiReasoningControls;
        public readonly ApiTemperatureBehavior temperatureBehavior;

        public ApiProviderModeUiSnapshot(
            bool showOpenAiReasoningControls,
            ApiTemperatureBehavior temperatureBehavior)
        {
            this.showOpenAiReasoningControls = showOpenAiReasoningControls;
            this.temperatureBehavior = temperatureBehavior;
        }
    }

    /// <summary>
    /// Validates the five shipped provider presets and converts one preset into safe lane defaults.
    /// Stable preset identities and provider/auth pairings are code contracts; editable URLs and menu
    /// order come from XML when valid, with complete built-in rows as a missing/corrupt-Def fallback.
    /// </summary>
    internal static class ApiProviderPresetPolicy
    {
        public const string CustomOpenAiPresetKey = "PawnDiary_ApiPreset_CustomOpenAi";
        public const string OpenAiResponsesPresetKey = "PawnDiary_ApiPreset_OpenAiResponses";
        public const string AnthropicPresetKey = "PawnDiary_ApiPreset_Anthropic";
        public const string GeminiPresetKey = "PawnDiary_ApiPreset_Gemini";
        public const string OllamaPresetKey = "PawnDiary_ApiPreset_Ollama";

        private const string OpenAiEndpoint = "https://api.openai.com/v1";

        private static readonly ApiProviderModeUiSnapshot OpenAiModeUi =
            new ApiProviderModeUiSnapshot(true, ApiTemperatureBehavior.Sent);
        private static readonly ApiProviderModeUiSnapshot AnthropicModeUi =
            new ApiProviderModeUiSnapshot(false, ApiTemperatureBehavior.Omitted);
        private static readonly ApiProviderModeUiSnapshot GeminiModeUi =
            new ApiProviderModeUiSnapshot(false, ApiTemperatureBehavior.ConditionalModelCapability);
        private static readonly ApiProviderModeUiSnapshot OllamaModeUi =
            new ApiProviderModeUiSnapshot(false, ApiTemperatureBehavior.Sent);

        private static readonly ApiProviderPresetSnapshot[] FallbackCatalog =
        {
            Preset(CustomOpenAiPresetKey, 10, ApiCompatibilityMode.OpenAIChatCompletions,
                ApiEndpointPolicy.DefaultEndpointUrl, ApiAuthMode.BearerToken, string.Empty),
            Preset(OpenAiResponsesPresetKey, 20, ApiCompatibilityMode.OpenAIResponses,
                OpenAiEndpoint, ApiAuthMode.BearerToken, string.Empty),
            Preset(AnthropicPresetKey, 30, ApiCompatibilityMode.AnthropicMessages,
                LlmProtocolDispatcher.DefaultAnthropicEndpoint, ApiAuthMode.CustomHeader,
                LlmProtocolDispatcher.AnthropicApiKeyHeaderName),
            Preset(GeminiPresetKey, 40, ApiCompatibilityMode.GeminiGenerateContent,
                LlmProtocolDispatcher.DefaultGeminiEndpoint, ApiAuthMode.CustomHeader,
                LlmProtocolDispatcher.GeminiApiKeyHeaderName),
            Preset(OllamaPresetKey, 50, ApiCompatibilityMode.OllamaChat,
                LlmProtocolDispatcher.DefaultOllamaEndpoint, ApiAuthMode.None, string.Empty)
        };

        /// <summary>
        /// Returns exactly one validated row for each shipped preset. Missing, duplicate, or malformed
        /// candidates use that identity's fallback; final order is numeric then canonical identity, so
        /// hand-edited duplicate order values remain deterministic.
        /// </summary>
        public static List<ApiProviderPresetSnapshot> BuildCatalog(
            IEnumerable<ApiProviderPresetSnapshot> candidates)
        {
            Dictionary<string, ApiProviderPresetSnapshot> accepted =
                new Dictionary<string, ApiProviderPresetSnapshot>(StringComparer.Ordinal);

            if (candidates != null)
            {
                foreach (ApiProviderPresetSnapshot candidate in candidates)
                {
                    string key = (candidate?.presetKey ?? string.Empty).Trim();
                    ApiProviderPresetSnapshot fallback = FallbackFor(key);
                    if (fallback != null
                        && !accepted.ContainsKey(key)
                        && IsValidCandidate(candidate, fallback))
                    {
                        accepted[key] = Copy(candidate);
                    }
                }
            }

            List<ApiProviderPresetSnapshot> result = new List<ApiProviderPresetSnapshot>();
            for (int i = 0; i < FallbackCatalog.Length; i++)
            {
                ApiProviderPresetSnapshot fallback = FallbackCatalog[i];
                result.Add(accepted.TryGetValue(fallback.presetKey, out ApiProviderPresetSnapshot row)
                    ? row
                    : Copy(fallback));
            }

            result.Sort(CompareCatalogRows);
            return result;
        }

        /// <summary>
        /// Creates a fresh enabled lane. Models and credentials are intentionally volatile and therefore
        /// always blank, even when a malformed caller supplied text elsewhere before selecting a preset.
        /// </summary>
        public static ApiProviderLaneDefaults CreateLane(ApiProviderPresetSnapshot preset)
        {
            ApiProviderPresetSnapshot fallback = FallbackFor(preset?.presetKey);
            ApiProviderPresetSnapshot effective = fallback != null
                && IsValidCandidate(preset, fallback)
                    ? preset
                    : fallback ?? FallbackCatalog[0];

            return new ApiProviderLaneDefaults
            {
                url = (effective.baseUrl ?? string.Empty).Trim(),
                model = string.Empty,
                apiKey = string.Empty,
                authMode = effective.authMode,
                customAuthHeaderName = effective.authMode == ApiAuthMode.CustomHeader
                    ? (effective.customAuthHeaderName ?? string.Empty).Trim()
                    : string.Empty,
                enabled = true,
                apiMode = effective.apiMode
            };
        }

        /// <summary>Returns visibility and temperature semantics for a normalized protocol mode.</summary>
        public static ApiProviderModeUiSnapshot ModeUi(ApiCompatibilityMode mode)
        {
            switch (ApiEndpointPolicy.NormalizeApiMode(mode))
            {
                case ApiCompatibilityMode.AnthropicMessages:
                    return AnthropicModeUi;
                case ApiCompatibilityMode.GeminiGenerateContent:
                    return GeminiModeUi;
                case ApiCompatibilityMode.OllamaChat:
                    return OllamaModeUi;
                default:
                    return OpenAiModeUi;
            }
        }

        /// <summary>Maps a live mode to the preset whose localized description explains that mode.</summary>
        public static string PresetKeyForMode(ApiCompatibilityMode mode)
        {
            switch (ApiEndpointPolicy.NormalizeApiMode(mode))
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return OpenAiResponsesPresetKey;
                case ApiCompatibilityMode.AnthropicMessages:
                    return AnthropicPresetKey;
                case ApiCompatibilityMode.GeminiGenerateContent:
                    return GeminiPresetKey;
                case ApiCompatibilityMode.OllamaChat:
                    return OllamaPresetKey;
                default:
                    return CustomOpenAiPresetKey;
            }
        }

        private static bool IsValidCandidate(
            ApiProviderPresetSnapshot candidate,
            ApiProviderPresetSnapshot fallback)
        {
            if (candidate == null
                || fallback == null
                || !string.Equals(
                    (candidate.presetKey ?? string.Empty).Trim(),
                    fallback.presetKey,
                    StringComparison.Ordinal)
                || candidate.displayOrder < 0
                || candidate.apiMode != fallback.apiMode
                || candidate.authMode != fallback.authMode
                || !IsHttpBaseUrl(candidate.baseUrl))
            {
                return false;
            }

            if (candidate.authMode != ApiAuthMode.CustomHeader)
            {
                return true;
            }

            string header = (candidate.customAuthHeaderName ?? string.Empty).Trim();
            return header.Length > 0
                && string.Equals(
                    ApiEndpointPolicy.NormalizeCustomHeaderName(header),
                    header,
                    StringComparison.Ordinal)
                && string.Equals(
                    header,
                    fallback.customAuthHeaderName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHttpBaseUrl(string value)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            bool http = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            return http && string.IsNullOrEmpty(uri.UserInfo);
        }

        private static ApiProviderPresetSnapshot FallbackFor(string presetKey)
        {
            string key = (presetKey ?? string.Empty).Trim();
            for (int i = 0; i < FallbackCatalog.Length; i++)
            {
                if (string.Equals(FallbackCatalog[i].presetKey, key, StringComparison.Ordinal))
                {
                    return FallbackCatalog[i];
                }
            }

            return null;
        }

        private static int CompareCatalogRows(
            ApiProviderPresetSnapshot left,
            ApiProviderPresetSnapshot right)
        {
            int byOrder = left.displayOrder.CompareTo(right.displayOrder);
            if (byOrder != 0)
            {
                return byOrder;
            }

            return FallbackRank(left.presetKey).CompareTo(FallbackRank(right.presetKey));
        }

        private static int FallbackRank(string presetKey)
        {
            for (int i = 0; i < FallbackCatalog.Length; i++)
            {
                if (string.Equals(FallbackCatalog[i].presetKey, presetKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private static ApiProviderPresetSnapshot Copy(ApiProviderPresetSnapshot source)
        {
            return Preset(
                source.presetKey,
                source.displayOrder,
                source.apiMode,
                (source.baseUrl ?? string.Empty).Trim(),
                source.authMode,
                (source.customAuthHeaderName ?? string.Empty).Trim());
        }

        private static ApiProviderPresetSnapshot Preset(
            string key,
            int order,
            ApiCompatibilityMode mode,
            string url,
            ApiAuthMode authMode,
            string customHeaderName)
        {
            return new ApiProviderPresetSnapshot
            {
                presetKey = key,
                displayOrder = order,
                apiMode = mode,
                baseUrl = url,
                authMode = authMode,
                customAuthHeaderName = customHeaderName
            };
        }
    }
}
