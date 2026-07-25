// Pure readiness rule for the Advanced-settings pristine-Def snapshot.
// The settings object can be deserialized before RimWorld has populated DefDatabase, so the
// impure catalog supplies a simple "Defs are bound" fact and this helper decides whether capture
// may happen. Keeping the decision detached makes the early-load regression testable without Verse.
namespace PawnDiary
{
    /// <summary>Prevents fallback objects from being mistaken for loaded XML defaults.</summary>
    internal static class AdvancedSnapshotPolicy
    {
        /// <summary>Returns true only for the first call made after required Defs are available.</summary>
        public static bool ShouldCapture(bool alreadyCaptured, bool definitionsReady)
        {
            return !alreadyCaptured && definitionsReady;
        }
    }
}
