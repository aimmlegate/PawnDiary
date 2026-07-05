// Day/quadrum-reflection ingestion signal — the emit half of the reflection source. Unlike the
// other sources this is not a discrete captured event: the component's sleep-path flush
// (FlushDaySummaryForPawn) aggregates the day's most notable signals, consumes the per-day filler and
// hediff batches, or selects a bounded list of quadrum highlights, then builds the reflection text.
// That aggregation stays on the component (it reads and mutates day-summary state); this signal is
// the thin carrier that runs the catalog decision and emits the finished entry through the shared
// dispatcher.
//
// The component Dispatches this directly (not the void Submit façade) and records the pawn/day as
// written only when Dispatch reports the event emitted — preserving the old inline coupling between
// the catalog decision and the writtenDayReflections guard. Pure decision + game-context format live
// in Source/Capture/Events/DayReflectionEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Carries one pawn's already-aggregated day or quadrum reflection (payload + pre-built text) and
    /// emits it as a solo entry. Built by the component's summary flush.
    /// </summary>
    internal sealed class DayReflectionSignal : DiarySignal
    {
        private readonly DayReflectionEventData payload;
        private readonly Pawn pawn;
        private readonly string label;
        private readonly string text;
        private readonly string instruction;
        private readonly string gameContext;

        public DayReflectionSignal(DayReflectionEventData payload, Pawn pawn,
            string label, string text, string instruction, string gameContext)
        {
            this.payload = payload;
            this.pawn = pawn;
            this.label = label;
            this.text = text;
            this.instruction = instruction;
            this.gameContext = gameContext;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            // Honor the Advanced-tab "Reflection" filter row (defName `reflection`), which matches
            // DayReflection / QuadrumReflection via matchDefNames. Without this the player-visible
            // toggle was inert for day/quadrum reflections. Mirrors Hediff/Death/MentalState signals.
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Reflection, payload.DefName);
            bool userEnabled = group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);

            return DiaryGameComponent.BuildCaptureContext(
                eligible: true,
                userEnabled: userEnabled,
                signalEnabled: DiaryTuning.Current.daySummaryEnabled,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string defName = string.IsNullOrWhiteSpace(payload.DefName)
                ? DayReflectionEventData.DefNameToken
                : payload.DefName;
            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, defName,
                label, text, instruction, gameContext);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }
    }
}
