// Expanded-card height measurement for the Diary tab. This helper is intentionally still a UI-layer
// class: it uses Verse/Unity text measurement so the calculated height matches RimWorld's IMGUI draw
// pass. The inspector tab owns selection, scrolling, and expansion state; this class owns the
// wrapped-text height cache for opened cards.
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Matches the roleplay prose measurer used by the Diary tab's draw pass.
    /// </summary>
    internal delegate float DiaryRoleplayTextHeightCalculator(
        string text,
        float width,
        string atmosphereCue,
        bool allowDirectSpeechBlocks,
        DiaryTextDecorationContext decorationContext,
        int seed,
        IEnumerable<DiaryNameHighlight> nameHighlights);

    /// <summary>
    /// Explicit inputs needed to measure one expanded diary entry card.
    /// </summary>
    internal struct DiaryEntryCardMeasureRequest
    {
        public string EntryKey;
        public float Width;
        public bool ShowLlmDebugInfo;
        public string BodyText;
        public string DebugText;
        public string AtmosphereCue;
        public bool AllowDirectSpeechBlocks;
        public DiaryTextDecorationContext DecorationContext;
        public int TextSeed;
        public IEnumerable<DiaryNameHighlight> NameHighlights;
        public bool HasLinkedEntry;
        public bool HasFooterNote;
        public float EntryTextTop;
        public float EntryBottomPadding;
        public float LinkedEntryPadding;
        public float LinkedEntryTotalHeight;
        public float ModelNameTopPadding;
        public float ModelNameHeight;
        public float DebugTextTopPadding;
        public float DevFooterHeight;
        public DiaryRoleplayTextHeightCalculator RoleplayTextHeight;
    }

    /// <summary>
    /// Measures expanded diary entry cards and caches the result until a layout input changes.
    /// </summary>
    internal sealed class DiaryEntryCardMeasurer
    {
        private readonly Dictionary<string, float> heightCache = new Dictionary<string, float>();
        private float cacheWidth = -1f;
        private bool cacheShowDebug;
        private DiaryRenderToken cacheToken;
        private int cacheHighlightVersion = -1;

        /// <summary>
        /// Returns the full expanded height for one entry, reusing wrapped-text measurement while the
        /// render token, width, debug flag, and highlight set are unchanged.
        /// </summary>
        public float CachedHeight(DiaryEntryCardMeasureRequest request, DiaryRenderToken token, int highlightVersion)
        {
            if (request.Width != cacheWidth
                || request.ShowLlmDebugInfo != cacheShowDebug
                || highlightVersion != cacheHighlightVersion
                || !token.Equals(cacheToken))
            {
                heightCache.Clear();
                cacheWidth = request.Width;
                cacheShowDebug = request.ShowLlmDebugInfo;
                cacheHighlightVersion = highlightVersion;
                cacheToken = token;
            }

            string key = request.EntryKey ?? string.Empty;
            float height;
            if (!heightCache.TryGetValue(key, out height))
            {
                height = MeasureExpandedHeight(request);
                heightCache[key] = height;
            }

            return height;
        }

        /// <summary>
        /// Measures the tiny diagnostic text block shown only when the dev debug toggle is enabled.
        /// </summary>
        public static float DebugTextHeight(string debugText, float width)
        {
            if (string.IsNullOrWhiteSpace(debugText))
            {
                return 0f;
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float height = Text.CalcHeight(debugText, width);
            Text.Font = oldFont;
            return height;
        }

        private static float MeasureExpandedHeight(DiaryEntryCardMeasureRequest request)
        {
            // Must match the draw width in FillTab (entryRect.width - 20f) so the measured wrap
            // height equals what is actually rendered; a wider measure clips long entries at the bottom.
            float innerWidth = request.Width - 20f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float textHeight = request.RoleplayTextHeight(
                request.BodyText,
                innerWidth,
                request.AtmosphereCue,
                request.AllowDirectSpeechBlocks,
                request.DecorationContext,
                request.TextSeed,
                request.NameHighlights);
            Text.Font = oldFont;

            float height = request.EntryTextTop + textHeight + request.EntryBottomPadding;

            if (request.HasLinkedEntry)
            {
                height += request.LinkedEntryTotalHeight + request.LinkedEntryPadding;
            }

            if (request.HasFooterNote)
            {
                height += request.ModelNameTopPadding + request.ModelNameHeight;
            }

            if (request.ShowLlmDebugInfo)
            {
                float debugHeight = DebugTextHeight(request.DebugText, innerWidth);
                if (debugHeight > 0f)
                {
                    height += request.DebugTextTopPadding + debugHeight;
                }
            }

            // Reserve the dev-only footer so the bottom-left copy badge clears the model-name line.
            // Outside dev mode the request passes 0 and production card heights are unchanged.
            height += request.DevFooterHeight;
            return height;
        }
    }
}
