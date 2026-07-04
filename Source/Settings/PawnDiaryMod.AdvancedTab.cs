// Advanced settings tab for Pawn Diary: a two-pane editor for XML Def tuning, prompt policy, and
// weight fields. Left rail lists feature groups (dedup windows, weather, health, reflections, signal
// policies, context reactions, prompt templates, prompt enchantments, event groups...); the right body
// draws one widget per field type (checkbox / slider / numeric text / text / multi-line list or table
// area), a per-field reset, and rich tooltips
// that combine authored help with the live value / XML default / range. Overridden fields are drawn
// in accent color. A name filter collapses the rail into a flat search view.
//
// All edits persist through TuningOverrideStore and take effect immediately: the descriptor writes
// the new value straight into the live Def instance field (see AdvancedFieldCatalog), so every
// existing reader picks it up without re-routing through a helper.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float AdvancedRailWidth = 204f;
        private const float AdvancedResetButtonWidth = 58f;
        private const string AdvancedNumberChars = "-0123456789.";
        private const int AdvancedSyntaxPreviewMaxRows = 2;
        private static readonly Color AdvancedSyntaxCategoryColor = new Color(0.56f, 0.86f, 0.62f);
        private static readonly Color AdvancedSyntaxDefColor = new Color(0.68f, 0.80f, 1f);
        private static readonly Color AdvancedSyntaxStageColor = new Color(1f, 0.82f, 0.50f);
        private static readonly Color AdvancedSyntaxErrorColor = new Color(1f, 0.42f, 0.38f);
        private static readonly Color AdvancedSyntaxPanelColor = new Color(0f, 0f, 0f, 0.18f);

        /// <summary>Draws the Advanced tab: summary bar, name filter, group rail, and the field body.</summary>
        private void DrawAdvancedTab(Rect inRect, AdvancedFieldCategory category)
        {
            // Make sure saved overrides are applied even when the tab is opened straight from the
            // main menu (settings load is normally enough; this is the defensive idempotent backstop).
            AdvancedFieldCatalog.EnsureApplied(Settings.advancedOverrides);

            float y = inRect.y;

            Rect summaryRect = new Rect(inRect.x, y, inRect.width, 22f);
            int overrideCount = AdvancedOverrideCount(category);
            DrawMutedLabel(summaryRect, "PawnDiary.Settings.Adv.Summary".Translate(overrideCount));
            y += summaryRect.height + 4f;

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Rect warningRect = new Rect(inRect.x, y, inRect.width, Mathf.Ceil(Text.LineHeight) + 2f);
            DrawMutedLabel(warningRect, "PawnDiary.Settings.Adv.ExperimentalWarning".Translate().ToString());
            Text.Font = previousFont;
            y += warningRect.height + 4f;

            Rect filterRect = new Rect(inRect.x, y, Mathf.Min(Mathf.Max(120f, inRect.width - 424f), 340f), 28f);
            advancedFilter = DrawCompactTextField(
                filterRect,
                "PawnDiary.Settings.Adv.Filter".Translate(),
                advancedFilter,
                70f);

            DrawAdvancedCategoryActions(inRect, y, category, overrideCount, filterRect.xMax);

            y += 34f;

            DrawAdvancedFilterModeButtons(new Rect(inRect.x, y, Mathf.Min(inRect.width, 360f), 28f));
            y += 34f;

            Rect paneRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y));
            DrawAdvancedPanes(paneRect, category);
        }

        /// <summary>Left rail of groups + right body of fields, each in its own scroll view.</summary>
        private void DrawAdvancedPanes(Rect rect, AdvancedFieldCategory category)
        {
            Rect railOuter = new Rect(rect.x, rect.y, AdvancedRailWidth, rect.height);
            Rect bodyOuter = new Rect(railOuter.xMax + 6f, rect.y, rect.width - AdvancedRailWidth - 6f, rect.height);
            Widgets.DrawMenuSection(railOuter);
            Widgets.DrawMenuSection(bodyOuter);

            DrawAdvancedRail(railOuter.ContractedBy(6f), category);
            DrawAdvancedBody(bodyOuter.ContractedBy(8f), category);
        }

        private void DrawAdvancedCategoryActions(Rect inRect, float y, AdvancedFieldCategory category, int overrideCount, float filterRight)
        {
            const float gap = 6f;
            const float resetWidth = 220f;
            const float copyWidth = 188f;
            Rect copyRect = new Rect(inRect.xMax - copyWidth, y, copyWidth, 28f);
            Rect resetRect = new Rect(copyRect.x - gap - resetWidth, y, resetWidth, 28f);
            if (resetRect.x < filterRight + gap)
            {
                resetRect.x = filterRight + gap;
                resetRect.width = Mathf.Max(120f, copyRect.x - resetRect.x - gap);
            }

            if (ButtonTextFit(resetRect, "PawnDiary.Settings.Adv.ResetChangedInTab".Translate()) && overrideCount > 0)
            {
                ResetAdvancedCategory(category);
            }
            TooltipHandler.TipRegion(resetRect, "PawnDiary.Settings.Adv.ResetChangedInTabTip".Translate());

            if (ButtonTextFit(copyRect, "PawnDiary.Settings.Adv.CopyChangedSummary".Translate()))
            {
                CopyAdvancedChangedSummary(category);
            }
            TooltipHandler.TipRegion(copyRect, "PawnDiary.Settings.Adv.CopyChangedSummaryTip".Translate());
        }

        private void ResetAdvancedCategory(AdvancedFieldCategory category)
        {
            List<AdvancedFieldDescriptor> changed = AdvancedChangedFields(category);
            for (int i = 0; i < changed.Count; i++)
            {
                AdvancedFieldDescriptor descriptor = changed[i];
                AdvancedFieldCatalog.ResetField(Settings.advancedOverrides, descriptor);
                ClearAdvancedFieldUiState(descriptor);
            }
        }

        private void CopyAdvancedChangedSummary(AdvancedFieldCategory category)
        {
            List<AdvancedFieldDescriptor> changed = AdvancedChangedFields(category);
            GUIUtility.systemCopyBuffer = BuildAdvancedChangedSummary(category, changed);
            Messages.Message(
                "PawnDiary.Settings.Adv.CopyChangedSummaryDone".Translate(changed.Count),
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private string BuildAdvancedChangedSummary(AdvancedFieldCategory category, List<AdvancedFieldDescriptor> changed)
        {
            string categoryLabel = AdvancedCategoryDisplayName(category);
            if (changed == null || changed.Count == 0)
            {
                return "PawnDiary.Settings.Adv.ChangedSummaryNone".Translate(categoryLabel).ToString();
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PawnDiary.Settings.Adv.ChangedSummaryTitle".Translate(categoryLabel).ToString());
            sb.AppendLine("PawnDiary.Settings.Adv.ChangedSummaryCount".Translate(changed.Count).ToString());
            for (int i = 0; i < changed.Count; i++)
            {
                AdvancedFieldDescriptor descriptor = changed[i];
                sb.AppendLine("PawnDiary.Settings.Adv.ChangedSummaryLine".Translate(
                    descriptor.key,
                    descriptor.DisplayLabel(),
                    AdvancedSummaryValue(descriptor.ReadDefValueString()),
                    AdvancedSummaryValue(descriptor.defaultSnapshot)).ToString());
            }

            return sb.ToString().TrimEnd();
        }

        private string AdvancedSummaryValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "PawnDiary.Settings.Adv.Source.Empty".Translate().ToString()
                : AdvancedPreviewSnippet(value, 180);
        }

        private string AdvancedCategoryDisplayName(AdvancedFieldCategory category)
        {
            return (category == AdvancedFieldCategory.Prompts
                ? "PawnDiary.Settings.Adv.Category.Prompts"
                : "PawnDiary.Settings.Adv.Category.Tuning").Translate().ToString();
        }

        private int AdvancedOverrideCount(AdvancedFieldCategory category)
        {
            return AdvancedChangedFields(category).Count;
        }

        private List<AdvancedFieldDescriptor> AdvancedChangedFields(AdvancedFieldCategory category)
        {
            List<AdvancedFieldDescriptor> result = new List<AdvancedFieldDescriptor>();
            List<AdvancedFieldGroup> groups = GroupsForCategory(category);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<AdvancedFieldDescriptor> fields = groups[groupIndex].fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    AdvancedFieldDescriptor descriptor = fields[i];
                    if (Settings.advancedOverrides.HasOverride(descriptor.key))
                    {
                        result.Add(descriptor);
                    }
                }
            }

            return result;
        }

        private void ClearAdvancedFieldUiState(AdvancedFieldDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            advancedTextBuffers.Remove(descriptor.key);
            advancedTextSynced.Remove(descriptor.key);
            advancedExpandedOverrideFields.Remove(descriptor.key);
        }

        private void DrawAdvancedFilterModeButtons(Rect rect)
        {
            const float gap = 4f;
            float width = Mathf.Max(80f, (rect.width - gap * 2f) / 3f);
            DrawAdvancedFilterModeButton(new Rect(rect.x, rect.y, width, rect.height), AdvancedFieldFilterMode.All, "PawnDiary.Settings.Adv.FilterMode.All");
            DrawAdvancedFilterModeButton(new Rect(rect.x + width + gap, rect.y, width, rect.height), AdvancedFieldFilterMode.Changed, "PawnDiary.Settings.Adv.FilterMode.Changed");
            DrawAdvancedFilterModeButton(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), AdvancedFieldFilterMode.Raw, "PawnDiary.Settings.Adv.FilterMode.Raw");
        }

        private void DrawAdvancedFilterModeButton(Rect rect, AdvancedFieldFilterMode mode, string labelKey)
        {
            if (advancedFieldFilterMode == mode)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.LabelFit(rect, labelKey.Translate().ToString());
            Text.Anchor = previousAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                advancedFieldFilterMode = mode;
                advancedBodyScroll.y = 0f;
            }
        }

        private void DrawAdvancedRail(Rect rect, AdvancedFieldCategory category)
        {
            List<AdvancedFieldGroup> groups = GroupsForCategory(category);
            if (groups.Count == 0)
            {
                return;
            }

            SelectedAdvancedGroup(category);

            float rowH = AdvancedFieldDescriptor.AdvancedBoolRowHeight;
            float contentHeight = groups.Count * (rowH + AdvancedFieldDescriptor.AdvancedRowGap);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref advancedRailScroll, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < groups.Count; i++)
                {
                    AdvancedFieldGroup group = groups[i];
                    Rect rowRect = new Rect(0f, y, viewRect.width, rowH);
                    DrawAdvancedRailRow(rowRect, group);
                    y += rowH + AdvancedFieldDescriptor.AdvancedRowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawAdvancedRailRow(Rect rect, AdvancedFieldGroup group)
        {
            bool selected = !AdvancedFilterActive() && string.Equals(selectedAdvancedGroupKey, group.groupKey, StringComparison.Ordinal);
            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.LabelFit(rect, group.DisplayTitle());
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, AdvancedGroupTooltip(group));

            if (Widgets.ButtonInvisible(rect))
            {
                selectedAdvancedGroupKey = group.groupKey;
                advancedFilter = string.Empty;
            }
        }

        private void DrawAdvancedBody(Rect rect, AdvancedFieldCategory category)
        {
            IReadOnlyList<AdvancedFieldDescriptor> fields = FieldsForBody(category);
            AdvancedFieldGroup group = SelectedAdvancedGroup(category);

            float contentHeight = AdvancedBodyHeight(fields, category);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref advancedBodyScroll, viewRect);
            try
            {
                float y = DrawAdvancedBodyHeader(viewRect, group, fields);
                for (int i = 0; i < fields.Count; i++)
                {
                    AdvancedFieldDescriptor descriptor = fields[i];
                    float rowHeight = AdvancedFieldRowHeight(descriptor);
                    Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight);
                    DrawAdvancedFieldRow(rowRect, descriptor);
                    y += rowHeight + AdvancedFieldDescriptor.AdvancedRowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private float DrawAdvancedBodyHeader(Rect viewRect, AdvancedFieldGroup group, IReadOnlyList<AdvancedFieldDescriptor> fields)
        {
            string title = AdvancedFilterActive()
                ? "PawnDiary.Settings.Adv.FilterResults".Translate(fields.Count).ToString()
                : (group != null ? group.DisplayTitle() : string.Empty);

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            // Measure the Medium-font line height instead of hardcoding 24px so the title does not
            // clip when the accessibility "Use tiny text" setting or UI scale changes line height.
            float titleHeight = Mathf.Ceil(Text.LineHeight) + 2f;
            Rect titleRect = new Rect(0f, 0f, viewRect.width, titleHeight);
            Widgets.LabelFit(titleRect, title);
            Text.Font = previousFont;
            float y = titleRect.height + 2f;

            if (!AdvancedFilterActive() && group != null && GroupHasOverride(group))
            {
                Rect resetRect = new Rect(0f, y, 150f, 24f);
                if (ButtonTextFit(resetRect, "PawnDiary.Settings.Adv.ResetGroup".Translate()))
                {
                    AdvancedFieldCatalog.ResetGroup(Settings.advancedOverrides, group);
                    foreach (AdvancedFieldDescriptor d in group.fields)
                    {
                        ClearAdvancedFieldUiState(d);
                    }
                }

                y += resetRect.height + AdvancedFieldDescriptor.AdvancedRowGap;
            }

            return y + 4f;
        }

        private void DrawAdvancedFieldRow(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            float resetX = rect.xMax - AdvancedResetButtonWidth;
            Rect resetRect;

            if (descriptor.fieldType == AdvancedFieldType.Bool)
            {
                // Inline: checkbox + full-width label on one line; reset at the right edge.
                DrawAdvancedBoolInline(new Rect(rect.x, rect.y, rect.width - AdvancedResetButtonWidth, AdvancedFieldDescriptor.AdvancedBoolRowHeight), descriptor);
                resetRect = new Rect(resetX, rect.y + 1f, AdvancedResetButtonWidth - 2f, AdvancedFieldDescriptor.AdvancedBoolRowHeight - 2f);
            }
            else
            {
                // Label gets the FULL row width so long names never clip and never need font shrinking.
                bool overridden = Settings.advancedOverrides.HasOverride(descriptor.key);
                Rect labelRect = new Rect(rect.x, rect.y, rect.width, AdvancedFieldDescriptor.AdvancedLabelLineHeight);
                DrawAdvancedFieldLabel(labelRect, descriptor.DisplayLabel(), overridden);

                float controlTop = rect.y + AdvancedFieldDescriptor.AdvancedLabelLineHeight + AdvancedFieldDescriptor.AdvancedRowGap;
                float controlWidth = resetX - rect.x - AdvancedFieldDescriptor.AdvancedRowGap;
                if (descriptor.isLongText
                    || descriptor.fieldType == AdvancedFieldType.StringList
                    || descriptor.fieldType == AdvancedFieldType.IntList)
                {
                    DrawAdvancedLongTextControl(
                        new Rect(rect.x, controlTop, controlWidth, AdvancedLongTextControlHeight(descriptor)),
                        descriptor);
                }
                else if (descriptor.fieldType == AdvancedFieldType.Float && descriptor.useSlider)
                {
                    DrawAdvancedSliderControl(new Rect(rect.x, controlTop, controlWidth, AdvancedFieldDescriptor.AdvancedControlLineHeight), descriptor);
                }
                else
                {
                    DrawAdvancedNumericTextControl(new Rect(rect.x, controlTop, controlWidth, AdvancedFieldDescriptor.AdvancedControlLineHeight), descriptor);
                }

                resetRect = new Rect(resetX, controlTop + 2f, AdvancedResetButtonWidth - 2f, AdvancedFieldDescriptor.AdvancedControlLineHeight - 4f);
            }

            // Recompute after the control may have committed, so the reset glyph reflects this frame's edit.
            bool nowOverridden = Settings.advancedOverrides.HasOverride(descriptor.key);
            DrawAdvancedResetButton(resetRect, descriptor, nowOverridden);

            string tooltip = BuildAdvancedTooltip(descriptor);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private void DrawAdvancedBoolInline(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            bool current = descriptor.ReadDefValue() is bool value && value;
            bool toggled = current;
            Widgets.Checkbox(rect.x, rect.y + (rect.height - 24f) * 0.5f, ref toggled);
            if (toggled != current)
            {
                CommitAdvancedValue(descriptor, toggled);
            }

            bool overridden = Settings.advancedOverrides.HasOverride(descriptor.key);
            Rect labelRect = new Rect(rect.x + 30f, rect.y, rect.width - 30f, rect.height);
            DrawAdvancedFieldLabel(labelRect, descriptor.DisplayLabel(), overridden);
        }

        private void DrawAdvancedSliderControl(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            float current = descriptor.ReadDefValue() is float f ? f : descriptor.min;
            Rect valueRect = new Rect(rect.x, rect.y, 58f, rect.height);
            Widgets.LabelFit(valueRect, current.ToString("0.###", CultureInfo.InvariantCulture));

            Rect sliderRect = new Rect(valueRect.xMax + 6f, rect.y + 3f, Mathf.Max(0f, rect.width - valueRect.width - 6f), rect.height - 6f);
            float edited = Widgets.HorizontalSlider(sliderRect, current, descriptor.min, descriptor.max);
            if (Mathf.Abs(edited - current) > 0.0000001f)
            {
                CommitAdvancedValue(descriptor, edited);
            }
        }

        private void DrawAdvancedNumericTextControl(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            string currentInvariant = descriptor.ReadDisplayValueString();
            string buffer;
            string synced;
            advancedTextBuffers.TryGetValue(descriptor.key, out buffer);
            advancedTextSynced.TryGetValue(descriptor.key, out synced);
            // Resync when unset, when the Def changed from outside (Reset/group reset), or when the
            // user cleared the box: an empty field must not sit beside a non-empty Def value.
            bool stale = buffer == null
                || synced != currentInvariant
                || (buffer.Length == 0 && currentInvariant.Length > 0);
            if (stale)
            {
                buffer = currentInvariant;
                advancedTextBuffers[descriptor.key] = buffer;
                advancedTextSynced[descriptor.key] = currentInvariant;
            }

            string edited = Widgets.TextField(rect, buffer ?? string.Empty);
            advancedTextBuffers[descriptor.key] = edited;

            string filtered = FilterAdvancedInput(edited, descriptor.fieldType);
            object parsed;
            if (descriptor.TryParse(filtered, out parsed))
            {
                string parsedInvariant = descriptor.Format(parsed);
                if (parsedInvariant != currentInvariant)
                {
                    CommitAdvancedValue(descriptor, parsed);
                    advancedTextBuffers[descriptor.key] = parsedInvariant;
                    advancedTextSynced[descriptor.key] = parsedInvariant;
                }
            }
        }

        private void DrawAdvancedLongTextControl(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            // Most prompt-policy text rows are raw override fields: blank means the XML/Keyed default
            // still applies at generation time, and we keep that fallback out of the editable box.
            if (AdvancedLongTextCollapsed(descriptor))
            {
                DrawAdvancedCollapsedOverrideControl(rect, descriptor);
                return;
            }

            Rect textRect = new Rect(rect.x, rect.y, rect.width, AdvancedFieldDescriptor.AdvancedLongTextHeight);
            string currentInvariant = descriptor.ReadDisplayValueString();
            string buffer;
            string synced;
            advancedTextBuffers.TryGetValue(descriptor.key, out buffer);
            advancedTextSynced.TryGetValue(descriptor.key, out synced);
            bool stale = buffer == null || synced != currentInvariant;
            if (stale)
            {
                buffer = currentInvariant;
                advancedTextBuffers[descriptor.key] = buffer;
                advancedTextSynced[descriptor.key] = currentInvariant;
            }

            string edited = Widgets.TextArea(textRect, buffer ?? string.Empty);
            advancedTextBuffers[descriptor.key] = edited;

            AdvancedRawSyntaxCheck syntaxCheck = AdvancedRawSyntax.Check(descriptor.fieldName, edited);
            bool syntaxAllowsCommit = syntaxCheck == null || syntaxCheck.valid;
            if (!string.Equals(edited, currentInvariant, StringComparison.Ordinal))
            {
                object parsed;
                if (syntaxAllowsCommit && descriptor.TryParse(edited ?? string.Empty, out parsed))
                {
                    CommitAdvancedValue(descriptor, parsed);
                    string display = descriptor.ReadDisplayValueString();
                    advancedTextBuffers[descriptor.key] = display;
                    advancedTextSynced[descriptor.key] = display;
                }
            }

            if (syntaxCheck != null)
            {
                Rect previewRect = new Rect(
                    rect.x,
                    textRect.yMax + AdvancedFieldDescriptor.AdvancedRowGap,
                    rect.width,
                    AdvancedFieldDescriptor.AdvancedSyntaxPreviewHeight);
                DrawAdvancedSyntaxPreview(previewRect, syntaxCheck);
            }
        }

        private void DrawAdvancedCollapsedOverrideControl(Rect rect, AdvancedFieldDescriptor descriptor)
        {
            Rect statusRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 118f), rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            DrawAdvancedSyntaxLabel(
                statusRect,
                AdvancedEffectivePreviewText(descriptor),
                HintColor);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect(rect.xMax - 112f, rect.y + 2f, 112f, Mathf.Max(0f, rect.height - 4f));
            if (ButtonTextFit(buttonRect, "PawnDiary.Settings.Adv.EditOverride".Translate()))
            {
                advancedExpandedOverrideFields.Add(descriptor.key);
            }

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.Adv.EditOverrideTip".Translate());
        }

        private string AdvancedEffectivePreviewText(AdvancedFieldDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            string sourceKey;
            string value;
            if (Settings.advancedOverrides.HasOverride(descriptor.key))
            {
                sourceKey = "PawnDiary.Settings.Adv.Source.Override";
                value = descriptor.ReadDefValueString();
            }
            else if (descriptor.effectivePreviewReader != null)
            {
                sourceKey = string.IsNullOrEmpty(descriptor.effectivePreviewSourceKey)
                    ? "PawnDiary.Settings.Adv.Source.XmlDefault"
                    : descriptor.effectivePreviewSourceKey;
                value = descriptor.effectivePreviewReader();
            }
            else
            {
                sourceKey = "PawnDiary.Settings.Adv.Source.XmlDefault";
                value = descriptor.defaultSnapshot;
            }

            string preview = string.IsNullOrWhiteSpace(value)
                ? "PawnDiary.Settings.Adv.Source.Empty".Translate().ToString()
                : AdvancedPreviewSnippet(value, 90);

            return "PawnDiary.Settings.Adv.EffectivePreview".Translate(sourceKey.Translate().ToString(), preview).ToString();
        }

        private static string AdvancedPreviewSnippet(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(text.Length);
            bool previousWhitespace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                    {
                        sb.Append(' ');
                        previousWhitespace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    previousWhitespace = false;
                }
            }

            string compact = sb.ToString().Trim();
            if (compact.Length <= maxChars)
            {
                return compact;
            }

            int prefixLength = Math.Max(0, maxChars - 3);
            return compact.Substring(0, prefixLength).TrimEnd() + "...";
        }

        private static void DrawAdvancedSyntaxPreview(Rect rect, AdvancedRawSyntaxCheck check)
        {
            if (check == null)
            {
                return;
            }

            Widgets.DrawBoxSolid(rect, AdvancedSyntaxPanelColor);
            Rect inner = rect.ContractedBy(5f);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            try
            {
                float rowHeight = Mathf.Ceil(Text.LineHeight) + 1f;
                float y = inner.y;
                string status = AdvancedSyntaxStatusText(check);
                Color statusColor = check.valid ? AccentColor : AdvancedSyntaxErrorColor;
                DrawAdvancedSyntaxLabel(new Rect(inner.x, y, inner.width, rowHeight), status, statusColor);
                y += rowHeight + 1f;

                if (!check.valid)
                {
                    AdvancedRawSyntaxError error = check.firstError;
                    string raw = error == null ? string.Empty : error.rawText;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        DrawAdvancedSyntaxLabel(new Rect(inner.x, y, inner.width, rowHeight), raw, HintColor);
                    }

                    return;
                }

                if (check.empty)
                {
                    return;
                }

                int rows = Math.Min(AdvancedSyntaxPreviewMaxRows, check.lines.Count);
                for (int i = 0; i < rows; i++)
                {
                    DrawAdvancedSyntaxLinePreview(new Rect(inner.x, y, inner.width, rowHeight), check.lines[i]);
                    y += rowHeight + 1f;
                }

                int remaining = check.lines.Count - rows;
                if (remaining > 0)
                {
                    string more = "PawnDiary.Settings.Adv.Syntax.More".Translate(remaining).ToString();
                    DrawAdvancedSyntaxLabel(new Rect(inner.x, y, inner.width, rowHeight), more, HintColor);
                }
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        private static void DrawAdvancedSyntaxLinePreview(Rect rect, AdvancedRawSyntaxLine line)
        {
            if (line == null)
            {
                return;
            }

            float x = rect.x;
            float lineNumberWidth = 38f;
            float separatorWidth = 10f;
            int columnCount = Math.Max(1, line.columns.Count);
            float columnsWidth = Mathf.Max(0f, rect.width - lineNumberWidth - separatorWidth * (columnCount - 1));
            float columnWidth = columnCount == 0 ? columnsWidth : columnsWidth / columnCount;

            DrawAdvancedSyntaxLabel(
                new Rect(x, rect.y, lineNumberWidth, rect.height),
                "PawnDiary.Settings.Adv.Syntax.Line".Translate(line.lineNumber).ToString(),
                HintColor);
            x += lineNumberWidth;
            if (line.columns.Count == 0)
            {
                DrawAdvancedSyntaxLabel(new Rect(x, rect.y, columnsWidth, rect.height), line.rawText, HintColor);
                return;
            }

            for (int i = 0; i < line.columns.Count; i++)
            {
                if (i > 0)
                {
                    DrawAdvancedSyntaxLabel(new Rect(x, rect.y, separatorWidth, rect.height), "|", HintColor);
                    x += separatorWidth;
                }

                DrawAdvancedSyntaxLabel(
                    new Rect(x, rect.y, columnWidth, rect.height),
                    line.columns[i],
                    AdvancedSyntaxColumnColor(i));
                x += columnWidth;
            }
        }

        private static void DrawAdvancedSyntaxLabel(Rect rect, string text, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            Widgets.LabelFit(rect, text ?? string.Empty);
            GUI.color = previousColor;
        }

        private static string AdvancedSyntaxStatusText(AdvancedRawSyntaxCheck check)
        {
            if (check == null)
            {
                return string.Empty;
            }

            if (!check.valid)
            {
                AdvancedRawSyntaxError error = check.firstError;
                string detail = AdvancedSyntaxErrorDetail(error);
                int line = error == null ? 0 : error.lineNumber;
                return "PawnDiary.Settings.Adv.Syntax.Invalid".Translate(line, detail).ToString();
            }

            if (check.empty)
            {
                return check.nullSentinel
                    ? "PawnDiary.Settings.Adv.Syntax.Null".Translate().ToString()
                    : "PawnDiary.Settings.Adv.Syntax.Empty".Translate().ToString();
            }

            return "PawnDiary.Settings.Adv.Syntax.Valid".Translate(check.lines.Count).ToString();
        }

        private static string AdvancedSyntaxErrorDetail(AdvancedRawSyntaxError error)
        {
            if (error == null)
            {
                return string.Empty;
            }

            switch (error.issue)
            {
                case AdvancedRawSyntaxIssue.MissingCategory:
                    return "PawnDiary.Settings.Adv.Syntax.ErrMissingCategory".Translate().ToString();
                case AdvancedRawSyntaxIssue.MissingThoughtDef:
                    return "PawnDiary.Settings.Adv.Syntax.ErrMissingThoughtDef".Translate().ToString();
                case AdvancedRawSyntaxIssue.MissingStages:
                    return "PawnDiary.Settings.Adv.Syntax.ErrMissingStages".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadStagePair:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadStagePair".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadStageIndex:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadStageIndex".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadSeverity:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadSeverity".Translate().ToString();
                case AdvancedRawSyntaxIssue.ExpectedPair:
                    return "PawnDiary.Settings.Adv.Syntax.ErrExpectedPair".Translate().ToString();
                case AdvancedRawSyntaxIssue.MissingKey:
                    return "PawnDiary.Settings.Adv.Syntax.ErrMissingKey".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadFloat:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadFloat".Translate(error.token).ToString();
                case AdvancedRawSyntaxIssue.ExpectedPromptFieldColumns:
                    return "PawnDiary.Settings.Adv.Syntax.ErrPromptFields".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadBool:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadBool".Translate().ToString();
                case AdvancedRawSyntaxIssue.ExpectedSeverityTierColumns:
                    return "PawnDiary.Settings.Adv.Syntax.ErrSeverityTier".Translate().ToString();
                case AdvancedRawSyntaxIssue.BadInt:
                    return "PawnDiary.Settings.Adv.Syntax.ErrBadInt".Translate(error.token).ToString();
                default:
                    return "PawnDiary.Settings.Adv.Syntax.ErrThoughtColumns".Translate().ToString();
            }
        }

        private static Color AdvancedSyntaxColumnColor(int columnIndex)
        {
            switch (columnIndex % 4)
            {
                case 0: return AdvancedSyntaxCategoryColor;
                case 1: return AdvancedSyntaxDefColor;
                case 2: return AdvancedSyntaxStageColor;
                default: return HintColor;
            }
        }

        private static string AdvancedSyntaxStageText(AdvancedRawSyntaxLine line)
        {
            if (line == null || line.stages.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < line.stages.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                AdvancedRawSyntaxStage stage = line.stages[i];
                sb.Append(stage.stageIndex).Append(':').Append(stage.severity);
            }

            return sb.ToString();
        }

        private void DrawAdvancedFieldLabel(Rect rect, string label, bool overridden)
        {
            // Plain Label (not LabelFit) at full width: no font shrinking, no clipping for long names.
            Color previousColor = GUI.color;
            if (overridden)
            {
                GUI.color = AccentColor;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, label ?? string.Empty);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previousColor;
        }

        private void DrawAdvancedResetButton(Rect rect, AdvancedFieldDescriptor descriptor, bool overridden)
        {
            if (!overridden)
            {
                return; // No override to reset; keep the column visually empty for alignment.
            }

            if (ButtonTextFit(rect, "PawnDiary.Settings.Adv.ResetShort".Translate()))
            {
                AdvancedFieldCatalog.ResetField(Settings.advancedOverrides, descriptor);
                ClearAdvancedFieldUiState(descriptor);
            }

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.Adv.ResetFieldTip".Translate());
        }

        /// <summary>Writes a value into the live Def field and records the override in the store.</summary>
        private void CommitAdvancedValue(AdvancedFieldDescriptor descriptor, object value)
        {
            string formatted = descriptor.Format(value);
            if (string.IsNullOrEmpty(formatted))
            {
                AdvancedFieldCatalog.ResetField(Settings.advancedOverrides, descriptor);
                advancedExpandedOverrideFields.Remove(descriptor.key);
                advancedTextSynced[descriptor.key] = descriptor.ReadDisplayValueString();
                return;
            }

            descriptor.WriteDefValue(value);
            Settings.advancedOverrides.Set(descriptor.key, formatted);
            advancedTextSynced[descriptor.key] = formatted;
        }

        /// <summary>Builds a per-field tooltip: authored help (when keyed) plus live value/default/range/status.</summary>
        private string BuildAdvancedTooltip(AdvancedFieldDescriptor descriptor)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(descriptor.DisplayLabel());

            string help = descriptor.DisplayTooltip();
            if (!string.IsNullOrEmpty(help))
            {
                sb.AppendLine().Append(help);
            }

            if (!descriptor.isLongText)
            {
                string current = descriptor.ReadDefValueString();
                string defaultValue = descriptor.defaultSnapshot;
                sb.AppendLine().Append("PawnDiary.Settings.Adv.TipCurrent".Translate(current).ToString());
                sb.AppendLine().Append("PawnDiary.Settings.Adv.TipDefault".Translate(defaultValue).ToString());
                if (descriptor.hasRange)
                {
                    sb.AppendLine().Append("PawnDiary.Settings.Adv.TipRange".Translate(
                        descriptor.min.ToString("0.###", CultureInfo.InvariantCulture),
                        descriptor.max.ToString("0.###", CultureInfo.InvariantCulture)).ToString());
                }
            }

            bool overridden = Settings.advancedOverrides.HasOverride(descriptor.key);
            sb.AppendLine().Append(overridden
                ? "PawnDiary.Settings.Adv.TipCustomized".Translate().ToString()
                : "PawnDiary.Settings.Adv.TipXmlDefault".Translate().ToString());

            return sb.ToString();
        }

        private static string AdvancedGroupTooltip(AdvancedFieldGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            string prefix = "PawnDiary.Settings.Adv.Group.";
            if (string.IsNullOrEmpty(group.groupKey) || !group.groupKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                return group.DisplayTitle();
            }

            string key = "PawnDiary.Settings.Adv.GroupHelp." + group.groupKey.Substring(prefix.Length);
            if (!key.CanTranslate())
            {
                return group.DisplayTitle();
            }

            TaggedString tagged = key.Translate();
            string resolved = tagged == null ? string.Empty : ((string)tagged).StripTags();
            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }

            return group.DisplayTitle();
        }

        private bool AdvancedFilterActive()
        {
            return !string.IsNullOrWhiteSpace(advancedFilter);
        }

        private IReadOnlyList<AdvancedFieldDescriptor> FieldsForBody(AdvancedFieldCategory category)
        {
            string needle = AdvancedFilterActive() ? advancedFilter.Trim().ToLowerInvariant() : null;
            if (needle == null)
            {
                AdvancedFieldGroup group = SelectedAdvancedGroup(category);
                return group != null
                    ? FilterAdvancedFields(group.fields)
                    : Array.Empty<AdvancedFieldDescriptor>();
            }

            List<AdvancedFieldDescriptor> matches = new List<AdvancedFieldDescriptor>();
            List<AdvancedFieldGroup> groups = GroupsForCategory(category);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<AdvancedFieldDescriptor> fields = groups[groupIndex].fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    AdvancedFieldDescriptor descriptor = fields[i];
                    if (AdvancedFieldVisibleByMode(descriptor)
                        && (descriptor.DisplayLabel().ToLowerInvariant().Contains(needle)
                            || descriptor.key.ToLowerInvariant().Contains(needle)))
                    {
                        matches.Add(descriptor);
                    }
                }
            }

            return matches;
        }

        private IReadOnlyList<AdvancedFieldDescriptor> FilterAdvancedFields(IReadOnlyList<AdvancedFieldDescriptor> fields)
        {
            if (fields == null)
            {
                return Array.Empty<AdvancedFieldDescriptor>();
            }

            if (advancedFieldFilterMode == AdvancedFieldFilterMode.All)
            {
                return fields;
            }

            List<AdvancedFieldDescriptor> filtered = new List<AdvancedFieldDescriptor>();
            for (int i = 0; i < fields.Count; i++)
            {
                AdvancedFieldDescriptor descriptor = fields[i];
                if (AdvancedFieldVisibleByMode(descriptor))
                {
                    filtered.Add(descriptor);
                }
            }

            return filtered;
        }

        private bool AdvancedFieldVisibleByMode(AdvancedFieldDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return false;
            }

            switch (advancedFieldFilterMode)
            {
                case AdvancedFieldFilterMode.Changed:
                    return Settings.advancedOverrides.HasOverride(descriptor.key);
                case AdvancedFieldFilterMode.Raw:
                    return IsAdvancedLongTextField(descriptor);
                default:
                    return true;
            }
        }

        private AdvancedFieldGroup SelectedAdvancedGroup(AdvancedFieldCategory category)
        {
            IReadOnlyList<AdvancedFieldGroup> groups = AdvancedFieldCatalog.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].category == category
                    && string.Equals(groups[i].groupKey, selectedAdvancedGroupKey, StringComparison.Ordinal))
                {
                    return groups[i];
                }
            }

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].category == category)
                {
                    selectedAdvancedGroupKey = groups[i].groupKey;
                    return groups[i];
                }
            }

            return null;
        }

        private float AdvancedBodyHeight(IReadOnlyList<AdvancedFieldDescriptor> fields, AdvancedFieldCategory category)
        {
            // Measure the Medium title height so it matches what DrawAdvancedBodyHeader actually
            // renders (line height varies with UI scale / text-size accessibility settings).
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            float titleHeight = Mathf.Ceil(Text.LineHeight) + 2f;
            Text.Font = previousFont;

            float height = titleHeight + 2f; // group title
            AdvancedFieldGroup group = SelectedAdvancedGroup(category);
            if (!AdvancedFilterActive() && group != null && GroupHasOverride(group))
            {
                height += 24f + AdvancedFieldDescriptor.AdvancedRowGap; // per-group reset row
            }

            height += 4f;
            for (int i = 0; i < fields.Count; i++)
            {
                height += AdvancedFieldRowHeight(fields[i]) + AdvancedFieldDescriptor.AdvancedRowGap;
            }

            return height;
        }

        private float AdvancedFieldRowHeight(AdvancedFieldDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return 0f;
            }

            if (descriptor.fieldType == AdvancedFieldType.Bool)
            {
                return AdvancedFieldDescriptor.AdvancedBoolRowHeight;
            }

            if (IsAdvancedLongTextField(descriptor))
            {
                return AdvancedFieldDescriptor.AdvancedLabelLineHeight
                    + AdvancedLongTextControlHeight(descriptor)
                    + AdvancedFieldDescriptor.AdvancedRowGap;
            }

            return AdvancedFieldDescriptor.AdvancedLabelLineHeight
                + AdvancedFieldDescriptor.AdvancedControlLineHeight
                + AdvancedFieldDescriptor.AdvancedRowGap;
        }

        private float AdvancedLongTextControlHeight(AdvancedFieldDescriptor descriptor)
        {
            if (AdvancedLongTextCollapsed(descriptor))
            {
                return AdvancedFieldDescriptor.AdvancedControlLineHeight;
            }

            float height = AdvancedFieldDescriptor.AdvancedLongTextHeight;
            if (descriptor != null && AdvancedRawSyntax.HasPreview(descriptor.fieldName))
            {
                height += AdvancedFieldDescriptor.AdvancedRowGap
                    + AdvancedFieldDescriptor.AdvancedSyntaxPreviewHeight;
            }

            return height;
        }

        private bool AdvancedLongTextCollapsed(AdvancedFieldDescriptor descriptor)
        {
            if (!IsAdvancedLongTextField(descriptor)
                || Settings.advancedOverrides.HasOverride(descriptor.key)
                || advancedExpandedOverrideFields.Contains(descriptor.key))
            {
                return false;
            }

            string buffer;
            if (advancedTextBuffers.TryGetValue(descriptor.key, out buffer)
                && !string.IsNullOrWhiteSpace(buffer))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(descriptor.ReadDisplayValueString());
        }

        private static bool IsAdvancedLongTextField(AdvancedFieldDescriptor descriptor)
        {
            return descriptor != null
                && (descriptor.isLongText
                    || descriptor.fieldType == AdvancedFieldType.StringList
                    || descriptor.fieldType == AdvancedFieldType.IntList);
        }

        private static List<AdvancedFieldGroup> GroupsForCategory(AdvancedFieldCategory category)
        {
            List<AdvancedFieldGroup> result = new List<AdvancedFieldGroup>();
            IReadOnlyList<AdvancedFieldGroup> groups = AdvancedFieldCatalog.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].category == category)
                {
                    result.Add(groups[i]);
                }
            }

            return result;
        }

        private bool GroupHasOverride(AdvancedFieldGroup group)
        {
            for (int i = 0; i < group.fields.Count; i++)
            {
                if (Settings.advancedOverrides.HasOverride(group.fields[i].key))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FilterAdvancedInput(string text, AdvancedFieldType type)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (type == AdvancedFieldType.String
                || type == AdvancedFieldType.StringList
                || type == AdvancedFieldType.IntList)
            {
                return text;
            }

            // Int/Float: keep only chars that can form a number; the descriptor clamps the result.
            // Int strips the decimal point so a half-typed float never reaches int.Parse.
            string allowed = type == AdvancedFieldType.Int ? "-0123456789" : AdvancedNumberChars;
            char[] chars = new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (allowed.IndexOf(c) >= 0)
                {
                    chars[count++] = c;
                }
            }

            return count == 0 ? string.Empty : new string(chars, 0, count);
        }
    }
}
