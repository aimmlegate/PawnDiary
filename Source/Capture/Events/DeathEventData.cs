// Payload + pure decision for the neutral death fallback. Tale-backed deaths use TaleEventData; this
// source covers Pawn.Kill paths that do not produce a death Tale and still need one final page.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one fallback death-description event.
    /// </summary>
    public class DeathEventData : DiaryEventData
    {
        public const string DefNameToken = "PawnDiary_DeathFallback";

        public override DiaryEventType EventType => DiaryEventType.Death;

        public string DefName;
        public string Label;
        public string PawnLabel;
        public string PawnLoadId;
        public string DeathFacts;
        public bool HasExistingDeathDescription;

        public static CaptureDecision Decide(DeathEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled || data.HasExistingDeathDescription)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSoloDeathDescription;
        }

        public static string BuildFallbackGameContext(
            string defName, string label, string pawnLabel, string pawnLoadId,
            string deathVictimRole, string deathFacts)
        {
            List<string> parts = new List<string>
            {
                "tale=" + defName,
                "label=" + label,
                "taleClass=PawnKillFallback",
                "death_description=true",
                "death_victim=" + pawnLabel,
                "death_victim_id=" + pawnLoadId,
                "death_victim_role=" + deathVictimRole
            };

            if (!string.IsNullOrWhiteSpace(deathFacts))
            {
                parts.Add(deathFacts);
            }

            return string.Join("; ", parts.ToArray());
        }
    }
}
