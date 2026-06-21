// Payload + pure decision for a colonist-arrival first page. The game adapter supplies only
// primitive facts: whether the pawn is eligible, whether an arrival page already exists, and whether
// the XML group is enabled. The neutral arrival prompt stays an impure generation-side detail.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one pawn becoming part of the colony.
    /// </summary>
    public class ArrivalEventData : DiaryEventData
    {
        public const string DefNameToken = "PawnDiary_Arrival";

        public override DiaryEventType EventType => DiaryEventType.Arrival;

        public string DefName;
        public string PawnLabel;
        public string PawnLoadId;
        public string ArrivalContext;
        public bool HasExistingArrival;

        public static CaptureDecision Decide(ArrivalEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled || data.HasExistingArrival)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSoloArrivalDescription;
        }

        public static bool IsStartingArrival(string arrivalContext)
        {
            return !string.IsNullOrWhiteSpace(arrivalContext)
                && arrivalContext.IndexOf("arrival_source=game_start", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string BuildGameContext(string pawnLabel, string pawnLoadId, string arrivalContext)
        {
            List<string> parts = new List<string>
            {
                "arrival_description=true",
                "arrival_pawn=" + (pawnLabel ?? string.Empty),
                "arrival_pawn_id=" + (pawnLoadId ?? string.Empty)
            };

            if (!string.IsNullOrWhiteSpace(arrivalContext))
            {
                parts.Add(arrivalContext);
            }
            else
            {
                parts.Add("arrival_source=unknown");
            }

            return string.Join("; ", parts.ToArray());
        }
    }
}
