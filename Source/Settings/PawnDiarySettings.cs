// All mod settings (connection, generation options, prompt-text overrides, and per-group
// enable/instruction overrides) plus value clamping and save/load. ExposeData persists them — see
// AGENTS.md ("IExposable"). The group catalog itself now lives in XML Defs (see
// InteractionGroups.cs); this file only stores the player's per-group overrides, keyed by
// group defName.
using System;
using System.Collections.Generic;
using System.Linq;
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
        // Master toggle: when false, no LLM requests are made (events are still recorded).
        public bool enableLlm = true;
        // The configured API lanes used for generation. Requests are distributed across these
        // round-robin and run in parallel (one in-flight request per lane, see LlmClient).
        public List<ApiEndpointConfig> apiEndpoints = new List<ApiEndpointConfig>();

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
        // UI preference: when false, the compact API/model setup block is collapsed in mod settings.
        public bool showApiSettings = true;
        // Dev-mode UI preference: shows the per-pawn persona picker in the Diary inspector tab.
        public bool showPersonaSettings = false;
        // Dev-mode UI preference: shows raw/pending entries and the LLM prompt/status diagnostic block.
        public bool showLlmDebugInfo = false;
        // Dev-mode UI preference: reveals entries still in the generation pipeline (in-progress or
        // stuck on "writing...") in the pawn Diary tab, without the full LLM diagnostic block. Lets a
        // player see which events never finished generating. Normal mode always hides them.
        public bool showGeneratingEntries = false;
        // System messages sent with each LLM request to set the model's behavior. One per narrative
        // mode; the request's event type chooses which (see DiaryGameComponent.Generation.cs).
        // systemPrompt = first-person diary voice; Reflection = first-person end-of-day reflection;
        // Neutral = third-person factual chronicle (death/arrival descriptions).
        public string systemPrompt = DefaultSystemPrompt;
        public string systemPromptReflection = DefaultSystemPromptReflection;
        public string systemPromptNeutral = DefaultSystemPromptNeutral;
        // Title generation: short chat-style subject (3-8 words) for an existing diary entry.
        // Used only by the title follow-up flow; main entries never send this prompt.
        public string systemPromptTitle = DefaultSystemPromptTitle;
        // User-message prompt text appended after the structured context for the main diary flows.
        public string singlePovInstruction = DefaultSinglePovInstruction;
        public string recipientFollowupInstruction = DefaultRecipientFollowupInstruction;
        // Neutral user-message prompt text for one-off chronicled events.
        public string deathDescriptionInstruction = DefaultDeathDescriptionInstruction;
        public string arrivalDescriptionInstruction = DefaultArrivalDescriptionInstruction;
        // User-message suffix for the separate title-generation follow-up call.
        public string titleUserInstruction = DefaultTitleUserInstruction;

        // Master toggle for the LLM-titling flow. When false, no extra title call is made and
        // diary card headers stay date-only.
        public bool generateTitles = true;
        // Player-facing multipliers for the two random entry gates:
        // work sampling and batched-social promotion. 1x preserves XML tuning defaults.
        public float workGenerationWeight = 1f;
        public float socialGenerationWeight = 1f;

        // Per interaction-group settings, keyed by InteractionGroup.defName.
        // groupEnabled: whether interactions in the group are recorded at all.
        // groupInstructions: optional override of the group's default diary prompt.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        public Dictionary<string, string> groupInstructions = new Dictionary<string, string>();
        // Persona preset edits made in settings: XML override rows plus user-created custom personas.
        public List<PersonaPresetConfig> personaPresets = new List<PersonaPresetConfig>();

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
        public static string DefaultSystemPromptReflection => DiaryPrompts.Current.systemPromptReflection;
        public static string DefaultSystemPromptNeutral => DiaryPrompts.Current.systemPromptNeutral;
        public static string DefaultSystemPromptTitle => DiaryPrompts.Current.titleSystemPrompt;
        public static string DefaultSinglePovInstruction => DiaryPrompts.Current.singlePovInstruction;
        public static string DefaultRecipientFollowupInstruction => DiaryPrompts.Current.recipientFollowupInstruction;
        public static string DefaultDeathDescriptionInstruction => DiaryPrompts.Current.deathDescriptionInstruction;
        public static string DefaultArrivalDescriptionInstruction => DiaryPrompts.Current.arrivalDescriptionInstruction;
        public static string DefaultTitleUserInstruction => DiaryPrompts.Current.titleUserInstruction;

        // Static helpers so generation code can read the live settings safely even before a settings
        // window has ever been opened. Blank strings are intentional player choices; only null falls
        // back to the XML-defined default.
        public static string CurrentSinglePovInstruction => PawnDiaryMod.Settings?.singlePovInstruction ?? DefaultSinglePovInstruction;
        public static string CurrentRecipientFollowupInstruction => PawnDiaryMod.Settings?.recipientFollowupInstruction ?? DefaultRecipientFollowupInstruction;
        public static string CurrentDeathDescriptionInstruction => PawnDiaryMod.Settings?.deathDescriptionInstruction ?? DefaultDeathDescriptionInstruction;
        public static string CurrentArrivalDescriptionInstruction => PawnDiaryMod.Settings?.arrivalDescriptionInstruction ?? DefaultArrivalDescriptionInstruction;
        public static string CurrentTitleUserInstruction => PawnDiaryMod.Settings?.titleUserInstruction ?? DefaultTitleUserInstruction;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableLlm, "enableLlm", true);
            Scribe_Collections.Look(ref apiEndpoints, "apiEndpoints", LookMode.Deep);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 30);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 4);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 100);
            Scribe_Values.Look(ref temperature, "temperature", 0.8f);
            Scribe_Values.Look(ref keepRawEntryOnFailure, "keepRawEntryOnFailure", true);
            Scribe_Values.Look(ref showApiSettings, "showApiSettings", true);
            Scribe_Values.Look(ref showPersonaSettings, "showPersonaSettings", false);
            Scribe_Values.Look(ref showLlmDebugInfo, "showLlmDebugInfo", false);
            Scribe_Values.Look(ref showGeneratingEntries, "showGeneratingEntries", false);
            Scribe_Values.Look(ref systemPrompt, "systemPrompt", DefaultSystemPrompt);
            Scribe_Values.Look(ref systemPromptReflection, "systemPromptReflection", DefaultSystemPromptReflection);
            Scribe_Values.Look(ref systemPromptNeutral, "systemPromptNeutral", DefaultSystemPromptNeutral);
            Scribe_Values.Look(ref systemPromptTitle, "systemPromptTitle", DefaultSystemPromptTitle);
            Scribe_Values.Look(ref singlePovInstruction, "singlePovInstruction", DefaultSinglePovInstruction);
            Scribe_Values.Look(ref recipientFollowupInstruction, "recipientFollowupInstruction", DefaultRecipientFollowupInstruction);
            Scribe_Values.Look(ref deathDescriptionInstruction, "deathDescriptionInstruction", DefaultDeathDescriptionInstruction);
            Scribe_Values.Look(ref arrivalDescriptionInstruction, "arrivalDescriptionInstruction", DefaultArrivalDescriptionInstruction);
            Scribe_Values.Look(ref titleUserInstruction, "titleUserInstruction", DefaultTitleUserInstruction);
            Scribe_Values.Look(ref generateTitles, "generateTitles", true);
            Scribe_Values.Look(ref workGenerationWeight, "workGenerationWeight", 1f);
            Scribe_Values.Look(ref socialGenerationWeight, "socialGenerationWeight", 1f);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            Scribe_Collections.Look(ref groupInstructions, "interactionGroupInstructions", LookMode.Value, LookMode.Value, ref groupInstructionKeys, ref groupInstructionValues);
            Scribe_Collections.Look(ref personaPresets, "personaPresets", LookMode.Deep);

            ClampValues();
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

        /// <summary>
        /// Restores every prompt-text field back to the current XML-defined defaults.
        /// This leaves event-group overrides alone because they are tuned separately.
        /// </summary>
        public void ResetPromptTextDefaults()
        {
            systemPrompt = DefaultSystemPrompt;
            systemPromptReflection = DefaultSystemPromptReflection;
            systemPromptNeutral = DefaultSystemPromptNeutral;
            systemPromptTitle = DefaultSystemPromptTitle;
            singlePovInstruction = DefaultSinglePovInstruction;
            recipientFollowupInstruction = DefaultRecipientFollowupInstruction;
            deathDescriptionInstruction = DefaultDeathDescriptionInstruction;
            arrivalDescriptionInstruction = DefaultArrivalDescriptionInstruction;
            titleUserInstruction = DefaultTitleUserInstruction;
        }

        // ---- API endpoint helpers ----

        /// <summary>
        /// Guarantees <see cref="apiEndpoints"/> is non-null and normalizes each row's URL.
        /// Public so the settings UI can call it before drawing the first frame.
        /// </summary>
        public void EnsureEndpointsList()
        {
            if (apiEndpoints == null)
            {
                apiEndpoints = new List<ApiEndpointConfig>();
            }

            if (apiEndpoints.Count == 0)
            {
                apiEndpoints.Add(new ApiEndpointConfig(DefaultEndpointUrl, string.Empty, DefaultModelName));
            }

            // Normalize each row's URL before it is used for /models or /chat/completions.
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
        /// Same as IsInteractionEnabled but for synthetic game events, such as map discoveries
        /// and special objects that vanilla exposes through direct hooks rather than TaleDefs.
        /// </summary>
        public bool IsGameEventEnabled(string gameEventDefName)
        {
            if (string.IsNullOrWhiteSpace(gameEventDefName))
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyGameEvent(gameEventDefName);
            return group != null && IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a synthetic game event.
        /// </summary>
        public string InstructionForGameEvent(string gameEventDefName)
        {
            if (string.IsNullOrWhiteSpace(gameEventDefName))
            {
                return string.Empty;
            }

            return InstructionForGroup(InteractionGroups.ClassifyGameEvent(gameEventDefName));
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

            EnsureGroupDictionaries();
            EnsurePersonaPresetList();

            EnsureEndpointsList();

            if (systemPrompt == null)
            {
                systemPrompt = string.Empty;
            }

            if (systemPromptReflection == null)
            {
                systemPromptReflection = string.Empty;
            }

            if (systemPromptNeutral == null)
            {
                systemPromptNeutral = string.Empty;
            }

            if (systemPromptTitle == null)
            {
                systemPromptTitle = string.Empty;
            }

            if (singlePovInstruction == null)
            {
                singlePovInstruction = string.Empty;
            }

            if (recipientFollowupInstruction == null)
            {
                recipientFollowupInstruction = string.Empty;
            }

            if (deathDescriptionInstruction == null)
            {
                deathDescriptionInstruction = string.Empty;
            }

            if (arrivalDescriptionInstruction == null)
            {
                arrivalDescriptionInstruction = string.Empty;
            }

            if (titleUserInstruction == null)
            {
                titleUserInstruction = string.Empty;
            }

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 300);
            maxConcurrentRequests = Mathf.Clamp(maxConcurrentRequests, 1, 16);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
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

            if (groupInstructions == null)
            {
                groupInstructions = new Dictionary<string, string>();
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
                existing = new PersonaPresetConfig(defName, label, rule, themes, false);
                personaPresets.Add(existing);
            }
            else
            {
                existing.label = label ?? string.Empty;
                existing.rule = rule ?? string.Empty;
                existing.themes = NormalizeThemes(themes);
                existing.custom = false;
            }
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

            personaPresets.RemoveAll(preset => preset != null && !preset.custom && preset.defName == defName);
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
                "New Persona",
                string.Empty,
                new[] { DiaryPersonas.PredefinedThemeTags[0] },
                true));
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

            personaPresets.RemoveAll(preset => preset != null && preset.custom && preset.defName == defName);
        }

        /// <summary>
        /// Removes all persona overrides and custom personas, restoring pure XML persona defs.
        /// </summary>
        public void ResetPersonaPresets()
        {
            EnsurePersonaPresetList();
            personaPresets.Clear();
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
