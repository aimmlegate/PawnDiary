// Inspirations — the InspirationHandler.TryStartInspiration hook's diary flow. When RimWorld
// accepts an inspiration for a pawn, this records a solo DiaryEvent from that pawn's point of view.
// The event carries an "inspiration=" game-context marker so the Diary tab and prompt policy classify
// it into the Inspiration domain rather than treating it as a social interaction.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Globalization;
using System.Text;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records one newly-started pawn inspiration as a solo diary event.
        /// </summary>
        public void RecordInspiration(Pawn pawn, InspirationDef inspirationDef, string reason)
        {
            if (!CanRecordGameplayEventNow() || pawn == null || inspirationDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn) || !PawnDiaryMod.Settings.IsInspirationEnabled(inspirationDef))
            {
                return;
            }

            string inspirationDefName = inspirationDef.defName;
            string label = CleanInspirationLabel(inspirationDef);
            string instruction = PawnDiaryMod.Settings.InstructionForInspiration(inspirationDef);
            string gameContext = BuildInspirationGameContext(inspirationDef, label, reason);
            string text = BuildInspirationText(pawn, label, reason);

            DiaryEvent inspirationEvent = AddSoloEvent(pawn, null, inspirationDefName, label, text, instruction, gameContext);
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

        /// <summary>
        /// Creates a compact metadata string for inspiration-sourced diary events. The leading
        /// inspiration= marker is stable and lets display code recover the correct group domain.
        /// </summary>
        private static string BuildInspirationGameContext(InspirationDef inspirationDef, string label, string reason)
        {
            StringBuilder context = new StringBuilder();
            context.Append("inspiration=").Append(inspirationDef.defName);
            context.Append("; label=").Append(DiaryContextBuilder.CleanLine(label));
            context.Append("; duration_days=")
                .Append(inspirationDef.baseDurationDays.ToString("F1", CultureInfo.InvariantCulture));

            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                context.Append("; reason=").Append(cleanReason);
            }

            return context.ToString();
        }
    }
}
