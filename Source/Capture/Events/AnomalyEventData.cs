// Common catalog envelope for one source-owned Anomaly moment. Source-specific policies still prove
// study, containment, reveal, transformation, or void truth separately; this detached value performs
// only the final kind/Def identity, visibility, writer-shape, settings, and replay gates.
namespace PawnDiary.Capture
{
    /// <summary>Primitive final-dispatch facts shared by all five frozen Anomaly moment kinds.</summary>
    internal sealed class AnomalyEventData : DiaryEventData
    {
        private const int MaximumSourceKeyCharacters = 512;

        public override DiaryEventType EventType => DiaryEventType.AnomalyEvent;

        public string DefName = string.Empty;
        public string Kind = string.Empty;
        public string SourceKey = string.Empty;
        public bool HasVerifiedSource;
        public bool PlayerVisible;
        public bool AlreadyRecorded;
        public string FirstWriterId = string.Empty;
        public bool FirstWriterEligible;
        public string SecondWriterId = string.Empty;
        public bool SecondWriterEligible;

        /// <summary>Applies final fail-closed truth/settings gates and chooses solo versus pair shape.</summary>
        public static CaptureDecision Decide(AnomalyEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || !data.HasVerifiedSource || !data.PlayerVisible || data.AlreadyRecorded
                || data.Tick < 0 || !SafeSourceKey(data.SourceKey)
                || DefNameForKind(data.Kind) != data.DefName)
            {
                return CaptureDecision.Drop;
            }

            bool first = data.FirstWriterEligible && SafeWriterId(data.FirstWriterId);
            bool second = data.SecondWriterEligible && SafeWriterId(data.SecondWriterId)
                // Distinctness matters only when the first slot is actually usable. A source may
                // retain the same pawn identity in an ineligible first-role slot and an eligible
                // fallback second-role slot; that truthful writer must still receive a solo page.
                && (!first || !string.Equals(
                    (data.FirstWriterId ?? string.Empty).Trim(),
                    data.SecondWriterId.Trim(),
                    System.StringComparison.Ordinal));
            if (first && second) return CaptureDecision.GeneratePair;
            return first || second ? CaptureDecision.GenerateSolo : CaptureDecision.Drop;
        }

        /// <summary>Returns the one source-owned key shared by both possible POV writers.</summary>
        public string DedupKey()
        {
            string kind = NormalizeKind(Kind);
            return kind.Length > 0 && SafeSourceKey(SourceKey)
                ? "anomaly-event|" + kind + "|" + SourceKey.Trim()
                : string.Empty;
        }

        /// <summary>Maps one exact stable kind token to its synthetic event Def name.</summary>
        public static string DefNameForKind(string kind)
        {
            kind = NormalizeKind(kind);
            if (kind == AnomalyKindTokens.StudyBreakthrough)
                return AnomalyEventDefNames.StudyBreakthrough;
            if (kind == AnomalyKindTokens.ContainmentBreach)
                return AnomalyEventDefNames.ContainmentBreach;
            if (kind == AnomalyKindTokens.CreepJoinerOutcome)
                return AnomalyEventDefNames.CreepJoinerOutcome;
            if (kind == AnomalyKindTokens.GhoulTransformation)
                return AnomalyEventDefNames.GhoulTransformation;
            if (kind == AnomalyKindTokens.VoidOutcome)
                return AnomalyEventDefNames.VoidOutcome;
            return string.Empty;
        }

        /// <summary>Returns one exact known token or empty; unknown/default values never generate.</summary>
        public static string NormalizeKind(string kind)
        {
            string value = (kind ?? string.Empty).Trim();
            return value == AnomalyKindTokens.StudyBreakthrough
                    || value == AnomalyKindTokens.ContainmentBreach
                    || value == AnomalyKindTokens.CreepJoinerOutcome
                    || value == AnomalyKindTokens.GhoulTransformation
                    || value == AnomalyKindTokens.VoidOutcome
                ? value
                : string.Empty;
        }

        private static bool SafeWriterId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0
                && cleaned.IndexOf(';') < 0 && cleaned.IndexOf('=') < 0
                && cleaned.IndexOf('\r') < 0 && cleaned.IndexOf('\n') < 0;
        }

        private static bool SafeSourceKey(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.Length <= MaximumSourceKeyCharacters
                && cleaned.IndexOf(';') < 0 && cleaned.IndexOf('=') < 0
                && cleaned.IndexOf('\r') < 0 && cleaned.IndexOf('\n') < 0;
        }
    }
}
