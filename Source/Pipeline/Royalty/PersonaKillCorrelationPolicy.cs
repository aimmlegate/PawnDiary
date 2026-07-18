// Pure buffer decisions for the short-lived persona-kill ownership bridge. The runtime correlation
// cache owns Verse-backed signals, but whether one exact signal is staged, suppressed, or allowed to
// follow its ordinary path is plain deterministic policy and stays testable without RimWorld.
using System;

namespace PawnDiary
{
    /// <summary>Action for one signal that matched an active persona-kill scope.</summary>
    internal enum PersonaKillSignalAction
    {
        PassThrough,
        Stage,
        Suppress
    }

    /// <summary>Lossless bounded-buffer policy shared by Thought and companion-Tale adapters.</summary>
    internal static class PersonaKillCorrelationPolicy
    {
        /// <summary>
        /// Suppresses signals already owned by a durable milestone or already staged, stages a new
        /// signal while capacity remains, and fails open to ordinary capture on defensive overflow.
        /// </summary>
        public static PersonaKillSignalAction Decide(
            bool scopeClaimed,
            bool alreadyStaged,
            int stagedCount,
            int maximumStagedSignals)
        {
            if (scopeClaimed || alreadyStaged) return PersonaKillSignalAction.Suppress;
            int cap = Math.Max(0, maximumStagedSignals);
            return stagedCount < cap
                ? PersonaKillSignalAction.Stage
                : PersonaKillSignalAction.PassThrough;
        }

        /// <summary>
        /// Returns whether a transient kill scope is outside its elapsed-tick window. A clock that
        /// moves backwards is treated as stale as well, preventing state from surviving a load/new
        /// game boundary when a caller failed to clear it first.
        /// </summary>
        public static bool IsExpired(int openedTick, int currentTick, int correlationTicks)
        {
            long elapsed = (long)Math.Max(0, currentTick) - Math.Max(0, openedTick);
            return elapsed < 0 || elapsed > Math.Max(1, correlationTicks);
        }
    }
}
