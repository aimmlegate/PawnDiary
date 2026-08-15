// Standalone tests for pure diary UI cache, responsive-layout, and editor-save decisions.
using System;
using PawnDiary;

namespace DiaryUiPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestSessionIdentity();
            TestReaderDirectorySessionIdentity();
            TestInlineYearSelector();
            TestPsychotypeControlRows();
            TestEffectiveFooterLineHeight();
            TestMemoryDraftPersistence();
            TestProtectedMemoryActions();

            Console.WriteLine("DiaryUiPolicyTests passed: " + assertions + " assertions.");
            return 0;
        }

        private static void TestSessionIdentity()
        {
            object first = new object();
            object second = new object();

            False(DiaryUiPolicy.SessionChanged(first, first), "same game component keeps its cache");
            True(DiaryUiPolicy.SessionChanged(first, second), "different game component resets its cache");
            True(DiaryUiPolicy.SessionChanged(first, null), "leaving a game resets its cache");
            True(DiaryUiPolicy.SessionChanged(null, first), "entering a game resets its cache");
            False(DiaryUiPolicy.SessionChanged(null, null), "no game remains the same session");
        }

        private static void TestInlineYearSelector()
        {
            False(
                DiaryUiPolicy.ShouldShowInlineYearSelector(0f, 1),
                "a single year needs no fallback control");
            True(
                DiaryUiPolicy.ShouldShowInlineYearSelector(0f, 2),
                "hidden panel gets an inline year selector");
            True(
                DiaryUiPolicy.ShouldShowInlineYearSelector(1f, 2),
                "non-drawable panel width gets an inline year selector");
            False(
                DiaryUiPolicy.ShouldShowInlineYearSelector(260f, 3),
                "visible panel owns the year selector");
            True(
                DiaryUiPolicy.ShouldShowInlineYearSelector(float.NaN, 2),
                "invalid panel geometry fails safe to inline");
            True(
                DiaryUiPolicy.ShouldShowInlineYearSelector(float.PositiveInfinity, 2),
                "infinite panel geometry fails safe to inline");
        }

        private static void TestReaderDirectorySessionIdentity()
        {
            object firstGame = new object();
            object secondGame = new object();
            object firstComponent = new object();
            object secondComponent = new object();

            False(
                DiaryUiPolicy.ReaderDirectorySessionChanged(
                    firstGame,
                    firstGame,
                    firstComponent,
                    firstComponent),
                "same game and component keep the throttled directory");
            True(
                DiaryUiPolicy.ReaderDirectorySessionChanged(
                    firstGame,
                    firstGame,
                    firstComponent,
                    secondComponent),
                "a new component invalidates the directory even when record counts match");
            True(
                DiaryUiPolicy.ReaderDirectorySessionChanged(
                    firstGame,
                    secondGame,
                    firstComponent,
                    firstComponent),
                "a new game invalidates the directory even if a component identity were reused");
            True(
                DiaryUiPolicy.ReaderDirectorySessionChanged(
                    firstGame,
                    null,
                    firstComponent,
                    null),
                "leaving the game clears directory Pawn references");
        }

        private static void TestPsychotypeControlRows()
        {
            const float picker = 120f;
            const float reroll = 90f;
            const float pin = 110f;
            const float gap = 6f;
            float exactFit = picker + reroll + pin + gap * 2f;

            Equal(
                1,
                DiaryUiPolicy.PsychotypeControlRowCount(exactFit, picker, reroll, pin, gap),
                "exact minimum width stays on one row");
            Equal(
                3,
                DiaryUiPolicy.PsychotypeControlRowCount(exactFit - 0.01f, picker, reroll, pin, gap),
                "one pixel-short layout stacks all controls");
            Equal(
                3,
                DiaryUiPolicy.PsychotypeControlRowCount(0f, picker, reroll, pin, gap),
                "zero-width layout stacks defensively");
            Equal(
                3,
                DiaryUiPolicy.PsychotypeControlRowCount(float.NaN, picker, reroll, pin, gap),
                "invalid width stacks defensively");
            Equal(
                1,
                DiaryUiPolicy.PsychotypeControlRowCount(100f, -1f, -1f, -1f, -1f),
                "negative component dimensions are clamped");
        }

        private static void TestEffectiveFooterLineHeight()
        {
            Equal(
                22f,
                DiaryUiPolicy.EffectiveFooterLineHeight(20f, 22f),
                "effective Small fallback expands the Tiny footer");
            Equal(
                24f,
                DiaryUiPolicy.EffectiveFooterLineHeight(24f, 22f),
                "XML minimum may reserve more room than the font");
            Equal(
                22f,
                DiaryUiPolicy.EffectiveFooterLineHeight(float.NaN, 22f),
                "invalid XML geometry falls back to measured font height");
            Equal(
                20f,
                DiaryUiPolicy.EffectiveFooterLineHeight(20f, float.PositiveInfinity),
                "invalid font geometry retains the XML minimum");
        }

        private static void TestMemoryDraftPersistence()
        {
            False(
                DiaryUiPolicy.MemoryDraftNeedsPersistence(
                    "A colonist joined the settlement.",
                    "A colonist joined the settlement."),
                "an unchanged rendered template remains a no-op");
            False(
                DiaryUiPolicy.MemoryDraftNeedsPersistence(null, string.Empty),
                "missing and blank canonical text are equivalent");
            True(
                DiaryUiPolicy.MemoryDraftNeedsPersistence(
                    "A colonist joined the settlement.",
                    "They chose to stay with us."),
                "edited prose must be persisted by either memory Save or profile Save");
            True(
                DiaryUiPolicy.MemoryDraftNeedsPersistence(
                    "A manually overridden memory.",
                    string.Empty),
                "clearing an override must persist so the localized template is restored");
        }

        private static void TestProtectedMemoryActions()
        {
            const string protectedKind = "status.faction.joined";
            False(
                DiaryUiPolicy.ShouldOfferMemoryRemove(protectedKind, protectedKind),
                "the protected faction-joining lifecycle row has no misleading Remove action");
            True(
                DiaryUiPolicy.ShouldOfferMemoryRemove("social.marriage", protectedKind),
                "ordinary captured memories remain removable");
            True(
                DiaryUiPolicy.ShouldOfferMemoryRemove(protectedKind, null),
                "a missing protected token must not hide every Remove action");
        }

        private static void True(bool value, string message)
        {
            assertions++;
            if (!value)
            {
                throw new InvalidOperationException("Expected true: " + message);
            }
        }

        private static void False(bool value, string message)
        {
            assertions++;
            if (value)
            {
                throw new InvalidOperationException("Expected false: " + message);
            }
        }

        private static void Equal(int expected, int actual, string message)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    message + " (expected " + expected + ", actual " + actual + ")");
            }
        }

        private static void Equal(float expected, float actual, string message)
        {
            assertions++;
            if (Math.Abs(expected - actual) > 0.001f)
            {
                throw new InvalidOperationException(
                    message + " (expected " + expected + ", actual " + actual + ")");
            }
        }
    }
}
