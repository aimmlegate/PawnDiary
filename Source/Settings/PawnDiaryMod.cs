using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// RimWorld mod entry point. Manages the settings dialog, prompt overrides,
    /// and model discovery for the Pawn Diary mod.
    /// </summary>
    public class PawnDiaryMod : Mod
    {
        /// <summary>Shared settings instance available throughout the mod.</summary>
        public static PawnDiarySettings Settings;

        // Model IDs retrieved from the remote endpoint via FetchModels().
        private readonly List<string> fetchedModels = new List<string>();
        // Incremented on each FetchModels call so stale async results are discarded.
        private int fetchGeneration;
        // True while an async model-list request is in flight; disables the button.
        private bool isFetchingModels;
        // Which API row the current/last fetch targets, so its status + picker show on that row.
        private int fetchTargetIndex = -1;
        // Human-readable status line shown below the Fetch button. Written only on the main thread.
        private string fetchStatus;
        // Result handed back from the (possibly background-thread) FetchModels await continuation,
        // assigned as a single reference write and drained on the main thread by
        // ApplyPendingFetchResult. RimWorld's runtime may resume awaits off the main thread, and
        // .Translate() plus shared-collection edits are main-thread-only (see AGENTS.md / LlmClient),
        // so the continuation must not touch them directly.
        private volatile ModelFetchResult pendingFetchResult;
        // Connection-test state mirrors model fetching: the async HTTP continuation hands a result
        // back here and the settings window applies UI text and RimWorld log lines on the main thread.
        private int connectionTestGeneration;
        private bool isTestingConnection;
        private int connectionTestTargetIndex = -1;
        private string connectionTestStatus;
        private volatile ConnectionTestResult pendingConnectionTestResult;
        // Which prompt card is open in the settings "Prompt Studio" section.
        private string selectedPromptStudioKey;
        // Which persona card is open in the settings "Persona Presets" section.
        private string selectedPersonaKey;
        // Scroll position for the settings window scroll view.
        private Vector2 settingsScrollPosition;

        // Measured pixel height of the settings content from the previous frame, used to size the
        // scroll view's inner rect. Starts generous so nothing clips before the first measurement;
        // afterwards it tracks the real content height so every control stays scrollable and
        // clickable as settings sections expand or collapse.
        private float lastSettingsContentHeight = 5000f;

        // Muted colors for secondary text and sub-headers, so the window reads as a hierarchy
        // instead of a flat wall of same-weight labels.
        private static readonly Color HintColor = new Color(0.72f, 0.72f, 0.72f);
        private static readonly Color AccentColor = new Color(0.50f, 0.77f, 0.60f);
        private const float PersonaTagRowHeight = 24f;
        private const float PersonaTagRowGap = 4f;
        private const float EventPromptTextAreaHeight = 88f;
        private const float SystemPromptTextAreaHeight = 138f;
        private const float PersonaRuleTextAreaHeight = 96f;
        private const float RequestTuningBlockHeight = 197f;
        private const string PromptStudioSystemPrefix = "system:";
        private const string PromptStudioEventPrefix = "event:";

        /// <summary>
        /// Initializes the mod, loading persisted settings from the save/config store.
        /// </summary>
        public PawnDiaryMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiarySettings>();
            LlmClient.ApplyDebugLoggingSetting();
        }

        /// <summary>Returns the title shown in the RimWorld mod-settings list.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiary.Settings.Category".Translate();
        }

        /// <summary>
        /// Draws the full settings window: API lanes, generation controls, the prompt-text studio,
        /// and the persona-preset editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();
            Settings.EnsurePersonaPresetList();
            // Apply any model-fetch result that completed since the last frame, on the main thread.
            ApplyPendingFetchResult();
            ApplyPendingConnectionTestResult();

            Rect outRect = inRect;
            // Self-measuring scroll height: render the content, then remember how tall it actually
            // was (lastSettingsContentHeight) and reuse that next frame. This replaces a hardcoded
            // height that was too short once the settings page gained expandable editors.
            float viewHeight = Mathf.Max(lastSettingsContentHeight, EstimateSettingsContentHeight(), inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, viewHeight); // 16px reserved for the scrollbar
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);
            listing.Begin(viewRect);

            listing.Gap(4f);
            DrawApiEndpointsEditor(listing);

            SectionTitle(listing, "PawnDiary.Settings.GenerationHeader".Translate());
            listing.CheckboxLabeled(
                "PawnDiary.Settings.GenerateTitles".Translate(),
                ref Settings.generateTitles,
                "PawnDiary.Settings.GenerateTitlesTip".Translate());
            listing.CheckboxLabeled(
                "PawnDiary.Settings.EnableAtmosphericFormatting".Translate(),
                ref Settings.enableAtmosphericFormatting,
                "PawnDiary.Settings.EnableAtmosphericFormattingTip".Translate());
            listing.CheckboxLabeled(
                "PawnDiary.Settings.EnablePromptEnchantments".Translate(),
                ref Settings.enablePromptEnchantments,
                "PawnDiary.Settings.EnablePromptEnchantmentsTip".Translate());
            if (Prefs.DevMode)
            {
                listing.CheckboxLabeled(
                    "PawnDiary.Settings.PromptTestMode".Translate(),
                    ref Settings.promptTestMode,
                    "PawnDiary.Settings.PromptTestModeTip".Translate());
            }
            listing.Label("PawnDiary.Settings.WorkGenerationWeight".Translate(Settings.workGenerationWeight.ToString("0.##")));
            Settings.workGenerationWeight = listing.Slider(Settings.workGenerationWeight, 0f, 5f);
            listing.Label("PawnDiary.Settings.SocialGenerationWeight".Translate(Settings.socialGenerationWeight.ToString("0.##")));
            Settings.socialGenerationWeight = listing.Slider(Settings.socialGenerationWeight, 0f, 5f);

            DrawPromptStudio(listing);
            DrawPersonaStudio(listing);

            listing.End();
            Widgets.EndScrollView();

            // Remember the real content height so next frame's scroll view fits it exactly.
            lastSettingsContentHeight = Mathf.Max(listing.CurHeight + 24f, inRect.height);
            settingsScrollPosition.y = Mathf.Clamp(settingsScrollPosition.y, 0f, Mathf.Max(0f, lastSettingsContentHeight - outRect.height));
            Settings.ClampValues();
        }

        /// <summary>
        /// Persists settings to disk and applies the current concurrency limit
        /// to the shared LlmClient so it takes effect immediately.
        /// </summary>
        public override void WriteSettings()
        {
            Settings.ClampValues();
            Settings.NormalizeEndpointUrls();
            LlmClient.ApplyConcurrency();
            LlmClient.ApplyDebugLoggingSetting();
            base.WriteSettings();
        }

        /// <summary>
        /// Draws the list of API lanes in a compact, collapsible block. Each row stores one
        /// endpoint/key/model tuple, can be enabled or disabled, and has fetch/pick model buttons.
        /// Requests are spread across the enabled lanes in parallel (see LlmClient / QueuePrompt).
        /// </summary>
        private void DrawApiEndpointsEditor(Listing_Standard listing)
        {
            // Section title row: "Connection" on the left, the Show/Hide-models toggle on the right.
            Text.Font = GameFont.Medium;
            Rect titleRect = listing.GetRect(Text.LineHeight);
            Rect labelRect = new Rect(titleRect.x, titleRect.y, titleRect.width - 126f, titleRect.height);
            Widgets.Label(labelRect, "PawnDiary.Settings.Connection".Translate());
            Text.Font = GameFont.Small;
            Rect toggleRect = new Rect(titleRect.xMax - 118f, titleRect.y, 118f, Mathf.Min(titleRect.height, 30f));
            string toggleKey = Settings.showApiSettings ? "PawnDiary.Settings.HideModelSettings" : "PawnDiary.Settings.ShowModelSettings";
            if (Widgets.ButtonText(toggleRect, toggleKey.Translate()))
            {
                Settings.showApiSettings = !Settings.showApiSettings;
                // The model editor changes the content height by several rows. Reset the cached
                // height immediately so the scroll view does not spend a frame using the collapsed
                // size, which can leave RimWorld's scrollbar in a bad state after the first click.
                lastSettingsContentHeight = EstimateSettingsContentHeight();
                settingsScrollPosition.y = 0f;
            }
            listing.GapLine(6f);

            if (!Settings.showApiSettings)
            {
                listing.Label("PawnDiary.Settings.ApisSummary".Translate(Settings.ActiveEndpoints().Count, Settings.apiEndpoints.Count));
                return;
            }

            // Defer removal until after the loop so we don't mutate the list while drawing it.
            int removeIndex = -1;
            int moveIndex = -1;
            int moveDelta = 0;
            for (int i = 0; i < Settings.apiEndpoints.Count; i++)
            {
                ApiEndpointConfig endpoint = Settings.apiEndpoints[i];
                if (endpoint == null)
                {
                    continue;
                }

                DrawCompactApiEndpointRow(listing, i, endpoint, ref removeIndex, ref moveIndex, ref moveDelta);
            }

            if (removeIndex >= 0)
            {
                Settings.apiEndpoints.RemoveAt(removeIndex);
                // A removed row shifts indices, so any pending fetch result no longer maps cleanly.
                CancelModelFetchUiState();
                CancelConnectionTestUiState();
            }
            else if (moveIndex >= 0 && moveDelta != 0)
            {
                int targetIndex = moveIndex + moveDelta;
                if (targetIndex >= 0 && targetIndex < Settings.apiEndpoints.Count)
                {
                    ApiEndpointConfig moving = Settings.apiEndpoints[moveIndex];
                    Settings.apiEndpoints[moveIndex] = Settings.apiEndpoints[targetIndex];
                    Settings.apiEndpoints[targetIndex] = moving;
                    // A moved row shifts index-based fetch/test status and changes routing priority.
                    CancelModelFetchUiState();
                    CancelConnectionTestUiState();
                }
            }

            Rect actionRect = listing.GetRect(28f);
            Rect addRect = new Rect(actionRect.x, actionRect.y, actionRect.width / 2f - 4f, actionRect.height);
            Rect resetRect = new Rect(actionRect.x + actionRect.width / 2f + 4f, actionRect.y, actionRect.width / 2f - 4f, actionRect.height);

            if (Widgets.ButtonText(addRect, "PawnDiary.Settings.AddApi".Translate()))
            {
                Settings.apiEndpoints.Add(new ApiEndpointConfig(PawnDiarySettings.DefaultEndpointUrl, string.Empty, string.Empty));
            }

            if (Widgets.ButtonText(resetRect, "PawnDiary.Settings.ResetConnection".Translate()))
            {
                CancelModelFetchUiState();
                CancelConnectionTestUiState();
                Settings.ResetConnectionDefaults();
            }

            listing.Gap(6f);
            DrawRequestTuningBlock(listing);
        }

        /// <summary>
        /// Draws global request knobs inside the connection section so the top-level settings page
        /// stays focused on diary behavior.
        /// </summary>
        private static void DrawRequestTuningBlock(Listing_Standard listing)
        {
            Rect blockRect = listing.GetRect(RequestTuningBlockHeight);
            Widgets.DrawMenuSection(blockRect);
            Rect innerRect = blockRect.ContractedBy(8f);
            float y = innerRect.y;
            const float rowHeight = 26f;
            const float gap = 5f;

            Widgets.LabelFit(new Rect(innerRect.x, y, innerRect.width, 24f), "PawnDiary.Settings.RequestTuning".Translate());
            y += 28f;

            Rect routingRect = new Rect(innerRect.x, y, innerRect.width, rowHeight);
            DrawRoutingModeRow(routingRect, 230f);
            y += rowHeight + gap;

            Rect tempRect = new Rect(innerRect.x, y, innerRect.width, rowHeight);
            Settings.temperature = DrawSliderRow(tempRect, "PawnDiary.Settings.Temperature".Translate(Settings.temperature.ToString("0.00")), Settings.temperature, 0f, 2f);
            y += rowHeight + gap;

            Rect timeoutRect = new Rect(innerRect.x, y, innerRect.width, rowHeight);
            Settings.timeoutSeconds = Mathf.RoundToInt(DrawSliderRow(timeoutRect, "PawnDiary.Settings.TimeoutSeconds".Translate(Settings.timeoutSeconds), Settings.timeoutSeconds, 5f, 300f));
            y += rowHeight + gap;

            Rect tokensRect = new Rect(innerRect.x, y, innerRect.width, rowHeight);
            Settings.maxTokens = Mathf.RoundToInt(DrawSliderRow(tokensRect, "PawnDiary.Settings.MaxTokens".Translate(Settings.maxTokens), Settings.maxTokens, 32f, 2048f));
            y += rowHeight + gap;

            Rect concurrentRect = new Rect(innerRect.x, y, innerRect.width, rowHeight);
            Settings.maxConcurrentRequests = Mathf.RoundToInt(DrawSliderRow(concurrentRect, "PawnDiary.Settings.MaxConcurrent".Translate(Settings.maxConcurrentRequests), Settings.maxConcurrentRequests, 1f, 16f));
        }

        private static float DrawSliderRow(Rect rect, string label, float value, float min, float max)
        {
            const float labelWidth = 230f;
            Rect labelRect = new Rect(rect.x, rect.y, Mathf.Min(labelWidth, rect.width * 0.45f), rect.height);
            Rect sliderRect = new Rect(labelRect.xMax + 8f, rect.y + 4f, Mathf.Max(0f, rect.xMax - labelRect.xMax - 8f), rect.height - 8f);
            Widgets.LabelFit(labelRect, label);
            return Widgets.HorizontalSlider(sliderRect, value, min, max);
        }

        private static void DrawRoutingModeRow(Rect rect, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, Mathf.Min(labelWidth, rect.width * 0.45f), rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 8f, rect.y, Mathf.Max(0f, rect.xMax - labelRect.xMax - 8f), rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ApiRouting".Translate());
            if (Widgets.ButtonText(buttonRect, ApiRoutingLabel(Settings.apiRoutingMode).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    ApiRoutingOption(ApiLaneRoutingMode.Balanced),
                    ApiRoutingOption(ApiLaneRoutingMode.PreferTopRows),
                    ApiRoutingOption(ApiLaneRoutingMode.FailoverOnly)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static FloatMenuOption ApiRoutingOption(ApiLaneRoutingMode mode)
        {
            return new FloatMenuOption(ApiRoutingLabel(mode).Translate(), delegate
            {
                Settings.apiRoutingMode = ApiLaneSelector.Normalize(mode);
            });
        }

        private static string ApiRoutingLabel(ApiLaneRoutingMode mode)
        {
            switch (ApiLaneSelector.Normalize(mode))
            {
                case ApiLaneRoutingMode.PreferTopRows:
                    return "PawnDiary.Settings.ApiRouting.PreferTopRows";
                case ApiLaneRoutingMode.FailoverOnly:
                    return "PawnDiary.Settings.ApiRouting.FailoverOnly";
                default:
                    return "PawnDiary.Settings.ApiRouting.Balanced";
            }
        }

        /// <summary>
        /// Draws one API/model lane as a small framed block. The row is intentionally taller than
        /// the old compact version: full-width endpoint/key fields avoid the clipped labels and
        /// cramped text boxes that made the settings window hard to scan.
        /// </summary>
        private void DrawCompactApiEndpointRow(Listing_Standard listing, int index, ApiEndpointConfig endpoint, ref int removeIndex, ref int moveIndex, ref int moveDelta)
        {
            int statusLineCount = ApiRowStatusLineCount(index);
            Rect blockRect = listing.GetRect(ApiEndpointRowHeight(endpoint, statusLineCount));
            Widgets.DrawMenuSection(blockRect);

            Rect innerRect = blockRect.ContractedBy(8f);
            float lineHeight = 28f;
            float gap = 5f;

            // Row header: "API N" on the left, order controls, then Enabled and Remove controls.
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, lineHeight);
            Rect removeRect = new Rect(headerRect.xMax - 84f, headerRect.y, 84f, headerRect.height);
            Rect enabledRect = new Rect(removeRect.x - 106f, headerRect.y, 98f, headerRect.height);
            Rect downRect = new Rect(enabledRect.x - 42f, headerRect.y, 36f, headerRect.height);
            Rect upRect = new Rect(downRect.x - 42f, headerRect.y, 36f, headerRect.height);
            Rect headerLabelRect = new Rect(headerRect.x, headerRect.y, Mathf.Max(0f, upRect.x - headerRect.x - 6f), headerRect.height);
            Widgets.Label(headerLabelRect, "PawnDiary.Settings.ApiLabel".Translate(index + 1));
            if (ButtonTextFit(upRect, "PawnDiary.Settings.MoveApiUp".Translate()) && index > 0)
            {
                moveIndex = index;
                moveDelta = -1;
            }

            if (ButtonTextFit(downRect, "PawnDiary.Settings.MoveApiDown".Translate()) && index < Settings.apiEndpoints.Count - 1)
            {
                moveIndex = index;
                moveDelta = 1;
            }

            Widgets.CheckboxLabeled(enabledRect, "PawnDiary.Settings.ApiEnabled".Translate(), ref endpoint.enabled);
            if (Settings.apiEndpoints.Count > 1)
            {
                if (ButtonTextFit(removeRect, "PawnDiary.Settings.RemoveApi".Translate()))
                {
                    removeIndex = index;
                }
            }

            float y = headerRect.yMax + gap;
            Rect modeRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            DrawCompatibilityModeRow(modeRect, endpoint, 94f);

            y += lineHeight + gap;
            Rect endpointRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            endpoint.url = DrawCompactTextField(endpointRect, "PawnDiary.Settings.Endpoint".Translate(), endpoint.url, 94f);

            y += lineHeight + gap;
            Rect modelLineRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            float pickButtonWidth = 84f;
            float fetchButtonWidth = 144f;
            Rect pickRect = new Rect(modelLineRect.xMax - pickButtonWidth, modelLineRect.y, pickButtonWidth, modelLineRect.height);
            Rect fetchRect = new Rect(pickRect.x - gap - fetchButtonWidth, modelLineRect.y, fetchButtonWidth, modelLineRect.height);
            Rect modelRect = new Rect(modelLineRect.x, modelLineRect.y, fetchRect.x - modelLineRect.x - gap, modelLineRect.height);
            endpoint.model = DrawCompactTextField(modelRect, "PawnDiary.Settings.ModelName".Translate(), endpoint.model, 94f);
            DrawModelButtons(fetchRect, pickRect, index, endpoint);

            y += lineHeight + gap;
            Rect keyRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            const float testButtonWidth = 96f;
            Rect testRect = new Rect(keyRect.xMax - testButtonWidth, keyRect.y, testButtonWidth, keyRect.height);
            Rect keyFieldRect = new Rect(keyRect.x, keyRect.y, keyRect.width - testButtonWidth - gap, keyRect.height);
            endpoint.apiKey = DrawCompactTextField(keyFieldRect, "PawnDiary.Settings.ApiKey".Translate(), endpoint.apiKey, 94f);
            DrawConnectionTestButton(testRect, index);

            y += lineHeight + gap;
            Rect authRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            DrawAuthModeRow(authRect, endpoint, 94f);

            if (HasApiAdvancedRow(endpoint))
            {
                y += lineHeight + gap;
                Rect advancedRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
                DrawApiAdvancedRow(advancedRect, endpoint, 94f);
            }

            // Show the fetch status under the row that triggered the fetch, inside the framed lane
            // so it cannot push later controls sideways or overlap adjacent rows.
            if (statusLineCount > 0)
            {
                y += lineHeight + gap;
                Rect statusRect = new Rect(innerRect.x + 94f, y, innerRect.width - 94f, 22f);
                DrawApiRowStatuses(statusRect, index);
            }

            listing.Gap(6f);
        }

        /// <summary>
        /// Returns the fixed height needed by one framed API row. Kept as a helper so drawing and
        /// scroll-height estimation agree when reasoning/thinking controls appear.
        /// </summary>
        private static float ApiEndpointRowHeight(ApiEndpointConfig endpoint, int statusLineCount)
        {
            float height = HasApiAdvancedRow(endpoint) ? 247f : 214f;
            return height + (Mathf.Max(0, statusLineCount) * 26f);
        }

        private static bool HasApiAdvancedRow(ApiEndpointConfig endpoint)
        {
            return endpoint != null
                && (endpoint.apiMode == ApiCompatibilityMode.OpenAIResponses
                    || endpoint.apiMode == ApiCompatibilityMode.OllamaNativeChat);
        }

        /// <summary>
        /// Draws the compatibility-mode selector for one API lane.
        /// </summary>
        private static void DrawCompatibilityModeRow(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ApiCompatibility".Translate());
            if (Widgets.ButtonText(buttonRect, ApiCompatibilityLabel(endpoint.apiMode).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    ApiCompatibilityOption(endpoint, ApiCompatibilityMode.OpenAIChatCompletions),
                    ApiCompatibilityOption(endpoint, ApiCompatibilityMode.OpenAIResponses),
                    ApiCompatibilityOption(endpoint, ApiCompatibilityMode.OllamaNativeChat)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static FloatMenuOption ApiCompatibilityOption(ApiEndpointConfig endpoint, ApiCompatibilityMode mode)
        {
            return new FloatMenuOption(ApiCompatibilityLabel(mode).Translate(), delegate
            {
                ApiCompatibilityMode oldMode = endpoint.apiMode;
                endpoint.apiMode = mode;
                endpoint.reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort);

                // A fresh default row should become useful immediately when the user picks Ollama.
                if (oldMode != ApiCompatibilityMode.OllamaNativeChat
                    && mode == ApiCompatibilityMode.OllamaNativeChat
                    && string.Equals(endpoint.url, PawnDiarySettings.DefaultEndpointUrl, StringComparison.OrdinalIgnoreCase))
                {
                    endpoint.url = PawnDiarySettings.DefaultOllamaEndpointUrl;
                }
                else if (oldMode == ApiCompatibilityMode.OllamaNativeChat
                    && mode != ApiCompatibilityMode.OllamaNativeChat
                    && string.Equals(endpoint.url, PawnDiarySettings.DefaultOllamaEndpointUrl, StringComparison.OrdinalIgnoreCase))
                {
                    endpoint.url = PawnDiarySettings.DefaultEndpointUrl;
                }
            });
        }

        private static string ApiCompatibilityLabel(ApiCompatibilityMode mode)
        {
            switch (mode)
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return "PawnDiary.Settings.ApiCompatibility.Responses";
                case ApiCompatibilityMode.OllamaNativeChat:
                    return "PawnDiary.Settings.ApiCompatibility.Ollama";
                default:
                    return "PawnDiary.Settings.ApiCompatibility.Chat";
            }
        }

        /// <summary>Draws the API-key transport style selector for one API lane.</summary>
        private static void DrawAuthModeRow(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ApiAuthMode".Translate());
            if (Widgets.ButtonText(buttonRect, ApiAuthModeLabel(endpoint.authMode).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    ApiAuthModeOption(endpoint, ApiAuthMode.BearerToken),
                    ApiAuthModeOption(endpoint, ApiAuthMode.None),
                    ApiAuthModeOption(endpoint, ApiAuthMode.ApiKeyHeader),
                    ApiAuthModeOption(endpoint, ApiAuthMode.XApiKeyHeader),
                    ApiAuthModeOption(endpoint, ApiAuthMode.QueryParameterKey)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static FloatMenuOption ApiAuthModeOption(ApiEndpointConfig endpoint, ApiAuthMode authMode)
        {
            return new FloatMenuOption(ApiAuthModeLabel(authMode).Translate(), delegate
            {
                endpoint.authMode = PawnDiarySettings.NormalizeAuthMode(authMode);
            });
        }

        private static string ApiAuthModeLabel(ApiAuthMode authMode)
        {
            switch (PawnDiarySettings.NormalizeAuthMode(authMode))
            {
                case ApiAuthMode.None:
                    return "PawnDiary.Settings.ApiAuthMode.None";
                case ApiAuthMode.ApiKeyHeader:
                    return "PawnDiary.Settings.ApiAuthMode.ApiKeyHeader";
                case ApiAuthMode.XApiKeyHeader:
                    return "PawnDiary.Settings.ApiAuthMode.XApiKeyHeader";
                case ApiAuthMode.QueryParameterKey:
                    return "PawnDiary.Settings.ApiAuthMode.QueryParameterKey";
                default:
                    return "PawnDiary.Settings.ApiAuthMode.Bearer";
            }
        }

        /// <summary>
        /// Draws the small mode-specific option row: reasoning effort for OpenAI Responses, or
        /// native thinking output for Ollama.
        /// </summary>
        private static void DrawApiAdvancedRow(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);

            if (endpoint.apiMode == ApiCompatibilityMode.OllamaNativeChat)
            {
                Widgets.LabelFit(labelRect, "PawnDiary.Settings.OllamaThinking".Translate());
                string labelKey = endpoint.ollamaThink ? "PawnDiary.Settings.ToggleOn" : "PawnDiary.Settings.ToggleOff";
                if (Widgets.ButtonText(buttonRect, labelKey.Translate()))
                {
                    endpoint.ollamaThink = !endpoint.ollamaThink;
                }

                return;
            }

            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ReasoningEffort".Translate());
            if (Widgets.ButtonText(buttonRect, ReasoningEffortLabel(endpoint.reasoningEffort).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                AddReasoningOption(options, endpoint, PawnDiarySettings.DefaultReasoningEffort);
                AddReasoningOption(options, endpoint, "none");
                AddReasoningOption(options, endpoint, "minimal");
                AddReasoningOption(options, endpoint, "low");
                AddReasoningOption(options, endpoint, "medium");
                AddReasoningOption(options, endpoint, "high");
                AddReasoningOption(options, endpoint, "xhigh");
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static void AddReasoningOption(List<FloatMenuOption> options, ApiEndpointConfig endpoint, string effort)
        {
            options.Add(new FloatMenuOption(ReasoningEffortLabel(effort).Translate(), delegate
            {
                endpoint.reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(effort);
            }));
        }

        private static string ReasoningEffortLabel(string effort)
        {
            switch (PawnDiarySettings.NormalizeReasoningEffort(effort))
            {
                case "none":
                    return "PawnDiary.Settings.ReasoningEffort.None";
                case "minimal":
                    return "PawnDiary.Settings.ReasoningEffort.Minimal";
                case "low":
                    return "PawnDiary.Settings.ReasoningEffort.Low";
                case "medium":
                    return "PawnDiary.Settings.ReasoningEffort.Medium";
                case "high":
                    return "PawnDiary.Settings.ReasoningEffort.High";
                case "xhigh":
                    return "PawnDiary.Settings.ReasoningEffort.XHigh";
                default:
                    return "PawnDiary.Settings.ReasoningEffort.Default";
            }
        }

        /// <summary>
        /// Draws a short label and text field inside one row. This mirrors TextEntryLabeled but lets
        /// two settings share a line without clipping labels into neighboring controls.
        /// </summary>
        private static string DrawCompactTextField(Rect rect, string label, string value, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            Widgets.LabelFit(labelRect, label);
            return Widgets.TextField(fieldRect, value ?? string.Empty);
        }

        /// <summary>Draws one muted status label without changing the caller's font or color.</summary>
        private static void DrawMutedLabel(Rect rect, string text)
        {
            Color previousColor = GUI.color;
            GUI.color = HintColor;
            Widgets.LabelFit(rect, text ?? string.Empty);
            GUI.color = previousColor;
        }

        /// <summary>Draws a compact field label inside an editor block.</summary>
        private static void DrawFieldLabel(Rect rect, string text)
        {
            Widgets.LabelFit(rect, text ?? string.Empty);
        }

        /// <summary>
        /// Draws a standard RimWorld button with LabelFit text, which keeps long translated labels
        /// readable in fixed-width settings rows.
        /// </summary>
        private static bool ButtonTextFit(Rect rect, string label)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect labelRect = new Rect(
                rect.x + 6f,
                rect.y + 2f,
                Mathf.Max(0f, rect.width - 12f),
                Mathf.Max(0f, rect.height - 4f));
            Widgets.LabelFit(labelRect, label ?? string.Empty);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            return clicked;
        }

        /// <summary>
        /// Draws the per-row Fetch + Pick buttons. "Fetch models" queries that row's endpoint;
        /// "Pick fetched model" opens a menu of the results to set the row's model.
        /// </summary>
        private void DrawModelButtons(Rect fetchRect, Rect pickRect, int index, ApiEndpointConfig endpoint)
        {
            bool fetchingThis = isFetchingModels && fetchTargetIndex == index;
            if (Widgets.ButtonText(fetchRect, (fetchingThis ? "PawnDiary.Settings.FetchingModels" : "PawnDiary.Settings.FetchModels").Translate()) && !isFetchingModels)
            {
                FetchModels(index);
            }

            if (Widgets.ButtonText(pickRect, "PawnDiary.Settings.PickModel".Translate()))
            {
                List<FloatMenuOption> options;
                if (fetchTargetIndex == index && fetchedModels.Count > 0)
                {
                    options = fetchedModels
                        .Distinct()
                        .OrderBy(model => model)
                        .Select(model => new FloatMenuOption(model, delegate { endpoint.model = model; }))
                        .ToList();
                }
                else
                {
                    options = new List<FloatMenuOption> { new FloatMenuOption("PawnDiary.Settings.NoModelsYet".Translate(), null) };
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawConnectionTestButton(Rect rect, int index)
        {
            bool testingThis = isTestingConnection && connectionTestTargetIndex == index;
            string label = testingThis ? "PawnDiary.Settings.TestingConnection" : "PawnDiary.Settings.TestConnection";
            if (ButtonTextFit(rect, label.Translate()) && !isTestingConnection)
            {
                TestApiConnection(index);
            }
        }

        private int ApiRowStatusLineCount(int index)
        {
            int count = 0;
            if (fetchTargetIndex == index && !string.IsNullOrEmpty(fetchStatus))
            {
                count++;
            }

            if (connectionTestTargetIndex == index && !string.IsNullOrEmpty(connectionTestStatus))
            {
                count++;
            }

            return count;
        }

        private void DrawApiRowStatuses(Rect firstLineRect, int index)
        {
            Rect lineRect = firstLineRect;
            if (fetchTargetIndex == index && !string.IsNullOrEmpty(fetchStatus))
            {
                DrawMutedLabel(lineRect, fetchStatus);
                lineRect.y += 24f;
            }

            if (connectionTestTargetIndex == index && !string.IsNullOrEmpty(connectionTestStatus))
            {
                DrawMutedLabel(lineRect, connectionTestStatus);
            }
        }

        /// <summary>
        /// Draws the compact prompt editor. System prompts and event-source prompts share one
        /// selector so only the selected prompt block is open at a time.
        /// </summary>
        private void DrawPromptStudio(Listing_Standard listing)
        {
            List<DiaryEventPromptDef> eventPromptDefs = EventPromptDefsForSettings();
            int customizedEventTypes = Settings.CustomizedEventPromptCount();
            List<PromptStudioOption> options = PromptStudioOptions(eventPromptDefs);

            Text.Font = GameFont.Medium;
            Rect titleRect = listing.GetRect(Text.LineHeight);
            Rect labelRect = new Rect(titleRect.x, titleRect.y, titleRect.width - 126f, titleRect.height);
            Widgets.Label(labelRect, "PawnDiary.Settings.PromptStudioTitle".Translate());
            Text.Font = GameFont.Small;
            Rect toggleRect = new Rect(titleRect.xMax - 118f, titleRect.y, 118f, Mathf.Min(titleRect.height, 30f));
            string toggleKey = Settings.showPromptStudio ? "PawnDiary.Settings.HidePromptStudio" : "PawnDiary.Settings.ShowPromptStudio";
            if (Widgets.ButtonText(toggleRect, toggleKey.Translate()))
            {
                Settings.showPromptStudio = !Settings.showPromptStudio;
                lastSettingsContentHeight = EstimateSettingsContentHeight();
                settingsScrollPosition.y = 0f;
            }
            listing.GapLine(6f);

            if (!Settings.showPromptStudio)
            {
                listing.Label("PawnDiary.Settings.PromptStudioSummary".Translate(
                    DiaryPromptTemplates.LoadedTemplateCount,
                    eventPromptDefs.Count,
                    customizedEventTypes));
                return;
            }

            if (options.Count == 0)
            {
                listing.Label("PawnDiary.Settings.EventPromptNoneLoaded".Translate());
                return;
            }

            DrawPromptStudioBlock(listing, options, SelectedPromptStudioOption(options));
        }

        private void DrawPromptStudioBlock(Listing_Standard listing, List<PromptStudioOption> options, PromptStudioOption selected)
        {
            Rect cardRect = listing.GetRect(PromptStudioBlockHeight(selected));
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);
            float y = innerRect.y;

            Rect selectorRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            DrawPromptStudioSelector(selectorRect, options, selected);
            y += 34f;

            bool selectedEventPrompt = selected != null && selected.IsEvent;
            Rect statusRect = new Rect(innerRect.x, y, selectedEventPrompt ? innerRect.width - 188f : innerRect.width, 24f);
            DrawMutedLabel(
                statusRect,
                (selected != null && selected.IsCustomized()
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());
            if (selectedEventPrompt)
            {
                Rect resetAllRect = new Rect(innerRect.xMax - 180f, y - 2f, 180f, 28f);
                if (ButtonTextFit(resetAllRect, "PawnDiary.Settings.ResetEventPromptOverrides".Translate()))
                {
                    Settings.ResetAllEventPromptOverrides();
                }
            }
            y += 32f;

            if (selected == null)
            {
                return;
            }

            if (selected.IsEvent)
            {
                DrawSelectedEventPromptEditor(innerRect, ref y, selected.eventPromptDef);
            }
            else
            {
                DrawSelectedSystemPromptEditor(innerRect, ref y, selected);
            }
        }

        private void DrawPromptStudioSelector(Rect rect, List<PromptStudioOption> options, PromptStudioOption selected)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PromptTypePickerHeader".Translate());

            string selectedLabel = selected == null
                ? "PawnDiary.Settings.EventPromptNoneLoaded".Translate().ToString()
                : selected.label;
            if (ButtonTextFit(buttonRect, selectedLabel))
            {
                List<FloatMenuOption> menuOptions = options
                    .Select(option =>
                    {
                        PromptStudioOption captured = option;
                        return new FloatMenuOption(captured.label, delegate
                        {
                            selectedPromptStudioKey = captured.key;
                        });
                    })
                    .ToList();

                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
        }

        private void DrawSelectedEventPromptEditor(Rect innerRect, ref float y, DiaryEventPromptDef selected)
        {
            string eventKey = EventPromptKeyForSettings(selected);
            string currentPrompt = Settings.EffectiveEventPrompt(eventKey, selected.prompt);
            string currentEnhancement = Settings.EffectiveEventEnhancement(eventKey, selected.enhancement);

            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.EventPromptPromptField".Translate());
            y += 22f;
            Rect promptRect = new Rect(innerRect.x, y, innerRect.width, EventPromptTextAreaHeight);
            string editedPrompt = Widgets.TextArea(promptRect, currentPrompt ?? string.Empty);
            if (!string.Equals(editedPrompt, currentPrompt ?? string.Empty, StringComparison.Ordinal))
            {
                Settings.SetEventPromptOverride(eventKey, editedPrompt, selected.prompt);
            }

            y += EventPromptTextAreaHeight + 8f;
            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.EventPromptEnhancementField".Translate());
            y += 22f;
            Rect enhancementRect = new Rect(innerRect.x, y, innerRect.width, EventPromptTextAreaHeight);
            string editedEnhancement = Widgets.TextArea(enhancementRect, currentEnhancement ?? string.Empty);
            if (!string.Equals(editedEnhancement, currentEnhancement ?? string.Empty, StringComparison.Ordinal))
            {
                Settings.SetEventEnhancementOverride(eventKey, editedEnhancement, selected.enhancement);
            }

            y += EventPromptTextAreaHeight + 10f;
            Rect promptResetRect = new Rect(innerRect.x, y, innerRect.width / 2f - 4f, 30f);
            Rect enhancementResetRect = new Rect(innerRect.x + innerRect.width / 2f + 4f, y, innerRect.width / 2f - 4f, 30f);
            if (ButtonTextFit(promptResetRect, "PawnDiary.Settings.RestoreEventPromptDefault".Translate()))
            {
                Settings.ResetEventPromptOverride(eventKey);
            }

            if (ButtonTextFit(enhancementResetRect, "PawnDiary.Settings.RestoreEventEnhancementDefault".Translate()))
            {
                Settings.ResetEventEnhancementOverride(eventKey);
            }
        }

        private static void DrawSelectedSystemPromptEditor(Rect innerRect, ref float y, PromptStudioOption selected)
        {
            string before = selected.CurrentPrompt() ?? string.Empty;
            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.SystemPromptField".Translate());
            y += 22f;

            Rect promptRect = new Rect(innerRect.x, y, innerRect.width, SystemPromptTextAreaHeight);
            string edited = Widgets.TextArea(promptRect, before);
            if (!string.Equals(edited, before, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(edited)
                    || string.Equals(edited, selected.DefaultPrompt() ?? string.Empty, StringComparison.Ordinal))
                {
                    selected.ResetOverride();
                }
                else
                {
                    selected.SetOverride(edited);
                }
            }

            y += SystemPromptTextAreaHeight + 10f;
            Rect restoreRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            if (ButtonTextFit(restoreRect, "PawnDiary.Settings.RestorePromptDefault".Translate()))
            {
                selected.ResetOverride();
            }
        }

        private List<PromptStudioOption> PromptStudioOptions(List<DiaryEventPromptDef> eventPromptDefs)
        {
            List<PromptStudioOption> options = new List<PromptStudioOption>
            {
                SystemPromptOption(
                    "diary",
                    "PawnDiary.Settings.SystemPromptDiary".Translate(),
                    Settings.EffectiveSystemPrompt,
                    () => DiaryPrompts.Current.systemPrompt,
                    Settings.HasSystemPromptOverride,
                    Settings.SetSystemPromptOverride,
                    Settings.ResetSystemPromptOverride),
                SystemPromptOption(
                    "reflection",
                    "PawnDiary.Settings.SystemPromptReflection".Translate(),
                    Settings.EffectiveReflectionSystemPrompt,
                    () => DiaryPrompts.Current.systemPromptReflection,
                    Settings.HasReflectionSystemPromptOverride,
                    Settings.SetReflectionSystemPromptOverride,
                    Settings.ResetReflectionSystemPromptOverride),
                SystemPromptOption(
                    "neutral",
                    "PawnDiary.Settings.SystemPromptNeutral".Translate(),
                    Settings.EffectiveNeutralSystemPrompt,
                    () => DiaryPrompts.Current.systemPromptNeutral,
                    Settings.HasNeutralSystemPromptOverride,
                    Settings.SetNeutralSystemPromptOverride,
                    Settings.ResetNeutralSystemPromptOverride),
                SystemPromptOption(
                    "title",
                    "PawnDiary.Settings.SystemPromptTitle".Translate(),
                    Settings.EffectiveTitleSystemPrompt,
                    () => DiaryPrompts.Current.titleSystemPrompt,
                    Settings.HasTitleSystemPromptOverride,
                    Settings.SetTitleSystemPromptOverride,
                    Settings.ResetTitleSystemPromptOverride)
            };

            if (eventPromptDefs != null)
            {
                foreach (DiaryEventPromptDef def in eventPromptDefs)
                {
                    string eventKey = EventPromptKeyForSettings(def);
                    options.Add(new PromptStudioOption
                    {
                        key = PromptStudioEventPrefix + eventKey,
                        label = EventPromptOptionLabel(def),
                        eventPromptDef = def,
                        IsCustomized = () => Settings.HasEventPromptOverride(eventKey) || Settings.HasEventEnhancementOverride(eventKey)
                    });
                }
            }

            return options;
        }

        private static PromptStudioOption SystemPromptOption(
            string key,
            string label,
            Func<string> currentPrompt,
            Func<string> defaultPrompt,
            Func<bool> isCustomized,
            Action<string> setOverride,
            Action resetOverride)
        {
            return new PromptStudioOption
            {
                key = PromptStudioSystemPrefix + key,
                label = label,
                CurrentPrompt = currentPrompt,
                DefaultPrompt = defaultPrompt,
                IsCustomized = isCustomized,
                SetOverride = setOverride,
                ResetOverride = resetOverride
            };
        }

        private PromptStudioOption SelectedPromptStudioOption(List<PromptStudioOption> options)
        {
            PromptStudioOption selected = options.FirstOrDefault(option =>
                string.Equals(option.key, selectedPromptStudioKey, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault();
            if (selected != null)
            {
                selectedPromptStudioKey = selected.key;
            }

            return selected;
        }

        private static float PromptStudioBlockHeight(PromptStudioOption option)
        {
            return option != null && option.IsEvent ? 362f : 300f;
        }

        private static List<DiaryEventPromptDef> EventPromptDefsForSettings()
        {
            List<DiaryEventPromptDef> defs = DefDatabase<DiaryEventPromptDef>.AllDefsListForReading;
            if (defs == null)
            {
                return new List<DiaryEventPromptDef>();
            }

            return defs
                .Where(def => def != null)
                .OrderBy(EventPromptLabelForUi)
                .ToList();
        }

        private static string EventPromptOptionLabel(DiaryEventPromptDef def)
        {
            string label = EventPromptLabelForUi(def);
            string key = EventPromptKeyForSettings(def);
            return string.Equals(label, key, StringComparison.OrdinalIgnoreCase)
                ? label
                : label + " (" + key + ")";
        }

        private static string EventPromptLabelForUi(DiaryEventPromptDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string label = def.LabelCap.ToString();
            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, def.defName, StringComparison.Ordinal))
            {
                return EventPromptKeyForSettings(def);
            }

            return label;
        }

        private static string EventPromptKeyForSettings(DiaryEventPromptDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(def.eventType) ? def.defName ?? string.Empty : def.eventType;
        }

        /// <summary>
        /// Draws the editable persona catalog as one compact highlighted block.
        /// </summary>
        private void DrawPersonaStudio(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.PersonaStudioTitle".Translate());
            DrawPersonaStudioBlock(listing);
        }

        private void DrawPersonaStudioBlock(Listing_Standard listing)
        {
            int total = DiaryPersonas.All.Count;
            int custom = Settings.CustomPersonas().Count;
            int customized = Settings.personaPresets == null ? 0 : Settings.personaPresets.Count(preset => preset != null && !preset.custom);
            float tagPickerHeight = PersonaTagPickerHeight();

            Rect cardRect = listing.GetRect(412f + tagPickerHeight);
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);
            float y = innerRect.y;

            Widgets.LabelFit(
                new Rect(innerRect.x, y, innerRect.width, 24f),
                "PawnDiary.Settings.PersonaStudioSummary".Translate(total, custom, customized));
            y += 30f;

            Rect addRect = new Rect(innerRect.x, y, innerRect.width / 2f - 4f, 30f);
            Rect clearRect = new Rect(innerRect.x + innerRect.width / 2f + 4f, y, innerRect.width / 2f - 4f, 30f);
            if (ButtonTextFit(addRect, "PawnDiary.Settings.AddPersonaPreset".Translate()))
            {
                selectedPersonaKey = Settings.AddCustomPersona();
            }

            if (ButtonTextFit(clearRect, "PawnDiary.Settings.ResetPersonaPresets".Translate()))
            {
                Settings.ResetPersonaPresets();
                selectedPersonaKey = null;
            }

            y += 38f;

            List<DiaryPersonaDef> personas = DiaryPersonas.All
                .OrderBy(PersonaLabelForUi)
                .ToList();
            DiaryPersonaDef selected = SelectedPersonaForSettings();
            DrawPersonaSelector(new Rect(innerRect.x, y, innerRect.width, 28f), personas, selected);
            y += 38f;

            if (selected != null)
            {
                DrawSelectedPersonaFields(innerRect, ref y, selected, tagPickerHeight);
            }
        }

        private void DrawPersonaSelector(Rect rect, List<DiaryPersonaDef> personas, DiaryPersonaDef selected)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PersonaPickerHeader".Translate());
            string selectedLabel = selected == null
                ? "PawnDiary.Persona.DefaultLabel".Translate().ToString()
                : PersonaOptionLabel(selected);
            if (ButtonTextFit(buttonRect, selectedLabel))
            {
                List<FloatMenuOption> options = personas
                    .Select(persona =>
                    {
                        DiaryPersonaDef option = persona;
                        return new FloatMenuOption(PersonaOptionLabel(option), delegate
                        {
                            selectedPersonaKey = option.defName;
                        });
                    })
                    .ToList();

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("PawnDiary.Persona.DefaultLabel".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawSelectedPersonaFields(Rect innerRect, ref float y, DiaryPersonaDef selected, float tagPickerHeight)
        {
            bool custom = Settings.CustomPersonaFor(selected.defName) != null;
            DiaryPersonaDef baseDef = BasePersonaForSettings(selected);
            PersonaPresetConfig overridePreset = custom ? Settings.CustomPersonaFor(selected.defName) : Settings.PersonaOverrideFor(selected.defName);
            string currentLabel = overridePreset?.label ?? baseDef?.label ?? string.Empty;
            string currentRule = overridePreset?.rule ?? baseDef?.rule ?? string.Empty;
            List<string> currentThemes = new List<string>(overridePreset?.themes ?? baseDef?.themes ?? new List<string>());

            Widgets.LabelFit(new Rect(innerRect.x, y, innerRect.width - 132f, 24f), "PawnDiary.Settings.EditingPersona".Translate(PersonaLabelForUi(selected)));
            DrawAccentLabel(
                new Rect(innerRect.xMax - 124f, y, 124f, 24f),
                (custom
                    ? "PawnDiary.Settings.PersonaBadgeCustom"
                    : "PawnDiary.Settings.PersonaBadgeBuiltIn").Translate());

            y += 24f;
            DrawMutedLabel(
                new Rect(innerRect.x, y, innerRect.width, 20f),
                (IsPersonaCustomized(selected.defName)
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());

            y += 24f;
            Rect labelFieldRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            string editedLabel = DrawCompactTextField(labelFieldRect, "PawnDiary.Settings.PersonaLabel".Translate(), currentLabel, 86f);

            y += 34f;
            Rect ruleLabelRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            DrawFieldLabel(ruleLabelRect, "PawnDiary.Settings.PersonaRule".Translate());
            y += 22f;
            Rect ruleRect = new Rect(innerRect.x, y, innerRect.width, PersonaRuleTextAreaHeight);
            string editedRule = Widgets.TextArea(ruleRect, currentRule);

            y += PersonaRuleTextAreaHeight + 8f;
            Rect tagsLabelRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            DrawFieldLabel(tagsLabelRect, "PawnDiary.Settings.PersonaTags".Translate());
            y += 22f;
            List<string> editedThemes = DrawPersonaTagPicker(new Rect(innerRect.x, y, innerRect.width, tagPickerHeight), currentThemes, custom);

            bool changed = !string.Equals(editedLabel, currentLabel, StringComparison.Ordinal)
                || !string.Equals(editedRule, currentRule, StringComparison.Ordinal)
                || !editedThemes.SequenceEqual(currentThemes);
            if (changed)
            {
                if (custom)
                {
                    PersonaPresetConfig customPreset = Settings.CustomPersonaFor(selected.defName);
                    if (customPreset != null)
                    {
                        customPreset.label = editedLabel ?? string.Empty;
                        customPreset.rule = editedRule ?? string.Empty;
                        customPreset.themes = editedThemes;
                        DiaryPersonas.InvalidateCache();
                    }
                }
                else
                {
                    bool matchesDefault = string.Equals(editedLabel, baseDef?.label ?? string.Empty, StringComparison.Ordinal)
                        && string.Equals(editedRule, baseDef?.rule ?? string.Empty, StringComparison.Ordinal)
                        && editedThemes.SequenceEqual(baseDef?.themes ?? new List<string>());
                    if (matchesDefault)
                    {
                        Settings.ResetPersonaOverride(selected.defName);
                    }
                    else
                    {
                        Settings.SetPersonaOverride(
                            selected.defName,
                            editedLabel,
                            editedRule,
                            editedThemes);
                    }
                }
            }

            y += tagPickerHeight + 10f;
            Rect actionRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            if (custom)
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.DeletePersonaPreset".Translate()))
                {
                    Settings.RemoveCustomPersona(selected.defName);
                    selectedPersonaKey = null;
                }
            }
            else
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.RestorePersonaPreset".Translate()))
                {
                    Settings.ResetPersonaOverride(selected.defName);
                }
            }
        }

        private List<string> DrawPersonaTagPicker(Rect rect, List<string> themes, bool requireAtLeastOneTag)
        {
            List<string> selected = themes == null ? new List<string>() : new List<string>(themes);
            float gap = 8f;
            float columnWidth = (rect.width - gap) / 2f;
            float y = rect.y;
            for (int i = 0; i < DiaryPersonas.PredefinedThemeTags.Length; i += 2)
            {
                DrawPersonaTagToggle(new Rect(rect.x, y, columnWidth, PersonaTagRowHeight), DiaryPersonas.PredefinedThemeTags[i], selected, requireAtLeastOneTag);
                if (i + 1 < DiaryPersonas.PredefinedThemeTags.Length)
                {
                    DrawPersonaTagToggle(new Rect(rect.x + columnWidth + gap, y, columnWidth, PersonaTagRowHeight), DiaryPersonas.PredefinedThemeTags[i + 1], selected, requireAtLeastOneTag);
                }

                y += PersonaTagRowHeight + PersonaTagRowGap;
            }

            return selected;
        }

        private static void DrawPersonaTagToggle(Rect rect, string tag, List<string> selected, bool requireAtLeastOneTag)
        {
            bool enabled = selected.Contains(tag);
            bool before = enabled;
            Widgets.CheckboxLabeled(rect, PersonaTagLabel(tag), ref enabled);
            if (enabled == before)
            {
                return;
            }

            if (enabled)
            {
                if (!selected.Contains(tag))
                {
                    selected.Add(tag);
                }
            }
            else
            {
                if (!requireAtLeastOneTag || selected.Count > 1)
                {
                    selected.Remove(tag);
                }
            }
        }

        private DiaryPersonaDef SelectedPersonaForSettings()
        {
            DiaryPersonaDef selected = DiaryPersonas.All.FirstOrDefault(persona => persona.defName == selectedPersonaKey);
            if (selected != null)
            {
                return selected;
            }

            selected = DiaryPersonas.All.FirstOrDefault();
            selectedPersonaKey = selected?.defName;
            return selected;
        }

        private static DiaryPersonaDef BasePersonaForSettings(DiaryPersonaDef effective)
        {
            if (effective == null || string.IsNullOrWhiteSpace(effective.defName))
            {
                return effective;
            }

            return DefDatabase<DiaryPersonaDef>.GetNamedSilentFail(effective.defName) ?? effective;
        }

        private bool IsPersonaCustomized(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return false;
            }

            return Settings.PersonaOverrideFor(defName) != null || Settings.CustomPersonaFor(defName) != null;
        }

        private string PersonaOptionLabel(DiaryPersonaDef persona)
        {
            string label = PersonaLabelForUi(persona);
            return IsPersonaCustomized(persona?.defName) ? label + " *" : label;
        }

        private static string PersonaLabelForUi(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

        private static string PersonaTagLabel(string tag)
        {
            return ("PawnDiary.Settings.PersonaTag." + tag).Translate();
        }

        private static float PersonaTagPickerHeight()
        {
            float rows = Mathf.Ceil(DiaryPersonas.PredefinedThemeTags.Length / 2f);
            return Mathf.Max(PersonaTagRowHeight, (rows * PersonaTagRowHeight) + (Mathf.Max(0f, rows - 1f) * PersonaTagRowGap));
        }

        /// <summary>
        /// Returns a conservative current-frame height for the settings scroll view. The exact
        /// height is still measured from <see cref="Listing_Standard.CurHeight"/> after drawing,
        /// but this estimate prevents one-frame stale heights when the API/model editor is opened.
        /// </summary>
        private float EstimateSettingsContentHeight()
        {
            Settings.EnsureEndpointsList();

            float height = 4f;

            // Connection section. API rows are framed blocks; one may include a fetch status line.
            height += Text.LineHeight + 20f;
            if (Settings.showApiSettings)
            {
                foreach (ApiEndpointConfig endpoint in Settings.apiEndpoints)
                {
                    height += ApiEndpointRowHeight(endpoint, 0) + 6f;
                }
                height += 38f; // Add API / Reset row
                height += RequestTuningBlockHeight + 6f;
            }
            else
            {
                height += 34f; // compact summary
            }

            // Generation controls, compact prompt studio, and persona-preset studio.
            height += 250f;
            if (Prefs.DevMode)
            {
                height += 30f;
            }
            height += Settings.showPromptStudio ? 430f : 44f;
            height += 460f + PersonaTagPickerHeight();

            return height + 120f; // breathing room for translated labels and RimWorld skin variance
        }

        /// <summary>
        /// Draws a section title (medium font) with a divider line beneath it, giving the settings
        /// window a clear visual hierarchy instead of a flat run of same-size labels.
        /// </summary>
        private static void SectionTitle(Listing_Standard listing, string label)
        {
            listing.Gap(10f);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Rect rect = listing.GetRect(Text.LineHeight);
            Widgets.LabelFit(rect, label);
            Text.Font = previousFont;
            listing.GapLine(6f);
        }

        /// <summary>
        /// Draws a small accent label without permanently changing the caller's GUI state.
        /// </summary>
        private static void DrawAccentLabel(Rect rect, string text)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = AccentColor;
            Widgets.LabelFit(rect, text ?? string.Empty);
            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Sends a tiny real generation request through one API row to verify endpoint, key, model,
        /// and compatibility mode. All UI/log work stays on the main thread; the await continuation
        /// only writes a result object for ApplyPendingConnectionTestResult to consume.
        /// </summary>
        private async void TestApiConnection(int index)
        {
            int generation = ++connectionTestGeneration;
            string url = string.Empty;
            string apiKey = string.Empty;
            string model = string.Empty;
            ApiAuthMode authMode = ApiAuthMode.BearerToken;
            ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
            string reasoningEffort = PawnDiarySettings.DefaultReasoningEffort;
            bool ollamaThink = false;
            try
            {
                isTestingConnection = true;
                connectionTestTargetIndex = index;
                connectionTestStatus = "PawnDiary.Settings.TestingConnection".Translate();

                if (Settings?.apiEndpoints == null || index < 0 || index >= Settings.apiEndpoints.Count)
                {
                    isTestingConnection = false;
                    connectionTestStatus = null;
                    return;
                }

                ApiEndpointConfig endpoint = Settings.apiEndpoints[index];
                if (endpoint == null)
                {
                    throw new InvalidOperationException("Endpoint row is missing.");
                }

                url = endpoint.url;
                apiKey = endpoint.apiKey;
                model = endpoint.model;
                authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode);
                apiMode = endpoint.apiMode;
                reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort);
                ollamaThink = endpoint.ollamaThink;
                int timeoutSeconds = Settings.timeoutSeconds;
                float temperature = Settings.temperature;
                string prompt = "PawnDiary.Settings.ConnectionTestPrompt".Translate();
                string validationError = ConnectionTestValidationError(url, model);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    isTestingConnection = false;
                    connectionTestStatus = "PawnDiary.Settings.ConnectionTestFailed".Translate(validationError);
                    Log.Warning("[PawnDiary debug] API connection check failed for " + ConnectionTestLaneLabel(url, model, apiMode)
                        + ": " + validationError);
                    return;
                }

                string sampleText = await LlmClient.TestConnection(new ApiEndpointConfig(url, apiKey, model)
                {
                    authMode = authMode,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort,
                    ollamaThink = ollamaThink
                }, prompt, timeoutSeconds, temperature);

                pendingConnectionTestResult = new ConnectionTestResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = true,
                    sampleText = sampleText,
                    endpointUrl = url,
                    apiKey = apiKey,
                    model = model,
                    authMode = authMode,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort,
                    ollamaThink = ollamaThink
                };
            }
            catch (Exception ex)
            {
                pendingConnectionTestResult = new ConnectionTestResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = false,
                    errorDetail = ex.Message,
                    endpointUrl = url,
                    apiKey = apiKey,
                    model = model,
                    authMode = authMode,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort,
                    ollamaThink = ollamaThink
                };
            }
        }

        private static string ConnectionTestValidationError(string url, string model)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "PawnDiary.Settings.ConnectionTestMissingEndpoint".Translate();
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return "PawnDiary.Settings.ConnectionTestMissingModel".Translate();
            }

            return null;
        }

        /// <summary>
        /// Fetches the list of available model IDs from one API row's endpoint asynchronously,
        /// and auto-fills that row's model if it has none yet. Uses a generation counter so stale
        /// results from earlier (or reset) requests are discarded.
        /// </summary>
        private async void FetchModels(int index)
        {
            int generation = ++fetchGeneration; // capture current generation to detect stale completions
            string url = string.Empty;
            string apiKey = string.Empty;
            ApiAuthMode authMode = ApiAuthMode.BearerToken;
            ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
            try
            {
                isFetchingModels = true;
                fetchTargetIndex = index;
                fetchStatus = "PawnDiary.Settings.FetchingModels".Translate(); // main thread here

                // Snapshot the inputs on the main thread. The await continuation may resume on a
                // background thread in RimWorld's runtime, so it must not read game state, call
                // .Translate(), or edit shared collections — it only hands a result back via
                // pendingFetchResult, which the main-thread draw (ApplyPendingFetchResult) consumes.
                if (Settings?.apiEndpoints == null || index < 0 || index >= Settings.apiEndpoints.Count)
                {
                    isFetchingModels = false;
                    return;
                }

                ApiEndpointConfig endpoint = Settings.apiEndpoints[index];
                if (endpoint == null)
                {
                    throw new InvalidOperationException("Endpoint row is missing.");
                }

                url = endpoint.url;
                apiKey = endpoint.apiKey;
                authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode);
                apiMode = endpoint.apiMode;
                int timeoutSeconds = Settings.timeoutSeconds;

                List<string> models = await ModelListClient.FetchModels(url, apiKey, authMode, apiMode, timeoutSeconds);
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = true,
                    models = models,
                    endpointUrl = url,
                    apiKey = apiKey,
                    authMode = authMode,
                    apiMode = apiMode
                };
            }
            catch (Exception ex)
            {
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = false,
                    errorDetail = ex.Message,
                    endpointUrl = url,
                    apiKey = apiKey,
                    authMode = authMode,
                    apiMode = apiMode
                };
            }
        }

        /// <summary>
        /// Drains a completed model fetch on the main thread: applies the localized status line, the
        /// fetched model list, and the optional auto-fill of a blank model. Called at the top of
        /// DoSettingsWindowContents so the (possibly background-thread) FetchModels continuation never
        /// touches RimWorld state, localization, or the shared model list itself.
        /// </summary>
        private void ApplyPendingFetchResult()
        {
            ModelFetchResult result = pendingFetchResult;
            if (result == null)
            {
                return;
            }

            pendingFetchResult = null;

            // Ignore a result from a superseded fetch (Reset or a newer fetch bumped the generation).
            if (result.generation != fetchGeneration)
            {
                return;
            }

            isFetchingModels = false;
            if (!FetchTargetStillMatches(result))
            {
                fetchTargetIndex = -1;
                fetchedModels.Clear();
                fetchStatus = null;
                return;
            }

            if (!result.success)
            {
                fetchStatus = "PawnDiary.Settings.FetchFailed".Translate(result.errorDetail ?? string.Empty);
                return;
            }

            fetchedModels.Clear();
            if (result.models != null)
            {
                fetchedModels.AddRange(result.models);
            }

            if (fetchedModels.Count == 0)
            {
                fetchStatus = "PawnDiary.Settings.NoModelsReturned".Translate();
                return;
            }

            fetchStatus = "PawnDiary.Settings.ModelsFound".Translate(fetchedModels.Count);

            // Auto-fill only a blank/placeholder model, and only if the target row still exists.
            if (result.targetIndex >= 0 && result.targetIndex < Settings.apiEndpoints.Count)
            {
                ApiEndpointConfig endpoint = Settings.apiEndpoints[result.targetIndex];
                if (endpoint != null
                    && (string.IsNullOrWhiteSpace(endpoint.model) || endpoint.model == PawnDiarySettings.DefaultModelName))
                {
                    endpoint.model = fetchedModels[0];
                }
            }
        }

        /// <summary>
        /// Applies a completed API connection test on the main thread: updates the row status and
        /// writes a concise RimWorld log line with no API key.
        /// </summary>
        private void ApplyPendingConnectionTestResult()
        {
            ConnectionTestResult result = pendingConnectionTestResult;
            if (result == null)
            {
                return;
            }

            pendingConnectionTestResult = null;
            if (result.generation != connectionTestGeneration)
            {
                return;
            }

            isTestingConnection = false;
            if (!ConnectionTestTargetStillMatches(result))
            {
                connectionTestTargetIndex = -1;
                connectionTestStatus = null;
                return;
            }

            if (result.success)
            {
                connectionTestStatus = "PawnDiary.Settings.ConnectionTestSucceeded".Translate(TrimForStatus(result.sampleText));
                Log.Message("[PawnDiary debug] API connection check succeeded for " + ConnectionTestLaneLabel(result)
                    + " sample=\"" + TrimForLog(result.sampleText) + "\"");
            }
            else
            {
                connectionTestStatus = "PawnDiary.Settings.ConnectionTestFailed".Translate(TrimForStatus(result.errorDetail));
                Log.Warning("[PawnDiary debug] API connection check failed for " + ConnectionTestLaneLabel(result)
                    + ": " + TrimForLog(result.errorDetail));
            }
        }

        /// <summary>
        /// Invalidates any in-flight model-list fetch and clears the per-row picker state.
        /// </summary>
        private void CancelModelFetchUiState()
        {
            fetchGeneration++;
            isFetchingModels = false;
            fetchTargetIndex = -1;
            pendingFetchResult = null;
            fetchedModels.Clear();
            fetchStatus = null;
        }

        /// <summary>
        /// Invalidates any in-flight connection test and clears its row status.
        /// </summary>
        private void CancelConnectionTestUiState()
        {
            connectionTestGeneration++;
            isTestingConnection = false;
            connectionTestTargetIndex = -1;
            pendingConnectionTestResult = null;
            connectionTestStatus = null;
        }

        /// <summary>
        /// Returns true when the row that requested a model list still points at the same endpoint.
        /// A user can edit or remove rows while the HTTP request is in flight.
        /// </summary>
        private bool FetchTargetStillMatches(ModelFetchResult result)
        {
            if (result == null || result.targetIndex < 0 || result.targetIndex >= Settings.apiEndpoints.Count)
            {
                return false;
            }

            ApiEndpointConfig endpoint = Settings.apiEndpoints[result.targetIndex];
            return endpoint != null
                && string.Equals(endpoint.url ?? string.Empty, result.endpointUrl ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(endpoint.apiKey ?? string.Empty, result.apiKey ?? string.Empty, StringComparison.Ordinal)
                && PawnDiarySettings.NormalizeAuthMode(endpoint.authMode) == result.authMode
                && endpoint.apiMode == result.apiMode;
        }

        /// <summary>
        /// Returns true when the API row still matches the exact configuration that was tested.
        /// </summary>
        private bool ConnectionTestTargetStillMatches(ConnectionTestResult result)
        {
            if (result == null || result.targetIndex < 0 || result.targetIndex >= Settings.apiEndpoints.Count)
            {
                return false;
            }

            ApiEndpointConfig endpoint = Settings.apiEndpoints[result.targetIndex];
            return endpoint != null
                && string.Equals(endpoint.url ?? string.Empty, result.endpointUrl ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(endpoint.apiKey ?? string.Empty, result.apiKey ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(endpoint.model ?? string.Empty, result.model ?? string.Empty, StringComparison.Ordinal)
                && PawnDiarySettings.NormalizeAuthMode(endpoint.authMode) == result.authMode
                && endpoint.apiMode == result.apiMode
                && string.Equals(PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort), result.reasoningEffort ?? string.Empty, StringComparison.Ordinal)
                && endpoint.ollamaThink == result.ollamaThink;
        }

        private static string ConnectionTestLaneLabel(ConnectionTestResult result)
        {
            if (result == null)
            {
                return "<null>";
            }

            return ConnectionTestLaneLabel(result.endpointUrl, result.model, result.apiMode);
        }

        private static string ConnectionTestLaneLabel(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            string model = string.IsNullOrWhiteSpace(modelName) ? "<blank-model>" : modelName;
            string endpoint = string.IsNullOrWhiteSpace(endpointUrl)
                ? "<blank-url>"
                : EndpointUtility.BuildGenerationUrl(SanitizeEndpointUrlForLog(endpointUrl), apiMode);
            return model + " [" + apiMode + "] @ " + endpoint;
        }

        private static string SanitizeEndpointUrlForLog(string endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return string.Empty;
            }

            int query = endpointUrl.IndexOf('?');
            int fragment = endpointUrl.IndexOf('#');
            int cut = -1;
            if (query >= 0)
            {
                cut = query;
            }

            if (fragment >= 0 && (cut < 0 || fragment < cut))
            {
                cut = fragment;
            }

            return cut >= 0 ? endpointUrl.Substring(0, cut) : endpointUrl;
        }

        private static string TrimForStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = OneLine(value);
            return value.Length <= 80 ? value : value.Substring(0, 80) + "...";
        }

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = OneLine(value);
            return value.Length <= 180 ? value : value.Substring(0, 180) + "...";
        }

        private static string OneLine(string value)
        {
            return (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }

        // One selectable row in Prompt Studio. Event rows edit DiaryEventPromptDef prompt fields;
        // system rows edit one saved system-prompt override.
        private sealed class PromptStudioOption
        {
            public string key;
            public string label;
            public DiaryEventPromptDef eventPromptDef;
            public Func<string> CurrentPrompt;
            public Func<string> DefaultPrompt;
            public Func<bool> IsCustomized;
            public Action<string> SetOverride;
            public Action ResetOverride;

            public bool IsEvent
            {
                get { return eventPromptDef != null; }
            }
        }

        // Result of one model fetch, handed from the await continuation to the main-thread draw.
        // Never mutated after construction; assigned to pendingFetchResult as a single reference write.
        private sealed class ModelFetchResult
        {
            public int generation;
            public int targetIndex;
            public bool success;
            public List<string> models;   // immutable snapshot returned by ModelListClient
            public string errorDetail;    // raw, untranslated exception message
            public string endpointUrl;     // row URL snapshot used to reject stale row edits
            public string apiKey;          // row key snapshot; never logged or shown
            public ApiAuthMode authMode;   // auth style snapshot; changing it changes request shape
            public ApiCompatibilityMode apiMode; // row mode snapshot; changing modes changes model-list shape
        }

        // Result of one connection test, handed from the await continuation to the main-thread draw.
        // Contains the API key only for stale-row matching; it is never displayed or logged.
        private sealed class ConnectionTestResult
        {
            public int generation;
            public int targetIndex;
            public bool success;
            public string sampleText;
            public string errorDetail;
            public string endpointUrl;
            public string apiKey;
            public string model;
            public ApiAuthMode authMode;
            public ApiCompatibilityMode apiMode;
            public string reasoningEffort;
            public bool ollamaThink;
        }
    }

}
