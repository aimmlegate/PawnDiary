// Shared, drawing-only status overlays for Diary gizmos, pawn rows, and the standalone main button.
// Callers provide the final screen/local Rect; this helper never reads or mutates saved game state.
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Paints compact unread, writing, and failure indicators using XML-backed Diary UI colors.
    /// </summary>
    internal static class DiaryStatusOverlay
    {
        public static void DrawUnreadUnderline(Rect rect)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            Color color = style.FavoriteStarColor;
            color.a *= Mathf.Clamp01(style.statusUnreadUnderlineAlpha);
            Widgets.DrawBoxSolid(rect, color);
            TooltipHandler.TipRegion(rect, "PawnDiary.Status.UnreadPagesTip".Translate());
        }

        public static void DrawWritingBadge(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            Widgets.DrawBoxSolid(rect, style.LinkedEntryBgColor);

            float dotSize = Mathf.Max(1f, style.writingDotSize);
            float gap = Mathf.Max(0f, style.writingDotGap);
            float dotsWidth = dotSize * 3f + gap * 2f;
            float startX = rect.x + (rect.width - dotsWidth) * 0.5f;
            DiaryJournalView.DrawWritingDots(
                new Rect(
                    startX,
                    rect.y + (rect.height - dotSize) * 0.5f,
                    dotsWidth,
                    dotSize),
                style.WritingPlaceholderHighColor,
                0f);

            TooltipHandler.TipRegion(rect, "PawnDiary.Command.WritingTip".Translate());
        }

        public static void DrawUnreadCountBadge(Rect rect, int count)
        {
            if (count <= 0)
            {
                return;
            }

            DrawCountBadge(
                rect,
                count,
                DiaryJournalView.UiStyle.FavoriteStarColor,
                "PawnDiary.Status.UnreadCountTip".Translate(count));
        }

        public static void DrawFailureBadge(Rect rect, int count)
        {
            if (count <= 0)
            {
                return;
            }

            DrawCountBadge(
                rect,
                count,
                DiaryJournalView.UiStyle.DevDangerButtonColor,
                "PawnDiary.Status.FailuresTip".Translate(count));
        }

        private static void DrawCountBadge(Rect rect, int count, Color accent, TaggedString tooltip)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolid(rect, accent);
            Rect inner = rect.ContractedBy(1f);
            Widgets.DrawBoxSolid(inner, DiaryJournalView.UiStyle.LinkedEntryBgColor);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = accent;
            Widgets.Label(inner, count > 99 ? "99+" : count.ToString());
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            TooltipHandler.TipRegion(rect, tooltip);
        }
    }
}
