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

        // Which prompt-text card is currently open in the settings "Prompt Studio".
        private enum PromptEditorKind
        {
            SystemDiary,
            SystemReflection,
            SystemNeutral,
            SystemTitle,
            SinglePov,
            RecipientFollowup,
            DeathDescription,
            ArrivalDescription,
            TitleUser
        }

        // Static metadata for one editable prompt text in the settings UI.
        private sealed class PromptEditorDescriptor
        {
            public readonly PromptEditorKind kind;
            public readonly string labelKey;
            public readonly string helpKey;
            public readonly float textAreaHeight;
            public readonly bool systemPrompt;

            public PromptEditorDescriptor(PromptEditorKind kind, string labelKey, string helpKey, float textAreaHeight, bool systemPrompt)
            {
                this.kind = kind;
                this.labelKey = labelKey;
                this.helpKey = helpKey;
                this.textAreaHeight = textAreaHeight;
                this.systemPrompt = systemPrompt;
            }
        }

        private static readonly PromptEditorDescriptor[] PromptEditors =
        {
            new PromptEditorDescriptor(PromptEditorKind.SystemDiary, "PawnDiary.Settings.SystemPrompt", "PawnDiary.Settings.SystemPromptHelp", 120f, true),
            new PromptEditorDescriptor(PromptEditorKind.SystemReflection, "PawnDiary.Settings.SystemPromptReflection", "PawnDiary.Settings.SystemPromptReflectionHelp", 120f, true),
            new PromptEditorDescriptor(PromptEditorKind.SystemNeutral, "PawnDiary.Settings.SystemPromptNeutral", "PawnDiary.Settings.SystemPromptNeutralHelp", 96f, true),
            new PromptEditorDescriptor(PromptEditorKind.SystemTitle, "PawnDiary.Settings.SystemPromptTitle", "PawnDiary.Settings.SystemPromptTitleHelp", 72f, true),
            new PromptEditorDescriptor(PromptEditorKind.SinglePov, "PawnDiary.Settings.SinglePovInstruction", "PawnDiary.Settings.SinglePovInstructionHelp", 72f, false),
            new PromptEditorDescriptor(PromptEditorKind.RecipientFollowup, "PawnDiary.Settings.RecipientFollowupInstruction", "PawnDiary.Settings.RecipientFollowupInstructionHelp", 84f, false),
            new PromptEditorDescriptor(PromptEditorKind.DeathDescription, "PawnDiary.Settings.DeathDescriptionInstruction", "PawnDiary.Settings.DeathDescriptionInstructionHelp", 96f, false),
            new PromptEditorDescriptor(PromptEditorKind.ArrivalDescription, "PawnDiary.Settings.ArrivalDescriptionInstruction", "PawnDiary.Settings.ArrivalDescriptionInstructionHelp", 96f, false),
            new PromptEditorDescriptor(PromptEditorKind.TitleUser, "PawnDiary.Settings.TitleUserInstruction", "PawnDiary.Settings.TitleUserInstructionHelp", 72f, false)
        };

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
        // DefName of the interaction group currently selected in the instruction editor.
        private string selectedGroupKey;
        // Which prompt-text card is open in the "Prompt Studio".
        private PromptEditorKind selectedPromptKind = PromptEditorKind.SystemDiary;
        // Which persona card is open in the settings "Persona Presets" section.
        private string selectedPersonaKey;
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
        private static readonly Color AccentColor = new Color(0.50f, 0.77f, 0.60f);
        private static readonly Color SelectedRowColor = new Color(0.86f, 0.94f, 0.88f);

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
        /// Draws the full settings window: API lanes, generation controls, the prompt-text studio,
        /// and the per-group event prompt editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();
            Settings.EnsurePersonaPresetList();
            // Apply any model-fetch result that completed since the last frame, on the main thread.
            ApplyPendingFetchResult();

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
            listing.CheckboxLabeled(
                "PawnDiary.Settings.EnablePromptEnchantments".Translate(),
                ref Settings.enablePromptEnchantments,
                "PawnDiary.Settings.EnablePromptEnchantmentsTip".Translate());
            listing.Label("PawnDiary.Settings.WorkGenerationWeight".Translate(Settings.workGenerationWeight.ToString("0.##")));
            Settings.workGenerationWeight = listing.Slider(Settings.workGenerationWeight, 0f, 5f);
            DrawHint(listing, "PawnDiary.Settings.WorkGenerationWeightHelp".Translate());
            listing.Label("PawnDiary.Settings.SocialGenerationWeight".Translate(Settings.socialGenerationWeight.ToString("0.##")));
            Settings.socialGenerationWeight = listing.Slider(Settings.socialGenerationWeight, 0f, 5f);
            DrawHint(listing, "PawnDiary.Settings.SocialGenerationWeightHelp".Translate());
            listing.Label("PawnDiary.Settings.MaxConcurrent".Translate(Settings.maxConcurrentRequests));
            Settings.maxConcurrentRequests = Mathf.RoundToInt(listing.Slider(Settings.maxConcurrentRequests, 1f, 16f));
            DrawHint(listing, "PawnDiary.Settings.MaxConcurrentHelp".Translate());

            DrawPromptStudio(listing);
            DrawPersonaStudio(listing);

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
            float pickButtonWidth = 84f;
            float fetchButtonWidth = 144f;
            Rect pickRect = new Rect(modelLineRect.xMax - pickButtonWidth, modelLineRect.y, pickButtonWidth, modelLineRect.height);
            Rect fetchRect = new Rect(pickRect.x - gap - fetchButtonWidth, modelLineRect.y, fetchButtonWidth, modelLineRect.height);
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
        /// Draws the prompt-text editor for every Def-backed prompt the player can customize:
        /// system prompts plus the appended instruction texts used in the user message body.
        /// </summary>
        private void DrawPromptStudio(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.PromptStudioTitle".Translate());
            DrawHint(listing, "PawnDiary.Settings.PromptStudioHelp".Translate());

            DrawPromptStudioSummary(listing);
            listing.Gap(6f);
            DrawPromptPicker(listing);
            listing.Gap(6f);
            DrawSelectedPromptEditor(listing);
        }

        /// <summary>
        /// Draws a compact overview card for the prompt studio so the player can immediately see
        /// how many prompt texts exist and how many differ from the XML defaults.
        /// </summary>
        private void DrawPromptStudioSummary(Listing_Standard listing)
        {
            Rect cardRect = listing.GetRect(88f);
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(8f);
            Rect labelRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            Widgets.Label(labelRect, "PawnDiary.Settings.PromptStudioSummary".Translate(PromptEditors.Length, CustomizedPromptCount()));
            Rect noteRect = new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 20f);
            DrawMutedLabel(noteRect, "PawnDiary.Settings.PromptStudioLiveNote".Translate());

            Rect restoreRect = new Rect(innerRect.x, innerRect.y + 46f, innerRect.width, 30f);
            if (Widgets.ButtonText(restoreRect, "PawnDiary.Settings.RestoreAllPromptDefaults".Translate()))
            {
                Settings.ResetPromptTextDefaults();
            }
        }

        /// <summary>
        /// Draws the prompt picker as a two-column grid of small buttons. Editing one prompt at a
        /// time keeps the settings screen readable while still exposing every Def-backed text.
        /// </summary>
        private void DrawPromptPicker(Listing_Standard listing)
        {
            float rows = Mathf.Ceil(PromptEditors.Length / 2f);
            Rect cardRect = listing.GetRect(36f + (rows * 34f));
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(8f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 22f), "PawnDiary.Settings.PromptPickerHeader".Translate());

            const float gap = 8f;
            float columnWidth = (innerRect.width - gap) / 2f;
            float y = innerRect.y + 26f;
            for (int i = 0; i < PromptEditors.Length; i += 2)
            {
                DrawPromptPickerButton(new Rect(innerRect.x, y, columnWidth, 28f), PromptEditors[i]);
                if (i + 1 < PromptEditors.Length)
                {
                    DrawPromptPickerButton(new Rect(innerRect.x + columnWidth + gap, y, columnWidth, 28f), PromptEditors[i + 1]);
                }

                y += 34f;
            }
        }

        /// <summary>
        /// Draws one prompt-selection button. Selected prompts are lightly tinted so the current
        /// editor card is obvious at a glance.
        /// </summary>
        private void DrawPromptPickerButton(Rect rect, PromptEditorDescriptor descriptor)
        {
            Color previousColor = GUI.color;
            if (descriptor.kind == selectedPromptKind)
            {
                GUI.color = SelectedRowColor;
            }

            string label = descriptor.labelKey.Translate();
            if (IsPromptCustomized(descriptor.kind))
            {
                label += " *";
            }

            if (Widgets.ButtonText(rect, label))
            {
                selectedPromptKind = descriptor.kind;
            }

            GUI.color = previousColor;
        }

        /// <summary>
        /// Draws the editor card for the currently selected prompt text.
        /// </summary>
        private void DrawSelectedPromptEditor(Listing_Standard listing)
        {
            PromptEditorDescriptor descriptor = SelectedPromptDescriptor();
            float cardHeight = descriptor.textAreaHeight + 126f;
            Rect cardRect = listing.GetRect(cardHeight);
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(10f);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width - 140f, 26f);
            Widgets.Label(titleRect, descriptor.labelKey.Translate());

            string badgeKey = descriptor.systemPrompt ? "PawnDiary.Settings.PromptBadgeSystem" : "PawnDiary.Settings.PromptBadgeInstruction";
            Rect badgeRect = new Rect(innerRect.xMax - 132f, innerRect.y, 132f, 24f);
            DrawAccentLabel(badgeRect, badgeKey.Translate());

            Rect statusRect = new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 18f);
            DrawMutedLabel(
                statusRect,
                (IsPromptCustomized(descriptor.kind)
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());

            Rect helpRect = new Rect(innerRect.x, innerRect.y + 42f, innerRect.width, 34f);
            DrawHint(helpRect, descriptor.helpKey.Translate());

            Rect textAreaRect = new Rect(innerRect.x, innerRect.y + 74f, innerRect.width, descriptor.textAreaHeight);
            SetPromptValue(descriptor.kind, Widgets.TextArea(textAreaRect, GetPromptValue(descriptor.kind)));

            Rect restoreRect = new Rect(innerRect.x, textAreaRect.yMax + 8f, innerRect.width, 30f);
            if (Widgets.ButtonText(restoreRect, "PawnDiary.Settings.RestorePromptDefault".Translate()))
            {
                SetPromptValue(descriptor.kind, DefaultPromptValue(descriptor.kind));
            }
        }

        /// <summary>
        /// Draws the editable persona catalog section in mod settings: existing XML presets can be
        /// overridden, and players can add/delete custom presets with predefined theme tags.
        /// </summary>
        private void DrawPersonaStudio(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.PersonaStudioTitle".Translate());
            DrawHint(listing, "PawnDiary.Settings.PersonaStudioHelp".Translate());
            DrawPersonaStudioSummary(listing);
            listing.Gap(6f);
            DrawPersonaPicker(listing);
            listing.Gap(6f);
            DrawSelectedPersonaEditor(listing);
        }

        private void DrawPersonaStudioSummary(Listing_Standard listing)
        {
            int total = DiaryPersonas.All.Count;
            int custom = Settings.CustomPersonas().Count;
            int customized = Settings.personaPresets == null ? 0 : Settings.personaPresets.Count(preset => preset != null && !preset.custom);

            Rect cardRect = listing.GetRect(120f);
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(8f);
            Widgets.Label(
                new Rect(innerRect.x, innerRect.y, innerRect.width, 24f),
                "PawnDiary.Settings.PersonaStudioSummary".Translate(total, custom, customized));
            DrawMutedLabel(
                new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 20f),
                "PawnDiary.Settings.PersonaStudioTagsHelp".Translate());

            Rect addRect = new Rect(innerRect.x, innerRect.y + 48f, innerRect.width, 30f);
            if (Widgets.ButtonText(addRect, "PawnDiary.Settings.AddPersonaPreset".Translate()))
            {
                selectedPersonaKey = Settings.AddCustomPersona();
            }

            Rect clearRect = new Rect(innerRect.x, addRect.yMax + 6f, innerRect.width, 30f);
            if (Widgets.ButtonText(clearRect, "PawnDiary.Settings.ResetPersonaPresets".Translate()))
            {
                Settings.ResetPersonaPresets();
                selectedPersonaKey = null;
            }
        }

        private void DrawPersonaPicker(Listing_Standard listing)
        {
            IReadOnlyList<DiaryPersonaDef> personas = DiaryPersonas.All
                .OrderBy(PersonaLabelForUi)
                .ToList();
            float rows = Mathf.Ceil(personas.Count / 2f);
            Rect cardRect = listing.GetRect(36f + (rows * 34f));
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(8f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 22f), "PawnDiary.Settings.PersonaPickerHeader".Translate());

            const float gap = 8f;
            float columnWidth = (innerRect.width - gap) / 2f;
            float y = innerRect.y + 26f;
            for (int i = 0; i < personas.Count; i += 2)
            {
                DrawPersonaPickerButton(new Rect(innerRect.x, y, columnWidth, 28f), personas[i]);
                if (i + 1 < personas.Count)
                {
                    DrawPersonaPickerButton(new Rect(innerRect.x + columnWidth + gap, y, columnWidth, 28f), personas[i + 1]);
                }

                y += 34f;
            }
        }

        private void DrawPersonaPickerButton(Rect rect, DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            if (persona.defName == selectedPersonaKey)
            {
                GUI.color = SelectedRowColor;
            }

            string label = PersonaLabelForUi(persona);
            if (IsPersonaCustomized(persona.defName))
            {
                label += " *";
            }

            if (Widgets.ButtonText(rect, label))
            {
                selectedPersonaKey = persona.defName;
            }

            GUI.color = previousColor;
        }

        private void DrawSelectedPersonaEditor(Listing_Standard listing)
        {
            DiaryPersonaDef selected = SelectedPersonaForSettings();
            if (selected == null)
            {
                return;
            }

            bool custom = Settings.CustomPersonaFor(selected.defName) != null;
            DiaryPersonaDef baseDef = BasePersonaForSettings(selected);
            PersonaPresetConfig overridePreset = custom ? Settings.CustomPersonaFor(selected.defName) : Settings.PersonaOverrideFor(selected.defName);
            string currentLabel = overridePreset?.label ?? baseDef?.label ?? string.Empty;
            string currentRule = overridePreset?.rule ?? baseDef?.rule ?? string.Empty;
            List<string> currentThemes = new List<string>(overridePreset?.themes ?? baseDef?.themes ?? new List<string>());

            Rect cardRect = listing.GetRect(356f);
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);

            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width - 132f, 24f), "PawnDiary.Settings.EditingPersona".Translate(PersonaLabelForUi(selected)));
            DrawAccentLabel(
                new Rect(innerRect.xMax - 124f, innerRect.y, 124f, 24f),
                (custom
                    ? "PawnDiary.Settings.PersonaBadgeCustom"
                    : "PawnDiary.Settings.PersonaBadgeBuiltIn").Translate());

            DrawMutedLabel(
                new Rect(innerRect.x, innerRect.y + 22f, innerRect.width, 18f),
                (IsPersonaCustomized(selected.defName)
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());

            Rect labelFieldRect = new Rect(innerRect.x, innerRect.y + 46f, innerRect.width, 28f);
            string editedLabel = DrawCompactTextField(labelFieldRect, "PawnDiary.Settings.PersonaLabel".Translate(), currentLabel, 86f);

            Rect ruleLabelRect = new Rect(innerRect.x, innerRect.y + 78f, innerRect.width, 20f);
            DrawHint(ruleLabelRect, "PawnDiary.Settings.PersonaRule".Translate());
            Rect ruleRect = new Rect(innerRect.x, innerRect.y + 98f, innerRect.width, 112f);
            string editedRule = Widgets.TextArea(ruleRect, currentRule);

            Rect tagsLabelRect = new Rect(innerRect.x, ruleRect.yMax + 6f, innerRect.width, 18f);
            DrawHint(tagsLabelRect, "PawnDiary.Settings.PersonaTags".Translate());
            List<string> editedThemes = DrawPersonaTagPicker(new Rect(innerRect.x, tagsLabelRect.yMax + 2f, innerRect.width, 72f), currentThemes, custom);

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
                        Settings.SetPersonaOverride(selected.defName, editedLabel, editedRule, editedThemes);
                    }
                }
            }

            Rect actionRect = new Rect(innerRect.x, innerRect.yMax - 30f, innerRect.width, 30f);
            if (custom)
            {
                if (Widgets.ButtonText(actionRect, "PawnDiary.Settings.DeletePersonaPreset".Translate()))
                {
                    Settings.RemoveCustomPersona(selected.defName);
                    selectedPersonaKey = null;
                }
            }
            else
            {
                if (Widgets.ButtonText(actionRect, "PawnDiary.Settings.RestorePersonaPreset".Translate()))
                {
                    Settings.ResetPersonaOverride(selected.defName);
                }
            }
        }

        private List<string> DrawPersonaTagPicker(Rect rect, List<string> themes, bool requireAtLeastOneTag)
        {
            List<string> selected = themes == null ? new List<string>() : new List<string>(themes);
            float rowHeight = 22f;
            float gap = 8f;
            float columnWidth = (rect.width - gap) / 2f;
            float y = rect.y;
            for (int i = 0; i < DiaryPersonas.PredefinedThemeTags.Length; i += 2)
            {
                DrawPersonaTagToggle(new Rect(rect.x, y, columnWidth, rowHeight), DiaryPersonas.PredefinedThemeTags[i], selected, requireAtLeastOneTag);
                if (i + 1 < DiaryPersonas.PredefinedThemeTags.Length)
                {
                    DrawPersonaTagToggle(new Rect(rect.x + columnWidth + gap, y, columnWidth, rowHeight), DiaryPersonas.PredefinedThemeTags[i + 1], selected, requireAtLeastOneTag);
                }

                y += rowHeight + 2f;
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

        /// <summary>
        /// Draws per-group enable checkboxes and, for the currently selected group,
        /// an editable instruction text area with save/restore buttons.
        /// </summary>
        private void DrawInteractionGroupsEditor(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.EventsSectionTitle".Translate());
            DrawHint(listing, "PawnDiary.Settings.EventsHeader".Translate());
            DrawEventGroupsSummary(listing);
            listing.Gap(6f);

            // Enable toggles, two per row so the ~30-group catalog stays compact. The Interaction
            // domain sits directly under the section title; later domains get their own framed card.
            DrawGroupTogglesForDomain(listing, GroupDomain.Interaction, null);
            DrawGroupTogglesForDomain(listing, GroupDomain.MentalState, "PawnDiary.Settings.MentalStatesHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.Tale, "PawnDiary.Settings.TalesHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.MoodEvent, "PawnDiary.Settings.MoodEventsHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.Thought, "PawnDiary.Settings.ThoughtsHeader");
            DrawGroupTogglesForDomain(listing, GroupDomain.Work, "PawnDiary.Settings.WorkHeader");

            listing.GapLine(10f);

            // Per-group prompt editor: pick a group, edit its diary-prompt instruction, save/restore.
            DiaryInteractionGroupDef selectedGroup = SelectedGroup();
            if (selectedGroup == null)
            {
                return;
            }

            EnsureInstructionEditBuffer(selectedGroup);

            Rect cardRect = listing.GetRect(268f);
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(10f);
            Rect pickLabelRect = new Rect(innerRect.x, innerRect.y, innerRect.width - 120f, 26f);
            Widgets.Label(pickLabelRect, "PawnDiary.Settings.EditingPromptFor".Translate(selectedGroup.label));
            Rect changeRect = new Rect(innerRect.xMax - 110f, innerRect.y, 110f, 28f);
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

            Rect stateRect = new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 18f);
            DrawMutedLabel(
                stateRect,
                (HasGroupInstructionOverride(selectedGroup)
                    ? "PawnDiary.Settings.GroupStatusCustomized"
                    : "PawnDiary.Settings.GroupStatusDefault").Translate());

            Rect helpRect = new Rect(innerRect.x, innerRect.y + 42f, innerRect.width, 20f);
            DrawHint(helpRect, "PawnDiary.Settings.GroupInstructionLabel".Translate());

            Rect textAreaRect = new Rect(innerRect.x, innerRect.y + 64f, innerRect.width, 100f);
            instructionEditBuffer = Widgets.TextArea(textAreaRect, instructionEditBuffer ?? string.Empty);

            Rect saveRect = new Rect(innerRect.x, textAreaRect.yMax + 8f, innerRect.width, 30f);
            Rect restoreRect = new Rect(innerRect.x, saveRect.yMax + 6f, innerRect.width, 30f);
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

            float headerHeight = headerKey == null ? 0f : 24f;
            float rowHeight = 24f;
            float columnGap = 18f;
            float blockHeight = headerHeight + (Mathf.Ceil(groups.Count / 2f) * (rowHeight + 6f)) + 16f;
            Rect blockRect = listing.GetRect(blockHeight);
            Widgets.DrawMenuSection(blockRect);

            Rect innerRect = blockRect.ContractedBy(8f);
            float y = innerRect.y;
            if (headerKey != null)
            {
                DrawAccentLabel(new Rect(innerRect.x, y, innerRect.width, 20f), headerKey.Translate());
                y += 24f;
            }

            for (int i = 0; i < groups.Count; i += 2)
            {
                Rect row = new Rect(innerRect.x, y, innerRect.width, rowHeight);
                float columnWidth = (row.width - columnGap) / 2f;
                DrawGroupToggle(new Rect(row.x, row.y, columnWidth, row.height), groups[i]);
                if (i + 1 < groups.Count)
                {
                    DrawGroupToggle(new Rect(row.x + columnWidth + columnGap, row.y, columnWidth, row.height), groups[i + 1]);
                }

                y += rowHeight + 6f;
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

            Rect editRect = new Rect(rect.xMax - 60f, rect.y, 60f, rect.height);
            Rect toggleRect = new Rect(rect.x, rect.y, rect.width - 68f, rect.height);
            bool enabled = Settings.IsGroupEnabled(group.defName);
            bool before = enabled;
            Color previousColor = GUI.color;
            if (group.defName == selectedGroupKey)
            {
                GUI.color = SelectedRowColor;
            }

            Widgets.CheckboxLabeled(toggleRect, group.label, ref enabled);
            GUI.color = previousColor;
            if (enabled != before)
            {
                Settings.SetGroupEnabled(group.defName, enabled);
            }

            if (Widgets.ButtonText(editRect, "PawnDiary.Settings.EditGroup".Translate()))
            {
                selectedGroupKey = group.defName;
                instructionEditGroupKey = null;
            }
        }

        /// <summary>
        /// Draws a compact event-group overview so the player can see recording coverage and how
        /// many groups have custom prompt overrides before scrolling through the catalog.
        /// </summary>
        private void DrawEventGroupsSummary(Listing_Standard listing)
        {
            int enabledCount = InteractionGroups.All.Count(group => Settings.IsGroupEnabled(group.defName));
            int overrideCount = InteractionGroups.All.Count(HasGroupInstructionOverride);

            Rect cardRect = listing.GetRect(58f);
            Widgets.DrawMenuSection(cardRect);

            Rect innerRect = cardRect.ContractedBy(8f);
            Widgets.Label(
                new Rect(innerRect.x, innerRect.y, innerRect.width, 24f),
                "PawnDiary.Settings.EventGroupSummary".Translate(enabledCount, InteractionGroups.All.Count, overrideCount));
            DrawMutedLabel(
                new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 20f),
                "PawnDiary.Settings.EventGroupHelp".Translate());
        }

        /// <summary>
        /// Returns the descriptor for the selected prompt, or the first descriptor if something
        /// invalid slipped into saved UI state.
        /// </summary>
        private PromptEditorDescriptor SelectedPromptDescriptor()
        {
            PromptEditorDescriptor descriptor = PromptEditors.FirstOrDefault(editor => editor.kind == selectedPromptKind);
            return descriptor ?? PromptEditors[0];
        }

        /// <summary>
        /// Returns the current editable value for one prompt-text field.
        /// </summary>
        private string GetPromptValue(PromptEditorKind kind)
        {
            switch (kind)
            {
                case PromptEditorKind.SystemDiary:
                    return Settings.systemPrompt ?? string.Empty;
                case PromptEditorKind.SystemReflection:
                    return Settings.systemPromptReflection ?? string.Empty;
                case PromptEditorKind.SystemNeutral:
                    return Settings.systemPromptNeutral ?? string.Empty;
                case PromptEditorKind.SystemTitle:
                    return Settings.systemPromptTitle ?? string.Empty;
                case PromptEditorKind.SinglePov:
                    return Settings.singlePovInstruction ?? string.Empty;
                case PromptEditorKind.RecipientFollowup:
                    return Settings.recipientFollowupInstruction ?? string.Empty;
                case PromptEditorKind.DeathDescription:
                    return Settings.deathDescriptionInstruction ?? string.Empty;
                case PromptEditorKind.ArrivalDescription:
                    return Settings.arrivalDescriptionInstruction ?? string.Empty;
                case PromptEditorKind.TitleUser:
                    return Settings.titleUserInstruction ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Writes one prompt-text field back into settings. Changes save when the player closes the
        /// mod-settings window, matching RimWorld's normal settings behavior.
        /// </summary>
        private void SetPromptValue(PromptEditorKind kind, string value)
        {
            switch (kind)
            {
                case PromptEditorKind.SystemDiary:
                    Settings.systemPrompt = value;
                    break;
                case PromptEditorKind.SystemReflection:
                    Settings.systemPromptReflection = value;
                    break;
                case PromptEditorKind.SystemNeutral:
                    Settings.systemPromptNeutral = value;
                    break;
                case PromptEditorKind.SystemTitle:
                    Settings.systemPromptTitle = value;
                    break;
                case PromptEditorKind.SinglePov:
                    Settings.singlePovInstruction = value;
                    break;
                case PromptEditorKind.RecipientFollowup:
                    Settings.recipientFollowupInstruction = value;
                    break;
                case PromptEditorKind.DeathDescription:
                    Settings.deathDescriptionInstruction = value;
                    break;
                case PromptEditorKind.ArrivalDescription:
                    Settings.arrivalDescriptionInstruction = value;
                    break;
                case PromptEditorKind.TitleUser:
                    Settings.titleUserInstruction = value;
                    break;
            }
        }

        /// <summary>
        /// Returns the XML-defined default text for a prompt editor card.
        /// </summary>
        private static string DefaultPromptValue(PromptEditorKind kind)
        {
            switch (kind)
            {
                case PromptEditorKind.SystemDiary:
                    return PawnDiarySettings.DefaultSystemPrompt;
                case PromptEditorKind.SystemReflection:
                    return PawnDiarySettings.DefaultSystemPromptReflection;
                case PromptEditorKind.SystemNeutral:
                    return PawnDiarySettings.DefaultSystemPromptNeutral;
                case PromptEditorKind.SystemTitle:
                    return PawnDiarySettings.DefaultSystemPromptTitle;
                case PromptEditorKind.SinglePov:
                    return PawnDiarySettings.DefaultSinglePovInstruction;
                case PromptEditorKind.RecipientFollowup:
                    return PawnDiarySettings.DefaultRecipientFollowupInstruction;
                case PromptEditorKind.DeathDescription:
                    return PawnDiarySettings.DefaultDeathDescriptionInstruction;
                case PromptEditorKind.ArrivalDescription:
                    return PawnDiarySettings.DefaultArrivalDescriptionInstruction;
                case PromptEditorKind.TitleUser:
                    return PawnDiarySettings.DefaultTitleUserInstruction;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// True when a prompt's saved text differs from its XML default.
        /// </summary>
        private bool IsPromptCustomized(PromptEditorKind kind)
        {
            return !string.Equals(GetPromptValue(kind), DefaultPromptValue(kind), StringComparison.Ordinal);
        }

        /// <summary>
        /// Counts how many prompt cards currently differ from their XML defaults.
        /// </summary>
        private int CustomizedPromptCount()
        {
            return PromptEditors.Count(editor => IsPromptCustomized(editor.kind));
        }

        /// <summary>
        /// True when the selected group has a saved override entry instead of using its XML prompt.
        /// </summary>
        private bool HasGroupInstructionOverride(DiaryInteractionGroupDef group)
        {
            return group != null
                && Settings.groupInstructions != null
                && Settings.groupInstructions.ContainsKey(group.defName);
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

            // Generation controls, prompt studio, and persona-preset studio.
            height += 280f;
            height += 470f;
            int personaCount = DiaryPersonas.All.Count;
            height += 560f + (Mathf.Ceil(personaCount / 2f) * 34f);

            // Events section: two-column group toggles, domain subheaders, and the prompt editor.
            height += 140f;
            GroupDomain[] domains =
            {
                GroupDomain.Interaction,
                GroupDomain.MentalState,
                GroupDomain.Tale,
                GroupDomain.MoodEvent,
                GroupDomain.Thought,
                GroupDomain.Work
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
                    height += 8f;
                }

                height += 40f + (Mathf.Ceil(groupCount / 2f) * 30f);
            }

            height += 290f;
            return height + 220f; // breathing room for translated labels and RimWorld skin variance
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
        /// Rect-based overload of DrawHint for custom laid-out cards that do not use Listing rows.
        /// </summary>
        private static void DrawHint(Rect rect, string text)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = HintColor;
            Widgets.Label(rect, text);
            GUI.color = previousColor;
            Text.Font = previousFont;
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
            Widgets.Label(rect, text);
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
            fetchStatus = "PawnDiary.Settings.FetchingModels".Translate(); // main thread here

            // Snapshot the inputs on the main thread. The await continuation may resume on a
            // background thread in RimWorld's runtime, so it must not read game state, call
            // .Translate(), or edit shared collections — it only hands a result back via
            // pendingFetchResult, which the main-thread draw (ApplyPendingFetchResult) consumes.
            if (index < 0 || index >= Settings.apiEndpoints.Count)
            {
                isFetchingModels = false;
                return;
            }

            ApiEndpointConfig endpoint = Settings.apiEndpoints[index];
            string url = endpoint.url;
            string apiKey = endpoint.apiKey;
            int timeoutSeconds = Settings.timeoutSeconds;

            try
            {
                List<string> models = await ModelListClient.FetchModels(url, apiKey, timeoutSeconds);
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = true,
                    models = models
                };
            }
            catch (Exception ex)
            {
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = false,
                    errorDetail = ex.Message
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

        // Result of one model fetch, handed from the await continuation to the main-thread draw.
        // Never mutated after construction; assigned to pendingFetchResult as a single reference write.
        private sealed class ModelFetchResult
        {
            public int generation;
            public int targetIndex;
            public bool success;
            public List<string> models;   // immutable snapshot returned by ModelListClient
            public string errorDetail;    // raw, untranslated exception message
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
