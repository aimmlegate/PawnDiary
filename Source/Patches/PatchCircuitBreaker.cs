// Pure runtime state for the Harmony safety boundary. A patch context that throws once is marked
// disabled, so hot vanilla paths do not keep paying for the same incompatible diary observation.
// This file deliberately has no Verse/RimWorld/Unity dependencies and is covered by a standalone
// test harness.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Tracks disabled patch contexts with lock-free reads and copy-on-write updates. Reads dominate:
    /// healthy Harmony hooks consult this object on every call, while writes happen only after a fault.
    /// </summary>
    internal sealed class PatchCircuitBreaker
    {
        private readonly object gate = new object();

        // Published dictionaries are never mutated. Replacing this volatile reference after copying
        // lets frequent hook calls read safely without taking a lock or allocating.
        private volatile Dictionary<string, bool> disabledContexts =
            new Dictionary<string, bool>(System.StringComparer.Ordinal);

        /// <summary>Returns whether the named patch context has already failed this session.</summary>
        public bool IsDisabled(string context)
        {
            Dictionary<string, bool> snapshot = disabledContexts;
            return snapshot.ContainsKey(Normalize(context));
        }

        /// <summary>
        /// Disables one context. Returns true only to the caller that performed the first transition,
        /// which lets the impure adapter emit exactly one warning even under concurrent callbacks.
        /// </summary>
        public bool Disable(string context)
        {
            string key = Normalize(context);
            lock (gate)
            {
                Dictionary<string, bool> current = disabledContexts;
                if (current.ContainsKey(key))
                {
                    return false;
                }

                Dictionary<string, bool> replacement =
                    new Dictionary<string, bool>(current, System.StringComparer.Ordinal)
                    {
                        [key] = true
                    };
                disabledContexts = replacement;
                return true;
            }
        }

        /// <summary>Clears session-scoped failures when a different Game is constructed.</summary>
        public void Reset()
        {
            lock (gate)
            {
                disabledContexts =
                    new Dictionary<string, bool>(System.StringComparer.Ordinal);
            }
        }

        private static string Normalize(string context)
        {
            return context ?? string.Empty;
        }
    }
}
