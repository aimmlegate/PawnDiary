// Shared settings-window widgets for Pawn Diary. These helpers keep small IMGUI idioms in one
// place while the feature-specific partial files stay focused on their settings sections.
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private static float DrawSliderRow(Rect rect, string label, float value, float min, float max)
        {
            const float labelWidth = 230f;
            Rect labelRect = new Rect(rect.x, rect.y, Mathf.Min(labelWidth, rect.width * 0.45f), rect.height);
            Rect sliderRect = new Rect(labelRect.xMax + 8f, rect.y + 4f, Mathf.Max(0f, rect.xMax - labelRect.xMax - 8f), rect.height - 8f);
            Widgets.LabelFit(labelRect, label);
            return Widgets.HorizontalSlider(sliderRect, value, min, max);
        }

        /// <summary>
        /// Draws a short label and text field inside one row. This mirrors TextEntryLabeled but lets
        /// two settings share a line without clipping labels into neighboring controls.
        /// </summary>
        private static string DrawCompactTextField(Rect rect, string label, string value, float labelWidth)
        {
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            Widgets.LabelFit(labelRect, label);
            return Widgets.TextField(fieldRect, value ?? string.Empty);
        }

        /// <summary>Draws one muted status label without changing the caller's font or color.</summary>
        private static void DrawMutedLabel(Rect rect, string text)
        {
            Color previousColor = GUI.color;
            GUI.color = HintColor;
            Widgets.LabelFit(rect, text ?? string.Empty);
            GUI.color = previousColor;
        }

        /// <summary>Draws a compact field label inside an editor block.</summary>
        private static void DrawFieldLabel(Rect rect, string text)
        {
            Widgets.LabelFit(rect, text ?? string.Empty);
        }

        /// <summary>
        /// Draws a standard RimWorld button with LabelFit text, which keeps long translated labels
        /// readable in fixed-width settings rows.
        /// </summary>
        private static bool ButtonTextFit(Rect rect, string label)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect labelRect = new Rect(
                rect.x + 6f,
                rect.y + 2f,
                Mathf.Max(0f, rect.width - 12f),
                Mathf.Max(0f, rect.height - 4f));
            Widgets.LabelFit(labelRect, label ?? string.Empty);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            return clicked;
        }

        /// <summary>Draws a compact symbol button and exposes its full meaning in a tooltip.</summary>
        private static bool ButtonSymbolWithTip(Rect rect, string symbol, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, symbol ?? string.Empty);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }

        /// <summary>
        /// Draws a section title (medium font) with a divider line beneath it, giving the settings
        /// window a clear visual hierarchy instead of a flat run of same-size labels.
        /// </summary>
        private static void SectionTitle(Listing_Standard listing, string label)
        {
            listing.Gap(10f);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Rect rect = listing.GetRect(Text.LineHeight);
            Widgets.LabelFit(rect, label);
            Text.Font = previousFont;
            listing.GapLine(6f);
        }

        /// <summary>Draws a small accent label without permanently changing the caller's GUI state.</summary>
        private static void DrawAccentLabel(Rect rect, string text)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = AccentColor;
            Widgets.LabelFit(rect, text ?? string.Empty);
            GUI.color = previousColor;
            Text.Font = previousFont;
        }
    }
}
