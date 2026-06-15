using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// RimWorld mod entry point. Manages the settings dialog, model discovery,
    /// and interaction-group configuration for the Pawn Diary mod.
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
        // Human-readable status line shown below the Fetch button.
        private string fetchStatus;
        // DefName of the interaction group currently selected in the instruction editor.
        private string selectedGroupKey;
        // Scroll position for the settings window scroll view.
        private Vector2 settingsScrollPosition;
        // Ephemeral text buffer for the per-group instruction text area.
        private string instructionEditBuffer;
        // Tracks which group instructionEditBuffer belongs to; when the selected group
        // changes the buffer is refreshed from settings to avoid stale edits.
        private string instructionEditGroupKey;

        /// <summary>
        /// Initializes the mod, loading persisted settings from the save/config store.
        /// </summary>
        public PawnDiaryMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiarySettings>();
        }

        /// <summary>Returns the title shown in the RimWorld mod-settings list.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiary.Settings.Category".Translate();
        }

        /// <summary>
        /// Draws the full settings window: the API lanes editor (parallel endpoints + models),
        /// paired-POV toggle, per-lane concurrency control, system prompt, and the per-group
        /// instruction editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();

            Rect outRect = inRect;
            // Height grows with the visible API rows so the scroll view always fits them.
            float apiEditorHeight = Settings.showApiSettings ? 92f + Settings.apiEndpoints.Count * 98f : 58f;
            float viewHeight = 1300f + apiEditorHeight;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, viewHeight); // 16px reserved for the scrollbar
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);
            listing.Begin(viewRect);

            DrawApiEndpointsEditor(listing);

            listing.Gap(12f);
            listing.CheckboxLabeled(
                "PawnDiary.Settings.PairedPov".Translate(),
                ref Settings.dualPovGeneration,
                "PawnDiary.Settings.PairedPovTip".Translate());

            listing.Label("PawnDiary.Settings.MaxConcurrent".Translate(Settings.maxConcurrentRequests));
            Settings.maxConcurrentRequests = Mathf.RoundToInt(listing.Slider(Settings.maxConcurrentRequests, 1f, 16f));
            listing.Label("PawnDiary.Settings.MaxConcurrentHelp".Translate());

            listing.GapLine();
            listing.Label("PawnDiary.Settings.SystemPrompt".Translate());
            Rect systemPromptRect = listing.GetRect(150f, 1f);
            Settings.systemPrompt = Widgets.TextArea(systemPromptRect, Settings.systemPrompt ?? string.Empty);
            if (listing.ButtonText("PawnDiary.Settings.RestoreSystemPrompt".Translate()))
            {
                Settings.systemPrompt = PawnDiarySettings.DefaultSystemPrompt;
            }

            listing.GapLine();
            DrawInteractionGroupsEditor(listing);

            listing.End();
            Widgets.EndScrollView();
            Settings.ClampValues();
        }

        /// <summary>
        /// Persists settings to disk and applies the current concurrency limit
        /// to the shared LlmClient so it takes effect immediately.
        /// </summary>
        public override void WriteSettings()
        {
            Settings.ClampValues();
            LlmClient.ApplyConcurrency();
            base.WriteSettings();
        }

        /// <summary>
        /// Draws the list of API lanes in a compact, collapsible block. Each row stores one
        /// endpoint/key/model tuple, can be enabled or disabled, and has fetch/pick model buttons.
        /// Requests are spread across the enabled lanes in parallel (see LlmClient / QueuePrompt).
        /// </summary>
        private void DrawApiEndpointsEditor(Listing_Standard listing)
        {
            Rect titleRect = listing.GetRect(28f);
            Rect labelRect = new Rect(titleRect.x, titleRect.y, titleRect.width - 126f, titleRect.height);
            Widgets.Label(labelRect, "PawnDiary.Settings.ApisHeader".Translate());
            Rect toggleRect = new Rect(titleRect.xMax - 118f, titleRect.y, 118f, titleRect.height);
            string toggleKey = Settings.showApiSettings ? "PawnDiary.Settings.HideModelSettings" : "PawnDiary.Settings.ShowModelSettings";
            if (Widgets.ButtonText(toggleRect, toggleKey.Translate()))
            {
                Settings.showApiSettings = !Settings.showApiSettings;
            }

            if (!Settings.showApiSettings)
            {
                listing.Label("PawnDiary.Settings.ApisSummary".Translate(Settings.ActiveEndpoints().Count, Settings.apiEndpoints.Count));
                listing.GapLine();
                return;
            }

            listing.Gap(2f);

            // Defer removal until after the loop so we don't mutate the list while drawing it.
            int removeIndex = -1;
            for (int i = 0; i < Settings.apiEndpoints.Count; i++)
            {
                ApiEndpointConfig endpoint = Settings.apiEndpoints[i];
                if (endpoint == null)
                {
                    continue;
                }

                DrawCompactApiEndpointRow(listing, i, endpoint, ref removeIndex);
            }

            if (removeIndex >= 0)
            {
                Settings.apiEndpoints.RemoveAt(removeIndex);
                // A removed row shifts indices, so any pending fetch result no longer maps cleanly.
                fetchTargetIndex = -1;
                fetchedModels.Clear();
                fetchStatus = null;
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
                fetchGeneration++;
                Settings.ResetConnectionDefaults();
                fetchTargetIndex = -1;
                fetchedModels.Clear();
                fetchStatus = null;
            }
        }

        /// <summary>
        /// Draws a compact API/model row using two thin field lines instead of three tall labeled
        /// entries, so adding several models stays readable in RimWorld's narrow settings window.
        /// </summary>
        private void DrawCompactApiEndpointRow(Listing_Standard listing, int index, ApiEndpointConfig endpoint, ref int removeIndex)
        {
            // Row header: "API N" on the left, then Enabled and Remove controls on the right.
            Rect headerRect = listing.GetRect(28f);
            Rect headerLabelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 230f, headerRect.height);
            Widgets.Label(headerLabelRect, "PawnDiary.Settings.ApiLabel".Translate(index + 1));
            Rect enabledRect = new Rect(headerRect.xMax - 220f, headerRect.y, 110f, headerRect.height);
            Widgets.CheckboxLabeled(enabledRect, "PawnDiary.Settings.ApiEnabled".Translate(), ref endpoint.enabled);
            if (Settings.apiEndpoints.Count > 1)
            {
                Rect removeRect = new Rect(headerRect.xMax - 100f, headerRect.y, 100f, headerRect.height);
                if (Widgets.ButtonText(removeRect, "PawnDiary.Settings.RemoveApi".Translate()))
                {
                    removeIndex = index;
                }
            }

            Rect firstLineRect = listing.GetRect(28f);
            float halfWidth = firstLineRect.width / 2f - 4f;
            Rect endpointRect = new Rect(firstLineRect.x, firstLineRect.y, halfWidth, firstLineRect.height);
            Rect modelRect = new Rect(firstLineRect.x + halfWidth + 8f, firstLineRect.y, halfWidth, firstLineRect.height);
            endpoint.url = DrawCompactTextField(endpointRect, "PawnDiary.Settings.Endpoint".Translate(), endpoint.url, 68f);
            endpoint.model = DrawCompactTextField(modelRect, "PawnDiary.Settings.ModelName".Translate(), endpoint.model, 54f);

            Rect secondLineRect = listing.GetRect(28f);
            float buttonWidth = 124f;
            Rect keyRect = new Rect(secondLineRect.x, secondLineRect.y, secondLineRect.width - buttonWidth * 2f - 16f, secondLineRect.height);
            Rect fetchRect = new Rect(keyRect.xMax + 8f, secondLineRect.y, buttonWidth, secondLineRect.height);
            Rect pickRect = new Rect(fetchRect.xMax + 8f, secondLineRect.y, buttonWidth, secondLineRect.height);
            endpoint.apiKey = DrawCompactTextField(keyRect, "PawnDiary.Settings.ApiKey".Translate(), endpoint.apiKey, 60f);
            DrawModelButtons(fetchRect, pickRect, index, endpoint);

            // Show the fetch status under the row that triggered the fetch.
            if (fetchTargetIndex == index && !string.IsNullOrEmpty(fetchStatus))
            {
                listing.Label(fetchStatus);
            }

            listing.GapLine();
        }

        /// <summary>
        /// Draws a short label and text field inside one row. This mirrors TextEntryLabeled but lets
        /// two settings share a line without clipping labels into neighboring controls.
        /// </summary>
        private static string DrawCompactTextField(Rect rect, string label, string value, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            Widgets.Label(labelRect, label);
            return Widgets.TextField(fieldRect, value ?? string.Empty);
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

        /// <summary>
        /// Draws per-group enable checkboxes and, for the currently selected group,
        /// an editable instruction text area with save/restore buttons.
        /// </summary>
        private void DrawInteractionGroupsEditor(Listing_Standard listing)
        {
            listing.Label("PawnDiary.Settings.EventsHeader".Translate());

            // One toggle per group: whether events in it are recorded at all. A header is
            // drawn the first time the group domain changes (interactions vs mental states).
            bool mentalHeaderDrawn = false;
            foreach (DiaryInteractionGroupDef group in InteractionGroups.All)
            {
                if (group.domain == GroupDomain.MentalState && !mentalHeaderDrawn)
                {
                    mentalHeaderDrawn = true;
                    listing.Gap(6f);
                    listing.Label("PawnDiary.Settings.MentalStatesHeader".Translate());
                }

                bool enabled = Settings.IsGroupEnabled(group.defName);
                bool before = enabled;
                listing.CheckboxLabeled(group.label, ref enabled, group.instruction);
                if (enabled != before)
                {
                    Settings.SetGroupEnabled(group.defName, enabled);
                }
            }

            listing.GapLine();

            DiaryInteractionGroupDef selectedGroup = SelectedGroup();
            if (selectedGroup == null)
            {
                return;
            }

            if (listing.ButtonText("PawnDiary.Settings.PromptForGroup".Translate(selectedGroup.label)))
            {
                List<FloatMenuOption> options = InteractionGroups.All
                    .Select(group => new FloatMenuOption(group.label, delegate
                    {
                        selectedGroupKey = group.defName;
                        instructionEditGroupKey = null;
                    }))
                    .ToList();

                Find.WindowStack.Add(new FloatMenu(options));
            }

            EnsureInstructionEditBuffer(selectedGroup);

            listing.Label("PawnDiary.Settings.GroupInstructionLabel".Translate());
            Rect textAreaRect = listing.GetRect(120f, 1f);
            instructionEditBuffer = Widgets.TextArea(textAreaRect, instructionEditBuffer ?? string.Empty);

            listing.Gap(6f);
            if (listing.ButtonText("PawnDiary.Settings.SaveInstruction".Translate()))
            {
                Settings.SetGroupInstruction(selectedGroup.defName, instructionEditBuffer);
                WriteSettings();
            }

            if (listing.ButtonText("PawnDiary.Settings.RestoreGroupDefault".Translate()))
            {
                Settings.ResetGroupInstruction(selectedGroup.defName);
                instructionEditBuffer = selectedGroup.instruction;
                instructionEditGroupKey = selectedGroup.defName;
                WriteSettings();
            }
        }

        /// <summary>
        /// Refreshes the instruction edit buffer when the selected group changes,
        /// so the text area always shows the correct group's instruction text.
        /// </summary>
        private void EnsureInstructionEditBuffer(DiaryInteractionGroupDef selectedGroup)
        {
            if (selectedGroup == null)
            {
                instructionEditBuffer = string.Empty;
                instructionEditGroupKey = null;
                return;
            }

            if (instructionEditGroupKey == selectedGroup.defName && instructionEditBuffer != null)
            {
                return;
            }

            instructionEditGroupKey = selectedGroup.defName;
            instructionEditBuffer = Settings.EditableInstructionForGroup(selectedGroup);
        }

        /// <summary>
        /// Returns the currently selected interaction group, falling back to
        /// the first available group if the stored key is invalid or null.
        /// </summary>
        private DiaryInteractionGroupDef SelectedGroup()
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(selectedGroupKey);
            if (group != null)
            {
                return group;
            }

            group = InteractionGroups.All.FirstOrDefault();
            if (group != null)
            {
                selectedGroupKey = group.defName;
            }

            return group;
        }

        /// <summary>
        /// Fetches the list of available model IDs from one API row's endpoint asynchronously,
        /// and auto-fills that row's model if it has none yet. Uses a generation counter so stale
        /// results from earlier (or reset) requests are discarded.
        /// </summary>
        private async void FetchModels(int index)
        {
            int generation = ++fetchGeneration; // capture current generation to detect stale completions
            isFetchingModels = true;
            fetchTargetIndex = index;
            fetchStatus = "PawnDiary.Settings.FetchingModels".Translate();

            try
            {
                if (index < 0 || index >= Settings.apiEndpoints.Count)
                {
                    return;
                }

                ApiEndpointConfig endpoint = Settings.apiEndpoints[index];
                List<string> models = await ModelListClient.FetchModels(endpoint.url, endpoint.apiKey, Settings.timeoutSeconds);

                if (generation != fetchGeneration)
                    return;

                fetchedModels.Clear();
                fetchedModels.AddRange(models);

                if (models.Count == 0)
                {
                    fetchStatus = "PawnDiary.Settings.NoModelsReturned".Translate();
                }
                else
                {
                    fetchStatus = "PawnDiary.Settings.ModelsFound".Translate(models.Count);
                    if (string.IsNullOrWhiteSpace(endpoint.model) || endpoint.model == PawnDiarySettings.DefaultModelName)
                    {
                        endpoint.model = models[0];
                    }
                }
            }
            catch (Exception ex)
            {
                if (generation != fetchGeneration)
                    return;

                fetchStatus = "PawnDiary.Settings.FetchFailed".Translate(ex.Message);
            }
            finally
            {
                if (generation == fetchGeneration)
                    isFetchingModels = false;
            }
        }
    }

    /// <summary>
    /// Static helpers to normalize endpoint URLs and build the
    /// /models and /chat/completions paths expected by OpenAI-compatible APIs.
    /// </summary>
    public static class EndpointUtility
    {
        /// <summary>
        /// Strips trailing slashes and any /chat/completions suffix so the
        /// endpoint can be used as a clean base for path construction.
        /// Falls back to the default endpoint when the input is empty.
        /// </summary>
        public static string NormalizeBaseEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return PawnDiarySettings.DefaultEndpointUrl;
            }

            string normalized = endpoint.Trim().TrimEnd('/');

            if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - "/chat/completions".Length);
            }

            return normalized;
        }

        /// <summary>Builds the full /models URL for model discovery.</summary>
        public static string BuildModelsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/models";
        }

        /// <summary>Builds the full /chat/completions URL for LLM requests.</summary>
        public static string BuildChatCompletionsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/chat/completions";
        }
    }

    /// <summary>
    /// Async HTTP client that fetches available model IDs from an
    /// OpenAI-compatible /models endpoint and parses the JSON response.
    /// </summary>
    public static class ModelListClient
    {
        // Shared HttpClient with no built-in timeout; per-request timeouts are set via CancellationTokenSource.
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>
        /// Sends a GET request to the /models endpoint, authenticates with the
        /// given API key, and returns a sorted, deduplicated list of model IDs.
        /// </summary>
        public static async Task<List<string>> FetchModels(string endpoint, string apiKey, int timeoutSeconds)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, EndpointUtility.BuildModelsUrl(endpoint)))
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (HttpResponseMessage response = await Client.SendAsync(request, cancellation.Token))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimForStatus(json)}");
                    }

                    return ParseModelIds(json);
                }
            }
        }

        /// <summary>
        /// Extracts the "id" strings from the "data" array of an OpenAI-style
        /// /models JSON response, returning distinct, sorted model IDs.
        /// </summary>
        private static List<string> ParseModelIds(string json)
        {
            List<string> models = new List<string>();
            Dictionary<string, object> root = MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("data", out object dataObject))
            {
                return models;
            }

            object[] data = dataObject as object[];
            if (data == null)
            {
                return models;
            }

            for (int i = 0; i < data.Length; i++)
            {
                Dictionary<string, object> model = data[i] as Dictionary<string, object>;
                if (model == null || !model.TryGetValue("id", out object idObject))
                {
                    continue;
                }

                string id = idObject as string;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(id);
                }
            }

            return models.Distinct().OrderBy(model => model).ToList();
        }

        /// <summary>Truncates a string to 120 chars for display in error status messages.</summary>
        private static string TrimForStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            return value.Length <= 120 ? value : value.Substring(0, 120) + "...";
        }
    }
}
