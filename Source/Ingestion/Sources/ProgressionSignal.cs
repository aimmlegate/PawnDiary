// Progression ingestion signal — the impure emit half of the pawn progression scanner. The scanner
// observes live pawn state, updates small saved bookkeeping, then submits this signal when a future
// change should become an ordinary solo diary page.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Carries one already-detected progression moment through the shared catalog dispatcher.
    /// </summary>
    internal sealed class ProgressionSignal : DiarySignal
    {
        private readonly ProgressionEventData payload;
        private readonly Pawn pawn;
        private readonly string label;
        private readonly string text;
        private readonly string instruction;
        private readonly string gameContext;
        private readonly bool eligible;
        private readonly bool userEnabled;
        private readonly bool signalEnabled;
        private readonly string dedupKey;
        private readonly int dedupWindowTicks;
        private readonly List<NarrativeEvidence> narrativeEvidence;
        private readonly BiotechNarrativeSnapshot biotechNarrative;

        public ProgressionSignal(ProgressionEventData payload, Pawn pawn, string label, string text,
            string instruction, string gameContext, bool eligible, bool userEnabled, bool signalEnabled,
            string dedupKey = null, int dedupWindowTicks = 0,
            List<NarrativeEvidence> narrativeEvidence = null,
            BiotechNarrativeSnapshot biotechNarrative = null)
        {
            this.payload = payload;
            this.pawn = pawn;
            this.label = label;
            this.text = text;
            this.instruction = instruction;
            this.gameContext = gameContext;
            this.eligible = eligible;
            this.userEnabled = userEnabled;
            this.signalEnabled = signalEnabled;
            this.dedupKey = dedupKey;
            this.dedupWindowTicks = dedupWindowTicks;
            this.narrativeEvidence = narrativeEvidence;
            this.biotechNarrative = biotechNarrative;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: eligible,
                userEnabled: userEnabled,
                signalEnabled: signalEnabled,
                ambientSignalEnabled: true);
        }

        // Scalar progression kinds (skill, psylink, xenotype, royal title) emit at most one page per
        // pawn per scan, so they leave this empty and rely on the dispatcher's generic type+subject
        // safety key. The trait-gain scanner sets a per-trait key so that a pawn gaining more than one
        // trait in a single scan produces a distinct page for each instead of colliding on that generic
        // key. Empty (the default) keeps the pre-existing behavior for every other caller.
        public override string DedupKey => string.IsNullOrEmpty(dedupKey) ? string.Empty : dedupKey;

        public override int DedupWindowTicks => dedupWindowTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, payload.DefName,
                label, text, instruction, gameContext);
            ApplyNarrativeEvidence(sink, diaryEvent);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Freezes optional source-owned continuity before generation. Ordinary skill/title/trait
        /// progression supplies no evidence and keeps the historical zero-work path.
        /// </summary>
        private void ApplyNarrativeEvidence(DiaryGameComponent sink, DiaryEvent diaryEvent)
        {
            if (sink == null || diaryEvent == null || pawn == null
                || narrativeEvidence == null || narrativeEvidence.Count == 0)
            {
                return;
            }

            try
            {
                string pawnId = pawn.GetUniqueLoadID();
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        evidence = narrativeEvidence,
                        biotech = biotechNarrative,
                        odyssey = sink.OdysseyNarrativeSnapshotFor(pawn, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full)
                    });
                if (result.evidence.Count > 0)
                {
                    diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Progression Narrative Continuity evidence failed; the page remains: "
                    + exception,
                    "PawnDiary.Progression.NarrativeEvidence".GetHashCode());
            }
        }
    }
}
