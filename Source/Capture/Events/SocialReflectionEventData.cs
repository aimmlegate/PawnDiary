// Plain payload and frozen context contract for Quality Wave H7 social reflections.
//
// The live scheduler captures Pawn/PlayLog/repository state. This DTO carries only stable IDs and
// booleans into the catalog, while BuildGameContext serializes the already-localized qualitative
// snapshot that regeneration must reuse verbatim.
using System;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>Captured facts for one delayed, writer-only reflection about another pawn.</summary>
    internal sealed class SocialReflectionEventData : DiaryEventData
    {
        public const string DefNameToken = "PawnDiary_SocialReflection";
        public const string ContextMarker = "social_reflection";
        public const string SourceKeyContextKey = "social_reflection_source_key";
        public const string SubjectIdContextKey = "social_reflection_subject_id";
        public const string SourceEventIdContextKey = "social_reflection_source_event_id";
        public const string SourceTickContextKey = "social_reflection_source_tick";
        public const string SourcePlayLogIdContextKey = "social_reflection_source_play_log_id";
        public const string SourceInteractionDefContextKey =
            "social_reflection_source_interaction_def";

        public override DiaryEventType EventType => DiaryEventType.SocialReflection;

        public string WriterPawnId = string.Empty;
        public string SubjectPawnId = string.Empty;
        public string SourceKey = string.Empty;
        public bool WriterEligible;
        public bool SubjectEligible;
        public bool FeatureEnabled;

        /// <summary>Allows exactly one solo writer when all frozen admission gates remain valid.</summary>
        public static CaptureDecision Decide(SocialReflectionEventData data, CaptureContext context)
        {
            if (data == null || context == null
                || string.IsNullOrWhiteSpace(data.WriterPawnId)
                || string.IsNullOrWhiteSpace(data.SubjectPawnId)
                || string.Equals(data.WriterPawnId, data.SubjectPawnId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(data.SourceKey))
            {
                return CaptureDecision.Drop;
            }

            return data.WriterEligible
                && data.SubjectEligible
                && data.FeatureEnabled
                && context.Eligible
                && context.UserEnabled
                && context.SignalEnabled
                    ? CaptureDecision.GenerateSolo
                    : CaptureDecision.Drop;
        }

        /// <summary>
        /// Serializes all event-time subject/source fields. Every dynamic value is sanitized because
        /// semicolon and equals delimiters are part of the saved game-context grammar.
        /// </summary>
        public static string BuildGameContext(
            string sourceFacts,
            string sourceTitle,
            string sourceEnding,
            SocialReflectionFormattedContext subject,
            string subjectPawnId,
            string sourceEventId,
            string sourceKey,
            int sourceTick,
            int sourcePlayLogEntryId,
            string sourceInteractionDefName)
        {
            SocialReflectionFormattedContext safe =
                subject ?? new SocialReflectionFormattedContext();
            return "social_reflection=true"
                + "; social_reflection_source_facts=" + GameContextValue.Sanitize(sourceFacts)
                + "; social_reflection_source_title=" + GameContextValue.Sanitize(sourceTitle)
                + "; social_reflection_source_ending=" + GameContextValue.Sanitize(sourceEnding)
                + "; social_reflection_subject_name=" + GameContextValue.Sanitize(safe.subjectName)
                + "; social_reflection_subject_age=" + GameContextValue.Sanitize(safe.subjectAge)
                + "; social_reflection_subject_life_stage="
                    + GameContextValue.Sanitize(safe.subjectLifeStage)
                + "; social_reflection_subject_appearance="
                    + GameContextValue.Sanitize(safe.subjectAppearance)
                + "; social_reflection_relation=" + GameContextValue.Sanitize(safe.subjectRelation)
                + "; social_reflection_sentiment=" + GameContextValue.Sanitize(safe.subjectSentiment)
                + "; social_reflection_reasons=" + GameContextValue.Sanitize(safe.subjectReasons)
                + "; social_reflection_polarity=" + GameContextValue.Sanitize(safe.polarity)
                + "; social_reflection_tone=" + GameContextValue.Sanitize(safe.tone)
                + "; social_reflection_color_cue=" + GameContextValue.Sanitize(safe.colorCue)
                + "; social_reflection_subject_id=" + GameContextValue.Sanitize(subjectPawnId)
                + "; social_reflection_source_event_id=" + GameContextValue.Sanitize(sourceEventId)
                + "; social_reflection_source_key=" + GameContextValue.Sanitize(sourceKey)
                + "; social_reflection_source_tick="
                    + GameContextValue.Sanitize(
                        sourceTick.ToString(CultureInfo.InvariantCulture))
                + "; social_reflection_source_play_log_id="
                    + GameContextValue.Sanitize(
                        sourcePlayLogEntryId.ToString(CultureInfo.InvariantCulture))
                + "; social_reflection_source_interaction_def="
                    + GameContextValue.Sanitize(sourceInteractionDefName);
        }
    }
}
