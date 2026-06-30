// Observed conditions, part 4 of the pure layer: the decisions the policy produces and the plan that
// bundles them. Each decision is a complete instruction the impure adapter applies verbatim — persist
// this state, remove this row, and/or record a diary page — so all lifecycle reasoning stays here, in
// testable pure code, and the adapter only does the RimWorld-facing side effects. See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// The lifecycle transition the policy decided for one condition identity on this scan.
    /// </summary>
    public enum ObservedConditionDecisionKind
    {
        // Newly observed, still inside the start debounce: remember it, but do not record a page yet.
        StartPending,
        // Observed long enough (or debounce 0): the start phase is now real; record the start page.
        StartRecorded,
        // Still observed after starting: just refresh last-observed tick and evidence.
        Refresh,
        // No longer observed, but still inside the end debounce: remember the missing-since tick.
        EndPending,
        // Missing past the end debounce and the start had been recorded: record the end page, then drop.
        EndRecorded,
        // The condition should be forgotten with no end page (never started, or its Def is gone).
        DropStale
    }

    /// <summary>
    /// One instruction for the adapter. <see cref="state"/> always carries the condition's identity and
    /// its post-decision fields (and, for end decisions, the last-seen evidence used for page text).
    /// </summary>
    public sealed class ObservedConditionDecision
    {
        public ObservedConditionDecisionKind kind;
        public ObservedConditionStateSnapshot state;

        // True when the adapter should delete the saved row for this identity after applying. Set for
        // EndRecorded and DropStale.
        public bool removeState;

        // True when this transition produced a page-worthy moment (StartRecorded / EndRecorded). The
        // adapter still gates the actual write on the Def's recordStartEvent / recordEndEvent flag, so
        // the policy stays free of recording policy.
        public bool recordPage;
    }

    /// <summary>
    /// The full set of decisions for one scan, in no particular order. The adapter applies each in turn.
    /// </summary>
    public sealed class ObservedConditionPlan
    {
        public List<ObservedConditionDecision> decisions = new List<ObservedConditionDecision>();
    }
}
