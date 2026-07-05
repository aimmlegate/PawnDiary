// Thought-progression ingestion signal — the impure capture+emit half of the "situational need
// thought worsened" source (hunger, exhaustion, outdoors deprivation, chemical desire). These stages
// never pass through MemoryThoughtHandler.TryGainMemory, so the component's periodic scan tracks live
// episode state and submits a ThoughtProgressionSignal when a matched stage first appears or worsens.
// Replaces the old DiaryGameComponent.RecordThoughtProgression.
//
// The scan calls DiaryGameComponent.Dispatch directly (not the void Submit façade) so it can read
// whether the event actually recorded — its recorded-stage set is updated only on a true result, just
// like the old method's bool return. Pure decision + game-context + dedup key live in
// Source/Capture/Events/ThoughtProgressionEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System.Globalization;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one worsening situational thought stage and emits a solo diary page. Built by the
    /// component's thought-progression scan.
    /// </summary>
    internal sealed class ThoughtProgressionSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly ThoughtDef thoughtDef;
        private readonly string label;
        private readonly string moodImpact;
        private readonly ThoughtProgressionEventData payload;

        public ThoughtProgressionSignal(Pawn pawn, ThoughtDef thoughtDef, string thoughtDefName, string categoryKey,
            string label, int stageIndex, int severity, float moodOffset, bool worsened, bool stageAlreadyRecorded)
        {
            this.pawn = pawn;
            this.thoughtDef = thoughtDef;
            this.label = label;

            if (!DiaryGameComponent.GamePlaying || pawn == null || thoughtDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            moodImpact = MoodImpact.Classify(moodOffset);
            payload = new ThoughtProgressionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = thoughtDefName,
                CategoryKey = categoryKey,
                Label = label,
                StageIndex = stageIndex.ToString(CultureInfo.InvariantCulture),
                Severity = severity.ToString(CultureInfo.InvariantCulture),
                MoodImpact = moodImpact,
                MoodOffset = moodOffset.ToString("F1", CultureInfo.InvariantCulture),
                Worsened = worsened,
                StageAlreadyRecorded = stageAlreadyRecorded,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings.IsThoughtEnabled(thoughtDef),
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.ThoughtProgression),
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        public override int DedupWindowTicks => DiarySignalPolicies.ThoughtProgressionDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            string instruction = AppendThoughtProgressionInstruction(InteractionGroups.InstructionForThought(thoughtDef));
            string gameContext = ThoughtProgressionEventData.BuildGameContext(
                payload.DefName, payload.CategoryKey, payload.Label, payload.StageIndex,
                payload.Severity, payload.MoodImpact, payload.MoodOffset);
            string text = "PawnDiary.Event.ThoughtProgressionNegative".Translate(pawn.LabelShortCap, label).Resolve();

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, payload.DefName, label, text, instruction, gameContext);
            diaryEvent.moodImpact = moodImpact;
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Appends the progression-specific instruction to the thought group's base instruction. Moved
        /// verbatim from the old DiaryGameComponent.AppendThoughtProgressionInstruction.
        /// </summary>
        private static string AppendThoughtProgressionInstruction(string baseInstruction)
        {
            string progressionInstruction = "PawnDiary.Event.ThoughtProgressionInstruction".Translate().Resolve();
            if (string.IsNullOrWhiteSpace(baseInstruction))
            {
                return progressionInstruction;
            }

            return baseInstruction.Trim() + "; " + progressionInstruction;
        }
    }
}
