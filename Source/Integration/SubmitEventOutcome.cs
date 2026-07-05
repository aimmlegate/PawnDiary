// Public outcome of an integration API SubmitEvent call. Lives in PawnDiary.Integration because it
// is part of the documented adapter contract: it lets a caller tell apart the distinct reasons a
// submission did not record, instead of collapsing them into one boolean.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// The outcome of <see cref="PawnDiaryApi.SubmitEvent(ExternalEventRequest, out SubmitEventOutcome)"/>.
    /// </summary>
    public enum SubmitEventOutcome
    {
        /// <summary>
        /// The event was validated, passed the budget guardrail, and was recorded as a diary event
        /// (queued for or completed generation, depending on the pawn/role). Mirrors the
        /// <c>recorded=true</c> branch of <see cref="PawnDiaryApi.SubmitEventWithHandle"/>.
        /// </summary>
        Recorded = 0,

        /// <summary>
        /// The request was null, missing a required field (eventKey/subject), or no
        /// External-domain <c>DiaryInteractionGroupDef</c> claimed the eventKey. The adapter almost
        /// always has a bug to fix here; the once-per-key log line names the missing piece.
        /// </summary>
        InvalidRequest = 1,

        /// <summary>
        /// The call arrived on a non-main thread. RimWorld's <c>DefDatabase</c>, settings, tick
        /// state, and <c>.Translate()</c> are main-thread-only, so the call was ignored. The adapter
        /// must queue the work and drain it from a main-thread hook (<c>GameComponentUpdate</c> /
        /// <c>OnGUI</c>).
        /// </summary>
        OffThread = 2,

        /// <summary>
        /// No game is loaded, the master integration toggle (<c>allowExternalIntegrations</c>) is
        /// off, or the subject failed normal diary-owner eligibility. Nothing was wrong with the
        /// request itself; the pipeline was not in a state that could accept it.
        /// </summary>
        Ineligible = 3,

        /// <summary>
        /// The rolling external-API prompt budget for this source or globally was exhausted. The
        /// event was not handed to the pipeline; the adapter should back off (the window is XML-tuned
        /// via <c>integrationPromptBudgetWindowTicks</c>) or shed load before retrying.
        /// </summary>
        DroppedBudget = 4,

        /// <summary>
        /// The request was valid and within budget, but the dispatcher dropped it: the matched group
        /// is disabled in XML, the eventKey is inside its dedup window, or pawn state made it
        /// ineligible at dispatch time. This is the normal "the pipeline declined afterwards" path
        /// that also returns <c>recorded=false</c> from
        /// <see cref="PawnDiaryApi.SubmitEventWithHandle"/>.
        /// </summary>
        DroppedByPipeline = 5
    }
}
