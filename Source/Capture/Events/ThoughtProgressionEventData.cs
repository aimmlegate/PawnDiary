// Payload + pure decision for situational thought-stage progression. The scanner tracks live
// episode state, but this class decides whether a matched stage should emit after the adapter
// snapshots "worsened" and "already recorded" as booleans.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one active need/thought stage becoming diary-worthy.
    /// </summary>
    internal class ThoughtProgressionEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.ThoughtProgression;

        public string DefName;
        public string CategoryKey;
        public string Label;
        public string StageIndex;
        public string Severity;
        public string MoodImpact;
        public string MoodOffset;
        public bool Worsened;
        public bool StageAlreadyRecorded;

        /// <summary>
        /// The transient dedup key for this progression event (raw, source-prefixed). Lifted verbatim
        /// out of the old RecordThoughtProgression: one window per pawn + category + thought def +
        /// stage. Distinct "thoughtprogression|" prefix so it never collides with plain Thought keys.
        /// </summary>
        public string DedupKey()
        {
            return "thoughtprogression|" + PawnId + "|" + CategoryKey + "|" + DefName + "|" + StageIndex;
        }

        public static CaptureDecision Decide(ThoughtProgressionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.Worsened || data.StageAlreadyRecorded)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string BuildGameContext(
            string defName, string categoryKey, string label, string stageIndex,
            string severity, string moodImpact, string moodOffset)
        {
            return "thought=" + defName
                + "; thought_progression=" + categoryKey
                + "; label=" + label
                + "; stage_index=" + stageIndex
                + "; severity=" + severity
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset;
        }
    }
}
