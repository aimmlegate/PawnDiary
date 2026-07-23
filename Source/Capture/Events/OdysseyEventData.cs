// Catalog payload for verified Odyssey source events beyond the gravship landing lifecycle. O3 uses
// this envelope for the exact Mechhive resolution only; its source policy has already verified the
// choice, quest, visibility, and operator before this final pure catalog gate runs.
namespace PawnDiary.Capture
{
    /// <summary>Primitive final-gate facts for one verified Odyssey event.</summary>
    internal sealed class OdysseyEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.OdysseyEvent;

        public string DefName;
        public string SourceKey;
        public bool HasVerifiedSource;
        public bool PlayerVisible;
        public bool AlreadyRecorded;
        public string WriterId;
        public bool WriterEligible;

        /// <summary>Generates one solo page only for a complete, enabled, verified source row.</summary>
        public static CaptureDecision Decide(OdysseyEventData data, CaptureContext context)
        {
            if (data == null || context == null
                || !context.UserEnabled || !context.SignalEnabled
                || !data.HasVerifiedSource || !data.PlayerVisible || data.AlreadyRecorded
                || string.IsNullOrWhiteSpace(data.DefName)
                || string.IsNullOrWhiteSpace(data.SourceKey)
                || !data.WriterEligible || string.IsNullOrWhiteSpace(data.WriterId))
            {
                return CaptureDecision.Drop;
            }
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>Returns the exact source-owned transient dedup identity.</summary>
        public string DedupKey()
        {
            return "odyssey|" + (DefName ?? string.Empty) + "|" + (SourceKey ?? string.Empty);
        }
    }
}
