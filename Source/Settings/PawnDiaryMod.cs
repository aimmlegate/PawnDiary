// RimWorld mod entry point for Pawn Diary. The detailed settings UI is split into sibling
// partial-class files, and settings-window API network state lives in ApiConnectionController.
using System.Collections.Generic;
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
        // Per-field text-entry buffers for Advanced int/float/string fields, keyed by descriptor key.
        // IMGUI redraws every frame, so without buffers each keystroke fights the live Def value.
        // advancedTextSynced stores the invariant value a buffer was last built from, so a buffer is
        // only rebuilt when the Def value changes from outside (e.g. Reset/group reset/filter clear).
        private readonly Dictionary<string, string> advancedTextBuffers = new Dictionary<string, string>();
        private readonly Dictionary<string, string> advancedTextSynced = new Dictionary<string, string>();
        private readonly HashSet<string> advancedExpandedOverrideFields = new HashSet<string>();
        private Vector2 advancedRailScroll;
        private Vector2 advancedBodyScroll;

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
        private const float RequestTuningBlockHeight = 198f;
        private const string PromptStudioSystemPrefix = "system:";
        private const string PromptStudioEventPrefix = "event:";
        private const string ApiMoveUpSymbol = "↑";
        private const string ApiMoveDownSymbol = "↓";

        /// <summary>Initializes the mod, loading persisted settings from the save/config store.</summary>
        public PawnDiaryMod(ModContentPack content) : base(content)
        {
            ModContent = content;
            Settings = GetSettings<PawnDiarySettings>();
            apiConnectionController = new ApiConnectionController(() => Settings);
            LlmClient.ApplyDebugLoggingSetting();
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
            Settings.ClampValues();
            Settings.NormalizeEndpointUrls();
            LlmClient.ApplyLaneConfiguration(Settings.ActiveEndpoints());
            LlmClient.ApplyDebugLoggingSetting();
            DiaryGameComponent.Instance?.ApplyDiaryEventLimitsFromSettings();
            DiaryGameComponent.Instance?.QueueMissingTitlesFromSettings();
            base.WriteSettings();
        }
    }
}
