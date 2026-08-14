// URL helpers for compatible LLM endpoints. Kept outside PawnDiaryMod so settings UI code
// stays focused on drawing controls while generation code can reuse the same normalization rules.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Static helpers to normalize endpoint URLs and build the paths expected by each supported
    /// compatibility mode.
    /// </summary>
    internal static class EndpointUtility
    {
        private static readonly string[] KnownEndpointSuffixes =
        {
            "/chat/completions",
            "/responses",
            "/models"
        };

        /// <summary>
        /// Strips trailing slashes and known generation/model suffixes so the endpoint can be used
        /// as a clean base for path construction. Falls back to the default endpoint when input is
        /// empty.
        /// </summary>
        public static string NormalizeBaseEndpoint(string endpoint)
        {
            return RewriteEndpointPath(endpoint, null);
        }

        /// <summary>Builds the full model-list URL for the selected compatibility mode.</summary>
        public static string BuildModelsUrl(string endpoint, ApiCompatibilityMode mode)
        {
            return RewriteEndpointPath(endpoint, "/models");
        }

        /// <summary>
        /// Builds a provider-aware model-list URL, optionally continuing one bounded native-provider
        /// page. The two-argument overload above intentionally remains the exact OpenAI helper used by
        /// the pure dispatcher, so routing back through this overload cannot recurse.
        /// </summary>
        public static string BuildModelsUrl(string endpoint, ApiCompatibilityMode mode, string pageCursor)
        {
            return LlmProtocolDispatcher.BuildModelsUrl(
                endpoint,
                ProtocolModeFor(mode),
                pageCursor);
        }

        /// <summary>Builds the full generation URL for the selected compatibility mode.</summary>
        public static string BuildGenerationUrl(string endpoint, ApiCompatibilityMode mode)
        {
            switch (mode)
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return RewriteEndpointPath(endpoint, "/responses");
                default:
                    return BuildChatCompletionsUrl(endpoint);
            }
        }

        /// <summary>
        /// Builds the final provider-aware generation URL. Native Gemini places the model in the path,
        /// while the historical two-argument OpenAI overload above remains byte-for-byte unchanged.
        /// </summary>
        public static string BuildGenerationUrl(
            string endpoint,
            string modelName,
            ApiCompatibilityMode mode)
        {
            return LlmProtocolDispatcher.BuildGenerationUrl(
                endpoint,
                modelName,
                ProtocolModeFor(mode));
        }

        /// <summary>
        /// Adapts the persisted enum to the transport-only protocol enum. Their explicit ordinals are
        /// intentionally mirrored; both normalizers keep retired ordinal 2 and unknown future values on
        /// the historical OpenAI Chat path.
        /// </summary>
        internal static LlmProtocolMode ProtocolModeFor(ApiCompatibilityMode mode)
        {
            ApiCompatibilityMode normalized = ApiEndpointPolicy.NormalizeApiMode(mode);
            return LlmProtocolDispatcher.NormalizeMode((int)normalized);
        }

        /// <summary>Builds the full /chat/completions URL for LLM requests.</summary>
        public static string BuildChatCompletionsUrl(string endpoint)
        {
            return RewriteEndpointPath(endpoint, "/chat/completions");
        }

        /// <summary>
        /// Rewrites only the URI path while leaving any query string and fragment in their legal
        /// positions. Plain string concatenation would turn
        /// <c>https://host/v1?key=x</c> into <c>https://host/v1?key=x/models</c>, where
        /// <c>/models</c> is part of the key value instead of the request path.
        /// </summary>
        private static string RewriteEndpointPath(string endpoint, string appendedSuffix)
        {
            string value = string.IsNullOrWhiteSpace(endpoint)
                ? ApiEndpointPolicy.DefaultEndpointUrl
                : endpoint.Trim();

            // UriBuilder understands which portion is the path, query, and fragment. Restrict this
            // path to HTTP(S): user-entered values such as "localhost:1234/v1" can otherwise be
            // interpreted as a custom URI scheme, so those values use the conservative fallback
            // below and retain the same text they had before validation by HttpClient.
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                UriBuilder builder = new UriBuilder(uri);
                builder.Path = AppendPathSuffix(StripKnownSuffix(builder.Path), appendedSuffix);
                return builder.Uri.AbsoluteUri;
            }

            // Keep malformed/host-only input recoverable in the settings UI. HttpClient will still
            // produce the useful validation error later, but path construction must not move a query
            // or fragment into the middle of the URL while preparing that request.
            SplitQueryAndFragment(value, out string path, out string tail);
            return AppendPathSuffix(StripKnownSuffix(path), appendedSuffix) + tail;
        }

        /// <summary>Removes trailing slashes and one recognized request suffix from a URI path.</summary>
        private static string StripKnownSuffix(string path)
        {
            string normalized = (path ?? string.Empty).TrimEnd('/');
            for (int i = 0; i < KnownEndpointSuffixes.Length; i++)
            {
                string suffix = KnownEndpointSuffixes[i];
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length).TrimEnd('/');
                    break;
                }
            }

            return normalized;
        }

        /// <summary>Appends a slash-prefixed request suffix to an already-normalized path.</summary>
        private static string AppendPathSuffix(string basePath, string suffix)
        {
            string normalized = (basePath ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(suffix))
            {
                // UriBuilder represents an empty absolute path as "/"; the fallback preserves an
                // actually-empty host-only value. Both forms are equivalent HTTP base endpoints.
                return normalized;
            }

            string normalizedSuffix = suffix[0] == '/' ? suffix : "/" + suffix;
            return normalized + normalizedSuffix;
        }

        /// <summary>
        /// Splits an invalid/non-absolute URI before its first query or fragment delimiter so the
        /// fallback path rewriter can preserve the untouched tail.
        /// </summary>
        private static void SplitQueryAndFragment(string value, out string path, out string tail)
        {
            int queryIndex = value.IndexOf('?');
            int fragmentIndex = value.IndexOf('#');
            int splitIndex;
            if (queryIndex < 0)
            {
                splitIndex = fragmentIndex;
            }
            else if (fragmentIndex < 0)
            {
                splitIndex = queryIndex;
            }
            else
            {
                splitIndex = Math.Min(queryIndex, fragmentIndex);
            }

            if (splitIndex < 0)
            {
                path = value;
                tail = string.Empty;
                return;
            }

            path = value.Substring(0, splitIndex);
            tail = value.Substring(splitIndex);
        }
    }
}
