// Public read-only DTO for the global automatic-event frequency setup. The selected preset belongs
// here once, rather than being repeated on every DiaryEventFilterSnapshot row. The existing
// PawnDiaryApi.GetEventFilters list remains available for API-v8 adapters; API-v9 callers can request
// this atomic setup snapshot when they need both the global preset and the per-group effective rows.
//
// Keep this class plain: fields and detached DTOs only, with no live RimWorld/Verse objects.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>The selected global frequency preset and current automatic-event filter rows.</summary>
    public sealed class DiaryEventFrequencySettingsSnapshot
    {
        /// <summary>
        /// Stable <c>DiaryFrequencyPresetDef.defName</c> selected in global settings. A nonblank token
        /// remains here when its third-party Def is temporarily unavailable; row multipliers then use
        /// Pawn Diary's safe Standard fallback until that provider returns.
        /// </summary>
        public string selectedPresetDefName = string.Empty;

        /// <summary>
        /// Localized selected-preset label for display, or the preserved Def token when its provider is
        /// unavailable. Never use this as an identifier.
        /// </summary>
        public string selectedPresetLabel = string.Empty;

        /// <summary>True when at least one known settings-visible group has a saved frequency override
        /// that differs from the selected preset.</summary>
        public bool hasCustomOverrides;

        /// <summary>Current settings-visible event groups in the same order as the Events tab and
        /// <see cref="PawnDiaryApi.GetEventFilters"/>. Never null and detached from live settings.</summary>
        public List<DiaryEventFilterSnapshot> filters = new List<DiaryEventFilterSnapshot>();
    }
}
