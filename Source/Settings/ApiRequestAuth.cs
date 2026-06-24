// Shared authentication helpers for compatible LLM HTTP requests. Kept separate from the generation
// client so settings-time model fetching and runtime diary generation send API keys the same way.
using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PawnDiary
{
    /// <summary>
    /// Applies one lane's configured API-key style to request headers or the URL.
    /// </summary>
    public static class ApiRequestAuth
    {
        /// <summary>Adds query-parameter auth when the selected auth mode requires it.</summary>
        public static string ApplyQueryAuth(string url, string apiKey, ApiAuthMode authMode)
        {
            string key = (apiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url)
                || string.IsNullOrEmpty(key)
                || ApiEndpointPolicy.NormalizeAuthMode(authMode) != ApiAuthMode.QueryParameterKey)
            {
                return url;
            }

            string separator = url.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
            return url + separator + "key=" + Uri.EscapeDataString(key);
        }

        /// <summary>Adds header-based auth when the selected auth mode requires it.</summary>
        public static void ApplyHeaders(HttpRequestMessage request, string apiKey, ApiAuthMode authMode)
        {
            if (request == null)
            {
                return;
            }

            string key = (apiKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            switch (ApiEndpointPolicy.NormalizeAuthMode(authMode))
            {
                case ApiAuthMode.None:
                case ApiAuthMode.QueryParameterKey:
                    return;
                case ApiAuthMode.ApiKeyHeader:
                    request.Headers.TryAddWithoutValidation("api-key", key);
                    return;
                case ApiAuthMode.XApiKeyHeader:
                    request.Headers.TryAddWithoutValidation("x-api-key", key);
                    return;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    return;
            }
        }
    }
}
