// "On this day" callback divider for the Diary tab. Presentation-only: when the player is reading a
// diary year OLDER than the current one, this helper finds the page that fell on today's calendar day
// back then and builds its localized "On this day · a year ago" header. It changes nothing about the
// saved history, the sort order, or the cards — it only tells the tab where to reserve one divider row.
//
// New to C#/RimWorld? (JS/TS analogy) This is the thin impure shell around the pure rules in
// Source/Pipeline/OnThisDayDividerPolicy.cs: it is the only part that reads the live clock
// (Find.TickManager), asks RimWorld for a calendar year (GenDate), and localizes text (.Translate()).
// All of the "does this row qualify?" logic lives in the pure policy so it can be tested headlessly.
//
// Why ticks and not the printed date: unlike DiaryQuadrumDivider (which groups by the DISPLAY date
// string, because that is what the year pager groups by), an anniversary must be exact to the day.
// Only the stored tick carries that. The policy then requires the tick's year to agree with the page
// the player is on, so a fabricated dev-mock date can never produce a divider.
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Resolves the Diary tab's "On this day" divider: today's calendar position, one entry's calendar
    /// position, whether they match, and the localized label. Kept separate from the large tab file so
    /// the callback rules read in one place, next to <see cref="DiaryQuadrumDivider"/>.
    /// </summary>
    internal static class DiaryOnThisDayDivider
    {
        /// <summary>Sentinel for "this cannot be placed on the calendar" (mirrors the pure policy).</summary>
        internal const int Invalid = OnThisDayDividerPolicy.Invalid;

        // Longitude used for every calendar read here. Zero matches how every stored diary date string
        // was printed (GenDate.DateFullStringAt(..., Vector2.zero)), so the divider's idea of "which
        // year/day is this" always agrees with the date shown on the card and with the year pager.
        private const float NominalLongitude = 0f;

        /// <summary>
        /// Game-tick to absolute-tick offset, read exactly the way
        /// DiaryGameComponent.DayIndexForGameTick reads it. Returns 0 outside a loaded game, where the
        /// per-entry conversions below then fail closed on their own.
        /// </summary>
        internal static int TickOffset()
        {
            TickManager ticks = Find.TickManager;
            return ticks == null ? 0 : ticks.TicksAbs - ticks.TicksGame;
        }

        /// <summary>Day index inside the year (0..59) for an absolute tick, or <see cref="Invalid"/>.</summary>
        internal static int DayOfYear(int absoluteTick)
        {
            return OnThisDayDividerPolicy.DayOfYear(absoluteTick);
        }

        /// <summary>
        /// Today's day of year, or <see cref="Invalid"/> outside a loaded game.
        /// </summary>
        internal static int CurrentDayOfYear()
        {
            TickManager ticks = Find.TickManager;
            return ticks == null ? Invalid : OnThisDayDividerPolicy.DayOfYear(ticks.TicksAbs);
        }

        /// <summary>
        /// The current in-game year (for example 5502), or <see cref="Invalid"/> outside a loaded game.
        /// Same source the pager's year labels come from, so the two can be compared directly.
        /// </summary>
        internal static int CurrentYear()
        {
            TickManager ticks = Find.TickManager;
            return ticks == null ? Invalid : GenDate.Year(ticks.TicksAbs, NominalLongitude);
        }

        /// <summary>
        /// Day of year the entry's own tick falls on, or <see cref="Invalid"/> when the entry has no
        /// usable tick (very old save rows, corrupt/negative ticks, no loaded game).
        /// </summary>
        internal static int EntryDayOfYear(DiaryEntryView entry, int tickOffset)
        {
            return OnThisDayDividerPolicy.DayOfYear(EntryAbsoluteTick(entry, tickOffset));
        }

        /// <summary>
        /// In-game year the entry's own tick falls in, or <see cref="Invalid"/> when it has no usable
        /// tick. Compared against the page's year so a fabricated display date cannot match.
        /// </summary>
        internal static int EntryYear(DiaryEntryView entry, int tickOffset)
        {
            long absoluteTick = EntryAbsoluteTick(entry, tickOffset);
            if (absoluteTick < 0L || absoluteTick > int.MaxValue)
            {
                return Invalid;
            }

            return GenDate.Year((int)absoluteTick, NominalLongitude);
        }

        /// <summary>
        /// True when <paramref name="entry"/> is the anniversary page for the year being viewed: its
        /// own tick lands on <paramref name="currentDayOfYear"/> inside <paramref name="viewedYear"/>,
        /// and it is not a dev stress-fill page. Used by the tab's layout pass (through
        /// <see cref="OnThisDayDividerScan"/>) and directly by tests.
        /// </summary>
        internal static bool Matches(
            DiaryEntryView entry,
            int tickOffset,
            int viewedYear,
            int currentDayOfYear)
        {
            return OnThisDayDividerPolicy.RowMatches(
                EntryYear(entry, tickOffset),
                EntryDayOfYear(entry, tickOffset),
                viewedYear,
                currentDayOfYear,
                GameContextOf(entry));
        }

        /// <summary>
        /// The plain saved context string behind an entry, used for the dev-mock guard. Empty for
        /// rows that never carried one.
        /// </summary>
        internal static string GameContextOf(DiaryEntryView entry)
        {
            DiaryTextDecorationContext decoration = entry?.TextDecorationContext;
            return decoration == null ? string.Empty : decoration.gameContext;
        }

        /// <summary>
        /// Arms <paramref name="scan"/> for one layout build over the given diary year, freezing
        /// today's calendar position. Returns true when a divider is still possible on this page:
        /// false for the current year, an undated page, or outside a loaded game.
        /// </summary>
        internal static bool Begin(OnThisDayDividerScan scan, int viewedYear, bool viewedYearKnown)
        {
            if (scan == null)
            {
                return false;
            }

            int currentYear = CurrentYear();
            int currentDayOfYear = CurrentDayOfYear();
            bool eligible = OnThisDayDividerPolicy.ViewedYearEligible(viewedYearKnown, viewedYear, currentYear);
            scan.Begin(
                eligible,
                viewedYear,
                currentDayOfYear,
                eligible ? OnThisDayDividerPolicy.YearsAgo(viewedYear, currentYear) : 0);
            return scan.WantsMore;
        }

        /// <summary>
        /// A cheap per-frame value that changes only when the divider's meaning changes: today's day
        /// of year while the viewed page could show a callback, and <see cref="Invalid"/> otherwise.
        /// The tab compares it against the value its cached layout was built with, so an in-game day
        /// rollover with the tab left open re-places the divider instead of showing yesterday's.
        /// </summary>
        internal static int LayoutDayStamp(int viewedYear, bool viewedYearKnown)
        {
            int currentDayOfYear = CurrentDayOfYear();
            return OnThisDayDividerPolicy.ViewedYearEligible(viewedYearKnown, viewedYear, CurrentYear())
                ? currentDayOfYear
                : Invalid;
        }

        /// <summary>
        /// Builds the localized callback header, for example "On this day · a year ago". Returns empty
        /// for a non-positive year gap, which the callers already exclude.
        /// </summary>
        internal static string Label(int yearsAgo)
        {
            if (yearsAgo <= 0)
            {
                return string.Empty;
            }

            // One year gets its own key so English reads "a year ago" instead of "1 years ago", and so
            // languages with several plural forms can phrase the singular case naturally.
            return yearsAgo == 1
                ? "PawnDiary.Tab.OnThisDayOneYear".Translate()
                : "PawnDiary.Tab.OnThisDay".Translate(yearsAgo);
        }

        /// <summary>
        /// Converts one entry's stored game tick into an absolute tick, or a negative value when the
        /// entry cannot be placed (missing entry, corrupt tick, no loaded game).
        /// </summary>
        private static long EntryAbsoluteTick(DiaryEntryView entry, int tickOffset)
        {
            if (entry == null || Find.TickManager == null)
            {
                return Invalid;
            }

            return OnThisDayDividerPolicy.AbsoluteTick(entry.Tick, tickOffset);
        }
    }
}
