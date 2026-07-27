// Capture hook for the error reporter. A Harmony postfix on Verse.Log.Error sees
// every error the game logs, but we forward ONLY the ones the Pawn Diary mod family raised — the main
// mod AND its first-party integration submods (the bridges), which all log through Verse.Log — so this
// one global postfix captures them all. The "is this one of ours?" test is the pure, unit-tested
// ModErrorPrefixPolicy (it matches each family log prefix); other mods' and the base game's errors are
// never reported.
//
// Verse.Log.ErrorOnce delegates accepted first occurrences to Verse.Log.Error. Patching only Error
// therefore observes exactly the entries RimWorld actually emits and does not mistake a suppressed
// duplicate ErrorOnce call for a new local error.
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
    /// Forwards the Pawn Diary family's logged errors (main mod + first-party submods) to
    /// <see cref="DiaryErrorReporter"/>, as classified by <see cref="ModErrorPrefixPolicy"/>. Registered
    /// manually from <see cref="DiaryPatchRegistrar"/>.
    /// </summary>
    internal static class DiaryLogReportPatch
    {
        // Guards against a report path that itself logs an error re-entering this postfix and looping.
        [ThreadStatic]
        private static bool capturing;

        /// <summary>
        /// Postfixes <c>Log.Error(string)</c> so locally raised errors flow to the reporter. No-ops
        /// cleanly if the method cannot be found on this RimWorld build.
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
                    DiaryPatchManifest.Report(
                        "Diagnostics",
                        "Log.Error(string)",
                        DiaryPatchManifest.HookStatus.Applied);
                }
                else
                {
                    Log.Warning("[Pawn Diary] Error reporter could not patch Log.Error; raised errors will not be reported.");
                    DiaryPatchManifest.Report(
                        "Diagnostics",
                        "Log.Error(string)",
                        DiaryPatchManifest.HookStatus.Degraded,
                        "target not found; errors will not be reported");
                }
            }
            catch (Exception e)
            {
                // Registration is best-effort; if it fails the mod runs exactly as before, just without reporting.
                Log.Warning("[Pawn Diary] Error reporter patch registration failed: " + e);
                DiaryPatchManifest.Report(
                    "Diagnostics",
                    "Log error reporter",
                    DiaryPatchManifest.HookStatus.Failed,
                    e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Harmony postfix for <c>Log.Error</c>. The parameter name <c>text</c> matches the target
        /// argument, so Harmony injects the exact text RimWorld already accepted for local logging.
        /// </summary>
        public static void Postfix(string text)
        {
            // ModErrorPrefixPolicy already treats null/empty as "not ours", so no separate guard here.
            if (capturing || !ModErrorPrefixPolicy.IsModErrorMessage(text))
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
    }
}
