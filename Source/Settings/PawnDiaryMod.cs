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
        // Human-readable status line shown below the Fetch button.
        private string fetchStatus = "Models not fetched.";
        // DefName of the interaction group currently selected in the instruction editor.
        private string selectedGroupKey;
        // Scroll position for the settings window scroll view.
        private Vector2 settingsScrollPosition;
        // Ephemeral text buffer for the model-name text field (decouples editing from Settings).
        private string modelNameEditBuffer;
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
            return "Pawn Diary";
        }

        /// <summary>
        /// Draws the full settings window: connection fields, model selector,
        /// concurrency control, system prompt, and per-group instruction editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 1500f); // 16px reserved for the scrollbar
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);
            listing.Begin(viewRect);

            listing.Label("Connection");
            Settings.endpointUrl = listing.TextEntryLabeled("Endpoint", Settings.endpointUrl);
            Settings.apiKey = listing.TextEntryLabeled("API key", Settings.apiKey);

            listing.Gap(6f);
            EnsureModelNameEditBuffer();
            modelNameEditBuffer = listing.TextEntryLabeled("Model name", modelNameEditBuffer);
            Settings.modelName = modelNameEditBuffer;
            DrawModelSelector(listing);

            listing.Gap(6f);
            if (listing.ButtonText(isFetchingModels ? "Fetching models..." : "Fetch models") && !isFetchingModels)
            {
                FetchModels();
            }

            listing.Label(fetchStatus);

            listing.Gap(12f);
            listing.CheckboxLabeled(
                "Paired POV generation",
                ref Settings.dualPovGeneration,
                "Generate pairwise diary entries sequentially: initiator first, then recipient with the initiator entry as hidden context. Disable to generate only the first POV immediately and let other POVs generate lazily.");

            listing.Label($"Max concurrent requests: {Settings.maxConcurrentRequests}");
            Settings.maxConcurrentRequests = Mathf.RoundToInt(listing.Slider(Settings.maxConcurrentRequests, 1f, 16f));
            listing.Label("How many requests may be in flight (awaiting a model response) at once; the rest wait in the queue. Use 1 for a single local model that handles one request at a time; raise it only if your endpoint serves requests in parallel.");

            listing.Gap(12f);
            if (listing.ButtonText("Reset connection"))
            {
                fetchGeneration++;
                Settings.ResetConnectionDefaults();
                fetchedModels.Clear();
                fetchStatus = "Models not fetched.";
                modelNameEditBuffer = Settings.modelName ?? string.Empty;
            }

            listing.GapLine();
            listing.Label("System prompt");
            Rect systemPromptRect = listing.GetRect(150f, 1f);
            Settings.systemPrompt = Widgets.TextArea(systemPromptRect, Settings.systemPrompt ?? string.Empty);
            if (listing.ButtonText("Restore default system prompt"))
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
        /// Draws a button that opens a floating menu of fetched model IDs,
        /// allowing the user to pick one instead of typing it manually.
        /// </summary>
        private void DrawModelSelector(Listing_Standard listing)
        {
            if (listing.ButtonText("Pick fetched model"))
            {
                List<FloatMenuOption> options = fetchedModels
                    .Distinct()
                    .OrderBy(model => model)
                    .Select(model => new FloatMenuOption(model, delegate
                    {
                        Settings.modelName = model;
                        modelNameEditBuffer = model;
                    }))
                    .ToList();

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("No models fetched yet", null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        /// <summary>
        /// Syncs the model-name edit buffer with the current setting value.
        /// Replaces the buffer when it is uninitialized or the saved model
        /// name changed externally (e.g. via the Pick menu or Reset button).
        /// </summary>
        private void EnsureModelNameEditBuffer()
        {
            if (modelNameEditBuffer == null || (modelNameEditBuffer != Settings.modelName && !string.IsNullOrWhiteSpace(Settings.modelName)))
            {
                modelNameEditBuffer = Settings.modelName ?? string.Empty;
            }
        }

        /// <summary>
        /// Draws per-group enable checkboxes and, for the currently selected group,
        /// an editable instruction text area with save/restore buttons.
        /// </summary>
        private void DrawInteractionGroupsEditor(Listing_Standard listing)
        {
            listing.Label("Events — enable groups and edit their diary prompt");

            // One toggle per group: whether events in it are recorded at all. A header is
            // drawn the first time the group domain changes (interactions vs mental states).
            bool mentalHeaderDrawn = false;
            foreach (DiaryInteractionGroupDef group in InteractionGroups.All)
            {
                if (group.domain == GroupDomain.MentalState && !mentalHeaderDrawn)
                {
                    mentalHeaderDrawn = true;
                    listing.Gap(6f);
                    listing.Label("Mental states & breaks");
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

            if (listing.ButtonText($"Prompt for: {selectedGroup.label}"))
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

            listing.Label("Diary prompt instruction for this group");
            Rect textAreaRect = listing.GetRect(120f, 1f);
            instructionEditBuffer = Widgets.TextArea(textAreaRect, instructionEditBuffer ?? string.Empty);

            listing.Gap(6f);
            if (listing.ButtonText("Save instruction"))
            {
                Settings.SetGroupInstruction(selectedGroup.defName, instructionEditBuffer);
                WriteSettings();
            }

            if (listing.ButtonText("Restore this group's default"))
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
        /// Fetches the list of available model IDs from the configured endpoint
        /// asynchronously. Uses a generation counter so stale results from
        /// earlier (or reset) requests are discarded.
        /// </summary>
        private async void FetchModels()
        {
            int generation = ++fetchGeneration; // capture current generation to detect stale completions
            isFetchingModels = true;
            fetchStatus = "Fetching models...";

            try
            {
                List<string> models = await ModelListClient.FetchModels(Settings.endpointUrl, Settings.apiKey, Settings.timeoutSeconds);

                if (generation != fetchGeneration)
                    return;

                fetchedModels.Clear();
                fetchedModels.AddRange(models);

                if (models.Count == 0)
                {
                    fetchStatus = "No models returned.";
                }
                else
                {
                    fetchStatus = $"Found {models.Count} model(s).";
                    if (string.IsNullOrWhiteSpace(Settings.modelName) || Settings.modelName == PawnDiarySettings.DefaultModelName)
                    {
                        Settings.modelName = models[0];
                        modelNameEditBuffer = models[0];
                    }
                }
            }
            catch (Exception ex)
            {
                if (generation != fetchGeneration)
                    return;

                fetchStatus = $"Fetch failed: {ex.Message}";
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
