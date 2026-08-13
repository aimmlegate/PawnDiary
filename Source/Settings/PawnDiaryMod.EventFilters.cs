// Events-tab settings UI for automatic diary capture. It keeps the existing hard enable checkbox
// separate from the new preset/inherited frequency controls, and delegates search/group projection
// to a pure policy so the immediate-mode renderer only draws the resulting rows.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private Vector2 eventFilterScrollPosition;

        /// <summary>Draws frequency presets plus saved automatic-capture controls on the Events tab.</summary>
        private void DrawEventFilterPanel(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            List<DiaryInteractionGroupDef> groups = EventFilterGroupsForUi();
            if (groups == null)
            {
                Verse.Log.Error("Event filter groups cannot be null");
                return;
            }

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            bool compactHeader = inner.height
                < UiMetric(DiaryUiStyles.Current.settingsEventsCompactHeightThreshold);

            DrawEventFilterTitleRow(inner, groups, ref y);
            DrawFrequencyPresetSection(inner, groups, compactHeader, ref y);
            if (Settings.frequencyMigrationNoticePending)
            {
                DrawFrequencyMigrationNotice(inner, compactHeader, ref y);
            }

            DrawEventFilterSearchAndSummary(inner, groups, ref y);

            if (!compactHeader)
            {
                string help = "PawnDiary.Settings.EventFilters.Help".Translate().ToString();
                float helpHeight = WrappedTextHeight(help, inner.width, GameFont.Small);
                DrawWrappedMutedLabel(
                    new Rect(inner.x, y, inner.width, helpHeight),
                    help,
                    GameFont.Small);
                y += helpHeight + 6f;
            }

            // Never move y backwards to manufacture list space: doing so paints the scroll view over
            // controls that were already drawn. Compact mode makes real space; exceptionally short
            // windows simply receive a zero-height list instead of overlapping the header.
            Rect listRect = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));
            DrawEventFilterRows(listRect, groups);
        }

        private void DrawEventFilterTitleRow(
            Rect inner,
            List<DiaryInteractionGroupDef> groups,
            ref float y)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float titleHeight = UiMetric(style.settingsEventsTitleHeight);
            float buttonWidth = UiMetric(style.settingsEventsEnableAllButtonWidth);
            Rect titleRect = new Rect(
                inner.x,
                y,
                Mathf.Max(0f, inner.width - buttonWidth - 8f),
                titleHeight);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(titleRect, "PawnDiary.Settings.EventFilters.Title".Translate());
            Text.Font = previousFont;

            Rect enableAllRect = new Rect(
                inner.xMax - buttonWidth,
                y,
                buttonWidth,
                titleHeight);
            if (ButtonTextFit(enableAllRect, "PawnDiary.Settings.EventFilters.EnableAll".Translate())
                && groups.Count > 0)
            {
                EnableVisibleEventFilters(groups);
            }

            TooltipHandler.TipRegion(
                enableAllRect,
                "PawnDiary.Settings.EventFilters.EnableAllTip".Translate());
            y += titleHeight + 4f;
        }

        private void DrawFrequencyPresetSection(
            Rect inner,
            List<DiaryInteractionGroupDef> groups,
            bool compact,
            ref float y)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float headerHeight = UiMetric(style.settingsEventsPresetHeaderHeight);
            float resetButtonWidth = UiMetric(style.settingsEventsResetButtonWidth);
            int overrideCount = Settings.GroupFrequencyOverrideCount();
            Rect headerRect = new Rect(
                inner.x,
                y,
                Mathf.Max(0f, inner.width - resetButtonWidth - 8f),
                headerHeight);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(
                headerRect,
                "PawnDiary.Settings.EventFilters.FrequencyTitle".Translate());
            Text.Font = previousFont;

            Rect resetRect = new Rect(
                inner.xMax - resetButtonWidth,
                y,
                resetButtonWidth,
                headerHeight);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && overrideCount > 0;
            bool resetClicked = ButtonTextFit(
                resetRect,
                "PawnDiary.Settings.EventFilters.ResetToPreset".Translate());
            GUI.enabled = previousEnabled;
            if (resetClicked && overrideCount > 0)
            {
                ConfirmResetFrequencyOverrides(overrideCount);
            }

            TooltipHandler.TipRegion(
                resetRect,
                overrideCount > 0
                    ? "PawnDiary.Settings.EventFilters.ResetToPresetTip".Translate(overrideCount)
                    : "PawnDiary.Settings.EventFilters.ResetToPresetEmptyTip".Translate());
            y += headerHeight + 4f;

            List<DiaryFrequencyPresetDef> presets = FrequencyPresetDefsForUi();
            float cardHeight = compact
                ? UiMetric(style.settingsEventsCompactPresetCardHeight)
                : UiMetric(style.settingsEventsPresetCardHeight);
            float gap = 6f;
            float cardWidth = presets.Count > 0
                ? Mathf.Max(0f, (inner.width - gap * (presets.Count - 1)) / presets.Count)
                : inner.width;
            if (!compact && presets.Count > 0)
            {
                // Treat the XML card height as a minimum. Translated descriptions wrap to their
                // measured height so the expected-volume summary is visible rather than ellipsized.
                GameFont previousCardFont = Text.Font;
                Text.Font = GameFont.Tiny;
                float descriptionWidth = Mathf.Max(1f, cardWidth - 14f);
                for (int i = 0; i < presets.Count; i++)
                {
                    float measured = Text.CalcHeight(
                        presets[i]?.description ?? string.Empty,
                        descriptionWidth);
                    cardHeight = Mathf.Max(cardHeight, 45f + measured);
                }

                Text.Font = previousCardFont;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                DiaryFrequencyPresetDef preset = presets[i];
                Rect cardRect = new Rect(
                    inner.x + i * (cardWidth + gap),
                    y,
                    cardWidth,
                    cardHeight);
                DrawFrequencyPresetCard(cardRect, preset, overrideCount, compact);
            }

            y += cardHeight + 4f;
            if (compact)
            {
                return;
            }

            int visibleOverrideCount = VisibleFrequencyOverrideCount(groups);
            bool hasVisibleOverrides = visibleOverrideCount > 0;
            DiaryFrequencyPresetDef selected = SelectedFrequencyPresetDef();
            string selectedLabel = FrequencyPresetLabel(selected);
            string status;
            if (Settings.frequencyMigrationNoticePending && hasVisibleOverrides)
            {
                status = "PawnDiary.Settings.EventFilters.PresetStatusMigrated"
                    .Translate(selectedLabel, visibleOverrideCount).ToString();
            }
            else if (hasVisibleOverrides)
            {
                status = "PawnDiary.Settings.EventFilters.PresetStatusCustom"
                    .Translate(selectedLabel, visibleOverrideCount).ToString();
            }
            else if (overrideCount > 0)
            {
                status = "PawnDiary.Settings.EventFilters.PresetStatusHiddenOverrides"
                    .Translate(selectedLabel, overrideCount).ToString();
            }
            else
            {
                status = "PawnDiary.Settings.EventFilters.PresetStatusInherited"
                    .Translate(selectedLabel).ToString();
            }

            DrawMutedLabel(
                new Rect(
                    inner.x,
                    y,
                    inner.width,
                    UiMetric(style.settingsEventsPresetStatusHeight)),
                status);
            y += UiMetric(style.settingsEventsPresetStatusHeight) + 4f;
        }

        private void DrawFrequencyPresetCard(
            Rect rect,
            DiaryFrequencyPresetDef preset,
            int overrideCount,
            bool compact)
        {
            if (preset == null)
            {
                return;
            }

            bool selected = string.Equals(
                Settings.frequencyPresetDefName,
                preset.defName,
                StringComparison.OrdinalIgnoreCase);
            Widgets.DrawMenuSection(rect);
            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            Rect content = rect.ContractedBy(7f);
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Medium;
            if (compact)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
            }

            Widgets.LabelFit(
                compact
                    ? content
                    : new Rect(content.x, content.y, content.width, 28f),
                preset.LabelCap);
            if (!compact)
            {
                Text.Anchor = previousAnchor;
                Text.Font = GameFont.Tiny;
                Widgets.Label(
                    new Rect(content.x, content.y + 31f, content.width, content.height - 31f),
                    preset.description ?? string.Empty);
            }

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            TooltipHandler.TipRegion(rect, preset.description ?? string.Empty);
            if (Widgets.ButtonInvisible(rect))
            {
                bool samePreset = selected;
                if (!samePreset || overrideCount > 0)
                {
                    SelectFrequencyPreset(preset, overrideCount);
                }
            }
        }

        private void SelectFrequencyPreset(DiaryFrequencyPresetDef preset, int overrideCount)
        {
            if (preset == null)
            {
                return;
            }

            if (overrideCount <= 0)
            {
                Settings.SetFrequencyPreset(preset.defName, true);
                return;
            }

            string label = FrequencyPresetLabel(preset);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PawnDiary.Settings.EventFilters.SelectPresetConfirm"
                    .Translate(label, overrideCount),
                delegate { Settings.SetFrequencyPreset(preset.defName, true); },
                false,
                "PawnDiary.Settings.EventFilters.SelectPresetTitle".Translate(label).Resolve()));
        }

        private void ConfirmResetFrequencyOverrides(int overrideCount)
        {
            DiaryFrequencyPresetDef selected = SelectedFrequencyPresetDef();
            string label = FrequencyPresetLabel(selected);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PawnDiary.Settings.EventFilters.ResetToPresetConfirm"
                    .Translate(label, overrideCount),
                delegate { Settings.ResetAllGroupFrequencyOverrides(); },
                false,
                "PawnDiary.Settings.EventFilters.ResetToPresetTitle".Translate().Resolve()));
        }

        private void DrawFrequencyMigrationNotice(Rect inner, bool compact, ref float y)
        {
            float dismissButtonWidth = UiMetric(
                DiaryUiStyles.Current.settingsEventsDismissButtonWidth);
            string notice = (compact
                    ? "PawnDiary.Settings.EventFilters.MigrationNoticeCompact"
                    : "PawnDiary.Settings.EventFilters.MigrationNotice")
                .Translate().ToString();
            float horizontalPadding = 6f;
            float textWidth = Mathf.Max(
                1f,
                inner.width - horizontalPadding * 2f - dismissButtonWidth - 8f);
            float textHeight = WrappedTextHeight(notice, textWidth, GameFont.Tiny);
            float contentHeight = Mathf.Max(28f, textHeight);
            float blockHeight = contentHeight + horizontalPadding * 2f;
            Rect block = new Rect(inner.x, y, inner.width, blockHeight);
            Widgets.DrawMenuSection(block);
            Rect content = block.ContractedBy(horizontalPadding);
            Rect dismissRect = new Rect(
                content.xMax - dismissButtonWidth,
                content.y + (content.height - 28f) * 0.5f,
                dismissButtonWidth,
                28f);
            Rect textRect = new Rect(
                content.x,
                content.y,
                Mathf.Max(0f, dismissRect.x - content.x - 8f),
                content.height);
            DrawWrappedMutedLabel(textRect, notice, GameFont.Tiny);
            if (ButtonTextFit(
                dismissRect,
                "PawnDiary.Settings.EventFilters.MigrationDismiss".Translate()))
            {
                Settings.AcknowledgeFrequencyMigrationNotice();
            }

            y += blockHeight + 6f;
        }

        private void DrawEventFilterSearchAndSummary(
            Rect inner,
            List<DiaryInteractionGroupDef> groups,
            ref float y)
        {
            float searchWidth = Mathf.Min(360f, Mathf.Max(220f, inner.width * 0.48f));
            Rect summaryRect = new Rect(
                inner.x,
                y,
                Mathf.Max(0f, inner.width - searchWidth - 8f),
                30f);
            DrawMutedLabel(
                summaryRect,
                "PawnDiary.Settings.EventFilters.Summary".Translate(
                    DisabledVisibleEventFilterCount(groups),
                    groups.Count).ToString());

            Rect searchRect = new Rect(inner.xMax - searchWidth, y, searchWidth, 28f);
            eventFilterSearch = DrawCompactTextField(
                searchRect,
                "PawnDiary.Settings.EventFilters.Search".Translate(),
                eventFilterSearch,
                Mathf.Min(88f, searchWidth * 0.32f));
            y += 34f;
        }

        private void DrawEventFilterRows(Rect rect, List<DiaryInteractionGroupDef> groups)
        {
            if (rect.height <= 0f)
            {
                return;
            }

            EnsureEventFilterCatalog(groups);
            List<DiaryEventFilterListSection> sections = EventFilterSectionsForCurrentView();
            if (sections.Count == 0)
            {
                Widgets.Label(
                    rect,
                    string.IsNullOrWhiteSpace(eventFilterSearch)
                        ? "PawnDiary.Settings.EventFilters.None".Translate()
                        : "PawnDiary.Settings.EventFilters.NoSearchResults".Translate());
                return;
            }

            float contentHeight = EventFilterListContentHeight(sections);
            // The preset snapshot owns dictionaries, so freeze it once per list draw rather than
            // copying those dictionaries once for every visible row during each IMGUI event.
            DiaryFrequencyPresetSnapshot preset = Settings.FrequencyPresetSnapshot();
            string presetLabel = FrequencyPresetLabel(SelectedFrequencyPresetDef());
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float domainHeight = UiMetric(style.settingsEventsDomainHeight);
            float rowHeight = UiMetric(style.settingsEventsRowHeight);
            float rowGap = UiMetric(style.settingsEventsRowGap, 0f);
            Rect viewRect = new Rect(
                0f,
                0f,
                rect.width - 16f,
                Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref eventFilterScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < sections.Count; i++)
                {
                    DiaryEventFilterListSection section = sections[i];
                    Rect headingRect = new Rect(0f, y, viewRect.width, domainHeight);
                    DrawEventFilterDomainHeading(headingRect, section);
                    y += domainHeight + rowGap;

                    for (int j = 0; j < section.visibleGroupKeys.Count; j++)
                    {
                        DiaryInteractionGroupDef group;
                        if (!eventFilterGroupByKey.TryGetValue(
                            section.visibleGroupKeys[j],
                            out group))
                        {
                            continue;
                        }

                        Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight);
                        DrawEventFilterRow(rowRect, group, preset, presetLabel);
                        y += rowHeight + rowGap;
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            eventFilterScrollPosition.y = Mathf.Clamp(
                eventFilterScrollPosition.y,
                0f,
                Mathf.Max(0f, contentHeight - rect.height));
        }

        private void DrawEventFilterDomainHeading(
            Rect rect,
            DiaryEventFilterListSection section)
        {
            bool searching = !string.IsNullOrWhiteSpace(eventFilterSearch);
            if (!searching)
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            string symbol = section.collapsed ? "▶" : "▼";
            Widgets.LabelFit(
                rect.ContractedBy(4f),
                searching
                    ? "PawnDiary.Settings.EventFilters.DomainHeadingSearch".Translate(
                        symbol,
                        section.domainLabel,
                        section.visibleGroupKeys.Count,
                        section.totalCount)
                    : "PawnDiary.Settings.EventFilters.DomainHeading".Translate(
                        symbol,
                        section.domainLabel,
                        section.totalCount));
            TooltipHandler.TipRegion(
                rect,
                searching
                    ? "PawnDiary.Settings.EventFilters.SearchDomainTip".Translate()
                    : section.collapsed
                    ? "PawnDiary.Settings.EventFilters.ExpandDomainTip".Translate()
                    : "PawnDiary.Settings.EventFilters.CollapseDomainTip".Translate());
            if (!searching && Widgets.ButtonInvisible(rect))
            {
                if (!eventFilterCollapsedDomains.Add(section.domainToken))
                {
                    eventFilterCollapsedDomains.Remove(section.domainToken);
                }

                eventFilterCollapseRevision++;
            }
        }

        private void EnsureEventFilterCatalog(List<DiaryInteractionGroupDef> groups)
        {
            LoadedLanguage language = LanguageDatabase.activeLanguage;
            if (eventFilterCatalogRows != null
                && eventFilterGroupByKey != null
                && ReferenceEquals(eventFilterCatalogLanguage, language)
                && ReferenceEquals(eventFilterCatalogSource, groups))
            {
                return;
            }

            eventFilterCatalogLanguage = language;
            eventFilterCatalogSource = groups;
            eventFilterGroupByKey =
                new Dictionary<string, DiaryInteractionGroupDef>(StringComparer.OrdinalIgnoreCase);
            eventFilterCatalogRows = new List<DiaryEventFilterListRowSnapshot>();
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group == null || string.IsNullOrWhiteSpace(group.defName))
                {
                    continue;
                }

                eventFilterGroupByKey[group.defName] = group;
                eventFilterCatalogRows.Add(new DiaryEventFilterListRowSnapshot
                {
                    groupKey = group.defName,
                    label = EventFilterLabel(group),
                    domainToken = group.domain.ToString(),
                    domainLabel = EventFilterDomainLabel(group.domain)
                });
            }

            eventFilterCachedSections = null;
        }

        private List<DiaryInteractionGroupDef> EventFilterGroupsForUi()
        {
            LoadedLanguage language = LanguageDatabase.activeLanguage;
            int capabilityRevision = CaptureCapabilities.Revision;
            int mutationRevision = InteractionGroups.MutationRevision;
            if (eventFilterUiGroups != null
                && ReferenceEquals(eventFilterUiGroupsLanguage, language)
                && eventFilterUiGroupsCapabilityRevision == capabilityRevision
                && eventFilterUiGroupsMutationRevision == mutationRevision)
            {
                return eventFilterUiGroups;
            }

            eventFilterUiGroupsLanguage = language;
            eventFilterUiGroupsCapabilityRevision = capabilityRevision;
            eventFilterUiGroupsMutationRevision = mutationRevision;
            eventFilterUiGroups = EventFilterGroupsForSettings();
            return eventFilterUiGroups;
        }

        private List<DiaryEventFilterListSection> EventFilterSectionsForCurrentView()
        {
            string search = eventFilterSearch ?? string.Empty;
            if (eventFilterCachedSections == null
                || !string.Equals(
                    eventFilterCachedSectionSearch,
                    search,
                    StringComparison.Ordinal)
                || eventFilterCachedCollapseRevision != eventFilterCollapseRevision)
            {
                eventFilterCachedSections = DiaryEventFilterListPolicy.Build(
                    eventFilterCatalogRows,
                    search,
                    eventFilterCollapsedDomains);
                eventFilterCachedSectionSearch = search;
                eventFilterCachedCollapseRevision = eventFilterCollapseRevision;
            }

            return eventFilterCachedSections;
        }

        private void DrawEventFilterRow(
            Rect rect,
            DiaryInteractionGroupDef group,
            DiaryFrequencyPresetSnapshot preset,
            string presetLabel)
        {
            if (group == null)
            {
                return;
            }

            float frequencyButtonWidth = UiMetric(
                DiaryUiStyles.Current.settingsEventsFrequencyButtonWidth);
            Rect frequencyRect = new Rect(
                rect.xMax - frequencyButtonWidth,
                rect.y + 2f,
                frequencyButtonWidth,
                rect.height - 4f);
            Rect enableArea = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, frequencyRect.x - rect.x - 8f),
                rect.height);
            bool enableOverridden = Settings.HasGroupEnabledOverride(group.defName);
            bool enabled = Settings.IsGroupEnabled(group.defName);
            bool edited = enabled;
            Rect checkboxRect = enableOverridden
                ? new Rect(enableArea.x, enableArea.y, Mathf.Max(0f, enableArea.width - 78f), enableArea.height)
                : enableArea;
            Widgets.CheckboxLabeled(checkboxRect, EventFilterLabel(group), ref edited);
            if (edited != enabled)
            {
                Settings.SetGroupEnabled(group.defName, edited);
            }

            if (enableOverridden)
            {
                Rect changedRect = new Rect(
                    enableArea.xMax - 72f,
                    enableArea.y + 4f,
                    70f,
                    enableArea.height - 8f);
                DrawMutedLabel(
                    changedRect,
                    "PawnDiary.Settings.EventFilters.Changed".Translate().ToString());
            }

            TooltipHandler.TipRegion(
                enableArea,
                "PawnDiary.Settings.EventFilters.RowTip".Translate(
                    EventFilterLabel(group)).ToString());
            DrawEventFilterFrequencyButton(frequencyRect, group, preset, presetLabel, enabled);
        }

        private void DrawEventFilterFrequencyButton(
            Rect rect,
            DiaryInteractionGroupDef group,
            DiaryFrequencyPresetSnapshot preset,
            string presetLabel,
            bool enabled)
        {
            float effective = Settings.EffectiveGroupFrequencyMultiplier(group, preset);
            bool overridden = Settings.HasGroupFrequencyOverride(group.defName);
            DiaryFrequencyChoiceDef displayChoice = FrequencyChoiceForUi(effective);
            string choiceLabel = displayChoice != null
                ? displayChoice.LabelCap.Resolve()
                : "PawnDiary.Settings.EventFilters.FrequencyExact"
                    .Translate(FormatFrequencyMultiplier(effective)).ToString();
            string buttonLabel = overridden
                ? "PawnDiary.Settings.EventFilters.FrequencyButtonCustom"
                    .Translate(choiceLabel).ToString()
                : choiceLabel;

            Color previousColor = GUI.color;
            if (!enabled)
            {
                GUI.color = HintColor;
            }

            bool clicked = ButtonTextFit(rect, buttonLabel);
            GUI.color = previousColor;
            TooltipHandler.TipRegion(
                rect,
                EventFilterFrequencyTooltip(
                    group,
                    choiceLabel,
                    presetLabel,
                    effective,
                    overridden));
            if (clicked)
            {
                OpenEventFilterFrequencyMenu(group, preset, presetLabel);
            }
        }

        private void OpenEventFilterFrequencyMenu(
            DiaryInteractionGroupDef group,
            DiaryFrequencyPresetSnapshot preset,
            string presetLabel)
        {
            if (group == null)
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            float inherited = PawnDiarySettings.PresetGroupFrequencyMultiplier(group, preset);
            string groupKey = group.defName;
            options.Add(new FloatMenuOption(
                "PawnDiary.Settings.EventFilters.UsePresetChoice".Translate(
                    presetLabel,
                    FormatFrequencyMultiplier(inherited)),
                delegate { Settings.ResetGroupFrequencyOverride(groupKey); })
            {
                tooltip = "PawnDiary.Settings.EventFilters.UsePresetChoiceTip".Translate()
            });

            List<DiaryFrequencyChoiceDef> choices = DiaryFrequencyChoices.All();
            for (int i = 0; i < choices.Count; i++)
            {
                DiaryFrequencyChoiceDef choice = choices[i];
                string label = "PawnDiary.Settings.EventFilters.FrequencyChoiceEntry".Translate(
                    choice.LabelCap,
                    FormatFrequencyMultiplier(choice.multiplier));
                string tooltip = choice.description ?? string.Empty;
                float multiplier = choice.multiplier;
                options.Add(new FloatMenuOption(
                    label,
                    delegate { Settings.SetGroupFrequencyOverride(groupKey, multiplier); })
                {
                    tooltip = tooltip
                });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string EventFilterFrequencyTooltip(
            DiaryInteractionGroupDef group,
            string choiceLabel,
            string presetLabel,
            float effective,
            bool overridden)
        {
            string multiplier = FormatFrequencyMultiplier(effective);
            string groupLabel = EventFilterLabel(group);
            if (overridden)
            {
                return "PawnDiary.Settings.EventFilters.FrequencyTipCustom".Translate(
                    choiceLabel,
                    multiplier,
                    groupLabel).ToString();
            }

            return "PawnDiary.Settings.EventFilters.FrequencyTipInherited".Translate(
                choiceLabel,
                multiplier,
                presetLabel,
                groupLabel).ToString();
        }

        private static int VisibleFrequencyOverrideCount(
            List<DiaryInteractionGroupDef> groups)
        {
            if (groups == null || Settings == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (Settings.HasGroupFrequencyOverride(groups[i].defName))
                {
                    count++;
                }
            }

            return count;
        }

        private DiaryFrequencyChoiceDef FrequencyChoiceForUi(float effectiveMultiplier)
        {
            DiaryFrequencyChoiceDef choice;
            if (!eventFilterFrequencyDisplayCache.TryGetValue(effectiveMultiplier, out choice))
            {
                choice = DiaryFrequencyChoices.ForMultiplier(effectiveMultiplier);
                eventFilterFrequencyDisplayCache[effectiveMultiplier] = choice;
            }

            return choice;
        }

        private static float EventFilterListContentHeight(
            List<DiaryEventFilterListSection> sections)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float domainHeight = UiMetric(style.settingsEventsDomainHeight);
            float rowHeight = UiMetric(style.settingsEventsRowHeight);
            float rowGap = UiMetric(style.settingsEventsRowGap, 0f);
            float height = 0f;
            for (int i = 0; i < sections.Count; i++)
            {
                height += domainHeight + rowGap;
                height += sections[i].visibleGroupKeys.Count
                    * (rowHeight + rowGap);
            }

            return height;
        }

        private static string EventFilterDomainLabel(GroupDomain domain)
        {
            return ("PawnDiary.Settings.EventFilters.Domain." + domain).Translate().ToString();
        }

        private static string FormatFrequencyMultiplier(float multiplier)
        {
            return multiplier.ToString("0.##");
        }

        private List<DiaryFrequencyPresetDef> FrequencyPresetDefsForUi()
        {
            if (eventFilterFrequencyPresetDefs != null)
            {
                return eventFilterFrequencyPresetDefs;
            }

            List<DiaryFrequencyPresetDef> result = new List<DiaryFrequencyPresetDef>();
            AddFrequencyPresetForUi(result, DiaryFrequencyPresets.LiteDefName);
            AddFrequencyPresetForUi(result, DiaryFrequencyPresets.StandardDefName);
            AddFrequencyPresetForUi(result, DiaryFrequencyPresets.FrequentDefName);
            eventFilterFrequencyPresetDefs = result;
            return eventFilterFrequencyPresetDefs;
        }

        private static void AddFrequencyPresetForUi(
            List<DiaryFrequencyPresetDef> result,
            string defName)
        {
            DiaryFrequencyPresetDef def =
                DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                result.Add(def);
            }
        }

        private static float UiMetric(float configured, float minimum = 1f)
        {
            if (float.IsNaN(configured) || float.IsInfinity(configured))
            {
                return minimum;
            }

            return Mathf.Max(minimum, configured);
        }

        private static float WrappedTextHeight(string text, float width, GameFont font)
        {
            GameFont previousFont = Text.Font;
            Text.Font = font;
            float minimum = Text.LineHeight;
            float height = Text.CalcHeight(text ?? string.Empty, Mathf.Max(1f, width));
            Text.Font = previousFont;
            return Mathf.Max(minimum, height);
        }

        private static void DrawWrappedMutedLabel(Rect rect, string text, GameFont font)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = HintColor;
            Widgets.Label(rect, text ?? string.Empty);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static DiaryFrequencyPresetDef SelectedFrequencyPresetDef()
        {
            DiaryFrequencyPresetDef selected =
                DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(
                    Settings.frequencyPresetDefName);
            return selected
                ?? DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(
                    DiaryFrequencyPresets.StandardDefName);
        }

        private static string FrequencyPresetLabel(DiaryFrequencyPresetDef preset)
        {
            return preset != null
                ? preset.LabelCap.Resolve()
                : "PawnDiary.Settings.EventFilters.PresetFallback".Translate().ToString();
        }

        // Internal so the public integration API exposes the exact same complete event-filter set the
        // Events tab owns. Search and collapsed headings are presentation only and never narrow this list.
        internal static List<DiaryInteractionGroupDef> EventFilterGroupsForSettings()
        {
            List<DiaryInteractionGroupDef> result = new List<DiaryInteractionGroupDef>();
            List<DiaryInteractionGroupDef> all = InteractionGroups.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsSettingsEventFilterGroup(all[i]))
                {
                    result.Add(all[i]);
                }
            }

            result.Sort(CompareEventFilterGroups);
            return result;
        }

        /// <summary>
        /// True when a group belongs to automatic-capture settings. All non-External, runtime-available
        /// groups qualify, including default-disabled rows a player or adapter may opt into.
        /// </summary>
        internal static bool IsSettingsEventFilterGroup(DiaryInteractionGroupDef group)
        {
            return group != null
                && group.domain != GroupDomain.External
                && !group.UnavailableForCurrentRuntime();
        }

        private static int CompareEventFilterGroups(
            DiaryInteractionGroupDef left,
            DiaryInteractionGroupDef right)
        {
            int domain = left.domain.CompareTo(right.domain);
            return domain != 0
                ? domain
                : string.Compare(
                    EventFilterLabel(left),
                    EventFilterLabel(right),
                    StringComparison.OrdinalIgnoreCase);
        }

        // Internal so IntegrationApiSettings labels event-filter snapshots exactly like the Events tab.
        internal static string EventFilterLabel(DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            string label = group.LabelCap.Resolve();
            return string.IsNullOrWhiteSpace(label) ? group.defName ?? string.Empty : label;
        }

        private static int DisabledVisibleEventFilterCount(List<DiaryInteractionGroupDef> groups)
        {
            if (groups == null || Settings == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group != null && !Settings.IsGroupEnabled(group.defName))
                {
                    count++;
                }
            }

            return count;
        }

        private static void EnableVisibleEventFilters(List<DiaryInteractionGroupDef> groups)
        {
            if (groups == null || Settings == null)
            {
                return;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group != null)
                {
                    Settings.SetGroupEnabled(group.defName, true);
                }
            }
        }
    }
}
