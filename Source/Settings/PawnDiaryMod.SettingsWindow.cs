// Top-level settings-window layout for Pawn Diary. Detail renderers live in the sibling partial
// files so the RimWorld Mod entry point is not also one monolithic IMGUI class. The window has four
// top-level pages: Main, Prompts, Styles, and Tuning.
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        // Text buffers for the numeric retention cap fields. RimWorld IMGUI redraws every frame, so
        // keeping buffers lets the user edit values without the fields fighting each keystroke.
        private string maxActiveDiaryEventsBuffer;
        private int maxActiveDiaryEventsBufferValue = -1;
        private string maxArchivedDiaryEventsBuffer;
        private int maxArchivedDiaryEventsBufferValue = -1;

        // Height of our settings tab button row above the page content. This is intentionally NOT
        // RimWorld's TabDrawer: TabDrawer tabs overhang their rect and can collide with the dialog
        // title in this settings window. A plain button row gives exact layout control.
        private const float SettingsTabHeight = 32f;

        /// <summary>
        /// Draws the settings window behind a compact tab button row.
        /// Main holds connection/generation basics, Prompts holds all prompt editing, Styles holds
        /// writing-style editing, and Tuning holds low-level XML parameters.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();
            Settings.personaPresets.EnsureList();
            // Apply any completed async API-tool results on the main thread before drawing rows.
            apiConnectionController.ApplyPendingResults();

            Rect pageRect = DrawSettingsTabButtons(inRect);
            switch (settingsTab)
            {
                case PawnDiarySettingsTab.Prompts:
                    DrawPromptSettingsTab(pageRect);
                    return;
                case PawnDiarySettingsTab.Styles:
                    DrawStyleSettingsTab(pageRect);
                    return;
                case PawnDiarySettingsTab.Tuning:
                    DrawAdvancedTab(pageRect, AdvancedFieldCategory.Tuning);
                    return;
                default:
                    DrawMainTab(pageRect);
                    return;
            }
        }

        /// <summary>Draws top-level settings tabs as plain RimWorld list buttons.</summary>
        private Rect DrawSettingsTabButtons(Rect inRect)
        {
            float x = inRect.x;
            DrawSettingsTabButton(ref x, inRect.y, PawnDiarySettingsTab.Main, "PawnDiary.Settings.Tab.Main");
            DrawSettingsTabButton(ref x, inRect.y, PawnDiarySettingsTab.Prompts, "PawnDiary.Settings.Tab.Prompts");
            DrawSettingsTabButton(ref x, inRect.y, PawnDiarySettingsTab.Styles, "PawnDiary.Settings.Tab.Styles");
            DrawSettingsTabButton(ref x, inRect.y, PawnDiarySettingsTab.Tuning, "PawnDiary.Settings.Tab.Tuning");

            return new Rect(
                inRect.x,
                inRect.y + SettingsTabHeight + 4f,
                inRect.width,
                inRect.height - SettingsTabHeight - 4f);
        }

        private void DrawSettingsTabButton(ref float x, float y, PawnDiarySettingsTab tab, string labelKey)
        {
            string label = labelKey.Translate().ToString();
            float width = Mathf.Max(110f, Text.CalcSize(label).x + 28f);
            Rect rect = new Rect(x, y, width, SettingsTabHeight);
            if (settingsTab == tab)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = previousAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                settingsTab = tab;
                advancedFilter = string.Empty;
            }

            x += width + 4f;
        }

        /// <summary>
        /// Main settings page: API lanes and generation basics only. Prompt text and writing-style
        /// editing live on their own tabs so the user does not see overlapping editor surfaces.
        /// </summary>
        private void DrawMainTab(Rect inRect)
        {
            Settings.EnsureEndpointsList();

            Rect outRect = inRect;
            // Self-measuring scroll height: render the content, then remember how tall it actually
            // was (lastSettingsContentHeight) and reuse that next frame. This replaces a hardcoded
            // height that was too short once the settings page gained expandable editors.
            float viewHeight = Mathf.Max(lastSettingsContentHeight, EstimateSettingsContentHeight(), inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, viewHeight); // 16px reserved for the scrollbar
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);
            listing.Begin(viewRect);
            // Same immediate-mode safety net as the diary tab: BeginScrollView pushes onto Unity's
            // shared GUI clip stack. If a detail renderer below throws, the finally still ends the
            // listing and the scroll view, so a bad row degrades to a missing section instead of an
            // unbalanced clip stack that corrupts the whole settings window (and the frame after it).
            try
            {
                listing.Gap(4f);
                DrawApiEndpointsEditor(listing);

                SectionTitle(listing, "PawnDiary.Settings.GenerationHeader".Translate());
                listing.CheckboxLabeled(
                    "PawnDiary.Settings.ShowDiaryInspectTab".Translate(),
                    ref Settings.showDiaryInspectTab,
                    "PawnDiary.Settings.ShowDiaryInspectTabTip".Translate());
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

                    Rect exportRect = listing.GetRect(28f);
                    if (ButtonTextFit(exportRect, "PawnDiary.Settings.ExportAllDiaries".Translate()))
                    {
                        HandleExportAllDiaries();
                    }

                    TooltipHandler.TipRegion(exportRect, "PawnDiary.Settings.ExportAllDiariesTip".Translate());
                }

                listing.Label("PawnDiary.Settings.GenerationChanceWeight".Translate(Settings.generationChanceWeight.ToString("0.##")));
                Settings.generationChanceWeight = listing.Slider(
                    Settings.generationChanceWeight,
                    PawnDiarySettings.MinGenerationChanceWeight,
                    PawnDiarySettings.MaxGenerationChanceWeight);
                DrawMaxActiveDiaryEventsField(listing);
                DrawMaxArchivedDiaryEventsField(listing);

            }
            finally
            {
                listing.End();
                Widgets.EndScrollView();
            }

            // Remember the real content height so next frame's scroll view fits it exactly.
            lastSettingsContentHeight = Mathf.Max(listing.CurHeight + 24f, inRect.height);
            settingsScrollPosition.y = Mathf.Clamp(settingsScrollPosition.y, 0f, Mathf.Max(0f, lastSettingsContentHeight - outRect.height));
            Settings.ClampValues();
        }

        /// <summary>
        /// Prompts settings page. Shared/event prompt overrides and low-level template prompt text are
        /// adjacent subpages, which keeps all prompt editing in one top-level destination.
        /// </summary>
        private void DrawPromptSettingsTab(Rect inRect)
        {
            Rect contentRect = DrawPromptSettingsPageButtons(inRect);
            if (promptSettingsPage == PawnDiaryPromptSettingsPage.Templates)
            {
                DrawAdvancedTab(contentRect, AdvancedFieldCategory.Prompts);
                return;
            }

            DrawSharedPromptSettingsPage(contentRect);
        }

        private Rect DrawPromptSettingsPageButtons(Rect inRect)
        {
            float x = inRect.x;
            DrawPromptSettingsPageButton(ref x, inRect.y, PawnDiaryPromptSettingsPage.SharedAndEvents, "PawnDiary.Settings.PromptTab.SharedEvents");
            DrawPromptSettingsPageButton(ref x, inRect.y, PawnDiaryPromptSettingsPage.Templates, "PawnDiary.Settings.PromptTab.Templates");

            return new Rect(
                inRect.x,
                inRect.y + SettingsTabHeight + 4f,
                inRect.width,
                inRect.height - SettingsTabHeight - 4f);
        }

        private void DrawPromptSettingsPageButton(ref float x, float y, PawnDiaryPromptSettingsPage page, string labelKey)
        {
            string label = labelKey.Translate().ToString();
            float width = Mathf.Max(150f, Text.CalcSize(label).x + 28f);
            Rect rect = new Rect(x, y, width, SettingsTabHeight);
            if (promptSettingsPage == page)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = previousAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                promptSettingsPage = page;
                advancedFilter = string.Empty;
            }

            x += width + 4f;
        }

        private void DrawSharedPromptSettingsPage(Rect inRect)
        {
            Rect outRect = inRect;
            float viewHeight = Mathf.Max(lastPromptSettingsContentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref promptSettingsScrollPosition, viewRect);
            listing.Begin(viewRect);
            try
            {
                listing.Gap(4f);
                DrawPromptStudio(listing, false);
            }
            finally
            {
                listing.End();
                Widgets.EndScrollView();
            }

            lastPromptSettingsContentHeight = Mathf.Max(listing.CurHeight + 24f, inRect.height);
            promptSettingsScrollPosition.y = Mathf.Clamp(
                promptSettingsScrollPosition.y,
                0f,
                Mathf.Max(0f, lastPromptSettingsContentHeight - outRect.height));
        }

        /// <summary>Writing-style settings page, separated from prompt text editing.</summary>
        private void DrawStyleSettingsTab(Rect inRect)
        {
            Settings.personaPresets.EnsureList();

            Rect outRect = inRect;
            float viewHeight = Mathf.Max(lastStyleSettingsContentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
            Listing_Standard listing = new Listing_Standard();
            Widgets.BeginScrollView(outRect, ref styleSettingsScrollPosition, viewRect);
            listing.Begin(viewRect);
            try
            {
                listing.Gap(4f);
                DrawPersonaStudio(listing);
            }
            finally
            {
                listing.End();
                Widgets.EndScrollView();
            }

            lastStyleSettingsContentHeight = Mathf.Max(listing.CurHeight + 24f, inRect.height);
            styleSettingsScrollPosition.y = Mathf.Clamp(
                styleSettingsScrollPosition.y,
                0f,
                Mathf.Max(0f, lastStyleSettingsContentHeight - outRect.height));
        }

        /// <summary>
        /// Draws the per-pawn hot diary-history cap as a numeric text field instead of a slider.
        /// Non-digits are removed immediately; parsed values are clamped to the supported range.
        /// </summary>
        private void DrawMaxActiveDiaryEventsField(Listing_Standard listing)
        {
            int currentValue = PawnDiarySettings.ClampActiveDiaryEventLimit(Settings.maxActiveDiaryEvents);
            if (maxActiveDiaryEventsBuffer == null || maxActiveDiaryEventsBufferValue != currentValue)
            {
                maxActiveDiaryEventsBuffer = currentValue.ToString();
                maxActiveDiaryEventsBufferValue = currentValue;
            }

            Rect rect = listing.GetRect(28f);
            string edited = DrawCompactTextField(
                rect,
                "PawnDiary.Settings.MaxActiveDiaryEvents".Translate(
                    PawnDiarySettings.MinActiveDiaryEvents,
                    PawnDiarySettings.MaxActiveDiaryEvents),
                maxActiveDiaryEventsBuffer,
                230f);
            string numeric = DigitsOnly(edited);
            maxActiveDiaryEventsBuffer = numeric;

            int parsed;
            if (!string.IsNullOrEmpty(numeric) && int.TryParse(numeric, out parsed))
            {
                int clamped = PawnDiarySettings.ClampActiveDiaryEventLimit(parsed);
                Settings.maxActiveDiaryEvents = clamped;
                maxActiveDiaryEventsBufferValue = clamped;
                if (parsed < PawnDiarySettings.MinActiveDiaryEvents
                    || parsed > PawnDiarySettings.MaxActiveDiaryEvents)
                {
                    maxActiveDiaryEventsBuffer = clamped.ToString();
                }
            }
        }

        /// <summary>
        /// Draws the per-pawn compact archive cap. 0 is valid here, so the archive can be disabled
        /// without letting the active hot diary list disappear completely.
        /// </summary>
        private void DrawMaxArchivedDiaryEventsField(Listing_Standard listing)
        {
            int currentValue = PawnDiarySettings.ClampArchivedDiaryEventLimit(Settings.maxArchivedDiaryEvents);
            if (maxArchivedDiaryEventsBuffer == null || maxArchivedDiaryEventsBufferValue != currentValue)
            {
                maxArchivedDiaryEventsBuffer = currentValue.ToString();
                maxArchivedDiaryEventsBufferValue = currentValue;
            }

            Rect rect = listing.GetRect(28f);
            string edited = DrawCompactTextField(
                rect,
                "PawnDiary.Settings.MaxArchivedDiaryEvents".Translate(
                    PawnDiarySettings.MinArchivedDiaryEvents,
                    PawnDiarySettings.MaxArchivedDiaryEvents),
                maxArchivedDiaryEventsBuffer,
                230f);
            string numeric = DigitsOnly(edited);
            maxArchivedDiaryEventsBuffer = numeric;

            int parsed;
            if (!string.IsNullOrEmpty(numeric) && int.TryParse(numeric, out parsed))
            {
                int clamped = PawnDiarySettings.ClampArchivedDiaryEventLimit(parsed);
                Settings.maxArchivedDiaryEvents = clamped;
                maxArchivedDiaryEventsBufferValue = clamped;
                if (parsed < PawnDiarySettings.MinArchivedDiaryEvents
                    || parsed > PawnDiarySettings.MaxArchivedDiaryEvents)
                {
                    maxArchivedDiaryEventsBuffer = clamped.ToString();
                }
            }
        }

        private static string DigitsOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            char[] chars = new char[value.Length];
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= '0' && c <= '9')
                {
                    chars[count] = c;
                    count++;
                }
            }

            return count == value.Length ? value : new string(chars, 0, count);
        }

        /// <summary>
        /// Returns a conservative current-frame height for the settings scroll view. The exact
        /// height is still measured from <see cref="Listing_Standard.CurHeight"/> after drawing,
        /// but this estimate prevents one-frame stale heights when sections are opened.
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

            // Generation controls only. Prompt and style editors live on dedicated tabs.
            height += 343f;
            if (Prefs.DevMode)
            {
                height += 62f;
            }

            return height + 120f; // breathing room for translated labels and RimWorld skin variance
        }

        /// <summary>
        /// Dev-only settings action: writes the current game's complete saved diary state to disk and
        /// copies the path to the OS clipboard for quick inspection.
        /// </summary>
        private static void HandleExportAllDiaries()
        {
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (component == null)
            {
                Messages.Message(
                    "PawnDiary.Settings.ExportAllDiariesNoGame".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            string filePath;
            string error;
            if (component.TryExportAllDiariesForDev(out filePath, out error))
            {
                GUIUtility.systemCopyBuffer = filePath;
                Messages.Message(
                    "PawnDiary.Settings.ExportAllDiariesDone".Translate(filePath),
                    MessageTypeDefOf.PositiveEvent,
                    false);
                return;
            }

            Messages.Message(
                "PawnDiary.Settings.ExportAllDiariesFailed".Translate(error),
                MessageTypeDefOf.RejectInput,
                false);
        }
    }
}
