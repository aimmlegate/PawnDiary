// Pure provider routing for LLM URLs, stable tokens, fixed headers, and auth recommendations.
//
// This file plans wire details only. It never creates an HttpRequestMessage and never accepts an
// API-key value, which makes it impossible for a returned fixed-header plan to leak a secret.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Maps one protocol mode to its provider-specific wire contract.</summary>
    internal static class LlmProtocolDispatcher
    {
        public const string ChatCompletionsToken = "chatCompletions";
        public const string ResponsesToken = "responses";
        public const string AnthropicMessagesToken = "anthropicMessages";
        public const string GeminiGenerateContentToken = "geminiGenerateContent";
        public const string OllamaChatToken = "ollamaChat";

        public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";
        public const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta";
        public const string DefaultOllamaEndpoint = "http://localhost:11434";
        public const string AnthropicVersionHeaderName = "anthropic-version";
        public const string AnthropicVersionHeaderValue = "2023-06-01";
        public const string AnthropicApiKeyHeaderName = "x-api-key";
        public const string GeminiApiKeyHeaderName = "x-goog-api-key";

        private const int MaxPaginationCursorChars = 1024;

        /// <summary>
        /// Normalizes corrupt/future values conservatively. Reserved ordinal 2 is deliberately not a
        /// live protocol and therefore falls back to historical OpenAI Chat behavior.
        /// </summary>
        public static LlmProtocolMode NormalizeMode(LlmProtocolMode mode)
        {
            switch (mode)
            {
                case LlmProtocolMode.OpenAIResponses:
                case LlmProtocolMode.AnthropicMessages:
                case LlmProtocolMode.GeminiGenerateContent:
                case LlmProtocolMode.OllamaChat:
                    return mode;
                default:
                    return LlmProtocolMode.OpenAIChatCompletions;
            }
        }

        /// <summary>Normalizes a raw persisted ordinal without allowing legacy ordinal 2 to reactivate.</summary>
        public static LlmProtocolMode NormalizeMode(int rawMode)
        {
            return NormalizeMode((LlmProtocolMode)rawMode);
        }

        /// <summary>Returns the stable public token for a normalized wire mode.</summary>
        public static string StableToken(LlmProtocolMode mode)
        {
            switch (NormalizeMode(mode))
            {
                case LlmProtocolMode.OpenAIResponses:
                    return ResponsesToken;
                case LlmProtocolMode.AnthropicMessages:
                    return AnthropicMessagesToken;
                case LlmProtocolMode.GeminiGenerateContent:
                    return GeminiGenerateContentToken;
                case LlmProtocolMode.OllamaChat:
                    return OllamaChatToken;
                default:
                    return ChatCompletionsToken;
            }
        }

        /// <summary>Parses a stable public token, falling back to OpenAI Chat for unknown input.</summary>
        public static LlmProtocolMode FromStableToken(string token)
        {
            string normalized = (token ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "responses":
                case "openairesponses":
                case "openai_responses":
                    return LlmProtocolMode.OpenAIResponses;
                case "anthropicmessages":
                    return LlmProtocolMode.AnthropicMessages;
                case "geminigeneratecontent":
                    return LlmProtocolMode.GeminiGenerateContent;
                case "ollamachat":
                    return LlmProtocolMode.OllamaChat;
                default:
                    return LlmProtocolMode.OpenAIChatCompletions;
            }
        }

        /// <summary>
        /// Returns the model text that identifies the actual provider wire target. Gemini accepts a
        /// resource-name prefix in discovery but its generation path adds <c>models/</c> itself, so
        /// trim and strip that prefix. Other modes preserve their raw model text exactly, matching
        /// their established request-JSON and lane-identity behavior.
        /// </summary>
        public static string CanonicalModelName(string modelName, LlmProtocolMode mode)
        {
            if (NormalizeMode(mode) != LlmProtocolMode.GeminiGenerateContent)
            {
                return modelName ?? string.Empty;
            }

            string model = (modelName ?? string.Empty).Trim();
            const string prefix = "models/";
            return model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? model.Substring(prefix.Length)
                : model;
        }

        /// <summary>
        /// Normalizes one saved row to an editable provider base URL. Only canonical request suffixes
        /// for the already-selected protocol are removed; this never infers a protocol from the URL.
        /// Query strings and fragments remain in their legal positions.
        /// </summary>
        public static string NormalizeBaseEndpoint(
            string endpoint,
            string modelName,
            LlmProtocolMode mode)
        {
            LlmProtocolMode normalized = NormalizeMode(mode);
            switch (normalized)
            {
                case LlmProtocolMode.AnthropicMessages:
                    return RewriteNativeUrl(
                        endpoint,
                        DefaultAnthropicEndpoint,
                        normalized,
                        null,
                        modelName);
                case LlmProtocolMode.GeminiGenerateContent:
                    return RewriteNativeUrl(
                        endpoint,
                        DefaultGeminiEndpoint,
                        normalized,
                        null,
                        modelName);
                case LlmProtocolMode.OllamaChat:
                    return RewriteNativeUrl(
                        endpoint,
                        DefaultOllamaEndpoint,
                        normalized,
                        null,
                        modelName);
                default:
                    return EndpointUtility.NormalizeBaseEndpoint(endpoint);
            }
        }

        /// <summary>Builds a provider- and model-aware generation URL.</summary>
        public static string BuildGenerationUrl(string endpoint, string modelName, LlmProtocolMode mode)
        {
            switch (NormalizeMode(mode))
            {
                case LlmProtocolMode.OpenAIResponses:
                    return EndpointUtility.BuildGenerationUrl(endpoint, ApiCompatibilityMode.OpenAIResponses);
                case LlmProtocolMode.AnthropicMessages:
                    return RewriteNativeUrl(endpoint, DefaultAnthropicEndpoint, mode, "messages", modelName);
                case LlmProtocolMode.GeminiGenerateContent:
                    return RewriteNativeUrl(endpoint, DefaultGeminiEndpoint, mode, "generate", modelName);
                case LlmProtocolMode.OllamaChat:
                    return RewriteNativeUrl(endpoint, DefaultOllamaEndpoint, mode, "chat", modelName);
                default:
                    return EndpointUtility.BuildGenerationUrl(endpoint, ApiCompatibilityMode.OpenAIChatCompletions);
            }
        }

        /// <summary>Builds the first provider-aware model-list URL.</summary>
        public static string BuildModelsUrl(string endpoint, LlmProtocolMode mode)
        {
            return BuildModelsUrl(endpoint, mode, string.Empty);
        }

        /// <summary>
        /// Builds a model-list URL and, for paginated native providers, replaces/appends the bounded
        /// cursor query parameter while preserving every unrelated query parameter and fragment.
        /// </summary>
        public static string BuildModelsUrl(string endpoint, LlmProtocolMode mode, string pageCursor)
        {
            LlmProtocolMode normalized = NormalizeMode(mode);
            string url;
            switch (normalized)
            {
                case LlmProtocolMode.OpenAIResponses:
                case LlmProtocolMode.OpenAIChatCompletions:
                    url = EndpointUtility.BuildModelsUrl(
                        endpoint,
                        normalized == LlmProtocolMode.OpenAIResponses
                            ? ApiCompatibilityMode.OpenAIResponses
                            : ApiCompatibilityMode.OpenAIChatCompletions);
                    break;
                case LlmProtocolMode.AnthropicMessages:
                    url = RewriteNativeUrl(endpoint, DefaultAnthropicEndpoint, normalized, "models", string.Empty);
                    break;
                case LlmProtocolMode.GeminiGenerateContent:
                    url = RewriteNativeUrl(endpoint, DefaultGeminiEndpoint, normalized, "models", string.Empty);
                    break;
                default:
                    url = RewriteNativeUrl(endpoint, DefaultOllamaEndpoint, normalized, "models", string.Empty);
                    break;
            }

            string cursor = BoundedCursor(pageCursor);
            if (string.IsNullOrEmpty(cursor))
            {
                return url;
            }

            if (normalized == LlmProtocolMode.AnthropicMessages)
            {
                return SetQueryParameter(url, "after_id", cursor);
            }

            if (normalized == LlmProtocolMode.GeminiGenerateContent)
            {
                return SetQueryParameter(url, "pageToken", cursor);
            }

            return url;
        }

        /// <summary>
        /// Returns fixed non-secret headers and preset auth defaults, and flags a saved custom secret
        /// header whose name collides with a mandatory fixed protocol header.
        /// </summary>
        public static LlmProtocolHeadersPlan HeadersFor(
            LlmProtocolMode mode,
            ApiAuthMode savedAuthMode,
            string savedCustomHeaderName)
        {
            LlmProtocolMode normalized = NormalizeMode(mode);
            List<LlmProtocolHeader> required = new List<LlmProtocolHeader>();
            ApiAuthMode recommendedAuth = ApiAuthMode.BearerToken;
            string recommendedHeader = string.Empty;

            switch (normalized)
            {
                case LlmProtocolMode.AnthropicMessages:
                    required.Add(new LlmProtocolHeader(
                        AnthropicVersionHeaderName,
                        AnthropicVersionHeaderValue));
                    recommendedAuth = ApiAuthMode.CustomHeader;
                    recommendedHeader = AnthropicApiKeyHeaderName;
                    break;
                case LlmProtocolMode.GeminiGenerateContent:
                    recommendedAuth = ApiAuthMode.CustomHeader;
                    recommendedHeader = GeminiApiKeyHeaderName;
                    break;
                case LlmProtocolMode.OllamaChat:
                    recommendedAuth = ApiAuthMode.None;
                    break;
            }

            string secretHeader = ApiEndpointPolicy.EffectiveAuthHeaderName(
                savedAuthMode,
                savedCustomHeaderName);
            string collision = string.Empty;
            if (!string.IsNullOrEmpty(secretHeader))
            {
                for (int i = 0; i < required.Count; i++)
                {
                    if (string.Equals(required[i].Name, secretHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        collision = required[i].Name;
                        break;
                    }
                }
            }

            return new LlmProtocolHeadersPlan(
                required,
                recommendedAuth,
                recommendedHeader,
                !string.IsNullOrEmpty(collision),
                collision);
        }

        private static string RewriteNativeUrl(
            string endpoint,
            string defaultEndpoint,
            LlmProtocolMode mode,
            string target,
            string modelName)
        {
            string value = string.IsNullOrWhiteSpace(endpoint) ? defaultEndpoint : endpoint.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                UriBuilder builder = new UriBuilder(uri);
                string basePath = NativeBasePath(builder.Path, mode);
                builder.Path = NativeTargetPath(basePath, mode, target, modelName);
                return builder.Uri.AbsoluteUri;
            }

            SplitQueryAndFragment(value, out string path, out string tail);
            return NativeTargetPath(NativeBasePath(path, mode), mode, target, modelName) + tail;
        }

        private static string NativeBasePath(string path, LlmProtocolMode mode)
        {
            string normalized = (path ?? string.Empty).TrimEnd('/');
            switch (NormalizeMode(mode))
            {
                case LlmProtocolMode.AnthropicMessages:
                    return StripEnding(normalized, "/v1/messages", "/messages",
                        StripEnding(normalized, "/v1/models", "/models", normalized));
                case LlmProtocolMode.GeminiGenerateContent:
                    int modelsIndex = normalized.LastIndexOf("/models/", StringComparison.OrdinalIgnoreCase);
                    if (modelsIndex >= 0
                        && normalized.EndsWith(":generateContent", StringComparison.OrdinalIgnoreCase))
                    {
                        return normalized.Substring(0, modelsIndex);
                    }
                    return normalized.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                        ? normalized.Substring(0, normalized.Length - "/models".Length).TrimEnd('/')
                        : normalized;
                case LlmProtocolMode.OllamaChat:
                    return StripEnding(normalized, "/api/chat", "/chat",
                        StripEnding(normalized, "/api/tags", "/tags", normalized));
                default:
                    return normalized;
            }
        }

        private static string NativeTargetPath(
            string basePath,
            LlmProtocolMode mode,
            string target,
            string modelName)
        {
            string normalized = (basePath ?? string.Empty).TrimEnd('/');
            if (target == null)
            {
                // A null target is the settings-normalization sentinel: return the recognized native
                // base without appending a generation or model-list suffix.
                return normalized;
            }

            switch (NormalizeMode(mode))
            {
                case LlmProtocolMode.AnthropicMessages:
                    return AppendAtVersionRoot(normalized, "/v1", "/" + target);
                case LlmProtocolMode.GeminiGenerateContent:
                    if (target == "models")
                    {
                        return normalized + "/models";
                    }

                    string model = CanonicalModelName(modelName, LlmProtocolMode.GeminiGenerateContent);
                    return normalized + "/models/" + Uri.EscapeDataString(model) + ":generateContent";
                case LlmProtocolMode.OllamaChat:
                    return AppendAtVersionRoot(
                        normalized,
                        "/api",
                        target == "models" ? "/tags" : "/chat");
                default:
                    return normalized;
            }
        }

        private static string AppendAtVersionRoot(string path, string root, string child)
        {
            return path.EndsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path + child
                : path + root + child;
        }

        private static string StripEnding(
            string value,
            string fullEnding,
            string childEnding,
            string fallback)
        {
            if (!value.EndsWith(fullEnding, StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            return value.Substring(0, value.Length - childEnding.Length).TrimEnd('/');
        }

        private static string BoundedCursor(string cursor)
        {
            string value = (cursor ?? string.Empty).Trim();
            return value.Length <= MaxPaginationCursorChars ? value : string.Empty;
        }

        private static string SetQueryParameter(string url, string name, string value)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                UriBuilder builder = new UriBuilder(uri);
                builder.Query = ReplaceQueryParameter(builder.Query, name, value);
                return builder.Uri.AbsoluteUri;
            }

            int fragmentIndex = url.IndexOf('#');
            string fragment = fragmentIndex >= 0 ? url.Substring(fragmentIndex) : string.Empty;
            string withoutFragment = fragmentIndex >= 0 ? url.Substring(0, fragmentIndex) : url;
            int queryIndex = withoutFragment.IndexOf('?');
            string path = queryIndex >= 0 ? withoutFragment.Substring(0, queryIndex) : withoutFragment;
            string query = queryIndex >= 0 ? withoutFragment.Substring(queryIndex + 1) : string.Empty;
            string replaced = ReplaceQueryParameter(query, name, value);
            return path + (string.IsNullOrEmpty(replaced) ? string.Empty : "?" + replaced) + fragment;
        }

        private static string ReplaceQueryParameter(string query, string name, string value)
        {
            string raw = (query ?? string.Empty).TrimStart('?');
            string encoded = Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
            if (string.IsNullOrEmpty(raw))
            {
                return encoded;
            }

            string[] fields = raw.Split('&');
            bool replaced = false;
            for (int i = 0; i < fields.Length; i++)
            {
                int equals = fields[i].IndexOf('=');
                string rawName = equals < 0 ? fields[i] : fields[i].Substring(0, equals);
                string decodedName;
                try
                {
                    decodedName = Uri.UnescapeDataString(rawName);
                }
                catch
                {
                    decodedName = rawName;
                }

                if (string.Equals(decodedName, name, StringComparison.OrdinalIgnoreCase))
                {
                    fields[i] = encoded;
                    replaced = true;
                }
            }

            return replaced ? string.Join("&", fields) : raw + "&" + encoded;
        }

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

            path = splitIndex < 0 ? value : value.Substring(0, splitIndex);
            tail = splitIndex < 0 ? string.Empty : value.Substring(splitIndex);
        }
    }
}
