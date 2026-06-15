// All mod settings (connection, generation options, and per-group enable/instruction overrides)
// plus value clamping and save/load. ExposeData persists them — see AGENTS.md ("IExposable").
// The group catalog itself now lives in XML Defs (see InteractionGroups.cs); this file only
// stores the player's per-group overrides, keyed by group defName.
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One configured API "lane": a single OpenAI-compatible endpoint, its optional key, and the
    /// one model it serves. Many of these can be listed (see <see cref="PawnDiarySettings.apiEndpoints"/>)
    /// so diary generation is spread across them in parallel. We keep it to one model per row on
    /// purpose — to use several models you just add several rows (possibly sharing an endpoint).
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it — see AGENTS.md ("IExposable").
    /// </summary>
    public class ApiEndpointConfig : IExposable
    {
        // Base URL of the OpenAI-compatible chat completions endpoint (e.g. http://localhost:1234/v1).
        public string url = PawnDiarySettings.DefaultEndpointUrl;
        // Model name sent in the request payload. Required — a row with no model is ignored.
        public string model = string.Empty;
        // API key (may be empty for local models that don't require auth).
        public string apiKey = string.Empty;
        // When false, keep this row configured but exclude it from generation and failover.
        public bool enabled = true;

        public ApiEndpointConfig()
        {
        }

        public ApiEndpointConfig(string url, string apiKey, string model)
        {
            this.url = url;
            this.apiKey = apiKey;
            this.model = model;
        }

        // Reads/writes the row fields on save and load (Scribe is RimWorld's serializer).
        public void ExposeData()
        {
            Scribe_Values.Look(ref url, "url", PawnDiarySettings.DefaultEndpointUrl);
            Scribe_Values.Look(ref model, "model", string.Empty);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref enabled, "enabled", true);
        }
    }

    public class PawnDiarySettings : ModSettings
    {
        // Master toggle: when false, no LLM requests are made (events are still recorded).
        public bool enableLlm = true;
        // The configured API lanes used for generation. Requests are distributed across these
        // round-robin and run in parallel (one in-flight request per lane, see LlmClient).
        // Seeded from the legacy single-endpoint fields below the first time it's empty.
        public List<ApiEndpointConfig> apiEndpoints = new List<ApiEndpointConfig>();

        // ---- Legacy single-endpoint fields (kept for migration from older saves) ----
        // Base URL of the OpenAI-compatible chat completions endpoint.
        public string endpointUrl = DefaultEndpointUrl;
        // Model name sent in the request payload.
        public string modelName = DefaultModelName;
        // API key (may be empty for local models that don't require auth).
        public string apiKey = string.Empty;

        // If true, sends the API key as a Bearer token; otherwise includes it as a query parameter.
        public bool sendApiKeyAsBearerToken = true;
        // Per-request timeout in seconds before the request is cancelled.
        public int timeoutSeconds = 30;
        // Maximum number of in-flight LLM requests to avoid overwhelming local servers.
        public int maxConcurrentRequests = 4;
        // Token cap on each completion response to keep diary entries concise.
        // Reduced from 160 to 100 for faster generation on small local models (6B–31B).
        public int maxTokens = 100;
        // Sampling temperature — higher values produce more creative/varied entries.
        public float temperature = 0.8f;
        // When true, raw game-text entries are kept even if LLM generation fails, so no history is lost.
        public bool keepRawEntryOnFailure = true;
        // When true, generates diary text from both the initiator's and recipient's perspectives.
        public bool dualPovGeneration = true;
        // UI preference: when false, the compact API/model setup block is collapsed in mod settings.
        public bool showApiSettings = true;
        // Dev-mode UI preference: shows the per-pawn persona picker in the Diary inspector tab.
        public bool showPersonaSettings = false;
        // Dev-mode UI preference: shows raw/pending entries and the LLM prompt/status diagnostic block.
        public bool showLlmDebugInfo = false;
        // System message sent with each LLM request to set the model's behavior.
        public string systemPrompt = DefaultSystemPrompt;

        // Per interaction-group settings, keyed by InteractionGroup.defName.
        // groupEnabled: whether interactions in the group are recorded at all.
        // groupInstructions: optional override of the group's default diary prompt.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        public Dictionary<string, string> groupInstructions = new Dictionary<string, string>();

        // Parallel lists used by Scribe_Collections for serializing the dictionaries (Unity's
        // serialization cannot handle Dictionary directly).
        private List<string> groupEnabledKeys;
        private List<bool> groupEnabledValues;
        private List<string> groupInstructionKeys;
        private List<string> groupInstructionValues;

        // Default local LLM server endpoint (LM Studio / Ollama typical port).
        public const string DefaultEndpointUrl = "http://localhost:1234/v1";
        // Placeholder model name; real value depends on the local server's loaded model.
        public const string DefaultModelName = "local-model";

        // Default comes from the DiaryPromptDef XML (editable without recompiling). If the Def
        // isn't loaded yet during early startup, the field initializer in DiaryPromptDef
        // provides the same hardcoded text as a fallback.
        public static string DefaultSystemPrompt => DiaryPrompts.Current.systemPrompt;

        public override void ExposeData()
        {
            string legacyChatCompletionsUrl = null;

            Scribe_Values.Look(ref enableLlm, "enableLlm", true);
            Scribe_Collections.Look(ref apiEndpoints, "apiEndpoints", LookMode.Deep);
            Scribe_Values.Look(ref endpointUrl, "endpointUrl", DefaultEndpointUrl);
            Scribe_Values.Look(ref legacyChatCompletionsUrl, "chatCompletionsUrl", null);
            Scribe_Values.Look(ref modelName, "modelName", DefaultModelName);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref sendApiKeyAsBearerToken, "sendApiKeyAsBearerToken", true);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 30);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 4);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 100);
            Scribe_Values.Look(ref temperature, "temperature", 0.8f);
            Scribe_Values.Look(ref keepRawEntryOnFailure, "keepRawEntryOnFailure", true);
            Scribe_Values.Look(ref dualPovGeneration, "dualPovGeneration", true);
            Scribe_Values.Look(ref showApiSettings, "showApiSettings", true);
            Scribe_Values.Look(ref showPersonaSettings, "showPersonaSettings", false);
            Scribe_Values.Look(ref showLlmDebugInfo, "showLlmDebugInfo", false);
            Scribe_Values.Look(ref systemPrompt, "systemPrompt", DefaultSystemPrompt);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            Scribe_Collections.Look(ref groupInstructions, "interactionGroupInstructions", LookMode.Value, LookMode.Value, ref groupInstructionKeys, ref groupInstructionValues);

            if (!string.IsNullOrWhiteSpace(legacyChatCompletionsUrl) && endpointUrl == DefaultEndpointUrl)
            {
                endpointUrl = EndpointUtility.NormalizeBaseEndpoint(legacyChatCompletionsUrl);
            }

            ClampValues();
        }

        /// <summary>
        /// Resets the connection config to a single default API lane (and clears the legacy fields).
        /// </summary>
        public void ResetConnectionDefaults()
        {
            endpointUrl = DefaultEndpointUrl;
            modelName = DefaultModelName;
            apiKey = string.Empty;

            apiEndpoints = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig(DefaultEndpointUrl, string.Empty, DefaultModelName)
            };
        }

        // ---- API endpoint helpers ----

        /// <summary>
        /// Guarantees <see cref="apiEndpoints"/> is non-null, migrates a legacy single-endpoint
        /// config into the list the first time it is empty, and normalizes each row's URL.
        /// Public so the settings UI can call it before drawing the first frame.
        /// </summary>
        public void EnsureEndpointsList()
        {
            if (apiEndpoints == null)
            {
                apiEndpoints = new List<ApiEndpointConfig>();
            }

            // Migrate: older saves stored a single endpoint/model/key. Seed the list from those so
            // existing users keep their configured API with no action needed.
            if (apiEndpoints.Count == 0)
            {
                apiEndpoints.Add(new ApiEndpointConfig(
                    string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpointUrl : endpointUrl,
                    apiKey ?? string.Empty,
                    string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName));
            }

            // Normalize each row's URL the same way the legacy single endpoint is normalized.
            foreach (ApiEndpointConfig endpoint in apiEndpoints)
            {
                if (endpoint == null)
                {
                    continue;
                }

                endpoint.url = EndpointUtility.NormalizeBaseEndpoint(endpoint.url);
                if (endpoint.apiKey == null)
                {
                    endpoint.apiKey = string.Empty;
                }

                if (endpoint.model == null)
                {
                    endpoint.model = string.Empty;
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
        /// Checks whether an interaction group is enabled, falling back to the group's
        /// defaultEnabled if no player override exists.
        /// </summary>
        public bool IsGroupEnabled(string groupKey)
        {
            EnsureGroupDictionaries();

            bool enabled;
            if (groupEnabled.TryGetValue(groupKey, out enabled))
            {
                return enabled;
            }

            return InteractionGroups.ByKey(groupKey)?.defaultEnabled ?? false;
        }

        /// <summary>
        /// Stores a player override for whether an interaction group is enabled.
        /// </summary>
        public void SetGroupEnabled(string groupKey, bool enabled)
        {
            EnsureGroupDictionaries();
            groupEnabled[groupKey] = enabled;
        }

        /// <summary>
        /// Returns the effective prompt instruction for a group: the player's override if
        /// set and non-blank, otherwise the group's XML-defined default.
        /// </summary>
        public string InstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.defName, out instruction) && !string.IsNullOrWhiteSpace(instruction))
            {
                return instruction.Trim();
            }

            return group.instruction;
        }

        /// <summary>
        /// Returns the instruction text as-is for editing in the settings UI. Unlike
        /// InstructionForGroup, this returns the stored string even if blank so the
        /// player can see and edit an empty override.
        /// </summary>
        public string EditableInstructionForGroup(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            EnsureGroupDictionaries();

            string instruction;
            if (groupInstructions.TryGetValue(group.defName, out instruction))
            {
                return instruction;
            }

            return group.instruction;
        }

        /// <summary>
        /// Stores a player override instruction for a group.
        /// </summary>
        public void SetGroupInstruction(string groupKey, string instruction)
        {
            EnsureGroupDictionaries();
            groupInstructions[groupKey] = instruction ?? string.Empty;
        }

        /// <summary>
        /// Removes the player's instruction override so the group falls back to its XML default.
        /// </summary>
        public void ResetGroupInstruction(string groupKey)
        {
            if (groupInstructions != null)
            {
                groupInstructions.Remove(groupKey);
            }
        }

        /// <summary>
        /// Clears all per-group overrides, returning every group to its default state.
        /// </summary>
        public void ResetAllGroups()
        {
            EnsureGroupDictionaries();
            groupEnabled.Clear();
            groupInstructions.Clear();
        }

        /// <summary>
        /// Clamps all numeric and connection fields to safe ranges, and normalizes the endpoint URL.
        /// Called after loading to guard against corrupted or outdated saves.
        /// </summary>
        public void ClampValues()
        {
            enableLlm = true;
            keepRawEntryOnFailure = true;
            sendApiKeyAsBearerToken = true;

            EnsureGroupDictionaries();

            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                endpointUrl = DefaultEndpointUrl;
            }

            endpointUrl = EndpointUtility.NormalizeBaseEndpoint(endpointUrl);

            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = DefaultModelName;
            }

            EnsureEndpointsList();

            if (systemPrompt == null)
            {
                systemPrompt = string.Empty;
            }

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 300);
            maxConcurrentRequests = Mathf.Clamp(maxConcurrentRequests, 1, 16);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
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

            if (groupInstructions == null)
            {
                groupInstructions = new Dictionary<string, string>();
            }
        }
    }
}
