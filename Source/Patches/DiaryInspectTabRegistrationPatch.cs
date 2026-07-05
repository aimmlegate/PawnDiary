// Keeps the Diary tab registration fresh before RimWorld draws the pawn inspect-tab row.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;
using RimWorld;

namespace PawnDiary
{
    /// <summary>
    /// Ensures the hidden Diary inspect tab is registered before RimWorld lays out inspect tabs.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    internal static class DiaryInspectTabRegistrationPatch
    {
        /// <summary>
        /// Runs just before RimWorld draws the inspect-tab strip, keeping the Diary tab registered.
        /// </summary>
        public static void Prefix()
        {
            DiaryModStartup.EnsureDiaryTabInjected();
        }
    }
}
