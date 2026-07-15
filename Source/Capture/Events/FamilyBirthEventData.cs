// Inert Phase-0 catalog payload for one canonical Biotech family birth. Phase 3 will construct it
// only after ApplyBirthOutcome proves the child/outcome and the pure writer policy selects adults.
namespace PawnDiary.Capture
{
    /// <summary>Plain catalog payload for the canonical family-owned birth event.</summary>
    internal class FamilyBirthEventData : DiaryEventData
    {
        public const string DefName = BiotechEventDefNames.FamilyBirth;

        public override DiaryEventType EventType => DiaryEventType.FamilyBirth;

        public string FamilyArcId;
        public string FirstWriterId;
        public string SecondWriterId;
        public bool FirstWriterEligible;
        public bool SecondWriterEligible;
        public bool HasValidSnapshot;
        public bool AlreadyRecorded;

        /// <summary>Applies package/user/signal gates and chooses pair, solo, or drop from proven writers.</summary>
        public static CaptureDecision Decide(FamilyBirthEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || data.AlreadyRecorded || !data.HasValidSnapshot
                || string.IsNullOrWhiteSpace(data.FamilyArcId))
            {
                return CaptureDecision.Drop;
            }

            bool first = data.FirstWriterEligible && !string.IsNullOrWhiteSpace(data.FirstWriterId);
            bool second = data.SecondWriterEligible && !string.IsNullOrWhiteSpace(data.SecondWriterId)
                && (!first || data.FirstWriterId != data.SecondWriterId);
            if (first && second) return CaptureDecision.GeneratePair;
            if (first || second) return CaptureDecision.GenerateSolo;
            return CaptureDecision.Drop;
        }

        /// <summary>Returns the frozen once-per-family-arc birth deduplication key.</summary>
        public string DedupKey()
        {
            return string.IsNullOrWhiteSpace(FamilyArcId) ? string.Empty : "birth|" + FamilyArcId.Trim();
        }
    }
}
