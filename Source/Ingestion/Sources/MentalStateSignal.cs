// Mental-state ingestion signal — the impure capture+emit half of the "pawn entered a mental state"
// source (MentalStateHandler.TryStartMentalState). Replaces the old DiaryGameComponent.RecordMentalState.
// A dual-shape source: a social fight ("SocialFighting") between two eligible pawns is a PAIR event
// (both POV entries, mirrored second call deduped by canonical pair key); every other break is a SOLO
// event from the breaking pawn's POV (deduped per pawn + defName). The dedup window therefore differs
// by shape (socialFight vs mentalBreak ticks).
//
// Pure pair-vs-solo decision, the IsSocialFightPair check, the dedup key, and the game-context format
// live in Source/Capture/Events/MentalStateEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one started mental state and emits it as a pairwise social-fight event or a solo
    /// mental-break event. Built by <see cref="MentalStateStartPatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class MentalStateSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly MentalStateDef stateDef;
        private readonly Pawn otherPawn;
        private readonly string reason;
        private readonly string otherPawnLabel;
        private readonly bool isPair;
        private readonly MentalStateEventData payload;

        public MentalStateSignal(Pawn pawn, MentalStateDef stateDef, Pawn otherPawn, string reason)
        {
            this.pawn = pawn;
            this.stateDef = stateDef;
            this.otherPawn = otherPawn;
            this.reason = reason;

            // GamePlaying first (before any Find.TickManager access), mirroring the old guard.
            if (!DiaryGameComponent.GamePlaying || pawn == null || stateDef == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            bool hasOtherPawn = otherPawn != null;
            string otherPawnId = hasOtherPawn ? otherPawn.GetUniqueLoadID() : null;
            otherPawnLabel = hasOtherPawn ? DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap) : null;

            payload = new MentalStateEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = stateDef.defName,
                OtherPawnId = otherPawnId,
                OtherPawnEligible = hasOtherPawn && DiaryGameComponent.IsDiaryEligible(otherPawn) && otherPawn != pawn,
                OtherPawnLabel = otherPawnLabel,
            };
            isPair = MentalStateEventData.IsSocialFightPair(payload);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.IsMentalStateEnabled(stateDef),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        // Social fights and solo breaks use different dedup windows.
        public override int DedupWindowTicks => isPair
            ? DiaryTuning.Current.socialFightDedupTicks
            : DiaryTuning.Current.mentalBreakDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            string label = stateDef.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForMentalState(stateDef);
            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            string cleanedReason = DiaryLineCleaner.CleanLine(reason);

            if (decision == CaptureDecision.GeneratePair)
            {
                string text = "PawnDiary.Event.SocialFight".Translate(pawn.LabelShortCap, otherPawn.LabelShortCap);
                string gameContext = MentalStateEventData.BuildPairGameContext(
                    stateDef.defName, cleanedLabel, cleanedReason);

                DiaryEvent fightEvent = sink.AddPairwiseEvent(pawn, otherPawn, stateDef.defName, label,
                    text, text, instruction, gameContext);
                sink.QueuePair(fightEvent);
                return;
            }

            string breakText = BuildMentalBreakText(pawn, label, otherPawn, reason);
            string breakContext = MentalStateEventData.BuildSoloGameContext(
                stateDef.defName, cleanedLabel, otherPawnLabel, cleanedReason);

            DiaryEvent breakEvent = sink.AddSoloEvent(pawn, otherPawn, stateDef.defName, label,
                breakText, instruction, breakContext);
            sink.QueueSolo(breakEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Assembles a human-readable fallback description for a mental break, including target and
        /// reason if available. Moved verbatim from the old DiaryGameComponent.BuildMentalBreakText.
        /// </summary>
        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = "PawnDiary.Event.MentalBreak".Translate(pawn.LabelShortCap, DiaryLineCleaner.CleanLine(label));
            if (otherPawn != null)
            {
                text += "PawnDiary.Event.DirectedAt".Translate(DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap)).Resolve();
            }

            string cleanReason = DiaryLineCleaner.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }
    }
}
