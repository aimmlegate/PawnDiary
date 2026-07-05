// Shared authentication helpers for compatible LLM HTTP requests. Kept separate from the generation
// client so settings-time model fetching and runtime diary generation send API keys the same way.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PawnDiary
{
    /// <summary>
    /// Applies one lane's configured API-key style to request headers or the URL.
    /// </summary>
    internal static class ApiRequestAuth
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

            return AddOrReplaceQueryParameter(url, "key", Uri.EscapeDataString(key));
        }

        /// <summary>Adds header-based auth when the selected auth mode requires it.</summary>
        public static void ApplyHeaders(HttpRequestMessage request, string apiKey, ApiAuthMode authMode)
        {
            ApplyHeaders(request, apiKey, authMode, ApiEndpointPolicy.DefaultCustomHeaderName);
        }

        /// <summary>Adds header-based auth when the selected auth mode requires it.</summary>
        public static void ApplyHeaders(HttpRequestMessage request, string apiKey, ApiAuthMode authMode, string customHeaderName)
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
                case ApiAuthMode.CustomHeader:
                    request.Headers.TryAddWithoutValidation(
                        ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customHeaderName),
                        key);
                    return;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    return;
            }
        }

        private static string AddOrReplaceQueryParameter(string url, string name, string escapedValue)
        {
            int fragmentIndex = url.IndexOf("#", StringComparison.Ordinal);
            string fragment = string.Empty;
            string withoutFragment = url;
            if (fragmentIndex >= 0)
            {
                fragment = url.Substring(fragmentIndex);
                withoutFragment = url.Substring(0, fragmentIndex);
            }

            int queryIndex = withoutFragment.IndexOf("?", StringComparison.Ordinal);
            string path = queryIndex >= 0 ? withoutFragment.Substring(0, queryIndex) : withoutFragment;
            string query = queryIndex >= 0 ? withoutFragment.Substring(queryIndex + 1) : string.Empty;

            List<string> parameters = new List<string>();
            if (!string.IsNullOrEmpty(query))
            {
                string[] existing = query.Split('&');
                for (int i = 0; i < existing.Length; i++)
                {
                    string parameter = existing[i];
                    if (string.IsNullOrEmpty(parameter) || IsQueryParameter(parameter, name))
                    {
                        continue;
                    }

                    parameters.Add(parameter);
                }
            }

            parameters.Add(name + "=" + escapedValue);
            return path + "?" + string.Join("&", parameters.ToArray()) + fragment;
        }

        private static bool IsQueryParameter(string parameter, string name)
        {
            int equalsIndex = parameter.IndexOf("=", StringComparison.Ordinal);
            string parameterName = equalsIndex >= 0 ? parameter.Substring(0, equalsIndex) : parameter;
            return string.Equals(parameterName, name, StringComparison.Ordinal);
        }
    }
}
