// Player overrides for XML Def tuning/prompt-policy fields shown in the Advanced settings tab. This is the
// typed persistence twin of PromptOverrideDictionary: where that class overrides prompt *text*, this
// one overrides Def-backed parameters and prompt policy rows that ship in XML. Values are stored as
// invariant-culture strings (one cell per field) and parsed back into the right runtime type by
// AdvancedFieldDescriptor at apply time.
//
// How a setting takes effect at runtime: the Advanced tab does NOT re-route every Def read through a
// helper. Instead AdvancedFieldDescriptor writes the override straight into the live Def instance's
// field (reflection), so every existing `DiaryTuning.Current.someField` / `def.someField` reader picks
// the new value up with no call-site changes. The pristine XML value is snapshotted once (before any
// override is applied) so Reset can restore it. See AdvancedFieldCatalog.EnsureApplied.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Persists player overrides for Advanced-tab Def fields. Keys are
    /// <c>"defName.fieldName"</c> (for example <c>"Diary_Tuning.socialFightDedupTicks"</c>); values
    /// are invariant-culture strings. Mirrors the Scribe shape of PromptOverrideDictionary.
    /// </summary>
    public class TuningOverrideStore
    {
        private Dictionary<string, string> overrides;
        private readonly string scribeKey;

        // Scratch parallel lists Scribe_Collections needs to (de)serialize a Dictionary on Unity Mono.
        private List<string> keys;
        private List<string> values;

        public TuningOverrideStore(string scribeKey)
        {
            this.scribeKey = scribeKey ?? string.Empty;
        }

        /// <summary>True when a non-blank override is stored for the key.</summary>
        public bool HasOverride(string key)
        {
            if (overrides == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string value;
            return overrides.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        /// <summary>Returns the stored invariant string, or empty when no override is stored.</summary>
        public bool TryGet(string key, out string value)
        {
            value = string.Empty;
            return overrides != null && overrides.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        /// <summary>Stores (or replaces) one override. A blank value clears it.</summary>
        public void Set(string key, string invariantValue)
        {
            Ensure();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (string.IsNullOrEmpty(invariantValue))
            {
                overrides.Remove(key);
                return;
            }

            overrides[key] = invariantValue;
        }

        /// <summary>Clears one key so the XML default supplies the value again.</summary>
        public void Clear(string key)
        {
            Ensure();
            if (!string.IsNullOrEmpty(key))
            {
                overrides.Remove(key);
            }
        }

        /// <summary>Clears every override. Callers are expected to restore Def fields from snapshot.</summary>
        public void ClearAll()
        {
            Ensure();
            overrides.Clear();
        }

        /// <summary>Number of non-blank overrides stored.</summary>
        public int Count
        {
            get
            {
                Ensure();
                int count = 0;
                foreach (KeyValuePair<string, string> pair in overrides)
                {
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Reads/writes the override map as part of PawnDiarySettings.ExposeData.</summary>
        public void ExposeData()
        {
            Ensure();
            Scribe_Collections.Look(ref overrides, scribeKey, LookMode.Value, LookMode.Value, ref keys, ref values);
        }

        private void Ensure()
        {
            if (overrides == null)
            {
                overrides = new Dictionary<string, string>();
            }
        }
    }
}
