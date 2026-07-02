// API-lane settings UI for Pawn Diary. This partial class owns the immediate-mode controls for
// endpoint rows, while ApiConnectionController owns the async fetch/test state behind the buttons.
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        // Per-endpoint "show this key in cleartext" choices for the open settings window. Keyed by the
        // ApiEndpointConfig instance so the choice survives row reordering, and defaulting to absent
        // means every key starts masked. Session-only UI state; never saved.
        private static readonly HashSet<ApiEndpointConfig> revealedApiKeys = new HashSet<ApiEndpointConfig>();

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
                // A removed row shifts indices, so any pending fetch/test result no longer maps cleanly.
                apiConnectionController.CancelUiState();
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
                    apiConnectionController.CancelUiState();
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
                apiConnectionController.CancelUiState();
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
            if (ButtonSymbolWithTip(upRect, ApiMoveUpSymbol, "PawnDiary.Settings.MoveApiUp".Translate()) && index > 0)
            {
                moveIndex = index;
                moveDelta = -1;
            }

            if (ButtonSymbolWithTip(downRect, ApiMoveDownSymbol, "PawnDiary.Settings.MoveApiDown".Translate()) && index < Settings.apiEndpoints.Count - 1)
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
            DrawApiKeyField(keyFieldRect, endpoint, 94f);
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

            // Show statuses inside the framed lane so they cannot push later controls sideways.
            if (statusLineCount > 0)
            {
                y += lineHeight + gap;
                Rect statusRect = new Rect(innerRect.x + 94f, y, innerRect.width - 94f, 22f);
                DrawApiRowStatuses(statusRect, index);
            }

            listing.Gap(6f);
        }

        /// <summary>
        /// Draws the API-key row with the key masked by default and a Show/Hide toggle. A key is never
        /// rendered in cleartext until the player explicitly reveals that row, so it is not exposed on
        /// screen (shoulder-surfing, screenshots, streaming). Reveal state is per-endpoint and
        /// session-only (see <see cref="revealedApiKeys"/>).
        /// </summary>
        private static void DrawApiKeyField(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            const float gap = 4f;
            const float toggleWidth = 52f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect toggleRect = new Rect(rect.xMax - toggleWidth, rect.y, toggleWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + gap, rect.y, Mathf.Max(0f, toggleRect.x - labelRect.xMax - gap - gap), rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ApiKey".Translate());

            bool revealed = revealedApiKeys.Contains(endpoint);
            if (revealed)
            {
                endpoint.apiKey = Widgets.TextField(fieldRect, endpoint.apiKey ?? string.Empty);
            }
            else
            {
                // Masked, non-editable display: a flat box with bullets, never the real key text.
                Widgets.DrawBoxSolid(fieldRect, new Color(0f, 0f, 0f, 0.12f));
                Rect maskedRect = new Rect(fieldRect.x + 4f, fieldRect.y, Mathf.Max(0f, fieldRect.width - 8f), fieldRect.height);
                DrawMutedLabel(maskedRect, MaskedApiKey(endpoint.apiKey));
            }

            string toggleKey = revealed ? "PawnDiary.Settings.HideApiKey" : "PawnDiary.Settings.RevealApiKey";
            if (ButtonTextFit(toggleRect, toggleKey.Translate()))
            {
                if (revealed)
                {
                    revealedApiKeys.Remove(endpoint);
                }
                else
                {
                    revealedApiKeys.Add(endpoint);
                }
            }

            TooltipHandler.TipRegion(toggleRect, "PawnDiary.Settings.RevealApiKeyTip".Translate());
        }

        /// <summary>Bullet-only stand-in for a hidden API key; length is capped so a long key
        /// cannot widen the row or hint at the real length.</summary>
        private static string MaskedApiKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return new string('•', Mathf.Clamp(key.Length, 1, 16));
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
                && (endpoint.apiMode == ApiCompatibilityMode.OpenAIChatCompletions
                    || endpoint.apiMode == ApiCompatibilityMode.OpenAIResponses);
        }

        /// <summary>Draws the compatibility-mode selector for one API lane.</summary>
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
                    ApiCompatibilityOption(endpoint, ApiCompatibilityMode.OpenAIResponses)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static FloatMenuOption ApiCompatibilityOption(ApiEndpointConfig endpoint, ApiCompatibilityMode mode)
        {
            return new FloatMenuOption(ApiCompatibilityLabel(mode).Translate(), delegate
            {
                endpoint.apiMode = PawnDiarySettings.NormalizeApiMode(mode);
                endpoint.reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort);
            });
        }

        private static string ApiCompatibilityLabel(ApiCompatibilityMode mode)
        {
            switch (mode)
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return "PawnDiary.Settings.ApiCompatibility.Responses";
                default:
                    return "PawnDiary.Settings.ApiCompatibility.Chat";
            }
        }

        /// <summary>Draws the API-key transport style selector for one API lane.</summary>
        private static void DrawAuthModeRow(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            bool customHeader = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode) == ApiAuthMode.CustomHeader;
            float headerWidth = customHeader ? Mathf.Min(190f, Mathf.Max(120f, rect.width * 0.34f)) : 0f;
            Rect buttonRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - headerWidth - (customHeader ? 8f : 4f), rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.ApiAuthMode".Translate());
            if (Widgets.ButtonText(buttonRect, ApiAuthModeLabel(endpoint.authMode).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    ApiAuthModeOption(endpoint, ApiAuthMode.BearerToken),
                    ApiAuthModeOption(endpoint, ApiAuthMode.None),
                    ApiAuthModeOption(endpoint, ApiAuthMode.CustomHeader),
                    ApiAuthModeOption(endpoint, ApiAuthMode.QueryParameterKey)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // Query-parameter auth puts the key in the request URL, where the endpoint's own server
            // logs can capture it. Warn so players reach for a header-based mode when one is available.
            if (PawnDiarySettings.NormalizeAuthMode(endpoint.authMode) == ApiAuthMode.QueryParameterKey)
            {
                TooltipHandler.TipRegion(buttonRect, "PawnDiary.Settings.QueryKeyAuthCaution".Translate());
            }

            if (customHeader)
            {
                Rect headerRect = new Rect(buttonRect.xMax + 4f, rect.y, Mathf.Max(0f, rect.xMax - buttonRect.xMax - 4f), rect.height);
                endpoint.customAuthHeaderName = Widgets.TextField(headerRect, endpoint.customAuthHeaderName ?? string.Empty);
                TooltipHandler.TipRegion(headerRect, "PawnDiary.Settings.CustomAuthHeaderTip".Translate());
            }
        }

        private static FloatMenuOption ApiAuthModeOption(ApiEndpointConfig endpoint, ApiAuthMode authMode)
        {
            return new FloatMenuOption(ApiAuthModeLabel(authMode).Translate(), delegate
            {
                endpoint.authMode = PawnDiarySettings.NormalizeAuthMode(authMode);
                if (endpoint.authMode == ApiAuthMode.CustomHeader)
                {
                    endpoint.customAuthHeaderName = ApiEndpointPolicy.NormalizeCustomHeaderName(endpoint.customAuthHeaderName);
                }
            });
        }

        private static string ApiAuthModeLabel(ApiAuthMode authMode)
        {
            switch (PawnDiarySettings.NormalizeAuthMode(authMode))
            {
                case ApiAuthMode.None:
                    return "PawnDiary.Settings.ApiAuthMode.None";
                case ApiAuthMode.CustomHeader:
                    return "PawnDiary.Settings.ApiAuthMode.CustomHeader";
                case ApiAuthMode.QueryParameterKey:
                    return "PawnDiary.Settings.ApiAuthMode.QueryParameterKey";
                default:
                    return "PawnDiary.Settings.ApiAuthMode.Bearer";
            }
        }

        /// <summary>Draws the small mode-specific option row for OpenAI-compatible reasoning effort.</summary>
        private static void DrawApiAdvancedRow(Rect rect, ApiEndpointConfig endpoint, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);

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
        /// Draws the per-row Fetch + Pick buttons. "Fetch models" queries that row's endpoint;
        /// "Pick fetched model" opens a menu of the results to set the row's model.
        /// </summary>
        private void DrawModelButtons(Rect fetchRect, Rect pickRect, int index, ApiEndpointConfig endpoint)
        {
            bool fetchingThis = apiConnectionController.IsFetchingModels && apiConnectionController.FetchTargetIndex == index;
            if (Widgets.ButtonText(fetchRect, (fetchingThis ? "PawnDiary.Settings.FetchingModels" : "PawnDiary.Settings.FetchModels").Translate())
                && !apiConnectionController.IsFetchingModels)
            {
                apiConnectionController.FetchModels(index);
            }

            if (Widgets.ButtonText(pickRect, "PawnDiary.Settings.PickModel".Translate()))
            {
                List<FloatMenuOption> options;
                if (apiConnectionController.FetchTargetIndex == index && apiConnectionController.FetchedModels.Count > 0)
                {
                    options = apiConnectionController.FetchedModels
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
            // Per-row gate: only this row's own test blocks its own button, so testing one row never
            // freezes the others. Each row tracks its own in-flight state in the controller.
            bool testingThis = apiConnectionController.IsTestingConnection(index);
            string label = testingThis ? "PawnDiary.Settings.TestingConnection" : "PawnDiary.Settings.TestConnection";
            if (ButtonTextFit(rect, label.Translate()) && !testingThis)
            {
                apiConnectionController.TestApiConnection(index);
            }
        }

        private int ApiRowStatusLineCount(int index)
        {
            return apiConnectionController.StatusLineCount(index);
        }

        private void DrawApiRowStatuses(Rect firstLineRect, int index)
        {
            Rect lineRect = firstLineRect;
            string fetchStatus = apiConnectionController.ModelFetchStatusForRow(index);
            if (!string.IsNullOrEmpty(fetchStatus))
            {
                DrawMutedLabel(lineRect, fetchStatus);
                lineRect.y += 24f;
            }

            string connectionStatus = apiConnectionController.ConnectionTestStatusForRow(index);
            if (!string.IsNullOrEmpty(connectionStatus))
            {
                DrawMutedLabel(lineRect, connectionStatus);
            }
        }
    }
}
