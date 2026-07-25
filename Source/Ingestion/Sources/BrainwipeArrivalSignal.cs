// Brainwipe rebirth signal — after vanilla applies the delayed Brainwipe outcome and the component
// clears the target's old memories, this creates one neutral arrival-shaped page as the pawn's new
// autobiographical boundary. It deliberately uses ArrivalEventData and the ArrivalDescription prompt
// template, so ordinary continuity treats this anxious amnesiac awakening as a new beginning.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates the first post-Brainwipe page describing anxiety and missing memories.</summary>
    internal sealed class BrainwipeArrivalSignal : DiarySignal
    {
        internal const string BrainwipeArrivalDefName = "PawnDiary_BrainwipeArrival";
        private const string BrainwipeArrivalContext =
            "arrival_source=brainwipe; memory_state=amnesia; emotional_state=anxiety";

        private readonly Pawn pawn;
        private readonly DiaryInteractionGroupDef arrivalGroup;
        private readonly ArrivalEventData payload;

        public BrainwipeArrivalSignal(Pawn pawn)
        {
            this.pawn = pawn;
            if (!DiaryGameComponent.GamePlaying || pawn == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            arrivalGroup = InteractionGroups.ByKey(ArrivalSignal.ArrivalGroupKey);
            payload = new ArrivalEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = BrainwipeArrivalDefName,
                PawnLabel = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                PawnLoadId = pawnId,
                ArrivalContext = BrainwipeArrivalContext,
                // The reset immediately before this signal intentionally establishes a new boundary.
                HasExistingArrival = false,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: arrivalGroup == null
                    || PawnDiaryMod.Settings.IsGroupEnabled(arrivalGroup.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
            if (payload == null)
            {
                return;
            }

            sink.CaptureEventKnowledgeWithoutPage(
                pawn,
                null,
                BrainwipeArrivalDefName,
                ArrivalEventData.BuildGameContext(
                    payload.PawnLabel, payload.PawnLoadId, payload.ArrivalContext),
                payload.Tick);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSoloArrivalDescription)
            {
                return;
            }

            string label = "PawnDiary.Event.BrainwipeArrivalLabel".Translate().Resolve();
            string text = "PawnDiary.Event.BrainwipeArrival".Translate(pawn.LabelShortCap).Resolve();
            string context = ArrivalEventData.BuildGameContext(
                payload.PawnLabel, payload.PawnLoadId, payload.ArrivalContext);
            DiaryEvent arrivalEvent = sink.AddSoloEvent(
                pawn, null, BrainwipeArrivalDefName, label, text, string.Empty, context);
            sink.QueueArrivalDescriptionFor(arrivalEvent);
        }
    }
}
