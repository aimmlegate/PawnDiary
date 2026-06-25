// Top-level settings-window layout for Pawn Diary. Detail renderers live in the sibling partial
// files so the RimWorld Mod entry point is not also one monolithic IMGUI class.
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        /// <summary>
        /// Draws the full settings window: API lanes, generation controls, the prompt-text studio,
        /// and the writing-style preset editor.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureEndpointsList();
            Settings.EnsurePersonaPresetList();
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
            height += 280f;
            if (Prefs.DevMode)
            {
                height += 30f;
            }

            height += Settings.showPromptStudio ? 430f : 44f;
            height += 460f + PersonaTagPickerHeight();

            return height + 120f; // breathing room for translated labels and RimWorld skin variance
        }
    }
}
