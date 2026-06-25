// Thoughts — the MemoryThoughtHandler.TryGainMemory hook's diary flow, now built on the Event
// Catalog pattern (see Source/Capture/). A pawn gaining a temporary memory thought becomes a solo
// DiaryEvent, but only after filtering: permanent thoughts (durationDays <= 0), configurable ignore
// tokens, and sub-threshold magnitudes are dropped (eating thoughts use a higher bar; "bypass"
// thoughts like death/banishment skip the threshold). Low-stakes ambient-token thoughts are routed
// to the per-pawn/day ambient batcher instead. The event carries a "thought=" game-context marker
// so the UI classifies it into the Thought domain.
//
// This file now contains only the IMPURE half of the flow: snapshotting live Pawn/Thought facts into
// ThoughtEventData + CaptureContext, asking DiaryEventCatalog for the pure decision, then performing
// whatever impure action the decision requests (dedup, ambient routing, save mutation, LLM queue).
// The pure decision logic (token match, threshold gate, ambient routing) lives in
// Source/Capture/Events/ThoughtEventData.cs and is unit-tested without RimWorld.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records a diary event when a pawn gains a temporary thought with expiration. Delegates the
        /// "should we?" decision to ThoughtEventData.Decide (pure) and keeps the impure side-effects
        /// (dedup, ambient routing, event creation, LLM queue) here.
        /// </summary>
        public void RecordThought(Pawn pawn, Thought_Memory thought)
        {
            if (!CanRecordGameplayEventNow() || pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            // Snapshot the live RimWorld facts the pure Decider needs. All impure reads (Pawn state,
            // DefDatabase, Settings, tick manager) happen here, so the Decider itself stays pure.
            float moodOffset = thought.MoodOffset();
            ThoughtEventData data = new ThoughtEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = thought.def.defName,
                MoodOffset = moodOffset,
                DurationDays = thought.def.durationDays,
                // MoodImpact.Classify needs the Verse-using MoodImpact helper, so classify here and
                // pass the resolved token into the pure layer.
                MoodImpact = MoodImpact.Classify(moodOffset),
                Policy = SnapshotThoughtPolicy(),
            };

            CaptureContext ctx = BuildCaptureContext(
                eligible: IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.IsThoughtEnabled(thought.def),
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.Thought),
                ambientSignalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.AmbientThought));

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Thought);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision == CaptureDecision.Drop)
            {
                return;
            }

            // Dedup happens AFTER the decision, BEFORE the impure build — same order as before the
            // refactor, so a thought that would be ambient-routed still benefits from dedup only if
            // it would otherwise have been a solo event. (Ambient routing has its own dedup via the
            // ambient batcher's "written" guard.)
            string dedupKey = "thought|" + data.PawnId + "|" + data.DefName;
            if (RecentlyRecorded(recentThoughtEvents, dedupKey, DiarySignalPolicies.ThoughtDedupTicks))
            {
                return;
            }

            // Impure build: label, instruction, game-context marker, localized event text. These need
            // the live ThoughtDef (LabelCap, settings instruction) so they cannot live in the pure
            // payload layer.
            string label = DiaryContextBuilder.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = InteractionGroups.InstructionForThought(thought.def);

            if (decision == CaptureDecision.RouteAmbient)
            {
                RecordAmbientThought(pawn, thought.def, label, moodOffset, data.MoodImpact, instruction);
                return;
            }

            string gameContext = ThoughtEventData.BuildGameContext(
                data.DefName, label, data.MoodImpact, moodOffset, thought.def.durationDays);

            string text = MoodImpact.PickText(data.MoodImpact,
                "PawnDiary.Event.ThoughtPositive", "PawnDiary.Event.ThoughtNegative", "PawnDiary.Event.Thought",
                pawn.LabelShortCap, label);

            DiaryEvent thoughtEvent = AddSoloEvent(pawn, null, data.DefName, label, text, instruction, gameContext);
            thoughtEvent.moodImpact = data.MoodImpact;
            QueueLlmRewrite(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Copies the live Thought signal policy (tokens + thresholds) out of DiarySignalPolicies into
        /// a frozen ThoughtCapturePolicy snapshot the pure Decider can read without touching the
        /// DefDatabase.
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

        /// <summary>
        /// Builds a CaptureContext from the impure eligibility/enable facts the caller already
        /// computed. Centralized so each source's Record method reads the same way and the pure
        /// Decider sees a consistent snapshot.
        /// </summary>
        private static CaptureContext BuildCaptureContext(
            bool eligible, bool userEnabled, bool signalEnabled, bool ambientSignalEnabled)
        {
            return new CaptureContext
            {
                Eligible = eligible,
                UserEnabled = userEnabled,
                SignalEnabled = signalEnabled,
                AmbientSignalEnabled = ambientSignalEnabled,
                Now = Find.TickManager.TicksGame,
            };
        }
    }
}
