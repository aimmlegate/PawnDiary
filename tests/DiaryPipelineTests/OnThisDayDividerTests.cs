// "On this day" divider placement rules (Quality Wave §6, H5 UI).
//
// The Diary tab marks the page that fell on today's calendar day in an earlier year with one quiet
// divider row. Everything that decides WHERE that row goes is pure arithmetic in
// Source/Pipeline/OnThisDayDividerPolicy.cs, so it is verified here without RimWorld: day-of-year
// math and year boundaries, the fail-closed rules (current year, fabricated/dev-mock dates, corrupt
// ticks), the "only the first matching row wins" scan the sliced layout build relies on, and the
// vertical space one divider row reserves above its card.
using System;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        // RimWorld's fixed calendar, restated here so an accidental edit to the policy constants shows
        // up as a failing test rather than a silently shifted divider.
        private const int TicksPerDay = 60000;
        private const int DaysPerYear = 60;
        private const int TicksPerYear = TicksPerDay * DaysPerYear;

        private static void TestOnThisDayDividerPolicy()
        {
            AssertEqual("H5-UI policy mirrors GenDate.TicksPerDay",
                TicksPerDay, OnThisDayDividerPolicy.TicksPerDay);
            AssertEqual("H5-UI policy mirrors GenDate.DaysPerYear",
                DaysPerYear, OnThisDayDividerPolicy.DaysPerYear);

            TestOnThisDayDayOfYearMath();
            TestOnThisDayViewedYearGate();
            TestOnThisDayRowMatching();
            TestOnThisDayFirstMatchOnlyScan();
            TestOnThisDayReservedHeight();
        }

        // --- Day-of-year math, including both ends of a year and every fail-closed tick ---------------
        private static void TestOnThisDayDayOfYearMath()
        {
            AssertEqual("H5-UI first day of a year is day 0",
                0, OnThisDayDividerPolicy.DayOfYear(0));
            AssertEqual("H5-UI a partial day still reports its own day",
                0, OnThisDayDividerPolicy.DayOfYear(TicksPerDay - 1));
            AssertEqual("H5-UI last day of a year is day 59",
                DaysPerYear - 1, OnThisDayDividerPolicy.DayOfYear(TicksPerYear - 1));
            AssertEqual("H5-UI the year boundary wraps back to day 0",
                0, OnThisDayDividerPolicy.DayOfYear(TicksPerYear));
            AssertEqual("H5-UI day 12 of a later year is still day 12",
                12, OnThisDayDividerPolicy.DayOfYear((3L * TicksPerYear) + (12L * TicksPerDay)));

            // Game tick -> absolute tick uses the caller's offset, exactly like DayIndexForGameTick.
            AssertEqual("H5-UI the tick offset shifts the day of year",
                59, OnThisDayDividerPolicy.DayOfYear(
                    OnThisDayDividerPolicy.AbsoluteTick(100, TicksPerYear - TicksPerDay)));

            AssertTrue("H5-UI a negative stored tick is not a calendar position",
                OnThisDayDividerPolicy.AbsoluteTick(-1, TicksPerYear) == OnThisDayDividerPolicy.Invalid);
            AssertEqual("H5-UI an unusable tick has no day of year",
                OnThisDayDividerPolicy.Invalid,
                OnThisDayDividerPolicy.DayOfYear(OnThisDayDividerPolicy.AbsoluteTick(-1, TicksPerYear)));
            AssertEqual("H5-UI a negative absolute tick has no day of year",
                OnThisDayDividerPolicy.Invalid, OnThisDayDividerPolicy.DayOfYear(-1L));

            // A hand-edited tick near int.MaxValue must widen instead of wrapping negative, which would
            // otherwise read as a valid date somewhere in the distant past.
            AssertTrue("H5-UI an extreme tick widens instead of overflowing",
                OnThisDayDividerPolicy.AbsoluteTick(int.MaxValue, TicksPerDay) > int.MaxValue);
        }

        // --- Only a page from a strictly older year can host the divider -----------------------------
        private static void TestOnThisDayViewedYearGate()
        {
            AssertTrue("H5-UI an older diary year is eligible",
                OnThisDayDividerPolicy.ViewedYearEligible(true, 5501, 5503));
            AssertTrue("H5-UI the current diary year is never eligible",
                !OnThisDayDividerPolicy.ViewedYearEligible(true, 5503, 5503));
            AssertTrue("H5-UI a future year is never eligible",
                !OnThisDayDividerPolicy.ViewedYearEligible(true, 5504, 5503));
            AssertTrue("H5-UI the undated page is never eligible",
                !OnThisDayDividerPolicy.ViewedYearEligible(false, 5501, 5503));
            AssertTrue("H5-UI an unreadable clock is never eligible",
                !OnThisDayDividerPolicy.ViewedYearEligible(true, 5501, OnThisDayDividerPolicy.Invalid));

            AssertEqual("H5-UI one year back reads as one year ago",
                1, OnThisDayDividerPolicy.YearsAgo(5502, 5503));
            AssertEqual("H5-UI several years back count whole years",
                4, OnThisDayDividerPolicy.YearsAgo(5499, 5503));
        }

        // --- Row-level match rule: right day, honest date, not a dev stress page ---------------------
        private static void TestOnThisDayRowMatching()
        {
            AssertTrue("H5-UI a past-year page on today's day matches",
                OnThisDayDividerPolicy.RowMatches(5501, 23, 5501, 23, "raid=tribal"));
            AssertTrue("H5-UI a page with no saved context still matches",
                OnThisDayDividerPolicy.RowMatches(5501, 0, 5501, 0, null));
            AssertTrue("H5-UI the last day of the year matches like any other",
                OnThisDayDividerPolicy.RowMatches(5501, 59, 5501, 59, string.Empty));

            AssertTrue("H5-UI a neighbouring day does not match",
                !OnThisDayDividerPolicy.RowMatches(5501, 24, 5501, 23, string.Empty));
            AssertTrue("H5-UI day 0 does not match day 59 across the year boundary",
                !OnThisDayDividerPolicy.RowMatches(5501, 0, 5501, 59, string.Empty));

            // The tick's year must agree with the year the page claims. A dev stress-fill page carries a
            // real tick under a fabricated display date, so the two diverge and the row fails closed.
            AssertTrue("H5-UI a tick from another year than the page never matches",
                !OnThisDayDividerPolicy.RowMatches(5502, 23, 5501, 23, string.Empty));
            AssertTrue("H5-UI an entry with no usable tick never matches",
                !OnThisDayDividerPolicy.RowMatches(OnThisDayDividerPolicy.Invalid, 23, 5501, 23, string.Empty));
            AssertTrue("H5-UI an entry with no usable day never matches",
                !OnThisDayDividerPolicy.RowMatches(5501, OnThisDayDividerPolicy.Invalid, 5501, 23, string.Empty));
            AssertTrue("H5-UI an unreadable clock matches nothing",
                !OnThisDayDividerPolicy.RowMatches(5501, 23, 5501, OnThisDayDividerPolicy.Invalid, string.Empty));

            // Dev stress-fill pages are excluded outright, even on a perfectly aligned tick.
            AssertTrue("H5-UI a dev mock page never matches",
                !OnThisDayDividerPolicy.RowMatches(
                    5501, 23, 5501, 23, "dev_mock=true; mock_index=7; mock_years=3"));
            AssertTrue("H5-UI the mock marker is found anywhere in the context",
                !OnThisDayDividerPolicy.RowMatches(
                    5501, 23, 5501, 23, "raid=tribal; dev_mock=true"));
            AssertTrue("H5-UI an unrelated context key is not mistaken for the mock marker",
                OnThisDayDividerPolicy.RowMatches(5501, 23, 5501, 23, "mock_index=7"));
        }

        // --- The sliced layout build places exactly one divider per rebuild --------------------------
        private static void TestOnThisDayFirstMatchOnlyScan()
        {
            OnThisDayDividerScan scan = new OnThisDayDividerScan();

            // Current-year page: armed with an ineligible year, no row can ever win, and the tab skips
            // all per-row work.
            scan.Begin(false, 5503, 23, 0);
            AssertTrue("H5-UI an ineligible page wants no rows", !scan.WantsMore);
            AssertTrue("H5-UI an ineligible page accepts nothing",
                !scan.Accept(5503, 23, string.Empty));
            AssertTrue("H5-UI an ineligible page places nothing", !scan.Placed);

            // Eligible page with an unreadable clock stays disarmed.
            scan.Begin(true, 5501, OnThisDayDividerPolicy.Invalid, 2);
            AssertTrue("H5-UI an unreadable clock disarms the scan", !scan.WantsMore);

            // Eligible page: the first matching row wins, later matches are ignored.
            scan.Begin(true, 5501, 23, 2);
            AssertTrue("H5-UI an eligible page wants rows", scan.WantsMore);
            AssertEqual("H5-UI the scan carries the year gap for the label", 2, scan.YearsAgo);
            AssertTrue("H5-UI a non-matching row before the match is skipped",
                !scan.Accept(5501, 22, string.Empty));
            AssertTrue("H5-UI the mock row is skipped without consuming the divider",
                !scan.Accept(5501, 23, "dev_mock=true"));
            AssertTrue("H5-UI a mock row leaves the divider available", scan.WantsMore);
            AssertTrue("H5-UI the first matching row takes the divider",
                scan.Accept(5501, 23, "raid=tribal"));
            AssertTrue("H5-UI the divider is placed once", scan.Placed);
            AssertTrue("H5-UI the scan stops wanting rows after placing", !scan.WantsMore);
            AssertTrue("H5-UI a second matching row on the same day is ignored",
                !scan.Accept(5501, 23, string.Empty));
            AssertTrue("H5-UI later rows never reopen the divider",
                !scan.Accept(5501, 23, "thought=sad"));

            // A restarted layout build (year change, resize, day rollover) re-places exactly one row.
            scan.Begin(true, 5501, 23, 2);
            AssertTrue("H5-UI a rebuilt layout starts unplaced", !scan.Placed);
            AssertTrue("H5-UI a rebuilt layout places its own single divider",
                scan.Accept(5501, 23, string.Empty));

            scan.Clear();
            AssertTrue("H5-UI a cleared scan places nothing", !scan.WantsMore);
            AssertTrue("H5-UI a cleared scan forgets its placement", !scan.Placed);
        }

        // --- Reserved layout height must match what the draw pass paints ----------------------------
        private static void TestOnThisDayReservedHeight()
        {
            const float height = 26f;
            const float gap = 6f;

            AssertNear("H5-UI a row without the callback reserves nothing",
                0f, OnThisDayDividerPolicy.ReservedHeight(false, false, false, height, gap));
            AssertNear("H5-UI a quadrum divider alone reserves nothing extra",
                0f, OnThisDayDividerPolicy.ReservedHeight(false, true, true, height, gap));
            AssertNear("H5-UI the very first row hugs the top of the journal",
                height, OnThisDayDividerPolicy.ReservedHeight(true, false, true, height, gap));
            AssertNear("H5-UI a first row under a quadrum header keeps the separating gap",
                height + gap, OnThisDayDividerPolicy.ReservedHeight(true, true, true, height, gap));
            AssertNear("H5-UI a later row reserves the row plus its gap",
                height + gap, OnThisDayDividerPolicy.ReservedHeight(true, false, false, height, gap));
            AssertNear("H5-UI a later row under a quadrum header reserves the same band",
                height + gap, OnThisDayDividerPolicy.ReservedHeight(true, true, false, height, gap));
            AssertNear("H5-UI a negative XML gap cannot pull the card upward",
                height, OnThisDayDividerPolicy.ReservedHeight(true, true, false, height, -12f));
            AssertNear("H5-UI dividers turned off in XML reserve nothing",
                0f, OnThisDayDividerPolicy.ReservedHeight(true, true, false, 0f, gap));
        }
    }
}
