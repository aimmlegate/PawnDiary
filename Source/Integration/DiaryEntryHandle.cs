// Public opaque handle for one diary entry POV created through the integration API.
// Keep this class plain: fields only, strings only, no live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Stable handle for one diary event from one point of view. Store the fields as opaque values and
    /// pass them back to <see cref="PawnDiaryApi.GetEntryStatus(DiaryEntryHandle)"/> or
    /// <see cref="PawnDiaryApi.GetEntrySnapshot(DiaryEntryHandle)"/>.
    /// </summary>
    public sealed class DiaryEntryHandle
    {
        /// <summary>Stable event id backing this entry.</summary>
        public string eventId = string.Empty;

        /// <summary>Point-of-view role for this entry: initiator, recipient, or neutral.</summary>
        public string povRole = string.Empty;

        /// <summary>RimWorld unique load id for the pawn whose POV this handle represents.</summary>
        public string pawnId = string.Empty;

        /// <summary>Stable UI/cache key used by Pawn Diary for this event+POV pair.</summary>
        public string entryKey = string.Empty;
    }
}
