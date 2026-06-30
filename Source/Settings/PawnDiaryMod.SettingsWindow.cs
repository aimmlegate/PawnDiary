// Top-level settings-window layout for Pawn Diary. Detail renderers live in the sibling partial
// files so the RimWorld Mod entry point is not also one monolithic IMGUI class.
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

        /// <summary>
        /// Draws the full settings window: API lanes, generation controls, the prompt-text studio,
        /// and the writing-style preset editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();
            Settings.personaPresets.EnsureList();
            // Apply any completed async API-tool results on the main thread before drawing rows.
            apiConnectionController.ApplyPendingResults();

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

                DrawPromptStudio(listing);
                DrawPersonaStudio(listing);
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

            // Generation controls, compact prompt studio, and writing-style preset studio.
            height += 343f;
            if (Prefs.DevMode)
            {
                height += 62f;
            }

            height += Settings.showPromptStudio ? 492f : 44f;
            height += 460f + PersonaTagPickerHeight();

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
