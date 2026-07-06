// Simulated prompt-example drawer for the Prompts settings tab. It deliberately avoids building a
// live prompt plan; this is a lightweight, made-up prompt shape players can inspect without opening
// the old context-detail diagnostics panel.
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float SimulatedPromptExampleHeight = 430f;

        /// <summary>Draws a collapsed drawer containing one static, simulated prompt example.</summary>
        private void DrawSimulatedPromptExampleDrawer(Listing_Standard listing)
        {
            listing.Gap(12f);
            Rect headerRect = listing.GetRect(82f);
            Widgets.DrawMenuSection(headerRect);
            Rect innerRect = headerRect.ContractedBy(8f);
            Rect buttonRect = new Rect(innerRect.xMax - 118f, innerRect.y, 118f, 28f);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, Mathf.Max(0f, innerRect.width - 126f), 28f);

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(titleRect, "PawnDiary.Settings.SimulatedPromptExampleTitle".Translate());
            Text.Font = previousFont;

            if (ButtonTextFit(
                buttonRect,
                (simulatedPromptExampleExpanded
                    ? "PawnDiary.Settings.SimulatedPromptExampleHide"
                    : "PawnDiary.Settings.SimulatedPromptExampleShow").Translate()))
            {
                simulatedPromptExampleExpanded = !simulatedPromptExampleExpanded;
            }

            Rect helpRect = new Rect(innerRect.x, titleRect.yMax + 4f, innerRect.width, innerRect.yMax - titleRect.yMax - 4f);
            DrawMutedLabel(helpRect, "PawnDiary.Settings.SimulatedPromptExampleHelp".Translate().ToString());

            if (!simulatedPromptExampleExpanded)
            {
                return;
            }

            listing.Gap(8f);
            Rect blockRect = listing.GetRect(SimulatedPromptExampleHeight);
            Widgets.DrawMenuSection(blockRect);
            Rect inner = blockRect.ContractedBy(8f);

            previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(new Rect(inner.x, inner.y, inner.width, 26f), "PawnDiary.Settings.SimulatedPromptExampleLabel".Translate());
            Text.Font = previousFont;

            Rect promptRect = new Rect(inner.x, inner.y + 32f, inner.width, Mathf.Max(0f, inner.yMax - inner.y - 32f));
            Widgets.TextArea(promptRect, "PawnDiary.Settings.SimulatedPromptExampleText".Translate().ToString());
        }
    }
}
