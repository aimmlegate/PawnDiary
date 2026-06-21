// The outcome of the pure decision step for one captured event. Each source's XxxEventData.Decide
// returns one of these; DiaryGameComponent then performs the corresponding impure action (build
// event text, mutate save state, queue LLM, route to ambient batcher).
//
// Pair-event sources (mental state social fights, future romance/raid pairs) will add a fourth
// value GeneratePair when they migrate. Today every migrated source is solo.
namespace PawnDiary.Capture
{
    public enum CaptureDecision
    {
        /// <summary>Drop the event entirely: it failed an eligibility gate, a token filter, or the
        /// magnitude threshold.</summary>
        Drop,

        /// <summary>Record this as one solo diary event from the pawn's point of view.</summary>
        GenerateSolo,

        /// <summary>Route the event to the ambient-day-note batcher instead of emitting a solo event
        /// right away. Only used by low-impact thought sources today.</summary>
        RouteAmbient,
    }
}
