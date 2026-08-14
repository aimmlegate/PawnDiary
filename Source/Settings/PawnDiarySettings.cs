// Connection + generation mod settings, the system-prompt overrides, and value clamping/save-load.
// Writing-style (persona) catalog edits live in PersonaPresetStore and the reusable event-prompt
// override dictionaries live in PromptOverrideDictionary; PawnDiarySettings owns one of each and
// delegates save/load to them. AdvancedFieldCatalog can also persist player overrides for selected
// XML Def prompt-policy and tuning fields.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PawnDiary.Capture;
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
        private const int MaximumPersistedProviderFamilyChars = 128;

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
        // Historical native-Ollama rows saved this toggle. It is read once below only so the obsolete
        // key is explicitly consumed, then cleared; it never selects the new native Ollama protocol.
        private bool obsoleteOllamaThink;
        // OpenAI Responses reasoning effort. "default" means omit the reasoning object entirely.
        public string reasoningEffort = PawnDiarySettings.DefaultReasoningEffort;
        // Reasoning-tag override for the response stripper. "auto" = built-in broad tag detection;
        // any other known tag (think/thinking/reasoning/analysis/thought/reflection/scratchpad) is
        // ALSO stripped so exotic wrappers a model emits do not leak into saved diary text.
        public string reasoningTag = PawnDiarySettings.DefaultReasoningTag;
        // Optional prompt-context detail override for this lane. Inherit preserves the global setting.
        public PromptContextDetailOverride contextDetailOverride = PromptContextDetailOverride.Inherit;
        // Attribution for a lane added through the public integration API: the requesting mod's sourceId
        // (empty for a lane the player added by hand). Persisted so an API-injected lane stays traceable
        // and is never silently indistinguishable from a hand-added row. See IntegrationApiSettings.AddLane.
        public string addedBySourceId = string.Empty;
        // Model discovery can expose a provider architecture family that is not present in a renamed
        // model id. Keep the bounded value with a credential-free signature of the exact URL, protocol,
        // and model that produced it so a later row edit can never reuse stale metadata.
        private string providerModelFamily = string.Empty;
        private string providerModelFamilyLaneSignature = string.Empty;

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
                reasoningEffort = reasoningEffort,
                reasoningTag = reasoningTag,
                contextDetailOverride = contextDetailOverride,
                addedBySourceId = addedBySourceId,
                providerModelFamily = this.providerModelFamily,
                providerModelFamilyLaneSignature = this.providerModelFamilyLaneSignature
            };
        }

        /// <summary>Stores bounded discovery metadata for this row's exact current lane identity.</summary>
        internal void RememberProviderModelFamily(string providerFamily)
        {
            this.providerModelFamily = NormalizeProviderModelFamily(providerFamily);
            providerModelFamilyLaneSignature = string.IsNullOrEmpty(this.providerModelFamily)
                ? string.Empty
                : CurrentProviderModelFamilyLaneSignature();
        }

        /// <summary>Returns saved family metadata only while URL, protocol, and model still match.</summary>
        internal string ProviderModelFamilyForCurrentLane()
        {
            string normalizedFamily = NormalizeProviderModelFamily(providerModelFamily);
            return !string.IsNullOrEmpty(normalizedFamily)
                && string.Equals(
                    providerModelFamilyLaneSignature,
                    CurrentProviderModelFamilyLaneSignature(),
                    StringComparison.Ordinal)
                ? normalizedFamily
                : string.Empty;
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
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Keep the retired key out of every new save, including after an old row was loaded.
                obsoleteOllamaThink = false;
            }
            Scribe_Values.Look(ref obsoleteOllamaThink, "ollamaThink", false);
            obsoleteOllamaThink = false;
            Scribe_Values.Look(ref reasoningEffort, "reasoningEffort", PawnDiarySettings.DefaultReasoningEffort);
            Scribe_Values.Look(ref reasoningTag, "reasoningTag", PawnDiarySettings.DefaultReasoningTag);
            Scribe_Values.Look(ref contextDetailOverride, "contextDetailOverride", PromptContextDetailOverride.Inherit);
            Scribe_Values.Look(ref addedBySourceId, "addedBySourceId", string.Empty);
            NormalizePersistedProviderModelFamily();
            Scribe_Values.Look(ref providerModelFamily, "providerModelFamily", string.Empty);
            Scribe_Values.Look(
                ref providerModelFamilyLaneSignature,
                "providerModelFamilyLaneSignature",
                string.Empty);
            NormalizePersistedProviderModelFamily();
        }

        private void NormalizePersistedProviderModelFamily()
        {
            providerModelFamily = NormalizeProviderModelFamily(providerModelFamily);
            if (string.IsNullOrEmpty(providerModelFamily)
                || !string.Equals(
                    providerModelFamilyLaneSignature,
                    CurrentProviderModelFamilyLaneSignature(),
                    StringComparison.Ordinal))
            {
                providerModelFamily = string.Empty;
                providerModelFamilyLaneSignature = string.Empty;
            }
        }

        private static string NormalizeProviderModelFamily(string value)
        {
            string raw = value ?? string.Empty;
            StringBuilder normalized = new StringBuilder(
                Math.Min(raw.Length, MaximumPersistedProviderFamilyChars));
            bool pendingSpace = false;
            for (int i = 0; i < raw.Length && normalized.Length < MaximumPersistedProviderFamilyChars; i++)
            {
                char current = raw[i];
                if (char.IsWhiteSpace(current) || char.IsControl(current))
                {
                    pendingSpace = normalized.Length > 0;
                    continue;
                }

                if (pendingSpace && normalized.Length < MaximumPersistedProviderFamilyChars)
                {
                    normalized.Append(' ');
                    pendingSpace = false;
                }

                if (char.IsHighSurrogate(current))
                {
                    if (i + 1 >= raw.Length
                        || !char.IsLowSurrogate(raw[i + 1])
                        || normalized.Length > MaximumPersistedProviderFamilyChars - 2)
                    {
                        continue;
                    }

                    normalized.Append(current);
                    normalized.Append(raw[++i]);
                    continue;
                }

                // An unpaired low surrogate is not valid XML text either; discard it defensively.
                if (!char.IsLowSurrogate(current))
                {
                    normalized.Append(current);
                }
            }

            return normalized.ToString().Trim();
        }

        private string CurrentProviderModelFamilyLaneSignature()
        {
            // Hashing avoids duplicating a query credential that may already be embedded in the saved
            // URL. Length prefixes make the exact raw URL/model boundary unambiguous before hashing.
            string rawUrl = url ?? string.Empty;
            string rawModel = model ?? string.Empty;
            string identity = ((int)ApiEndpointPolicy.NormalizeApiMode(apiMode)).ToString(
                    CultureInfo.InvariantCulture)
                + "|" + rawUrl.Length.ToString(CultureInfo.InvariantCulture) + "|" + rawUrl
                + "|" + rawModel.Length.ToString(CultureInfo.InvariantCulture) + "|" + rawModel;
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)));
            }
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
        // Maximum attempts made against one API lane after transient transport failures.
        public int retryAttempts = DefaultRetryAttempts;
        // Delay before the first retry. Later waits grow linearly from this base interval.
        public float retryBaseDelaySeconds = DefaultRetryBaseDelaySeconds;
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
        // UI preference: reveals the experimental raw XML Def override pages in settings.
        public bool showExperimentalAdvancedOverrides = false;
        // UI preference: when true, show Diary in the normal pawn inspect-tab row. This is the
        // default surface; disabling it uses the selected-pawn/corpse bottom command instead.
        // The tab remains registered either way so links can always open it.
        public bool showDiaryInspectTab = true;
        // Alternative UI mode: all pawn diaries open in one standalone three-pane reader from the
        // bottom main bar. While enabled, the inspect tab and selected-pawn Diary gizmo stay hidden.
        public bool useDiaryReaderWindow = false;
        // UI preference: when true, the Diary tab shows its right-hand filter/controls sidebar. The
        // header toggle button flips this; hiding the panel widens the journal and keeps the year
        // pager reachable inline. Global (not per-pawn) so the choice persists across pawns/sessions.
        public bool showDiaryFilterPanel = true;
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
        // FORCED ON and hidden from the settings window — see ApplyForcedFeatureSwitches.
        public bool generateTitles = true;
        // Display-only diary page atmosphere. When true, rare extreme entries can use unusual
        // spacing or staggered word sizes in the Diary tab. This never changes prompts or saved
        // generated text.
        // FORCED ON and hidden from the settings window — see ApplyForcedFeatureSwitches.
        public bool enableAtmosphericFormatting = true;
        // Experimental: when true, the whole Diary window takes a subtle seasonal color wash that
        // follows the season at the top of the page and crossfades as you scroll. Display-only — it
        // never changes prompts or saved text.
        // FORCED OFF and hidden from the settings window — see ApplyForcedFeatureSwitches.
        public bool enableSeasonalBackground = false;
        // Master toggle for live prompt enchantments. When true, first-person diary prompts may get
        // one live health/status hint weighted by DiaryPromptEnchantmentDefs.xml.
        // DERIVED from contextDetailLevel — see ApplyForcedFeatureSwitches.
        public bool enablePromptEnchantments = true;
        // Master toggle for the psychotype (outlook) voice layer. When true, each pawn's psychotype rule
        // is folded into first-person prompts alongside their writing style. When false, the block is
        // omitted and pending psychotype rolls stay deferred; existing pawns keep their saved psychotype.
        // DERIVED from contextDetailLevel — see ApplyForcedFeatureSwitches.
        public bool enablePsychotypes = true;
        // The ONE player-facing memory switch (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §3.2): it
        // gates PROMPT INJECTION only — the "relevant past" lines and inline culture annotations.
        // Important-event capture and culture tracking continue while this is off, so re-enabling
        // later surfaces everything that happened meanwhile. The saved key predates the redesign
        // on purpose (§6): the old master value carries over.
        // DERIVED from contextDetailLevel — see ApplyForcedFeatureSwitches.
        public bool enableMemorySystem = true;
        // Global prompt-context detail level. Full preserves the original prompt shape; smaller levels
        // dynamically choose the most relevant optional fields for small local models. It is also the
        // ONE player-facing switch for the three optional writing layers above (live context hints,
        // psychotypes, pawn memory) — see ApplyForcedFeatureSwitches / PromptContextFeaturePolicy.
        public PromptContextDetailLevel contextDetailLevel = PromptContextDetailLevel.Full;
        // Master switch for public integration API behavior. Registration remains harmless while this
        // is off, but external submissions, reads, and provider invocations no-op.
        public bool allowExternalIntegrations = true;
        // Separate, default-OFF opt-in for handing the player's raw API keys to other mods through the
        // integration API (DiaryApiSetupSnapshot). The master toggle above governs event/context/lane
        // access; sharing a plaintext provider key is a strictly higher-trust action, so it is gated on
        // its own switch the player must deliberately enable. When off, GetApiSetup reports hasApiKey but
        // returns an empty apiKey. See IntegrationApiSettings.BuildSetupSnapshot / DiaryApiLaneSnapshot.
        public bool enableExternalKeySharing = false;
        // Opt-out crash reporting. When true (default), errors THIS mod raises are scrubbed of all
        // personal data and sent to a remote endpoint so bugs can be found and fixed. There is no
        // first-run prompt; the player turns it off here. See DiaryErrorReporter for what is/isn't sent.
        public bool enableErrorReporting = true;
        // Anonymous, random per-install id attached to error reports so repeats from one install can be
        // grouped. Generated once by EnsureErrorReportInstallId; never a machine/user/hardware id.
        public string errorReportInstallId = string.Empty;
        // Whether the one-time "error reporting is on by default" notice has been shown. Persisted so the
        // informational opt-out prompt appears exactly once per install, not on every game load.
        public bool errorReportingNoticeShown = false;
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
        // Retired player-facing multiplier retained only as a one-release migration field. Pre-schema
        // configs are still read below, but every current-schema settings object neutralizes this to 1x;
        // runtime admission reads only the preset and sparse per-group overrides.
        public float generationChanceWeight = DefaultGenerationChanceWeight;
        // Per-pawn hard cap for hot diary pages. Each pawn keeps its newest maxActiveDiaryEvents
        // full event references; older displayable rows compact into the archive before the hot ref is
        // removed. The field name and Scribe key stay "maxActiveDiaryEvents" for save compatibility.
        public int maxActiveDiaryEvents = DefaultMaxActiveDiaryEvents;
        // Per-pawn hard cap for compact archived diary pages. Archive rows are display-only, so this
        // defaults higher than the hot cap; 0 is allowed for players who want old compact rows purged.
        public int maxArchivedDiaryEvents = DefaultMaxArchivedDiaryEvents;

        // Per-event-group automatic capture settings, keyed by DiaryInteractionGroupDef.defName.
        // A missing key means "use the XML defaultEnabled value"; rows are stored only when the
        // player changes a group from its XML default.
        public Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>();
        // Frequency schema for one-time conversion from generationChanceWeight and the even older
        // Work/Social sliders. Version zero means none of the fields below existed yet.
        public int frequencySettingsSchemaVersion = CurrentFrequencySettingsSchemaVersion;
        // Stable DiaryFrequencyPresetDef.defName. Standard reproduces the pre-update behavior.
        public string frequencyPresetDefName = DiaryFrequencyPresets.StandardDefName;
        // Sparse absolute multipliers keyed by DiaryInteractionGroupDef.defName. Missing inherits the
        // selected preset; unknown nonblank keys are retained so a temporarily absent add-on recovers.
        public Dictionary<string, float> groupFrequencyOverrides =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        // Separate one-shot UI state: migration may not be mathematically identical for interaction
        // promotion routing, so the Events tab explains it until the player acknowledges the notice.
        public bool frequencyMigrationNoticePending;
        // Writing-style preset edits made in settings: XML override rows plus user-created custom
        // styles. Owned by PersonaPresetStore, which holds the CRUD and normalization logic.
        public PersonaPresetStore personaPresets = new PersonaPresetStore();
        // Psychotype (outlook) preset edits made in settings: XML override rows plus user-created custom
        // psychotypes. Owned by PsychotypePresetStore. Custom rows are manual-only (never auto-rolled).
        public PsychotypePresetStore psychotypePresets = new PsychotypePresetStore();
        // Player overrides for Advanced-tab Def tuning/prompt-policy fields. Owned here for save/load;
        // AdvancedFieldCatalog applies them to the live Def instances so existing readers see the new
        // values with no call-site changes.
        public TuningOverrideStore advancedOverrides = new TuningOverrideStore("advancedTuningOverrides");

        // Parallel lists used by Scribe_Collections for serializing the group-enabled dictionary
        // (Unity's serialization cannot handle Dictionary directly). The event-override maps keep
        // their own scratch lists inside PromptOverrideDictionary.
        private List<string> groupEnabledKeys;
        private List<bool> groupEnabledValues;
        private List<string> groupFrequencyOverrideKeys;
        private List<float> groupFrequencyOverrideValues;
        // GetSettings<PawnDiarySettings>() can deserialize before RimWorld binds XML Defs. Keep the
        // raw legacy presence/value shape in memory until the post-Def startup hook can enumerate the
        // complete group catalog; advancing the schema earlier would permanently lose split intent.
        private DiaryFrequencyLegacySettingsSnapshot pendingLegacyFrequencyMigration;
        // Effective-value reads happen for many rows every UI frame. Once post-Def migration and
        // normalization finish, avoid rebuilding the entire 147-row snapshot for each button.
        private bool frequencyDefBackedNormalizationComplete;
        // Automatic capture reads the selected preset on a hot path. The public/UI projection is
        // deliberately detached on every call, but runtime admission can safely reuse one immutable
        // snapshot until the selected Def token changes.
        private DiaryFrequencyPresetSnapshot runtimeFrequencyPresetSnapshot;
        private string runtimeFrequencyPresetKey = string.Empty;
        // Enable overrides need the interaction Def catalog but do not depend on the frequency preset
        // XML. Keep their one-shot cleanup independent so the additive v8 API remains usable even if
        // a frequency Def is missing or malformed.
        private bool groupEnabledDefBackedNormalizationComplete;
        // Advanced settings can change a group's XML-backed defaultEnabled value after the first
        // cleanup. Remember which Def-mutation revision was normalized so a newly redundant sparse
        // row is removed immediately instead of being reported as a player override until restart.
        private int groupEnabledNormalizedMutationRevision = int.MinValue;

        // Default local LLM server endpoint (LM Studio/OpenAI-compatible local servers).
        public const string DefaultEndpointUrl = ApiEndpointPolicy.DefaultEndpointUrl;
        // Placeholder model name; real value depends on the local server's loaded model.
        public const string DefaultModelName = "local-model";
        // Sentinel value stored in settings to mean "do not send a reasoning override".
        public const string DefaultReasoningEffort = ApiEndpointPolicy.DefaultReasoningEffort;
        // Sentinel value stored in settings to mean "use built-in reasoning-tag detection". Any
        // other known tag adds that wrapper to the stripper's tag list (see ApiEndpointPolicy).
        public const string DefaultReasoningTag = ApiEndpointPolicy.DefaultReasoningTag;
        public const int DefaultRetryAttempts = 5;
        public const float DefaultRetryBaseDelaySeconds = 0.5f;
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
        public const int CurrentFrequencySettingsSchemaVersion = 1;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref apiEndpoints, "apiEndpoints", LookMode.Deep);
            Scribe_Values.Look(ref apiRoutingMode, "apiRoutingMode", ApiLaneRoutingMode.Balanced);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 30);
            Scribe_Values.Look(ref retryAttempts, "retryAttempts", DefaultRetryAttempts);
            Scribe_Values.Look(ref retryBaseDelaySeconds, "retryBaseDelaySeconds", DefaultRetryBaseDelaySeconds);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 4);
            Scribe_Values.Look(ref maxTokens, "maxTokens", 100);
            Scribe_Values.Look(ref temperature, "temperature", 0.8f);
            Scribe_Values.Look(ref showApiSettings, "showApiSettings", true);
            Scribe_Values.Look(ref showPromptStudio, "showPromptStudio", true);
            Scribe_Values.Look(ref showExperimentalAdvancedOverrides, "showExperimentalAdvancedOverrides", false);
            Scribe_Values.Look(ref showDiaryInspectTab, "showDiaryInspectTab", true);
            Scribe_Values.Look(ref useDiaryReaderWindow, "useDiaryReaderWindow", false);
            Scribe_Values.Look(ref showDiaryFilterPanel, "showDiaryFilterPanel", true);
            Scribe_Values.Look(ref showPersonaSettings, "showPersonaSettings", false);
            Scribe_Values.Look(ref showLlmDebugInfo, "showLlmDebugInfo", false);
            Scribe_Values.Look(ref showGeneratingEntries, "showGeneratingEntries", false);
            Scribe_Values.Look(ref promptTestMode, "promptTestMode", false);
            Scribe_Values.Look(ref generateTitles, "generateTitles", true);
            Scribe_Values.Look(ref enableAtmosphericFormatting, "enableAtmosphericFormatting", true);
            Scribe_Values.Look(ref enableSeasonalBackground, "enableSeasonalBackground", false);
            Scribe_Values.Look(ref enablePromptEnchantments, "enablePromptEnchantments", true);
            Scribe_Values.Look(ref enablePsychotypes, "enablePsychotypes", true);
            Scribe_Values.Look(ref enableMemorySystem, "enableMemorySystem", true);
            // The retired lore-seed toggle's "enableLoreSeeds" key is deliberately no longer read;
            // its saved value is ignored (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §6).
            Scribe_Values.Look(ref contextDetailLevel, "contextDetailLevel", PromptContextDetailLevel.Full);
            Scribe_Values.Look(ref allowExternalIntegrations, "allowExternalIntegrations", true);
            Scribe_Values.Look(ref enableExternalKeySharing, "enableExternalKeySharing", false);
            Scribe_Values.Look(ref enableErrorReporting, "enableErrorReporting", true);
            Scribe_Values.Look(ref errorReportInstallId, "errorReportInstallId", string.Empty);
            Scribe_Values.Look(ref errorReportingNoticeShown, "errorReportingNoticeShown", false);
            Scribe_Values.Look(ref injectGeneratedSpeechToPlayLog, "injectGeneratedSpeechToPlayLog", false);
            Scribe_Values.Look(ref systemPromptOverride, "systemPromptOverride", string.Empty);
            Scribe_Values.Look(ref systemPromptReflectionOverride, "systemPromptReflectionOverride", string.Empty);
            Scribe_Values.Look(ref systemPromptNeutralOverride, "systemPromptNeutralOverride", string.Empty);
            Scribe_Values.Look(ref titleSystemPromptOverride, "titleSystemPromptOverride", string.Empty);
            eventPromptOverrides.ExposeData();
            eventEnhancementOverrides.ExposeData();
            eventForcedModelOverrides.ExposeData();
            DiaryFrequencyLegacySettingsSnapshot legacyFrequency = ExposeGenerationChanceWeight();
            Scribe_Values.Look(
                ref frequencySettingsSchemaVersion,
                "frequencySettingsSchemaVersion",
                0);
            Scribe_Values.Look(
                ref frequencyPresetDefName,
                "frequencyPresetDefName",
                DiaryFrequencyPresets.StandardDefName);
            Scribe_Collections.Look(
                ref groupFrequencyOverrides,
                "groupFrequencyOverrides",
                LookMode.Value,
                LookMode.Value,
                ref groupFrequencyOverrideKeys,
                ref groupFrequencyOverrideValues);
            Scribe_Values.Look(
                ref frequencyMigrationNoticePending,
                "frequencyMigrationNoticePending",
                false);
            if (Scribe.mode == LoadSaveMode.LoadingVars
                && frequencySettingsSchemaVersion < CurrentFrequencySettingsSchemaVersion)
            {
                pendingLegacyFrequencyMigration = legacyFrequency
                    ?? new DiaryFrequencyLegacySettingsSnapshot();
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                frequencyDefBackedNormalizationComplete = false;
                runtimeFrequencyPresetSnapshot = null;
                runtimeFrequencyPresetKey = string.Empty;
                groupEnabledDefBackedNormalizationComplete = false;
                groupEnabledNormalizedMutationRevision = int.MinValue;
            }
            Scribe_Values.Look(ref maxActiveDiaryEvents, "maxActiveDiaryEvents", DefaultMaxActiveDiaryEvents);
            Scribe_Values.Look(ref maxArchivedDiaryEvents, "maxArchivedDiaryEvents", DefaultMaxArchivedDiaryEvents);
            Scribe_Collections.Look(ref groupEnabled, "interactionGroupEnabled", LookMode.Value, LookMode.Value, ref groupEnabledKeys, ref groupEnabledValues);
            personaPresets.ExposeData();
            psychotypePresets.ExposeData();
            advancedOverrides.ExposeData();

            ClampValues();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeEndpointUrls();
                DiaryPersonas.InvalidateCache();
                DiaryPsychotypes.InvalidateCache();
                // Snapshot pristine XML defaults, then push saved Advanced overrides into the live
                // Def fields so they take effect for this session. Safe to call before Defs bind
                // (resolvers return fallbacks) and idempotent across later UI re-applies.
                AdvancedFieldCatalog.EnsureApplied(advancedOverrides);
            }
        }

        /// <summary>
        /// Returns the anonymous per-install id used to group error reports, generating and storing a
        /// random one on first use. It is a random GUID — never derived from any machine, user, or
        /// hardware value — so it identifies an install, not a person.
        /// </summary>
        public string EnsureErrorReportInstallId()
        {
            if (string.IsNullOrEmpty(errorReportInstallId))
            {
                errorReportInstallId = Guid.NewGuid().ToString("N");
            }

            return errorReportInstallId;
        }

        /// <summary>
        /// Main-thread startup helper: generates the install id if missing and persists it immediately, so
        /// the same value survives across sessions. Without this, the id was generated lazily off-thread by
        /// the reporter but never written, so a player who never opened settings got a fresh id each run —
        /// inflating the server's distinct-install counts. Call once from the mod constructor.
        /// </summary>
        public void EnsureErrorReportInstallIdPersisted()
        {
            if (!string.IsNullOrEmpty(errorReportInstallId))
            {
                return;
            }

            EnsureErrorReportInstallId();
            Write();
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
                // This is intentionally value-only. Never infer a native protocol from the URL: a
                // working http://localhost:11434/v1 row is an OpenAI-compatible Ollama lane and must
                // remain one unless the player explicitly chooses the new native mode.
                endpoint.apiMode = NormalizeApiMode(endpoint.apiMode);
                endpoint.reasoningEffort = NormalizeReasoningEffort(endpoint.reasoningEffort);
                endpoint.reasoningTag = NormalizeReasoningTag(endpoint.reasoningTag);
                endpoint.contextDetailOverride = NormalizeContextDetailOverride(endpoint.contextDetailOverride);
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

        /// <summary>
        /// Keeps the saved reasoning-tag value to the known set, falling back to "auto" (built-in
        /// broad detection) when the saved value is blank or unrecognized.
        /// </summary>
        public static string NormalizeReasoningTag(string tag)
        {
            return ApiEndpointPolicy.NormalizeReasoningTag(tag);
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

        /// <summary>Normalizes invalid prompt-context detail values loaded from settings.</summary>
        public static PromptContextDetailLevel NormalizeContextDetailLevel(PromptContextDetailLevel level)
        {
            return PromptContextSelector.Normalize(level);
        }

        /// <summary>Normalizes invalid per-lane prompt-context detail override values.</summary>
        public static PromptContextDetailOverride NormalizeContextDetailOverride(PromptContextDetailOverride value)
        {
            return PromptContextSelector.NormalizeOverride(value);
        }

        /// <summary>Resolves the context detail level used by one API lane against the global setting.</summary>
        public PromptContextDetailLevel EffectiveContextDetailLevel(ApiEndpointConfig endpoint)
        {
            return PromptContextSelector.Resolve(
                contextDetailLevel,
                endpoint == null ? PromptContextDetailOverride.Inherit : endpoint.contextDetailOverride);
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
        /// Checks whether automatic capture is enabled for an interaction group. XML supplies the
        /// default, package gates can still make compatibility groups inert, and the saved dictionary
        /// stores only player overrides from that XML default.
        /// </summary>
        public bool IsGroupEnabled(string groupKey)
        {
            EnsureGroupDictionaries();
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (group == null || group.UnavailableForCurrentRuntime())
            {
                return false;
            }

            if (string.Equals(group.defName, CounselEventPolicy.GroupDefName,
                StringComparison.Ordinal))
            {
                // Counsel split out of the older broad conversion row in Phase 2. Until the player
                // touches the new row, preserve an explicit legacy conversion choice across upgrades.
                return CounselSettingsInheritance.Enabled(
                    GroupEnabledOverride(CounselEventPolicy.GroupDefName),
                    GroupEnabledOverride("conversion"),
                    group.defaultEnabled);
            }

            if (string.Equals(group.defName, ConversionRitualPolicy.GroupDefName,
                StringComparison.Ordinal))
            {
                // Completed conversion rituals split out of the older Ritual catch-all. Preserve an
                // explicit ritualFinished choice until the player deliberately touches the new row.
                return ConversionRitualSettingsInheritance.Enabled(
                    GroupEnabledOverride(ConversionRitualPolicy.GroupDefName),
                    GroupEnabledOverride(ConversionRitualPolicy.LegacyGroupDefName),
                    group.defaultEnabled);
            }

            if (ReflectionSettingsInheritance.IsSplitRow(group.defName))
            {
                // Day, quadrum and belief reflections each own a row now. Before the split the generic
                // `reflection` row was the only working reflection toggle, so a player who turned it
                // off meant all of them — honor that until the new row is deliberately touched.
                return ReflectionSettingsInheritance.Enabled(
                    GroupEnabledOverride(group.defName),
                    GroupEnabledOverride(ReflectionSettingsInheritance.LegacyGroupDefName),
                    group.defaultEnabled);
            }

            bool saved;
            return groupEnabled.TryGetValue(group.defName, out saved) ? saved : group.defaultEnabled;
        }

        /// <summary>
        /// Effective canonical-growth setting. Before the player touches the new Biotech row, an
        /// explicit legacy Birthday override is inherited so an upgrade does not reverse prior intent.
        /// The guarded Phase 1 growth source calls this at event creation; observation/baselines advance
        /// independently so disabling the row releases the mature Birthday fallback instead.
        /// </summary>
        public bool IsBiotechGrowthMomentEnabled()
        {
            EnsureGroupDictionaries();
            DiaryInteractionGroupDef group = InteractionGroups.ByKey("progressionGrowthMoment");
            if (group == null || group.UnavailableForCurrentRuntime())
            {
                return false;
            }

            return BiotechSettingsInheritance.GrowthEnabled(
                GroupEnabledOverride("progressionGrowthMoment"),
                GroupEnabledOverride("eventWindowBirthday"),
                group.defaultEnabled);
        }

        /// <summary>
        /// Effective canonical-birth setting. A new explicit override wins; otherwise only explicit
        /// mature Tale/ritual choices are inherited. The canonical birth owner freezes this choice at
        /// the exact ApplyBirthOutcome completion boundary.
        /// </summary>
        public bool IsBiotechFamilyBirthEnabled(bool ritualBirth)
        {
            EnsureGroupDictionaries();
            DiaryInteractionGroupDef group = InteractionGroups.ByKey("biotechFamilyBirth");
            if (group == null || group.UnavailableForCurrentRuntime())
            {
                return false;
            }

            return BiotechSettingsInheritance.FamilyBirthEnabled(
                GroupEnabledOverride("biotechFamilyBirth"),
                GroupEnabledOverride("talelife"),
                GroupEnabledOverride("ritualChildbirth"),
                ritualBirth,
                group.defaultEnabled);
        }

        /// <summary>
        /// Stores a player override for one automatic-capture group. Matching the XML default normally
        /// removes the override; newly split Counsel/conversion-ritual rows keep it when needed to
        /// override inherited legacy intent.
        /// </summary>
        public void SetGroupEnabled(string groupKey, bool enabled)
        {
            EnsureGroupDictionaries();
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (group == null)
            {
                return;
            }

            bool keepCounselOverride = string.Equals(group.defName,
                    CounselEventPolicy.GroupDefName, StringComparison.Ordinal)
                && CounselSettingsInheritance.ShouldStoreOverride(
                    enabled, group.defaultEnabled, GroupEnabledOverride("conversion"));
            bool keepConversionRitualOverride = string.Equals(group.defName,
                    ConversionRitualPolicy.GroupDefName, StringComparison.Ordinal)
                && ConversionRitualSettingsInheritance.ShouldStoreOverride(
                    enabled, group.defaultEnabled,
                    GroupEnabledOverride(ConversionRitualPolicy.LegacyGroupDefName));
            bool keepReflectionOverride = ReflectionSettingsInheritance.IsSplitRow(group.defName)
                && ReflectionSettingsInheritance.ShouldStoreOverride(
                    enabled, group.defaultEnabled,
                    GroupEnabledOverride(ReflectionSettingsInheritance.LegacyGroupDefName));
            if (enabled == group.defaultEnabled
                && !keepCounselOverride && !keepConversionRitualOverride && !keepReflectionOverride)
            {
                groupEnabled.Remove(group.defName);
                return;
            }

            groupEnabled[group.defName] = enabled;
        }

        /// <summary>
        /// True when the player has a saved group choice. A newly split row may intentionally store
        /// its XML default to override the opposite inherited legacy choice.
        /// </summary>
        public bool HasGroupEnabledOverride(string groupKey)
        {
            EnsureGroupDictionaries();
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            return group != null && groupEnabled.ContainsKey(group.defName);
        }

        // ---- Diary frequency settings ----------------------------------------------------------

        /// <summary>Returns a detached copy of the selected XML frequency preset.</summary>
        internal DiaryFrequencyPresetSnapshot FrequencyPresetSnapshot()
        {
            TryFinalizeFrequencySettingsAfterDefsLoaded();
            return DiaryFrequencyPresets.Snapshot(frequencyPresetDefName);
        }

        /// <summary>
        /// Returns the main-thread runtime preset without copying its dictionaries for every captured
        /// candidate. The snapshot contains no live Def or settings references and is replaced as soon
        /// as the selected preset token changes.
        /// </summary>
        internal DiaryFrequencyPresetSnapshot RuntimeFrequencyPresetSnapshot()
        {
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return null;
            }

            string selected = frequencyPresetDefName ?? string.Empty;
            if (runtimeFrequencyPresetSnapshot == null
                || !string.Equals(runtimeFrequencyPresetKey, selected, StringComparison.Ordinal))
            {
                runtimeFrequencyPresetSnapshot = DiaryFrequencyPresets.Snapshot(selected);
                runtimeFrequencyPresetKey = selected;
            }

            return runtimeFrequencyPresetSnapshot;
        }

        /// <summary>
        /// Reads the already-normalized sparse map for a classified runtime group without performing
        /// another Def lookup. Callers still fail open to inherited Standard behavior until post-Def
        /// settings finalization is available.
        /// </summary>
        internal bool TryGetRuntimeGroupFrequencyOverride(string groupKey, out float multiplier)
        {
            multiplier = DiaryFrequencyPolicy.StandardMultiplier;
            EnsureFrequencyDictionary();
            if (string.IsNullOrWhiteSpace(groupKey)
                || !TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            return groupFrequencyOverrides.TryGetValue(groupKey.Trim(), out multiplier);
        }

        /// <summary>Resolves one already-classified runtime group without allocating detached maps.</summary>
        internal float RuntimeGroupFrequencyMultiplier(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return DiaryFrequencyPolicy.StandardMultiplier;
            }

            float saved = DiaryFrequencyPolicy.StandardMultiplier;
            bool hasOverride = TryGetRuntimeGroupFrequencyOverride(group.defName, out saved);
            return DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                RuntimeFrequencyPresetSnapshot(),
                group.defName,
                group.frequencyTier,
                hasOverride,
                saved);
        }

        /// <summary>Returns this group's inherited multiplier before a sparse player override.</summary>
        public float PresetGroupFrequencyMultiplier(DiaryInteractionGroupDef group)
        {
            return PresetGroupFrequencyMultiplier(group, FrequencyPresetSnapshot());
        }

        /// <summary>Resolves inherited frequency against an already detached preset snapshot.</summary>
        internal static float PresetGroupFrequencyMultiplier(
            DiaryInteractionGroupDef group,
            DiaryFrequencyPresetSnapshot preset)
        {
            if (group == null)
            {
                return DiaryFrequencyPolicy.StandardMultiplier;
            }

            return DiaryFrequencyPolicy.ResolvePresetMultiplier(
                preset,
                group.defName,
                group.frequencyTier);
        }

        /// <summary>Returns this group's selected-preset multiplier with its saved override applied.</summary>
        public float EffectiveGroupFrequencyMultiplier(DiaryInteractionGroupDef group)
        {
            return EffectiveGroupFrequencyMultiplier(group, FrequencyPresetSnapshot());
        }

        /// <summary>Resolves effective frequency while reusing one detached preset projection.</summary>
        internal float EffectiveGroupFrequencyMultiplier(
            DiaryInteractionGroupDef group,
            DiaryFrequencyPresetSnapshot preset)
        {
            if (group == null)
            {
                return DiaryFrequencyPolicy.StandardMultiplier;
            }

            float saved;
            bool hasOverride = TryGetGroupFrequencyOverride(group.defName, out saved);
            return DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                preset,
                group.defName,
                group.frequencyTier,
                hasOverride,
                saved);
        }

        /// <summary>True when this known group owns a sparse frequency override.</summary>
        public bool HasGroupFrequencyOverride(string groupKey)
        {
            float ignored;
            return TryGetGroupFrequencyOverride(groupKey, out ignored);
        }

        /// <summary>Reads a known group's saved absolute multiplier without applying its preset.</summary>
        public bool TryGetGroupFrequencyOverride(string groupKey, out float multiplier)
        {
            EnsureFrequencyDictionary();
            multiplier = DiaryFrequencyPolicy.StandardMultiplier;
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (group == null)
            {
                return false;
            }

            float saved;
            if (!groupFrequencyOverrides.TryGetValue(group.defName, out saved))
            {
                return false;
            }

            multiplier = saved;
            return true;
        }

        /// <summary>
        /// Stores one settings-visible group's absolute multiplier. A value equal to the selected
        /// preset is re-sparsified immediately; malformed values use the inherited preset safely.
        /// </summary>
        public void SetGroupFrequencyOverride(string groupKey, float multiplier)
        {
            EnsureFrequencyDictionary();
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (!PawnDiaryMod.IsSettingsEventFilterGroup(group))
            {
                return;
            }

            float inherited = PresetGroupFrequencyMultiplier(group);
            float normalized = DiaryFrequencySettingsPolicy.NormalizeMultiplier(
                multiplier,
                inherited);
            if (DiaryFrequencySettingsPolicy.NearlyEqual(normalized, inherited))
            {
                // An unavailable third-party preset uses Standard only as its temporary effective
                // fallback. Keep an explicit 1x choice in that state: removing it would let the
                // add-on's different inherited value silently take over when the preset returns.
                if (FindFrequencyPreset(frequencyPresetDefName) != null)
                {
                    groupFrequencyOverrides.Remove(group.defName);
                    return;
                }
            }

            groupFrequencyOverrides[group.defName] = normalized;
        }

        /// <summary>Clears one known group's saved multiplier so it inherits the selected preset.</summary>
        public void ResetGroupFrequencyOverride(string groupKey)
        {
            EnsureFrequencyDictionary();
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (group != null)
            {
                groupFrequencyOverrides.Remove(group.defName);
            }
        }

        /// <summary>Clears every known or preserved future-group frequency override.</summary>
        public void ResetAllGroupFrequencyOverrides()
        {
            EnsureFrequencyDictionary();
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return;
            }

            groupFrequencyOverrides.Clear();
        }

        /// <summary>Number of sparse rows currently persisted, including forward-compatible keys.</summary>
        public int GroupFrequencyOverrideCount()
        {
            EnsureFrequencyDictionary();
            TryFinalizeFrequencySettingsAfterDefsLoaded();
            return groupFrequencyOverrides.Count;
        }

        /// <summary>True when at least one known group differs from the selected preset.</summary>
        public bool HasCustomFrequencyOverrides()
        {
            return HasCustomFrequencyOverrides(FrequencyPresetSnapshot());
        }

        /// <summary>Detects Custom state while reusing an already detached preset projection.</summary>
        internal bool HasCustomFrequencyOverrides(DiaryFrequencyPresetSnapshot preset)
        {
            EnsureFrequencyDictionary();
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            return DiaryFrequencyPolicy.HasCustomOverrides(
                groupFrequencyOverrides,
                FrequencyGroupSnapshots(settingsVisibleOnly: true),
                preset);
        }

        /// <summary>
        /// Selects any currently loaded frequency preset. Unknown input safely selects Standard for
        /// direct/UI callers; pass <paramref name="clearOverrides"/> only after any UI confirmation.
        /// </summary>
        public void SetFrequencyPreset(string presetDefName, bool clearOverrides)
        {
            if (!TrySetFrequencyPreset(presetDefName, clearOverrides))
            {
                TrySetFrequencyPreset(DiaryFrequencyPresets.StandardDefName, clearOverrides);
            }
        }

        /// <summary>
        /// Attempts to select any loaded frequency preset, including add-on presets. Unknown tokens
        /// return false without mutating saved state; the public API uses this forward-safe contract.
        /// </summary>
        public bool TrySetFrequencyPreset(string presetDefName, bool clearOverrides)
        {
            if (!TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            DiaryFrequencyPresetDef preset = FindFrequencyPreset(presetDefName);
            if (preset == null)
            {
                return false;
            }

            frequencyPresetDefName = preset.defName;
            runtimeFrequencyPresetSnapshot = null;
            runtimeFrequencyPresetKey = string.Empty;
            if (clearOverrides)
            {
                ResetAllGroupFrequencyOverrides();
            }
            else
            {
                NormalizeGroupFrequencyOverrides(resparsifyKnownValues: true);
            }

            return true;
        }

        /// <summary>Marks the one-time migrated-frequency explanation as seen.</summary>
        public void AcknowledgeFrequencyMigrationNotice()
        {
            frequencyMigrationNoticePending = false;
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
            // Mod settings can load before DefDatabase is populated. Def-backed cleanup must wait;
            // otherwise every saved enable row looks unknown and frequency migration sees no groups.
            TryFinalizeGroupEnabledSettingsAfterDefsLoaded();
            TryFinalizeFrequencySettingsAfterDefsLoaded();
            eventPromptOverrides.Normalize();
            eventEnhancementOverrides.Normalize();
            eventForcedModelOverrides.Normalize();
            personaPresets.Normalize();
            psychotypePresets.Normalize();

            EnsureEndpointsList();

            apiRoutingMode = NormalizeRoutingMode(apiRoutingMode);
            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 5, 300);
            retryAttempts = LlmTransportPolicy.NormalizeRetryAttempts(retryAttempts);
            retryBaseDelaySeconds = float.IsNaN(retryBaseDelaySeconds)
                ? DefaultRetryBaseDelaySeconds
                : (float)LlmTransportPolicy.NormalizeRetryDelaySeconds(retryBaseDelaySeconds);
            maxConcurrentRequests = Mathf.Clamp(maxConcurrentRequests, 1, 16);
            maxTokens = Mathf.Clamp(maxTokens, 32, 2048);
            temperature = Mathf.Clamp(temperature, 0f, 2f);
            contextDetailLevel = NormalizeContextDetailLevel(contextDetailLevel);
            ApplyForcedFeatureSwitches();
            injectGeneratedSpeechToPlayLog = false;
            systemPromptOverride = systemPromptOverride ?? string.Empty;
            systemPromptReflectionOverride = systemPromptReflectionOverride ?? string.Empty;
            systemPromptNeutralOverride = systemPromptNeutralOverride ?? string.Empty;
            titleSystemPromptOverride = titleSystemPromptOverride ?? string.Empty;
            generationChanceWeight = frequencySettingsSchemaVersion
                >= CurrentFrequencySettingsSchemaVersion
                    ? DefaultGenerationChanceWeight
                    : ClampGenerationChanceWeight(generationChanceWeight);
            maxActiveDiaryEvents = ClampActiveDiaryEventLimit(maxActiveDiaryEvents);
            maxArchivedDiaryEvents = ClampArchivedDiaryEventLimit(maxArchivedDiaryEvents);
        }

        /// <summary>
        /// Re-derives the settings the player no longer toggles one by one.
        ///
        /// Two groups live here. First, three display/writing switches are pinned to the shipped
        /// behavior: short page titles and rare atmosphere formatting are always on, and the
        /// experimental seasonal window tint is always off. Second, the three optional prompt layers
        /// (live context hints, psychotypes, pawn memory) follow the Prompt context detail preset via
        /// the pure <see cref="PromptContextFeaturePolicy"/> table.
        ///
        /// Called from <see cref="ClampValues"/>, which runs after loading settings, before saving
        /// them, and once per settings-window frame — so an older settings file with the opposite
        /// values converges the moment it is read, and clicking a preset row takes effect the same
        /// frame. The fields stay real (not computed properties) so every existing reader, save
        /// migration, and test seam keeps working unchanged.
        /// </summary>
        private void ApplyForcedFeatureSwitches()
        {
            generateTitles = true;
            enableAtmosphericFormatting = true;
            enableSeasonalBackground = false;

            enablePromptEnchantments = PromptContextFeaturePolicy.AllowsPromptEnchantments(contextDetailLevel);
            enablePsychotypes = PromptContextFeaturePolicy.AllowsPsychotypes(contextDetailLevel);
            enableMemorySystem = PromptContextFeaturePolicy.AllowsMemoryContext(contextDetailLevel);
        }

        /// <summary>
        /// Reads the retired scalar for one-release migration compatibility and captures raw legacy-key
        /// presence before the old merge step erases whether Work/Social keys existed independently.
        /// </summary>
        private DiaryFrequencyLegacySettingsSnapshot ExposeGenerationChanceWeight()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // The mod constructor may persist a newly generated install id before Defs bind. If
                // migration is still deferred, re-emit the exact retired-key shape so an interrupted
                // startup cannot collapse distinct Work/Social intent into their compatibility mean.
                if (frequencySettingsSchemaVersion < CurrentFrequencySettingsSchemaVersion
                    && pendingLegacyFrequencyMigration != null)
                {
                    ExposePendingLegacyFrequencySettings(pendingLegacyFrequencyMigration);
                    return null;
                }

                generationChanceWeight = DefaultGenerationChanceWeight;
                Scribe_Values.Look(ref generationChanceWeight, "generationChanceWeight", DefaultGenerationChanceWeight);
                return null;
            }

            // The sentinel lies beyond the supported range and is finite, so even a literal NaN or
            // infinity in hand-edited XML remains distinguishable from a missing key.
            const float missingSentinel = -987654f;
            float loadedGenerationChanceWeight = missingSentinel;
            Scribe_Values.Look(ref loadedGenerationChanceWeight, "generationChanceWeight", missingSentinel);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                return null;
            }

            float legacyWorkGenerationWeight = missingSentinel;
            float legacySocialGenerationWeight = missingSentinel;
            Scribe_Values.Look(ref legacyWorkGenerationWeight, "workGenerationWeight", missingSentinel);
            Scribe_Values.Look(ref legacySocialGenerationWeight, "socialGenerationWeight", missingSentinel);

            DiaryFrequencyLegacySettingsSnapshot legacy =
                new DiaryFrequencyLegacySettingsSnapshot
                {
                    hasGenerationChanceWeight = loadedGenerationChanceWeight != missingSentinel,
                    generationChanceWeight = loadedGenerationChanceWeight,
                    hasWorkGenerationWeight = legacyWorkGenerationWeight != missingSentinel,
                    workGenerationWeight = legacyWorkGenerationWeight,
                    hasSocialGenerationWeight = legacySocialGenerationWeight != missingSentinel,
                    socialGenerationWeight = legacySocialGenerationWeight
                };

            // Keep the raw value only until post-Def migration can project it into exact group rows.
            if (legacy.hasGenerationChanceWeight)
            {
                generationChanceWeight = loadedGenerationChanceWeight;
            }
            else
            {
                generationChanceWeight = MergeLegacyGenerationChanceWeights(
                    legacy.hasWorkGenerationWeight ? legacyWorkGenerationWeight : float.NaN,
                    legacy.hasSocialGenerationWeight ? legacySocialGenerationWeight : float.NaN);
            }

            return legacy;
        }

        /// <summary>Writes the exact raw legacy-key presence while post-Def migration is deferred.</summary>
        private static void ExposePendingLegacyFrequencySettings(
            DiaryFrequencyLegacySettingsSnapshot legacy)
        {
            if (legacy.hasGenerationChanceWeight)
            {
                float unified = legacy.generationChanceWeight;
                Scribe_Values.Look(
                    ref unified,
                    "generationChanceWeight",
                    DefaultGenerationChanceWeight,
                    forceSave: true);
            }

            if (legacy.hasWorkGenerationWeight)
            {
                float work = legacy.workGenerationWeight;
                Scribe_Values.Look(
                    ref work,
                    "workGenerationWeight",
                    DefaultGenerationChanceWeight,
                    forceSave: true);
            }

            if (legacy.hasSocialGenerationWeight)
            {
                float social = legacy.socialGenerationWeight;
                Scribe_Values.Look(
                    ref social,
                    "socialGenerationWeight",
                    DefaultGenerationChanceWeight,
                    forceSave: true);
            }
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
        /// Converts a pre-frequency-schema config exactly once, preserving the more precise original
        /// Work/Social intent in sparse group rows and neutralizing the retired scalar afterwards.
        /// </summary>
        private void MigrateLegacyFrequencySettings(
            DiaryFrequencyLegacySettingsSnapshot legacy)
        {
            EnsureFrequencyDictionary();
            frequencyPresetDefName = DiaryFrequencyPresets.StandardDefName;

            DiaryFrequencyMigrationResult migration =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    legacy,
                    FrequencyMigrationGroupSnapshots());
            foreach (KeyValuePair<string, float> entry in migration.groupOverrides)
            {
                if (!groupFrequencyOverrides.ContainsKey(entry.Key))
                {
                    groupFrequencyOverrides[entry.Key] = entry.Value;
                }
            }

            frequencyMigrationNoticePending = migration.hasMigratedCustomIntent;
            frequencySettingsSchemaVersion = CurrentFrequencySettingsSchemaVersion;
            generationChanceWeight = DefaultGenerationChanceWeight;
            pendingLegacyFrequencyMigration = null;
        }

        /// <summary>
        /// Completes Def-backed migration and normalization only after RimWorld has loaded the shipped
        /// preset and representative Work/Ability/Interaction rows. The startup hook calls this after
        /// Def binding; public settings readers retry defensively without ever caching an empty catalog.
        /// </summary>
        internal bool TryFinalizeFrequencySettingsAfterDefsLoaded()
        {
            if (frequencyDefBackedNormalizationComplete)
            {
                return true;
            }

            if (!FrequencyDefinitionsReady())
            {
                return false;
            }

            // This method is also the lazy boundary used by public API readers. Another mod can call
            // those readers from a post-Def static constructor before DiaryModStartup runs, so the
            // prerequisite cannot live only in that startup hook. Promotion membership used by the
            // legacy Social migration must reflect the player's saved Advanced overrides on every
            // entry path.
            AdvancedFieldCatalog.EnsureApplied(advancedOverrides);
            if (!AdvancedFieldCatalog.DefaultsCaptured)
            {
                return false;
            }

            if (frequencySettingsSchemaVersion < CurrentFrequencySettingsSchemaVersion)
            {
                DiaryFrequencyLegacySettingsSnapshot legacy = pendingLegacyFrequencyMigration
                    ?? new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasGenerationChanceWeight = true,
                        generationChanceWeight = generationChanceWeight
                    };
                MigrateLegacyFrequencySettings(legacy);
            }

            bool selectedPresetResolved = NormalizeFrequencyPresetDefName();
            NormalizeGroupFrequencyOverrides(selectedPresetResolved);
            // Schema v1 owns runtime frequency completely. Keep the retired public field readable for
            // binary compatibility, but never let a carried Slice-2 bridge value affect later saves.
            generationChanceWeight = DefaultGenerationChanceWeight;
            // Constructor-time Clamp deliberately skips this Def-backed cleanup. Finish it beside
            // frequency normalization so an early API snapshot cannot report stale/redundant enable
            // overrides before the settings window has ever been opened.
            TryFinalizeGroupEnabledSettingsAfterDefsLoaded();
            frequencyDefBackedNormalizationComplete = true;
            return true;
        }

        /// <summary>
        /// Completes the older enable-override cleanup once interaction Defs exist. This intentionally
        /// does not require any frequency Def, preserving the pre-v9 enable API under partial XML load.
        /// </summary>
        internal bool TryFinalizeGroupEnabledSettingsAfterDefsLoaded()
        {
            if (groupEnabledDefBackedNormalizationComplete
                && groupEnabledNormalizedMutationRevision == InteractionGroups.MutationRevision)
            {
                return true;
            }

            if (!InteractionGroupDefinitionsReady())
            {
                return false;
            }

            // defaultEnabled itself is Advanced-overridable. Apply the saved Def values before
            // deciding which sparse enable rows are redundant, even when frequency preset XML is
            // unavailable and the frequency finalizer cannot run.
            AdvancedFieldCatalog.EnsureApplied(advancedOverrides);
            if (!AdvancedFieldCatalog.DefaultsCaptured)
            {
                return false;
            }

            NormalizeGroupEnabledOverrides();
            groupEnabledDefBackedNormalizationComplete = true;
            groupEnabledNormalizedMutationRevision = InteractionGroups.MutationRevision;
            return true;
        }

        /// <summary>
        /// True once representative interaction groups and the Standard preset can be resolved.
        /// Safe before Def binding: public adapters use this guard before touching InteractionGroups.
        /// </summary>
        internal static bool FrequencyDefinitionsReady()
        {
            return InteractionGroupDefinitionsReady()
                && DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(
                    DiaryFrequencyPresets.StandardDefName) != null;
        }

        private static bool InteractionGroupDefinitionsReady()
        {
            // These three base-mod rows cover every family used by legacy migration. Requiring all of
            // them avoids treating a partially populated DefDatabase as the complete catalog.
            return DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail("smalltalk") != null
                && DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail("workRoutine") != null
                && DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail("abilityUsed") != null;
        }

        /// <summary>Returns any loaded frequency preset by case-insensitive stable Def name.</summary>
        private static DiaryFrequencyPresetDef FindFrequencyPreset(string presetDefName)
        {
            string key = (presetDefName ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                return null;
            }

            List<DiaryFrequencyPresetDef> presets =
                DefDatabase<DiaryFrequencyPresetDef>.AllDefsListForReading;
            for (int i = 0; i < presets.Count; i++)
            {
                DiaryFrequencyPresetDef preset = presets[i];
                if (preset != null && string.Equals(
                    preset.defName,
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return null;
        }

        /// <summary>
        /// Canonicalizes a loaded preset token. Blank/corrupt absence becomes shipped Standard, while
        /// a nonblank unresolved token is preserved for a temporarily missing third-party provider.
        /// Returns whether the selected Def is currently available.
        /// </summary>
        private bool NormalizeFrequencyPresetDefName()
        {
            string key = (frequencyPresetDefName ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                key = DiaryFrequencyPresets.StandardDefName;
            }

            DiaryFrequencyPresetDef preset = FindFrequencyPreset(key);
            if (preset != null)
            {
                frequencyPresetDefName = preset.defName;
                return true;
            }

            frequencyPresetDefName = key;
            return false;
        }

        /// <summary>
        /// Normalizes current rows while retaining unknown saved keys. Re-sparsification is safe only
        /// when the selected preset is loaded; Standard may supply effective fallback arithmetic for
        /// an unresolved third-party token but must not erase values that its returning Def can change.
        /// </summary>
        private void NormalizeGroupFrequencyOverrides(bool resparsifyKnownValues)
        {
            EnsureFrequencyDictionary();
            groupFrequencyOverrides = DiaryFrequencySettingsPolicy.NormalizeOverrides(
                groupFrequencyOverrides,
                FrequencyGroupSnapshots(settingsVisibleOnly: false),
                DiaryFrequencyPresets.Snapshot(frequencyPresetDefName),
                resparsifyKnownValues);
        }

        /// <summary>Builds detached identities for Custom detection and sparse normalization.</summary>
        private static List<DiaryFrequencyGroupSnapshot> FrequencyGroupSnapshots(
            bool settingsVisibleOnly)
        {
            List<DiaryFrequencyGroupSnapshot> snapshots =
                new List<DiaryFrequencyGroupSnapshot>();
            List<DiaryInteractionGroupDef> groups =
                DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading;
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group == null
                    || group.domain == GroupDomain.External
                    || (settingsVisibleOnly && !PawnDiaryMod.IsSettingsEventFilterGroup(group)))
                {
                    continue;
                }

                snapshots.Add(new DiaryFrequencyGroupSnapshot
                {
                    groupKey = group.defName ?? string.Empty,
                    frequencyTier = group.frequencyTier ?? string.Empty
                });
            }

            return snapshots;
        }

        /// <summary>
        /// Builds detached migration facts for every known non-External row. A package-gated
        /// compatibility group may be hidden today and become available later; legacy intent must
        /// already be waiting when it does. Work, Ability, and active promotion groups are the
        /// complete historical blast radius of the retired sliders.
        /// </summary>
        internal static List<DiaryFrequencyMigrationGroupSnapshot>
            FrequencyMigrationGroupSnapshots()
        {
            List<DiaryFrequencyMigrationGroupSnapshot> snapshots =
                new List<DiaryFrequencyMigrationGroupSnapshot>();
            List<DiaryInteractionGroupDef> groups =
                DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading;
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group == null || group.domain == GroupDomain.External)
                {
                    continue;
                }

                snapshots.Add(new DiaryFrequencyMigrationGroupSnapshot
                {
                    groupKey = group.defName ?? string.Empty,
                    frequencyTier = group.frequencyTier ?? string.Empty,
                    affectedByWorkWeight = group.domain == GroupDomain.Work,
                    affectedByAbilityWeight = group.domain == GroupDomain.Ability,
                    affectedByInteractionPromotionWeight = group.HasPromotionPolicy
                });
            }

            return snapshots;
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
        }

        /// <summary>Ensures the sparse frequency dictionary survives old or damaged settings XML.</summary>
        private void EnsureFrequencyDictionary()
        {
            if (groupFrequencyOverrides == null)
            {
                groupFrequencyOverrides =
                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            // Scribe usually creates Dictionary<string,float> with its default comparer. Rebuild once
            // so every runtime lookup honors stable Def keys case-insensitively.
            if (!ReferenceEquals(
                groupFrequencyOverrides.Comparer,
                StringComparer.OrdinalIgnoreCase))
            {
                Dictionary<string, float> caseInsensitive =
                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, float> entry in groupFrequencyOverrides)
                {
                    if (entry.Key != null && !caseInsensitive.ContainsKey(entry.Key))
                    {
                        caseInsensitive[entry.Key] = entry.Value;
                    }
                }

                groupFrequencyOverrides = caseInsensitive;
            }
        }

        /// <summary>Returns an explicit saved group override, preserving missing as null for migration policy.</summary>
        private bool? GroupEnabledOverride(string groupKey)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            if (group == null)
            {
                return null;
            }

            bool value;
            return groupEnabled.TryGetValue(group.defName, out value) ? (bool?)value : null;
        }

        /// <summary>
        /// Drops stale group override keys and redundant values after loading or writing settings.
        ///
        /// Invariant: this only removes (a) keys no DiaryInteractionGroupDef recognizes and (b) entries
        /// whose saved value equals the group's current XML <c>defaultEnabled</c>. It intentionally
        /// KEEPS overrides that differ from the XML default — including disabled-by-default groups a
        /// player has enabled and enabled-by-default groups a player has disabled — so legitimate
        /// player config survives XML edits and version upgrades. Package-gated overrides are also
        /// kept: while a gate is active <see cref="IsGroupEnabled"/> returns false regardless, but the
        /// override reapplies if the gate later clears. Do not "clean up" such entries here.
        /// </summary>
        private void NormalizeGroupEnabledOverrides()
        {
            EnsureGroupDictionaries();
            if (groupEnabled.Count == 0)
            {
                return;
            }

            List<string> removeKeys = null;
            foreach (KeyValuePair<string, bool> entry in groupEnabled)
            {
                DiaryInteractionGroupDef group = InteractionGroups.ByKey(entry.Key);
                bool redundant = group != null && entry.Value == group.defaultEnabled;
                if (redundant && string.Equals(group.defName,
                    CounselEventPolicy.GroupDefName, StringComparison.Ordinal))
                {
                    redundant = !CounselSettingsInheritance.ShouldStoreOverride(
                        entry.Value, group.defaultEnabled, GroupEnabledOverride("conversion"));
                }
                if (redundant && string.Equals(group.defName,
                    ConversionRitualPolicy.GroupDefName, StringComparison.Ordinal))
                {
                    redundant = !ConversionRitualSettingsInheritance.ShouldStoreOverride(
                        entry.Value, group.defaultEnabled,
                        GroupEnabledOverride(ConversionRitualPolicy.LegacyGroupDefName));
                }
                if (redundant && ReflectionSettingsInheritance.IsSplitRow(group.defName))
                {
                    redundant = !ReflectionSettingsInheritance.ShouldStoreOverride(
                        entry.Value, group.defaultEnabled,
                        GroupEnabledOverride(ReflectionSettingsInheritance.LegacyGroupDefName));
                }
                if (group == null || redundant)
                {
                    if (removeKeys == null)
                    {
                        removeKeys = new List<string>();
                    }

                    removeKeys.Add(entry.Key);
                }
            }

            if (removeKeys == null)
            {
                return;
            }

            for (int i = 0; i < removeKeys.Count; i++)
            {
                groupEnabled.Remove(removeKeys[i]);
            }
        }

        // Writing-style (persona) CRUD, normalization, and theme policy moved to PersonaPresetStore;
        // call settings.personaPresets.* directly. The reusable event-prompt override map plumbing
        // lives on PromptOverrideDictionary (settings.eventPromptOverrides / eventEnhancementOverrides
        // / eventForcedModelOverrides).
    }
}
