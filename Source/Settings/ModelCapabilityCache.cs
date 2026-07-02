// Process-wide cache of per-model reasoning capability, populated at settings time from /models
// responses and read on the background generation thread to clamp reasoning_effort.
//
// Capability is a property of (endpoint, model), not of the API key: the same model behind the same
// gateway supports the same efforts regardless of which key authenticates the request. So the key
// excludes the key deliberately -- this also avoids caching anything key-derived, and means a key
// rotation does not needlessly invalidate the capability map.
//
// Thread-safety: writes happen on the main thread (settings fetch apply) and reads happen on the
// background generation thread. ConcurrentDictionary plus immutable ModelReasoningCapability
// instances make this safe without explicit locking.
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Static in-memory cache mapping a normalized (endpoint, modelId) pair to that model's
    /// advertised reasoning capability, or null when capability is unknown. Populated from
    /// <see cref="ModelListClient"/> results and consulted by the generation path to clamp effort.
    /// </summary>
    internal static class ModelCapabilityCache
    {
        private static readonly ConcurrentDictionary<string, ModelReasoningCapability> cache =
            new ConcurrentDictionary<string, ModelReasoningCapability>();

        /// <summary>Records a capability for one (endpoint, modelId) pair, replacing any prior entry.</summary>
        public static void Update(string endpointUrl, string modelId, ModelReasoningCapability capability)
        {
            if (capability == null || string.IsNullOrWhiteSpace(modelId))
            {
                return;
            }

            cache[CacheKey(endpointUrl, modelId)] = capability;
        }

        /// <summary>
        /// Bulk-records capabilities from one fetch result for a shared endpoint. Models without an
        /// advertised reasoning object are skipped (they stay "unknown" rather than being cached as
        /// unsupported, preserving graceful degradation).
        /// </summary>
        public static void UpdateFromFetch(string endpointUrl, ModelListResult fetchResult)
        {
            if (fetchResult?.Capabilities == null)
            {
                return;
            }

            foreach (KeyValuePair<string, ModelReasoningCapability> entry in fetchResult.Capabilities)
            {
                Update(endpointUrl, entry.Key, entry.Value);
            }
        }

        /// <summary>Returns the cached capability for (endpoint, modelId), or null when unknown.</summary>
        public static ModelReasoningCapability Get(string endpointUrl, string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return null;
            }

            ModelReasoningCapability capability;
            return cache.TryGetValue(CacheKey(endpointUrl, modelId), out capability) ? capability : null;
        }

        // The key deliberately excludes the API key: capability is a property of (endpoint, model),
        // and excluding the key also avoids caching key-derived data and surviving key rotations.
        private static string CacheKey(string endpointUrl, string modelId)
        {
            return EndpointUtility.NormalizeBaseEndpoint(endpointUrl) + "|" + (modelId ?? string.Empty);
        }
    }
}
