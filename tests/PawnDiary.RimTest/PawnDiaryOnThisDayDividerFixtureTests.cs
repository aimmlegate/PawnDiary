// Loaded-game fixture for the Diary tab's "On this day" callback divider (Quality Wave §6, H5 UI).
//
// The placement RULES are pure and covered headlessly by DiaryPipelineTests. What only a loaded game
// can prove is the adapter around them: that the game-tick -> absolute-tick offset matches the one
// DiaryGameComponent.DayIndexForGameTick uses, that a page's tick resolves to the same calendar year
// RimWorld printed into its date string (the trust check that keeps fabricated dates out), that the
// current year is gated off, and that both localized label keys actually exist in the loaded language.
//
// No pawns, no events, no rendering: the tests build read-only DiaryEntryView values from the live
// clock and read Defs/Keyed strings. Nothing here touches the save, so no scope is needed.
//
// The anniversary case is built forward instead of backward on purpose: a colony younger than one year
// has no valid past-year tick at all, so the fixture takes a real present-day page as the OLD page and
// treats "one year from now" as today. That is the same arithmetic the divider performs, with no
// assumption about how old the developer's test colony is.
using System;
using RimTestRedux;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Pins the live-clock half of the "On this day" divider: offset semantics, tick/printed-date year
    /// agreement, the current-year gate, dev-mock and corrupt-tick fail-closed paths, and label keys.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryOnThisDayDividerFixtureTests
    {
        private const int TicksPerYear = GenDate.TicksPerDay * GenDate.DaysPerYear;

        /// <summary>
        /// The divider's tick offset must be exactly the one the day-summary collectors use, or a page
        /// would be filed under a different day than the reflection that references it.
        /// </summary>
        [Test]
        public static void TickOffsetMatchesDayIndexSemantics()
        {
            RequireLoadedClock();
            int expected = Find.TickManager.TicksAbs - Find.TickManager.TicksGame;

            PawnDiaryRimTestScope.Require(DiaryOnThisDayDivider.TickOffset() == expected,
                "The On this day divider does not use DayIndexForGameTick's absolute-tick offset.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.CurrentDayOfYear() == GenDate.DayOfYear(Find.TickManager.TicksAbs, 0f),
                "The On this day divider disagrees with GenDate about today's day of year.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.CurrentYear() == GenDate.Year(Find.TickManager.TicksAbs, 0f),
                "The On this day divider disagrees with GenDate about the current year.");
        }

        /// <summary>
        /// A real page's stored tick must resolve to the same year RimWorld printed into its displayed
        /// date. That agreement is what the match rule uses to reject fabricated dev-mock dates, so a
        /// genuine page must never trip it.
        /// </summary>
        [Test]
        public static void RealPageTickAgreesWithItsPrintedDate()
        {
            RequireLoadedClock();
            int offset = DiaryOnThisDayDivider.TickOffset();
            int absoluteTick = Find.TickManager.TicksAbs;
            DiaryEntryView page = BuildPage(Find.TickManager.TicksGame, absoluteTick, null);

            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.EntryYear(page, offset) == GenDate.Year(absoluteTick, 0f),
                "A real diary page's tick did not resolve to its own calendar year.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.EntryYear(page, offset) == DiarySaveNormalization.ExtractYear(page.Date),
                "A real diary page's tick year and printed date year disagree, so honest pages would be rejected.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.EntryDayOfYear(page, offset) == GenDate.DayOfYear(absoluteTick, 0f),
                "A real diary page's tick did not resolve to its own day of year.");
        }

        /// <summary>
        /// The anniversary page matches, and every fail-closed variant of it does not: a different page
        /// year (a fabricated date), the neighbouring day, a dev stress-fill page, and a corrupt tick.
        /// </summary>
        [Test]
        public static void AnniversaryPageMatchesAndFailClosedVariantsDoNot()
        {
            RequireLoadedClock();
            int offset = DiaryOnThisDayDivider.TickOffset();
            int pageAbsoluteTick = Find.TickManager.TicksAbs;
            int pageGameTick = Find.TickManager.TicksGame;
            int pageYear = GenDate.Year(pageAbsoluteTick, 0f);
            // "Today", one whole year after the page. Same calendar day, next year up.
            int laterDayOfYear = GenDate.DayOfYear(pageAbsoluteTick + TicksPerYear, 0f);

            DiaryEntryView page = BuildPage(pageGameTick, pageAbsoluteTick, null);
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.Matches(page, offset, pageYear, laterDayOfYear),
                "A page on this same calendar day one year earlier did not match.");
            PawnDiaryRimTestScope.Require(
                !DiaryOnThisDayDivider.Matches(page, offset, pageYear, (laterDayOfYear + 1) % GenDate.DaysPerYear),
                "A page from a neighbouring calendar day matched the callback.");
            PawnDiaryRimTestScope.Require(
                !DiaryOnThisDayDivider.Matches(page, offset, pageYear - 1, laterDayOfYear),
                "A page whose tick year disagrees with the viewed year matched the callback.");
            PawnDiaryRimTestScope.Require(
                !DiaryOnThisDayDivider.Matches(null, offset, pageYear, laterDayOfYear),
                "A missing entry matched the callback.");

            DiaryEntryView mockPage = BuildPage(pageGameTick, pageAbsoluteTick,
                "dev_mock=true; mock_index=7; mock_target_count=6000; mock_years=3");
            PawnDiaryRimTestScope.Require(
                !DiaryOnThisDayDivider.Matches(mockPage, offset, pageYear, laterDayOfYear),
                "A dev stress-fill page matched the callback.");

            DiaryEntryView corruptPage = BuildPage(-1, pageAbsoluteTick, null);
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.EntryDayOfYear(corruptPage, offset) == DiaryOnThisDayDivider.Invalid
                    && !DiaryOnThisDayDivider.Matches(corruptPage, offset, pageYear, laterDayOfYear),
                "A page with a corrupt negative tick matched the callback.");
        }

        /// <summary>
        /// The callback only arms on a page from an older year: the current year, a future year, and the
        /// undated page all resolve to no divider at all.
        /// </summary>
        [Test]
        public static void OnlyOlderYearPagesArmTheScan()
        {
            RequireLoadedClock();
            int currentYear = DiaryOnThisDayDivider.CurrentYear();
            OnThisDayDividerScan scan = new OnThisDayDividerScan();

            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.LayoutDayStamp(currentYear, true) == DiaryOnThisDayDivider.Invalid,
                "The current diary year advertised an On this day divider.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.LayoutDayStamp(currentYear + 1, true) == DiaryOnThisDayDivider.Invalid,
                "A future diary year advertised an On this day divider.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.LayoutDayStamp(currentYear - 1, false) == DiaryOnThisDayDivider.Invalid,
                "The undated diary page advertised an On this day divider.");
            PawnDiaryRimTestScope.Require(
                DiaryOnThisDayDivider.LayoutDayStamp(currentYear - 1, true)
                    == DiaryOnThisDayDivider.CurrentDayOfYear(),
                "An older diary year did not stamp today's day of year for the layout pass.");

            PawnDiaryRimTestScope.Require(!DiaryOnThisDayDivider.Begin(scan, currentYear, true),
                "The layout scan armed itself on the current diary year.");
            PawnDiaryRimTestScope.Require(!scan.WantsMore,
                "The layout scan still wanted rows on the current diary year.");
            PawnDiaryRimTestScope.Require(DiaryOnThisDayDivider.Begin(scan, currentYear - 2, true),
                "The layout scan did not arm on a two-year-old diary page.");
            PawnDiaryRimTestScope.Require(scan.YearsAgo == 2,
                "The layout scan resolved the wrong year gap for its label.");
        }

        /// <summary>
        /// Both label forms must resolve through the loaded language. A missing key would render the raw
        /// key text straight into the journal.
        /// </summary>
        [Test]
        public static void BothLabelFormsAreLocalized()
        {
            string singular = DiaryOnThisDayDivider.Label(1);
            string plural = DiaryOnThisDayDivider.Label(3);

            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(singular)
                    && singular.IndexOf("PawnDiary.Tab.", StringComparison.Ordinal) < 0,
                "The single-year On this day label did not resolve to a translated string.");
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(plural)
                    && plural.IndexOf("PawnDiary.Tab.", StringComparison.Ordinal) < 0,
                "The multi-year On this day label did not resolve to a translated string.");
            PawnDiaryRimTestScope.Require(plural.IndexOf("3", StringComparison.Ordinal) >= 0,
                "The multi-year On this day label dropped its year count.");
            PawnDiaryRimTestScope.Require(!string.Equals(singular, plural, StringComparison.Ordinal),
                "The singular and plural On this day labels rendered identically.");
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(DiaryOnThisDayDivider.Label(0)),
                "A zero year gap produced an On this day label.");
        }

        /// <summary>
        /// Builds a read-only page view with a chosen stored tick and the date RimWorld would print for
        /// <paramref name="displayAbsoluteTick"/>, optionally carrying a saved context string.
        /// </summary>
        private static DiaryEntryView BuildPage(int gameTick, int displayAbsoluteTick, string gameContext)
        {
            DiaryTextDecorationContext decoration = gameContext == null
                ? null
                : new DiaryTextDecorationContext { gameContext = gameContext };

            return new DiaryEntryView(
                gameTick,
                GenDate.DateFullStringAt(displayAbsoluteTick, Vector2.zero),
                "raw text",
                "generated text",
                DiaryEvent.CompleteStatus,
                string.Empty,
                string.Empty,
                "test-model",
                string.Empty,
                "pawndiary_rimtest_onthisday_event",
                DiaryEvent.InitiatorRole,
                "group",
                string.Empty,
                string.Empty,
                0,
                false,
                false,
                textDecorationContext: decoration);
        }

        private static void RequireLoadedClock()
        {
            PawnDiaryRimTestScope.Require(Find.TickManager != null,
                "The On this day divider fixture needs a loaded game clock.");
        }
    }
}
