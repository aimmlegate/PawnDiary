// Thought ingestion signal — the impure capture+emit half of the "temporary thought gained" source
// (MemoryThoughtHandler.TryGainMemory). This replaces the old DiaryGameComponent.RecordThought:
// the shared pipeline in DiaryGameComponent.Dispatch now runs the guard, the pure Decide, and the
// dedup; this class only snapshots live Pawn/Thought facts into the payload and, once the decision is
// in, builds the localized text + game-context and creates/queues the entry (or routes it to the
// ambient day-note batcher).
//
// The pure decision and game-context format still live in Source/Capture/Events/ThoughtEventData.cs
// and are unit-tested there without RimWorld. New to C#/RimWorld? See AGENTS.md.
using System;
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
        private readonly string biotechFamilyContext;
        private readonly string thoughtLabel;
        private readonly BeliefEventEvidence beliefEvidence;

        /// <summary>Detached source identity/evidence exposed to focused RimTests.</summary>
        internal BeliefEventEvidence CapturedBeliefEvidence => beliefEvidence;

        public ThoughtSignal(Pawn pawn, Thought_Memory thought)
        {
            this.pawn = pawn;
            this.thought = thought;

            // A null payload tells the dispatcher to drop the event. The GamePlaying check mirrors the
            // old RecordThought's leading CanRecordGameplayEventNow guard: it runs before any
            // Find.TickManager access below, so a hook firing during pawn/world generation no-ops
            // safely (matches the old guard: pawn/thought/thought.def null).
            //
            // IsDiaryEligible MUST be checked here, before thought.MoodOffset() below. MoodOffset re-runs
            // RimWorld's ThoughtUtility.ThoughtNullified, which dereferences thought.pawn.story (trait
            // nullifiers) and thought.pawn.health (hediff nullifiers). Those trackers are null for
            // animals / non-humanlike or not-fully-built pawns, so calling MoodOffset for them threw an
            // NRE *inside* RimWorld — caught by DiaryPatchSafety and logged as "ThoughtGainPatch failed
            // and was skipped". TryGainMemory is one of the hottest hooks in the game (every meal, sleep,
            // and social memory for every pawn, animals included), and ThoughtEventData.Decide already
            // drops anything that is not diary-eligible at its first gate. So gating here is
            // behavior-preserving, removes the crash, and skips wasted payload/MoodOffset work.
            //
            // thought.pawn (the field on the thought, distinct from the pawn argument) guards the second
            // MoodOffset crash avenue: MoodOffset passes the THOUGHT'S OWN pawn into ThoughtNullified,
            // and 1.6's NullifyingHediff dereferences pawn.health unconditionally. Vanilla TryGainMemory
            // assigns thought.pawn only AFTER its accept-gates pass, so a rejected memory reaches the
            // postfix with thought.pawn still null and MoodOffset throws deep inside RimWorld. (In the
            // wild the trace points at ThoughtUtility.NullifyingHediff and lists other mods' postfixes
            // on it — e.g. Vanilla Psycasts Expanded's NullDarkness — but those annotations only mean
            // the method is patched; the null pawn is ours to filter.) A rejected memory was never
            // gained, so dropping it is a data fix, not just crash-proofing. ThoughtGainPatch filters
            // these too; this keeps the invariant next to the MoodOffset call it protects.
            if (!DiaryGameComponent.GamePlaying || pawn == null || thought == null || thought.def == null
                || thought.pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
            {
                return;
            }

            moodOffset = thought.MoodOffset();
            biotechFamilyContext = BiotechBirthCorrelation.MiscarriageContext(pawn, thought.def.defName);
            string downstreamGroup = string.Empty;
            if (ModsConfig.IdeologyActive)
            {
                BeliefPolicySnapshot beliefPolicy = DiaryBeliefPolicy.Snapshot();
                downstreamGroup = BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    thought.def.defName,
                    ideologyActive: true,
                    policyEnabled: beliefPolicy.enabled,
                    rules: beliefPolicy.canonicalEventOwnershipRules);
            }
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
                DownstreamCovered = downstreamGroup.Length > 0 && PawnDiaryMod.Settings != null
                    && PawnDiaryMod.Settings.IsGroupEnabled(downstreamGroup),
            };
            thoughtLabel = ResolveThoughtLabel(thought.def);
            beliefEvidence = BeliefEventEvidenceFactory.ForThought(
                payload.PawnId,
                payload.Tick,
                payload.DefName,
                thoughtLabel,
                DlcContext.CaptureThoughtSourcePrecept(thought));
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
            string label = thoughtLabel;
            string instruction = InteractionGroups.InstructionForThought(thought.def);

            if (decision == CaptureDecision.RouteAmbient)
            {
                sink.RecordAmbientThought(pawn, thought.def, label, moodOffset, payload.MoodImpact, instruction);
                return;
            }

            string gameContext = ThoughtEventData.BuildGameContext(
                payload.DefName, label, payload.MoodImpact, moodOffset, thought.def.durationDays);
            if (!string.IsNullOrWhiteSpace(biotechFamilyContext))
            {
                gameContext = string.IsNullOrWhiteSpace(gameContext)
                    ? biotechFamilyContext
                    : gameContext.Trim().TrimEnd(';') + "; " + biotechFamilyContext;
            }

            string text = MoodImpact.PickText(payload.MoodImpact,
                "PawnDiary.Event.ThoughtPositive", "PawnDiary.Event.ThoughtNegative", "PawnDiary.Event.Thought",
                pawn.LabelShortCap, label);

            DiaryEvent thoughtEvent = CreateSoloEvent(
                sink, pawn, null, payload.DefName, label, text, instruction, gameContext,
                beliefEvidence);
            thoughtEvent.moodImpact = payload.MoodImpact;
            sink.QueueSolo(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        // ThoughtDef.LabelCap normally resolves through the Def's localized label/stages, but a
        // malformed or transient modded ThoughtDef can throw inside RimWorld's Label getter even
        // though the ThoughtDef itself is non-null. Keep the capture and use the stable defName as a
        // last-resort technical label; one broken Def should not escape to DiaryPatchSafety and drop
        // the whole thought event. The warning is keyed by exception type so a hot thought hook cannot
        // flood Player.log when several memories share the same malformed shape.
        private static string ResolveThoughtLabel(ThoughtDef thoughtDef)
        {
            string fallback = DiaryLineCleaner.CleanLine(thoughtDef?.defName);
            if (thoughtDef == null)
            {
                return fallback;
            }

            try
            {
                string label = DiaryLineCleaner.CleanLine(thoughtDef.LabelCap.Resolve());
                return string.IsNullOrWhiteSpace(label) ? fallback : label;
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Could not read a thought label; using its defName so capture can continue: " + e,
                    ("PawnDiary.ThoughtLabelFallback." + e.GetType().Name).GetHashCode());
                return fallback;
            }
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
