// Payload + pure decision for the end-of-day reflection meta-source. The adapter gathers and
// randomly selects live highlight evidence; the catalog decides whether a reflection should emit.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one pawn/day reflection candidate.
    /// </summary>
    public class DayReflectionEventData : DiaryEventData
    {
        public const string DefNameToken = "DayReflection";

        public override DiaryEventType EventType => DiaryEventType.DayReflection;

        public string DefName;
        public int Day;
        public int CandidateCount;
        public int HighlightCount;
        public int FillerMomentCount;
        public string SignalTags;
        public bool AlreadyWritten;

        public static CaptureDecision Decide(DayReflectionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled || data.AlreadyWritten)
            {
                return CaptureDecision.Drop;
            }

            return data.CandidateCount > 0 && data.HighlightCount > 0
                ? CaptureDecision.GenerateSolo
                : CaptureDecision.Drop;
        }

        public static string BuildGameContext(
            int day, int highlightCount, int candidateCount, int fillerMomentCount, string signalTags)
        {
            return "day_reflection=true"
                + "; day=" + day
                + "; highlights=" + highlightCount
                + "; candidates=" + candidateCount
                + "; filler_moments=" + fillerMomentCount
                + "; signals=" + (signalTags ?? string.Empty);
        }
    }
}
