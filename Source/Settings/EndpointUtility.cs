// URL helpers for OpenAI-compatible endpoints. Kept outside PawnDiaryMod so settings UI code
// stays focused on drawing controls while generation code can reuse the same normalization rules.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Static helpers to normalize endpoint URLs and build the /models and /chat/completions paths
    /// expected by OpenAI-compatible APIs.
    /// </summary>
    public static class EndpointUtility
    {
        /// <summary>
        /// Strips trailing slashes and any /chat/completions suffix so the endpoint can be used as a
        /// clean base for path construction. Falls back to the default endpoint when input is empty.
        /// </summary>
        public static string NormalizeBaseEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return PawnDiarySettings.DefaultEndpointUrl;
            }

            string normalized = endpoint.Trim().TrimEnd('/');

            if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - "/chat/completions".Length);
            }

            return normalized;
        }

        /// <summary>Builds the full /models URL for model discovery.</summary>
        public static string BuildModelsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/models";
        }

        /// <summary>Builds the full /chat/completions URL for LLM requests.</summary>
        public static string BuildChatCompletionsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/chat/completions";
        }
    }
}
