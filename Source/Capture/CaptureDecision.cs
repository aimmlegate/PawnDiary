// The outcome of the pure decision step for one captured event. Each source's XxxEventData.Decide
// returns one of these; DiaryGameComponent then performs the corresponding impure action (build
// event text, mutate save state, queue LLM, route to ambient batcher).
//
// MentalState is the first migrated source that uses GeneratePair (social fights). Future pair
// sources (romance, raid pairs) reuse the same value.
namespace PawnDiary.Capture
{
    public enum CaptureDecision
    {
        /// <summary>Drop the event entirely: it failed an eligibility gate, a token filter, or the
        /// magnitude threshold.</summary>
        Drop,

        /// <summary>Record this as one solo diary event from the pawn's point of view.</summary>
        GenerateSolo,

        /// <summary>Record this as one pairwise diary event with both POV entries (initiator +
        /// recipient). Used by social fights, romance milestones, and future raid pair sources. The other
        /// participant's id is carried on the payload so the sink can build the pair.</summary>
        GeneratePair,

        /// <summary>Route the event to the ambient-day-note batcher instead of emitting a solo event
        /// right away. Only used by low-impact thought sources today.</summary>
        RouteAmbient,
    }
}
