// Pure transition policy for the AddDirectRelation Harmony hook. Vanilla may reject a requested
// relation addition, so capture is valid only when the exact direct relation changed absent -> present.
namespace PawnDiary.Capture
{
    /// <summary>Decides whether a direct-relation call created new evidence worth submitting.</summary>
    internal static class RomanceRelationTransitionPolicy
    {
        /// <summary>True only for a verified absent-to-present direct-relation transition.</summary>
        public static bool ShouldEmit(bool wasPresent, bool isPresent)
        {
            return !wasPresent && isPresent;
        }
    }
}
