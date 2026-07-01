// Connection + generation mod settings, the system-prompt overrides, and value clamping/save-load.
// Writing-style (persona) catalog edits live in PersonaPresetStore and the reusable event-prompt
// override dictionaries live in PromptOverrideDictionary; PawnDiarySettings owns one of each and
// delegates save/load to them. AdvancedFieldCatalog can also persist player overrides for selected
// XML Def prompt-policy and tuning fields.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
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
        // Header name used when authMode is CustomHeader.
        public string customAuthHeaderName = ApiEndpointPolicy.DefaultCustomHeaderName;
        // When false, keep this row configured but exclude it from generation and failover.
        public bool enabled = true;
        // Request/response compatibility mode. Default preserves existing OpenAI-compatible setups.
        public ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
        // OpenAI Responses reasoning effort. "default" means omit the reasoning object entirely.
        public string reasoningEffort = PawnDiarySettings.DefaultReasoningEffort;

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
                customAuthHeaderName = customAuthHeaderName,
                apiMode = apiMode,
                reasoningEffort = reasoningEffort
            };
        }

        // Reads/writes the row fields on save and load (Scribe is RimWorld's serializer).
        public void ExposeData()
        {
            Scribe_Values.Look(ref url, "url", PawnDiarySettings.DefaultEndpointUrl);
            Scribe_Values.Look(ref model, "model", string.Empty);
            Scribe_Values.Look(ref apiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref authMode, "authMode", ApiAuthMode.BearerToken);
            Scribe_Values.Look(ref customAuthHeaderName, "customAuthHeaderName", ApiEndpointPolicy.DefaultCustomHeaderName);
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref apiMode, "apiMode", ApiCompatibilityMode.OpenAIChatCompletions);
            Scribe_Values.Look(ref reasoningEffort, "reasoningEffort", PawnDiarySettings.DefaultReasoningEffort);
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
        // UI preference: when true, show Diary in the normal pawn inspect-tab row. This is the
        // default surface; disabling it uses the selected-pawn/corpse bottom command instead.
        // The tab remains registered either way so links can always open it.
        public bool showDiaryInspectTab = true;
        // Dev-mode UI preference: shows the per-pawn writing-style picker in the Diary inspector tab.
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
        // Master toggle for live prompt enchantments. When true, first-person diary prompts may get
        // one live health/status hint weighted by DiaryPromptEnchantmentDefs.xml.
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
        // Optional saved overrides for event prompt fields. Keys are DiaryEventPromptDef.eventType
        // values such as "Interaction", "Raid", or a mod-added source/group key; blank means
        // "use the XML default" so Defs stay the canonical prompt catalog. Each map is a
        // PromptOverrideDictionary that owns its own Scribe key and lookup/normalize plumbing.
        public PromptOverrideDictionary eventPromptOverrides = new PromptOverrideDictionary("eventPromptOverrides");
        public PromptOverrideDictionary eventEnhancementOverrides = new PromptOverrideDictionary("eventEnhancementOverrides");
        public PromptOverrideDictionary eventForcedModelOverrides = new PromptOverrideDictionary("eventForcedModelOverrides");
        // Player-facing multiplier for diary-page chance gates. 1x preserves XML tuning defaults;
        // 0x suppresses optional random pages while guaranteed/important event pages still record.
        public float generationChanceWeight = DefaultGenerationChanceWeight;
        // Per-pawn hard cap for hot diary pages. Each pawn keeps its newest maxActiveDiaryEvents
        // full event references; older displayable rows compact into the archive before the hot ref is
        // removed. The field name and Scribe key stay "maxActiveDiaryEvents" for save compatibility.
        public int maxActiveDiaryEvents = DefaultMaxActiveDiaryEvents;
        // Per-pawn hard cap for compact archived diary pages. Archive rows are display-only, so this
        // defaults higher than the hot cap; 0 is allowed for players who want old compact rows purged.
        public int maxArchivedDiaryEvents = DefaultMaxArchivedDiaryEvents;

        // Legacy per-interaction-group settings, keyed by InteractionGroup.defName. Event filtering
        // is now XML-only (DiaryInteractionGroupDef.defaultEnabled); this dictionary remains only so
        // old configs load without losing unrelated settings.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        // Writing-style preset edits made in settings: XML override rows plus user-created custom
        // styles. Owned by PersonaPresetStore, which holds the CRUD and normalization logic.
        public PersonaPresetStore personaPresets = new PersonaPresetStore();
        // Player overrides for Advanced-tab Def tuning/prompt-policy fields. Owned here for save/load;
        // AdvancedFieldCatalog applies them to the live Def instances so existing readers see the new
        // values with no call-site changes.
        public TuningOverrideStore advancedOverrides = new TuningOverrideStore("advancedTuningOverrides");

        // Parallel lists used by Scribe_Collections for serializing the group-enabled dictionary
        // (Unity's serialization cannot handle Dictionary directly). The event-override maps keep
        // their own scratch lists inside PromptOverrideDictionary.
        private List<string> groupEnabledKeys;
        private List<bool> groupEnabledValues;

        // Default local LLM server endpoint (LM Studio/OpenAI-compatible local servers).
        public const string DefaultEndpointUrl = ApiEndpointPolicy.DefaultEndpointUrl;
        // Placeholder model name; real value depends on the local server's loaded model.
        public const string DefaultModelName = "local-model";
        // Sentinel value stored in settings to mean "do not send a reasoning override".
        public const string DefaultReasoningEffort = ApiEndpointPolicy.DefaultReasoningEffort;
        // Per-pawn hot diary-history retention cap. Hot rows keep full generation/retry state, so keep
        // this deliberately small and let older displayable pages compact into the archive.
        public const int DefaultMaxActiveDiaryEvents = 100;
        public const int MinActiveDiaryEvents = 1;
        public const int MaxActiveDiaryEvents = 100;
        // Per-pawn compact archive cap. Archived rows are much smaller than hot DiaryEvent records and
        // never enter generation scans, so the editable ceiling can be higher. 0 means keep no archive
        // rows after they age out of the active hot list.
        public const int DefaultMaxArchivedDiaryEvents = 10000;
        public const int MinArchivedDiaryEvents = 0;
        public const int MaxArchivedDiaryEvents = 50000;
        public const float DefaultGenerationChanceWeight = 1f;
        public const float MinGenerationChanceWeight = 0f;
        public const float MaxGenerationChanceWeight = 5f;

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
            Scribe_Values.Look(ref showDiaryInspectTab, "showDiaryInspectTab", true);
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
            eventPromptOverrides.ExposeData();
            eventEnhancementOverrides.ExposeData();
            eventForcedModelOverrides.ExposeData();
            ExposeGenerationChanceWeight();
            Scribe_Values.Look(ref maxActiveDiaryEvents, "maxActiveDiaryEvents", DefaultMaxActiveDiaryEvents);
            Scribe_Values.Look(ref maxArchivedDiaryEvents, "maxArchivedDiaryEvents", DefaultMaxArchivedDiaryEvents);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            personaPresets.ExposeData();
            advancedOverrides.ExposeData();

            ClampValues();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeEndpointUrls();
                DiaryPersonas.InvalidateCache();
                // Snapshot pristine XML defaults, then push saved Advanced overrides into the live
                // Def fields so they take effect for this session. Safe to call before Defs bind
                // (resolvers return fallbacks) and idempotent across later UI re-applies.
                AdvancedFieldCatalog.EnsureApplied(advancedOverrides);
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
        // Per-key lookup/set/reset and "is customized" live on PromptOverrideDictionary now; these
        // methods span all event-prompt maps, so they stay here and delegate.

        /// <summary>Clears all saved event prompt dictionaries.</summary>
        public void ResetAllEventPromptOverrides()
        {
            eventPromptOverrides.Clear();
            eventEnhancementOverrides.Clear();
            eventForcedModelOverrides.Clear();
        }

        /// <summary>Counts event types with prompt, enhancement, or forced-model text customized.</summary>
        public int CustomizedEventPromptCount()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            eventPromptOverrides.AddKeysTo(keys);
            eventEnhancementOverrides.AddKeysTo(keys);
            eventForcedModelOverrides.AddKeysTo(keys);
            return keys.Count;
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

                if (endpoint.authMode == ApiAuthMode.ApiKeyHeader)
                {
                    endpoint.customAuthHeaderName = ApiEndpointPolicy.LegacyApiKeyHeaderName;
                }
                else if (endpoint.authMode == ApiAuthMode.XApiKeyHeader)
                {
                    endpoint.customAuthHeaderName = ApiEndpointPolicy.LegacyXApiKeyHeaderName;
                }
                else
                {
                    endpoint.customAuthHeaderName = endpoint.customAuthHeaderName ?? ApiEndpointPolicy.DefaultCustomHeaderName;
                }

                endpoint.authMode = NormalizeAuthMode(endpoint.authMode);
                endpoint.apiMode = NormalizeApiMode(endpoint.apiMode);
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
            return ApiEndpointPolicy.NormalizeReasoningEffort(effort);
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

        /// <summary>Normalizes invalid compatibility enum values loaded from hand-edited settings.</summary>
        public static ApiCompatibilityMode NormalizeApiMode(ApiCompatibilityMode mode)
        {
            return ApiEndpointPolicy.NormalizeApiMode(mode);
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

        // The diary-prompt InstructionFor* family used to live here on the settings DTO. They read
        // NO settings state (instructions are XML-only now — no saved overrides), only classifying a
        // Def and rolling a prompt variant, so they moved to InteractionGroups next to Classify*.
        // Call InteractionGroups.InstructionFor*(...) instead. The Is*Enabled eligibility checks and
        // the EditableInstructionForGroup preview helper remain below.

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
        /// Same as IsInteractionEnabled but for synthetic work events emitted by the work scanner.
        /// The scanner picks the group first (passion, strain, routine, dark study), because those
        /// groups depend on pawn state as well as the WorkTypeDef.
        /// </summary>
        public bool IsWorkEnabled(DiaryInteractionGroupDef group)
        {
            return group != null && IsGroupEnabled(group.defName);
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
        /// Checks whether an interaction group is enabled. Event filters are XML-only now, so saved
        /// groupEnabled values from older settings files are ignored.
        /// </summary>
        public bool IsGroupEnabled(string groupKey)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            return group != null && group.defaultEnabled && !group.DisabledByLoadedPackage();
        }

        /// <summary>
        /// Obsolete compatibility shim. Event filters are XML-only.
        /// </summary>
        public void SetGroupEnabled(string groupKey, bool enabled)
        {
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
            eventPromptOverrides.Normalize();
            eventEnhancementOverrides.Normalize();
            eventForcedModelOverrides.Normalize();
            personaPresets.Normalize();

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
            generationChanceWeight = ClampGenerationChanceWeight(generationChanceWeight);
            maxActiveDiaryEvents = ClampActiveDiaryEventLimit(maxActiveDiaryEvents);
            maxArchivedDiaryEvents = ClampArchivedDiaryEventLimit(maxArchivedDiaryEvents);
        }

        /// <summary>
        /// Reads/writes the shared chance multiplier and migrates older separate work/social sliders.
        /// </summary>
        private void ExposeGenerationChanceWeight()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Values.Look(ref generationChanceWeight, "generationChanceWeight", DefaultGenerationChanceWeight);
                return;
            }

            float loadedGenerationChanceWeight = float.NaN;
            Scribe_Values.Look(ref loadedGenerationChanceWeight, "generationChanceWeight", float.NaN);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                return;
            }

            float legacyWorkGenerationWeight = float.NaN;
            float legacySocialGenerationWeight = float.NaN;
            Scribe_Values.Look(ref legacyWorkGenerationWeight, "workGenerationWeight", float.NaN);
            Scribe_Values.Look(ref legacySocialGenerationWeight, "socialGenerationWeight", float.NaN);

            generationChanceWeight = float.IsNaN(loadedGenerationChanceWeight)
                ? MergeLegacyGenerationChanceWeights(legacyWorkGenerationWeight, legacySocialGenerationWeight)
                : loadedGenerationChanceWeight;
        }

        /// <summary>
        /// Merges the old two-slider settings into the one shared slider for upgraded configs.
        /// </summary>
        private static float MergeLegacyGenerationChanceWeights(float workWeight, float socialWeight)
        {
            bool hasWork = !float.IsNaN(workWeight);
            bool hasSocial = !float.IsNaN(socialWeight);
            if (!hasWork && !hasSocial)
            {
                return DefaultGenerationChanceWeight;
            }

            float migratedWorkWeight = hasWork ? workWeight : DefaultGenerationChanceWeight;
            float migratedSocialWeight = hasSocial ? socialWeight : DefaultGenerationChanceWeight;
            return (migratedWorkWeight + migratedSocialWeight) * 0.5f;
        }

        /// <summary>
        /// Clamps the shared random diary-page generation multiplier to the settings slider range.
        /// </summary>
        public static float ClampGenerationChanceWeight(float value)
        {
            if (float.IsNaN(value))
            {
                return DefaultGenerationChanceWeight;
            }

            return Mathf.Clamp(value, MinGenerationChanceWeight, MaxGenerationChanceWeight);
        }

        /// <summary>
        /// Clamps the active diary-event history cap to the bounded range exposed in settings.
        /// </summary>
        public static int ClampActiveDiaryEventLimit(int value)
        {
            return Mathf.Clamp(value, MinActiveDiaryEvents, MaxActiveDiaryEvents);
        }

        /// <summary>
        /// Clamps the archived diary-event history cap to the bounded range exposed in settings.
        /// </summary>
        public static int ClampArchivedDiaryEventLimit(int value)
        {
            return Mathf.Clamp(value, MinArchivedDiaryEvents, MaxArchivedDiaryEvents);
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

        // Writing-style (persona) CRUD, normalization, and theme policy moved to PersonaPresetStore;
        // call settings.personaPresets.* directly. The reusable event-prompt override map plumbing
        // lives on PromptOverrideDictionary (settings.eventPromptOverrides / eventEnhancementOverrides
        // / eventForcedModelOverrides).
    }
}
