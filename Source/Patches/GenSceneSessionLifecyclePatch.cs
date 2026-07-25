// Cancels background LLM transport as soon as RimWorld begins leaving a loaded game. GameComponent
// has a load/start hook but no matching unload callback, so this narrow base-game scene transition is
// the explicit other half of LlmClient.BeginSession. It touches no DLC content.
using System;
using HarmonyLib;
using Verse;

namespace PawnDiary
{
    /// <summary>Stops queued and in-flight API work before the main-menu scene replaces the game.</summary>
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    internal static class GenSceneSessionLifecyclePatch
    {
        private static void Prefix()
        {
            try
            {
                LlmClient.EndSession();
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnDiary] Could not fully cancel the LLM session while leaving the game: "
                    + ApiLaneLabels.TrimForLog(ex.Message));
            }

            try
            {
                Integration.ExternalLlmCompletionService.ResetSession();
            }
            catch (Exception ex)
            {
                Log.Warning("[PawnDiary] Could not fully reset external LLM completions while leaving the game: "
                    + ApiLaneLabels.TrimForLog(ex.Message));
            }
        }
    }
}
