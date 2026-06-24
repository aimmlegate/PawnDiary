// URL helpers for compatible LLM endpoints. Kept outside PawnDiaryMod so settings UI code
// stays focused on drawing controls while generation code can reuse the same normalization rules.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Static helpers to normalize endpoint URLs and build the paths expected by each supported
    /// compatibility mode.
    /// </summary>
    public static class EndpointUtility
    {
        /// <summary>
        /// Strips trailing slashes and known generation/model suffixes so the endpoint can be used
        /// as a clean base for path construction. Falls back to the default endpoint when input is
        /// empty.
        /// </summary>
        public static string NormalizeBaseEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return PawnDiarySettings.DefaultEndpointUrl;
            }

            string normalized = endpoint.Trim().TrimEnd('/');

            string[] suffixes =
            {
                "/chat/completions",
                "/responses",
                "/models"
            };

            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length);
                    break;
                }
            }

            return normalized;
        }

        /// <summary>Builds the full model-list URL for the selected compatibility mode.</summary>
        public static string BuildModelsUrl(string endpoint, ApiCompatibilityMode mode)
        {
            return NormalizeBaseEndpoint(endpoint) + "/models";
        }

        /// <summary>Builds the full generation URL for the selected compatibility mode.</summary>
        public static string BuildGenerationUrl(string endpoint, ApiCompatibilityMode mode)
        {
            switch (mode)
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return NormalizeBaseEndpoint(endpoint) + "/responses";
                default:
                    return BuildChatCompletionsUrl(endpoint);
            }
        }

        /// <summary>Builds the full /chat/completions URL for LLM requests.</summary>
        public static string BuildChatCompletionsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/chat/completions";
        }
    }
}
