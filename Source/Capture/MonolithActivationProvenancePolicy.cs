// Pure provenance check for the Anomaly void-monolith hook. Vanilla uses the same Activate(Pawn)
// method for a player order and for the automatic timer, where it supplies a random colonist merely
// to satisfy the method signature. This decision prevents that random pawn from being treated as an
// intentional first-person actor.
namespace PawnDiary.Capture
{
    /// <summary>Classifies whether the monolith's scheduled automatic activation is due.</summary>
    internal static class MonolithActivationProvenancePolicy
    {
        /// <summary>
        /// Mirrors vanilla's strict <c>currentTick &gt; autoActivateTick</c> trigger.
        /// Non-positive schedule values mean no automatic activation is pending.
        /// </summary>
        public static bool IsAutomatic(int currentTick, int autoActivateTick)
        {
            return autoActivateTick > 0 && currentTick > autoActivateTick;
        }
    }
}
