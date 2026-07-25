// Pure placement rules for the Diary tab's "On this day" callback divider.
//
// The Diary tab pages history one in-game year at a time. When the player is reading an OLDER year,
// this policy finds the single page that fell on today's calendar day back then, so the journal can
// mark it with a quiet "On this day · a year ago" row. Every decision here is plain arithmetic on
// primitives, so the whole rule set is testable without loading RimWorld/Verse/Unity — the UI
// adapter (Source/UI/DiaryOnThisDayDivider.cs) only converts ticks and localizes the label.
//
// New to C#/RimWorld? (JS/TS analogy) RimWorld's calendar is fixed: 60 days per year, 60000 ticks
// per day, so "day of year" is integer division, not a Date library. Two tick spaces exist: the
// per-save "game tick" (0 at colony start) and the shared "absolute tick" the calendar/date strings
// are printed from. The caller converts game -> absolute exactly the way
// DiaryGameComponent.DayIndexForGameTick does, then hands the absolute tick here.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Decides whether one diary row belongs on today's calendar day in an earlier year, and how much
    /// vertical space its divider row needs. Pure: no game state, no randomness, no localization.
    /// </summary>
    internal static class OnThisDayDividerPolicy
    {
        // RimWorld's fixed calendar, mirrored here so this file stays Verse-free. These are stable
        // game constants (GenDate.TicksPerDay / GenDate.DaysPerYear), not tunable policy — same
        // treatment as QuadrumAnniversaryMemoryPolicy.QuadrumsPerYear.
        internal const int TicksPerDay = 60000;
        internal const int DaysPerYear = 60;

        /// <summary>Returned instead of a day/year when a tick cannot be placed on the calendar.</summary>
        internal const int Invalid = -1;

        /// <summary>
        /// Day index inside the year (0..59) for an absolute tick, or <see cref="Invalid"/> when the
        /// tick is not a real calendar position. Matches GenDate's longitude-0 convention, which is
        /// what every saved diary date string was printed with.
        /// </summary>
        public static int DayOfYear(long absoluteTick)
        {
            if (absoluteTick < 0L)
            {
                return Invalid;
            }

            return (int)((absoluteTick / TicksPerDay) % DaysPerYear);
        }

        /// <summary>
        /// Converts a stored game tick into an absolute tick using the caller's offset. Widened to
        /// long on purpose: a corrupt or hand-edited tick near int.MaxValue would otherwise overflow
        /// into a negative number and silently look like a valid past date.
        /// </summary>
        public static long AbsoluteTick(int gameTick, int tickOffset)
        {
            if (gameTick < 0)
            {
                // Negative stored ticks only happen in corrupt/hand-edited saves. Fail closed.
                return Invalid;
            }

            return (long)gameTick + tickOffset;
        }

        /// <summary>
        /// True when the viewed diary year can host the divider at all: the page must be a known
        /// in-game year strictly older than the current one. Today's own year never gets a callback.
        /// </summary>
        public static bool ViewedYearEligible(bool viewedYearKnown, int viewedYear, int currentYear)
        {
            return viewedYearKnown && currentYear > 0 && viewedYear > 0 && viewedYear < currentYear;
        }

        /// <summary>Whole years between the viewed page's year and the current in-game year.</summary>
        public static int YearsAgo(int viewedYear, int currentYear)
        {
            return currentYear - viewedYear;
        }

        /// <summary>
        /// True when this row is the anniversary page: its own tick lands on today's day of year, in
        /// the year the page claims to be. Everything else fails closed.
        ///
        /// The <paramref name="entryYear"/> == <paramref name="viewedYear"/> test is the trust check.
        /// A real page formats its printed date from its own tick, so the two always agree; a page
        /// with a fabricated date (dev stress-fill) or an unparseable legacy date does not, and then
        /// its tick says nothing about when the player thinks it happened.
        /// </summary>
        public static bool RowMatches(
            int entryYear,
            int entryDayOfYear,
            int viewedYear,
            int currentDayOfYear,
            string gameContext)
        {
            if (entryYear == Invalid || entryDayOfYear == Invalid || currentDayOfYear == Invalid)
            {
                return false;
            }

            if (entryYear != viewedYear || entryDayOfYear != currentDayOfYear)
            {
                return false;
            }

            // Dev stress-fill pages carry real ticks but deliberately fabricated display dates, so a
            // coincidental tick alignment must never produce a divider on mock history. The marker is
            // the same one the generation pipeline already gates on, and it survives archiving.
            return !DiaryContextFields.HasMarker(gameContext, DevMockMarker);
        }

        /// <summary>
        /// Vertical space one row must reserve above its card for the callback divider. Mirrors the
        /// quadrum divider's own geometry: the label row itself, plus the small gap that separates it
        /// from whatever sits above (a quadrum header, or the previous card). The very first row of
        /// the journal hugs the top, so it gets no gap when nothing precedes it.
        /// </summary>
        public static float ReservedHeight(
            bool hasOnThisDay,
            bool hasQuadrumDivider,
            bool isFirstRow,
            float dividerHeight,
            float topGap)
        {
            if (!hasOnThisDay || dividerHeight <= 0f)
            {
                return 0f;
            }

            bool hugsTop = isFirstRow && !hasQuadrumDivider;
            return dividerHeight + (hugsTop ? 0f : Math.Max(0f, topGap));
        }

        // Context marker written by DiaryGameComponent.FillMockDiaryEntriesForDev.
        private const string DevMockMarker = "dev_mock=";
    }

    /// <summary>
    /// Carries "have we placed the callback divider yet?" across the Diary tab's sliced layout build.
    /// The tab lays out a long year over several frames, so the first-match-only rule cannot live in a
    /// local variable. Pure state machine: <see cref="Begin"/> once per layout build, then
    /// <see cref="Accept"/> per row in list order; exactly one row can ever win.
    /// </summary>
    internal sealed class OnThisDayDividerScan
    {
        private bool active;
        private int viewedYear;
        private int currentDayOfYear;
        private int yearsAgo;
        private bool placed;

        /// <summary>Years between the viewed page and today, for the divider label.</summary>
        public int YearsAgo
        {
            get { return yearsAgo; }
        }

        /// <summary>
        /// True while a row could still win the divider. False on ineligible pages and after the
        /// divider has been placed, so the tab can skip the per-row work entirely.
        /// </summary>
        public bool WantsMore
        {
            get { return active && !placed; }
        }

        /// <summary>True once this build has placed its single divider row.</summary>
        public bool Placed
        {
            get { return placed; }
        }

        /// <summary>
        /// Arms the scan for one layout build and clears any previous result. Pass a frozen snapshot
        /// of today's date: re-reading the clock per row could otherwise place two dividers when the
        /// in-game day rolls over mid-build.
        /// </summary>
        public void Begin(bool eligible, int viewedYear, int currentDayOfYear, int yearsAgo)
        {
            this.active = eligible && currentDayOfYear != OnThisDayDividerPolicy.Invalid && yearsAgo > 0;
            this.viewedYear = viewedYear;
            this.currentDayOfYear = currentDayOfYear;
            this.yearsAgo = yearsAgo;
            this.placed = false;
        }

        /// <summary>Disarms the scan so no row can win (used when the clock cannot be read).</summary>
        public void Clear()
        {
            Begin(false, 0, OnThisDayDividerPolicy.Invalid, 0);
        }

        /// <summary>
        /// Offers one row, in list order, to the scan. Returns true for the single row that should
        /// draw the divider; every later row returns false even when it also matches.
        /// </summary>
        public bool Accept(int entryYear, int entryDayOfYear, string gameContext)
        {
            if (!WantsMore)
            {
                return false;
            }

            if (!OnThisDayDividerPolicy.RowMatches(
                    entryYear,
                    entryDayOfYear,
                    viewedYear,
                    currentDayOfYear,
                    gameContext))
            {
                return false;
            }

            placed = true;
            return true;
        }
    }
}
