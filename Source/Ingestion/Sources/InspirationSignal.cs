// Inspiration ingestion signal — the impure capture+emit half of the "inspiration started" source
// (InspirationHandler.TryStartInspiration). Replaces the old DiaryGameComponent.RecordInspiration.
// A solo source with no dedup (matches pre-refactor behavior). Pure decision + game-context live in
// Source/Capture/Events/InspirationEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one newly-started pawn inspiration and emits it as a solo diary event. Built by
    /// <see cref="InspirationStartPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class InspirationSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly InspirationDef inspirationDef;
        private readonly string reason;
        private readonly InspirationEventData payload;

        public InspirationSignal(Pawn pawn, InspirationDef inspirationDef, string reason)
        {
            this.pawn = pawn;
            this.inspirationDef = inspirationDef;
            this.reason = reason;

            // GamePlaying first (before any Find.TickManager access), mirroring the old
            // RecordInspiration's leading CanRecordGameplayEventNow guard.
            if (!DiaryGameComponent.GamePlaying || pawn == null || inspirationDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            payload = new InspirationEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = inspirationDef.defName,
                DurationDays = inspirationDef.baseDurationDays,
                Reason = reason,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings.IsInspirationEnabled(inspirationDef),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        // Inspiration has no dedup today (DedupKey empty → dispatcher skips the dedup gate).

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            string label = CleanInspirationLabel(inspirationDef);
            string instruction = InteractionGroups.InstructionForInspiration(inspirationDef);
            string cleanedReason = DiaryLineCleaner.CleanLine(reason);
            string gameContext = InspirationEventData.BuildGameContext(
                inspirationDef.defName, label, inspirationDef.baseDurationDays, cleanedReason);
            string text = BuildInspirationText(pawn, label, reason);

            DiaryEvent inspirationEvent = sink.AddSoloEvent(pawn, null, payload.DefName, label, text, instruction, gameContext);
            sink.QueueSolo(inspirationEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Resolves a user-facing InspirationDef label, falling back to defName if the label is blank.
        /// Moved verbatim from the old DiaryGameComponent.CleanInspirationLabel.
        /// </summary>
        private static string CleanInspirationLabel(InspirationDef inspirationDef)
        {
            if (inspirationDef == null)
            {
                return "unknown";
            }

            string label = DiaryLineCleaner.CleanLine(inspirationDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? inspirationDef.defName : label;
        }

        /// <summary>
        /// Builds the raw localized event text shown in the diary and fed to the model as what happened.
        /// Moved verbatim from the old DiaryGameComponent.BuildInspirationText.
        /// </summary>
        private static string BuildInspirationText(Pawn pawn, string label, string reason)
        {
            string text = "PawnDiary.Event.Inspiration".Translate(pawn.LabelShortCap, label).Resolve();
            string cleanReason = DiaryLineCleaner.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }
    }
}
