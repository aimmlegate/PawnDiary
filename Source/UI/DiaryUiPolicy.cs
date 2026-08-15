// Pure layout, cache, and editor-save decisions shared by the diary UI. Keeping these tiny policies
// free of Verse and Unity types lets standalone tests cover narrow-window and game-session edge cases.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Pure policy helpers for responsive diary UI layout, session-bound caches, and draft saves.
    /// </summary>
    internal static class DiaryUiPolicy
    {
        /// <summary>
        /// Returns whether a cache owner belongs to a different game-session object.
        /// </summary>
        public static bool SessionChanged(object cachedSession, object currentSession)
        {
            return !ReferenceEquals(cachedSession, currentSession);
        }

        /// <summary>
        /// Returns whether a static navigation request belongs to another loaded game. A request made
        /// before this view's first draw is retained when it already names the current component.
        /// </summary>
        public static bool ShouldClearPendingRequest(
            object requestSession,
            object currentSession)
        {
            return SessionChanged(requestSession, currentSession);
        }

        /// <summary>
        /// Returns whether a reader-directory cache belongs to another game or component instance.
        /// </summary>
        public static bool ReaderDirectorySessionChanged(
            object cachedGame,
            object currentGame,
            object cachedComponent,
            object currentComponent)
        {
            return SessionChanged(cachedGame, currentGame)
                || SessionChanged(cachedComponent, currentComponent);
        }

        /// <summary>
        /// Returns whether the year selector must move into the journal because the side panel is hidden.
        /// </summary>
        public static bool ShouldShowInlineYearSelector(float filterPanelWidth, int yearCount)
        {
            return yearCount > 1
                && (float.IsNaN(filterPanelWidth)
                    || float.IsInfinity(filterPanelWidth)
                    || filterPanelWidth <= 1f);
        }

        /// <summary>
        /// Returns one row when all psychotype controls fit and three stacked rows otherwise.
        /// </summary>
        public static int PsychotypeControlRowCount(
            float availableWidth,
            float minimumPickerWidth,
            float rerollWidth,
            float pinWidth,
            float gap)
        {
            if (float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || availableWidth <= 0f)
            {
                return 3;
            }

            float requiredWidth = Math.Max(0f, minimumPickerWidth)
                + Math.Max(0f, rerollWidth)
                + Math.Max(0f, pinWidth)
                + Math.Max(0f, gap) * 2f;
            return availableWidth >= requiredWidth ? 1 : 3;
        }

        /// <summary>
        /// Returns a footer row tall enough for the effective Tiny font while retaining the XML-owned
        /// configured height as a minimum. RimWorld can map Tiny to Small for accessibility/locales.
        /// </summary>
        public static float EffectiveFooterLineHeight(
            float configuredMinimumHeight,
            float measuredLineHeight)
        {
            float configured = float.IsNaN(configuredMinimumHeight)
                || float.IsInfinity(configuredMinimumHeight)
                ? 0f
                : Math.Max(0f, configuredMinimumHeight);
            float measured = float.IsNaN(measuredLineHeight)
                || float.IsInfinity(measuredLineHeight)
                ? 0f
                : Math.Max(0f, measuredLineHeight);
            return Math.Max(configured, measured);
        }

        /// <summary>
        /// Returns whether a canonical memory-editor draft differs from the canonical text shown when
        /// editing began. Callers sanitize both values first, so markup or whitespace-only differences
        /// cannot freeze a currently localized XML template into a manual override.
        /// </summary>
        public static bool MemoryDraftNeedsPersistence(
            string initialCanonicalText,
            string draftCanonicalText)
        {
            return !string.Equals(
                initialCanonicalText ?? string.Empty,
                draftCanonicalText ?? string.Empty,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether the memory editor should offer Remove for one event kind. The adapter passes
        /// the central schema token so this pure UI helper does not duplicate persistence identifiers.
        /// </summary>
        public static bool ShouldOfferMemoryRemove(
            string eventKind,
            string protectedEventKind)
        {
            return string.IsNullOrWhiteSpace(protectedEventKind)
                || !string.Equals(
                    eventKind ?? string.Empty,
                    protectedEventKind,
                    StringComparison.Ordinal);
        }
    }
}
