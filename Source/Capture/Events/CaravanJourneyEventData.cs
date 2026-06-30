// Payload + pure decision for caravan departure/arrival moments. RimWorld caravan objects stay in
// the ingestion signal; this payload carries only stable strings/counts for filtering and prompts.
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one player caravan departure or arrival.
    /// </summary>
    public class CaravanJourneyEventData : DiaryEventData
    {
        public const string Departed = "departed";
        public const string Arrived = "arrived";

        public override DiaryEventType EventType => DiaryEventType.CaravanJourney;

        public string Signal;
        public string CaravanLabel;
        public string RouteLabel;
        public string MemberSummary;
        public int PawnCount;
        public int AnimalCount;

        public static CaptureDecision Decide(CaravanJourneyEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.Signal))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!IsKnownSignal(data.Signal) || data.PawnCount <= 0)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static bool IsKnownSignal(string signal)
        {
            return string.Equals(signal, Departed, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, Arrived, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildGameContext(
            string signal,
            string caravanLabel,
            string routeLabel,
            string memberSummary,
            int pawnCount,
            int animalCount)
        {
            return "caravan_journey=" + Clean(signal)
                + "; caravan_signal=" + Clean(signal)
                + "; caravan_label=" + Fallback(caravanLabel, "caravan")
                + "; caravan_route=" + Fallback(routeLabel, "unknown")
                + "; caravan_members=" + Fallback(memberSummary, "none")
                + "; caravan_pawns=" + pawnCount.ToString(CultureInfo.InvariantCulture)
                + "; caravan_animals=" + animalCount.ToString(CultureInfo.InvariantCulture);
        }

        private static string Fallback(string value, string fallback)
        {
            string clean = Clean(value);
            return string.IsNullOrWhiteSpace(clean) ? Clean(fallback) : clean;
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
