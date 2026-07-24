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
    internal sealed class ArrivalSignal : DiarySignal
    {
        // Stable save/classification tokens for the synthetic arrival event (moved from
        // DiaryGameComponent: the arrival group key and the arrival defName).
        internal const string ArrivalGroupKey = "arrival";
        internal const string ArrivalDefName = "PawnDiary_Arrival";

        private readonly Pawn pawn;
        private readonly string arrivalContext;
        private readonly string capturedOriginCultureDefName;
        private readonly DiaryInteractionGroupDef arrivalGroup;
        private readonly ArrivalEventData payload;

        public ArrivalSignal(Pawn pawn, string arrivalContext)
            : this(pawn, arrivalContext, string.Empty)
        {
        }

        /// <summary>
        /// Builds an arrival with the origin culture captured before SetFaction mutated the pawn.
        /// The two-argument overload remains for starting-colonist scans and binary compatibility.
        /// </summary>
        public ArrivalSignal(Pawn pawn, string arrivalContext, string capturedOriginCultureDefName)
        {
            this.pawn = pawn;
            this.arrivalContext = arrivalContext;
            this.capturedOriginCultureDefName = capturedOriginCultureDefName ?? string.Empty;

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
                // Needs the component's persisted page/knowledge state, so the pure payload receives
                // the result rather than reaching into live game state itself. Arrival is a one-time
                // boundary. A disabled page still leaves durable faction-joined
                // knowledge, and that marker must reject later submissions exactly like an existing
                // hot or archived page.
                HasExistingArrival = DiaryGameComponent.Instance?.HasArrivalBoundaryFor(pawnId) ?? false,
            };

            // Origin is a one-time identity fact, not page policy. Capture it before dispatch can
            // reject a disabled arrival page.
            DiaryGameComponent.Instance?.CaptureOriginCulture(
                pawn, this.capturedOriginCultureDefName);

            // The arrival route is the sole owner of creepjoiner acceptance continuity. State is
            // observed before catalog/settings gates so disabling pages cannot manufacture a later
            // catch-up event; no arrival event ID is stored until Emit actually creates that page.
            DiaryGameComponent.Instance?.ObserveCreepJoinerArrival(
                pawn, arrivalContext, createdArrivalEventId: null);
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

        public override void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
            if (payload == null)
            {
                return;
            }

            sink.CaptureEventKnowledgeWithoutPage(
                pawn,
                null,
                ArrivalDefName,
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

            bool startingPawn = ArrivalEventData.IsStartingArrival(arrivalContext);
            string label = "PawnDiary.Event.ArrivalLabel".Translate().Resolve();
            string text = startingPawn
                ? "PawnDiary.Event.StartingArrival".Translate(pawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.JoinedArrival".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent arrivalEvent = sink.AddSoloEvent(pawn, null, ArrivalDefName, label, text, string.Empty,
                ArrivalEventData.BuildGameContext(payload.PawnLabel, payload.PawnLoadId, payload.ArrivalContext));
            if (arrivalEvent != null)
                sink.ObserveCreepJoinerArrival(pawn, arrivalContext, arrivalEvent.eventId);
            sink.QueueArrivalDescriptionFor(arrivalEvent);
        }
    }
}
