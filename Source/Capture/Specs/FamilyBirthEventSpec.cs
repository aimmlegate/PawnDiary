// Inert Phase-0 catalog Spec for canonical Biotech family births.
namespace PawnDiary.Capture
{
    internal class FamilyBirthEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.FamilyBirth;

        /// <summary>Delegates catalog policy to the typed inert family-birth payload.</summary>
        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return FamilyBirthEventData.Decide(data as FamilyBirthEventData, context);
        }
    }
}
