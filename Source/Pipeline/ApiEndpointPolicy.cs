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
    internal static class ApiEndpointPolicy
    {
        private const int BaseCooldownSeconds = 10;
        private const int MaxCooldownSeconds = 300;
        // Ceiling for an honored server Retry-After. Higher than the local backoff max so daily-quota
        // waits are respected, but bounded so a garbage header cannot pin a lane for hours.
        private const int MaxRetryAfterSeconds = 3600;
        public const string DefaultEndpointUrl = "http://localhost:1234/v1";
        public const string DefaultReasoningEffort = "default";
        public const string DefaultCustomHeaderName = "x-goog-api-key";
        public const string LegacyApiKeyHeaderName = "api-key";
        public const string LegacyXApiKeyHeaderName = "x-api-key";

        // Sentinel meaning "use the built-in broad reasoning-tag detection" rather than one named
        // tag. This is the safe default: most endpoints already strip cleanly via the default
        // guess-list, so a lane does not need a specific tag unless it leaks private thinking.
        public const string DefaultReasoningTag = "auto";

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

        // The tag names a player may pin a lane to. "auto" is listed so the dropdown can offer the
        // built-in detection; the rest are the common wrappers reasoning models emit (DeepSeek/Qwen
        // <think>, Anthropic-style <thinking>, generic <reasoning>/<analysis>, plus exotic ones like
        // <reflection> / <scratchpad> for RP-tuned models). The parser treats any non-auto choice as
        // an ADDITIONAL tag on top of the base guess-list, never a replacement.
        private static readonly HashSet<string> KnownReasoningTags = new HashSet<string>
        {
            DefaultReasoningTag,
            "think",
            "thinking",
            "reasoning",
            "analysis",
            "thought",
            "reflection",
            "scratchpad"
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

        /// <summary>
        /// Keeps the saved reasoning-tag value to the known set, falling back to "auto" (built-in
        /// broad detection) when the saved value is blank or unrecognized. Hand-edited settings
        /// cannot therefore break parsing by inventing a tag the UI never offered.
        /// </summary>
        public static string NormalizeReasoningTag(string tag)
        {
            string normalized = (tag ?? DefaultReasoningTag).Trim().ToLowerInvariant();
            return KnownReasoningTags.Contains(normalized) ? normalized : DefaultReasoningTag;
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

        /// <summary>
        /// Combines the local exponential backoff with a server-supplied <c>Retry-After</c> (from a
        /// 429/503): the lane cools for whichever is LONGER, so a rate-limited endpoint is never
        /// re-hit before the server permits, while a server that under-asks still gets the local
        /// minimum. A non-positive <paramref name="retryAfterSeconds"/> means no header was sent; the
        /// honored value is capped so a garbage header cannot pin a lane indefinitely.
        /// </summary>
        public static int EffectiveCooldownSeconds(int failureCount, int retryAfterSeconds)
        {
            int backoff = CooldownSecondsForFailures(failureCount);
            if (retryAfterSeconds <= 0)
            {
                return backoff;
            }

            int honored = retryAfterSeconds > MaxRetryAfterSeconds ? MaxRetryAfterSeconds : retryAfterSeconds;
            return honored > backoff ? honored : backoff;
        }
    }
}
