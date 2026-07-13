// Events-tab filter UI for Pawn Diary. These controls edit saved per-event-group automatic
// capture toggles; they do not edit XML prompt policy and they do not affect external API requests.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float EventFilterTitleHeight = 28f;
        private const float EventFilterHelpHeight = 44f;
        private const float EventFilterButtonWidth = 118f;
        private const float EventFilterRowHeight = 28f;
        private const float EventFilterRowGap = 3f;
        private Vector2 eventFilterScrollPosition;

        /// <summary>
        /// Draws the saved automatic-capture filter list on the Events tab.
        /// </summary>
        private void DrawEventFilterPanel(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            List<DiaryInteractionGroupDef> groups = EventFilterGroupsForSettings();
            if (groups == null)
            {
                Verse.Log.Error("Event filter groups cannot be null");
                return;
            }

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            Rect titleRect = new Rect(inner.x, y, inner.width - EventFilterButtonWidth - 8f, EventFilterTitleHeight);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            if (!string.IsNullOrEmpty("PawnDiary.Settings.EventFilters.Title".Translate()))
            {
                Widgets.LabelFit(titleRect, "PawnDiary.Settings.EventFilters.Title".Translate());
            }
            Text.Font = previousFont;

            Rect enableAllRect = new Rect(inner.xMax - EventFilterButtonWidth, y, EventFilterButtonWidth, 28f);
            if (ButtonTextFit(enableAllRect, "PawnDiary.Settings.EventFilters.EnableAll".Translate()) && groups.Count > 0)
            {
                EnableVisibleEventFilters(groups);
            }

            TooltipHandler.TipRegion(enableAllRect, "PawnDiary.Settings.EventFilters.EnableAllTip".Translate());
            y += EventFilterTitleHeight + 2f;

            Rect summaryRect = new Rect(inner.x, y, inner.width, 22f);
            string summary = "PawnDiary.Settings.EventFilters.Summary".Translate(
                DisabledVisibleEventFilterCount(groups),
                groups.Count).ToString();
            if (!string.IsNullOrEmpty(summary))
            {
                DrawMutedLabel(summaryRect, summary);
            }
            y += 24f;

            Rect helpRect = new Rect(inner.x, y, inner.width, EventFilterHelpHeight);
            string helpText = "PawnDiary.Settings.EventFilters.Help".Translate().ToString();
            if (!string.IsNullOrEmpty(helpText))
            {
                DrawMutedLabel(helpRect, helpText);
            }
            y += EventFilterHelpHeight + 6f;

            Rect listRect = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));
            DrawEventFilterRows(listRect, groups);
        }

        private void DrawEventFilterRows(Rect rect, List<DiaryInteractionGroupDef> groups)
        {
            if (rect.height <= 0f)
            {
                return;
            }

            if (groups.Count == 0)
            {
                Widgets.Label(rect, "PawnDiary.Settings.EventFilters.None".Translate());
                return;
            }

            float contentHeight = groups.Count * (EventFilterRowHeight + EventFilterRowGap);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref eventFilterScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < groups.Count; i++)
                {
                    DiaryInteractionGroupDef group = groups[i];
                    Rect rowRect = new Rect(0f, y, viewRect.width, EventFilterRowHeight);
                    DrawEventFilterRow(rowRect, group);
                    y += EventFilterRowHeight + EventFilterRowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawEventFilterRow(Rect rect, DiaryInteractionGroupDef group)
        {
            if (group == null)
            {
                return;
            }

            bool enabled = Settings.IsGroupEnabled(group.defName);
            bool edited = enabled;
            Rect checkboxRect = Settings.HasGroupEnabledOverride(group.defName)
                ? new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 78f), rect.height)
                : rect;
            Widgets.CheckboxLabeled(checkboxRect, EventFilterLabel(group), ref edited);
            if (edited != enabled)
            {
                Settings.SetGroupEnabled(group.defName, edited);
            }

            if (Settings.HasGroupEnabledOverride(group.defName))
            {
                Rect changedRect = new Rect(rect.xMax - 72f, rect.y + 2f, 70f, rect.height - 4f);
                DrawMutedLabel(changedRect, "PawnDiary.Settings.EventFilters.Changed".Translate().ToString());
            }

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.EventFilters.RowTip".Translate(group.defName).ToString());
        }

        // Internal so the public integration API (IntegrationApiSettings) exposes the exact same
        // event-filter set the Events tab shows, with no risk of the two lists drifting apart.
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
        /// True when a group belongs to the automatic-capture "Events" filter list. All non-External,
        /// runtime-available groups qualify — including <c>defaultEnabled=false</c> rows like
        /// questAccepted, so a player (or an adapter) can opt INTO a group the XML ships disabled.
        /// External-domain groups are deliberately excluded: their capture is governed by the master
        /// integration switch and adapter XML, not by this filter list. Package-gated groups are
        /// excluded because they are inert by design without their target mod or while a richer
        /// capture capability owns their event stream.
        /// </summary>
        internal static bool IsSettingsEventFilterGroup(DiaryInteractionGroupDef group)
        {
            return group != null
                && group.domain != GroupDomain.External
                && !group.UnavailableForCurrentRuntime();
        }

        private static int CompareEventFilterGroups(DiaryInteractionGroupDef left, DiaryInteractionGroupDef right)
        {
            int domain = left.domain.CompareTo(right.domain);
            if (domain != 0)
            {
                return domain;
            }

            return string.Compare(EventFilterLabel(left), EventFilterLabel(right), StringComparison.OrdinalIgnoreCase);
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
