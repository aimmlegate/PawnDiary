// Focused pure tests for the Pawn Diary profile generation-switch transition. These pin the one
// confirmation boundary independently of RimWorld UI and save-state adapters.
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryPawnProfilePolicy()
        {
            AssertProfileDecision(
                "enabled unchanged ignores backlog",
                DiaryPawnProfileGenerationDecision.Unchanged,
                true,
                true,
                7);
            AssertProfileDecision(
                "disabled unchanged ignores backlog",
                DiaryPawnProfileGenerationDecision.Unchanged,
                false,
                false,
                7);
            AssertProfileDecision(
                "enabled to disabled applies directly",
                DiaryPawnProfileGenerationDecision.Disable,
                true,
                false,
                7);
            AssertProfileDecision(
                "disabled to enabled without backlog applies directly",
                DiaryPawnProfileGenerationDecision.EnableDirect,
                false,
                true,
                0);
            AssertProfileDecision(
                "disabled to enabled with backlog confirms",
                DiaryPawnProfileGenerationDecision.EnableWithConfirmation,
                false,
                true,
                1);
            AssertProfileDecision(
                "negative corrupt backlog normalizes to empty",
                DiaryPawnProfileGenerationDecision.EnableDirect,
                false,
                true,
                -12);
        }

        private static void AssertProfileDecision(
            string label,
            DiaryPawnProfileGenerationDecision expected,
            bool originalEnabled,
            bool draftEnabled,
            int backlogCount)
        {
            assertions++;
            DiaryPawnProfileGenerationDecision actual =
                DiaryPawnProfilePolicy.DecideGenerationChange(
                    originalEnabled,
                    draftEnabled,
                    backlogCount);
            if (actual != expected)
            {
                throw new System.Exception(
                    label + ": expected " + expected + ", got " + actual);
            }
        }
    }
}
