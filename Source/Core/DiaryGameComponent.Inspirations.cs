// Inspirations — the InspirationHandler.TryStartInspiration hook's diary flow, now built on the
// Event Catalog pattern (see Source/Capture/). When RimWorld accepts an inspiration for a pawn, this
// records a solo DiaryEvent from that pawn's point of view. The event carries an "inspiration="
// game-context marker so the Diary tab and prompt policy classify it into the Inspiration domain
// rather than treating it as a social interaction.
//
// This file now contains only the IMPURE half: snapshot live Pawn/InspirationDef facts into
// InspirationEventData + CaptureContext, ask DiaryEventCatalog for the pure decision, then perform
// the impure side-effects (event creation, LLM queue). The pure decision (eligibility + user toggle)
// lives in Source/Capture/Events/InspirationEventData.cs.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records one newly-started pawn inspiration as a solo diary event. Delegates the
        /// "should we?" decision to InspirationEventData.Decide (pure) and keeps the impure
        /// side-effects (label/text/game-context build, event creation, LLM queue) here.
        /// </summary>
        public void RecordInspiration(Pawn pawn, InspirationDef inspirationDef, string reason)
        {
            if (!CanRecordGameplayEventNow() || pawn == null || inspirationDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            // Snapshot live facts. Inspiration has no token/threshold policy, so the payload is small.
            InspirationEventData data = new InspirationEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = inspirationDef.defName,
                DurationDays = inspirationDef.baseDurationDays,
                Reason = reason,
            };

            CaptureContext ctx = BuildCaptureContext(
                eligible: IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings.IsInspirationEnabled(inspirationDef),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Inspiration);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision == CaptureDecision.Drop)
            {
                return;
            }

            // Impure build: inspiration has no dedup today (matches pre-refactor behavior); jump
            // straight to event assembly.
            string label = CleanInspirationLabel(inspirationDef);
            string instruction = PawnDiaryMod.Settings.InstructionForInspiration(inspirationDef);
            string cleanedReason = DiaryContextBuilder.CleanLine(reason);
            string gameContext = InspirationEventData.BuildGameContext(
                inspirationDef.defName, label, inspirationDef.baseDurationDays, cleanedReason);
            string text = BuildInspirationText(pawn, label, reason);

            DiaryEvent inspirationEvent = AddSoloEvent(pawn, null, data.DefName, label, text, instruction, gameContext);
            QueueLlmRewrite(inspirationEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Resolves a user-facing InspirationDef label, falling back to defName if the label is blank.
        /// </summary>
        private static string CleanInspirationLabel(InspirationDef inspirationDef)
        {
            if (inspirationDef == null)
            {
                return "unknown";
            }

            string label = DiaryContextBuilder.CleanLine(inspirationDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? inspirationDef.defName : label;
        }

        /// <summary>
        /// Builds the raw localized event text shown in the diary and fed to the model as what happened.
        /// </summary>
        private static string BuildInspirationText(Pawn pawn, string label, string reason)
        {
            string text = "PawnDiary.Event.Inspiration".Translate(pawn.LabelShortCap, label).Resolve();
            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }
    }
}
