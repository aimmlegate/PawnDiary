// Process-wide cache of immutable per-provider model capabilities discovered from model-list rows.
//
// Capability is a property of (protocol, endpoint, model), never of the API key. Including the
// normalized protocol prevents metadata advertised by an OpenAI-compatible endpoint from leaking
// into a native Gemini/Anthropic row that happens to reuse the same editable URL and model text.
//
// Thread-safety: writes happen on the main thread (settings fetch apply) and reads happen on the
// background generation thread. ConcurrentDictionary plus immutable values keeps that safe.
using System;
using System.Collections.Concurrent;

namespace PawnDiary
{
    /// <summary>Immutable capability snapshot cached for one provider model.</summary>
    internal sealed class ModelProtocolCapability
    {
        /// <summary>OpenAI-compatible reasoning metadata, or null when not advertised/applicable.</summary>
        public readonly ModelReasoningCapability ReasoningCapability;

        /// <summary>Positive provider-advertised output-token limit, or zero when unknown.</summary>
        public readonly int MaxOutputTokens;

        /// <summary>Finite non-negative sampling maximum, or null when unknown.</summary>
        public readonly double? MaxTemperature;

        public ModelProtocolCapability(
            ModelReasoningCapability reasoningCapability,
            int maxOutputTokens,
            double? maxTemperature)
        {
            ReasoningCapability = reasoningCapability;
            MaxOutputTokens = Math.Max(0, maxOutputTokens);
            MaxTemperature = IsUsableTemperature(maxTemperature) ? maxTemperature : null;
        }

        /// <summary>Creates an immutable cache value from one pure model-list entry.</summary>
        public static ModelProtocolCapability FromEntry(LlmProtocolModelEntry entry)
        {
            return entry == null
                ? null
                : new ModelProtocolCapability(
                    entry.ReasoningCapability,
                    entry.MaxOutputTokens,
                    entry.MaxTemperature);
        }

        private static bool IsUsableTemperature(double? value)
        {
            return value.HasValue
                && !double.IsNaN(value.Value)
                && !double.IsInfinity(value.Value)
                && value.Value >= 0d;
        }
    }

    /// <summary>
    /// Static in-memory cache mapping a normalized (protocol, endpoint, modelId) tuple to provider
    /// capability metadata. Keys deliberately contain no authentication material.
    /// </summary>
    internal static class ModelCapabilityCache
    {
        private static readonly ConcurrentDictionary<string, ModelProtocolCapability> cache =
            new ConcurrentDictionary<string, ModelProtocolCapability>();

        /// <summary>Records one immutable capability, replacing any prior value for the tuple.</summary>
        public static void Update(
            string endpointUrl,
            ApiCompatibilityMode apiMode,
            string modelId,
            ModelProtocolCapability capability)
        {
            if (capability == null || string.IsNullOrWhiteSpace(modelId))
            {
                return;
            }

            cache[CacheKey(endpointUrl, apiMode, modelId)] = capability;
        }

        /// <summary>Returns cached provider metadata, or null when this tuple is unknown.</summary>
        public static ModelProtocolCapability Get(
            string endpointUrl,
            ApiCompatibilityMode apiMode,
            string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return null;
            }

            ModelProtocolCapability capability;
            return cache.TryGetValue(CacheKey(endpointUrl, apiMode, modelId), out capability)
                ? capability
                : null;
        }

        /// <summary>Returns only the legacy reasoning view used by the existing settings control.</summary>
        public static ModelReasoningCapability GetReasoning(
            string endpointUrl,
            ApiCompatibilityMode apiMode,
            string modelId)
        {
            return Get(endpointUrl, apiMode, modelId)?.ReasoningCapability;
        }

        private static string CacheKey(
            string endpointUrl,
            ApiCompatibilityMode apiMode,
            string modelId)
        {
            ApiCompatibilityMode normalizedMode = ApiEndpointPolicy.NormalizeApiMode(apiMode);
            string modelsUrl = EndpointUtility.BuildModelsUrl(
                endpointUrl,
                normalizedMode,
                string.Empty);
            // A player can save a complete proxy URL containing query auth. The cache must retain
            // non-secret routing parameters (tenant/api-version) but never keep recognized key/token
            // values in its process-wide identity string.
            string safeModelsUrl = SanitizeModelsUrlForCache(modelsUrl);
            string canonicalModel = LlmProtocolDispatcher.CanonicalModelName(
                modelId,
                EndpointUtility.ProtocolModeFor(normalizedMode));
            return normalizedMode + "|" + safeModelsUrl + "|" + canonicalModel;
        }

        private static string SanitizeModelsUrlForCache(string value)
        {
            string url = ApiLaneLabels.RedactSecrets(value ?? string.Empty);
            int queryIndex = url.IndexOf('?');
            if (queryIndex < 0)
            {
                return url;
            }

            int fragmentIndex = url.IndexOf('#', queryIndex + 1);
            int queryEnd = fragmentIndex < 0 ? url.Length : fragmentIndex;
            string[] parameters = url.Substring(
                queryIndex + 1,
                queryEnd - queryIndex - 1).Split('&');
            for (int i = 0; i < parameters.Length; i++)
            {
                int equalsIndex = parameters[i].IndexOf('=');
                string rawName = equalsIndex < 0
                    ? parameters[i]
                    : parameters[i].Substring(0, equalsIndex);
                string decodedName;
                try
                {
                    decodedName = Uri.UnescapeDataString(rawName);
                }
                catch
                {
                    decodedName = rawName;
                }

                string normalizedName = (decodedName ?? string.Empty).ToLowerInvariant();
                if (equalsIndex >= 0
                    && (normalizedName.IndexOf("key", StringComparison.Ordinal) >= 0
                        || normalizedName.IndexOf("token", StringComparison.Ordinal) >= 0
                        || normalizedName.IndexOf("auth", StringComparison.Ordinal) >= 0))
                {
                    parameters[i] = rawName + "=<redacted>";
                }
            }

            string fragment = fragmentIndex < 0 ? string.Empty : url.Substring(fragmentIndex);
            return url.Substring(0, queryIndex + 1)
                + string.Join("&", parameters)
                + fragment;
        }
    }
}
