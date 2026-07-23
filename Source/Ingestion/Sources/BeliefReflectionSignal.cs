// Thin Ideology belief-reflection ingestion carrier. The component owns trigger selection and saved
// debt; this signal runs the catalog gate and creates one solo page with an already-frozen context.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Carries one prepared belief reflection through the shared dispatcher.</summary>
    internal sealed class BeliefReflectionSignal : DiarySignal
    {
        private readonly BeliefReflectionEventData payload;
        private readonly Pawn pawn;
        private readonly string label;
        private readonly string text;
        private readonly string instruction;
        private readonly string gameContext;
        private readonly BeliefContextBuildResult preparedBelief;

        public BeliefReflectionSignal(
            BeliefReflectionEventData payload,
            Pawn pawn,
            string label,
            string text,
            string instruction,
            string gameContext,
            BeliefContextBuildResult preparedBelief)
        {
            this.payload = payload;
            this.pawn = pawn;
            this.label = label;
            this.text = text;
            this.instruction = instruction;
            this.gameContext = gameContext;
            this.preparedBelief = preparedBelief;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Reflection, payload.DefName);
            bool userEnabled = group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: userEnabled,
                signalEnabled: ModsConfig.IdeologyActive && DiaryBeliefPolicy.Snapshot().enabled,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo) return;
            DiaryEvent diaryEvent = sink.AddSoloEvent(
                pawn,
                null,
                BeliefReflectionEventData.DefNameToken,
                label,
                text,
                instruction,
                gameContext,
                null,
                preparedBelief);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }
    }
}
