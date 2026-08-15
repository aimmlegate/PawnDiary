// Immediate-mode layout for Dialog_DiaryEntryEditor.
//
// This partial owns presentation only: measured responsive cards, selectors, text areas, warning and
// status panels, a content scroll view, and a fixed footer. Drawing may change detached input buffers
// or react to a button click, but it never polls generation or mutates a saved diary implicitly.
using System;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_DiaryEntryEditor
    {
        /// <summary>Draws a measured header, scrolling composer body, and fixed action footer.</summary>
        public override void DoWindowContents(Rect inRect)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            try
            {
                float fieldGap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
                float headerHeight = DrawHeader(new Rect(inRect.x, inRect.y, inRect.width, 0f), style);
                float footerHeight = MeasureFooterHeight(inRect.width, style);
                Rect footerRect = new Rect(
                    inRect.x,
                    Mathf.Max(inRect.y, inRect.yMax - footerHeight),
                    inRect.width,
                    footerHeight);
                float contentTop = inRect.y + headerHeight + fieldGap;
                Rect contentRect = new Rect(
                    inRect.x,
                    contentTop,
                    inRect.width,
                    Mathf.Max(0f, footerRect.y - fieldGap - contentTop));

                DrawScrollableComposer(contentRect, style);
                DrawFooter(footerRect, style);
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private float DrawHeader(Rect rect, DiaryUiStyleDef style)
        {
            float gap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
            string title = FormatPlayerTextFrame(
                Creating ? "PawnDiary.ManualEntry.NewTitle" : "PawnDiary.ManualEntry.EditTitle",
                pawnDisplayName);
            string subtitle = (Creating
                    ? "PawnDiary.ManualEntry.SubtitleCreate"
                    : "PawnDiary.ManualEntry.SubtitleEdit")
                .Translate().Resolve();

            Text.Font = GameFont.Medium;
            float titleHeight = Mathf.Ceil(Text.CalcHeight(title, rect.width));
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, titleHeight), title);

            Text.Font = GameFont.Tiny;
            float subtitleHeight = Mathf.Ceil(Text.CalcHeight(subtitle, rect.width));
            GUI.color = style.ManualEntryComposerMutedText;
            Widgets.Label(
                new Rect(rect.x, rect.y + titleHeight + gap * 0.5f, rect.width, subtitleHeight),
                subtitle);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return titleHeight + gap * 0.5f + subtitleHeight;
        }

        private void DrawScrollableComposer(Rect outRect, DiaryUiStyleDef style)
        {
            if (outRect.width <= 0f || outRect.height <= 0f) return;

            // RimWorld's standard scroll bar is stable widget chrome. Reserving it prevents editable
            // text from sitting below the grip while the viewRect still covers every measured control.
            float viewWidth = Mathf.Max(20f, outRect.width - 16f);
            Rect measureRect = new Rect(0f, 0f, viewWidth, 0f);
            float measuredHeight = LayoutComposer(measureRect, style, false);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(outRect.height, measuredHeight));

            Widgets.BeginScrollView(outRect, ref contentScroll, viewRect);
            try
            {
                LayoutComposer(viewRect, style, true);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>Runs the same vertical layout as a measurement pass and as a drawing pass.</summary>
        private float LayoutComposer(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float sectionGap = NonNegativeOr(style.manualEntryComposerSectionGap, 12f);
            float y = rect.y;

            if (Creating && !Reviewing)
            {
                y += LayoutModeSelector(new Rect(rect.x, y, rect.width, 0f), style, draw);
                y += sectionGap;
            }

            y += LayoutMetadata(new Rect(rect.x, y, rect.width, 0f), style, draw);
            y += sectionGap;

            // Keep progress/failure visible without forcing a player to scroll past long prompt fields.
            if (Pending)
            {
                y += LayoutStatusPanel(
                    new Rect(rect.x, y, rect.width, 0f),
                    "PawnDiary.ManualEntry.Generating".Translate().Resolve(),
                    string.Empty,
                    style.ManualEntryComposerMutedText,
                    style,
                    draw);
                y += sectionGap;
            }
            else if (draftStage == ComposerDraftStage.Failed)
            {
                y += LayoutStatusPanel(
                    new Rect(rect.x, y, rect.width, 0f),
                    "PawnDiary.ManualEntry.GenerationErrorTitle".Translate().Resolve(),
                    generationError,
                    style.ManualEntryComposerErrorText,
                    style,
                    draw);
                y += sectionGap;
            }

            if (Reviewing)
            {
                y += LayoutReview(new Rect(rect.x, y, rect.width, 0f), style, draw);
            }
            else if (selectedMode == PlayerEntryComposerMode.Context)
            {
                y += LayoutContextPrompt(new Rect(rect.x, y, rect.width, 0f), style, draw);
            }
            else if (selectedMode == PlayerEntryComposerMode.FullPrompt)
            {
                y += LayoutFullPrompt(new Rect(rect.x, y, rect.width, 0f), style, draw);
            }
            else
            {
                y += LayoutDirect(new Rect(rect.x, y, rect.width, 0f), style, draw);
            }

            return Mathf.Max(0f, y - rect.y);
        }

        private float LayoutModeSelector(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryComposerModeGap, 8f);
            bool compact = rect.width < PositiveOr(style.manualEntryComposerCompactWidth, 620f);
            PlayerEntryComposerMode[] modes =
            {
                PlayerEntryComposerMode.Direct,
                PlayerEntryComposerMode.Context,
                PlayerEntryComposerMode.FullPrompt
            };

            if (compact)
            {
                float y = rect.y;
                for (int i = 0; i < modes.Length; i++)
                {
                    float height = ModeCardHeight(rect.width, modes[i], style);
                    if (draw) DrawModeCard(new Rect(rect.x, y, rect.width, height), modes[i], style);
                    y += height + (i + 1 < modes.Length ? gap : 0f);
                }
                return y - rect.y;
            }

            float width = Mathf.Max(1f, (rect.width - gap * 2f) / 3f);
            float rowHeight = 0f;
            for (int i = 0; i < modes.Length; i++)
                rowHeight = Mathf.Max(rowHeight, ModeCardHeight(width, modes[i], style));
            if (draw)
            {
                for (int i = 0; i < modes.Length; i++)
                {
                    DrawModeCard(
                        new Rect(rect.x + i * (width + gap), rect.y, width, rowHeight),
                        modes[i],
                        style);
                }
            }
            return rowHeight;
        }

        private float ModeCardHeight(float width, PlayerEntryComposerMode mode, DiaryUiStyleDef style)
        {
            float padding = NonNegativeOr(style.manualEntryComposerPanelPadding, 10f);
            float gap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            float innerWidth = Mathf.Max(1f, width - padding * 2f);
            float measured = padding * 2f
                + MeasureText(ModeLabel(mode), innerWidth, GameFont.Small)
                + gap
                + MeasureText(ModeDescription(mode), innerWidth, GameFont.Tiny);
            return Mathf.Max(PositiveOr(style.manualEntryComposerModeMinHeight, 72f), measured);
        }

        private void DrawModeCard(Rect rect, PlayerEntryComposerMode mode, DiaryUiStyleDef style)
        {
            Widgets.DrawMenuSection(rect);
            if (selectedMode == mode) Widgets.DrawHighlightSelected(rect);
            bool enabled = !Pending;
            if (enabled) Widgets.DrawHighlightIfMouseover(rect);

            float padding = NonNegativeOr(style.manualEntryComposerPanelPadding, 10f);
            float gap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            Rect inner = rect.ContractedBy(padding);
            string label = ModeLabel(mode);
            string description = ModeDescription(mode);

            Text.Font = GameFont.Small;
            float labelHeight = Mathf.Ceil(Text.CalcHeight(label, inner.width));
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, labelHeight), label);
            Text.Font = GameFont.Tiny;
            GUI.color = style.ManualEntryComposerMutedText;
            Widgets.Label(
                new Rect(inner.x, inner.y + labelHeight + gap, inner.width,
                    Mathf.Max(0f, inner.height - labelHeight - gap)),
                description);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(rect, description);
            if (enabled && Widgets.ButtonInvisible(rect)) SelectMode(mode);
        }

        private static string ModeLabel(PlayerEntryComposerMode mode)
        {
            switch (mode)
            {
                case PlayerEntryComposerMode.Context:
                    return "PawnDiary.ManualEntry.ModeContext".Translate().Resolve();
                case PlayerEntryComposerMode.FullPrompt:
                    return "PawnDiary.ManualEntry.ModeFullPrompt".Translate().Resolve();
                default:
                    return "PawnDiary.ManualEntry.ModeWrite".Translate().Resolve();
            }
        }

        private static string ModeDescription(PlayerEntryComposerMode mode)
        {
            switch (mode)
            {
                case PlayerEntryComposerMode.Context:
                    return "PawnDiary.ManualEntry.ModeContextDescription".Translate().Resolve();
                case PlayerEntryComposerMode.FullPrompt:
                    return "PawnDiary.ManualEntry.ModeFullPromptDescription".Translate().Resolve();
                default:
                    return "PawnDiary.ManualEntry.ModeWriteDescription".Translate().Resolve();
            }
        }

        private float LayoutMetadata(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float padding = NonNegativeOr(style.manualEntryComposerPanelPadding, 10f);
            float gap = NonNegativeOr(style.manualEntryComposerSelectorGap, 12f);
            bool showTemplate = selectedMode == PlayerEntryComposerMode.Context;
            bool compact = rect.width < PositiveOr(style.manualEntryComposerCompactWidth, 620f);
            float innerWidth = Mathf.Max(1f, rect.width - padding * 2f);
            string typeDescription = SelectedEntryTypeDescription();
            string templateDescription = SelectedTemplateDescription();

            float contentHeight;
            if (showTemplate && !compact)
            {
                float columnWidth = Mathf.Max(1f, (innerWidth - gap) * 0.5f);
                contentHeight = Mathf.Max(
                    SelectorBlockHeight(columnWidth, typeDescription, style),
                    SelectorBlockHeight(columnWidth, templateDescription, style));
            }
            else
            {
                contentHeight = SelectorBlockHeight(innerWidth, typeDescription, style);
                if (showTemplate)
                    contentHeight += gap + SelectorBlockHeight(innerWidth, templateDescription, style);
            }

            float panelHeight = padding * 2f + contentHeight;
            if (!draw) return panelHeight;

            Rect panel = new Rect(rect.x, rect.y, rect.width, panelHeight);
            Widgets.DrawMenuSection(panel);
            Rect inner = panel.ContractedBy(padding);
            if (showTemplate && !compact)
            {
                float columnWidth = Mathf.Max(1f, (inner.width - gap) * 0.5f);
                DrawSelectorBlock(
                    new Rect(inner.x, inner.y, columnWidth, inner.height),
                    "PawnDiary.ManualEntry.EntryTypeLabel".Translate().Resolve(),
                    SelectedEntryTypeLabel(),
                    typeDescription,
                    !entryTypeLocked && !Pending && !Reviewing,
                    ShowEntryTypeMenu,
                    entryTypeLocked ? "PawnDiary.ManualEntry.EntryTypeLockedTip".Translate().Resolve() : string.Empty,
                    style);
                DrawSelectorBlock(
                    new Rect(inner.x + columnWidth + gap, inner.y, columnWidth, inner.height),
                    "PawnDiary.ManualEntry.TemplateLabel".Translate().Resolve(),
                    SelectedTemplateLabel(),
                    templateDescription,
                    !Pending && !Reviewing,
                    ShowTemplateMenu,
                    string.Empty,
                    style);
            }
            else
            {
                float typeHeight = SelectorBlockHeight(inner.width, typeDescription, style);
                DrawSelectorBlock(
                    new Rect(inner.x, inner.y, inner.width, typeHeight),
                    "PawnDiary.ManualEntry.EntryTypeLabel".Translate().Resolve(),
                    SelectedEntryTypeLabel(),
                    typeDescription,
                    !entryTypeLocked && !Pending && !Reviewing,
                    ShowEntryTypeMenu,
                    entryTypeLocked ? "PawnDiary.ManualEntry.EntryTypeLockedTip".Translate().Resolve() : string.Empty,
                    style);
                if (showTemplate)
                {
                    DrawSelectorBlock(
                        new Rect(inner.x, inner.y + typeHeight + gap, inner.width,
                            SelectorBlockHeight(inner.width, templateDescription, style)),
                        "PawnDiary.ManualEntry.TemplateLabel".Translate().Resolve(),
                        SelectedTemplateLabel(),
                        templateDescription,
                        !Pending && !Reviewing,
                        ShowTemplateMenu,
                        string.Empty,
                        style);
                }
            }
            return panelHeight;
        }

        private float SelectorBlockHeight(float width, string description, DiaryUiStyleDef style)
        {
            float fieldGap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
            float descriptionGap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            float labelHeight = MeasureText("Ag", width, GameFont.Small);
            float buttonHeight = PositiveOr(style.manualEntryComposerSelectorMinHeight, 34f);
            float descriptionHeight = string.IsNullOrWhiteSpace(description)
                ? 0f
                : MeasureText(description, width, GameFont.Tiny);
            return labelHeight + fieldGap + buttonHeight
                + (descriptionHeight > 0f ? descriptionGap + descriptionHeight : 0f);
        }

        private void DrawSelectorBlock(
            Rect rect,
            string label,
            string selectedLabel,
            string description,
            bool enabled,
            Action openMenu,
            string disabledTip,
            DiaryUiStyleDef style)
        {
            float fieldGap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
            float descriptionGap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            Text.Font = GameFont.Small;
            float labelHeight = Mathf.Ceil(Text.CalcHeight(label, rect.width));
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, labelHeight), label);

            float buttonHeight = PositiveOr(style.manualEntryComposerSelectorMinHeight, 34f);
            Rect button = new Rect(rect.x, rect.y + labelHeight + fieldGap, rect.width, buttonHeight);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && enabled;
            if (Widgets.ButtonText(button, selectedLabel ?? string.Empty)) openMenu?.Invoke();
            GUI.enabled = oldEnabled;
            if (!string.IsNullOrWhiteSpace(disabledTip)) TooltipHandler.TipRegion(button, disabledTip);

            if (!string.IsNullOrWhiteSpace(description))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = style.ManualEntryComposerMutedText;
                float top = button.yMax + descriptionGap;
                Widgets.Label(new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top)), description);
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;
        }

        private float LayoutDirect(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryComposerSectionGap, 12f);
            float y = rect.y;
            y += LayoutSingleLineField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.TitleLabel".Translate().Resolve(),
                ref titleBuffer,
                titleMaxCharacters,
                true,
                style,
                draw);
            y += gap;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.BodyLabel".Translate().Resolve(),
                string.Empty,
                ref bodyBuffer,
                bodyMaxCharacters,
                PositiveOr(style.manualEntryComposerLongAreaHeight, 240f),
                true,
                style,
                draw);
            return y - rect.y;
        }

        private float LayoutContextPrompt(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryComposerSectionGap, 12f);
            bool editable = !Pending;
            float y = rect.y;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.ContextSubjectLabel".Translate().Resolve(),
                "PawnDiary.ManualEntry.ContextSubjectHelp".Translate().Resolve(),
                ref contextSubjectBuffer,
                PlayerEntryComposerPolicy.ContextSummaryMaxCharacters,
                PositiveOr(style.manualEntryComposerShortAreaHeight, 104f),
                editable,
                style,
                draw);
            y += gap;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.ContextInstructionLabel".Translate().Resolve(),
                "PawnDiary.ManualEntry.ContextInstructionHelp".Translate().Resolve(),
                ref contextInstructionBuffer,
                PlayerEntryComposerPolicy.ContextInstructionMaxCharacters,
                PositiveOr(style.manualEntryComposerShortAreaHeight, 104f),
                editable,
                style,
                draw);
            y += gap;
            y += LayoutStatusPanel(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.ContextAddedTitle".Translate().Resolve(),
                "PawnDiary.ManualEntry.ContextAddedBody".Translate().Resolve(),
                style.ManualEntryComposerMutedText,
                style,
                draw);
            return y - rect.y;
        }

        private float LayoutFullPrompt(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryComposerSectionGap, 12f);
            bool editable = !Pending;
            float y = rect.y;
            y += LayoutWarning(new Rect(rect.x, y, rect.width, 0f), style, draw);
            y += gap;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.SystemPromptLabel".Translate().Resolve(),
                "PawnDiary.ManualEntry.SystemPromptHelp".Translate().Resolve(),
                ref rawSystemPromptBuffer,
                PlayerEntryComposerPolicy.RawPromptMaxCharacters,
                PositiveOr(style.manualEntryComposerSystemAreaHeight, 120f),
                editable,
                style,
                draw);
            y += gap;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.UserPromptLabel".Translate().Resolve(),
                "PawnDiary.ManualEntry.UserPromptHelp".Translate().Resolve(),
                ref rawUserPromptBuffer,
                PlayerEntryComposerPolicy.RawPromptMaxCharacters,
                PositiveOr(style.manualEntryComposerLongAreaHeight, 240f),
                editable,
                style,
                draw);
            return y - rect.y;
        }

        private float LayoutReview(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryComposerSectionGap, 12f);
            string badge = selectedMode == PlayerEntryComposerMode.Context
                ? "PawnDiary.ManualEntry.ReviewContextBadge".Translate().Resolve()
                : "PawnDiary.ManualEntry.ReviewFullPromptBadge".Translate().Resolve();
            string description = selectedMode == PlayerEntryComposerMode.Context
                ? "PawnDiary.ManualEntry.ReviewContextDescription".Translate().Resolve()
                : "PawnDiary.ManualEntry.ReviewFullPromptDescription".Translate().Resolve();
            if (selectedMode == PlayerEntryComposerMode.Context
                && !string.IsNullOrWhiteSpace(generatedTemplateKey))
            {
                description += "\n" + FormatPlayerTextFrame(
                    "PawnDiary.ManualEntry.ReviewTemplateUsed",
                    GeneratedTemplateLabel());
            }

            float y = rect.y;
            y += LayoutStatusPanel(
                new Rect(rect.x, y, rect.width, 0f),
                badge,
                description,
                style.ManualEntryComposerMutedText,
                style,
                draw);
            y += gap;
            y += LayoutSingleLineField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.TitleLabel".Translate().Resolve(),
                ref reviewTitleBuffer,
                titleMaxCharacters,
                true,
                style,
                draw);
            y += gap;
            y += LayoutTextAreaField(
                new Rect(rect.x, y, rect.width, 0f),
                "PawnDiary.ManualEntry.BodyLabel".Translate().Resolve(),
                string.Empty,
                ref reviewBodyBuffer,
                bodyMaxCharacters,
                PositiveOr(style.manualEntryComposerLongAreaHeight, 240f),
                true,
                style,
                draw);
            return y - rect.y;
        }

        private string GeneratedTemplateLabel()
        {
            for (int i = 0; i < promptTemplates.Count; i++)
            {
                if (string.Equals(promptTemplates[i]?.templateKey, generatedTemplateKey,
                    StringComparison.OrdinalIgnoreCase)) return promptTemplates[i].label;
            }
            return generatedTemplateKey;
        }

        private float LayoutWarning(Rect rect, DiaryUiStyleDef style, bool draw)
        {
            string title = "PawnDiary.ManualEntry.FullPromptWarningTitle".Translate().Resolve();
            string body = "PawnDiary.ManualEntry.FullPromptWarningBody".Translate().Resolve();
            float padding = NonNegativeOr(style.manualEntryComposerWarningPadding, 10f);
            float gap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            float innerWidth = Mathf.Max(1f, rect.width - padding * 2f);
            float height = padding * 2f
                + MeasureText(title, innerWidth, GameFont.Small)
                + gap
                + MeasureText(body, innerWidth, GameFont.Tiny);
            if (!draw) return height;

            Rect panel = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(panel, style.ManualEntryComposerWarningBackground);
            Color old = GUI.color;
            GUI.color = style.ManualEntryComposerWarningBorder;
            Widgets.DrawBox(panel);
            GUI.color = old;
            Rect inner = panel.ContractedBy(padding);
            Text.Font = GameFont.Small;
            float titleHeight = Mathf.Ceil(Text.CalcHeight(title, inner.width));
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleHeight), title);
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, inner.y + titleHeight + gap, inner.width,
                    Mathf.Max(0f, inner.height - titleHeight - gap)),
                body);
            Text.Font = GameFont.Small;
            return height;
        }

        private float LayoutStatusPanel(
            Rect rect,
            string title,
            string body,
            Color textColor,
            DiaryUiStyleDef style,
            bool draw)
        {
            float padding = NonNegativeOr(style.manualEntryComposerPanelPadding, 10f);
            float gap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            float innerWidth = Mathf.Max(1f, rect.width - padding * 2f);
            float titleHeight = MeasureText(title, innerWidth, GameFont.Small);
            float bodyHeight = string.IsNullOrWhiteSpace(body)
                ? 0f
                : MeasureText(body, innerWidth, GameFont.Tiny);
            float height = padding * 2f + titleHeight + (bodyHeight > 0f ? gap + bodyHeight : 0f);
            if (!draw) return height;

            Rect panel = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawMenuSection(panel);
            Rect inner = panel.ContractedBy(padding);
            GUI.color = textColor;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleHeight), title);
            if (bodyHeight > 0f)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(
                    new Rect(inner.x, inner.y + titleHeight + gap, inner.width, bodyHeight),
                    body);
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return height;
        }

        private float LayoutSingleLineField(
            Rect rect,
            string label,
            ref string value,
            int maximum,
            bool enabled,
            DiaryUiStyleDef style,
            bool draw)
        {
            float gap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
            float textPadding = NonNegativeOr(style.manualEntryComposerTextPadding, 6f);
            float labelHeight = FieldLabelHeight(rect.width, label, value?.Length ?? 0, maximum, style);
            float fieldHeight = Mathf.Max(
                PositiveOr(style.manualEntryComposerSelectorMinHeight, 34f),
                MeasureText("Ag", rect.width, GameFont.Small) + textPadding * 2f);
            float height = labelHeight + gap + fieldHeight;
            if (!draw) return height;

            DrawFieldLabel(new Rect(rect.x, rect.y, rect.width, labelHeight), label,
                value?.Length ?? 0, maximum, style);
            Rect field = new Rect(rect.x, rect.y + labelHeight + gap, rect.width, fieldHeight);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && enabled;
            value = Widgets.TextField(field, value ?? string.Empty) ?? string.Empty;
            GUI.enabled = oldEnabled;
            return height;
        }

        private float LayoutTextAreaField(
            Rect rect,
            string label,
            string help,
            ref string value,
            int maximum,
            float minimumAreaHeight,
            bool enabled,
            DiaryUiStyleDef style,
            bool draw)
        {
            float fieldGap = NonNegativeOr(style.manualEntryEditorFieldGap, 8f);
            float descriptionGap = NonNegativeOr(style.manualEntryComposerDescriptionGap, 4f);
            float textPadding = NonNegativeOr(style.manualEntryComposerTextPadding, 6f);
            float labelHeight = FieldLabelHeight(rect.width, label, value?.Length ?? 0, maximum, style);
            float helpHeight = string.IsNullOrWhiteSpace(help)
                ? 0f
                : MeasureText(help, rect.width, GameFont.Tiny);
            float textWidth = Mathf.Max(1f, rect.width - textPadding * 2f);
            float contentHeight = MeasureText(
                string.IsNullOrEmpty(value) ? "Ag" : value,
                textWidth,
                GameFont.Small) + textPadding * 2f;
            float areaHeight = Mathf.Max(minimumAreaHeight, contentHeight);
            float height = labelHeight + fieldGap
                + (helpHeight > 0f ? helpHeight + descriptionGap : 0f)
                + areaHeight;
            if (!draw) return height;

            float y = rect.y;
            DrawFieldLabel(new Rect(rect.x, y, rect.width, labelHeight), label,
                value?.Length ?? 0, maximum, style);
            y += labelHeight + fieldGap;
            if (helpHeight > 0f)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = style.ManualEntryComposerMutedText;
                Widgets.Label(new Rect(rect.x, y, rect.width, helpHeight), help);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += helpHeight + descriptionGap;
            }

            Rect area = new Rect(rect.x, y, rect.width, areaHeight);
            Widgets.DrawBoxSolid(area, new Color(0f, 0f, 0f, 0.25f));
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && enabled;
            value = Widgets.TextArea(area.ContractedBy(2f), value ?? string.Empty) ?? string.Empty;
            GUI.enabled = oldEnabled;
            return height;
        }

        private float FieldLabelHeight(
            float width,
            string label,
            int count,
            int maximum,
            DiaryUiStyleDef style)
        {
            string counter = "PawnDiary.ManualEntry.CharacterCount".Translate(count, maximum);
            Text.Font = GameFont.Tiny;
            float counterWidth = Mathf.Min(width, Mathf.Ceil(Text.CalcSize(counter).x));
            float counterHeight = Mathf.Ceil(Text.CalcHeight(counter, Mathf.Max(1f, counterWidth)));
            Text.Font = GameFont.Small;
            // Measure the same reduced width DrawFieldLabel actually gives the label. Long Russian and
            // high UI scales can otherwise wrap during draw after the measurement pass reserved one line.
            float counterGap = NonNegativeOr(style.manualEntryComposerCharacterGap, 8f);
            float labelWidth = Mathf.Max(1f, width - counterWidth - counterGap);
            float labelHeight = Mathf.Ceil(Text.CalcHeight(label, labelWidth));
            return Mathf.Max(labelHeight, counterHeight);
        }

        private void DrawFieldLabel(
            Rect rect,
            string label,
            int count,
            int maximum,
            DiaryUiStyleDef style)
        {
            Text.Font = GameFont.Small;
            string counter = "PawnDiary.ManualEntry.CharacterCount".Translate(count, maximum);
            Text.Font = GameFont.Tiny;
            float counterWidth = Mathf.Min(rect.width, Mathf.Ceil(Text.CalcSize(counter).x));
            Text.Font = GameFont.Small;
            float counterGap = NonNegativeOr(style.manualEntryComposerCharacterGap, 8f);
            Widgets.Label(
                new Rect(rect.x, rect.y,
                    Mathf.Max(0f, rect.width - counterWidth - counterGap), rect.height),
                label);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(
                new Rect(rect.xMax - counterWidth, rect.y, counterWidth, rect.height),
                counter);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private float MeasureFooterHeight(float width, DiaryUiStyleDef style)
        {
            string left;
            string right;
            FooterLabels(out left, out right);
            float gap = Mathf.Min(
                NonNegativeOr(style.manualEntryEditorButtonGap, 10f),
                Mathf.Max(0f, width));
            float preferred = PositiveOr(style.manualEntryEditorButtonWidth, 140f);
            float buttonWidth = Mathf.Min(preferred, Mathf.Max(1f, (width - gap) * 0.5f));
            return Mathf.Max(
                PositiveOr(style.controlLineHeight, 28f),
                Mathf.Ceil(Mathf.Max(
                    MeasureText(left, buttonWidth, GameFont.Small),
                    MeasureText(right, buttonWidth, GameFont.Small)))
                + NonNegativeOr(style.manualEntryEditorFieldGap, 8f));
        }

        private void DrawFooter(Rect rect, DiaryUiStyleDef style)
        {
            string leftLabel;
            string rightLabel;
            FooterLabels(out leftLabel, out rightLabel);
            float gap = Mathf.Min(
                NonNegativeOr(style.manualEntryEditorButtonGap, 10f),
                Mathf.Max(0f, rect.width));
            float preferred = PositiveOr(style.manualEntryEditorButtonWidth, 140f);
            float buttonWidth = Mathf.Min(preferred, Mathf.Max(0f, (rect.width - gap) * 0.5f));
            float groupWidth = buttonWidth * 2f + gap;
            float left = rect.x + Mathf.Max(0f, (rect.width - groupWidth) * 0.5f);
            Rect leftRect = new Rect(left, rect.y, buttonWidth, rect.height);
            Rect rightRect = new Rect(leftRect.xMax + gap, rect.y, buttonWidth, rect.height);

            bool leftEnabled = !Pending || draftHandle > 0;
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && leftEnabled;
            if (Widgets.ButtonText(leftRect, leftLabel)) HandleFooterLeft();
            GUI.enabled = oldEnabled;

            string actionTip;
            bool rightEnabled = FooterRightEnabled(out actionTip);
            GUI.enabled = oldEnabled && rightEnabled;
            if (Widgets.ButtonText(rightRect, rightLabel)) HandleFooterRight();
            GUI.enabled = oldEnabled;
            if (!string.IsNullOrWhiteSpace(actionTip)) TooltipHandler.TipRegion(rightRect, actionTip);
        }

        private void FooterLabels(out string left, out string right)
        {
            if (Pending)
            {
                left = "PawnDiary.ManualEntry.CancelRequest".Translate().Resolve();
                right = "PawnDiary.ManualEntry.Generating".Translate().Resolve();
            }
            else if (draftStage == ComposerDraftStage.Failed)
            {
                left = "PawnDiary.ManualEntry.BackToPrompt".Translate().Resolve();
                right = "PawnDiary.ManualEntry.Retry".Translate().Resolve();
            }
            else if (Reviewing)
            {
                left = "PawnDiary.ManualEntry.BackToPrompt".Translate().Resolve();
                right = "PawnDiary.ManualEntry.Save".Translate().Resolve();
            }
            else
            {
                left = "PawnDiary.ManualEntry.Cancel".Translate().Resolve();
                right = selectedMode == PlayerEntryComposerMode.Direct
                    ? "PawnDiary.ManualEntry.Save".Translate().Resolve()
                    : "PawnDiary.ManualEntry.GenerateDraft".Translate().Resolve();
            }
        }

        private bool FooterRightEnabled(out string tip)
        {
            if (Pending)
            {
                tip = string.Empty;
                return false;
            }
            if (Reviewing || selectedMode == PlayerEntryComposerMode.Direct)
                return CanSave(out tip);
            return CanGenerate(out tip);
        }

        private void HandleFooterLeft()
        {
            if (Pending) CancelPendingRequest();
            else if (Reviewing || draftStage == ComposerDraftStage.Failed) BackToPrompt();
            else Close();
        }

        private void HandleFooterRight()
        {
            if (Pending) return;
            if (Reviewing || selectedMode == PlayerEntryComposerMode.Direct) Save();
            else StartGeneration();
        }

        private static float MeasureText(string text, float width, GameFont font)
        {
            GameFont old = Text.Font;
            Text.Font = font;
            float height = Mathf.Ceil(Text.CalcHeight(
                string.IsNullOrEmpty(text) ? " " : text,
                Mathf.Max(1f, width)));
            Text.Font = old;
            return height;
        }
    }
}
