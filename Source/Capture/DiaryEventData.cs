// Plain payload captured from one live RimWorld event. Subclasses add source-specific fields; the
// base carries only what every event has (which event type, which pawn, when). Every field here is a
// primitive or string so the decision logic in XxxEventData.Decide can be unit-tested without
// RimWorld/Verse/Unity assemblies.
//
// In Redux terms: this is the "action payload" — the data captured at the moment something happened.
// The reducer (XxxEventSpec / XxxEventData.Decide) reads it and decides what to do.
namespace PawnDiary.Capture
{
    public abstract class DiaryEventData
    {
        /// <summary>Which kind of event this payload represents. Set by each subclass constructor
        /// or override; the catalog dispatches on this value.</summary>
        public abstract DiaryEventType EventType { get; }

        /// <summary>The pawn the event happened to (RimWorld's stable cross-save id). Empty for
        /// colony-wide events.</summary>
        public string PawnId;

        /// <summary>Tick the event was observed. Used for dedup keys and for ordering on display.</summary>
        public int Tick;

        /// <summary>
        /// Deterministic, order-independent key for a pawn-id pair. Shared by pair sources (romance
        /// milestones, social fights) so the mirrored second call from the other participant collapses
        /// to the same dedup key. Pure: usable from the catalog payloads and from the standalone tests.
        /// </summary>
        public static string CanonicalPairKey(string firstId, string secondId)
        {
            firstId = firstId ?? string.Empty;
            secondId = secondId ?? string.Empty;
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }
    }
}
