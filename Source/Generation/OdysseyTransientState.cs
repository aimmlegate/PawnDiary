// One lifecycle reset owner for every process-static Odyssey O3 correlation scope and loaded-test
// seam. Journey/landing correlation remains component-owned; only synchronous terminal ownership and
// null-by-default test overrides live here.
namespace PawnDiary
{
    /// <summary>Clears transient Odyssey state whenever a game is constructed, started, or loaded.</summary>
    internal static class OdysseyTransientState
    {
        internal static void Reset()
        {
            OdysseyMechhiveOutcomeScope.Clear();
            DlcContext.ResetOdysseyMechhiveTestState();
        }
    }
}
