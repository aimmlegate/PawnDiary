// Adapter-owned, plain snapshots for Pawn Diary API-v9 event-frequency settings.
//
// These types deliberately do not inherit from or mention the core v9 DTOs. The example adapter
// can therefore be loaded beside Pawn Diary API v8: optional v9 objects are copied into this shape
// by FrequencyApiV9Shim only after the loaded-version guard succeeds.
//
// New to C#? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiaryExampleAdapter
{
    /// <summary>A detached copy of the selected frequency preset and settings-visible event rows.</summary>
    internal sealed class AdapterEventFrequencySettingsSnapshot
    {
        /// <summary>Stable preset Def name selected in Pawn Diary's global settings.</summary>
        public string selectedPresetDefName = string.Empty;

        /// <summary>Localized label supplied by the loaded Pawn Diary build.</summary>
        public string selectedPresetLabel = string.Empty;

        /// <summary>Whether at least one event row overrides its selected-preset multiplier.</summary>
        public bool hasCustomOverrides;

        /// <summary>Detached event rows in the same order as Pawn Diary's Events tab.</summary>
        public List<AdapterEventFrequencyFilterSnapshot> filters =
            new List<AdapterEventFrequencyFilterSnapshot>();
    }

    /// <summary>Adapter-owned copy of one API-v9 event filter and its frequency values.</summary>
    internal sealed class AdapterEventFrequencyFilterSnapshot
    {
        public string key = string.Empty;
        public string label = string.Empty;
        public string domain = string.Empty;
        public bool enabled;
        public bool defaultEnabled;
        public bool hasOverride;
        public string frequencyTier = string.Empty;
        public float presetFrequencyMultiplier;
        public float effectiveFrequencyMultiplier;
        public bool hasFrequencyOverride;
    }
}
