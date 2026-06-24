// Pure API endpoint policy shared by settings, runtime transport, prompt-lab-adjacent helpers, and
// tests. Keep small routing/auth/backoff constants here so behavior can be validated without
// RimWorld, Verse, Unity, or HTTP calls.
namespace PawnDiary
{
    /// <summary>
    /// How one API lane sends its saved API key. Most OpenAI-compatible providers use Bearer, but
    /// some gateways expect a named header or query parameter. The request body stays unchanged.
    /// </summary>
    public enum ApiAuthMode
    {
        BearerToken,
        None,
        ApiKeyHeader,
        XApiKeyHeader,
        QueryParameterKey
    }

    /// <summary>Pure policy helpers for API lane auth and transient-failure cooldown.</summary>
    public static class ApiEndpointPolicy
    {
        private const int BaseCooldownSeconds = 10;
        private const int MaxCooldownSeconds = 300;

        /// <summary>Normalizes invalid auth enum values loaded from hand-edited settings.</summary>
        public static ApiAuthMode NormalizeAuthMode(ApiAuthMode mode)
        {
            switch (mode)
            {
                case ApiAuthMode.BearerToken:
                case ApiAuthMode.None:
                case ApiAuthMode.ApiKeyHeader:
                case ApiAuthMode.XApiKeyHeader:
                case ApiAuthMode.QueryParameterKey:
                    return mode;
                default:
                    return ApiAuthMode.BearerToken;
            }
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
