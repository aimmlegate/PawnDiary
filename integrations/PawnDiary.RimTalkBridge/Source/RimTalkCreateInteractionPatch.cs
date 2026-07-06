// Harmony patch that observes RimTalk right after it has accepted/generated a chat message and is
// about to create the in-game interaction (RimTalk.Service.TalkService.CreateInteraction, which fires
// once per displayed chat line). We only READ the line and forward it to ConversationTracker; we never
// change RimTalk's behavior.
//
// Why the manual TargetMethod (not [HarmonyPatch(typeof(TalkService), "CreateInteraction")]): the
// method is private and could be renamed by a RimTalk update. Resolving it by reflection with a null
// guard lets a changed RimTalk degrade to "conversation capture disabled" instead of throwing during
// PatchAll and taking down the whole mod. The patch class itself is only ever reached when RimTalk is
// active (the mod constructor guards PatchAll behind RimTalkActive).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo. See also SKILL.md "Optional-mod hooks".
using System;
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Listens to RimTalk's displayed-chat boundary and forwards each line to the conversation tracker.
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
                    + " could not find RimTalk.Service.TalkService.CreateInteraction(Pawn, TalkResponse);"
                    + " conversation capture is disabled.",
                    "PawnDiaryRimTalkBridge.MissingCreateInteraction".GetHashCode());
            }

            return method;
        }

        private static void Postfix(Pawn pawn, TalkResponse talk)
        {
            // Never let an exception in our observer disturb RimTalk's own chat flow.
            try
            {
                ConversationTracker.RecordDisplayedChat(pawn, talk);
            }
            catch (Exception e)
            {
                Log.ErrorOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " failed while recording a RimTalk chat line: " + e,
                    "PawnDiaryRimTalkBridge.RecordChat.Exception".GetHashCode());
            }
        }
    }
}
