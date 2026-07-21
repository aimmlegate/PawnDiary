// Dev controls and per-pawn setting helpers for the Diary tab. Split from ITab_Pawn_Diary.cs with no behavior change.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        /// <summary>
        /// Returns the height needed for per-pawn dev controls above the diary list. The player-facing
        /// Writing style opener lives in the header icon, so normal play reserves no extra row.
        /// </summary>
        private static float PawnControlsHeight()
        {
            return Prefs.DevMode ? DevControlsHeight : 0f;
        }



        /// <summary>
        /// Renders dev-mode-only troubleshooting controls. Player-facing writing-style editing is
        /// opened by the compact header icon, so this block is absent in normal play.
        /// </summary>
        private void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {

            if (pawn == null || component == null || !Prefs.DevMode)
            {

                return;

            }



            bool writeGlobalSettings = false;

            Listing_Standard listing = new Listing_Standard();

            listing.Begin(rect);

            // Balance the Listing's GUI group even if a control throws — this block is nested inside the
            // filter panel's own scroll group, so a leak here would corrupt the whole frame's UI.
            try
            {



            // Toggling this only gates future LLM requests. Recorded events remain visible as raw

            // diary entries, which lets players pause generation without losing history.

            bool enabled = component.DiaryGenerationEnabledFor(pawn);

            bool before = enabled;

            listing.CheckboxLabeled(

                "PawnDiary.Tab.GenerateForPawn".Translate(),

                ref enabled,

                "PawnDiary.Tab.GenerateForPawnTip".Translate());

            if (enabled != before)
            {

                component.SetDiaryGenerationEnabled(pawn, enabled);

            }



            PawnDiarySettings settings = PawnDiaryMod.Settings;

            if (Prefs.DevMode && settings != null)
            {

                bool showPersonaSettings = settings.showPersonaSettings;

                bool showPersonaBefore = showPersonaSettings;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowPersonaSettings".Translate(),

                    ref showPersonaSettings,

                    "PawnDiary.Tab.ShowPersonaSettingsTip".Translate());

                if (showPersonaSettings != showPersonaBefore)
                {

                    settings.showPersonaSettings = showPersonaSettings;

                    writeGlobalSettings = true;

                }



                bool showLlmDebugInfo = settings.showLlmDebugInfo;

                bool showDebugBefore = showLlmDebugInfo;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),

                    ref showLlmDebugInfo,

                    "PawnDiary.Tab.ShowLlmDebugInfoTip".Translate());

                if (showLlmDebugInfo != showDebugBefore)
                {

                    settings.showLlmDebugInfo = showLlmDebugInfo;

                    writeGlobalSettings = true;

                }



                bool showGeneratingEntries = settings.showGeneratingEntries;

                bool showGeneratingBefore = showGeneratingEntries;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowGeneratingEntries".Translate(),

                    ref showGeneratingEntries,

                    "PawnDiary.Tab.ShowGeneratingEntriesTip".Translate());

                if (showGeneratingEntries != showGeneratingBefore)
                {

                    settings.showGeneratingEntries = showGeneratingEntries;

                    writeGlobalSettings = true;

                }

            }



            // Three dev fixtures, each on its own full-width row so the labels aren't clipped in the
            // narrow filter panel: mock-page filler (long-history scrolling), the prompt suite (opens a
            // dropdown of event categories; selecting one shows a single prompt-only card for that
            // category), and a clear button that deletes every prompt-test entry.

            Rect mockButtonRect = listing.GetRect(ControlLineHeight);

            Rect promptSuiteButtonRect = listing.GetRect(ControlLineHeight);

            Rect clearPromptSuiteButtonRect = listing.GetRect(ControlLineHeight);

            if (Widgets.ButtonText(mockButtonRect, "PawnDiary.Tab.FillMockEntries".Translate(DevMockDiaryTargetCount)))
            {

                int added = component.FillMockDiaryEntriesForDev(
                    pawn,
                    DevMockDiaryTargetCount,
                    DevMockDiaryTargetYears);

                if (added > 0)
                {

                    Messages.Message(

                        "PawnDiary.Tab.MockEntriesAdded".Translate(added, pawn.LabelShortCap),

                        MessageTypeDefOf.PositiveEvent,

                        false);

                }

                else

                {

                    Messages.Message(

                        "PawnDiary.Tab.MockEntriesAlreadyFilled".Translate(DevMockDiaryTargetCount, pawn.LabelShortCap),

                        MessageTypeDefOf.NeutralEvent,

                        false);

                }

            }



            TooltipHandler.TipRegion(

                mockButtonRect,

                "PawnDiary.Tab.FillMockEntriesTip".Translate(
                    DevMockDiaryTargetCount,
                    DevMockDiaryTargetYears,
                    DevMockDiaryEntriesPerYear));



            if (Widgets.ButtonText(promptSuiteButtonRect, "PawnDiary.Tab.GeneratePromptSuite".Translate()))
            {

                HandleGeneratePromptSuite(pawn, component);

            }



            TooltipHandler.TipRegion(

                promptSuiteButtonRect,

                "PawnDiary.Tab.GeneratePromptSuiteTip".Translate());



            if (Widgets.ButtonText(clearPromptSuiteButtonRect, "PawnDiary.Tab.ClearPromptSuite".Translate()))
            {

                HandleClearPromptSuite(pawn, component);

            }



            TooltipHandler.TipRegion(

                clearPromptSuiteButtonRect,

                "PawnDiary.Tab.ClearPromptSuiteTip".Translate());


            DrawDevPreviewButtons(listing, pawn);



            // The base-style picker used to live here behind ShouldShowPersonaSettings(); it moved to

            // the player Writing Style dialog opened by the header icon, which also exposes the custom

            // prompt and override explanation, so this dev-only duplicate is gone.

            }
            finally
            {
                listing.End();
            }

            if (writeGlobalSettings)
            {

                WriteGlobalSettings();

            }

        }



        /// <summary>
        /// True when the Diary tab should offer the player-facing writing-style editor for this pawn.
        /// The tab can render children and corpses, but only diary-eligible pawns can store the result.
        /// </summary>
        private static bool ShouldDrawWritingStyleButton(Pawn pawn, DiaryGameComponent component)
        {
            return pawn != null && component != null && DiaryGameComponent.IsDiaryEligible(pawn);
        }

        /// <summary>
        /// Draws the subtle header icon that opens <see cref="Dialog_PawnWritingStyle"/>. It is
        /// read-only during draw; Save/Reset in the dialog remain the only mutation points.
        /// </summary>
        private void DrawWritingStyleHeaderIcon(Rect rect, Pawn pawn, DiaryGameComponent component)
        {
            WritingStyleResolution resolution = component.ResolveWritingStyleFor(pawn);
            // Read-only (no EnsureVoiceStage): the tooltip must not roll/mutate during a draw pass.
            PsychotypeResolution psychotype = component.ResolvePsychotypeForDisplay(pawn);

            // Base/mouseover-color overload so the quiet alpha is honored: the 2-arg ButtonImage
            // overload forces GUI.color to white/mouseover and would draw the icon at full strength.
            Color baseColor = new Color(1f, 1f, 1f, Mathf.Clamp01(WritingStyleIconAlpha));
            Color hoverColor = new Color(1f, 1f, 1f, Mathf.Clamp01(WritingStyleIconHoverAlpha));
            if (Widgets.ButtonImage(rect, DiaryButtonTextures.WritingStyle, baseColor, hoverColor))
            {
                OpenWritingStyleDialog(pawn, component);
            }

            TooltipHandler.TipRegion(rect, WritingStyleTooltip(resolution, psychotype, component));
        }

        /// <summary>
        /// Toggles the writing-style dialog for the pawn: a second click on the header icon closes the
        /// editor that is already open, otherwise it opens one (still avoiding duplicate editors that
        /// could save over each other).
        /// </summary>
        private static void OpenWritingStyleDialog(Pawn pawn, DiaryGameComponent component)
        {
            Dialog_PawnWritingStyle existing =
                Find.WindowStack.Windows.OfType<Dialog_PawnWritingStyle>().FirstOrDefault(w => w.IsFor(pawn));
            if (existing != null)
            {
                existing.Close();
            }
            else
            {
                Find.WindowStack.Add(new Dialog_PawnWritingStyle(pawn, component));
            }
        }

        /// <summary>
        /// Tooltip text for the icon button: current style first, then the editor affordance and any
        /// active custom/override status previously shown as row text.
        /// </summary>
        private static string WritingStyleTooltip(WritingStyleResolution resolution,
            PsychotypeResolution psychotype, DiaryGameComponent component)
        {
            string tooltip =
                "PawnDiary.Tab.WritingStyle".Translate(WritingStyleLabel(resolution)).Resolve();

            // Surface the psychotype (outlook) next to the style. Shown only when the layer is enabled,
            // since a disabled layer never reaches the prompt. The same header icon edits both.
            if (component != null && component.PsychotypeLayerEnabled)
            {
                tooltip += "\n" + "PawnDiary.Tab.Psychotype".Translate(PsychotypeLabel(psychotype)).Resolve();
            }

            tooltip += "\n\n" + "PawnDiary.Tab.WritingStyleTip".Translate().Resolve();
            string status = WritingStyleStatusLabel(resolution);
            if (!string.IsNullOrWhiteSpace(status))
            {
                tooltip += "\n" + status;
            }

            return tooltip;
        }

        // The psychotype label for the tooltip: the active external override's source, else the base
        // type label, falling back to "neutral".
        private static string PsychotypeLabel(PsychotypeResolution psychotype)
        {
            if (psychotype == null)
            {
                return "PawnDiary.Psychotype.NeutralLabel".Translate();
            }

            if (psychotype.source == PsychotypeRuleSource.ExternalApiOverride)
            {
                return string.IsNullOrWhiteSpace(psychotype.externalSourceId)
                    ? "PawnDiary.Psychotype.ExternalSourceLabel".Translate().ToString()
                    : psychotype.externalSourceId;
            }

            return string.IsNullOrWhiteSpace(psychotype.baseTypeLabel)
                ? "PawnDiary.Psychotype.NeutralLabel".Translate().ToString()
                : psychotype.baseTypeLabel;
        }

        /// <summary>
        /// Resolves the human-readable label for the effective writing style shown in the icon tooltip.
        /// Prefers the active override's label, then the base style label, falling back to "default".
        /// </summary>
        private static string WritingStyleLabel(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate();
            }

            switch (resolution.source)
            {
                case WritingStyleRuleSource.ExternalApiOverride:
                    return "PawnDiary.WritingStyle.ExternalSourceLabel".Translate();
                case WritingStyleRuleSource.HediffOverride:
                    return string.IsNullOrWhiteSpace(resolution.hediffStyleLabel)
                        ? (resolution.hediffStyleDefName ?? string.Empty)
                        : resolution.hediffStyleLabel;
                default:
                    return string.IsNullOrWhiteSpace(resolution.baseStyleLabel)
                        ? (resolution.baseStyleDefName ?? string.Empty)
                        : resolution.baseStyleLabel;
            }
        }

        /// <summary>
        /// Returns the compact status hint for the writing-style tooltip: "Custom", "Override", or
        /// nothing for a plain base style.
        /// </summary>
        private static string WritingStyleStatusLabel(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return string.Empty;
            }

            if (resolution.source == WritingStyleRuleSource.ExternalApiOverride
                || resolution.source == WritingStyleRuleSource.HediffOverride)
            {
                return "PawnDiary.WritingStyle.OverrideActive".Translate().Resolve();
            }

            if (!string.IsNullOrWhiteSpace(resolution.customRule))
            {
                return "PawnDiary.WritingStyle.CustomActive".Translate().Resolve();
            }

            return string.Empty;
        }



        /// <summary>
        /// Draws compact animated dots while hidden pending entries are being written.
        /// </summary>
        private static void DrawWritingIndicator(Rect rect)
        {

            DrawWritingDots(

                new Rect(rect.x + rect.width * 0.5f - 10f, rect.y + rect.height * 0.5f - 2f, 24f, 8f),

                new Color(0.78f, 0.95f, 0.78f),

                0f);

        }

        /// <summary>
        /// Shows while the Diary tab is indexing a very large saved history over several frames.
        /// </summary>
        private static void DrawDiaryLoading(Rect rect, int processed, int total)
        {
            Widgets.DrawMenuSection(rect);

            Rect inner = rect.ContractedBy(14f);
            int safeTotal = Math.Max(0, total);
            int safeProcessed = Math.Min(Math.Max(0, processed), safeTotal);
            string label = safeTotal > 0
                ? "PawnDiary.Tab.LoadingHistoryProgress".Translate(safeProcessed, safeTotal).ToString()
                : "PawnDiary.Tab.LoadingHistory".Translate().ToString();

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width, 28f), label);
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            float dotsWidth = WritingDotSize * 3f + WritingDotGap * 2f;
            Rect dotsRect = new Rect(
                inner.x + inner.width * 0.5f - dotsWidth * 0.5f,
                inner.y + 54f,
                dotsWidth,
                12f);
            DrawWritingDots(dotsRect, UiStyle.WritingPlaceholderHighColor, 0.65f);
        }



        /// <summary>
        /// True when an entry has either actual LLM output or a finished archive fallback ready for
        /// the production diary list.
        /// </summary>
        private static bool IsGenerated(DiaryEntryView entry)
        {

            return entry != null && (!string.IsNullOrWhiteSpace(entry.GeneratedText) || IsArchivedGenerationFallback(entry));

        }



        /// <summary>
        /// Dev-mode preference gate for manual persona editing in the Diary tab.
        /// </summary>
        private static bool ShouldShowPersonaSettings()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showPersonaSettings;

        }



        /// <summary>
        /// Dev-mode preference gate for raw/pending entries and the LLM prompt/status block.
        /// </summary>
        private static bool ShouldShowLlmDebugInfo()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showLlmDebugInfo;

        }



        /// <summary>
        /// Dev-mode gate for prompt-only cards captured by the no-generation prompt test setting.
        /// </summary>
        private static bool ShouldShowPromptOnlyEntries()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.promptTestMode;

        }



        /// <summary>
        /// Dev-mode preference gate for revealing entries still in the LLM generation pipeline
        /// (in-progress or stuck), without the full prompt/status diagnostic block.
        /// </summary>
        private static bool ShouldShowGeneratingEntries()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showGeneratingEntries;

        }



        /// <summary>
        /// Dev-only handler for the "Prompt suite" button. Enables prompt test mode (so the queue
        /// captures prompts instead of calling an LLM), then opens a dropdown of the event categories
        /// sourced from <see cref="DiaryGameComponent.AvailableSuiteEntriesForDev"/>. Picking one calls
        /// back into the component, which deletes any prior test entry and captures exactly one
        /// prompt-only card for the chosen category. Pair categories are omitted from the menu when no
        /// second colonist is available.
        /// </summary>
        private void HandleGeneratePromptSuite(Pawn pawn, DiaryGameComponent component)
        {

            if (pawn == null || component == null)
            {

                return;

            }



            PawnDiarySettings settings = PawnDiaryMod.Settings;

            if (settings != null && !settings.promptTestMode)
            {

                settings.promptTestMode = true;

                WriteGlobalSettings();

            }



            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries = component.AvailableSuiteEntriesForDev(pawn);

            if (entries == null || entries.Count == 0)
            {

                Messages.Message(

                    "PawnDiary.Tab.PromptSuiteEmpty".Translate(pawn.LabelShortCap),

                    MessageTypeDefOf.NeutralEvent,

                    false);

                return;

            }



            List<FloatMenuOption> options = new List<FloatMenuOption>();

            for (int i = 0; i < entries.Count; i++)
            {

                DiaryGameComponent.DevPromptSuiteEntry entry = entries[i];

                string entryLabel = entry.labelKey.Translate();

                Pawn selectedPawn = pawn;

                DiaryGameComponent.DevPromptSuiteEntry captured = entry;

                options.Add(new FloatMenuOption(entryLabel, delegate
                {

                    bool shown = component.ShowPromptSuiteEntryForDev(selectedPawn, captured);

                    Messages.Message(

                        shown
                            ? "PawnDiary.Tab.PromptSuiteShown".Translate(selectedPawn.LabelShortCap, entryLabel)
                            : "PawnDiary.Tab.PromptSuiteEmpty".Translate(selectedPawn.LabelShortCap),

                        shown ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,

                        false);

                }));

            }



            Find.WindowStack.Add(new FloatMenu(options));

        }



        /// <summary>
        /// Dev-only handler for the "Clear test prompts" button: deletes every prompt-test entry from
        /// all colonists' diaries and reports how many were removed.
        /// </summary>
        private void HandleClearPromptSuite(Pawn pawn, DiaryGameComponent component)
        {

            if (component == null)
            {

                return;

            }



            int removed = component.ClearPromptSuiteForDev();

            Messages.Message(

                "PawnDiary.Tab.PromptSuiteCleared".Translate(removed),

                MessageTypeDefOf.NeutralEvent,

                false);

        }



        /// <summary>
        /// Persists global mod UI preferences changed from this pawn tab.
        /// </summary>
        private static void WriteGlobalSettings()
        {

            PawnDiaryMod mod = LoadedModManager.GetMod<PawnDiaryMod>();

            if (mod != null)
            {

                mod.WriteSettings();

            }

        }
    }
}
