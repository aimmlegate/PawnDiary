// Thought ingestion signal — the impure capture+emit half of the "temporary thought gained" source
// (MemoryThoughtHandler.TryGainMemory). This replaces the old DiaryGameComponent.RecordThought:
// the shared pipeline in DiaryGameComponent.Dispatch now runs the guard, the pure Decide, and the
// dedup; this class only snapshots live Pawn/Thought facts into the payload and, once the decision is
// in, builds the localized text + game-context and creates/queues the entry (or routes it to the
// ambient day-note batcher).
//
// The pure decision and game-context format still live in Source/Capture/Events/ThoughtEventData.cs
// and are unit-tested there without RimWorld. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one gained temporary memory thought and emits it as a solo diary event (or routes it
    /// to the ambient batcher). Built by <see cref="ThoughtGainPatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class ThoughtSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly Thought_Memory thought;
        private readonly float moodOffset;
        private readonly ThoughtEventData payload;

        public ThoughtSignal(Pawn pawn, Thought_Memory thought)
        {
            this.pawn = pawn;
            this.thought = thought;

            // A null payload tells the dispatcher to drop the event. The GamePlaying check mirrors the
            // old RecordThought's leading CanRecordGameplayEventNow guard: it runs before any
            // Find.TickManager access below, so a hook firing during pawn/world generation no-ops
            // safely (matches the old guard: pawn/thought/thought.def null).
            if (!DiaryGameComponent.GamePlaying || pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            moodOffset = thought.MoodOffset();
            payload = new ThoughtEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = thought.def.defName,
                MoodOffset = moodOffset,
                DurationDays = thought.def.durationDays,
                // MoodImpact.Classify lives in Verse-using code, so classify here and pass the resolved
                // token into the pure payload.
                MoodImpact = MoodImpact.Classify(moodOffset),
                Policy = SnapshotThoughtPolicy(),
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.IsThoughtEnabled(thought.def),
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.Thought),
                ambientSignalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.AmbientThought));
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        public override int DedupWindowTicks => DiarySignalPolicies.ThoughtDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            // Impure build: label, instruction. These need the live ThoughtDef (LabelCap, settings
            // instruction) so they cannot live in the pure payload layer.
            string label = DiaryLineCleaner.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = InteractionGroups.InstructionForThought(thought.def);

            if (decision == CaptureDecision.RouteAmbient)
            {
                sink.RecordAmbientThought(pawn, thought.def, label, moodOffset, payload.MoodImpact, instruction);
                return;
            }

            string gameContext = ThoughtEventData.BuildGameContext(
                payload.DefName, label, payload.MoodImpact, moodOffset, thought.def.durationDays);

            string text = MoodImpact.PickText(payload.MoodImpact,
                "PawnDiary.Event.ThoughtPositive", "PawnDiary.Event.ThoughtNegative", "PawnDiary.Event.Thought",
                pawn.LabelShortCap, label);

            DiaryEvent thoughtEvent = sink.AddSoloEvent(pawn, null, payload.DefName, label, text, instruction, gameContext);
            thoughtEvent.moodImpact = payload.MoodImpact;
            sink.QueueSolo(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Copies the live Thought signal policy (tokens + thresholds) out of DiarySignalPolicies into
        /// a frozen ThoughtCapturePolicy snapshot the pure Decider can read without touching the
        /// DefDatabase. Moved verbatim from the old DiaryGameComponent.SnapshotThoughtPolicy.
        /// </summary>
        private static ThoughtCapturePolicy SnapshotThoughtPolicy()
        {
            return new ThoughtCapturePolicy
            {
                IgnoreTokens = DiarySignalPolicies.ThoughtIgnoreTokens,
                BypassThresholdTokens = DiarySignalPolicies.ThoughtBypassThresholdTokens,
                EatingTokens = DiarySignalPolicies.ThoughtEatingTokens,
                AmbientTokens = DiarySignalPolicies.ThoughtAmbientTokens,
                MinMoodOffset = DiarySignalPolicies.ThoughtMinMoodOffset,
                EatingMinMoodOffset = DiarySignalPolicies.ThoughtEatingMinMoodOffset,
            };
        }
    }
}
