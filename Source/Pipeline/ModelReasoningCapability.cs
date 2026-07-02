// Pure per-model reasoning capability descriptor and the effort-clamping policy.
//
// Some "OpenAI-compatible" providers (notably OpenRouter, and a few gateways) attach a per-model
// "reasoning" object to each entry of their /models response. When it is present, it tells us
// whether a model supports reasoning at all, which effort levels it accepts, and whether a token
// budget is honored. That lets the mod (a) guide the settings UI to only offer supported efforts
// and (b) clamp the outgoing request so we never send a value the model rejects -- which is the
// root cause of the "reasoning strength doesn't work / 400 Thinking budget not supported" class
// of error for models like Gemma that do not support reasoning at all.
//
// "Pure" here means: no RimWorld / Verse / Unity types, no .Translate(), no IO. It works on the
// Dictionary<string,object>/object[] shape produced by MiniJson, so tests can compile this file
// with MiniJson alone, without loading the game assemblies. Mirrors LlmResponseParser's contract.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Immutable snapshot of one model's reasoning capability, parsed from an OpenRouter-style
    /// <c>/models</c> entry. <see cref="Supported"/> is the only strictly required field once
    /// constructed; the effort list and token-budget flag may be empty/false but are never null.
    /// </summary>
    public sealed class ModelReasoningCapability
    {
        /// <summary>True when the model can produce reasoning tokens at all (e.g. o-series, R1,
        /// Claude with extended thinking). False for plain chat models (Gemma, Llama instruct).</summary>
        public readonly bool Supported;

        /// <summary>True when reasoning cannot be turned off (the model always thinks). The UI hides
        /// the "None" option in that case, but the request still works either way.</summary>
        public readonly bool Mandatory;

        /// <summary>Effort levels the model accepts (e.g. low/medium/high). Empty when the provider
        /// reports support but does not enumerate levels; callers fall back to the default effort.</summary>
        public readonly List<string> SupportedEfforts;

        /// <summary>The provider's recommended default effort, or empty. Used as the clamp target when
        /// the player picked an effort the model does not list.</summary>
        public readonly string DefaultEffort;

        /// <summary>True when the provider honors a reasoning token budget. Informational today; kept
        /// so future budget-aware UI can show a slider only when it is meaningful.</summary>
        public readonly bool SupportsMaxTokens;

        private ModelReasoningCapability(bool supported, bool mandatory, List<string> supportedEfforts, string defaultEffort, bool supportsMaxTokens)
        {
            Supported = supported;
            Mandatory = mandatory;
            SupportedEfforts = supportedEfforts ?? new List<string>();
            DefaultEffort = defaultEffort ?? string.Empty;
            SupportsMaxTokens = supportsMaxTokens;
        }

        /// <summary>
        /// Parses the OpenRouter-shape <c>reasoning</c> object from one <c>/models</c> data entry.
        /// Returns null when the entry has no <c>reasoning</c> object, which means the provider does
        /// not advertise capability at all (OpenAI-direct, local GGUF servers) -- callers must treat
        /// null as "unknown, fall back to today's unconditional behavior".
        /// </summary>
        public static ModelReasoningCapability FromModelEntry(Dictionary<string, object> modelEntry)
        {
            if (modelEntry == null || !modelEntry.TryGetValue("reasoning", out object reasoningObject))
            {
                return null;
            }

            Dictionary<string, object> reasoning = reasoningObject as Dictionary<string, object>;
            if (reasoning == null)
            {
                return null;
            }

            // default_enabled is the canonical field; some gateways omit it and only list efforts,
            // in which case "has any effort list" implies support.
            bool supported = AsBool(reasoning, "default_enabled");
            List<string> efforts = AsStringList(reasoning, "supported_efforts");
            if (!supported && efforts.Count > 0)
            {
                supported = true;
            }

            string defaultEffort = AsTrimmedString(reasoning, "default_effort");
            bool mandatory = AsBool(reasoning, "mandatory");
            bool supportsMaxTokens = AsBool(reasoning, "supports_max_tokens");

            return new ModelReasoningCapability(supported, mandatory, efforts, defaultEffort, supportsMaxTokens);
        }

        /// <summary>
        /// Resolves the player-chosen reasoning effort against a model's capability so the request
        /// never carries a value the model rejects. Pure: given the same inputs it always returns
        /// the same normalized effort string. Returns the chosen effort unchanged when capability is
        /// null (provider did not advertise reasoning -- degrade gracefully to today's behavior).
        /// </summary>
        public static string EffectiveReasoningEffort(string chosenEffort, ModelReasoningCapability capability)
        {
            // Unknown capability: behave exactly as before the feature existed.
            if (capability == null)
            {
                return chosenEffort;
            }

            string chosen = ApiEndpointPolicy.NormalizeReasoningEffort(chosenEffort);

            // "default" (no override) and "none" survive as-is: they mean "let the API decide" and
            // "explicitly off" respectively, and the request builder already maps both correctly.
            if (string.Equals(chosen, ApiEndpointPolicy.DefaultReasoningEffort, StringComparison.Ordinal))
            {
                return chosen;
            }

            // Model cannot reason at all. Force "none": Chat Completions omits reasoning_effort
            // (the builder's ShouldEmitChatReasoningEffort rule) and Responses sends effort:"none".
            // This is the direct fix for "Gemma 400 Thinking budget is not supported for this model".
            if (!capability.Supported)
            {
                return "none";
            }

            // The player did not override (default) but the model supports reasoning -- leave it to
            // the API by staying "default". (Reached only when chosen defaulted and Supported, since
            // the explicit-default branch above already returned. Kept defensive/explicit.)
            if (string.Equals(chosen, ApiEndpointPolicy.DefaultReasoningEffort, StringComparison.Ordinal))
            {
                return chosen;
            }

            // Explicit effort the model lists as supported: keep it.
            if (ContainsOrdinalIgnoreCase(capability.SupportedEfforts, chosen))
            {
                return chosen;
            }

            // Explicit effort the model does NOT list: clamp to the provider default, else the
            // highest supported effort, else "default" (omit). Never send an unsupported value.
            if (!string.IsNullOrWhiteSpace(capability.DefaultEffort))
            {
                return ApiEndpointPolicy.NormalizeReasoningEffort(capability.DefaultEffort);
            }

            if (capability.SupportedEfforts.Count > 0)
            {
                return HighestSupportedEffort(capability.SupportedEfforts);
            }

            return ApiEndpointPolicy.DefaultReasoningEffort;
        }

        /// <summary>
        /// Picks the most thorough effort present in a capability's supported list, since the player
        /// explicitly asked for reasoning and a model without an enumerated default still benefits
        /// from the deepest available level. Order: xhigh &gt; high &gt; medium &gt; low &gt; minimal.
        /// </summary>
        private static string HighestSupportedEffort(List<string> supportedEfforts)
        {
            string[] preference = { "xhigh", "high", "medium", "low", "minimal" };
            for (int i = 0; i < preference.Length; i++)
            {
                if (ContainsOrdinalIgnoreCase(supportedEfforts, preference[i]))
                {
                    return preference[i];
                }
            }

            // Unknown effort strings (a provider inventing its own level): keep the first one.
            return supportedEfforts.Count > 0 ? supportedEfforts[0] : ApiEndpointPolicy.DefaultReasoningEffort;
        }

        // ---- MiniJson shape helpers (objects are Dictionary<string,object>, arrays are object[]) ----

        private static bool AsBool(Dictionary<string, object> fields, string key)
        {
            if (fields == null || !fields.TryGetValue(key, out object value) || value == null)
            {
                return false;
            }

            if (value is bool b)
            {
                return b;
            }

            // Some providers emit "true"/"false" as strings.
            if (value is string s)
            {
                return string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string AsTrimmedString(Dictionary<string, object> fields, string key)
        {
            if (fields == null || !fields.TryGetValue(key, out object value) || value == null)
            {
                return string.Empty;
            }

            return (value as string ?? string.Empty).Trim();
        }

        private static List<string> AsStringList(Dictionary<string, object> fields, string key)
        {
            List<string> result = new List<string>();
            if (fields == null || !fields.TryGetValue(key, out object value) || value == null)
            {
                return result;
            }

            object[] array = value as object[];
            if (array == null)
            {
                return result;
            }

            for (int i = 0; i < array.Length; i++)
            {
                string item = array[i] as string;
                if (!string.IsNullOrWhiteSpace(item))
                {
                    result.Add(item.Trim().ToLowerInvariant());
                }
            }

            return result;
        }

        /// <summary>Case-insensitive membership test avoiding the LINQ/IEqualityComparer overloads
        /// absent on RimWorld's Mono runtime. Mirrors the helper in LlmResponseParser.</summary>
        private static bool ContainsOrdinalIgnoreCase(List<string> list, string value)
        {
            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
