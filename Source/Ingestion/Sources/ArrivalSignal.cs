// Arrival ingestion signal — the impure capture+emit half of the "colonist joined" source. Replaces
// the old DiaryGameComponent.RecordColonistArrival. Every colonist gets one neutral first entry
// describing how they joined (game start vs. recruited/joined later). Two capture paths build the
// signal: the Pawn.SetFaction postfix (joins after game start) and the first-playing-tick
// starting-colonist scan (founding colonists). Both submit through the bus; the starting scan stays
// on the component because it polls map state, but it now hands each pawn to a signal.
//
// Pure decision (incl. the "already has an arrival" drop) + the arrival game-context format live in
// Source/Capture/Events/ArrivalEventData.cs. The arrival cause facts come from ArrivalContextCache,
// captured by the SetFaction PREFIX before vanilla mutates the pawn. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one colonist arrival and emits a neutral arrival-description page. Built by
    /// <see cref="PawnSetFactionPatch"/> and by the starting-colonist scan, then submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class ArrivalSignal : DiarySignal
    {
        // Stable save/classification tokens for the synthetic arrival event (moved from
        // DiaryGameComponent: the arrival group key and the arrival defName).
        internal const string ArrivalGroupKey = "arrival";
        internal const string ArrivalDefName = "PawnDiary_Arrival";

        private readonly Pawn pawn;
        private readonly string arrivalContext;
        private readonly DiaryInteractionGroupDef arrivalGroup;
        private readonly ArrivalEventData payload;

        public ArrivalSignal(Pawn pawn, string arrivalContext)
        {
            this.pawn = pawn;
            this.arrivalContext = arrivalContext;

            if (!DiaryGameComponent.GamePlaying || pawn == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            arrivalGroup = InteractionGroups.ByKey(ArrivalGroupKey);
            payload = new ArrivalEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = ArrivalDefName,
                PawnLabel = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                PawnLoadId = pawnId,
                ArrivalContext = arrivalContext,
                // Needs the live event store; read it through the current component. Matches the old
                // RecordColonistArrival capture order (drops if an arrival page already exists).
                HasExistingArrival = DiaryGameComponent.Current?.HasArrivalEventFor(pawnId) ?? false,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: arrivalGroup == null || PawnDiaryMod.Settings.IsGroupEnabled(arrivalGroup.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSoloArrivalDescription)
            {
                return;
            }

            bool startingPawn = ArrivalEventData.IsStartingArrival(arrivalContext);
            string label = "PawnDiary.Event.ArrivalLabel".Translate().Resolve();
            string text = startingPawn
                ? "PawnDiary.Event.StartingArrival".Translate(pawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.JoinedArrival".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent arrivalEvent = sink.AddSoloEvent(pawn, null, ArrivalDefName, label, text, string.Empty,
                ArrivalEventData.BuildGameContext(payload.PawnLabel, payload.PawnLoadId, payload.ArrivalContext));
            sink.QueueArrivalDescriptionFor(arrivalEvent);
        }
    }
}
