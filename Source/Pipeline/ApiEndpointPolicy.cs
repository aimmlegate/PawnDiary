// Pure API endpoint policy shared by settings, runtime transport, prompt-lab-adjacent helpers, and
// tests. Keep small routing/auth/backoff constants here so behavior can be validated without
// RimWorld, Verse, Unity, or HTTP calls.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Which request/response shape an API lane speaks. Most providers that advertise
    /// "OpenAI-compatible" should use <see cref="OpenAIChatCompletions"/>; the Responses mode covers
    /// newer OpenAI reasoning models.
    /// </summary>
    public enum ApiCompatibilityMode
    {
        OpenAIChatCompletions,
        OpenAIResponses
    }

    /// <summary>
    /// How one API lane sends its saved API key. Most OpenAI-compatible providers use Bearer, but
    /// some gateways expect a named header or query parameter. The request body stays unchanged.
    /// </summary>
    public enum ApiAuthMode
    {
        BearerToken,
        None,
        // Legacy saves may still contain these fixed header modes. The settings UI now exposes
        // CustomHeader instead, and load-time normalization maps the old names to that mode after
        // seeding the matching header name on the endpoint row.
        ApiKeyHeader,
        XApiKeyHeader,
        QueryParameterKey,
        CustomHeader
    }

    /// <summary>Pure policy helpers for API lane auth and transient-failure cooldown.</summary>
    public static class ApiEndpointPolicy
    {
        private const int BaseCooldownSeconds = 10;
        private const int MaxCooldownSeconds = 300;
        public const string DefaultEndpointUrl = "http://localhost:1234/v1";
        public const string DefaultReasoningEffort = "default";
        public const string DefaultCustomHeaderName = "x-goog-api-key";
        public const string LegacyApiKeyHeaderName = "api-key";
        public const string LegacyXApiKeyHeaderName = "x-api-key";

        private static readonly HashSet<string> ValidReasoningEfforts = new HashSet<string>
        {
            DefaultReasoningEffort,
            "none",
            "minimal",
            "low",
            "medium",
            "high",
            "xhigh"
        };

        /// <summary>Normalizes invalid compatibility enum values loaded from hand-edited settings.</summary>
        public static ApiCompatibilityMode NormalizeApiMode(ApiCompatibilityMode mode)
        {
            return mode == ApiCompatibilityMode.OpenAIResponses
                ? ApiCompatibilityMode.OpenAIResponses
                : ApiCompatibilityMode.OpenAIChatCompletions;
        }

        /// <summary>Normalizes invalid auth enum values loaded from hand-edited settings.</summary>
        public static ApiAuthMode NormalizeAuthMode(ApiAuthMode mode)
        {
            switch (mode)
            {
                case ApiAuthMode.BearerToken:
                case ApiAuthMode.None:
                case ApiAuthMode.QueryParameterKey:
                case ApiAuthMode.CustomHeader:
                    return mode;
                case ApiAuthMode.ApiKeyHeader:
                case ApiAuthMode.XApiKeyHeader:
                    return ApiAuthMode.CustomHeader;
                default:
                    return ApiAuthMode.BearerToken;
            }
        }

        /// <summary>
        /// Returns the API key value that actually affects a lane's request identity. "No auth"
        /// lanes deliberately ignore any stale saved key text because that key is not sent.
        /// </summary>
        public static string EffectiveApiKey(ApiAuthMode authMode, string apiKey)
        {
            if (NormalizeAuthMode(authMode) == ApiAuthMode.None)
            {
                return string.Empty;
            }

            return (apiKey ?? string.Empty).Trim();
        }

        /// <summary>
        /// Returns the header name used by CustomHeader auth, or an empty string for non-header auth.
        /// Invalid custom names fall back to x-goog-api-key so hand-edited settings cannot break sends.
        /// </summary>
        public static string EffectiveAuthHeaderName(ApiAuthMode authMode, string customHeaderName)
        {
            switch (authMode)
            {
                case ApiAuthMode.ApiKeyHeader:
                    return LegacyApiKeyHeaderName;
                case ApiAuthMode.XApiKeyHeader:
                    return LegacyXApiKeyHeaderName;
            }

            return NormalizeAuthMode(authMode) == ApiAuthMode.CustomHeader
                ? NormalizeCustomHeaderName(customHeaderName)
                : string.Empty;
        }

        /// <summary>Normalizes a user-entered HTTP header field name.</summary>
        public static string NormalizeCustomHeaderName(string headerName)
        {
            string trimmed = (headerName ?? string.Empty).Trim();
            return IsValidHeaderName(trimmed) ? trimmed : DefaultCustomHeaderName;
        }

        /// <summary>
        /// Keeps the saved reasoning value to the small set understood by OpenAI Responses.
        /// Unknown values fall back to "default", which sends no reasoning object at all.
        /// </summary>
        public static string NormalizeReasoningEffort(string effort)
        {
            string normalized = (effort ?? DefaultReasoningEffort).Trim().ToLowerInvariant();
            return ValidReasoningEfforts.Contains(normalized) ? normalized : DefaultReasoningEffort;
        }

        private static bool IsValidHeaderName(string headerName)
        {
            if (string.IsNullOrEmpty(headerName))
            {
                return false;
            }

            for (int i = 0; i < headerName.Length; i++)
            {
                char c = headerName[i];
                bool valid = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '!'
                    || c == '#'
                    || c == '$'
                    || c == '%'
                    || c == '&'
                    || c == '\''
                    || c == '*'
                    || c == '+'
                    || c == '-'
                    || c == '.'
                    || c == '^'
                    || c == '_'
                    || c == '`'
                    || c == '|'
                    || c == '~';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Exponential transient-failure backoff used by LlmClient. Failure counts below one behave
        /// like the first failure so corrupted state cannot create a zero-second cooldown.
        /// </summary>
        public static int CooldownSecondsForFailures(int failureCount)
        {
            int safeCount = failureCount < 1 ? 1 : failureCount;
            int seconds = BaseCooldownSeconds;
            for (int i = 1; i < safeCount && seconds < MaxCooldownSeconds; i++)
            {
                seconds *= 2;
                if (seconds > MaxCooldownSeconds)
                {
                    seconds = MaxCooldownSeconds;
                }
            }

            return seconds;
        }
    }
}
