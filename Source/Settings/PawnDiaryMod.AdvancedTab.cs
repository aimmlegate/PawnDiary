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
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float AdvancedRailWidth = 204f;
        private const float AdvancedResetButtonWidth = 58f;
        private const string AdvancedNumberChars = "-0123456789.";

        /// <summary>Draws the Advanced tab: summary bar, name filter, group rail, and the field body.</summary>
        private void DrawAdvancedTab(Rect inRect, AdvancedFieldCategory category)
        {
            // Make sure saved overrides are applied even when the tab is opened straight from the
            // main menu (settings load is normally enough; this is the defensive idempotent backstop).
            AdvancedFieldCatalog.EnsureApplied(Settings.advancedOverrides);

            float y = inRect.y;

            Rect summaryRect = new Rect(inRect.x, y, inRect.width, 22f);
            int overrideCount = Settings.advancedOverrides.Count;
            DrawMutedLabel(summaryRect, "PawnDiary.Settings.Adv.Summary".Translate(overrideCount));
            y += summaryRect.height + 4f;

            Rect filterRect = new Rect(inRect.x, y, Mathf.Min(inRect.width * 0.55f, 340f), 28f);
            advancedFilter = DrawCompactTextField(
                filterRect,
                "PawnDiary.Settings.Adv.Filter".Translate(),
                advancedFilter,
                70f);

            Rect resetAllRect = new Rect(inRect.xMax - 150f, y, 150f, 28f);
            if (ButtonTextFit(resetAllRect, "PawnDiary.Settings.Adv.ResetAll".Translate()) && overrideCount > 0)
            {
                AdvancedFieldCatalog.ResetAll(Settings.advancedOverrides);
                advancedTextBuffers.Clear();
                advancedTextSynced.Clear();
            }

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
                    Rect rowRect = new Rect(0f, y, viewRect.width, descriptor.RowHeight);
                    DrawAdvancedFieldRow(rowRect, descriptor);
                    y += descriptor.RowHeight + AdvancedFieldDescriptor.AdvancedRowGap;
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
            Rect titleRect = new Rect(0f, 0f, viewRect.width, 24f);
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
                        advancedTextBuffers.Remove(d.key);
                        advancedTextSynced.Remove(d.key);
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
                    DrawAdvancedLongTextControl(new Rect(rect.x, controlTop, controlWidth, AdvancedFieldDescriptor.AdvancedLongTextHeight), descriptor);
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
            // Prompt templates often leave their raw XML field blank to inherit a shared prompt.
            // Show the resolved effective prompt here so the editor is not an empty black box.
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

            string edited = Widgets.TextArea(rect, buffer ?? string.Empty);
            advancedTextBuffers[descriptor.key] = edited;

            if (!string.Equals(edited, currentInvariant, StringComparison.Ordinal))
            {
                object parsed;
                if (descriptor.TryParse(edited ?? string.Empty, out parsed))
                {
                    CommitAdvancedValue(descriptor, parsed);
                    string display = descriptor.ReadDisplayValueString();
                    advancedTextBuffers[descriptor.key] = display;
                    advancedTextSynced[descriptor.key] = display;
                }
            }
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
                advancedTextBuffers.Remove(descriptor.key);
                advancedTextSynced.Remove(descriptor.key);
            }

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.Adv.ResetFieldTip".Translate());
        }

        /// <summary>Writes a value into the live Def field and records the override in the store.</summary>
        private void CommitAdvancedValue(AdvancedFieldDescriptor descriptor, object value)
        {
            descriptor.WriteDefValue(value);
            Settings.advancedOverrides.Set(descriptor.key, descriptor.Format(value));
            advancedTextSynced[descriptor.key] = descriptor.Format(value);
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
                return group != null ? (IReadOnlyList<AdvancedFieldDescriptor>)group.fields : Array.Empty<AdvancedFieldDescriptor>();
            }

            List<AdvancedFieldDescriptor> matches = new List<AdvancedFieldDescriptor>();
            List<AdvancedFieldGroup> groups = GroupsForCategory(category);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<AdvancedFieldDescriptor> fields = groups[groupIndex].fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    AdvancedFieldDescriptor descriptor = fields[i];
                    if (descriptor.DisplayLabel().ToLowerInvariant().Contains(needle)
                        || descriptor.key.ToLowerInvariant().Contains(needle))
                    {
                        matches.Add(descriptor);
                    }
                }
            }

            return matches;
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
            float height = 24f + 2f; // group title
            AdvancedFieldGroup group = SelectedAdvancedGroup(category);
            if (!AdvancedFilterActive() && group != null && GroupHasOverride(group))
            {
                height += 24f + AdvancedFieldDescriptor.AdvancedRowGap; // per-group reset row
            }

            height += 4f;
            for (int i = 0; i < fields.Count; i++)
            {
                height += fields[i].RowHeight + AdvancedFieldDescriptor.AdvancedRowGap;
            }

            return height;
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
