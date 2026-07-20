// Tiny bounded cross-call owner for vanilla DoRejection -> DoLeave/DoAggressive nesting. The outer
// rejection must remain the semantic page, while the same exact methods called from unpatched
// DoDownside naturally own their own outcome. Only stable pawn IDs are retained, never live objects.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Tracks synchronous rejection ownership and is cleared at every game lifecycle boundary.</summary>
    internal static class CreepJoinerOutcomeScope
    {
        private const int MaximumDepth = 32;
        private static readonly List<string> ActiveRejections = new List<string>();

        internal static bool BeginRejection(string pawnId)
        {
            string id = (pawnId ?? string.Empty).Trim();
            if (id.Length == 0 || ActiveRejections.Count >= MaximumDepth) return false;
            ActiveRejections.Add(id);
            return true;
        }

        internal static bool OwnsRejection(string pawnId)
        {
            string id = (pawnId ?? string.Empty).Trim();
            return id.Length > 0 && ActiveRejections.Count > 0
                && string.Equals(
                    ActiveRejections[ActiveRejections.Count - 1], id, StringComparison.Ordinal);
        }

        internal static void EndRejection(string pawnId)
        {
            string id = (pawnId ?? string.Empty).Trim();
            int last = ActiveRejections.Count - 1;
            if (last < 0) return;
            if (string.Equals(ActiveRejections[last], id, StringComparison.Ordinal))
                ActiveRejections.RemoveAt(last);
            else
                // A mismatched unwind means a mod caused unusual reentrancy. Clearing is safer than
                // allowing ownership to leak into an unrelated later lifecycle call.
                ActiveRejections.Clear();
        }

        internal static void Clear()
        {
            ActiveRejections.Clear();
        }

        internal static int CountForTests => ActiveRejections.Count;
    }
}
