// RimWorld mod entry point for Pawn Diary. The detailed settings UI is split into sibling
// partial-class files, and settings-window API network state lives in ApiConnectionController.
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>Top-level page selection in the Pawn Diary settings window.</summary>
    internal enum PawnDiarySettingsTab
    {
        Main,
        Prompts,
        Styles,
        Events,
        Tuning
    }

    /// <summary>Advanced field-list filter selected by the settings window.</summary>
    internal enum AdvancedFieldFilterMode
    {
        All,
        Changed,
        Raw
    }

    /// <summary>
    /// RimWorld mod entry point. Owns the shared settings instance and delegates settings-window
    /// rendering to focused partial classes.
    /// </summary>
    public partial class PawnDiaryMod : Mod
    {
        /// <summary>The loaded mod instance; settings persistence is a mod-owned transaction.</summary>
        private static PawnDiaryMod instance;
        private static bool futureMemorySettingsWarningShown;

        /// <summary>Shared settings instance available throughout the mod.</summary>
        public static PawnDiarySettings Settings;

        /// <summary>This mod's content pack, captured at construction. Diagnostics read its RootDir to
        /// tell a Steam Workshop install from a local one; see DiaryErrorReporter.</summary>
        public static ModContentPack ModContent;

        // The settings window's API buttons start HTTP requests. This controller keeps their async
        // status, stale-result detection, and main-thread handoff out of the immediate-mode renderer.
        private readonly ApiConnectionController apiConnectionController;
        // Which prompt card is open in the settings "Prompt Studio" section.
        private string selectedPromptStudioKey;
        // Which writing-style card is open in the settings "Writing styles" section.
        private string selectedPersonaKey;
        // Which psychotype card is open in the settings "Psychotypes" section (same Styles tab).
        private string selectedPsychotypeKey;
        // Scroll position for the settings window scroll view.
        private Vector2 settingsScrollPosition;
        // Separate scroll positions keep each page from inheriting a surprising offset from another page.
        private Vector2 promptSettingsScrollPosition;
        private Vector2 styleSettingsScrollPosition;
        // Which top-level settings page is open.
        private PawnDiarySettingsTab settingsTab = PawnDiarySettingsTab.Main;
        // Which Advanced group is selected in the Advanced-tab left rail, and the live name filter.
        private string selectedAdvancedGroupKey;
        private string advancedFilter;
        private AdvancedFieldFilterMode advancedFieldFilterMode = AdvancedFieldFilterMode.All;
        // Events-tab search/collapse state is deliberately session-only: it changes presentation, not
        // capture policy. Searching temporarily expands matching domains without clearing this set.
        private string eventFilterSearch = string.Empty;
        private readonly HashSet<string> eventFilterCollapsedDomains =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        // Def databases are immutable while the settings window is open. Cache the three preset rows
        // and memoize effective-value display bands so repaint events do not rebuild adapter lists for
        // every visible event row.
        private List<DiaryFrequencyPresetDef> eventFilterFrequencyPresetDefs;
        private readonly Dictionary<float, DiaryFrequencyChoiceDef> eventFilterFrequencyDisplayCache =
            new Dictionary<float, DiaryFrequencyChoiceDef>();
        // The settings-visible group catalog and its localized labels are immutable for one loaded
        // language/mod set. Cache those 147 plain rows across IMGUI Layout/Repaint events; only search
        // text or a collapse click needs to rebuild the much smaller section projection.
        private LoadedLanguage eventFilterCatalogLanguage;
        private List<DiaryInteractionGroupDef> eventFilterCatalogSource;
        private Dictionary<string, DiaryInteractionGroupDef> eventFilterGroupByKey;
        private List<DiaryEventFilterListRowSnapshot> eventFilterCatalogRows;
        private List<DiaryEventFilterListSection> eventFilterCachedSections;
        private string eventFilterCachedSectionSearch;
        private int eventFilterCollapseRevision;
        private int eventFilterCachedCollapseRevision = -1;
        // Availability changes only when an integration reports a different capture-capability state;
        // loaded packages and Def rows are immutable for the session. Cache the sorted UI group list
        // across IMGUI events, then invalidate it on that revision or a language switch.
        private LoadedLanguage eventFilterUiGroupsLanguage;
        private int eventFilterUiGroupsCapabilityRevision = -1;
        private int eventFilterUiGroupsMutationRevision = -1;
        private List<DiaryInteractionGroupDef> eventFilterUiGroups;
        // Per-field text-entry buffers for Advanced int/float/string fields, keyed by descriptor key.
        // IMGUI redraws every frame, so without buffers each keystroke fights the live Def value.
        // advancedTextSynced stores the invariant value a buffer was last built from, so a buffer is
        // only rebuilt when the Def value changes from outside (e.g. Reset/group reset/filter clear).
        private readonly Dictionary<string, string> advancedTextBuffers = new Dictionary<string, string>();
        private readonly Dictionary<string, string> advancedTextSynced = new Dictionary<string, string>();
        private readonly HashSet<string> advancedExpandedOverrideFields = new HashSet<string>();
        private Vector2 advancedRailScroll;
        private Vector2 advancedBodyScroll;
        // Last persisted host mode, used to close the old surface only when the setting changes.
        private bool lastWrittenReaderWindowMode;
        // Set only after the verified settings stage becomes the canonical file. Integration API
        // callers use this result because Mod.WriteSettings itself cannot return a value.
        private bool lastSettingsWritePersisted;
        private string postCommitSideEffectKey = string.Empty;
        private int postCommitSideEffectStep;
        private bool postCommitPriorReaderWindowMode;

        // Measured pixel height of the settings content from the previous frame, used to size the
        // scroll view's inner rect. Starts generous so nothing clips before the first measurement;
        // afterwards it tracks the real content height so every control stays scrollable and
        // clickable as settings sections expand or collapse.
        private float lastSettingsContentHeight = 5000f;
        private float lastPromptSettingsContentHeight = 1200f;
        private float lastStyleSettingsContentHeight = 1000f;

        // Muted colors for secondary text and sub-headers, so the window reads as a hierarchy
        // instead of a flat wall of same-weight labels.
        private static readonly Color HintColor = new Color(0.72f, 0.72f, 0.72f);
        private static readonly Color AccentColor = new Color(0.50f, 0.77f, 0.60f);
        private const float PersonaTagRowHeight = 24f;
        private const float PersonaTagRowGap = 4f;
        private const float EventPromptTextAreaHeight = 88f;
        private const float SystemPromptTextAreaHeight = 138f;
        private const float PersonaRuleTextAreaHeight = 96f;
        private const float RequestTuningBlockHeight = 260f;
        private const string PromptStudioSystemPrefix = "system:";
        private const string PromptStudioEventPrefix = "event:";
        private const string ApiMoveUpSymbol = "↑";
        private const string ApiMoveDownSymbol = "↓";

        /// <summary>Initializes the mod, loading persisted settings from the save/config store.</summary>
        public PawnDiaryMod(ModContentPack content) : base(content)
        {
            instance = this;
            ModContent = content;
            Settings = GetSettings<PawnDiarySettings>();
            MemorySettingsBounds memoryBounds = MemoryPolicyDefAdapter.Bounds();
            if (Settings.memoryVersionZeroMigrationNeedsDefBounds)
            {
                Settings.ApplyMemoryPolicyFields(MemoryPolicyNormalizer.MigrateVersionZero(
                    Settings.memoryVersionZeroLegacyMaster,
                    Settings.memoryVersionZeroLegacyMode,
                    memoryBounds));
                Settings.memoryVersionZeroMigrationNeedsDefBounds = false;
            }
            MemoryPolicySnapshot initialMemoryPolicy = Settings.NormalizeMemoryPolicy(memoryBounds);
            MemoryEffectivePolicyProvider.Reset(
                Settings.memorySettingsSchemaVersion,
                initialMemoryPolicy.ToFields(),
                memoryBounds);
            if (initialMemoryPolicy.compatibilityFailClosed)
                ShowFutureMemorySettingsWarningOnce();
            lastWrittenReaderWindowMode = Settings.useDiaryReaderWindow;
            apiConnectionController = new ApiConnectionController(() => Settings);
            BeginPostCommitSideEffects(
                "startup:" + (initialMemoryPolicy.fingerprint ?? string.Empty),
                Settings.useDiaryReaderWindow);
            ResumePostCommitSideEffects();
            // Classify the install source (Workshop vs local) here on the main thread so the error
            // reporter never reads the RimWorld ModContent object from its background send thread.
            DiaryErrorReporter.CacheInstallSource(content?.RootDir);
            // Generate and persist the anonymous install id once, now, on the main thread. Doing it here
            // (rather than lazily off-thread inside the reporter, which never wrote it) keeps one stable
            // id per install so the server's distinct-install crash counts stay accurate.
            Settings.EnsureErrorReportInstallIdPersisted();
        }

        /// <summary>Returns the title shown in the RimWorld mod-settings list.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiary.Settings.Category".Translate();
        }

        /// <summary>
        /// Persists settings to disk and applies the current API lane snapshot
        /// to the shared LlmClient so connection changes take effect immediately.
        /// </summary>
        public override void WriteSettings()
        {
            lastSettingsWritePersisted = false;
            bool wasReaderWindowMode = lastWrittenReaderWindowMode;
            Settings.ClampValues();
            Settings.NormalizeEndpointUrls();
            MemorySettingsBounds bounds = MemoryPolicyDefAdapter.Bounds();
            MemoryPolicySnapshot priorMemoryPolicy = MemoryEffectivePolicyProvider.Current;
            MemorySettingsCommitPlan memoryCommit = MemoryPolicyNormalizer.PrepareCommit(
                Settings.memorySettingsSchemaVersion,
                priorMemoryPolicy.ToFields(),
                Settings.MemoryPolicyFields(),
                bounds);
            if (!memoryCommit.valid || memoryCommit.futureVersion
                || memoryCommit.snapshot.compatibilityFailClosed)
            {
                RejectSettingsWrite(priorMemoryPolicy,
                    "unsupported memorySettingsSchemaVersion");
                return;
            }
            if (!MemoryEffectivePolicyProvider.CanPublish(memoryCommit.snapshot))
            {
                RejectSettingsWrite(priorMemoryPolicy, "memory policy publication saturated");
                return;
            }
            Settings.ApplyMemoryPolicyFields(memoryCommit.candidate);

            MemorySettingsWriteResult write = MemorySettingsDurableWriter.TryWrite(
                Content.FolderName,
                GetType().Name,
                Settings);
            if (!write.persisted)
            {
                RejectSettingsWrite(priorMemoryPolicy, write.failure);
                return;
            }

            lastSettingsWritePersisted = true;
            // Persistence is the linearization point. Initialize resumable non-memory work before
            // touching the independently durable game component so a component exception cannot
            // suppress lane/debug/event-limit/title/reader-mode application.
            BeginPostCommitSideEffects(write.verifiedSha256, wasReaderWindowMode);
            try
            {
                if (!MemoryEffectivePolicyProvider.Publish(memoryCommit.snapshot))
                {
                    Log.Error("[Pawn Diary] Persisted memory settings could not be published in-process.");
                }
                else
                {
                    try
                    {
                        DiaryGameComponent.Instance?.ReconcilePublishedMemoryPolicy(
                            memoryCommit.snapshot);
                    }
                    catch (System.Exception exception)
                    {
                        // The saved fingerprint mismatch is the idempotent retry marker. The settings
                        // file and published runtime policy stay committed.
                        Log.Error("[Pawn Diary] Memory policy reconciliation will retry: " + exception);
                    }
                }
            }
            finally
            {
                ResumePostCommitSideEffects();
            }
        }

        /// <summary>Routes non-UI settings writes through the same durable transaction.</summary>
        internal static bool PersistSettingsImmediately(PawnDiarySettings settings)
        {
            if (instance == null || settings == null || !ReferenceEquals(settings, Settings))
                return false;
            instance.WriteSettings();
            return instance.lastSettingsWritePersisted;
        }

        private void RejectSettingsWrite(MemoryPolicySnapshot priorMemoryPolicy, string failure)
        {
            RestoreCompleteSettingsAfterFailedWrite(priorMemoryPolicy);
            Log.Error("[Pawn Diary] Settings were not persisted: " + (failure ?? "unknown failure"));
            Messages.Message(
                "PawnDiary.Memory.SettingsSaveFailed".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }

        private void RestoreCompleteSettingsAfterFailedWrite(MemoryPolicySnapshot priorMemoryPolicy)
        {
            try
            {
                PawnDiarySettings restored = LoadedModManager.ReadModSettings<PawnDiarySettings>(
                    Content.FolderName, GetType().Name);
                if (priorMemoryPolicy != null && !priorMemoryPolicy.compatibilityFailClosed)
                {
                    restored.memorySettingsSchemaVersion = priorMemoryPolicy.settingsSchemaVersion;
                    restored.ApplyMemoryPolicyFields(priorMemoryPolicy.ToFields());
                }
                PropertyInfo ownerProperty = typeof(ModSettings).GetProperty(
                    "Mod", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo settingsField = typeof(Mod).GetField(
                    "modSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (ownerProperty == null || settingsField == null)
                    throw new System.MissingMemberException(
                        "Verse ModSettings ownership fields changed.");
                ownerProperty.SetValue(restored, this, null);
                settingsField.SetValue(this, restored);
                Settings = restored;
            }
            catch (System.Exception exception)
            {
                // The canonical file is still untouched. Preserve at least the policy tuple if a future
                // RimWorld revision prevents replacing the complete settings object through reflection.
                if (priorMemoryPolicy != null && !priorMemoryPolicy.compatibilityFailClosed)
                    Settings.ApplyMemoryPolicyFields(priorMemoryPolicy.ToFields());
                Log.ErrorOnce(
                    "[Pawn Diary] Could not restore the complete in-memory settings object: " + exception,
                    "PawnDiary.Settings.Restore".GetHashCode());
            }
        }

        private static void ShowFutureMemorySettingsWarningOnce()
        {
            if (futureMemorySettingsWarningShown) return;
            futureMemorySettingsWarningShown = true;
            LongEventHandler.ExecuteWhenFinished(() => Messages.Message(
                "PawnDiary.Memory.SettingsFutureVersion".Translate(),
                MessageTypeDefOf.RejectInput,
                false));
        }

        private void BeginPostCommitSideEffects(string key, bool priorReaderWindowMode)
        {
            string normalized = key ?? string.Empty;
            if (string.Equals(postCommitSideEffectKey, normalized,
                System.StringComparison.Ordinal)) return;
            postCommitSideEffectKey = normalized;
            postCommitSideEffectStep = 0;
            postCommitPriorReaderWindowMode = priorReaderWindowMode;
        }

        /// <summary>
        /// Resumes the ordered shipped side effects for the current committed settings snapshot.
        /// Completed steps never repeat in this process; component-owned steps wait for a loaded game.
        /// </summary>
        internal static void ResumeCommittedSettingsSideEffects()
        {
            instance?.ResumePostCommitSideEffects();
        }

        private void ResumePostCommitSideEffects()
        {
            try
            {
                while (postCommitSideEffectStep < 6)
                {
                    switch (postCommitSideEffectStep)
                    {
                        case 0:
                            LlmClient.ApplyLaneConfiguration(Settings.ActiveEndpoints());
                            break;
                        case 1:
                            LlmClient.ApplyDebugLoggingSetting();
                            break;
                        case 2:
                            if (DiaryGameComponent.Instance == null) return;
                            DiaryGameComponent.Instance.ApplyDiaryEventLimitsFromSettings();
                            break;
                        case 3:
                            if (DiaryGameComponent.Instance == null) return;
                            DiaryGameComponent.Instance.QueueMissingTitlesFromSettings();
                            break;
                        case 4:
                            DiaryUiRouter.ApplyReaderWindowModeChange(
                                postCommitPriorReaderWindowMode,
                                Settings.useDiaryReaderWindow);
                            break;
                        case 5:
                            lastWrittenReaderWindowMode = Settings.useDiaryReaderWindow;
                            break;
                    }
                    postCommitSideEffectStep++;
                }
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] A committed settings side effect will be retried: " + exception,
                    ("PawnDiary.Settings.SideEffect." + postCommitSideEffectKey + "." +
                        postCommitSideEffectStep).GetHashCode());
            }
        }
    }
}
