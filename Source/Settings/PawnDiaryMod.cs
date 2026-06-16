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

        // Measured pixel height of the settings content from the previous frame, used to size the
        // scroll view's inner rect. Starts generous so nothing clips before the first measurement;
        // afterwards it tracks the real content height so every control stays scrollable and
        // clickable no matter how many event groups are listed. Replaces a brittle hardcoded height
        // that pushed the bottom controls (the per-group prompt editor) out of reach.
        private float lastSettingsContentHeight = 5000f;

        // Muted colors for secondary text and sub-headers, so the window reads as a hierarchy
        // instead of a flat wall of same-weight labels.
        private static readonly Color HintColor = new Color(0.72f, 0.72f, 0.72f);
        private static readonly Color SubheaderColor = new Color(0.58f, 0.80f, 0.95f);

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
        /// per-lane concurrency control, system prompt, and the per-group
        /// instruction editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();

            Rect outRect = inRect;
            // Self-measuring scroll height: render the content, then remember how tall it actually
            // was (lastSettingsContentHeight) and reuse that next frame. This replaces a hardcoded
            // height that was too short once enough event groups were listed, which pushed the
            // bottom controls (the per-group prompt editor) outside the scroll area where they
            // could not be reached or clicked.
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
            listing.Label("PawnDiary.Settings.MaxConcurrent".Translate(Settings.maxConcurrentRequests));
            Settings.maxConcurrentRequests = Mathf.RoundToInt(listing.Slider(Settings.maxConcurrentRequests, 1f, 16f));
            DrawHint(listing, "PawnDiary.Settings.MaxConcurrentHelp".Translate());

            // Diary voice: first-person, in-character entries (the main system prompt).
            SectionTitle(listing, "PawnDiary.Settings.SystemPrompt".Translate());
            DrawHint(listing, "PawnDiary.Settings.SystemPromptHelp".Translate());
            Rect systemPromptRect = listing.GetRect(120f);
            Settings.systemPrompt = Widgets.TextArea(systemPromptRect, Settings.systemPrompt ?? string.Empty);
            listing.Gap(4f);
            if (listing.ButtonText("PawnDiary.Settings.RestoreSystemPrompt".Translate()))
            {
                Settings.systemPrompt = PawnDiarySettings.DefaultSystemPrompt;
            }

            // Day reflection: first-person, looking back on the whole day.
            SectionTitle(listing, "PawnDiary.Settings.SystemPromptReflection".Translate());
            DrawHint(listing, "PawnDiary.Settings.SystemPromptReflectionHelp".Translate());
            Rect reflectionPromptRect = listing.GetRect(120f);
            Settings.systemPromptReflection = Widgets.TextArea(reflectionPromptRect, Settings.systemPromptReflection ?? string.Empty);
            listing.Gap(4f);
            if (listing.ButtonText("PawnDiary.Settings.RestoreSystemPromptReflection".Translate()))
            {
                Settings.systemPromptReflection = PawnDiarySettings.DefaultSystemPromptReflection;
            }

            // Neutral chronicle: third-person factual notes (death + arrival descriptions).
            SectionTitle(listing, "PawnDiary.Settings.SystemPromptNeutral".Translate());
            DrawHint(listing, "PawnDiary.Settings.SystemPromptNeutralHelp".Translate());
            Rect neutralPromptRect = listing.GetRect(120f);
            Settings.systemPromptNeutral = Widgets.TextArea(neutralPromptRect, Settings.systemPromptNeutral ?? string.Empty);
            listing.Gap(4f);
            if (listing.ButtonText("PawnDiary.Settings.RestoreSystemPromptNeutral".Translate()))
            {
                Settings.systemPromptNeutral = PawnDiarySettings.DefaultSystemPromptNeutral;
            }

            // Title generation: short chat-style subject (3-8 words) for an existing diary entry.
            // Used only by the title follow-up flow; main entries never send this prompt.
            SectionTitle(listing, "PawnDiary.Settings.SystemPromptTitle".Translate());
            DrawHint(listing, "PawnDiary.Settings.SystemPromptTitleHelp".Translate());
            Rect titlePromptRect = listing.GetRect(80f);
            Settings.systemPromptTitle = Widgets.TextArea(titlePromptRect, Settings.systemPromptTitle ?? string.Empty);
            listing.Gap(4f);
            if (listing.ButtonText("PawnDiary.Settings.RestoreSystemPromptTitle".Translate()))
            {
                Settings.systemPromptTitle = PawnDiarySettings.DefaultSystemPromptTitle;
            }

            DrawInteractionGroupsEditor(listing);

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

            DrawHint(listing, "PawnDiary.Settings.ApisHeader".Translate());
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
        /// Draws one API/model lane as a small framed block. The row is intentionally taller than
        /// the old compact version: full-width endpoint/key fields avoid the clipped labels and
        /// cramped text boxes that made the settings window hard to scan.
        /// </summary>
        private void DrawCompactApiEndpointRow(Listing_Standard listing, int index, ApiEndpointConfig endpoint, ref int removeIndex)
        {
            bool showStatus = fetchTargetIndex == index && !string.IsNullOrEmpty(fetchStatus);
            Rect blockRect = listing.GetRect(showStatus ? 174f : 148f);
            Widgets.DrawMenuSection(blockRect);

            Rect innerRect = blockRect.ContractedBy(8f);
            float lineHeight = 28f;
            float gap = 5f;

            // Row header: "API N" on the left, then Enabled and Remove controls on the right.
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, lineHeight);
            Rect headerLabelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 240f, headerRect.height);
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

            float y = headerRect.yMax + gap;
            Rect endpointRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            endpoint.url = DrawCompactTextField(endpointRect, "PawnDiary.Settings.Endpoint".Translate(), endpoint.url, 94f);

            y += lineHeight + gap;
            Rect modelLineRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            float buttonWidth = 112f;
            Rect pickRect = new Rect(modelLineRect.xMax - buttonWidth, modelLineRect.y, buttonWidth, modelLineRect.height);
            Rect fetchRect = new Rect(pickRect.x - gap - buttonWidth, modelLineRect.y, buttonWidth, modelLineRect.height);
            Rect modelRect = new Rect(modelLineRect.x, modelLineRect.y, fetchRect.x - modelLineRect.x - gap, modelLineRect.height);
            endpoint.model = DrawCompactTextField(modelRect, "PawnDiary.Settings.ModelName".Translate(), endpoint.model, 94f);
            DrawModelButtons(fetchRect, pickRect, index, endpoint);

            y += lineHeight + gap;
            Rect keyRect = new Rect(innerRect.x, y, innerRect.width, lineHeight);
            endpoint.apiKey = DrawCompactTextField(keyRect, "PawnDiary.Settings.ApiKey".Translate(), endpoint.apiKey, 94f);

            // Show the fetch status under the row that triggered the fetch, inside the framed lane
            // so it cannot push later controls sideways or overlap adjacent rows.
            if (showStatus)
            {
                y += lineHeight + gap;
                Rect statusRect = new Rect(innerRect.x + 94f, y, innerRect.width - 94f, 22f);
                DrawMutedLabel(statusRect, fetchStatus);
            }

            listing.Gap(6f);
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

        /// <summary>Draws one muted status label without changing the caller's font or color.</summary>
        private static void DrawMutedLabel(Rect rect, string text)
        {
            Color previousColor = GUI.color;
            GUI.color = HintColor;
            Widgets.Label(rect, text);
            GUI.color = previousColor;
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
            SectionTitle(listing, "PawnDiary.Settings.EventsSectionTitle".Translate());
            DrawHint(listing, "PawnDiary.Settings.EventsHeader".Translate());

            // Enable toggles, two per row so the ~30-group catalog stays compact. The Interaction
            // domain sits directly under the section title; later domains get a muted sub-header.
            DrawGroupTogglesForDomain(listing, GroupDomain.Interaction, null);
            DrawGroupTogglesForDomain(listing, GroupDomain.MentalState, "PawnDiary.Settings.MentalStatesHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.Tale, "PawnDiary.Settings.TalesHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.MoodEvent, "PawnDiary.Settings.MoodEventsHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.Thought, "PawnDiary.Settings.ThoughtsHeader");

            listing.GapLine(10f);

            // Per-group prompt editor: pick a group, edit its diary-prompt instruction, save/restore.
            DiaryInteractionGroupDef selectedGroup = SelectedGroup();
            if (selectedGroup == null)
            {
                return;
            }

            // Header row: which group is being edited, plus a button to switch to another group.
            Rect pickRow = listing.GetRect(28f);
            Rect pickLabelRect = new Rect(pickRow.x, pickRow.y, pickRow.width - 120f, pickRow.height);
            Widgets.Label(pickLabelRect, "PawnDiary.Settings.EditingPromptFor".Translate(selectedGroup.label));
            Rect changeRect = new Rect(pickRow.xMax - 110f, pickRow.y, 110f, pickRow.height);
            if (Widgets.ButtonText(changeRect, "PawnDiary.Settings.ChangeGroup".Translate()))
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

            DrawHint(listing, "PawnDiary.Settings.GroupInstructionLabel".Translate());
            Rect textAreaRect = listing.GetRect(120f);
            instructionEditBuffer = Widgets.TextArea(textAreaRect, instructionEditBuffer ?? string.Empty);

            listing.Gap(6f);
            // Save and Restore share one row instead of stacking two full-width buttons.
            Rect buttonRow = listing.GetRect(30f);
            float halfButton = buttonRow.width / 2f - 4f;
            Rect saveRect = new Rect(buttonRow.x, buttonRow.y, halfButton, buttonRow.height);
            Rect restoreRect = new Rect(buttonRow.x + halfButton + 8f, buttonRow.y, halfButton, buttonRow.height);
            if (Widgets.ButtonText(saveRect, "PawnDiary.Settings.SaveInstruction".Translate()))
            {
                Settings.SetGroupInstruction(selectedGroup.defName, instructionEditBuffer);
                WriteSettings();
            }

            if (Widgets.ButtonText(restoreRect, "PawnDiary.Settings.RestoreGroupDefault".Translate()))
            {
                Settings.ResetGroupInstruction(selectedGroup.defName);
                instructionEditBuffer = selectedGroup.instruction;
                instructionEditGroupKey = selectedGroup.defName;
                WriteSettings();
            }
        }

        /// <summary>
        /// Draws the enable toggles for one event domain as a two-column block, with an optional
        /// muted sub-header above it. Two columns keep the ~30-group catalog from becoming a tall
        /// single-column wall (the old layout's main source of wasted height).
        /// </summary>
        private void DrawGroupTogglesForDomain(Listing_Standard listing, GroupDomain domain, string headerKey)
        {
            List<DiaryInteractionGroupDef> groups = InteractionGroups.All.Where(group => group.domain == domain).ToList();
            if (groups.Count == 0)
            {
                return;
            }

            if (headerKey != null)
            {
                listing.Gap(8f);
                Color previousColor = GUI.color;
                GUI.color = SubheaderColor;
                listing.Label(headerKey.Translate());
                GUI.color = previousColor;
            }

            const float rowHeight = 24f;
            const float columnGap = 18f;
            for (int i = 0; i < groups.Count; i += 2)
            {
                Rect row = listing.GetRect(rowHeight);
                float columnWidth = (row.width - columnGap) / 2f;
                DrawGroupToggle(new Rect(row.x, row.y, columnWidth, row.height), groups[i]);
                if (i + 1 < groups.Count)
                {
                    DrawGroupToggle(new Rect(row.x + columnWidth + columnGap, row.y, columnWidth, row.height), groups[i + 1]);
                }
            }
        }

        /// <summary>
        /// Draws one group's enable checkbox into the given rect, showing the group's diary-prompt
        /// instruction as a hover tooltip so the player can preview it without opening the editor.
        /// </summary>
        private void DrawGroupToggle(Rect rect, DiaryInteractionGroupDef group)
        {
            if (!string.IsNullOrEmpty(group.instruction) && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, group.instruction);
            }

            bool enabled = Settings.IsGroupEnabled(group.defName);
            bool before = enabled;
            Widgets.CheckboxLabeled(rect, group.label, ref enabled);
            if (enabled != before)
            {
                Settings.SetGroupEnabled(group.defName, enabled);
            }
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
                height += 38f; // hint text plus its small gap
                height += Settings.apiEndpoints.Count * 180f;
                height += 38f; // Add API / Reset row
            }
            else
            {
                height += 34f; // compact summary
            }

            // Generation and system-prompt sections. The system-prompt block includes the title
            // prompt editor as a short extra section.
            height += 160f;
            height += 290f;

            // Events section: two-column group toggles, domain subheaders, and the prompt editor.
            height += 70f;
            GroupDomain[] domains =
            {
                GroupDomain.Interaction,
                GroupDomain.MentalState,
                GroupDomain.Tale,
                GroupDomain.MoodEvent,
                GroupDomain.Thought
            };

            foreach (GroupDomain domain in domains)
            {
                int groupCount = InteractionGroups.All.Count(group => group.domain == domain);
                if (groupCount == 0)
                {
                    continue;
                }

                if (domain != GroupDomain.Interaction)
                {
                    height += 28f;
                }

                height += Mathf.Ceil(groupCount / 2f) * 30f;
            }

            height += 240f;
            return height + 160f; // breathing room for translated labels and RimWorld skin variance
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
            Widgets.Label(rect, label);
            Text.Font = previousFont;
            listing.GapLine(6f);
        }

        /// <summary>
        /// Draws small, muted helper text (tiny font, grey) for secondary descriptions and hints,
        /// so long explanatory lines don't compete visually with the actual controls.
        /// </summary>
        private static void DrawHint(Listing_Standard listing, string text)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = HintColor;
            listing.Label(text);
            GUI.color = previousColor;
            Text.Font = previousFont;
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
