// Year-paging and pending-scroll helpers for the Diary tab. Split from ITab_Pawn_Diary.cs with no behavior change.
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
        /// Draws the one-year-at-a-time pager above the diary list. The buttons move through years
        /// that actually have visible entries, newest first; all entries for the selected year are
        /// shown in the scroll view below.
        /// </summary>
        private void DrawYearFilter(Rect rect, List<int> years, DiaryTabVisibleEntriesCache entriesCache)
        {
            if (years == null || years.Count <= 1)
            {
                return;
            }

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            int index = years.IndexOf(selectedYear);
            if (index < 0)
            {
                index = 0;
                SelectYear(years[0]);
            }

            // Prev/next arrows appear only when the row is wide enough for them plus a readable center
            // label. In the narrow filter panel the row collapses to a single full-width year dropdown.
            bool showPager = inner.width >= (YearButtonWidth * 2f + 64f);
            Rect labelRect = inner;
            if (showPager)
            {
                Rect newerRect = new Rect(inner.x, inner.y, YearButtonWidth, inner.height);
                Rect olderRect = new Rect(inner.xMax - YearButtonWidth, inner.y, YearButtonWidth, inner.height);
                labelRect = new Rect(newerRect.xMax + 8f, inner.y, olderRect.x - newerRect.xMax - 16f, inner.height);
                if (DrawYearButton(newerRect, "PawnDiary.Tab.NewerYear", index > 0))
                {
                    SelectYear(years[index - 1]);
                }

                if (DrawYearButton(olderRect, "PawnDiary.Tab.OlderYear", index < years.Count - 1))
                {
                    SelectYear(years[index + 1]);
                }
            }

            int entryCount = entriesCache == null ? 0 : entriesCache.CountForYear(selectedYear);
            if (Widgets.ButtonText(labelRect, "PawnDiary.Tab.YearFilter".Translate(YearLabel(selectedYear), entryCount)))
            {
                ShowYearFloatMenu(years, entriesCache);
            }

            TooltipHandler.TipRegion(labelRect, "PawnDiary.Tab.YearSelectorTip".Translate());
        }

        /// <summary>
        /// Opens the year picker: a FloatMenu listing every year that has visible entries, newest
        /// first, each with its page count. Shared by the pager and the filter panel's dropdown.
        /// </summary>
        private void ShowYearFloatMenu(List<int> years, DiaryTabVisibleEntriesCache entriesCache)
        {
            if (years == null || years.Count == 0)
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < years.Count; i++)
            {
                int optionYear = years[i];
                string label = "PawnDiary.Tab.YearFilter".Translate(
                    YearLabel(optionYear),
                    entriesCache == null ? 0 : entriesCache.CountForYear(optionYear)).ToString();
                options.Add(new FloatMenuOption(label, delegate
                {
                    SelectYear(optionYear);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }



        /// <summary>
        /// Draws a pager button with a disabled visual state while keeping all text localizable.
        /// </summary>
        private static bool DrawYearButton(Rect rect, string labelKey, bool enabled)
        {

            bool oldEnabled = GUI.enabled;

            Color oldColor = GUI.color;

            GUI.enabled = enabled;

            if (!enabled)
            {

                GUI.color = UiStyle.YearDisabledColor;

            }



            bool clicked = Widgets.ButtonText(rect, labelKey.Translate());

            GUI.enabled = oldEnabled;

            GUI.color = oldColor;

            return enabled && clicked;

        }



        /// <summary>
        /// Converts a year key into the short label shown by the pager.
        /// </summary>
        private static string YearLabel(int year)
        {

            if (year == UnknownYear)
            {

                return "PawnDiary.Tab.UnknownYear".Translate().ToString();

            }



            return "PawnDiary.Tab.YearLabel".Translate(year).ToString();

        }



        /// <summary>
        /// Changes the visible diary year and returns the scroll position to the top of that year.
        /// </summary>
        private void SelectYear(int year)
        {
            // Tag chips describe one year's available group labels. Clear them at the transition so a
            // tag absent from the destination year cannot remain active while exposing no chip to undo it.
            if (selectedYear != year)
            {
                filterActiveTags.Clear();
            }
            selectedYear = year;
            scrollPosition.y = 0f;
        }



        /// <summary>
        /// Keeps the selected year valid for the current pawn. New pawns open on their newest
        /// available year, which is the first item because <see cref="YearsFor"/> sorts descending.
        /// </summary>
        private void EnsureSelectedYear(Pawn pawn, List<int> years)
        {

            if (years == null || years.Count == 0)
            {

                SelectYear(UnknownYear);

                yearFilterPawnId = null;

                scrollPosition.y = 0f;

                return;

            }



            string pawnId = pawn?.GetUniqueLoadID();

            if (yearFilterPawnId != pawnId || !years.Contains(selectedYear))
            {

                yearFilterPawnId = pawnId;

                SelectYear(years[0]);

            }

        }



        /// <summary>
        /// A Social-tab or linked-entry jump may target an older year. Switch the pager first so
        /// TryApplyPendingScroll can find the event in the filtered list and place it on screen.
        /// </summary>
        private void SelectYearForPendingScroll(Pawn pawn, DiaryTabVisibleEntriesCache entriesCache)
        {

            if (pawn == null || entriesCache == null || string.IsNullOrWhiteSpace(pendingScrollPawnId) || string.IsNullOrWhiteSpace(pendingScrollEventId))
            {

                return;

            }



            if (pawn.GetUniqueLoadID() != pendingScrollPawnId)
            {

                return;

            }



            int year;
            if (entriesCache.TryGetYearForEvent(pendingScrollEventId, out year))
            {

                SelectYear(year);

                yearFilterPawnId = pendingScrollPawnId;

                return;

            }

        }



        /// <summary>
        /// Returns the distinct in-game years represented by the entries, newest first. Entries with
        /// a missing/malformed display date use an "undated" page so old saves remain reachable
        /// instead of disappearing.
        /// </summary>
        private static List<int> YearsFor(List<DiaryEntryView> entries)
        {

            List<int> years = new List<int>();

            if (entries == null)
            {

                years.Add(UnknownYear);

                return years;

            }



            for (int i = 0; i < entries.Count; i++)
            {

                int year = EntryYear(entries[i]);

                if (!years.Contains(year))
                {

                    years.Add(year);

                }

            }



            if (years.Count == 0)
            {

                years.Add(UnknownYear);

            }



            years.Sort((left, right) => right.CompareTo(left));

            return years;

        }



        /// <summary>
        /// Counts the entries that will appear on a year page for the pager label.
        /// </summary>
        private static int CountEntriesForYear(List<DiaryEntryView> entries, int year)
        {

            if (entries == null)
            {

                return 0;

            }



            int count = 0;

            for (int i = 0; i < entries.Count; i++)
            {

                if (EntryYear(entries[i]) == year)
                {

                    count++;

                }

            }



            return count;

        }



        /// <summary>
        /// Extracts the game year from the saved display date. Existing saves store game ticks
        /// (`TicksGame`) plus this formatted date, not absolute ticks, so the date is the stable
        /// old-save-compatible source for year paging.
        /// </summary>
        private static int EntryYear(DiaryEntryView entry)
        {

            return ExtractYear(entry?.Date);

        }



        /// <summary>
        /// Finds the final run of digits in RimWorld's full date string (for example the 5503 in
        /// "1st of Aprimay, 5503"). Reading from the end keeps this tolerant of localized month names
        /// and day ordinals earlier in the string.
        /// </summary>
        private static int ExtractYear(string date)
        {

            if (string.IsNullOrWhiteSpace(date))
            {

                return UnknownYear;

            }



            int end = -1;

            for (int i = date.Length - 1; i >= 0; i--)
            {

                if (char.IsDigit(date[i]))
                {

                    end = i;

                    break;

                }

            }



            if (end < 0)
            {

                return UnknownYear;

            }



            int start = end;

            while (start > 0 && char.IsDigit(date[start - 1]))
            {

                start--;

            }



            string yearText = date.Substring(start, end - start + 1);

            int year;

            if (int.TryParse(yearText, out year))
            {

                return year;

            }



            return UnknownYear;

        }



        /// <summary>
        /// Consumes a pending Social-tab jump request by placing the target card near the top
        /// of the scroll view. If the entry disappeared before redraw, clear the stale request.
        /// </summary>
        private void TryApplyPendingScroll(Pawn pawn, List<DiaryEntryView> ordered, float[] offsets, float viewHeight, float outHeight)
        {

            if (pawn == null || string.IsNullOrWhiteSpace(pendingScrollPawnId) || string.IsNullOrWhiteSpace(pendingScrollEventId))
            {

                return;

            }



            string pawnId = pawn.GetUniqueLoadID();

            if (pawnId != pendingScrollPawnId)
            {

                return;

            }



            for (int i = 0; i < ordered.Count; i++)
            {

                DiaryEntryView entry = ordered[i];

                if (entry != null && entry.EventId == pendingScrollEventId)
                {

                    SetEntryExpanded(entry, true, 1f);

                    float maxScroll = Mathf.Max(0f, viewHeight - outHeight);

                    scrollPosition.y = Mathf.Clamp(offsets[i], 0f, maxScroll);

                    ClearPendingScrollRequest();

                    return;

                }



            }



            ClearPendingScrollRequest();

        }
    }
}
