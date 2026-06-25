// Pure API lane identity and log-label helpers. Runtime code passes plain endpoint fields into this
// file so gate keys, cooldown keys, failover dedupe, and settings-row stale-result checks stay in one
// audited place without depending on RimWorld, Verse, Unity, HTTP, or saved settings objects.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Canonical identity key for one API lane under a specific comparison mode. Different call sites
    /// intentionally compare different fields: generation gates include effective auth, model-list
    /// fetches use exact raw row values, and UI connection tests also include reasoning effort.
    /// </summary>
    public struct ApiLaneIdentity : IEquatable<ApiLaneIdentity>
    {
        private readonly string key;

        private ApiLaneIdentity(string key)
        {
            this.key = key ?? string.Empty;
        }

        /// <summary>True for a default identity created because there was no request or row to key.</summary>
        public bool Empty
        {
            get { return string.IsNullOrEmpty(key); }
        }

        /// <summary>
        /// Identity for concurrency gates and transient-failure cooldowns. This preserves the old
        /// behavior: normalized endpoint, trimmed model, raw compatibility mode, effective auth style,
        /// and effective key; reasoning effort does not split a lane.
        /// </summary>
        public static ApiLaneIdentity ForGate(string endpointUrl, string modelName, ApiCompatibilityMode apiMode,
            ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "gate",
                NormalizedBaseEndpointKey(endpointUrl),
                Trimmed(modelName),
                apiMode.ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>
        /// Identity for removing duplicate failover attempts. Model text stays raw here because the
        /// previous comparison did not trim it.
        /// </summary>
        public static ApiLaneIdentity ForAttempt(string endpointUrl, string modelName, ApiCompatibilityMode apiMode,
            ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "attempt",
                NormalizedBaseEndpointKey(endpointUrl),
                Raw(modelName),
                apiMode.ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>Identity for endpoint+model generation-lane pinning when no auth data was saved.</summary>
        public static ApiLaneIdentity ForGeneration(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            return new ApiLaneIdentity(Join(
                "generation",
                NormalizedGenerationUrlKey(endpointUrl, apiMode),
                Raw(modelName),
                apiMode.ToString()));
        }

        /// <summary>Identity for generation-lane pinning when auth must also match.</summary>
        public static ApiLaneIdentity ForGenerationWithAuth(string endpointUrl, string modelName,
            ApiCompatibilityMode apiMode, ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "generation-auth",
                NormalizedGenerationUrlKey(endpointUrl, apiMode),
                Raw(modelName),
                apiMode.ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>
        /// Exact row identity for model-list fetch results. This deliberately uses raw URL and key
        /// text because the old stale-result check invalidated a fetch after any row edit.
        /// </summary>
        public static ApiLaneIdentity ForFetchTarget(string endpointUrl, string apiKey, ApiAuthMode authMode,
            string customAuthHeaderName, ApiCompatibilityMode apiMode)
        {
            return new ApiLaneIdentity(Join(
                "fetch",
                Raw(endpointUrl),
                Raw(apiKey),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                apiMode.ToString()));
        }

        /// <summary>
        /// Exact row identity for connection-test results. This includes model and reasoning effort,
        /// and normalizes fields the test runner normalized when it captured its request snapshot.
        /// </summary>
        public static ApiLaneIdentity ForConnectionTest(string endpointUrl, string apiKey, string modelName,
            ApiAuthMode authMode, string customAuthHeaderName, ApiCompatibilityMode apiMode, string reasoningEffort)
        {
            return new ApiLaneIdentity(Join(
                "connection-test",
                Raw(endpointUrl),
                Raw(apiKey),
                Raw(modelName),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString(),
                ApiEndpointPolicy.NormalizeReasoningEffort(reasoningEffort)));
        }

        public bool Equals(ApiLaneIdentity other)
        {
            return string.Equals(key ?? string.Empty, other.key ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ApiLaneIdentity && Equals((ApiLaneIdentity)obj);
        }

        public override int GetHashCode()
        {
            return (key ?? string.Empty).GetHashCode();
        }

        public override string ToString()
        {
            return key ?? string.Empty;
        }

        public static bool operator ==(ApiLaneIdentity left, ApiLaneIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ApiLaneIdentity left, ApiLaneIdentity right)
        {
            return !left.Equals(right);
        }

        private static string NormalizedBaseEndpointKey(string endpointUrl)
        {
            return EndpointUtility.NormalizeBaseEndpoint(endpointUrl ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizedGenerationUrlKey(string endpointUrl, ApiCompatibilityMode apiMode)
        {
            return EndpointUtility.BuildGenerationUrl(endpointUrl ?? string.Empty, apiMode).Trim().ToLowerInvariant();
        }

        private static string Raw(string value)
        {
            return value ?? string.Empty;
        }

        private static string Trimmed(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string Join(params string[] parts)
        {
            return string.Join("\n", parts ?? new string[0]);
        }
    }

    /// <summary>
    /// Sanitized API lane labels for debug logs and settings connection-test logs. These labels are
    /// intentionally English diagnostics and never include API keys or URL query/fragment text.
    /// </summary>
    public static class ApiLaneLabels
    {
        /// <summary>Formats one endpoint/model/mode tuple for logs without leaking query-string keys.</summary>
        public static string Label(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            string model = string.IsNullOrWhiteSpace(modelName) ? "<blank-model>" : modelName;
            string endpoint = string.IsNullOrWhiteSpace(endpointUrl)
                ? "<blank-url>"
                : EndpointUtility.BuildGenerationUrl(SanitizeEndpointUrlForLog(endpointUrl), apiMode);
            return model + " [" + apiMode + "] @ " + endpoint;
        }

        /// <summary>Trims one-line log details to the shared diagnostic length cap.</summary>
        public static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = OneLine(value);
            return value.Length <= 180 ? value : value.Substring(0, 180) + "...";
        }

        private static string SanitizeEndpointUrlForLog(string endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return string.Empty;
            }

            int query = endpointUrl.IndexOf('?');
            int fragment = endpointUrl.IndexOf('#');
            int cut = -1;
            if (query >= 0)
            {
                cut = query;
            }

            if (fragment >= 0 && (cut < 0 || fragment < cut))
            {
                cut = fragment;
            }

            return cut >= 0 ? endpointUrl.Substring(0, cut) : endpointUrl;
        }

        /// <summary>Collapses whitespace/newlines/tabs to a single trimmed line. Shared so log/status
        /// trimmers (e.g. ApiConnectionController.TrimForStatus) don't each re-implement it.</summary>
        internal static string OneLine(string value)
        {
            return (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }
    }
}
