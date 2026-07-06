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
                null);
            y += ContextDetailFullRowHeight + 4f;

            DrawContextDetailPresetRow(
                new Rect(innerRect.x, y, innerRect.width, ContextDetailPresetRowHeight),
                PromptContextDetailLevel.Balanced,
                "PawnDiary.Settings.ContextDetail.Balanced.Added",
                "PawnDiary.Settings.ContextDetail.Balanced.Cut");
            y += ContextDetailPresetRowHeight + 4f;

            DrawContextDetailPresetRow(
                new Rect(innerRect.x, y, innerRect.width, ContextDetailPresetRowHeight),
                PromptContextDetailLevel.Compact,
                "PawnDiary.Settings.ContextDetail.Compact.Added",
                "PawnDiary.Settings.ContextDetail.Compact.Cut");
        }

        private static void DrawContextDetailPresetRow(Rect rect, PromptContextDetailLevel level, string addedKey, string cutKey)
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
            float detailHeight = showCut ? (inner.height - 4f) / 2f : inner.height;
            Rect addLabelRect = new Rect(textX, inner.y, labelWidth, detailHeight);
            Rect addTextRect = new Rect(addLabelRect.xMax + gap, inner.y, Mathf.Max(0f, inner.xMax - addLabelRect.xMax - gap), detailHeight);

            DrawAccentLabel(addLabelRect, "PawnDiary.Settings.ContextDetail.AddedLabel".Translate().ToString());
            Widgets.LabelFit(addTextRect, addedKey.Translate().ToString());
            if (showCut)
            {
                Rect cutLabelRect = new Rect(textX, inner.y + detailHeight + 4f, labelWidth, detailHeight);
                Rect cutTextRect = new Rect(cutLabelRect.xMax + gap, cutLabelRect.y, Mathf.Max(0f, inner.xMax - cutLabelRect.xMax - gap), detailHeight);
                DrawAccentLabel(cutLabelRect, "PawnDiary.Settings.ContextDetail.CutLabel".Translate().ToString());
                Widgets.LabelFit(cutTextRect, cutKey.Translate().ToString());
            }

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            TooltipHandler.TipRegion(rect, "PawnDiary.Settings.ContextDetail.RowTip".Translate());
            if (Widgets.ButtonInvisible(rect))
            {
                Settings.contextDetailLevel = normalizedLevel;
            }
        }
    }
}
