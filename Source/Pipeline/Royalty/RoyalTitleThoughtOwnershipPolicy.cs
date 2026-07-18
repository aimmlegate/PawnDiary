// Pure exact matching for Thought_MemoryRoyalTitle ownership. Runtime adapters stage the unchanged
// ThoughtSignal briefly; this file decides only whether a detached award/loss fact belongs to an
// exact title transition, so no ThoughtDef is ever suppressed globally.
using System;

namespace PawnDiary
{
    /// <summary>Matches one staged title memory to the pawn and exact before/after title edge.</summary>
    internal static class RoyalTitleThoughtOwnershipPolicy
    {
        public static bool Matches(
            RoyalTitleThoughtSnapshot thought,
            string pawnId,
            string previousTitleDefName,
            string newTitleDefName)
        {
            if (thought == null || !Safe(thought.pawnId) || !Safe(thought.titleDefName)
                || !RoyalTitleThoughtRelationshipTokens.IsKnown(thought.relationshipToken)
                || !Same(thought.pawnId, pawnId)) return false;

            return thought.relationshipToken == RoyalTitleThoughtRelationshipTokens.Award
                ? Same(thought.titleDefName, newTitleDefName)
                : Same(thought.titleDefName, previousTitleDefName);
        }

        public static bool IsExpired(int observedTick, int nowTick, int correlationTicks)
        {
            return observedTick < 0 || nowTick < observedTick || correlationTicks < 1
                || (long)nowTick - observedTick > correlationTicks;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        private static bool Safe(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf(';') < 0 && cleaned.IndexOf('|') < 0;
        }
    }
}
