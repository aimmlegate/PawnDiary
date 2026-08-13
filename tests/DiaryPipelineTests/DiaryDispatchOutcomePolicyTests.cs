// Pure contract for state settlement after typed diary dispatch. These assertions keep a frequency
// skip terminal for stateful sources and distinguish a committed page from the older Boolean API's
// narrower "Emit completed" meaning.
using PawnDiary.Ingestion;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryDispatchOutcomePolicy()
        {
            AssertTrue("ordinary rejection does not settle its source",
                !DiaryDispatchOutcomePolicy.SettlesSource(DiaryDispatchOutcome.Rejected));
            AssertTrue("frequency rejection settles its source",
                DiaryDispatchOutcomePolicy.SettlesSource(DiaryDispatchOutcome.FrequencyRejected));
            AssertTrue("digest folding settles its source",
                DiaryDispatchOutcomePolicy.SettlesSource(DiaryDispatchOutcome.ConsumedWithoutPage));
            AssertTrue("registered page settles its source",
                DiaryDispatchOutcomePolicy.SettlesSource(DiaryDispatchOutcome.PageRegistered));
            AssertTrue("post-commit exception settles its source",
                DiaryDispatchOutcomePolicy.SettlesSource(DiaryDispatchOutcome.ExceptionAfterCommit));

            AssertTrue("frequency rejection is not a registered page",
                !DiaryDispatchOutcomePolicy.PageRegistered(DiaryDispatchOutcome.FrequencyRejected));
            AssertTrue("folded digest is not a registered page",
                !DiaryDispatchOutcomePolicy.PageRegistered(DiaryDispatchOutcome.ConsumedWithoutPage));
            AssertTrue("ordinary page reports registered",
                DiaryDispatchOutcomePolicy.PageRegistered(DiaryDispatchOutcome.PageRegistered));
            AssertTrue("post-commit exception reports persistence began",
                DiaryDispatchOutcomePolicy.PageRegistered(DiaryDispatchOutcome.ExceptionAfterCommit));

            AssertTrue("folded digest preserves the legacy emitted bool",
                DiaryDispatchOutcomePolicy.EmissionRan(DiaryDispatchOutcome.ConsumedWithoutPage));
            AssertTrue("ordinary page preserves the legacy emitted bool",
                DiaryDispatchOutcomePolicy.EmissionRan(DiaryDispatchOutcome.PageRegistered));
            AssertTrue("frequency rejection remains false through the legacy bool adapter",
                !DiaryDispatchOutcomePolicy.EmissionRan(DiaryDispatchOutcome.FrequencyRejected));
            AssertTrue("post-commit exception remains false through the legacy bool adapter",
                !DiaryDispatchOutcomePolicy.EmissionRan(DiaryDispatchOutcome.ExceptionAfterCommit));

            AssertTrue("bootstrap registration cannot settle a target that did not start",
                DiaryDispatchOutcomePolicy.ForException(
                    targetDispatchStarted: false,
                    targetRegistrationAdvanced: true) == DiaryDispatchOutcome.Rejected);
            AssertTrue("target pre-commit exception remains rejected",
                DiaryDispatchOutcomePolicy.ForException(
                    targetDispatchStarted: true,
                    targetRegistrationAdvanced: false) == DiaryDispatchOutcome.Rejected);
            AssertTrue("target post-commit exception remains settled",
                DiaryDispatchOutcomePolicy.ForException(
                    targetDispatchStarted: true,
                    targetRegistrationAdvanced: true)
                    == DiaryDispatchOutcome.ExceptionAfterCommit);
        }
    }
}
