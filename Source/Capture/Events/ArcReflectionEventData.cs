// Pure payload and decision for rare pawn life-arc reflections. Runtime code selects existing diary
// pages as memories, then this catalog source decides whether the assembled arc prompt may emit.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one yearly/major-event pawn arc reflection.
    /// </summary>
    public class ArcReflectionEventData : DiaryEventData
    {
        public const string DefNameToken = "PawnArcReflection";

        public override DiaryEventType EventType => DiaryEventType.ArcReflection;

        public string DefName;
        public int ArcYear;
        public int CandidateMemoryCount;
        public int SelectedMemoryCount;
        public int EntriesThisYear;
        public bool Forced;
        public bool AlreadyWritten;

        public static CaptureDecision Decide(ArcReflectionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled || data.AlreadyWritten)
            {
                return CaptureDecision.Drop;
            }

            if (string.IsNullOrWhiteSpace(data.PawnId)
                || string.IsNullOrWhiteSpace(data.DefName)
                || data.CandidateMemoryCount <= 0
                || data.SelectedMemoryCount <= 0)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string BuildGameContext(int arcYear, bool forced, int selectedMemories,
            int candidateMemories, int entriesThisYear)
        {
            return "arc_reflection=true"
                + "; arc_year=" + arcYear
                + "; forced=" + (forced ? "true" : "false")
                + "; selected_memories=" + selectedMemories
                + "; candidate_memories=" + candidateMemories
                + "; entries_this_year=" + entriesThisYear;
        }
    }
}
