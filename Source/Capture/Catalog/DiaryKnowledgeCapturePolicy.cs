// DiaryKnowledgeCapturePolicy.cs — pure boundary between page policy and durable knowledge.
// Some allowlisted state changes remain lifelong facts even when the player disables their diary
// page. The dispatcher uses this reducer to distinguish that policy-only rejection from semantic
// rejection such as a duplicate arrival, an already-recorded birth, or an unverified mutation.
namespace PawnDiary.Capture
{
    /// <summary>Decides whether an otherwise dropped event remains valid with page gates relaxed.</summary>
    internal static class DiaryKnowledgeCapturePolicy
    {
        /// <summary>
        /// Returns true only when the ordinary decision drops, but the same payload succeeds after
        /// user/signal page switches are relaxed. Eligibility and every event-specific validity fact
        /// remain unchanged, so no-page capture cannot turn a malformed or duplicate event into
        /// durable knowledge.
        /// </summary>
        public static bool ShouldCaptureWithoutPage(DiaryEventData payload, CaptureContext context)
        {
            if (payload == null || context == null)
            {
                return false;
            }

            DiaryEventSpec spec = DiaryEventCatalog.Get(payload.EventType);
            if (spec == null || spec.Decide(payload, context) != CaptureDecision.Drop)
            {
                return false;
            }

            CaptureContext withoutPagePolicy = new CaptureContext
            {
                Eligible = context.Eligible,
                UserEnabled = true,
                SignalEnabled = true,
                AmbientSignalEnabled = true,
                Now = context.Now
            };
            return spec.Decide(payload, withoutPagePolicy) != CaptureDecision.Drop;
        }
    }
}
