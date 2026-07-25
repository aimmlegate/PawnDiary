// Pure layout and cache decisions shared by the diary UI. Keeping these tiny policies free of
// Verse and Unity types lets standalone tests cover narrow-window and game-session edge cases.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Pure policy helpers for responsive diary UI layout and session-bound caches.
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
    }
}
