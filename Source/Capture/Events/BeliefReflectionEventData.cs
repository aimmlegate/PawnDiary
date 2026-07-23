// Pure payload and decision for an Ideology belief reflection. Runtime adapters collect current
// guarded doctrine and saved pending before/after facts; this catalog row only decides whether the
// already-built reflection may create one solo page.
namespace PawnDiary.Capture
{
    /// <summary>Detached facts required to authorize one standalone belief reflection.</summary>
    internal sealed class BeliefReflectionEventData : DiaryEventData
    {
        public const string DefNameToken = "PawnBeliefReflection";

        public override DiaryEventType EventType => DiaryEventType.BeliefReflection;

        public string DefName;
        public string Trigger;
        public bool HasBeliefContext;
        public bool AlreadyWritten;

        public static CaptureDecision Decide(BeliefReflectionEventData data, CaptureContext context)
        {
            if (data == null || context == null
                || !context.Eligible || !context.UserEnabled || !context.SignalEnabled
                || data.AlreadyWritten
                || string.IsNullOrWhiteSpace(data.PawnId)
                || string.IsNullOrWhiteSpace(data.DefName)
                || string.IsNullOrWhiteSpace(data.Trigger)
                || !data.HasBeliefContext)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string BuildGameContext(string trigger)
        {
            return "belief_reflection=true; belief_reflection_trigger=" + (trigger ?? string.Empty);
        }
    }
}
