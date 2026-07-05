// The outcome of the pure decision step for one captured event. Each source's XxxEventData.Decide
// returns one of these; DiaryGameComponent then performs the corresponding impure action (build
// event text, mutate save state, queue LLM, route to ambient batcher).
//
// MentalState was the first migrated source that used GeneratePair (social fights). Later slices
// added batch routing, day-reflection routing, and neutral death-description outcomes so the catalog
// can choose the final event shape while DiaryGameComponent performs only the side effects.
namespace PawnDiary.Capture
{
    internal enum CaptureDecision
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

        /// <summary>Route the signal into a delayed batch instead of creating an immediate event.
        /// Used by Tale combat batching and non-ambient social interaction batches.</summary>
        RouteBatch,

        /// <summary>Route the event to the ambient-day-note batcher instead of emitting a solo event
        /// right away. Used by low-impact thought sources and ambient social interactions.</summary>
        RouteAmbient,

        /// <summary>Route the signal into the end-of-day reflection collector instead of creating
        /// an immediate event. Used by Hediff groups whose XML policy chooses DayReflection.</summary>
        RouteDayReflection,

        /// <summary>Create a solo event, then queue the neutral death-description prompt instead of
        /// a first-person rewrite.</summary>
        GenerateSoloDeathDescription,

        /// <summary>Create a pair-shaped event carrying both involved pawns, then queue the neutral
        /// death-description prompt instead of pairwise first-person rewrites.</summary>
        GeneratePairDeathDescription,

        /// <summary>Create a solo event, then queue the neutral arrival-description prompt instead
        /// of a first-person rewrite.</summary>
        GenerateSoloArrivalDescription,
    }
}
