// Player-facing Phase M8 Memory settings pages.
//
// Every control reads and changes MemorySettingsDraft, never PawnDiarySettings or a loaded game
// component. RimWorld may therefore run Layout/Repaint any number of times without publishing a
// half-edited policy. PawnDiaryMod.WriteSettings is the one adapter that validates and commits the
// complete draft. The top-level caller keeps this entire file unreachable while LegacyShadow is on.
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const string MemoryNumericControlPrefix = "PawnDiary.Memory.Numeric.";

        private enum MemoryDraftToggle
        {
            SaveNew,
            UseWriting,
            UseBackground,
            AllowExtra,
            Occasional
        }

        /// <summary>Draws the approved normal Memory page in canonical dependency order.</summary>
        private void DrawMemorySettingsTab(Rect inRect)
        {
            MemorySettingsDraft draft = GetMemorySettingsDraft();
            MemorySettingsBounds bounds = MemoryPolicyDefAdapter.Bounds();
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float contentWidth = Mathf.Max(1f, inRect.width - 16f);
            float viewHeight = Mathf.Max(lastMemorySettingsContentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, contentWidth, viewHeight);
            float y = 0f;

            Widgets.BeginScrollView(inRect, ref memorySettingsScrollPosition, viewRect);
            try
            {
                DrawMemorySectionHeader(
                    ref y, contentWidth, "PawnDiary.Memory.Settings.Page".Translate(), style);
                if (draft == null)
                {
                    DrawMemoryText(
                        ref y,
                        contentWidth,
                        "PawnDiary.Memory.Settings.Compatibility".Translate(),
                        GameFont.Small,
                        HintColor,
                        0f,
                        style);
                    return;
                }

                DrawMemoryToggle(
                    ref y, contentWidth, draft, MemoryDraftToggle.SaveNew,
                    "PawnDiary.Memory.Settings.SaveNew",
                    "PawnDiary.Memory.Settings.SaveNew.Desc",
                    true, null, 0f, style);
                DrawMemoryToggle(
                    ref y, contentWidth, draft, MemoryDraftToggle.UseWriting,
                    "PawnDiary.Memory.Settings.UseWriting",
                    "PawnDiary.Memory.Settings.UseWriting.Desc",
                    true, null, 0f, style);
                DrawMemoryToggle(
                    ref y, contentWidth, draft, MemoryDraftToggle.UseBackground,
                    "PawnDiary.Memory.Settings.UseBackground",
                    "PawnDiary.Memory.Settings.UseBackground.Desc",
                    true, null, 0f, style);

                float childIndent = UiMetric(style.settingsMemoryChildIndent, 0f);
                DrawMemoryToggle(
                    ref y, contentWidth, draft, MemoryDraftToggle.AllowExtra,
                    "PawnDiary.Memory.Settings.AllowExtra",
                    "PawnDiary.Memory.Settings.AllowExtra.Desc",
                    draft.UseMemoriesInWriting,
                    draft.UseMemoriesInWriting
                        ? null : "PawnDiary.Memory.Settings.Disabled.UseWriting",
                    childIndent, style);
                DrawMemoryText(
                    ref y,
                    contentWidth,
                    "PawnDiary.Memory.Settings.ExtraCost".Translate(),
                    GameFont.Tiny,
                    AccentColor,
                    childIndent,
                    style);

                bool reflectionParentsEnabled = draft.UseMemoriesInWriting
                    && draft.AllowExtraMemoryAiRequests;
                DrawMemoryToggle(
                    ref y, contentWidth, draft, MemoryDraftToggle.Occasional,
                    "PawnDiary.Memory.Settings.Occasional",
                    "PawnDiary.Memory.Settings.Occasional.Desc",
                    reflectionParentsEnabled,
                    reflectionParentsEnabled
                        ? null
                        : (draft.UseMemoriesInWriting
                            ? "PawnDiary.Memory.Settings.Disabled.AllowExtra"
                            : "PawnDiary.Memory.Settings.Disabled.UseWriting"),
                    childIndent, style);

                DrawMemoryOpenLibrary(ref y, contentWidth, style);
                DrawMemoryText(
                    ref y,
                    contentWidth,
                    "PawnDiary.Memory.Settings.Privacy".Translate(),
                    GameFont.Tiny,
                    HintColor,
                    0f,
                    style);
            }
            finally
            {
                // Numeric controls live only in Advanced. If a top-tab click hid one, complete its
                // detached edit now; this still cannot reach persistence or a game component.
                CompleteOutstandingMemoryNumericEdit(draft, bounds);
                float bottomPadding = UiMetric(style.settingsMemorySectionGap, 0f);
                lastMemorySettingsContentHeight = Mathf.Max(y + bottomPadding, inRect.height);
                memorySettingsScrollPosition.y = Mathf.Clamp(
                    memorySettingsScrollPosition.y,
                    0f,
                    Mathf.Max(0f, lastMemorySettingsContentHeight - inRect.height));
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Hosts the three approved advanced Memory groups beside the unchanged experimental editor.
        /// The existing opt-in applies only to Experimental tuning, never to Memory policy.
        /// </summary>
        private void DrawMemoryAdvancedSettingsTab(Rect inRect)
        {
            MemorySettingsDraft draft = GetMemorySettingsDraft();
            MemorySettingsBounds bounds = MemoryPolicyDefAdapter.Bounds();
            Rect bodyRect = DrawMemoryAdvancedSelector(inRect, draft, bounds);

            if (memoryAdvancedSettingsPage == MemoryAdvancedSettingsPage.ExperimentalTuning)
            {
                CompleteOutstandingMemoryNumericEdit(draft, bounds);
                DrawExperimentalAdvancedSettingsBody(bodyRect);
                return;
            }

            DrawMemoryAdvancedPolicyPage(bodyRect, draft, bounds);
        }

        private Rect DrawMemoryAdvancedSelector(
            Rect inRect,
            MemorySettingsDraft draft,
            MemorySettingsBounds bounds)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float height = UiMetric(style.settingsMemoryAdvancedSelectorHeight);
            float gap = UiMetric(style.settingsMemoryAdvancedSelectorGap, 0f);
            float buttonWidth = Mathf.Max(1f, (inRect.width - gap) * 0.5f);

            DrawMemoryAdvancedSelectorButton(
                new Rect(inRect.x, inRect.y, buttonWidth, height),
                MemoryAdvancedSettingsPage.Categories,
                "PawnDiary.Memory.Settings.Categories.Header",
                draft,
                bounds);
            DrawMemoryAdvancedSelectorButton(
                new Rect(inRect.x + buttonWidth + gap, inRect.y, buttonWidth, height),
                MemoryAdvancedSettingsPage.Retention,
                "PawnDiary.Memory.Settings.Retention.Header",
                draft,
                bounds);
            DrawMemoryAdvancedSelectorButton(
                new Rect(inRect.x, inRect.y + height + gap, buttonWidth, height),
                MemoryAdvancedSettingsPage.Repetition,
                "PawnDiary.Memory.Settings.Repetition.Header",
                draft,
                bounds);
            DrawMemoryAdvancedSelectorButton(
                new Rect(inRect.x + buttonWidth + gap, inRect.y + height + gap, buttonWidth, height),
                MemoryAdvancedSettingsPage.ExperimentalTuning,
                "PawnDiary.Settings.Adv.Category.Tuning",
                draft,
                bounds);

            float selectorHeight = height * 2f + gap;
            return new Rect(
                inRect.x,
                inRect.y + selectorHeight + gap,
                inRect.width,
                Mathf.Max(0f, inRect.height - selectorHeight - gap));
        }

        private void DrawMemoryAdvancedSelectorButton(
            Rect rect,
            MemoryAdvancedSettingsPage page,
            string labelKey,
            MemorySettingsDraft draft,
            MemorySettingsBounds bounds)
        {
            if (memoryAdvancedSettingsPage == page)
                Widgets.DrawHighlightSelected(rect);
            else
                Widgets.DrawHighlightIfMouseover(rect);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = new Rect(
                rect.x + 6f,
                rect.y,
                Mathf.Max(0f, rect.width - 12f),
                rect.height);
            Widgets.LabelFit(labelRect, labelKey.Translate());
            Text.Anchor = previousAnchor;

            if (!Widgets.ButtonInvisible(rect) || memoryAdvancedSettingsPage == page) return;
            CompleteOutstandingMemoryNumericEdit(draft, bounds);
            GUI.FocusControl(string.Empty);
            memoryAdvancedSettingsPage = page;
            memoryAdvancedScrollPosition = Vector2.zero;
        }

        private void DrawMemoryAdvancedPolicyPage(
            Rect inRect,
            MemorySettingsDraft draft,
            MemorySettingsBounds bounds)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float contentWidth = Mathf.Max(1f, inRect.width - 16f);
            float viewHeight = Mathf.Max(lastMemoryAdvancedContentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, contentWidth, viewHeight);
            float y = 0f;

            Widgets.BeginScrollView(inRect, ref memoryAdvancedScrollPosition, viewRect);
            try
            {
                if (draft == null)
                {
                    DrawMemoryText(
                        ref y,
                        contentWidth,
                        "PawnDiary.Memory.Settings.Compatibility".Translate(),
                        GameFont.Small,
                        HintColor,
                        0f,
                        style);
                    return;
                }

                switch (memoryAdvancedSettingsPage)
                {
                    case MemoryAdvancedSettingsPage.Retention:
                        DrawMemoryRetentionPage(ref y, contentWidth, draft, style);
                        FinishMemoryNumericFocus(draft, bounds);
                        break;
                    case MemoryAdvancedSettingsPage.Repetition:
                        DrawMemoryRepetitionPage(ref y, contentWidth, draft, style);
                        FinishMemoryNumericFocus(draft, bounds);
                        break;
                    default:
                        CompleteOutstandingMemoryNumericEdit(draft, bounds);
                        DrawMemoryCategoriesPage(ref y, contentWidth, draft, style);
                        break;
                }
            }
            finally
            {
                float bottomPadding = UiMetric(style.settingsMemorySectionGap, 0f);
                lastMemoryAdvancedContentHeight = Mathf.Max(y + bottomPadding, inRect.height);
                memoryAdvancedScrollPosition.y = Mathf.Clamp(
                    memoryAdvancedScrollPosition.y,
                    0f,
                    Mathf.Max(0f, lastMemoryAdvancedContentHeight - inRect.height));
                Widgets.EndScrollView();
            }
        }

        private void DrawMemoryCategoriesPage(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            DiaryUiStyleDef style)
        {
            DrawMemorySectionHeader(
                ref y, width, "PawnDiary.Memory.Settings.Categories.Header".Translate(), style);
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Category.Desc".Translate(),
                GameFont.Tiny,
                HintColor,
                0f,
                style);
            DrawMemoryCategoryToggle(
                ref y, width, draft, MemoryCategoryBits.Personal,
                "PawnDiary.Memory.Settings.Category.Personal", style);
            DrawMemoryCategoryToggle(
                ref y, width, draft, MemoryCategoryBits.Relationships,
                "PawnDiary.Memory.Settings.Category.Relationships", style);
            DrawMemoryCategoryToggle(
                ref y, width, draft, MemoryCategoryBits.Family,
                "PawnDiary.Memory.Settings.Category.Family", style);
            DrawMemoryCategoryToggle(
                ref y, width, draft, MemoryCategoryBits.Factions,
                "PawnDiary.Memory.Settings.Category.Factions", style);
        }

        private void DrawMemoryRetentionPage(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            DiaryUiStyleDef style)
        {
            DrawMemorySectionHeader(
                ref y, width, "PawnDiary.Memory.Settings.Retention.Header".Translate(), style);
            DrawMemoryNumericField(
                ref y, width, draft,
                MemoryNumericSettingKeys.MinorLifetimeDays,
                "PawnDiary.Memory.Settings.Retention.Minor",
                "PawnDiary.Memory.Settings.Retention.DaySuffix",
                style);
            DrawMemoryNumericField(
                ref y, width, draft,
                MemoryNumericSettingKeys.RegularLifetimeDays,
                "PawnDiary.Memory.Settings.Retention.Regular",
                "PawnDiary.Memory.Settings.Retention.DaySuffix",
                style);
            if (draft.invalidLifetimeOrderWarning)
            {
                Color warning = style.settingsMemoryWarningText == null
                    ? new Color(1f, 0.76f, 0.24f, 1f)
                    : style.settingsMemoryWarningText.ToColor(
                        new Color(1f, 0.76f, 0.24f, 1f));
                DrawMemoryText(
                    ref y,
                    width,
                    "PawnDiary.Memory.Settings.Retention.InvalidOrder".Translate(),
                    GameFont.Tiny,
                    warning,
                    0f,
                    style);
            }
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Retention.AgeHelp".Translate(),
                GameFont.Tiny,
                HintColor,
                0f,
                style);
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Retention.Important".Translate(),
                GameFont.Tiny,
                AccentColor,
                0f,
                style);
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Retention.Edited".Translate(),
                GameFont.Tiny,
                AccentColor,
                0f,
                style);

            DrawMemoryNumericField(
                ref y, width, draft,
                MemoryNumericSettingKeys.ThreadTarget,
                "PawnDiary.Memory.Settings.Retention.ThreadTarget",
                null,
                style);
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Retention.ThreadHelp".Translate(),
                GameFont.Tiny,
                HintColor,
                0f,
                style);
        }

        private void DrawMemoryRepetitionPage(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            DiaryUiStyleDef style)
        {
            DrawMemorySectionHeader(
                ref y, width, "PawnDiary.Memory.Settings.Repetition.Header".Translate(), style);
            DrawMemoryNumericField(
                ref y, width, draft,
                MemoryNumericSettingKeys.ReuseDays,
                "PawnDiary.Memory.Settings.Repetition.Days",
                "PawnDiary.Memory.Settings.Retention.DaySuffix",
                style);
            DrawMemoryNumericField(
                ref y, width, draft,
                MemoryNumericSettingKeys.RevisitEntries,
                "PawnDiary.Memory.Settings.Repetition.Entries",
                null,
                style);
            DrawMemoryText(
                ref y,
                width,
                "PawnDiary.Memory.Settings.Repetition.Help".Translate(),
                GameFont.Tiny,
                HintColor,
                0f,
                style);
        }

        private void DrawMemoryToggle(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            MemoryDraftToggle toggle,
            string labelKey,
            string descriptionKey,
            bool enabled,
            string disabledExplanationKey,
            float indent,
            DiaryUiStyleDef style)
        {
            bool currentValue = MemoryToggleValue(draft, toggle);
            bool priorEnabled = GUI.enabled;
            bool nextValue = DrawMemoryCheckboxRow(
                ref y,
                width,
                labelKey.Translate(),
                currentValue,
                enabled,
                indent,
                style);
            if (priorEnabled && enabled && nextValue != currentValue)
                SetMemoryToggleValue(draft, toggle, nextValue);

            DrawMemoryText(
                ref y,
                width,
                descriptionKey.Translate(),
                GameFont.Tiny,
                HintColor,
                indent,
                style);
            if (!enabled && !string.IsNullOrEmpty(disabledExplanationKey))
            {
                DrawMemoryText(
                    ref y,
                    width,
                    disabledExplanationKey.Translate(),
                    GameFont.Tiny,
                    AccentColor,
                    indent,
                    style);
            }
        }

        private static bool MemoryToggleValue(MemorySettingsDraft draft, MemoryDraftToggle toggle)
        {
            switch (toggle)
            {
                case MemoryDraftToggle.SaveNew: return draft.SaveNewMemories;
                case MemoryDraftToggle.UseWriting: return draft.UseMemoriesInWriting;
                case MemoryDraftToggle.UseBackground: return draft.UsePawnBackground;
                case MemoryDraftToggle.AllowExtra: return draft.AllowExtraMemoryAiRequests;
                case MemoryDraftToggle.Occasional: return draft.OccasionalMemoryReflections;
                default: throw new ArgumentOutOfRangeException(nameof(toggle));
            }
        }

        private static void SetMemoryToggleValue(
            MemorySettingsDraft draft,
            MemoryDraftToggle toggle,
            bool value)
        {
            switch (toggle)
            {
                case MemoryDraftToggle.SaveNew:
                    draft.SetSaveNewMemories(value);
                    return;
                case MemoryDraftToggle.UseWriting:
                    draft.SetUseMemoriesInWriting(value);
                    return;
                case MemoryDraftToggle.UseBackground:
                    draft.SetUsePawnBackground(value);
                    return;
                case MemoryDraftToggle.AllowExtra:
                    draft.SetAllowExtraMemoryAiRequests(value);
                    return;
                case MemoryDraftToggle.Occasional:
                    draft.SetOccasionalMemoryReflections(value);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(toggle));
            }
        }

        private void DrawMemoryCategoryToggle(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            int categoryBit,
            string labelKey,
            DiaryUiStyleDef style)
        {
            bool current = draft.CategoryEnabled(categoryBit);
            bool next = DrawMemoryCheckboxRow(
                ref y,
                width,
                labelKey.Translate(),
                current,
                true,
                0f,
                style);
            if (next != current)
                draft.SetCategoryEnabled(categoryBit, next);
            y += UiMetric(style.settingsMemoryBlockGap, 0f);
        }

        private static bool DrawMemoryCheckboxRow(
            ref float y,
            float width,
            string label,
            bool value,
            bool enabled,
            float indent,
            DiaryUiStyleDef style)
        {
            float availableWidth = Mathf.Max(1f, width - indent);
            float controlHeight = UiMetric(style.settingsMemoryControlHeight);
            float checkboxSize = Mathf.Min(
                controlHeight,
                UiMetric(style.settingsMemoryCheckboxSize));
            float gap = UiMetric(style.settingsMemoryBlockGap, 0f);
            float labelX = indent + checkboxSize + gap;
            float labelWidth = Mathf.Max(1f, availableWidth - checkboxSize - gap);
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            float labelHeight = Mathf.Max(
                Text.LineHeight,
                Mathf.Ceil(Text.CalcHeight(label ?? string.Empty, labelWidth)));
            float rowHeight = Mathf.Max(controlHeight, labelHeight);

            bool priorEnabled = GUI.enabled;
            GUI.enabled = priorEnabled && enabled;
            bool edited = value;
            Widgets.Checkbox(
                indent,
                y + (rowHeight - checkboxSize) * 0.5f,
                ref edited,
                checkboxSize);
            Rect labelRect = new Rect(labelX, y, labelWidth, rowHeight);
            Widgets.Label(labelRect, label ?? string.Empty);
            if (Widgets.ButtonInvisible(labelRect))
                edited = !edited;
            GUI.enabled = priorEnabled;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            y += rowHeight;
            return edited;
        }

        private void DrawMemoryNumericField(
            ref float y,
            float width,
            MemorySettingsDraft draft,
            string numericKey,
            string labelKey,
            string suffixKey,
            DiaryUiStyleDef style)
        {
            string label = labelKey.Translate();
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float labelHeight = Mathf.Max(
                Text.LineHeight,
                Mathf.Ceil(Text.CalcHeight(label, Mathf.Max(1f, width))));
            Widgets.Label(new Rect(0f, y, width, labelHeight), label);
            Text.Font = previousFont;
            y += labelHeight + UiMetric(style.settingsMemoryBlockGap, 0f);

            float controlHeight = UiMetric(style.settingsMemoryControlHeight);
            float fieldWidth = Mathf.Min(
                width,
                UiMetric(style.settingsMemoryNumericFieldWidth));
            string controlName = MemoryNumericControlPrefix + numericKey;
            GUI.SetNextControlName(controlName);
            string edited = Widgets.TextField(
                new Rect(0f, y, fieldWidth, controlHeight),
                draft.NumericBuffer(numericKey));
            draft.UpdateNumericBuffer(numericKey, edited);

            if (!string.IsNullOrEmpty(suffixKey))
            {
                float suffixX = fieldWidth + UiMetric(style.settingsMemoryBlockGap, 0f);
                Widgets.LabelFit(
                    new Rect(
                        suffixX,
                        y,
                        Mathf.Max(0f, width - suffixX),
                        controlHeight),
                    suffixKey.Translate());
            }
            y += controlHeight + UiMetric(style.settingsMemorySectionGap, 0f);
        }

        private void DrawMemoryOpenLibrary(
            ref float y,
            float width,
            DiaryUiStyleDef style)
        {
            float gap = UiMetric(style.settingsMemorySectionGap, 0f);
            float controlHeight = UiMetric(style.settingsMemoryControlHeight);
            y += gap;
            bool hasGame = Verse.Current.ProgramState == ProgramState.Playing
                && Verse.Current.Game != null;
            bool priorEnabled = GUI.enabled;
            GUI.enabled = priorEnabled && hasGame && OpenMemoryLibraryAction != null;
            bool clicked = ButtonTextFit(
                new Rect(
                    0f,
                    y,
                    Mathf.Min(width, UiMetric(style.settingsMemoryOpenLibraryButtonWidth)),
                    controlHeight),
                "PawnDiary.Memory.Settings.OpenLibrary".Translate());
            GUI.enabled = priorEnabled;
            if (clicked && priorEnabled && hasGame)
                OpenMemoryLibraryAction?.Invoke();
            y += controlHeight;

            if (!hasGame)
            {
                DrawMemoryText(
                    ref y,
                    width,
                    "PawnDiary.Memory.Settings.NoGame".Translate(),
                    GameFont.Tiny,
                    HintColor,
                    0f,
                    style);
            }
            else
            {
                y += UiMetric(style.settingsMemoryBlockGap, 0f);
            }
        }

        private static void DrawMemorySectionHeader(
            ref float y,
            float width,
            string label,
            DiaryUiStyleDef style)
        {
            float sectionGap = UiMetric(style.settingsMemorySectionGap, 0f);
            y += sectionGap;
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            float height = Mathf.Max(
                Text.LineHeight,
                Mathf.Ceil(Text.CalcHeight(label ?? string.Empty, Mathf.Max(1f, width))));
            Widgets.Label(new Rect(0f, y, width, height), label ?? string.Empty);
            Text.Font = previousFont;
            y += height + sectionGap;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += UiMetric(style.settingsMemoryBlockGap, 0f);
        }

        private static void DrawMemoryText(
            ref float y,
            float width,
            string text,
            GameFont font,
            Color color,
            float indent,
            DiaryUiStyleDef style)
        {
            float availableWidth = Mathf.Max(1f, width - indent);
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = color;
            float height = Mathf.Max(
                Text.LineHeight,
                Mathf.Ceil(Text.CalcHeight(text ?? string.Empty, availableWidth)));
            Widgets.Label(new Rect(indent, y, availableWidth, height), text ?? string.Empty);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            y += height + UiMetric(style.settingsMemoryBlockGap, 0f);
        }

        private void FinishMemoryNumericFocus(
            MemorySettingsDraft draft,
            MemorySettingsBounds bounds)
        {
            if (draft == null) return;
            string currentControl = GUI.GetNameOfFocusedControl() ?? string.Empty;
            bool currentIsMemory = currentControl.StartsWith(
                MemoryNumericControlPrefix, StringComparison.Ordinal);

            Event currentEvent = Event.current;
            bool pressedEnter = currentIsMemory
                && currentEvent != null
                && currentEvent.type == EventType.KeyDown
                && (currentEvent.keyCode == KeyCode.Return
                    || currentEvent.keyCode == KeyCode.KeypadEnter);
            if (pressedEnter)
            {
                draft.CompleteNumericEdit(
                    currentControl.Substring(MemoryNumericControlPrefix.Length),
                    bounds);
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
                currentControl = string.Empty;
                currentIsMemory = false;
            }

            if (!string.IsNullOrEmpty(focusedMemoryNumericControl)
                && !string.Equals(
                    focusedMemoryNumericControl,
                    currentControl,
                    StringComparison.Ordinal))
            {
                CompleteOutstandingMemoryNumericEdit(draft, bounds);
            }

            focusedMemoryNumericControl = currentIsMemory ? currentControl : string.Empty;
        }

        private void CompleteOutstandingMemoryNumericEdit(
            MemorySettingsDraft draft,
            MemorySettingsBounds bounds)
        {
            if (draft == null || string.IsNullOrEmpty(focusedMemoryNumericControl))
            {
                focusedMemoryNumericControl = string.Empty;
                return;
            }
            if (focusedMemoryNumericControl.StartsWith(
                MemoryNumericControlPrefix, StringComparison.Ordinal))
            {
                draft.CompleteNumericEdit(
                    focusedMemoryNumericControl.Substring(MemoryNumericControlPrefix.Length),
                    bounds);
            }
            focusedMemoryNumericControl = string.Empty;
        }
    }
}
