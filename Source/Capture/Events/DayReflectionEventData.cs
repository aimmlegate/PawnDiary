// Payload + pure decision for the end-of-day reflection meta-source. The adapter gathers live
// evidence, marks which candidates are genuinely important, and randomly selects the highlight
// cues; the catalog decides whether a reflection should emit.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one pawn/day reflection candidate.
    /// </summary>
    public class DayReflectionEventData : DiaryEventData
    {
        public const string DefNameToken = "DayReflection";
        public const string SignalKindEvent = "event";
        public const string SignalKindOpinion = "opinion";
        public const string SignalKindHediff = "hediff";
        public const string SignalKindFiller = "filler";

        public override DiaryEventType EventType => DiaryEventType.DayReflection;

        public string DefName;
        public int Day;
        public int CandidateCount;
        public int ImportantCandidateCount;
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

            return data.CandidateCount > 0 && data.ImportantCandidateCount > 0 && data.HighlightCount > 0
                ? CaptureDecision.GenerateSolo
                : CaptureDecision.Drop;
        }

        /// <summary>
        /// Pure XML-token matcher for the signal kinds allowed to justify a day reflection. The
        /// adapter passes the XML list from DiaryTuningDef; a null or empty list intentionally means
        /// no signal kind is important enough to create a reflection.
        /// </summary>
        public static bool IsImportantSignalKind(string signalKind, IList<string> importantSignalKinds)
        {
            if (string.IsNullOrWhiteSpace(signalKind) || importantSignalKinds == null)
            {
                return false;
            }

            for (int i = 0; i < importantSignalKinds.Count; i++)
            {
                if (string.Equals(signalKind, importantSignalKinds[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
