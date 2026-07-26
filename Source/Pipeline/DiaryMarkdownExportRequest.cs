// Plain snapshot of the diary reader state used by a player-facing Markdown export.
//
// The UI fills this contract with the selected year, the entry keys that survived its live filters,
// and localized header metadata. The filesystem adapter can then reproduce that exact view without
// receiving Unity controls or other live UI objects.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Captures one applied reader view. A null request retains the legacy whole-diary export behavior.
    /// </summary>
    internal sealed class DiaryMarkdownExportRequest
    {
        public int selectedYear;
        public readonly List<string> includedEntryKeys = new List<string>();
        public readonly List<DiaryMarkdownMetadata> metadata = new List<DiaryMarkdownMetadata>();
    }
}
