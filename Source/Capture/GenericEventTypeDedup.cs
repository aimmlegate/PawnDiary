// Pure key builder for the last-resort "same event type just fired" dedup layer.
//
// Source-specific dedup keys are still the primary defense because they know the exact RimWorld
// event identity (thought def, tale pawns, quest id, etc.). This helper is deliberately blunter:
// it gives sources without a detailed key a short type+subject safety net, and it gives cross-source
// shapes such as neutral death descriptions one common key even when the underlying source differs.
// Keeping the string assembly here makes the transient key format easy to test without RimWorld.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Builds transient dedup keys for the generic event-type safety window.
    /// </summary>
    internal static class GenericEventTypeDedup
    {
        private const string Prefix = "event-type";
        private const string EmptySubject = "none";

        /// <summary>
        /// Builds the default key for one decided payload: event source type + final decision shape +
        /// subject pawn. This is used only as a short fallback when a signal has no source-specific key.
        /// </summary>
        public static string KeyFor(DiaryEventData payload, CaptureDecision decision)
        {
            return payload == null
                ? string.Empty
                : KeyFor(payload.EventType, decision, payload.PawnId);
        }

        /// <summary>
        /// Builds a generic type+shape+subject key from primitive inputs.
        /// </summary>
        public static string KeyFor(DiaryEventType eventType, CaptureDecision decision, string subjectId)
        {
            return Prefix + "|" + eventType + "|" + decision + "|" + SubjectPart(subjectId);
        }

        /// <summary>
        /// Shared key for neutral death-description pages. Tale deaths and the Pawn.Kill fallback both
        /// use this key so only one final death page can be emitted for the same pawn in the short
        /// generic window, regardless of which source arrives first.
        /// </summary>
        public static string DeathDescriptionKey(string pawnId)
        {
            return Prefix + "|DeathDescription|" + SubjectPart(pawnId);
        }

        private static string SubjectPart(string subjectId)
        {
            return string.IsNullOrWhiteSpace(subjectId) ? EmptySubject : subjectId.Trim();
        }
    }
}
