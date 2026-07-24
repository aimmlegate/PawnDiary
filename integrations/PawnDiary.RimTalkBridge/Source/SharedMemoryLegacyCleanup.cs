// SharedMemoryLegacyCleanup.cs — one-release cleanup path for the retired shared-memory feature
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §6). Older bridge versions auto-injected a
// "{{diary_shared}}" prompt entry into the player's active RimTalk preset; that entry PERSISTS in
// RimTalk's own settings, so simply deleting the feature would orphan it (RimTalk would render a
// dangling variable token forever). This class removes it once per loaded game and never adds
// anything back. Delete this file after one release cycle.
//
// RimTalk-type isolation (same convention as the deleted SharedMemoryInjector): the method that
// names a RimTalk type is [NoInlining] and only reached after the RimTalkActive guard, so the
// bridge still loads cleanly when RimTalk's API surface drifts.
using System;
using System.Runtime.CompilerServices;
using RimTalk.API;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Removes the legacy {{diary_shared}} prompt entry from the active RimTalk preset.</summary>
    internal static class SharedMemoryLegacyCleanup
    {
        private static bool cleanupDone;

        public static void ResetForNewGame()
        {
            cleanupDone = false;
        }

        /// <summary>
        /// Runs from the bridge's periodic pass, behind the RimTalkActive guard. Removal is keyed
        /// by our mod id — the shared entry was the ONLY prompt entry this bridge ever registered,
        /// so this cannot disturb anything else. Repeating once per loaded game is deliberate
        /// idempotent self-healing across preset switches.
        /// </summary>
        public static void RunOnce()
        {
            if (cleanupDone || !PawnDiaryRimTalkBridgeMod.RimTalkActive)
            {
                return;
            }

            cleanupDone = true;
            try
            {
                RemoveLegacyEntry();
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " could not remove the legacy "
                    + "{{diary_shared}} prompt entry (RimTalk prompt-entry API unavailable?); "
                    + "remove it manually from the RimTalk preset if it lingers: " + e,
                    "PawnDiaryRimTalkBridge.LegacySharedCleanup".GetHashCode());
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RemoveLegacyEntry()
        {
            RimTalkPromptAPI.RemovePromptEntriesByModId(BridgeIds.ModId);
        }
    }
}
