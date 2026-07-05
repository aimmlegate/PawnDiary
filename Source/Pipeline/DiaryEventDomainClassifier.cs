// Pure domain recovery for saved diary events. Runtime adapters store compact marker strings such as
// "tale=" or "romance=" in DiaryEvent.gameContext; this helper maps those markers back to the
// XML group domain used by prompt policy, display styling, and text decoration tests.
using PawnDiary.Capture;

namespace PawnDiary
{
    /// <summary>
    /// Converts stable game-context markers into source-domain names without touching RimWorld.
    /// </summary>
    internal static class DiaryEventDomainClassifier
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
        public const string Ritual = "Ritual";
        public const string Ability = "Ability";
        public const string Progression = "Progression";
        public const string Reflection = "Reflection";
        public const string External = "External";

        /// <summary>
        /// Returns the domain implied by a saved event's game-context marker. Plain social
        /// interactions intentionally fall back to Interaction.
        /// </summary>
        public static string DomainForContext(string context)
        {
            if (DiaryContextFields.HasMarker(context, "external=")) return External;
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
            if (DiaryContextFields.HasMarker(context, "ritual=")) return Ritual;
            if (DiaryContextFields.HasMarker(context, "psychic_ritual=")) return Ritual;
            if (DiaryContextFields.HasMarker(context, "ability=")) return Ability;
            if (DiaryContextFields.HasMarker(context, "progression=")) return Progression;
            if (DiaryContextFields.HasMarker(context, "arc_reflection=")) return Reflection;
            if (DiaryContextFields.HasMarker(context, "quadrum_reflection=")) return Reflection;
            if (DiaryContextFields.HasMarker(context, "day_reflection=")) return Reflection;
            return Interaction;
        }

        /// <summary>
        /// Returns the key that should be used when recovering an XML group for a saved event. Most
        /// domains classify by the saved source defName, but Quest groups classify by lifecycle
        /// signal so accepted/completed/failed entries keep their distinct prompt policy.
        /// </summary>
        public static string GroupClassifierKey(string domain, string context, string savedDefName)
        {
            if (string.Equals(domain, Quest, System.StringComparison.OrdinalIgnoreCase))
            {
                string signal = DiaryContextFields.Value(context, "signal");
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    return signal;
                }
            }

            if (string.Equals(domain, Ritual, System.StringComparison.OrdinalIgnoreCase))
            {
                string behavior = DiaryContextFields.Value(context, "ritual_behavior");
                if (!string.IsNullOrWhiteSpace(behavior))
                {
                    return savedDefName + ";" + behavior;
                }

                if (DiaryContextFields.HasMarker(context, "psychic_ritual="))
                {
                    return "PsychicRitual;" + savedDefName;
                }
            }

            if (string.Equals(domain, Ability, System.StringComparison.OrdinalIgnoreCase))
            {
                string category = DiaryContextFields.Value(context, "ability_category");
                if (!string.IsNullOrWhiteSpace(category))
                {
                    return savedDefName + ";" + category;
                }
            }

            if (string.Equals(domain, Hediff, System.StringComparison.OrdinalIgnoreCase))
            {
                string partKind = DiaryContextFields.Value(context, "part_kind");
                if (!string.IsNullOrWhiteSpace(partKind))
                {
                    return BodyPartEventPolicy.BuildHediffClassifierKey(
                        savedDefName,
                        BodyPartEventPolicy.KindHasToken(partKind, BodyPartEventPolicy.KindAddedPart),
                        BodyPartEventPolicy.KindHasToken(partKind, BodyPartEventPolicy.KindMissingPart),
                        BodyPartEventPolicy.KindHasToken(partKind, BodyPartEventPolicy.KindOrganicPart));
                }
            }

            return savedDefName;
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
