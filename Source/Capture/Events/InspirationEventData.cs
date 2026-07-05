// Payload + pure decision for a "pawn gained an inspiration" event (the InspirationHandler
// .TryStartInspiration hook). This is the trivial source migrated in this slice on purpose: it
// proves the Event Catalog pattern does not force simple sources through heavy ceremony. An
// inspiration has no tokens, no magnitude threshold, and no ambient routing — only eligibility and
// the user's per-def enable toggle. Decide() therefore collapses to two checks.
//
// Like ThoughtEventData, the decision is pure; the RimWorld-facing label/text/game-context assembly
// stays in DiaryGameComponent.RecordInspiration.
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for an inspiration event. Filled by DiaryGameComponent.RecordInspiration from
    /// the live Pawn + InspirationDef.
    /// </summary>
    internal class InspirationEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Inspiration;

        /// <summary>The inspiration's defName (e.g. "Inspired_Recruitment").</summary>
        public string DefName;

        /// <summary>The inspiration's base duration in days. Recorded into the game-context marker;
        /// the decision itself does not currently use it.</summary>
        public float DurationDays;

        /// <summary>Optional vanilla "reason" string for the inspiration start, cleaned and surfaced
        /// to the prompt as prompt evidence.</summary>
        public string Reason;

        /// <summary>
        /// Pure decision for an inspiration event. Eligibility + the user's per-def toggle are the
        /// only gates; everything else is recorded.
        /// </summary>
        public static CaptureDecision Decide(InspirationEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Pure assembly of the inspiration's game-context marker string. Inputs must already be
        /// cleaned (RecordInspiration runs DiaryLineCleaner.CleanLine on the label and reason
        /// before calling). The leading "inspiration=" marker is load-bearing: the UI uses it to
        /// classify the event into the Inspiration domain. A null/whitespace reason omits the
        /// "reason=" field entirely (matches pre-refactor behavior). Keeping this in a pure helper
        /// lets tests lock down the exact format.
        /// </summary>
        public static string BuildGameContext(
            string defName, string label, float durationDays, string cleanedReason)
        {
            string context = "inspiration=" + defName
                + "; label=" + label
                + "; duration_days=" + durationDays.ToString("F1", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(cleanedReason))
            {
                context += "; reason=" + cleanedReason;
            }
            return context;
        }
    }
}
