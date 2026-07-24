// Central routing for every programmatic diary open.
// The saved alternative-mode setting chooses either the classic inspect-tab host or the standalone
// reader, so social-log links, linked cards, dev previews, and commands cannot drift apart.
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Routes diary navigation to the active UI mode.
    /// </summary>
    internal static class DiaryUiRouter
    {
        public static bool ReaderWindowMode
        {
            get { return PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.useDiaryReaderWindow; }
        }

        /// <summary>
        /// Opens the requested pawn's diary in the configured host.
        /// </summary>
        public static bool OpenDiaryFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (ReaderWindowMode)
            {
                Dialog_DiaryReader.Open(pawn);
                return Find.WindowStack != null;
            }

            return ITab_Pawn_Diary.OpenDiaryTab() is ITab_Pawn_Diary;
        }

        /// <summary>
        /// Opens a diary and requests that its host scroll to one event.
        /// </summary>
        public static bool OpenDiaryAt(Pawn pawn, string eventId)
        {
            if (pawn == null)
            {
                return false;
            }

            DiaryJournalView.RequestScrollToEntry(pawn, eventId);
            bool opened = OpenDiaryFor(pawn);
            if (!opened)
            {
                DiaryJournalView.ClearPendingScrollRequest();
            }

            return opened;
        }

        /// <summary>
        /// Applies the host transition after settings are saved.
        /// </summary>
        public static void ApplyReaderWindowModeChange(bool wasReaderMode, bool isReaderMode)
        {
            if (wasReaderMode == isReaderMode)
            {
                return;
            }

            if (!isReaderMode)
            {
                Dialog_DiaryReader.CloseAll();
                return;
            }

            // Mod settings can be opened from RimWorld's main menu, where the active root is
            // UIRoot_Entry rather than UIRoot_Play. Find.MainTabsRoot assumes the play root and
            // throws an InvalidCastException there, which aborts Dialog_ModSettings.PreClose and
            // leaves the settings window impossible to close. There is no inspect tab to dismiss
            // outside a running game, so stop before touching that play-only accessor.
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                return;
            }

            MainTabWindow_Inspect inspectWindow =
                MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
            if (inspectWindow != null
                && Find.MainTabsRoot != null
                && Find.MainTabsRoot.OpenTab == MainButtonDefOf.Inspect
                && inspectWindow.OpenTabType == typeof(ITab_Pawn_Diary))
            {
                inspectWindow.CloseOpenTab();
            }
        }
    }
}
