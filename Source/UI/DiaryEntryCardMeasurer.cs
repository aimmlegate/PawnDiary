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
    /// Explicit inputs needed to measure one expanded diary entry card.
    /// </summary>
    internal struct DiaryEntryCardMeasureRequest
    {
        public string EntryKey;
        public float Width;
        public bool ShowLlmDebugInfo;
        public float BodyTextHeight;
        public string DebugText;
        public bool HasLinkedEntry;
        public bool HasFooterLine;
        public float EntryTextTop;
        public float EntryBottomPadding;
        public float LinkedEntryPadding;
        public float LinkedEntryTotalHeight;
        public float ModelNameTopPadding;
        public float FooterLineHeight;
        public float DebugTextTopPadding;
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
        private float cacheFooterLineHeight = -1f;

        /// <summary>
        /// Drops every session-bound measurement and its cache identity.
        /// </summary>
        public void Clear()
        {
            heightCache.Clear();
            cacheWidth = -1f;
            cacheShowDebug = false;
            cacheToken = default(DiaryRenderToken);
            cacheHighlightVersion = -1;
            cacheFooterLineHeight = -1f;
        }

        /// <summary>
        /// Tries to reuse an expanded height while the render token, width, debug flag, highlight set,
        /// and effective footer-font height are unchanged.
        /// </summary>
        public bool TryGetCachedHeight(
            string entryKey,
            float width,
            bool showLlmDebugInfo,
            DiaryRenderToken token,
            int highlightVersion,
            float footerLineHeight,
            out float height)
        {
            if (width != cacheWidth
                || showLlmDebugInfo != cacheShowDebug
                || highlightVersion != cacheHighlightVersion
                || footerLineHeight != cacheFooterLineHeight
                || !token.Equals(cacheToken))
            {
                heightCache.Clear();
                cacheWidth = width;
                cacheShowDebug = showLlmDebugInfo;
                cacheHighlightVersion = highlightVersion;
                cacheFooterLineHeight = footerLineHeight;
                cacheToken = token;
            }

            return heightCache.TryGetValue(entryKey ?? string.Empty, out height);
        }

        /// <summary>
        /// Measures a cache miss whose body prose has already been prepared for drawing.
        /// </summary>
        public float MeasureAndCache(DiaryEntryCardMeasureRequest request)
        {
            float height = MeasureExpandedHeight(request);
            heightCache[request.EntryKey ?? string.Empty] = height;
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
            try
            {
                Text.Font = GameFont.Tiny;
                return Text.CalcHeight(debugText, width);
            }
            finally
            {
                Text.Font = oldFont;
            }
        }

        private static float MeasureExpandedHeight(DiaryEntryCardMeasureRequest request)
        {
            // Must match the draw width in FillTab (entryRect.width - 20f) so the measured wrap
            // height equals what is actually rendered; a wider measure clips long entries at the bottom.
            float innerWidth = request.Width - 20f;

            float textHeight = request.BodyTextHeight;

            float height = request.EntryTextTop + textHeight + request.EntryBottomPadding;

            if (request.HasLinkedEntry)
            {
                height += request.LinkedEntryTotalHeight + request.LinkedEntryPadding;
            }

            if (request.HasFooterLine)
            {
                height += request.ModelNameTopPadding + request.FooterLineHeight;
            }

            if (request.ShowLlmDebugInfo)
            {
                float debugHeight = DebugTextHeight(request.DebugText, innerWidth);
                if (debugHeight > 0f)
                {
                    height += request.DebugTextTopPadding + debugHeight;
                }
            }

            return height;
        }
    }
}
