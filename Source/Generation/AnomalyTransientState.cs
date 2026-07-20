// One lifecycle reset owner for every process-static Anomaly correlation cache/scope. Keeping this
// adapter separate lets the individual detached caches remain linkable into the no-RimWorld tests.
namespace PawnDiary
{
    /// <summary>Clears transient Anomaly state whenever a game is constructed, started, or loaded.</summary>
    internal static class AnomalyTransientState
    {
        internal static void Reset()
        {
            AnomalyStudySuppressionCache.Clear();
            AnomalyRecentStudyCache.Clear();
            ContainmentEscapeScopeStack.Clear();
            CreepJoinerOutcomeScope.Clear();
            CreepJoinerSurgicalInspectionScope.Clear();
        }
    }
}
