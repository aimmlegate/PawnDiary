// Detached editor snapshot for one exact diary page. The immediate-mode UI edits its own buffers and
// submits the stable identity back to DiaryGameComponent only when the player presses Save, so drawing
// the dialog never mutates persisted state. ManualDiaryEntryFacts owns the stable saved context marker
// used to distinguish player-created pages from ordinary events that were merely edited later.
using System;

namespace PawnDiary
{
    /// <summary>Stable schema facts shared by creation, display classification, and UI actions.</summary>
    internal static class ManualDiaryEntryFacts
    {
        public const string GameContext = "manual_entry=true";

        public static bool IsPlayerCreated(string gameContext)
        {
            return string.Equals(
                DiaryContextFields.Value(gameContext, "manual_entry"),
                "true",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Immutable player-editable projection of one hot or compact archived diary POV.
    /// </summary>
    internal sealed class ManualDiaryEntrySnapshot
    {
        public readonly string PawnId;
        public readonly string EventId;
        public readonly string PovRole;
        public readonly string Body;
        public readonly string Title;
        /// <summary>Exact saved key. Blank is preserved for an untouched legacy/generated page.</summary>
        public readonly string EntryTypeKey;
        /// <summary>True for arrival/death boundary pages whose category cannot be changed.</summary>
        public readonly bool EntryTypeLocked;
        public readonly string EntryTypeLabel;
        public readonly string EntryTypeDescription;
        public readonly bool Archived;
        public readonly bool PlayerCreated;

        public ManualDiaryEntrySnapshot(
            string pawnId,
            string eventId,
            string povRole,
            string body,
            string title,
            string entryTypeKey,
            bool entryTypeLocked,
            string entryTypeLabel,
            string entryTypeDescription,
            bool archived,
            bool playerCreated)
        {
            PawnId = pawnId ?? string.Empty;
            EventId = eventId ?? string.Empty;
            PovRole = povRole ?? string.Empty;
            Body = body ?? string.Empty;
            Title = title ?? string.Empty;
            EntryTypeKey = entryTypeKey ?? string.Empty;
            EntryTypeLocked = entryTypeLocked;
            EntryTypeLabel = entryTypeLabel ?? string.Empty;
            EntryTypeDescription = entryTypeDescription ?? string.Empty;
            Archived = archived;
            PlayerCreated = playerCreated;
        }

        /// <summary>Compatibility constructor for callers compiled before entry categories existed.</summary>
        public ManualDiaryEntrySnapshot(
            string pawnId,
            string eventId,
            string povRole,
            string body,
            string title,
            bool archived,
            bool playerCreated)
            : this(pawnId, eventId, povRole, body, title, string.Empty, false,
                string.Empty, string.Empty, archived, playerCreated)
        {
        }
    }
}
