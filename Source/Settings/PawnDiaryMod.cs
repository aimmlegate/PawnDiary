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
    public class PawnDiaryMod : Mod
    {
        public static PawnDiarySettings Settings;

        private readonly List<string> fetchedModels = new List<string>();
        private int fetchGeneration;
        private bool isFetchingModels;
        private string fetchStatus = "Models not fetched.";
        private string selectedGroupKey;
        private Vector2 settingsScrollPosition;
        private string modelNameEditBuffer;
        private string instructionEditBuffer;
        private string instructionEditGroupKey;

        public PawnDiaryMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiarySettings>();
        }

        public override string SettingsCategory()
        {
            return "Pawn Diary";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 1500f);
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

        public override void WriteSettings()
        {
            Settings.ClampValues();
            LlmClient.ApplyConcurrency();
            base.WriteSettings();
        }

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

        private void EnsureModelNameEditBuffer()
        {
            if (modelNameEditBuffer == null || (modelNameEditBuffer != Settings.modelName && !string.IsNullOrWhiteSpace(Settings.modelName)))
            {
                modelNameEditBuffer = Settings.modelName ?? string.Empty;
            }
        }

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

        private async void FetchModels()
        {
            int generation = ++fetchGeneration;
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

    public static class EndpointUtility
    {
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

        public static string BuildModelsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/models";
        }

        public static string BuildChatCompletionsUrl(string endpoint)
        {
            return NormalizeBaseEndpoint(endpoint) + "/chat/completions";
        }
    }

    public static class ModelListClient
    {
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

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
