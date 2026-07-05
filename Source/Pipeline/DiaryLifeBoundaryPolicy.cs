// Pure diary-lifespan rules. Runtime code supplies the facts it observed from RimWorld
// (for example "this pawn is alive right now"); this helper owns the small decision that
// tests can pin without loading Verse/RimWorld assemblies.
namespace PawnDiary
{
    /// <summary>
    /// Decides when lifecycle boundary entries should constrain later diary pages.
    /// </summary>
    internal static class DiaryLifeBoundaryPolicy
    {
        /// <summary>
        /// A death page is terminal only while there is no live pawn with the same load ID.
        /// Resurrection keeps the death page as history, but later pages must be visible/generatable.
        /// </summary>
        public static bool FinalDeathBoundaryApplies(bool hasFinalDeathBoundary, bool pawnAliveNow)
        {
            return hasFinalDeathBoundary && !pawnAliveNow;
        }
    }
}
