// Pure domain recovery for saved diary events. Runtime adapters store compact marker strings such as
// "tale=" or "romance=" in DiaryEvent.gameContext; this helper maps those markers back to the
// XML group domain used by prompt policy, display styling, and text decoration tests.
namespace PawnDiary
{
    /// <summary>
    /// Converts stable game-context markers into source-domain names without touching RimWorld.
    /// </summary>
    public static class DiaryEventDomainClassifier
    {
        public const string Interaction = "Interaction";
        public const string MentalState = "MentalState";
        public const string Tale = "Tale";
        public const string MoodEvent = "MoodEvent";
        public const string Thought = "Thought";
        public const string Inspiration = "Inspiration";
        public const string Romance = "Romance";
        public const string Work = "Work";
        public const string Hediff = "Hediff";
        public const string Raid = "Raid";
        public const string Quest = "Quest";

        /// <summary>
        /// Returns the domain implied by a saved event's game-context marker. Plain social
        /// interactions intentionally fall back to Interaction.
        /// </summary>
        public static string DomainForContext(string context)
        {
            if (DiaryContextFields.HasMarker(context, "tale=")) return Tale;
            if (DiaryContextFields.HasMarker(context, "mood_event=")) return MoodEvent;
            if (DiaryContextFields.HasMarker(context, "thought=")) return Thought;
            if (DiaryContextFields.HasMarker(context, "inspiration=")) return Inspiration;
            if (DiaryContextFields.HasMarker(context, "romance=")) return Romance;
            if (DiaryContextFields.HasMarker(context, "work=")) return Work;
            if (DiaryContextFields.HasMarker(context, "hediff=")) return Hediff;
            if (DiaryContextFields.HasMarker(context, "mental_state=")) return MentalState;
            if (DiaryContextFields.HasMarker(context, "raid=")) return Raid;
            if (DiaryContextFields.HasMarker(context, "quest=")) return Quest;
            return Interaction;
        }

        /// <summary>
        /// True when a context marker identifies a source that is not a normal social InteractionDef.
        /// Used to avoid adding direct-speech prompt instructions to non-social-log events.
        /// </summary>
        public static bool HasNonInteractionSourceMarker(string context)
        {
            return !string.Equals(DomainForContext(context), Interaction, System.StringComparison.Ordinal);
        }
    }
}
