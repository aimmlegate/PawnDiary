// Trade-deal ingestion signal — the impure capture+emit half of TradeDeal.TryExecute. The Harmony
// prefix snapshots the deal before vanilla resolves transfer counts; the postfix submits only when
// TryExecute reports that a real trade happened.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one completed significant trade or gift for the player negotiator.
    /// </summary>
    public sealed class TradeDealSignal : DiarySignal
    {
        private static readonly System.Reflection.FieldInfo TraderField = AccessTools.Field(typeof(TradeSession), "trader");
        private static readonly System.Reflection.FieldInfo NegotiatorField = AccessTools.Field(typeof(TradeSession), "playerNegotiator");
        private static readonly System.Reflection.FieldInfo GiftModeField = AccessTools.Field(typeof(TradeSession), "giftMode");

        private readonly Pawn pawn;
        private readonly DiaryInteractionGroupDef group;
        private readonly TradeDealEventData payload;
        private readonly string label;

        public TradeDealSignal(TradeDeal deal)
        {
            if (!DiaryGameComponent.GamePlaying || deal == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Pawn negotiator = NegotiatorField?.GetValue(null) as Pawn;
            if (negotiator == null || !DiaryGameComponent.IsDiaryEligible(negotiator))
            {
                return;
            }

            bool giftMode = GiftModeField != null && GiftModeField.GetValue(null) is bool gift && gift;
            ITrader trader = TraderField?.GetValue(null) as ITrader;
            string classifier = giftMode ? "gift" : "trade";
            DiaryInteractionGroupDef classified = InteractionGroups.ClassifyTradeDeal(classifier);
            if (classified == null || !PawnDiaryMod.Settings.IsGroupEnabled(classified.defName))
            {
                return;
            }

            TradeSnapshot snapshot = SnapshotDeal(deal, DiarySignalPolicies.TradeDealMaxSummaryItems);
            float minValue = DiarySignalPolicies.TradeDealMinMarketValue;
            if (snapshot.ItemCount <= 0 || snapshot.TotalMarketValue < minValue)
            {
                return;
            }

            pawn = negotiator;
            group = classified;
            label = giftMode
                ? "PawnDiary.Event.TradeGift.Label".Translate().Resolve()
                : "PawnDiary.Event.TradeDeal.Label".Translate().Resolve();
            payload = new TradeDealEventData
            {
                PawnId = negotiator.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = classifier,
                PartnerLabel = TraderLabel(trader),
                PartnerFactionDefName = trader?.Faction?.def?.defName ?? "unknown",
                TraderKindDefName = trader?.TraderKind?.defName ?? "unknown",
                Summary = snapshot.Summary,
                TotalMarketValue = snapshot.TotalMarketValue,
                MinMarketValue = minValue,
                ItemCount = snapshot.ItemCount,
                GiftMode = giftMode
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true,
                userEnabled: true,
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.TradeDeal),
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload == null ? string.Empty : payload.DedupKey();

        public override int DedupWindowTicks => DiarySignalPolicies.TradeDealDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo || pawn == null || payload == null)
            {
                return;
            }

            string context = TradeDealEventData.BuildGameContext(
                payload.DefName, payload.PartnerLabel, payload.PartnerFactionDefName,
                payload.TraderKindDefName, payload.Summary, payload.TotalMarketValue,
                payload.ItemCount, payload.GiftMode);
            string textKey = payload.GiftMode
                ? "PawnDiary.Event.TradeGift"
                : "PawnDiary.Event.TradeDeal";
            string text = textKey.Translate(pawn.LabelShortCap, payload.PartnerLabel, payload.Summary).Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, payload.DefName, label, text, instruction, context);
            if (diaryEvent == null)
            {
                return;
            }

            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        private static TradeSnapshot SnapshotDeal(TradeDeal deal, int maxSummaryItems)
        {
            TradeSnapshot snapshot = new TradeSnapshot();
            List<Tradeable> tradeables = deal.AllTradeables;
            if (tradeables == null)
            {
                return snapshot;
            }

            int summaryLimit = maxSummaryItems < 0 ? 4 : maxSummaryItems;
            StringBuilder summary = new StringBuilder();
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable tradeable = tradeables[i];
                if (tradeable == null || tradeable.CountToTransfer == 0)
                {
                    continue;
                }

                int count = Math.Abs(tradeable.CountToTransfer);
                snapshot.ItemCount += count;
                snapshot.TotalMarketValue += Math.Max(0f, tradeable.BaseMarketValue) * count;

                if (tradeable.IsCurrency || summaryLimit == 0 || snapshot.SummaryItems >= summaryLimit)
                {
                    continue;
                }

                string itemLabel = DiaryLineCleaner.CleanLine(tradeable.Label);
                if (string.IsNullOrWhiteSpace(itemLabel))
                {
                    itemLabel = tradeable.ThingDef?.defName ?? "item";
                }

                if (summary.Length > 0)
                {
                    summary.Append(", ");
                }

                summary.Append(count).Append("x ").Append(itemLabel);
                snapshot.SummaryItems++;
            }

            snapshot.Summary = summary.Length == 0
                ? "PawnDiary.Event.TradeDeal.SummaryFallback".Translate(snapshot.ItemCount).Resolve()
                : summary.ToString();
            return snapshot;
        }

        private static string TraderLabel(ITrader trader)
        {
            string label = DiaryLineCleaner.CleanLine(trader?.TraderName);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = DiaryLineCleaner.CleanLine(trader?.Faction?.Name);
            }

            return string.IsNullOrWhiteSpace(label)
                ? "PawnDiary.Event.TradeDeal.UnknownTrader".Translate().Resolve()
                : label;
        }

        private struct TradeSnapshot
        {
            public float TotalMarketValue;
            public int ItemCount;
            public int SummaryItems;
            public string Summary;
        }
    }
}
