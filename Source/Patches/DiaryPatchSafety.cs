// One defensive choke point for Harmony patch bodies. Our prefixes/postfixes run *inside* the
// vanilla method we hooked, so an exception escaping one of them propagates into that game method
// and breaks the mechanic (death, recruitment, incidents, social logging, ...) for the whole game.
// These helpers run a patch body, and on any failure log it once and let vanilla continue as if the
// diary hook were not there. See AGENTS.md ("Harmony patches").
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Wraps Harmony patch bodies so a diary-capture failure degrades to "no diary entry" instead of
    /// breaking the patched vanilla method.
    /// </summary>
    internal static class DiaryPatchSafety
    {
        /// <summary>
        /// Runs a void prefix/postfix body. Any exception is logged once (keyed on <paramref name="context"/>,
        /// so a recurring failure reports a single time instead of spamming) and swallowed, leaving the
        /// vanilla method's own result untouched.
        /// </summary>
        public static void Run(string context, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                LogFailure(context, e);
            }
        }

        /// <summary>
        /// Runs a bool-returning prefix body (the return decides whether vanilla runs). On failure it
        /// logs once and returns <paramref name="fallback"/> — pass the value that lets vanilla proceed
        /// normally, so a broken diary hook never suppresses the original behavior.
        /// </summary>
        public static bool RunPrefix(string context, bool fallback, Func<bool> body)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                LogFailure(context, e);
                return fallback;
            }
        }

        private static void LogFailure(string context, Exception e)
        {
            // GetHashCode is deterministic within a session, which is all Log.ErrorOnce needs to dedup;
            // one report per patch keeps a persistent failure from flooding the log.
            Log.ErrorOnce("[Pawn Diary] " + context + " failed and was skipped: " + e, context.GetHashCode());
        }
    }
}
