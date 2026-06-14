// Colony-wide timeline window opened from a main-button tab. Read-only aggregation over all
// recorded DiaryEvents with lightweight filters, paging, and row actions.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public class MainTabWindow_DiaryTimeline : MainTabWindow
    {
        private const int PageSize = 30;
        private const int TicksPerDay = 60000;
        private const string SmallTalkContextToken = "group=smalltalk";

        private enum TimelineStatusFilter
        {
            All,
            Generated,
            Raw,
            Failed
        }

        private enum TimelineDateRange
        {
            All,
            Last1Day,
            Last3Days,
            Last7Days,
            Last15Days,
            Last30Days
        }

        private sealed class TimelineRow
        {
            public string EventId;
            public int Tick;
            public string Date;
            public string GroupKey;
            public string GroupLabel;
            public string InteractionLabel;
            public string PawnId;
            public string PawnName;
            public string OtherPawnName;
            public string PovRole;
            public string RawText;
            public string GeneratedText;
            public string Status;
        }

        private Vector2 scrollPosition;
        private string selectedPawnId;
        private string selectedGroupKey;
        private TimelineStatusFilter statusFilter;
        private TimelineDateRange dateRange = TimelineDateRange.All;
        private string searchText = string.Empty;
        private int pageIndex;

        private int cacheBuiltAtTick = -9999;
        private int cacheEventCount = -1;
        private readonly List<TimelineRow> cachedRows = new List<TimelineRow>();

        public override Vector2 RequestedTabSize => new Vector2(1120f, 760f);

        public override void DoWindowContents(Rect inRect)
        {
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (component == null)
            {
                Widgets.Label(inRect, "PawnDiaryTimeline_NoGameLoaded".Translate());
                return;
            }

            RebuildCacheIfNeeded(component);

            Rect content = inRect.ContractedBy(8f);
            float y = content.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(content.x, y, content.width, 34f), "PawnDiaryTimeline_Title".Translate());
            Text.Font = GameFont.Small;
            y += 38f;

            Rect filtersRect = new Rect(content.x, y, content.width, 58f);
            DrawFilters(filtersRect);
            y += filtersRect.height + 6f;

            List<TimelineRow> filtered = ApplyFilters(cachedRows);
            int totalPages = Math.Max(1, Mathf.CeilToInt(filtered.Count / (float)PageSize));
            pageIndex = Mathf.Clamp(pageIndex, 0, totalPages - 1);

            Rect pagingRect = new Rect(content.x, y, content.width, 26f);
            DrawPagingControls(pagingRect, filtered.Count, totalPages);
            y += pagingRect.height + 6f;

            Rect listRect = new Rect(content.x, y, content.width, content.yMax - y);
            DrawList(component, listRect, filtered, pageIndex, totalPages);
        }

        private void DrawFilters(Rect rect)
        {
            float firstRowY = rect.y;
            float secondRowY = firstRowY + 30f;
            float buttonWidth = Math.Max(140f, (rect.width - 12f) / 4f);

            if (Widgets.ButtonText(new Rect(rect.x, firstRowY, buttonWidth, 26f),
                "PawnDiaryTimeline_FilterPawn".Translate(PawnLabelForSelection())))
            {
                OpenPawnFilterMenu();
            }

            if (Widgets.ButtonText(new Rect(rect.x + buttonWidth + 4f, firstRowY, buttonWidth, 26f),
                "PawnDiaryTimeline_FilterGroup".Translate(GroupLabelForSelection())))
            {
                OpenGroupFilterMenu();
            }

            if (Widgets.ButtonText(new Rect(rect.x + (buttonWidth + 4f) * 2f, firstRowY, buttonWidth, 26f),
                "PawnDiaryTimeline_FilterStatus".Translate(StatusLabel(statusFilter))))
            {
                OpenStatusFilterMenu();
            }

            if (Widgets.ButtonText(new Rect(rect.x + (buttonWidth + 4f) * 3f, firstRowY, buttonWidth, 26f),
                "PawnDiaryTimeline_FilterDate".Translate(DateRangeLabel(dateRange))))
            {
                OpenDateFilterMenu();
            }

            Widgets.Label(new Rect(rect.x, secondRowY + 3f, 56f, 24f), "PawnDiaryTimeline_Search".Translate());
            searchText = Widgets.TextField(
                new Rect(rect.x + 58f, secondRowY, Math.Max(220f, rect.width - 58f), 26f),
                searchText ?? string.Empty);
        }

        private void DrawPagingControls(Rect rect, int totalCount, int totalPages)
        {
            Widgets.Label(new Rect(rect.x, rect.y + 3f, rect.width * 0.45f, rect.height),
                "PawnDiaryTimeline_ResultCount".Translate(totalCount));

            float controlsWidth = 220f;
            Rect controlsRect = new Rect(rect.xMax - controlsWidth, rect.y, controlsWidth, rect.height);
            if (Widgets.ButtonText(new Rect(controlsRect.x, controlsRect.y, 68f, 24f), "PawnDiaryTimeline_PagePrev".Translate()))
            {
                pageIndex = Math.Max(0, pageIndex - 1);
                scrollPosition = Vector2.zero;
            }

            Widgets.Label(new Rect(controlsRect.x + 72f, controlsRect.y + 3f, 76f, 24f),
                "PawnDiaryTimeline_Page".Translate(pageIndex + 1, totalPages));

            if (Widgets.ButtonText(new Rect(controlsRect.x + 152f, controlsRect.y, 68f, 24f), "PawnDiaryTimeline_PageNext".Translate()))
            {
                pageIndex = Math.Min(totalPages - 1, pageIndex + 1);
                scrollPosition = Vector2.zero;
            }
        }

        private void DrawList(DiaryGameComponent component, Rect rect, List<TimelineRow> rows, int page, int totalPages)
        {
            if (rows.Count == 0)
            {
                Widgets.DrawMenuSection(rect);
                Widgets.Label(rect.ContractedBy(8f), "PawnDiaryTimeline_NoResults".Translate());
                return;
            }

            int start = page * PageSize;
            int count = Math.Min(PageSize, rows.Count - start);
            List<TimelineRow> pageRows = rows.GetRange(start, count);

            float viewWidth = rect.width - 16f;
            float viewHeight = pageRows.Sum(row => RowHeight(row, viewWidth)) + 8f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < pageRows.Count; i++)
            {
                TimelineRow row = pageRows[i];
                float rowHeight = RowHeight(row, viewRect.width);
                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight);
                Widgets.DrawMenuSection(rowRect);
                DrawRow(component, rowRect, row);
                y += rowHeight + 8f;
            }

            Widgets.EndScrollView();
        }

        private void DrawRow(DiaryGameComponent component, Rect rect, TimelineRow row)
        {
            string statusLabel = StatusLabelFromEvent(row.Status, row.GeneratedText);
            string header = string.IsNullOrWhiteSpace(row.OtherPawnName)
                ? "PawnDiaryTimeline_HeaderSolo".Translate(row.Date, row.PawnName, row.InteractionLabel, row.GroupLabel)
                : "PawnDiaryTimeline_HeaderWithOther".Translate(row.Date, row.PawnName, row.OtherPawnName, row.InteractionLabel, row.GroupLabel);
            if (!string.IsNullOrWhiteSpace(statusLabel))
            {
                header = "PawnDiaryTimeline_HeaderWithStatus".Translate(header, statusLabel);
            }

            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 200f, 24f), header);

            string bodyText = string.IsNullOrWhiteSpace(row.GeneratedText) ? (row.RawText ?? string.Empty) : row.GeneratedText;
            float textHeight = Text.CalcHeight(bodyText, rect.width - 16f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, textHeight), bodyText);

            float buttonsY = rect.y + rect.height - 30f;
            if (Widgets.ButtonText(new Rect(rect.x + 8f, buttonsY, 120f, 24f), "PawnDiaryTimeline_Jump".Translate()))
            {
                JumpToPawn(row.PawnId);
            }

            bool canRetry = string.Equals(row.Status, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase);
            bool retryPressed = Widgets.ButtonText(new Rect(rect.x + 134f, buttonsY, 180f, 24f), "PawnDiaryTimeline_Retry".Translate());
            if (retryPressed && canRetry)
            {
                bool queued = component.RetryFailedGeneration(row.EventId, row.PovRole);
                Messages.Message(
                    queued ? "PawnDiaryTimeline_RetryQueued".Translate() : "PawnDiaryTimeline_RetryUnavailable".Translate(),
                    queued ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        private static float RowHeight(TimelineRow row, float width)
        {
            float bodyHeight = Text.CalcHeight(string.IsNullOrWhiteSpace(row.GeneratedText) ? (row.RawText ?? string.Empty) : row.GeneratedText, width - 16f);
            return 72f + bodyHeight;
        }

        private void RebuildCacheIfNeeded(DiaryGameComponent component)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            IReadOnlyList<DiaryEvent> events = component.AllEvents();
            int eventCount = events?.Count ?? 0;
            if (eventCount == cacheEventCount && now - cacheBuiltAtTick < 120)
            {
                return;
            }

            cachedRows.Clear();
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    DiaryEvent diaryEvent = events[i];
                    if (diaryEvent == null)
                    {
                        continue;
                    }

                    AddRowForRole(diaryEvent, DiaryEvent.InitiatorRole);
                    if (!diaryEvent.solo && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                    {
                        AddRowForRole(diaryEvent, DiaryEvent.RecipientRole);
                    }
                }
            }

            cacheBuiltAtTick = now;
            cacheEventCount = eventCount;
        }

        private void AddRowForRole(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            string pawnId = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? diaryEvent.recipientPawnId
                : diaryEvent.initiatorPawnId;
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            DiaryInteractionGroupDef group = ClassifyGroupForEvent(diaryEvent);
            cachedRows.Add(new TimelineRow
            {
                EventId = diaryEvent.eventId,
                Tick = diaryEvent.tick,
                Date = diaryEvent.date,
                GroupKey = group?.defName ?? "other",
                GroupLabel = group?.label ?? "Other",
                InteractionLabel = string.IsNullOrWhiteSpace(diaryEvent.interactionLabel) ? diaryEvent.interactionDefName : diaryEvent.interactionLabel,
                PawnId = pawnId,
                PawnName = string.IsNullOrWhiteSpace(diaryEvent.NameForRole(povRole)) ? "PawnDiaryTimeline_UnknownPawn".Translate() : diaryEvent.NameForRole(povRole),
                OtherPawnName = diaryEvent.OtherNameForRole(povRole),
                PovRole = povRole,
                RawText = diaryEvent.TextForRole(povRole),
                GeneratedText = diaryEvent.DisplayTextForRole(povRole) == diaryEvent.TextForRole(povRole) ? string.Empty : diaryEvent.DisplayTextForRole(povRole),
                Status = diaryEvent.StatusForRole(povRole)
            });
        }

        private List<TimelineRow> ApplyFilters(List<TimelineRow> rows)
        {
            IEnumerable<TimelineRow> query = rows.OrderByDescending(row => row.Tick);

            if (!string.IsNullOrWhiteSpace(selectedPawnId))
            {
                query = query.Where(row => row.PawnId == selectedPawnId);
            }

            if (!string.IsNullOrWhiteSpace(selectedGroupKey))
            {
                query = query.Where(row => string.Equals(row.GroupKey, selectedGroupKey, StringComparison.OrdinalIgnoreCase));
            }

            switch (statusFilter)
            {
                case TimelineStatusFilter.Generated:
                    query = query.Where(IsGenerated);
                    break;
                case TimelineStatusFilter.Raw:
                    query = query.Where(row => !IsGenerated(row) && !IsFailed(row));
                    break;
                case TimelineStatusFilter.Failed:
                    query = query.Where(IsFailed);
                    break;
            }

            int cutoffTick = CutoffTick(rows);
            if (cutoffTick > 0)
            {
                query = query.Where(row => row.Tick >= cutoffTick);
            }

            string needle = (searchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(needle))
            {
                query = query.Where(row =>
                    ContainsIgnoreCase(row.RawText, needle)
                    || ContainsIgnoreCase(row.GeneratedText, needle));
            }

            return query.ToList();
        }

        private int CutoffTick(List<TimelineRow> rows)
        {
            int days = DaysFor(dateRange);
            if (days <= 0 || rows.Count == 0)
            {
                return -1;
            }

            int newest = rows.Max(row => row.Tick);
            return Math.Max(0, newest - days * TicksPerDay);
        }

        private static int DaysFor(TimelineDateRange range)
        {
            switch (range)
            {
                case TimelineDateRange.Last1Day: return 1;
                case TimelineDateRange.Last3Days: return 3;
                case TimelineDateRange.Last7Days: return 7;
                case TimelineDateRange.Last15Days: return 15;
                case TimelineDateRange.Last30Days: return 30;
                default: return 0;
            }
        }

        private void OpenPawnFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryTimeline_All".Translate(), delegate
                {
                    selectedPawnId = null;
                    pageIndex = 0;
                })
            };

            foreach (IGrouping<string, TimelineRow> grouping in cachedRows
                         .Where(row => !string.IsNullOrWhiteSpace(row.PawnId))
                         .GroupBy(row => row.PawnId)
                         .OrderBy(group => group.First().PawnName))
            {
                string pawnId = grouping.Key;
                string label = grouping.First().PawnName;
                options.Add(new FloatMenuOption(label, delegate
                {
                    selectedPawnId = pawnId;
                    pageIndex = 0;
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenGroupFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryTimeline_All".Translate(), delegate
                {
                    selectedGroupKey = null;
                    pageIndex = 0;
                })
            };

            foreach (IGrouping<string, TimelineRow> grouping in cachedRows
                         .Where(row => !string.IsNullOrWhiteSpace(row.GroupKey))
                         .GroupBy(row => row.GroupKey)
                         .OrderBy(group => group.First().GroupLabel))
            {
                string groupKey = grouping.Key;
                string label = grouping.First().GroupLabel;
                options.Add(new FloatMenuOption(label, delegate
                {
                    selectedGroupKey = groupKey;
                    pageIndex = 0;
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenStatusFilterMenu()
        {
            List<FloatMenuOption> options = Enum.GetValues(typeof(TimelineStatusFilter))
                .Cast<TimelineStatusFilter>()
                .Select(filter => new FloatMenuOption(StatusLabel(filter), delegate
                {
                    statusFilter = filter;
                    pageIndex = 0;
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenDateFilterMenu()
        {
            List<FloatMenuOption> options = Enum.GetValues(typeof(TimelineDateRange))
                .Cast<TimelineDateRange>()
                .Select(filter => new FloatMenuOption(DateRangeLabel(filter), delegate
                {
                    dateRange = filter;
                    pageIndex = 0;
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string PawnLabelForSelection()
        {
            if (string.IsNullOrWhiteSpace(selectedPawnId))
            {
                return "PawnDiaryTimeline_All".Translate();
            }

            TimelineRow match = cachedRows.FirstOrDefault(row => row.PawnId == selectedPawnId);
            return match?.PawnName ?? "PawnDiaryTimeline_All".Translate();
        }

        private string GroupLabelForSelection()
        {
            if (string.IsNullOrWhiteSpace(selectedGroupKey))
            {
                return "PawnDiaryTimeline_All".Translate();
            }

            TimelineRow match = cachedRows.FirstOrDefault(row => row.GroupKey == selectedGroupKey);
            return match?.GroupLabel ?? "PawnDiaryTimeline_All".Translate();
        }

        private static string StatusLabel(TimelineStatusFilter filter)
        {
            switch (filter)
            {
                case TimelineStatusFilter.Generated: return "PawnDiaryTimeline_StatusGenerated".Translate();
                case TimelineStatusFilter.Raw: return "PawnDiaryTimeline_StatusRaw".Translate();
                case TimelineStatusFilter.Failed: return "PawnDiaryTimeline_StatusFailed".Translate();
                default: return "PawnDiaryTimeline_All".Translate();
            }
        }

        private static string DateRangeLabel(TimelineDateRange range)
        {
            switch (range)
            {
                case TimelineDateRange.Last1Day: return "PawnDiaryTimeline_DateLast1".Translate();
                case TimelineDateRange.Last3Days: return "PawnDiaryTimeline_DateLast3".Translate();
                case TimelineDateRange.Last7Days: return "PawnDiaryTimeline_DateLast7".Translate();
                case TimelineDateRange.Last15Days: return "PawnDiaryTimeline_DateLast15".Translate();
                case TimelineDateRange.Last30Days: return "PawnDiaryTimeline_DateLast30".Translate();
                default: return "PawnDiaryTimeline_AllDates".Translate();
            }
        }

        private static string StatusLabelFromEvent(string status, string generatedText)
        {
            if (IsFailed(status))
            {
                return "PawnDiaryTimeline_StatusFailed".Translate();
            }

            if (IsGenerated(status, generatedText))
            {
                return "PawnDiaryTimeline_StatusGenerated".Translate();
            }

            return "PawnDiaryTimeline_StatusRaw".Translate();
        }

        private static bool IsGenerated(TimelineRow row)
        {
            return IsGenerated(row.Status, row.GeneratedText);
        }

        private static bool IsGenerated(string status, string generatedText)
        {
            return !string.IsNullOrWhiteSpace(generatedText)
                || string.Equals(status, DiaryEvent.CompleteStatus, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFailed(TimelineRow row)
        {
            return IsFailed(row.Status);
        }

        private static bool IsFailed(string status)
        {
            return string.Equals(status, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string needle)
        {
            return !string.IsNullOrWhiteSpace(text)
                && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static DiaryInteractionGroupDef ClassifyGroupForEvent(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(diaryEvent.gameContext)
                && diaryEvent.gameContext.IndexOf(SmallTalkContextToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return InteractionGroups.ByKey("smalltalk");
            }

            InteractionDef interactionDef = DefDatabase<InteractionDef>.GetNamedSilentFail(diaryEvent.interactionDefName);
            if (interactionDef != null)
            {
                return InteractionGroups.Classify(interactionDef);
            }

            MentalStateDef mentalDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(diaryEvent.interactionDefName);
            if (mentalDef != null)
            {
                return InteractionGroups.ClassifyMentalState(mentalDef);
            }

            List<DiaryInteractionGroupDef> all = InteractionGroups.All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain == GroupDomain.Interaction && group.Matches(diaryEvent.interactionDefName))
                {
                    return group;
                }
            }

            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain == GroupDomain.MentalState && group.Matches(diaryEvent.interactionDefName))
                {
                    return group;
                }
            }

            return null;
        }

        private static void JumpToPawn(string pawnId)
        {
            Pawn pawn = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
                .FirstOrDefault(candidate => candidate.GetUniqueLoadID() == pawnId);
            if (pawn == null)
            {
                Messages.Message("PawnDiaryTimeline_PawnUnavailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            CameraJumper.TryJumpAndSelect(pawn);
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect);
        }
    }
}
