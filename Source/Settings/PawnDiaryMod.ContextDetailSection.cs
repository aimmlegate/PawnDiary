// Main-tab context-detail section for Pawn Diary. It keeps the real selector next to an illustrative
// cut/add display so players can see the tradeoff without opening prompt-editing tools.
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float ContextDetailDisplayHeight = 314f;
        private const float ContextDetailFullRowHeight = 58f;
        private const float ContextDetailPresetRowHeight = 84f;

        /// <summary>Draws the global context-detail selector and a static explanation of each preset.</summary>
        private void DrawContextDetailSection(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.ContextDetailSectionTitle".Translate());

            Rect blockRect = listing.GetRect(ContextDetailDisplayHeight);
            Widgets.DrawMenuSection(blockRect);
            Rect innerRect = blockRect.ContractedBy(8f);
            float y = innerRect.y;

            Rect helpRect = new Rect(innerRect.x, y, innerRect.width, 40f);
            DrawMutedLabel(helpRect, "PawnDiary.Settings.ContextDetailSectionHelp".Translate().ToString());
            y += helpRect.height + 8f;

            DrawContextDetailPresetRow(
                new Rect(innerRect.x, y, innerRect.width, ContextDetailFullRowHeight),
                PromptContextDetailLevel.Full,
                "PawnDiary.Settings.ContextDetail.Full.Added",
                null,
                "PawnDiary.Settings.ContextDetail.Full.Layers");
            y += ContextDetailFullRowHeight + 4f;

            DrawContextDetailPresetRow(
                new Rect(innerRect.x, y, innerRect.width, ContextDetailPresetRowHeight),
                PromptContextDetailLevel.Balanced,
                "PawnDiary.Settings.ContextDetail.Balanced.Added",
                "PawnDiary.Settings.ContextDetail.Balanced.Cut",
                "PawnDiary.Settings.ContextDetail.Balanced.Layers");
            y += ContextDetailPresetRowHeight + 4f;

            DrawContextDetailPresetRow(
                new Rect(innerRect.x, y, innerRect.width, ContextDetailPresetRowHeight),
                PromptContextDetailLevel.Compact,
                "PawnDiary.Settings.ContextDetail.Compact.Added",
                "PawnDiary.Settings.ContextDetail.Compact.Cut",
                "PawnDiary.Settings.ContextDetail.Compact.Layers");
        }

        /// <summary>
        /// Draws one clickable preset row: what the preset sends, what it drops first, and which
        /// optional writing layers (pawn memory, psychotype outlook, live context hints) it keeps.
        /// The "cut first" line is absent for Full, so the row lays itself out as two or three
        /// equal-height text lines depending on how many it was given.
        /// </summary>
        private static void DrawContextDetailPresetRow(
            Rect rect,
            PromptContextDetailLevel level,
            string addedKey,
            string cutKey,
            string layersKey)
        {
            PromptContextDetailLevel normalizedLevel = PawnDiarySettings.NormalizeContextDetailLevel(level);
            if (Settings.contextDetailLevel == normalizedLevel)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            Rect inner = rect.ContractedBy(6f);
            const float nameWidth = 106f;
            const float labelWidth = 78f;
            const float gap = 6f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.LabelFit(new Rect(inner.x, inner.y, nameWidth, inner.height), ContextDetailLabel(normalizedLevel).Translate());

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            float textX = inner.x + nameWidth + gap;
            bool showCut = !string.IsNullOrEmpty(cutKey);
            const float lineGap = 4f;
            int lineCount = showCut ? 3 : 2;
            float detailHeight = (inner.height - lineGap * (lineCount - 1)) / lineCount;
            float lineY = inner.y;

            DrawContextDetailTextLine(
                textX, lineY, labelWidth, gap, inner.xMax, detailHeight,
                "PawnDiary.Settings.ContextDetail.AddedLabel", addedKey);
            lineY += detailHeight + lineGap;

            if (showCut)
            {
                DrawContextDetailTextLine(
                    textX, lineY, labelWidth, gap, inner.xMax, detailHeight,
                    "PawnDiary.Settings.ContextDetail.CutLabel", cutKey);
                lineY += detailHeight + lineGap;
            }

            DrawContextDetailTextLine(
                textX, lineY, labelWidth, gap, inner.xMax, detailHeight,
                "PawnDiary.Settings.ContextDetail.LayersLabel", layersKey);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.ContextDetail.RowTip".Translate());
            if (Widgets.ButtonInvisible(rect))
            {
                Settings.contextDetailLevel = normalizedLevel;
                // Picking a preset also flips the three optional writing layers. ClampValues (called at
                // the end of the Main tab draw) re-derives them, so the change lands this frame.
            }
        }

        /// <summary>
        /// Draws one accent-labelled explanation line inside a preset row. The caller keeps the shared
        /// font/anchor state, so this only positions the label and its wrapped-to-fit description.
        /// </summary>
        private static void DrawContextDetailTextLine(
            float textX,
            float lineY,
            float labelWidth,
            float gap,
            float rightEdge,
            float lineHeight,
            string labelKey,
            string textKey)
        {
            Rect labelRect = new Rect(textX, lineY, labelWidth, lineHeight);
            Rect textRect = new Rect(labelRect.xMax + gap, lineY, Mathf.Max(0f, rightEdge - labelRect.xMax - gap), lineHeight);
            DrawAccentLabel(labelRect, labelKey.Translate().ToString());
            Widgets.LabelFit(textRect, textKey.Translate().ToString());
        }
    }
}
