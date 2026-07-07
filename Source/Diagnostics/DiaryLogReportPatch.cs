// Capture hook for the error reporter. A Harmony postfix on Verse.Log.Error / Log.ErrorOnce sees
// every error the game logs, but we forward ONLY the ones this mod raised — identified by our
// "[Pawn Diary]" message prefix — so other mods' and the base game's errors are never reported.
//
// Two safety rails, because Log.Error is a shared, hot-ish choke point on any thread:
//   * A [ThreadStatic] re-entrancy flag: if reporting ever triggers a Log.Error, we must not capture
//     our own report and loop forever.
//   * The whole path is wrapped so a fault here can never turn one logged error into a crash.
// See DiaryErrorReporter for the transport, and AGENTS.md ("Harmony patches").
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Forwards this mod's own logged errors to <see cref="DiaryErrorReporter"/>. Registered manually
    /// from <see cref="DiaryPatchRegistrar"/>.
    /// </summary>
    internal static class DiaryLogReportPatch
    {
        // Prefixes our own log lines start with. Only messages beginning with one of these are ours;
        // everything else in the game's log is ignored.
        private static readonly string[] ModLogPrefixes = { "[Pawn Diary]", "[PawnDiary" };

        // Guards against a report path that itself logs an error re-entering this postfix and looping.
        [ThreadStatic]
        private static bool capturing;

        /// <summary>
        /// Postfixes <c>Log.Error(string)</c> and <c>Log.ErrorOnce(string, int)</c> so raised errors
        /// flow to the reporter. No-ops cleanly if either method can't be found on this RimWorld build.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            try
            {
                HarmonyMethod postfix = new HarmonyMethod(typeof(DiaryLogReportPatch), nameof(Postfix));

                MethodBase error = AccessTools.Method(typeof(Log), "Error", new[] { typeof(string) });
                if (error != null)
                {
                    harmony.Patch(error, postfix: postfix);
                }
                else
                {
                    Log.Warning("[Pawn Diary] Error reporter could not patch Log.Error; raised errors will not be reported.");
                }

                MethodBase errorOnce = AccessTools.Method(typeof(Log), "ErrorOnce", new[] { typeof(string), typeof(int) });
                if (errorOnce != null)
                {
                    harmony.Patch(errorOnce, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                // Registration is best-effort; if it fails the mod runs exactly as before, just without reporting.
                Log.Warning("[Pawn Diary] Error reporter patch registration failed: " + e);
            }
        }

        /// <summary>
        /// Harmony postfix shared by both targets. The parameter name <c>text</c> matches the argument
        /// name on both <c>Log.Error</c> and <c>Log.ErrorOnce</c>, so Harmony injects the logged text.
        /// </summary>
        public static void Postfix(string text)
        {
            if (capturing || string.IsNullOrEmpty(text) || !IsModMessage(text))
            {
                return;
            }

            capturing = true;
            try
            {
                DiaryErrorReporter.Report(text);
            }
            catch
            {
                // Never propagate a reporter fault back into Log.Error's caller.
            }
            finally
            {
                capturing = false;
            }
        }

        private static bool IsModMessage(string text)
        {
            for (int i = 0; i < ModLogPrefixes.Length; i++)
            {
                if (text.StartsWith(ModLogPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
