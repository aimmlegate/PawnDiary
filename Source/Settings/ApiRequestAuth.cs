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
        /// <summary>
        /// Builds and validates the non-secret provider-header plan before an HTTP request is created.
        /// A custom secret header may never replace a mandatory protocol header (or vice versa).
        /// </summary>
        public static LlmProtocolHeadersPlan PrepareProtocolHeaders(
            ApiCompatibilityMode apiMode,
            ApiAuthMode authMode,
            string customHeaderName)
        {
            LlmProtocolHeadersPlan plan = LlmProtocolDispatcher.HeadersFor(
                EndpointUtility.ProtocolModeFor(apiMode),
                authMode,
                customHeaderName);
            if (plan.HasSecretHeaderCollision)
            {
                throw new InvalidOperationException(
                    "The custom authentication header conflicts with the required provider header '"
                    + plan.CollisionHeaderName
                    + "'.");
            }

            return plan;
        }

        /// <summary>
        /// Applies mandatory non-secret provider headers after collision preflight. Secret attachment
        /// remains exclusively in <see cref="ApplyHeaders(HttpRequestMessage,string,ApiAuthMode,string)"/>.
        /// </summary>
        public static void ApplyProtocolHeaders(
            HttpRequestMessage request,
            LlmProtocolHeadersPlan plan)
        {
            if (request == null || plan?.RequiredHeaders == null)
            {
                return;
            }

            for (int i = 0; i < plan.RequiredHeaders.Count; i++)
            {
                LlmProtocolHeader header = plan.RequiredHeaders[i];
                if (header == null
                    || string.IsNullOrWhiteSpace(header.Name)
                    || string.IsNullOrWhiteSpace(header.Value))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

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
            try
            {
                parameterName = Uri.UnescapeDataString(parameterName);
            }
            catch
            {
                // A malformed user-entered query stays recoverable. It simply cannot match the
                // canonical key name and will remain beside the newly attached safe parameter.
            }

            return string.Equals(parameterName, name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
