// Optional compatibility guard for SpeakUp. Pawn Diary has no compile-time dependency on SpeakUp;
// this file finds its reply scheduler by reflection when the mod is loaded, then suppresses only the
// scheduling side effect while Pawn Diary renders social-log text for prompt evidence.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Blocks SpeakUp reply scheduling during Pawn Diary's own social-log text rendering.
    /// </summary>
    public static class SpeakUpReplySchedulingGuardPatch
    {
        private const string DialogManagerTypeName = "SpeakUp.DialogManager";
        private const string EnsueMethodName = "Ensue";

        private static bool patchRegistered;
        private static bool suppressReplyScheduling;

        /// <summary>
        /// True once Pawn Diary can render tagged social grammar without triggering SpeakUp replies.
        /// </summary>
        public static bool CanRenderTaggedGrammarSafely
        {
            get { return patchRegistered; }
        }

        /// <summary>
        /// Patches SpeakUp.DialogManager.Ensue(List&lt;string&gt;) when SpeakUp is loaded.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            Type dialogManagerType = AccessTools.TypeByName(DialogManagerTypeName);
            if (dialogManagerType == null)
            {
                return;
            }

            MethodBase target = AccessTools.Method(dialogManagerType, EnsueMethodName, new[] { typeof(List<string>) });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find SpeakUp reply scheduler; tagged social-log grammar will use fallback text.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SpeakUpReplySchedulingGuardPatch), nameof(Prefix)));
            patchRegistered = true;
        }

        /// <summary>
        /// Harmony Prefix for SpeakUp.DialogManager.Ensue.
        /// </summary>
        public static bool Prefix()
        {
            return !suppressReplyScheduling;
        }

        /// <summary>
        /// Temporarily suppresses SpeakUp reply scheduling around one Pawn Diary render call.
        /// </summary>
        public static IDisposable SuppressDuringCapture()
        {
            return new SuppressionScope();
        }

        private sealed class SuppressionScope : IDisposable
        {
            private readonly bool previous;

            public SuppressionScope()
            {
                previous = suppressReplyScheduling;
                suppressReplyScheduling = true;
            }

            public void Dispose()
            {
                suppressReplyScheduling = previous;
            }
        }
    }
}
