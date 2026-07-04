// Harmony patch that observes RimTalk after it has accepted/generated a chat message and is about
// to create the in-game interaction. This bridge only logs in the first version.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Listens to RimTalk's displayed-chat boundary and forwards the details to the bridge logger.
    /// </summary>
    [HarmonyPatch]
    public static class RimTalkCreateInteractionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(TalkService),
                "CreateInteraction",
                new[] { typeof(Pawn), typeof(TalkResponse) });

            if (method == null)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " could not find RimTalk.Service.TalkService.CreateInteraction(Pawn, TalkResponse); chat logging is disabled.",
                    "PawnDiaryRimTalkBridge.MissingCreateInteraction".GetHashCode());
            }

            return method;
        }

        private static void Postfix(Pawn pawn, TalkResponse talk)
        {
            RimTalkChatLogger.LogDisplayedChat(pawn, talk);
        }
    }
}
