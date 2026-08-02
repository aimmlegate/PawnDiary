// One defensive choke point for Harmony patch bodies. Our prefixes/postfixes run *inside* the
// vanilla method we hooked, so an exception escaping one of them propagates into that game method
// and breaks the mechanic (death, recruitment, incidents, social logging, ...) for the whole game.
// These helpers run a patch body, and on the first failure disable that exact observation context,
// warn once, and let vanilla continue as if the diary hook were not there. See AGENTS.md
// ("Harmony patches").
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
        private static readonly PatchCircuitBreaker CircuitBreaker = new PatchCircuitBreaker();

        /// <summary>
        /// Re-enables contexts disabled by a previous loaded game. Static state otherwise survives an
        /// exit to the menu and could unnecessarily disable a healthy hook in the next session.
        /// </summary>
        public static void ResetSession()
        {
            CircuitBreaker.Reset();
        }

        /// <summary>
        /// Runs a void prefix/postfix body. The first exception warns and disables this exact context
        /// for the loaded game; later calls skip it, leaving the vanilla method's result untouched.
        /// </summary>
        public static void Run(string context, Action body)
        {
            if (CircuitBreaker.IsDisabled(context))
            {
                return;
            }

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
        /// Zero-allocation variant of <see cref="Run(string,Action)"/> for patch bodies on HOT vanilla
        /// paths (every Thing spawn, every hediff, every gained memory). A lambda that captures locals
        /// allocates a closure object on every call; passing the inputs through <paramref name="state"/>
        /// instead lets the body be a non-capturing lambda, which the C# compiler caches in a static
        /// field — so the patch adds no per-call GC pressure. Bundle several inputs as a value tuple.
        /// The body must read inputs ONLY from its state parameter: touching an enclosing local or
        /// method parameter reintroduces the very capture this overload exists to avoid.
        /// </summary>
        public static void Run<TState>(string context, TState state, Action<TState> body)
        {
            if (CircuitBreaker.IsDisabled(context))
            {
                return;
            }

            try
            {
                body(state);
            }
            catch (Exception e)
            {
                LogFailure(context, e);
            }
        }

        /// <summary>
        /// Result-returning companion to the state-passing overload. It keeps frequent prefixes free
        /// of captured closures while still returning detached Harmony __state; failures log once and
        /// return the caller's safe fallback.
        /// </summary>
        public static TResult Run<TState, TResult>(
            string context,
            TState state,
            Func<TState, TResult> body,
            TResult fallback)
        {
            if (CircuitBreaker.IsDisabled(context))
            {
                return fallback;
            }

            try
            {
                return body(state);
            }
            catch (Exception e)
            {
                LogFailure(context, e);
                return fallback;
            }
        }

        /// <summary>
        /// Runs a bool-returning prefix body (the return decides whether vanilla runs). On failure it
        /// logs once and returns <paramref name="fallback"/> — pass the value that lets vanilla proceed
        /// normally, so a broken diary hook never suppresses the original behavior.
        /// </summary>
        public static bool RunPrefix(string context, bool fallback, Func<bool> body)
        {
            if (CircuitBreaker.IsDisabled(context))
            {
                return fallback;
            }

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
            // Disable before diagnostics: a second callback on another thread must already see the open
            // circuit, and a diagnostics failure must never leave a hot path retrying broken code.
            if (!CircuitBreaker.Disable(context))
            {
                return;
            }

            DiaryTelemetryReporter.RecordException(
                DiaryTelemetryOutcome.PatchException,
                "harmony.patch",
                context,
                null,
                e,
                -1);
            Log.Warning(
                "[Pawn Diary] " + (context ?? string.Empty)
                    + " failed and its diary hook was disabled for the rest of this game session; "
                    + "vanilla behavior will continue: " + e);
        }
    }
}
