// Payload + pure decision for significant completed trades. The live TradeDeal.TryExecute patch
// snapshots the trade before vanilla resolves it; this class owns the value threshold gate and
// context marker format.
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one completed trade or gift deal.
    /// </summary>
    public class TradeDealEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.TradeDeal;

        public string DefName;
        public string PartnerLabel;
        public string PartnerFactionDefName;
        public string TraderKindDefName;
        public string Summary;
        public float TotalMarketValue;
        public float MinMarketValue;
        public int ItemCount;
        public bool GiftMode;

        public static CaptureDecision Decide(TradeDealEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (data.ItemCount <= 0 || data.TotalMarketValue < data.MinMarketValue)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public string DedupKey()
        {
            return "trade_deal|" + (PawnId ?? string.Empty) + "|" + (PartnerLabel ?? string.Empty)
                + "|" + Tick.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildGameContext(
            string defName,
            string partnerLabel,
            string partnerFactionDefName,
            string traderKindDefName,
            string summary,
            float totalMarketValue,
            int itemCount,
            bool giftMode)
        {
            return "trade_deal=" + Clean(defName)
                + "; trade_mode=" + (giftMode ? "gift" : "trade")
                + "; trade_partner=" + Fallback(partnerLabel, "unknown")
                + "; trade_faction=" + Fallback(partnerFactionDefName, "unknown")
                + "; trader_kind=" + Fallback(traderKindDefName, "unknown")
                + "; trade_value=" + totalMarketValue.ToString("0.#", CultureInfo.InvariantCulture)
                + "; trade_items=" + itemCount.ToString(CultureInfo.InvariantCulture)
                + "; trade_summary=" + Fallback(summary, "none");
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
