// Catalog payload for one exact successful dramatic Royalty permit use. The Harmony/live adapter
// already proved vanilla reached FactionPermit.Notify_Used and detached the owner/faction facts;
// this value performs the final pure eligibility, family, setting, and identity gates.
namespace PawnDiary.Capture
{
    /// <summary>Primitive facts deciding whether one successful permit use creates a solo page.</summary>
    internal sealed class RoyalPermitEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.RoyalPermit;

        public string DefName = string.Empty;
        public string PermitDefName = string.Empty;
        public string PermitFamily = string.Empty;
        public string FactionId = string.Empty;
        public bool PawnEligible;
        public bool HasExactSuccessfulUse;

        /// <summary>Applies the final pure truth, eligibility, and player-setting gates.</summary>
        public static CaptureDecision Decide(RoyalPermitEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || !data.PawnEligible || !data.HasExactSuccessfulUse
                || string.IsNullOrWhiteSpace(data.PawnId)
                || !SafeId(data.PermitDefName) || !SafeId(data.FactionId)
                || !RoyalPermitFamilyTokens.IsKnown(data.PermitFamily)
                || RoyalPermitPolicy.EventDefNameForFamily(data.PermitFamily) != data.DefName)
                return CaptureDecision.Drop;
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>Suppresses only immediate repeats of the same owner/permit/faction success.</summary>
        public string DedupKey()
        {
            return !string.IsNullOrWhiteSpace(PawnId) && SafeId(PermitDefName) && SafeId(FactionId)
                ? "royal-permit|" + PawnId.Trim() + "|" + PermitDefName.Trim() + "|" + FactionId.Trim()
                : string.Empty;
        }

        private static bool SafeId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0;
        }
    }
}
