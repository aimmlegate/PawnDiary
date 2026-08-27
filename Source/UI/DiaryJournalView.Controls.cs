// Player actions, dev controls, and per-pawn setting helpers for the Diary tab.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Control and settings helpers for the reusable diary journal renderer.
    /// </summary>
    internal sealed partial class DiaryJournalView
    {
        /// <summary>
        /// Draws the filter-panel export glyph for the current subject's applied reader view. It remains
        /// visibly disabled until the selected year's entry list has finished loading.
        /// </summary>
        private void DrawMarkdownExportButton(
            Rect rect,
            DiaryReaderSubject subject,
            DiaryGameComponent component,
            List<DiaryEntryView> orderedEntries,
            int visibleRevision,
            bool showLlmDebugInfo,
            bool exportReady)
        {
            string displayName = string.IsNullOrWhiteSpace(subject.DisplayName)
                ? "PawnDiary.Reader.UnknownPawn".Translate().ToString()
                : subject.DisplayName;
            bool active = exportReady && subject.IsValid && component != null;
            float baseAlpha = active ? WritingStyleIconAlpha : WritingStyleIconAlpha * 0.4f;
            float hoverAlpha = active ? WritingStyleIconHoverAlpha : baseAlpha;
            bool clicked = Widgets.ButtonImage(
                rect,
                DiaryButtonTextures.Export,
                new Color(1f, 1f, 1f, Mathf.Clamp01(baseAlpha)),
                new Color(1f, 1f, 1f, Mathf.Clamp01(hoverAlpha)),
                active);
            if (active && clicked)
            {
                HandleMarkdownExport(
                    subject,
                    component,
                    orderedEntries,
                    visibleRevision,
                    showLlmDebugInfo);
            }

            TooltipHandler.TipRegion(
                rect,
                "PawnDiary.Export.ButtonTip".Translate(displayName).Resolve());
        }

        /// <summary>
        /// Snapshots the selected year plus every live filter, runs the export on the main thread, reports
        /// a normal player message, and copies the resulting path for easy discovery.
        /// </summary>
        private void HandleMarkdownExport(
            DiaryReaderSubject subject,
            DiaryGameComponent component,
            List<DiaryEntryView> orderedEntries,
            int visibleRevision,
            bool showLlmDebugInfo)
        {
            if (!subject.IsValid || component == null)
            {
                return;
            }

            List<DiaryEntryView> includedEntries = orderedEntries;
            if (JournalFiltersActive && orderedEntries != null)
            {
                includedEntries = EnsureFilteredJournalEntries(
                    orderedEntries,
                    visibleRevision,
                    showLlmDebugInfo);
            }

            DiaryMarkdownExportRequest request = BuildMarkdownExportRequest(includedEntries);
            string displayName = string.IsNullOrWhiteSpace(subject.DisplayName)
                ? "PawnDiary.Reader.UnknownPawn".Translate().ToString()
                : subject.DisplayName;
            string filePath;
            string error;
            int pageCount;
            if (component.TryExportPawnDiaryMarkdown(
                subject.PawnId,
                displayName,
                subject.Alive,
                request,
                out filePath,
                out pageCount,
                out error))
            {
                GUIUtility.systemCopyBuffer = filePath;
                Messages.Message(
                    "PawnDiary.Export.Done".Translate(displayName, pageCount, filePath),
                    MessageTypeDefOf.PositiveEvent,
                    false);
                return;
            }

            Messages.Message(
                "PawnDiary.Export.Failed".Translate(displayName, error),
                MessageTypeDefOf.RejectInput,
                false);
        }

        /// <summary>
        /// Converts the current UI state into a plain export request. Entry keys preserve the exact
        /// search/favorite/tag result, and selectedYear remains explicit so year paging is a real filter.
        /// </summary>
        private DiaryMarkdownExportRequest BuildMarkdownExportRequest(List<DiaryEntryView> includedEntries)
        {
            DiaryMarkdownExportRequest request = new DiaryMarkdownExportRequest
            {
                selectedYear = selectedYear
            };

            if (includedEntries != null)
            {
                for (int i = 0; i < includedEntries.Count; i++)
                {
                    DiaryEntryView entry = includedEntries[i];
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.EntryKey))
                    {
                        request.includedEntryKeys.Add(entry.EntryKey);
                    }
                }
            }

            request.metadata.Add(new DiaryMarkdownMetadata
            {
                label = "PawnDiary.Export.GameTimeLabel".Translate().Resolve(),
                value = CurrentMarkdownExportGameTime()
            });
            request.metadata.Add(new DiaryMarkdownMetadata
            {
                label = "PawnDiary.Export.FiltersLabel".Translate().Resolve(),
                value = MarkdownExportFilterSummary()
            });
            return request;
        }

        /// <summary>
        /// Formats the applied reader filters for the Markdown header. A short, inactive search query is
        /// omitted because it does not narrow the cards; the selected year is always recorded.
        /// </summary>
        private string MarkdownExportFilterSummary()
        {
            List<string> filters = new List<string>();
            string yearValue = selectedYear == UnknownYear
                ? "PawnDiary.Tab.UnknownYear".Translate().Resolve()
                : selectedYear.ToString();
            filters.Add("PawnDiary.Export.FilterYear".Translate(yearValue).Resolve());

            if (JournalSearchActive)
            {
                filters.Add(
                    "PawnDiary.Export.FilterSearch".Translate(ActiveJournalSearchQuery).Resolve());
            }

            if (filterFavoritesOnly)
            {
                filters.Add("PawnDiary.Tab.FilterFavoritesOnly".Translate().Resolve());
            }

            if (filterActiveTags.Count > 0)
            {
                List<string> tags = filterActiveTags.ToList();
                tags.Sort(StringComparer.CurrentCultureIgnoreCase);
                filters.Add(
                    "PawnDiary.Export.FilterTags".Translate(DiaryListText.JoinComma(tags)).Resolve());
            }

            return string.Join("; ", filters.ToArray());
        }

        /// <summary>
        /// Returns RimWorld's own localized date-and-hour string at the same nominal longitude used by
        /// saved diary dates. The fallback keeps the metadata row present during unusual teardown states.
        /// </summary>
        private static string CurrentMarkdownExportGameTime()
        {
            TickManager ticks = Find.TickManager;
            return ticks == null
                ? "PawnDiary.Export.UnknownGameTime".Translate().Resolve()
                : GenDate.DateFullStringWithHourAt(ticks.TicksAbs, Vector2.zero);
        }

        /// <summary>
        /// Returns the height needed for per-pawn dev controls above the diary list. The player-facing
        /// Diary profile opener lives in the header icon, so normal play reserves no extra row.
        /// </summary>
        private static float PawnControlsHeight(float availableWidth)
        {
            if (!Prefs.DevMode)
            {
                return 0f;
            }

            float width = Mathf.Max(1f, availableWidth);
            float height = 0f;
            if (PawnDiaryMod.Settings != null)
            {
                height += DevCheckboxRowHeight(
                    "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),
                    width);
                height += DevCheckboxRowHeight(
                    "PawnDiary.Tab.ShowGeneratingEntries".Translate(),
                    width);
            }

            // The three action buttons use explicit fixed-height Listing rows below.
            return height + 3f * ControlLineHeight;
        }

        /// <summary>
        /// Mirrors Listing_Standard.CheckboxLabeled's wrapped-label measurement plus its default
        /// two-pixel vertical spacing. Measuring against the actual filter width prevents translated
        /// dev labels from pushing the final safety buttons outside their reserved rectangle.
        /// </summary>
        private static float DevCheckboxRowHeight(string label, float availableWidth)
        {
            return Mathf.Max(Text.LineHeight, Text.CalcHeight(label ?? string.Empty, availableWidth)) + 2f;
        }



        /// <summary>
        /// Renders dev-mode-only troubleshooting controls. Player-facing Diary profile editing is
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



            PawnDiarySettings settings = PawnDiaryMod.Settings;

            if (Prefs.DevMode && settings != null)
            {
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



            // Keep the pawn filter focused: one prompt-fixture selector, a safe way to remove only
            // those fixtures, and one explicitly confirmed destructive full-history reset. Broader
            // mock/formatting test grids remain in Debug Actions.
            Rect promptSuiteButtonRect = listing.GetRect(ControlLineHeight);
            Rect clearPromptSuiteButtonRect = listing.GetRect(ControlLineHeight);
            Rect purgeHistoryButtonRect = listing.GetRect(ControlLineHeight);
            if (Widgets.ButtonText(promptSuiteButtonRect, "PawnDiary.Tab.GeneratePromptSuite".Translate()))
            {

                HandleGeneratePromptSuite(pawn, component);

            }



            TooltipHandler.TipRegion(

                promptSuiteButtonRect,

                "PawnDiary.Tab.GeneratePromptSuiteTip".Translate());



            if (Widgets.ButtonText(
                clearPromptSuiteButtonRect,
                "PawnDiary.Tab.ClearPromptSuite".Translate()))
            {
                HandleClearPromptSuite(component);
            }



            TooltipHandler.TipRegion(
                clearPromptSuiteButtonRect,
                "PawnDiary.Tab.ClearPromptSuiteTip".Translate());



            if (Widgets.ButtonText(
                purgeHistoryButtonRect,
                "PawnDiary.Tab.PurgeDiaryHistory".Translate()))
            {
                HandlePurgeDiaryHistory(pawn, component);
            }



            TooltipHandler.TipRegion(

                purgeHistoryButtonRect,
                "PawnDiary.Tab.PurgeDiaryHistoryTip".Translate(pawn.LabelShortCap));



            // The old per-pawn generation/persona toggles now live together in the player-facing Diary
            // profile, so this panel contains troubleshooting controls only.

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
        /// True when the current subject can receive a new player-authored page. This intentionally
        /// ignores the automatic-generation toggle: pausing the LLM must not disable manual writing.
        /// </summary>
        private static bool ShouldDrawManualEntryCreateButton(
            DiaryReaderSubject subject,
            DiaryGameComponent component)
        {
            return subject.Alive
                && subject.Pawn != null
                && component != null
                && DiaryGameComponent.IsDiaryEligible(subject.Pawn);
        }

        /// <summary>
        /// Draws the bordered storage icon that opens the selected owner's unified Memory Library.
        /// The glyph follows the neighboring quiet header icons; the thin outline preserves the button
        /// affordance previously supplied by the wider text button without consuming title space.
        /// </summary>
        private static void DrawMemoryLibraryHeaderIcon(Rect rect, string pawnId)
        {
            bool hover = Mouse.IsOver(rect);
            float alpha = hover ? WritingStyleIconHoverAlpha : WritingStyleIconAlpha;
            float padding = Mathf.Clamp(
                UiStyle.memoryLibraryDiaryIconPadding,
                0f,
                Mathf.Max(0f, Mathf.Min(rect.width, rect.height) * 0.4f));
            Rect iconRect = rect.ContractedBy(padding);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(iconRect, DiaryButtonTextures.Memory, ScaleMode.ScaleToFit);

            int borderThickness = Mathf.Clamp(
                UiStyle.memoryLibraryDiaryIconBorderThickness,
                0,
                3);
            if (borderThickness > 0)
            {
                GUI.color = UiStyle.MemoryLibraryDiaryIconBorderColor;
                Widgets.DrawBox(rect, borderThickness);
            }

            GUI.color = oldColor;
            TooltipHandler.TipRegion(
                rect,
                "PawnDiary.Memory.Library.MemoriesAction".Translate());
            if (Widgets.ButtonInvisible(rect))
            {
                Dialog_MemoryLibrary.OpenForOwner(pawnId);
            }
        }

        /// <summary>
        /// Draws the quiet header plus icon that opens a detached new-page draft.
        /// </summary>
        private static void DrawManualEntryCreateHeaderIcon(
            Rect rect,
            Pawn pawn,
            DiaryGameComponent component)
        {
            Color baseColor = new Color(1f, 1f, 1f, Mathf.Clamp01(WritingStyleIconAlpha));
            Color hoverColor = new Color(1f, 1f, 1f, Mathf.Clamp01(WritingStyleIconHoverAlpha));
            if (Widgets.ButtonImage(rect, DiaryButtonTextures.NewEntry, baseColor, hoverColor))
            {
                OpenManualEntryCreateDialog(pawn, component);
            }

            string pawnName = pawn == null ? string.Empty : pawn.LabelShortCap.ToString();
            TooltipHandler.TipRegion(
                rect,
                Dialog_DiaryEntryEditor.FormatPlayerTextFrame(
                    "PawnDiary.ManualEntry.NewTip",
                    pawnName));
        }

        /// <summary>
        /// Opens one new-page editor, or focuses the existing manual editor so two detached drafts
        /// cannot later overwrite each other in a surprising order.
        /// </summary>
        private static void OpenManualEntryCreateDialog(Pawn pawn, DiaryGameComponent component)
        {
            if (pawn == null || component == null || FocusExistingManualEntryEditor())
            {
                return;
            }

            Find.WindowStack.Add(Dialog_DiaryEntryEditor.ForCreate(
                pawn,
                component,
                DiaryGameComponent.ManualEntryTitleMaxCharacters,
                DiaryGameComponent.ManualEntryBodyMaxCharacters));
        }

        /// <summary>Focuses the singleton manual editor when a detached draft is already open.</summary>
        private static bool FocusExistingManualEntryEditor()
        {
            Dialog_DiaryEntryEditor existing =
                Find.WindowStack.Windows.OfType<Dialog_DiaryEntryEditor>().FirstOrDefault();
            if (existing == null)
            {
                return false;
            }

            Find.WindowStack.Notify_ManuallySetFocus(existing);
            return true;
        }

        /// <summary>
        /// Resolves a fresh detached snapshot for one exact persisted page before opening the editor.
        /// The stable pawn/event/POV identity is used instead of EntryKey, whose damaged-row fallback is
        /// intentionally suitable only for transient UI caches.
        /// </summary>
        private static void OpenManualEntryEditDialog(
            string pawnId,
            string pawnDisplayName,
            DiaryEntryView entry,
            DiaryGameComponent component)
        {
            if (entry == null || component == null || FocusExistingManualEntryEditor())
            {
                return;
            }

            ManualDiaryEntrySnapshot snapshot;
            if (!component.TryGetManualEntrySnapshot(
                pawnId,
                entry.EventId,
                entry.PovRole,
                out snapshot))
            {
                Messages.Message(
                    "PawnDiary.ManualEntry.EntryUnavailable".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(pawnDisplayName)
                ? "PawnDiary.Reader.UnknownPawn".Translate().ToString()
                : pawnDisplayName;
            Find.WindowStack.Add(Dialog_DiaryEntryEditor.ForEdit(
                displayName,
                snapshot,
                component,
                DiaryGameComponent.ManualEntryTitleMaxCharacters,
                DiaryGameComponent.ManualEntryBodyMaxCharacters));
        }

        /// <summary>
        /// True when the Diary tab should offer the player-facing Diary profile editor for this pawn.
        /// The tab can render children and corpses, but only diary-eligible pawns can store the result.
        /// </summary>
        private static bool ShouldDrawWritingStyleButton(Pawn pawn, DiaryGameComponent component)
        {
            return pawn != null && component != null && DiaryGameComponent.IsDiaryEligible(pawn);
        }

        /// <summary>
        /// Draws the subtle header icon that opens <see cref="Dialog_PawnWritingStyle"/>. It is
        /// read-only during draw; the dialog's explicit Save is the profile mutation boundary.
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
        /// Toggles the Diary profile for the pawn: a second click on the header icon closes the
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

                    bool shown = component.ShowPromptSuiteEntryForCurrentPawnForDev(
                        selectedPawn,
                        captured);

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
        /// Opens RimWorld's destructive confirmation modal, then purges only the diary currently being
        /// viewed and posts a completion notification. No mutation occurs before the player confirms.
        /// </summary>
        private void HandlePurgeDiaryHistory(Pawn pawn, DiaryGameComponent component)
        {

            if (pawn == null || component == null || !Prefs.DevMode)
            {

                return;

            }



            string pawnName = pawn.LabelShortCap;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PawnDiary.Tab.PurgeDiaryHistoryConfirm".Translate(pawnName),
                delegate
                {
                    int removed = component.PurgeDiaryHistoryForPawnForDev(pawn);
                    Messages.Message(
                        "PawnDiary.Tab.DiaryHistoryPurged".Translate(removed, pawnName),
                        removed > 0
                            ? MessageTypeDefOf.PositiveEvent
                            : MessageTypeDefOf.NeutralEvent,
                        false);
                },
                true,
                "PawnDiary.Tab.PurgeDiaryHistoryTitle".Translate(pawnName).Resolve()));

        }



        /// <summary>
        /// Removes only synthetic prompt-suite pages. This gives dev users a non-destructive escape
        /// hatch beside the full diary-history purge.
        /// </summary>
        private static void HandleClearPromptSuite(DiaryGameComponent component)
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
            PawnDiaryMod.PersistSettingsImmediately(PawnDiaryMod.Settings);
        }
    }
}
