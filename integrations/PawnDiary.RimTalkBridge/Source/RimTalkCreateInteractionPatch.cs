// Harmony patch that observes RimTalk right after it has accepted/generated a chat message and is
// about to create the in-game interaction (RimTalk.Service.TalkService.CreateInteraction, which fires
// once per displayed chat line). We only READ the line and forward it to ConversationTracker; we never
// change RimTalk's behavior.
//
// Why manual TryRegister (not [HarmonyPatch]): the method is private and could be renamed by a RimTalk
// update. Resolving and patching it independently lets the bridge report real hook health to Pawn
// Diary. When resolution fails, core ambient XML remains active instead of silently losing all chat.
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
    public static class RimTalkCreateInteractionPatch
    {
        /// <summary>
        /// Resolves and installs the exact RimTalk displayed-chat postfix. Returns false on any target
        /// drift or Harmony failure so the caller can leave Pawn Diary's ambient XML fallback active.
        /// </summary>
        public static bool TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return false;
            }

            MethodInfo method = AccessTools.Method(
                typeof(TalkService),
                "CreateInteraction",
                new[] { typeof(Pawn), typeof(TalkResponse) });

            if (method == null)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " could not find RimTalk.Service.TalkService.CreateInteraction(Pawn, TalkResponse);"
                    + " rich conversation capture is unavailable and Pawn Diary's ambient RimTalk"
                    + " fallback will remain active at bridge Level 2.",
                    "PawnDiaryRimTalkBridge.MissingCreateInteraction".GetHashCode());
                return false;
            }

            MethodInfo postfix = AccessTools.Method(
                typeof(RimTalkCreateInteractionPatch),
                nameof(Postfix));
            if (postfix == null)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " could not resolve its RimTalk conversation postfix; the ambient fallback"
                    + " will remain active at bridge Level 2.",
                    "PawnDiaryRimTalkBridge.MissingCreateInteractionPostfix".GetHashCode());
                return false;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception e)
            {
                Log.Error(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " failed to install the RimTalk displayed-conversation hook; Pawn Diary's"
                    + " ambient fallback will remain active at bridge Level 2: " + e);
                return false;
            }
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
