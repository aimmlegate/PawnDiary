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
        /// Returns the height needed for the per-pawn controls above the diary list. The Writing style
        /// row is always shown to players; the dev-only troubleshooting block is folded into this same
        /// height only when RimWorld dev mode is on.
        /// </summary>
        private static float PawnControlsHeight()
        {
            float height = WritingStyleRowHeight + WritingStyleRowGap;
            if (Prefs.DevMode)
            {
                height += DevControlsHeight;
            }

            return height;
        }



        /// <summary>
        /// Renders the per-pawn Writing style row (always shown to players) plus dev-mode-only
        /// troubleshooting controls below it. The player row is drawn first so it always appears,
        /// then the dev block fills the remaining reserved height when RimWorld dev mode is on.
        /// </summary>
        private void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {

            if (pawn == null || component == null)
            {

                return;

            }



            // Player-facing row: a "Writing style" button + status hint. Always visible so players can
            // experiment with this pawn's voice straight from the Diary tab, no dev mode required.
            Rect writingStyleRow = new Rect(rect.x, rect.y, rect.width, WritingStyleRowHeight);
            DrawWritingStyleRow(writingStyleRow, pawn, component);



            if (!Prefs.DevMode)
            {

                return;

            }



            // Dev-only block sits below the player row inside the reserved height. The controls rect
            // starts after the player row plus its gap.
            Rect devRect = new Rect(
                rect.x,
                writingStyleRow.yMax + WritingStyleRowGap,
                rect.width,
                Mathf.Max(0f, rect.height - WritingStyleRowHeight - WritingStyleRowGap));

            Listing_Standard listing = new Listing_Standard();

            listing.Begin(devRect);



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

            bool writeGlobalSettings = false;

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



            // Three dev fixtures share one row: mock-page filler (long-history scrolling), the prompt
            // suite (opens a dropdown of event categories; selecting one shows a single prompt-only
            // card for that category), and a clear button that deletes every prompt-test entry.

            Rect devButtonRow = listing.GetRect(ControlLineHeight);

            float devButtonGap = 4f;

            float devButtonWidth = (devButtonRow.width - devButtonGap * 2f) / 3f;

            Rect mockButtonRect = new Rect(devButtonRow.x, devButtonRow.y, devButtonWidth, devButtonRow.height);

            Rect promptSuiteButtonRect = new Rect(mockButtonRect.xMax + devButtonGap, devButtonRow.y, devButtonWidth, devButtonRow.height);

            Rect clearPromptSuiteButtonRect = new Rect(promptSuiteButtonRect.xMax + devButtonGap, devButtonRow.y, devButtonWidth, devButtonRow.height);

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

            // the always-visible player Writing Style dialog (DrawWritingStyleRow above), which also

            // exposes the custom prompt and override explanation, so this dev-only duplicate is gone.

            listing.End();



            if (writeGlobalSettings)
            {

                WriteGlobalSettings();

            }

        }



        /// <summary>
        /// Draws the always-visible player Writing style row: a button showing the current style plus
        /// a compact status hint when a custom prompt or a temporary override is active. Clicking the
        /// button opens <see cref="Dialog_PawnWritingStyle"/>. Read-only here — no save mutation.
        /// </summary>
        private void DrawWritingStyleRow(Rect rect, Pawn pawn, DiaryGameComponent component)
        {
            // The Diary tab is visible for any humanlike colonist — including children and corpses —
            // but only diary-eligible pawns can actually store a style (SetPersona/SetCustomWritingStyleRule
            // require it). Gate the editable row to match, so the editor never opens just to fail on Save.
            if (!DiaryGameComponent.IsDiaryEligible(pawn))
            {
                DrawWritingStyleUnavailable(rect);
                return;
            }

            WritingStyleResolution resolution = component.ResolveWritingStyleFor(pawn);

            string buttonLabel = "PawnDiary.Tab.WritingStyle".Translate(WritingStyleLabel(resolution));
            float buttonWidth = Mathf.Min(rect.width * 0.5f, 320f);
            Rect buttonRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(buttonRect, buttonLabel))
            {
                // Don't open a second editor for the same pawn; a concurrent Save would clobber this one.
                if (!Find.WindowStack.Windows.OfType<Dialog_PawnWritingStyle>().Any(w => w.IsFor(pawn)))
                {
                    Find.WindowStack.Add(new Dialog_PawnWritingStyle(pawn, component));
                }
            }

            TooltipHandler.TipRegion(buttonRect, "PawnDiary.Tab.WritingStyleTip".Translate());

            // Status hint to the right of the button: which layer is active right now.
            Rect statusRect = new Rect(
                buttonRect.xMax + WritingStyleStatusLeftGap,
                rect.y,
                Mathf.Max(0f, rect.width - buttonWidth - WritingStyleStatusLeftGap),
                rect.height);
            DrawWritingStyleStatus(statusRect, resolution);
        }

        /// <summary>
        /// Draws a muted "writing style unavailable" hint for pawns the Diary tab shows but that are
        /// not diary-eligible (children, corpses), so no editable button is offered for them.
        /// </summary>
        private static void DrawWritingStyleUnavailable(Rect rect)
        {
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiStyle.quietTextColor.ToColor(Color.gray);
            Widgets.Label(rect, "PawnDiary.Tab.WritingStyleUnavailable".Translate());
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        /// <summary>
        /// Resolves the human-readable label for the effective writing style shown on the row button.
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
        /// Draws the compact status hint next to the writing-style button: "Custom", "Override", or
        /// nothing for a plain base style. Muted color so it does not compete with the diary text.
        /// </summary>
        private static void DrawWritingStyleStatus(Rect rect, WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return;
            }

            string status;
            if (resolution.source == WritingStyleRuleSource.ExternalApiOverride
                || resolution.source == WritingStyleRuleSource.HediffOverride)
            {
                status = "PawnDiary.WritingStyle.OverrideActive".Translate();
            }
            else if (!string.IsNullOrWhiteSpace(resolution.customRule))
            {
                status = "PawnDiary.WritingStyle.CustomActive".Translate();
            }
            else
            {
                return;
            }

            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiStyle.quietTextColor.ToColor(Color.gray);
            Widgets.Label(rect, status);
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
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
