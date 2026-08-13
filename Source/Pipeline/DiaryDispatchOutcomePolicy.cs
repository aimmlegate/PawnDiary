// Pure meaning of one diary-signal dispatch result. Runtime source owners use this small contract to
// decide whether an occurrence is settled, whether a canonical page entered persistence, and whether
// the legacy Boolean adapter should report that ordinary emission completed. Keeping those meanings
// together prevents stateful scanners from accidentally retrying a deliberate frequency skip.
namespace PawnDiary.Ingestion
{
    /// <summary>Typed result of one signal dispatch, including a settled frequency skip.</summary>
    internal enum DiaryDispatchOutcome
    {
        Rejected = 0,
        FrequencyRejected = 1,
        ConsumedWithoutPage = 2,
        PageRegistered = 3,
        // Compatibility alias for the early Slice-3 name. New code should use PageRegistered.
        Accepted = PageRegistered,
        ExceptionAfterCommit = 4
    }

    /// <summary>Central meaning of dispatch results for stateful source owners.</summary>
    internal static class DiaryDispatchOutcomePolicy
    {
        /// <summary>True when this exact source occurrence must not be retried.</summary>
        public static bool SettlesSource(DiaryDispatchOutcome outcome)
        {
            return outcome == DiaryDispatchOutcome.FrequencyRejected
                || outcome == DiaryDispatchOutcome.ConsumedWithoutPage
                || outcome == DiaryDispatchOutcome.PageRegistered
                || outcome == DiaryDispatchOutcome.ExceptionAfterCommit;
        }

        /// <summary>True once persistence began for a canonical page.</summary>
        public static bool PageRegistered(DiaryDispatchOutcome outcome)
        {
            return outcome == DiaryDispatchOutcome.PageRegistered
                || outcome == DiaryDispatchOutcome.ExceptionAfterCommit;
        }

        /// <summary>Matches the legacy bool contract: semantic admission reached and Emit completed.</summary>
        public static bool EmissionRan(DiaryDispatchOutcome outcome)
        {
            return outcome == DiaryDispatchOutcome.ConsumedWithoutPage
                || outcome == DiaryDispatchOutcome.PageRegistered;
        }

        /// <summary>
        /// Classifies an escaped dispatch exception using only registration work owned by the requested
        /// signal. Prerequisite work (such as founding-arrival bootstrap) must leave
        /// <paramref name="targetDispatchStarted"/> false and can therefore never settle the target.
        /// </summary>
        public static DiaryDispatchOutcome ForException(
            bool targetDispatchStarted,
            bool targetRegistrationAdvanced)
        {
            return targetDispatchStarted && targetRegistrationAdvanced
                ? DiaryDispatchOutcome.ExceptionAfterCommit
                : DiaryDispatchOutcome.Rejected;
        }
    }
}
