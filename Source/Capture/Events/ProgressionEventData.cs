// Pure payload and decision for pawn progression diary moments. Runtime scanners observe live pawn
// state (skills, psylink, titles, xenotypes), copy only plain facts here, and the catalog decides
// whether the moment should become an ordinary solo diary entry.
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one progression signal such as a passion skill milestone, psylink level,
    /// xenotype change, or royal title change.
    /// </summary>
    internal class ProgressionEventData : DiaryEventData
    {
        public const string SkillMilestoneDefName = "SkillMilestone";
        public const string PsylinkLevelDefName = "PsylinkLevel";
        public const string XenotypeChangedDefName = "XenotypeChanged";
        public const string RoyalTitleChangedDefName = "RoyalTitleChanged";
        public const string OtherDefName = "ProgressionOther";

        public override DiaryEventType EventType => DiaryEventType.Progression;

        public string DefName;
        public string Kind;
        public string Label;
        public string PreviousValue;
        public string NewValue;
        public string Context;
        public bool AlreadyRecorded;

        public static CaptureDecision Decide(ProgressionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled || data.AlreadyRecorded)
            {
                return CaptureDecision.Drop;
            }

            if (string.IsNullOrWhiteSpace(data.PawnId)
                || string.IsNullOrWhiteSpace(data.DefName)
                || string.IsNullOrWhiteSpace(data.Kind))
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string BuildGameContext(string defName, string kind, string label,
            string previousValue, string newValue, string extraContext)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, "progression", defName);
            Append(builder, "progression_kind", kind);
            Append(builder, "label", label);
            Append(builder, "previous_value", previousValue);
            Append(builder, "new_value", newValue);

            if (!string.IsNullOrWhiteSpace(extraContext))
            {
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(extraContext.Trim());
            }

            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(key).Append("=").Append(value);
        }
    }
}
