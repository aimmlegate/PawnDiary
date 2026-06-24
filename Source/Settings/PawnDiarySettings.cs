// All mod settings (connection, generation options, prompt overrides, per-group enable overrides,
// and persona edits) plus value clamping and save/load. Prompt templates, signal policies, and
// per-group instructions are XML Defs, not save settings.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Which request/response shape an API lane speaks. Most providers that advertise
    /// "OpenAI-compatible" should use <see cref="OpenAIChatCompletions"/>; the other modes cover
    /// newer OpenAI reasoning models and Ollama's native thinking-model endpoint.
    /// </summary>
    public enum ApiCompatibilityMode
    {
        OpenAIChatCompletions,
        OpenAIResponses,
        OllamaNativeChat
    }

    /// <summary>
    /// One configured API "lane": a single compatible endpoint, its optional key, and the one
    /// model it serves. Many of these can be listed (see <see cref="PawnDiarySettings.apiEndpoints"/>)
    /// so diary generation is spread across them in parallel. We keep it to one model per row on
    /// purpose — to use several models you just add several rows (possibly sharing an endpoint).
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it — see AGENTS.md ("IExposable").
    /// </summary>
    public class ApiEndpointConfig : IExposable
    {
        // Base URL of the API. EndpointUtility adds the mode-specific path at send time.
        public string url = PawnDiarySettings.DefaultEndpointUrl;
        // Model name sent in the request payload. Required — a row with no model is ignored.
        public string model = string.Empty;
        // API key (may be empty for local models that don't require auth).
        public string apiKey = string.Empty;
        // How apiKey is attached to requests for this lane. Bearer preserves existing saves.
        public ApiAuthMode authMode = ApiAuthMode.BearerToken;
        // When false, keep this row configured but exclude it from generation and failover.
        public bool enabled = true;
        // Request/response compatibility mode. Default preserves existing OpenAI-compatible setups.
        public ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
        // OpenAI Responses reasoning effort. "default" means omit the reasoning object entirely.
        public string reasoningEffort = PawnDiarySettings.DefaultReasoningEffort;
        // Ollama native chat: opt into the model's separate thinking stream when supported.
        public bool ollamaThink;

        public ApiEndpointConfig()
        {
        }

        public ApiEndpointConfig(string url, string apiKey, string model)
        {
            this.url = url;
            this.apiKey = apiKey;
            this.model = model;
        }

        /// <summary>
        /// Returns a detached copy for in-flight requests so failover can mutate the active lane
        /// without editing the player's saved settings row.
        /// </summary>
        public ApiEndpointConfig Copy()
        {
            return new ApiEndpointConfig(url, apiKey, model)
            {
                enabled = enabled,
                authMode = authMode,
                apiMode = apiMode,
                reasoningEffort = reasoningEffort,
                ollamaThink = ollamaThink
            };
        }

        // Reads/writes the row fields on save and load (Scribe is RimWorld's serializer).
        public void ExposeData()
        {
            Scribe_Values.Look(ref url, "url", PawnDiarySettings.DefaultEndpointUrl);
            Scribe_Values.Look(ref model, "model", string.Empty);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref authMode, "authMode", ApiAuthMode.BearerToken);
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref apiMode, "apiMode", ApiCompatibilityMode.OpenAIChatCompletions);
            Scribe_Values.Look(ref reasoningEffort, "reasoningEffort", PawnDiarySettings.DefaultReasoningEffort);
            Scribe_Values.Look(ref ollamaThink, "ollamaThink", false);
        }
    }

    /// <summary>
    /// One editable persona preset row persisted in settings. Rows are either:
    /// - an override of an XML persona Def (custom = false, defName matches the Def), or
    /// - a fully custom persona created in settings (custom = true).
    /// </summary>
    public class PersonaPresetConfig : IExposable
    {
        // Stable key used everywhere personas are referenced (per-pawn record, prompt context, picker).
        public string defName = string.Empty;
        // Human-readable picker label shown in UI.
        public string label = string.Empty;
        // Writing-style rule appended to prompts as the persona voice target.
        public string rule = string.Empty;
        // Internal theme tags used for weighted first-roll persona selection.
        public List<string> themes = new List<string>();
        // True when this row is a user-created persona (not an override of an XML Def).
        public bool custom;

        public PersonaPresetConfig()
        {
        }

        public PersonaPresetConfig(string defName, string label, string rule, IEnumerable<string> themes, bool custom)
        {
            this.defName = defName ?? string.Empty;
            this.label = label ?? string.Empty;
            this.rule = rule ?? string.Empty;
            this.themes = PawnDiarySettings.NormalizeThemes(themes);
            this.custom = custom;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", string.Empty);
            Scribe_Values.Look(ref label, "label", string.Empty);
            Scribe_Values.Look(ref rule, "rule", string.Empty);
            Scribe_Collections.Look(ref themes, "themes", LookMode.Value);
            Scribe_Values.Look(ref custom, "custom", false);
        }
    }

    public class PawnDiarySettings : ModSettings
    {
        // The configured API lanes used for generation. Requests are distributed according to
        // apiRoutingMode and run in parallel (one in-flight request per lane, see LlmClient).
        public List<ApiEndpointConfig> apiEndpoints = new List<ApiEndpointConfig>();
        // Global primary-lane routing policy. Row order always controls failover order.
        public ApiLaneRoutingMode apiRoutingMode = ApiLaneRoutingMode.Balanced;

        // Per-request timeout in seconds before the request is cancelled.
        public int timeoutSeconds = 30;
        // Maximum number of in-flight LLM requests to avoid overwhelming local servers.
        public int maxConcurrentRequests = 4;
        // Token cap on each completion response to keep diary entries concise.
        // Reduced from 160 to 100 for faster generation on small local models (6B–31B).
        public int maxTokens = 100;
        // Sampling temperature — higher values produce more creative/varied entries.
        public float temperature = 0.8f;
        // UI preference: when false, the compact API/model setup block is collapsed in mod settings.
        public bool showApiSettings = true;
        // UI preference: when false, the compact Prompt Studio block is collapsed in mod settings.
        public bool showPromptStudio = true;
        // Dev-mode UI preference: shows the per-pawn persona picker in the Diary inspector tab.
        public bool showPersonaSettings = false;
        // Dev-mode UI preference: shows raw/pending entries and the LLM prompt/status diagnostic block.
        public bool showLlmDebugInfo = false;
        // Dev-mode UI preference: reveals entries still in the generation pipeline (in-progress or
        // stuck on "writing...") in the pawn Diary tab, without the full LLM diagnostic block. Lets a
        // player see which events never finished generating. Normal mode always hides them.
        public bool showGeneratingEntries = false;
        // Dev-mode test switch: captures assembled prompts on real gameplay events and skips the LLM
        // request. Prompt-only cards appear in the Diary tab so prompt formatting can be checked
        // without running a model or writing fake generated text.
        public bool promptTestMode = false;
        // Master toggle for the LLM-titling flow. When false, no extra title call is made and
        // diary card headers stay date-only.
        public bool generateTitles = true;
        // Display-only diary page atmosphere. When true, rare extreme entries can use unusual
        // spacing or staggered word sizes in the Diary tab. This never changes prompts or saved
        // generated text.
        public bool enableAtmosphericFormatting = true;
        // Master toggle for hediff-based prompt enchantments. When true, first-person diary prompts
        // may get one live health-condition hint weighted by DiaryPromptEnchantmentDefs.xml.
        public bool enablePromptEnchantments = true;
        // Disabled compatibility field. Old configs may have this set, but the Social-log injection
        // path is hidden and forced off because RimWorld accepts the row without reliably showing it.
        public bool injectGeneratedSpeechToPlayLog;
        // Optional saved overrides for the shared system prompts. Blank means "use the XML default"
        // from DiaryPromptDef.xml, so XML remains the restore source and template/final instructions
        // stay Def-owned.
        public string systemPromptOverride = string.Empty;
        public string systemPromptReflectionOverride = string.Empty;
        public string systemPromptNeutralOverride = string.Empty;
        public string titleSystemPromptOverride = string.Empty;
        // Optional saved overrides for the broad event-source prompt fields. Keys are
        // DiaryEventPromptDef.eventType values such as "Interaction" or "Raid"; blank means
        // "use the XML default" so Defs stay the canonical prompt catalog.
        public Dictionary<string, string> eventPromptOverrides = new Dictionary<string, string>();
        public Dictionary<string, string> eventEnhancementOverrides = new Dictionary<string, string>();
        // Player-facing multipliers for the two random entry gates:
        // work sampling and batched-social promotion. 1x preserves XML tuning defaults.
        public float workGenerationWeight = 1f;
        public float socialGenerationWeight = 1f;

        // Legacy per-interaction-group settings, keyed by InteractionGroup.defName. Event filtering
        // is now XML-only (DiaryInteractionGroupDef.defaultEnabled); this dictionary remains only so
        // old configs load without losing unrelated settings.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        // Persona preset edits made in settings: XML override rows plus user-created custom personas.
        public List<PersonaPresetConfig> personaPresets = new List<PersonaPresetConfig>();

        // Parallel lists used by Scribe_Collections for serializing the dictionaries (Unity's
        // serialization cannot handle Dictionary directly).
        private List<string> groupEnabledKeys;
        private List<bool> groupEnabledValues;
        private List<string> eventPromptOverrideKeys;
        private List<string> eventPromptOverrideValues;
        private List<string> eventEnhancementOverrideKeys;
        private List<string> eventEnhancementOverrideValues;

        // Default local LLM server endpoint (LM Studio/OpenAI-compatible local servers).
        public const string DefaultEndpointUrl = "http://localhost:1234/v1";
        // Default native Ollama endpoint used when a user switches a fresh row to Ollama mode.
        public const string DefaultOllamaEndpointUrl = "http://localhost:11434";
        // Placeholder model name; real value depends on the local server's loaded model.
        public const string DefaultModelName = "local-model";
        // Sentinel value stored in settings to mean "do not send a reasoning override".
        public const string DefaultReasoningEffort = "default";

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

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref apiEndpoints, "apiEndpoints", LookMode.Deep);
            Scribe_Values.Look(ref apiRoutingMode, "apiRoutingMode", ApiLaneRoutingMode.Balanced);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 30);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 4);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 100);
            Scribe_Values.Look(ref temperature, "temperature", 0.8f);
            Scribe_Values.Look(ref showApiSettings, "showApiSettings", true);
            Scribe_Values.Look(ref showPromptStudio, "showPromptStudio", true);
            Scribe_Values.Look(ref showPersonaSettings, "showPersonaSettings", false);
            Scribe_Values.Look(ref showLlmDebugInfo, "showLlmDebugInfo", false);
            Scribe_Values.Look(ref showGeneratingEntries, "showGeneratingEntries", false);
            Scribe_Values.Look(ref promptTestMode, "promptTestMode", false);
            Scribe_Values.Look(ref generateTitles, "generateTitles", true);
            Scribe_Values.Look(ref enableAtmosphericFormatting, "enableAtmosphericFormatting", true);
            Scribe_Values.Look(ref enablePromptEnchantments, "enablePromptEnchantments", true);
            Scribe_Values.Look(ref injectGeneratedSpeechToPlayLog, "injectGeneratedSpeechToPlayLog", false);
            Scribe_Values.Look(ref systemPromptOverride, "systemPromptOverride", string.Empty);
            Scribe_Values.Look(ref systemPromptReflectionOverride, "systemPromptReflectionOverride", string.Empty);
            Scribe_Values.Look(ref systemPromptNeutralOverride, "systemPromptNeutralOverride", string.Empty);
            Scribe_Values.Look(ref titleSystemPromptOverride, "titleSystemPromptOverride", string.Empty);
            Scribe_Collections.Look(ref eventPromptOverrides, "eventPromptOverrides", LookMode.Value, LookMode.Value, ref eventPromptOverrideKeys, ref eventPromptOverrideValues);
            Scribe_Collections.Look(ref eventEnhancementOverrides, "eventEnhancementOverrides", LookMode.Value, LookMode.Value, ref eventEnhancementOverrideKeys, ref eventEnhancementOverrideValues);
            Scribe_Values.Look(ref workGenerationWeight, "workGenerationWeight", 1f);
            Scribe_Values.Look(ref socialGenerationWeight, "socialGenerationWeight", 1f);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            Scribe_Collections.Look(ref personaPresets, "personaPresets", LookMode.Deep);

            ClampValues();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeEndpointUrls();
                DiaryPersonas.InvalidateCache();
            }
        }

        /// <summary>
        /// Resets the connection config to a single default API lane.
        /// </summary>
        public void ResetConnectionDefaults()
        {
            apiEndpoints = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig(DefaultEndpointUrl, string.Empty, DefaultModelName)
            };
        }

        // ---- System prompt helpers ----

        /// <summary>Returns the diary-entry system prompt, using a saved override when present.</summary>
        public string EffectiveSystemPrompt()
        {
            return PromptOverrideOrDefault(systemPromptOverride, DiaryPrompts.Current.systemPrompt);
        }

        /// <summary>Returns the end-of-day reflection system prompt, using a saved override when present.</summary>
        public string EffectiveReflectionSystemPrompt()
        {
            return PromptOverrideOrDefault(systemPromptReflectionOverride, DiaryPrompts.Current.systemPromptReflection);
        }

        /// <summary>Returns the neutral chronicle system prompt, using a saved override when present.</summary>
        public string EffectiveNeutralSystemPrompt()
        {
            return PromptOverrideOrDefault(systemPromptNeutralOverride, DiaryPrompts.Current.systemPromptNeutral);
        }

        /// <summary>Returns the title-generation system prompt, using a saved override when present.</summary>
        public string EffectiveTitleSystemPrompt()
        {
            return PromptOverrideOrDefault(titleSystemPromptOverride, DiaryPrompts.Current.titleSystemPrompt);
        }

        /// <summary>Stores or clears the diary-entry system prompt override.</summary>
        public void SetSystemPromptOverride(string prompt)
        {
            systemPromptOverride = NormalizePromptOverride(prompt, DiaryPrompts.Current.systemPrompt);
        }

        /// <summary>Stores or clears the reflection system prompt override.</summary>
        public void SetReflectionSystemPromptOverride(string prompt)
        {
            systemPromptReflectionOverride = NormalizePromptOverride(prompt, DiaryPrompts.Current.systemPromptReflection);
        }

        /// <summary>Stores or clears the neutral chronicle system prompt override.</summary>
        public void SetNeutralSystemPromptOverride(string prompt)
        {
            systemPromptNeutralOverride = NormalizePromptOverride(prompt, DiaryPrompts.Current.systemPromptNeutral);
        }

        /// <summary>Stores or clears the title-generation system prompt override.</summary>
        public void SetTitleSystemPromptOverride(string prompt)
        {
            titleSystemPromptOverride = NormalizePromptOverride(prompt, DiaryPrompts.Current.titleSystemPrompt);
        }

        /// <summary>Clears the diary-entry system prompt override so XML supplies the text again.</summary>
        public void ResetSystemPromptOverride()
        {
            systemPromptOverride = string.Empty;
        }

        /// <summary>Clears the reflection system prompt override so XML supplies the text again.</summary>
        public void ResetReflectionSystemPromptOverride()
        {
            systemPromptReflectionOverride = string.Empty;
        }

        /// <summary>Clears the neutral chronicle system prompt override so XML supplies the text again.</summary>
        public void ResetNeutralSystemPromptOverride()
        {
            systemPromptNeutralOverride = string.Empty;
        }

        /// <summary>Clears the title-generation system prompt override so XML supplies the text again.</summary>
        public void ResetTitleSystemPromptOverride()
        {
            titleSystemPromptOverride = string.Empty;
        }

        /// <summary>True when the diary-entry system prompt differs from the XML default.</summary>
        public bool HasSystemPromptOverride()
        {
            return !string.IsNullOrWhiteSpace(systemPromptOverride);
        }

        /// <summary>True when the reflection system prompt differs from the XML default.</summary>
        public bool HasReflectionSystemPromptOverride()
        {
            return !string.IsNullOrWhiteSpace(systemPromptReflectionOverride);
        }

        /// <summary>True when the neutral chronicle system prompt differs from the XML default.</summary>
        public bool HasNeutralSystemPromptOverride()
        {
            return !string.IsNullOrWhiteSpace(systemPromptNeutralOverride);
        }

        /// <summary>True when the title-generation system prompt differs from the XML default.</summary>
        public bool HasTitleSystemPromptOverride()
        {
            return !string.IsNullOrWhiteSpace(titleSystemPromptOverride);
        }

        private static string PromptOverrideOrDefault(string overrideText, string xmlDefault)
        {
            return string.IsNullOrWhiteSpace(overrideText) ? xmlDefault ?? string.Empty : overrideText;
        }

        private static string NormalizePromptOverride(string prompt, string xmlDefault)
        {
            string value = prompt ?? string.Empty;
            string defaultValue = xmlDefault ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, defaultValue, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return value;
        }

        // ---- Event prompt helpers ----

        /// <summary>Returns the event-type prompt, using a saved override when present.</summary>
        public string EffectiveEventPrompt(string eventType, string xmlDefault)
        {
            EnsureEventPromptDictionaries();
            return PromptOverrideOrDefault(EventOverrideFor(eventPromptOverrides, eventType), xmlDefault);
        }

        /// <summary>Returns the event-type enhancement, using a saved override when present.</summary>
        public string EffectiveEventEnhancement(string eventType, string xmlDefault)
        {
            EnsureEventPromptDictionaries();
            return PromptOverrideOrDefault(EventOverrideFor(eventEnhancementOverrides, eventType), xmlDefault);
        }

        /// <summary>Stores or clears the prompt override for one broad event source.</summary>
        public void SetEventPromptOverride(string eventType, string prompt, string xmlDefault)
        {
            EnsureEventPromptDictionaries();
            SetEventOverride(eventPromptOverrides, eventType, NormalizePromptOverride(prompt, xmlDefault));
        }

        /// <summary>Stores or clears the enhancement override for one broad event source.</summary>
        public void SetEventEnhancementOverride(string eventType, string enhancement, string xmlDefault)
        {
            EnsureEventPromptDictionaries();
            SetEventOverride(eventEnhancementOverrides, eventType, NormalizePromptOverride(enhancement, xmlDefault));
        }

        /// <summary>Clears one event prompt override so XML supplies the text again.</summary>
        public void ResetEventPromptOverride(string eventType)
        {
            EnsureEventPromptDictionaries();
            RemoveEventOverride(eventPromptOverrides, eventType);
        }

        /// <summary>Clears one event enhancement override so XML supplies the text again.</summary>
        public void ResetEventEnhancementOverride(string eventType)
        {
            EnsureEventPromptDictionaries();
            RemoveEventOverride(eventEnhancementOverrides, eventType);
        }

        /// <summary>Clears both saved event prompt dictionaries.</summary>
        public void ResetAllEventPromptOverrides()
        {
            EnsureEventPromptDictionaries();
            eventPromptOverrides.Clear();
            eventEnhancementOverrides.Clear();
        }

        /// <summary>True when the event prompt text differs from its XML default.</summary>
        public bool HasEventPromptOverride(string eventType)
        {
            EnsureEventPromptDictionaries();
            return !string.IsNullOrWhiteSpace(EventOverrideFor(eventPromptOverrides, eventType));
        }

        /// <summary>True when the event enhancement text differs from its XML default.</summary>
        public bool HasEventEnhancementOverride(string eventType)
        {
            EnsureEventPromptDictionaries();
            return !string.IsNullOrWhiteSpace(EventOverrideFor(eventEnhancementOverrides, eventType));
        }

        /// <summary>Counts event types with either prompt or enhancement text customized.</summary>
        public int CustomizedEventPromptCount()
        {
            EnsureEventPromptDictionaries();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddOverrideKeys(keys, eventPromptOverrides);
            AddOverrideKeys(keys, eventEnhancementOverrides);
            return keys.Count;
        }

        private void EnsureEventPromptDictionaries()
        {
            if (eventPromptOverrides == null)
            {
                eventPromptOverrides = new Dictionary<string, string>();
            }

            if (eventEnhancementOverrides == null)
            {
                eventEnhancementOverrides = new Dictionary<string, string>();
            }
        }

        private string EventOverrideFor(Dictionary<string, string> overrides, string eventType)
        {
            EnsureEventPromptDictionaries();
            string key = EventPromptKey(eventType);
            if (string.IsNullOrEmpty(key) || overrides == null)
            {
                return string.Empty;
            }

            string value;
            if (overrides.TryGetValue(key, out value))
            {
                return value ?? string.Empty;
            }

            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private void SetEventOverride(Dictionary<string, string> overrides, string eventType, string value)
        {
            EnsureEventPromptDictionaries();
            string key = EventPromptKey(eventType);
            if (string.IsNullOrEmpty(key) || overrides == null)
            {
                return;
            }

            RemoveEventOverride(overrides, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[key] = value;
            }
        }

        private void RemoveEventOverride(Dictionary<string, string> overrides, string eventType)
        {
            string key = EventPromptKey(eventType);
            if (string.IsNullOrEmpty(key) || overrides == null)
            {
                return;
            }

            List<string> keysToRemove = null;
            foreach (string existingKey in overrides.Keys)
            {
                if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (keysToRemove == null)
                    {
                        keysToRemove = new List<string>();
                    }

                    keysToRemove.Add(existingKey);
                }
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                overrides.Remove(keysToRemove[i]);
            }
        }

        private static string EventPromptKey(string eventType)
        {
            return (eventType ?? string.Empty).Trim();
        }

        private static void AddOverrideKeys(HashSet<string> keys, Dictionary<string, string> overrides)
        {
            if (keys == null || overrides == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    keys.Add(pair.Key.Trim());
                }
            }
        }

        private static void NormalizeEventOverrideDictionary(Dictionary<string, string> overrides)
        {
            if (overrides == null)
            {
                return;
            }

            List<string> keysToRemove = null;
            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    if (keysToRemove == null)
                    {
                        keysToRemove = new List<string>();
                    }

                    keysToRemove.Add(pair.Key);
                }
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                overrides.Remove(keysToRemove[i]);
            }
        }

        // ---- API endpoint helpers ----

        /// <summary>
        /// Guarantees <see cref="apiEndpoints"/> is non-null and each row has non-null editable
        /// fields. Does not normalize URLs because the settings UI calls this every frame while the
        /// user may be typing into a text field.
        /// </summary>
        public void EnsureEndpointsList()
        {
            apiRoutingMode = NormalizeRoutingMode(apiRoutingMode);

            if (apiEndpoints == null)
            {
                apiEndpoints = new List<ApiEndpointConfig>();
            }

            if (apiEndpoints.Count == 0)
            {
                apiEndpoints.Add(new ApiEndpointConfig(DefaultEndpointUrl, string.Empty, DefaultModelName));
            }
            foreach (ApiEndpointConfig endpoint in apiEndpoints)
            {
                if (endpoint == null)
                {
                    continue;
                }

                if (endpoint.url == null)
                {
                    endpoint.url = string.Empty;
                }

                if (endpoint.apiKey == null)
                {
                    endpoint.apiKey = string.Empty;
                }

                if (endpoint.model == null)
                {
                    endpoint.model = string.Empty;
                }

                endpoint.authMode = NormalizeAuthMode(endpoint.authMode);
                endpoint.reasoningEffort = NormalizeReasoningEffort(endpoint.reasoningEffort);
            }
        }

        /// <summary>
        /// Normalizes endpoint URL rows at load/save boundaries without editing the active text field
        /// every settings-frame while the user is typing.
        /// </summary>
        public void NormalizeEndpointUrls()
        {
            EnsureEndpointsList();
            foreach (ApiEndpointConfig endpoint in apiEndpoints)
            {
                if (endpoint != null)
                {
                    endpoint.url = EndpointUtility.NormalizeBaseEndpoint(endpoint.url);
                }
            }
        }

        /// <summary>
        /// Returns the API lanes usable for generation: enabled rows with both a URL and a model.
        /// A model is required ("force to pick a model"), so disabled or blank-model rows are skipped.
        /// </summary>
        public List<ApiEndpointConfig> ActiveEndpoints()
        {
            EnsureEndpointsList();

            List<ApiEndpointConfig> active = new List<ApiEndpointConfig>();
            foreach (ApiEndpointConfig endpoint in apiEndpoints)
            {
                if (endpoint != null
                    && endpoint.enabled
                    && !string.IsNullOrWhiteSpace(endpoint.url)
                    && !string.IsNullOrWhiteSpace(endpoint.model))
                {
                    active.Add(endpoint);
                }
            }

            return active;
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

        /// <summary>Normalizes invalid routing enum values loaded from hand-edited settings.</summary>
        public static ApiLaneRoutingMode NormalizeRoutingMode(ApiLaneRoutingMode mode)
        {
            return ApiLaneSelector.Normalize(mode);
        }

        /// <summary>Normalizes invalid auth enum values loaded from hand-edited settings.</summary>
        public static ApiAuthMode NormalizeAuthMode(ApiAuthMode mode)
        {
            return ApiEndpointPolicy.NormalizeAuthMode(mode);
        }

        // ---- Interaction group helpers ----

        /// <summary>
        /// Determines whether an interaction should be recorded by checking if its group is enabled.
        /// </summary>
        // Whether an interaction should be recorded at all (its group is enabled).
        public bool IsInteractionEnabled(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return false;
            }

            // Classify only returns null if the group catalog (XML Defs) failed to load; treat
            // that as "not recorded" rather than crashing on every interaction.
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the diary prompt instruction for an interaction, using the group's
        /// override if set, otherwise the group's default instruction.
        /// </summary>
        // The diary prompt instruction for an interaction (its group's override or default).
        public string InstructionFor(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.Classify(interactionDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for mental states (social fights, mental breaks).
        /// </summary>
        // Mental-state equivalents (social fights, mental breaks).
        public bool IsMentalStateEnabled(MentalStateDef stateDef)
        {
            if (stateDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyMentalState(stateDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a mental state's diary entry,
        /// falling back to the group's default if no override is set.
        /// </summary>
        public string InstructionForMentalState(MentalStateDef stateDef)
        {
            if (stateDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyMentalState(stateDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for RimWorld tales (notable history events such as
        /// deaths, injuries, recruitment, research, disasters, and other non-social events).
        /// </summary>
        public bool IsTaleEnabled(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyTale(taleDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a TaleDef's diary entry,
        /// falling back to the group's default if no override is set.
        /// </summary>
        public string InstructionForTale(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyTale(taleDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for mood-affecting GameConditions (aurora,
        /// eclipse, psychic drone, toxic fallout, etc.).
        /// </summary>
        public bool IsMoodEventEnabled(GameConditionDef conditionDef)
        {
            if (conditionDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyMoodEvent(conditionDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a mood event's diary entry,
        /// falling back to the group's default if no override is set.
        /// </summary>
        public string InstructionForMoodEvent(GameConditionDef conditionDef)
        {
            if (conditionDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyMoodEvent(conditionDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for ThoughtDefs with expiration (positive/negative mood thoughts).
        /// </summary>
        public bool IsThoughtEnabled(ThoughtDef thoughtDef)
        {
            if (thoughtDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyThought(thoughtDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a thought's diary entry,
        /// falling back to the group's default if no override is set.
        /// </summary>
        public string InstructionForThought(ThoughtDef thoughtDef)
        {
            if (thoughtDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyThought(thoughtDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for InspirationDefs when a pawn gains an inspiration.
        /// </summary>
        public bool IsInspirationEnabled(InspirationDef inspirationDef)
        {
            if (inspirationDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyInspiration(inspirationDef);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for an inspiration diary entry,
        /// falling back to the group's default if no override is set.
        /// </summary>
        public string InstructionForInspiration(InspirationDef inspirationDef)
        {
            if (inspirationDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyInspiration(inspirationDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for synthetic work events emitted by the work scanner.
        /// The scanner picks the group first (passion, strain, routine, dark study), because those
        /// groups depend on pawn state as well as the WorkTypeDef.
        /// </summary>
        public bool IsWorkEnabled(DiaryInteractionGroupDef group)
        {
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a work diary entry.
        /// </summary>
        public string InstructionForWork(DiaryInteractionGroupDef group)
        {
            return InstructionForGroup(group);
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for HediffDefs recorded by the generic health-signal
        /// layer. Mod compatibility XML can add Hediff-domain groups; saved settings still use the
        /// shared per-group dictionary.
        /// </summary>
        public bool IsHediffEnabled(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyHediff(hediffDef);
            return group != null && group.HasHediffPolicy && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a hediff diary entry, falling back to the
        /// group's XML default if no player override is set.
        /// </summary>
        public string InstructionForHediff(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyHediff(hediffDef));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for raid incidents (RaidEnemy/RaidFriendly/RaidBeacon).
        /// Classifies by incident defName into the Raid domain; the catch-all "Raids" group makes
        /// every raid recordable by default.
        /// </summary>
        public bool IsRaidEnabled(string incidentDefName)
        {
            if (string.IsNullOrEmpty(incidentDefName))
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRaid(incidentDefName);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a raid diary entry (group's XML default).
        /// </summary>
        public string InstructionForRaid(string incidentDefName)
        {
            if (string.IsNullOrEmpty(incidentDefName))
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyRaid(incidentDefName));
        }

        /// <summary>
        /// Same as IsInteractionEnabled but for quest lifecycle signals. The signal ("accepted",
        /// "completed", "failed") is the classifier key — each maps to its own Quest group, so a
        /// player could disable just failed-quest entries by turning that group off in XML.
        /// </summary>
        public bool IsQuestEnabled(string signal)
        {
            if (string.IsNullOrEmpty(signal))
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyQuest(signal);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a quest diary entry (group's XML default).
        /// </summary>
        public string InstructionForQuest(string signal)
        {
            if (string.IsNullOrEmpty(signal))
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyQuest(signal));
        }

        /// <summary>
        /// Checks whether an interaction group is enabled. Event filters are XML-only now, so saved
        /// groupEnabled values from older settings files are ignored.
        /// </summary>
        public bool IsGroupEnabled(string groupKey)
        {
            return InteractionGroups.ByKey(groupKey)?.defaultEnabled ?? false;
        }

        /// <summary>
        /// Obsolete compatibility shim. Event filters are XML-only.
        /// </summary>
        public void SetGroupEnabled(string groupKey, bool enabled)
        {
        }

        /// <summary>
        /// Returns the effective prompt instruction for a group. When the group defines an
        /// <see cref="DiaryInteractionGroupDef.instructions"/> variant pool, one wording is rolled
        /// per call (capture-time, so the result is persisted on the DiaryEvent); otherwise the
        /// singular <see cref="DiaryInteractionGroupDef.instruction"/> fallback is used. Prompt
        /// wording is XML-only so tuning stays in Defs instead of save settings.
        /// </summary>
        public string InstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            // Rand is RimWorld's main-thread RNG; this is called only from the capture path that
            // freezes the result into diaryEvent.instruction, so a fresh roll per event is correct.
            // The settings preview uses EditableInstructionForGroup (singular) to avoid flicker.
            return PromptVariants.Pick(group.instructions, group.instruction, Rand.Range(0, int.MaxValue));
        }

        /// <summary>
        /// Returns the XML instruction text for settings preview. It is no longer editable in saves.
        /// </summary>
        public string EditableInstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            return group.instruction;
        }

        /// <summary>
        /// Obsolete compatibility shim. Prompt instructions are XML-only.
        /// </summary>
        public void SetGroupInstruction(string groupKey, string instruction)
        {
        }

        /// <summary>
        /// Obsolete compatibility shim. Prompt instructions are XML-only.
        /// </summary>
        public void ResetGroupInstruction(string groupKey)
        {
        }

        /// <summary>
        /// Clears all per-group overrides, returning every group to its default state.
        /// </summary>
        public void ResetAllGroups()
        {
            EnsureGroupDictionaries();
            groupEnabled.Clear();
        }

        /// <summary>
        /// Clamps all numeric and connection fields to safe ranges.
        /// Called after loading to guard against corrupted or outdated saves.
        /// </summary>
        public void ClampValues()
        {
            EnsureGroupDictionaries();
            EnsureEventPromptDictionaries();
            EnsurePersonaPresetList();

            EnsureEndpointsList();

            apiRoutingMode = NormalizeRoutingMode(apiRoutingMode);
            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 300);
            maxConcurrentRequests = Mathf.Clamp(maxConcurrentRequests, 1, 16);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
            injectGeneratedSpeechToPlayLog = false;
            systemPromptOverride = systemPromptOverride ?? string.Empty;
            systemPromptReflectionOverride = systemPromptReflectionOverride ?? string.Empty;
            systemPromptNeutralOverride = systemPromptNeutralOverride ?? string.Empty;
            titleSystemPromptOverride = titleSystemPromptOverride ?? string.Empty;
            NormalizeEventOverrideDictionary(eventPromptOverrides);
            NormalizeEventOverrideDictionary(eventEnhancementOverrides);
            workGenerationWeight = Mathf.Clamp(workGenerationWeight, 0f, 5f);
            socialGenerationWeight = Mathf.Clamp(socialGenerationWeight, 0f, 5f);
            NormalizePersonaPresets();
        }

        /// <summary>
        /// Ensures the group dictionaries are non-null (defensive against deserialization gaps).
        /// </summary>
        private void EnsureGroupDictionaries()
        {
            if (groupEnabled == null)
            {
                groupEnabled = new Dictionary<string, bool>();
            }
            else
            {
                groupEnabled.Clear();
            }
        }

        /// <summary>
        /// Ensures the persona preset edit list is non-null (defensive against deserialization gaps).
        /// </summary>
        public void EnsurePersonaPresetList()
        {
            if (personaPresets == null)
            {
                personaPresets = new List<PersonaPresetConfig>();
            }
        }

        /// <summary>
        /// Finds an override row for an XML persona Def by defName.
        /// </summary>
        public PersonaPresetConfig PersonaOverrideFor(string defName)
        {
            EnsurePersonaPresetList();
            return personaPresets.FirstOrDefault(preset =>
                preset != null
                && !preset.custom
                && preset.defName == defName);
        }

        /// <summary>
        /// Finds a custom persona row by defName.
        /// </summary>
        public PersonaPresetConfig CustomPersonaFor(string defName)
        {
            EnsurePersonaPresetList();
            return personaPresets.FirstOrDefault(preset =>
                preset != null
                && preset.custom
                && preset.defName == defName);
        }

        /// <summary>
        /// Returns only user-created personas (custom=true) for catalog merging and UI.
        /// </summary>
        public List<PersonaPresetConfig> CustomPersonas()
        {
            EnsurePersonaPresetList();
            return personaPresets
                .Where(preset => preset != null && preset.custom)
                .ToList();
        }

        /// <summary>
        /// Upserts an override row for an XML persona Def.
        /// </summary>
        public void SetPersonaOverride(string defName, string label, string rule, IEnumerable<string> themes)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            EnsurePersonaPresetList();
            PersonaPresetConfig existing = PersonaOverrideFor(defName);
            if (existing == null)
            {
                existing = new PersonaPresetConfig(
                    defName,
                    label,
                    rule,
                    themes,
                    false);
                personaPresets.Add(existing);
            }
            else
            {
                existing.label = label ?? string.Empty;
                existing.rule = rule ?? string.Empty;
                existing.themes = NormalizeThemes(themes);
                existing.custom = false;
            }

            DiaryPersonas.InvalidateCache();
        }

        /// <summary>
        /// Removes an override row for an XML persona Def, restoring XML defaults.
        /// </summary>
        public void ResetPersonaOverride(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || personaPresets == null)
            {
                return;
            }

            if (personaPresets.RemoveAll(preset => preset != null && !preset.custom && preset.defName == defName) > 0)
            {
                DiaryPersonas.InvalidateCache();
            }
        }

        /// <summary>
        /// Adds a new custom persona row and returns its generated defName.
        /// </summary>
        public string AddCustomPersona()
        {
            EnsurePersonaPresetList();
            string defName = NextCustomPersonaDefName();
            personaPresets.Add(new PersonaPresetConfig(
                defName,
                "PawnDiary.Settings.NewPersonaLabel".Translate().Resolve(),
                string.Empty,
                new[] { DiaryPersonas.PredefinedThemeTags[0] },
                true));
            DiaryPersonas.InvalidateCache();
            return defName;
        }

        /// <summary>
        /// Deletes one user-created persona row.
        /// </summary>
        public void RemoveCustomPersona(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || personaPresets == null)
            {
                return;
            }

            if (personaPresets.RemoveAll(preset => preset != null && preset.custom && preset.defName == defName) > 0)
            {
                DiaryPersonas.InvalidateCache();
            }
        }

        /// <summary>
        /// Removes all persona overrides and custom personas, restoring pure XML persona defs.
        /// </summary>
        public void ResetPersonaPresets()
        {
            EnsurePersonaPresetList();
            personaPresets.Clear();
            DiaryPersonas.InvalidateCache();
        }

        // Generates deterministic custom persona keys so they are stable across saves and merges.
        private string NextCustomPersonaDefName()
        {
            const string prefix = "DiaryPersona_Custom_";
            int next = 1;
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);

            List<DiaryPersonaDef> defs = DefDatabase<DiaryPersonaDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(defs[i]?.defName))
                    {
                        used.Add(defs[i].defName);
                    }
                }
            }

            for (int i = 0; i < personaPresets.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(personaPresets[i]?.defName))
                {
                    used.Add(personaPresets[i].defName);
                }
            }

            while (used.Contains(prefix + next))
            {
                next++;
            }

            return prefix + next;
        }

        // Keeps persona preset edits safe and deterministic after load:
        // - strips invalid/unknown tags
        // - guarantees custom personas keep at least one predefined tag
        // - drops malformed rows and duplicate keys
        private void NormalizePersonaPresets()
        {
            EnsurePersonaPresetList();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<PersonaPresetConfig> normalized = new List<PersonaPresetConfig>();
            for (int i = 0; i < personaPresets.Count; i++)
            {
                PersonaPresetConfig preset = personaPresets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.defName))
                {
                    continue;
                }

                if (!seen.Add(preset.defName))
                {
                    continue;
                }

                preset.label = preset.label ?? string.Empty;
                preset.rule = preset.rule ?? string.Empty;

                if (preset.themes == null)
                {
                    preset.themes = new List<string>();
                }

                preset.themes = NormalizeThemes(preset.themes);

                if (preset.custom && preset.themes.Count == 0)
                {
                    preset.themes.Add(DiaryPersonas.PredefinedThemeTags[0]);
                }

                normalized.Add(preset);
            }

            personaPresets = normalized;
        }

        internal static List<string> NormalizeThemes(IEnumerable<string> themes)
        {
            HashSet<string> allowedTags = new HashSet<string>(DiaryPersonas.PredefinedThemeTags, StringComparer.Ordinal);
            return themes == null
                ? new List<string>()
                : themes
                    .Where(theme => !string.IsNullOrWhiteSpace(theme))
                    .Select(theme => theme.Trim().ToLowerInvariant())
                    .Where(theme => allowedTags.Contains(theme))
                    .Distinct()
                    .ToList();
        }
    }
}
