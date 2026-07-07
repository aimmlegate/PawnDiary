// Public read-only DTO for one automatic-capture "event filter" — the same per-interaction-group
// on/off toggle the player edits on Pawn Diary's settings "Events" tab. Returned by
// PawnDiaryApi.GetEventFilters so an adapter can see which event kinds Pawn Diary is capturing and,
// with SetEventFilterEnabled, flip a particular one using the exact same saved flag.
//
// Keep this class plain: fields only, no live RimWorld objects. `key` is the interaction-group
// defName; it is the identifier passed back to IsEventFilterEnabled / SetEventFilterEnabled.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One automatic-capture event filter (an interaction group toggle) exposed through the public API.
    /// </summary>
    public sealed class DiaryEventFilterSnapshot
    {
        /// <summary>Interaction-group defName. Stable save-data identifier; pass it to
        /// <see cref="PawnDiaryApi.IsEventFilterEnabled"/> / <see cref="PawnDiaryApi.SetEventFilterEnabled"/>.</summary>
        public string key = string.Empty;

        /// <summary>Localized group label as shown on the Events tab (falls back to the defName).</summary>
        public string label = string.Empty;

        /// <summary>Semantic event domain token (e.g. "Interaction", "Thought", "Tale", "Raid"), the
        /// same strings used by <c>DiaryEntryTitleQuery.domain</c>.</summary>
        public string domain = string.Empty;

        /// <summary>Whether Pawn Diary currently captures this event kind (the effective saved flag).</summary>
        public bool enabled;

        /// <summary>The XML-shipped default for this group, before any player/adapter override.</summary>
        public bool defaultEnabled;

        /// <summary>True when the effective value differs from the XML default (a saved override exists).</summary>
        public bool hasOverride;
    }
}
