// Catalog Spec for Royalty persona-weapon lifecycle pages.
namespace PawnDiary.Capture
{
    /// <summary>Delegates persona lifecycle page policy to its detached payload.</summary>
    internal sealed class PersonaWeaponEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.PersonaWeapon;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return PersonaWeaponEventData.Decide(data as PersonaWeaponEventData, context);
        }
    }
}
