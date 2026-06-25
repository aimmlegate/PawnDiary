// Reusable per-key prompt-override store, extracted from PawnDiarySettings. PawnDiarySettings keeps
// two of these — one for event-source prompts and one for event-source enhancements — so the
// lookup/normalize/remove plumbing lives in exactly one place instead of being duplicated per
// dictionary. Each instance owns its own Scribe key (passed to the constructor) and writes its
// Dictionary<string,string> at the settings node level via Scribe_Collections, so existing save
// files keep loading unchanged. See AGENTS.md ("IExposable") for the save model primer.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// A saved map of eventType -> override text used to layer player edits on top of XML prompt
    /// defaults. Blank (or default-matching) values are never stored, so "no entry" always means
    /// "use the XML default". Implements <see cref="IExposable"/> so RimWorld can save/load it.
    /// </summary>
    public class PromptOverrideDictionary : IExposable
    {
        // The single Scribe key this instance reads/writes (e.g. "eventPromptOverrides").
        private readonly string scribeKey;
        // The live override map. Keys are trimmed event types; values are non-blank override text.
        private Dictionary<string, string> overrides = new Dictionary<string, string>();
        // Scratch parallel lists Scribe_Collections needs because Unity cannot serialize Dictionary.
        private List<string> keys;
        private List<string> values;

        public PromptOverrideDictionary(string scribeKey)
        {
            this.scribeKey = scribeKey ?? string.Empty;
        }

        /// <summary>Returns the override text, or the XML default when no override is stored.</summary>
        public string Effective(string eventType, string xmlDefault)
        {
            return OverrideOrDefault(OverrideFor(eventType), xmlDefault);
        }

        /// <summary>Stores or clears the override for one key. A blank/default value clears it.</summary>
        public void Set(string eventType, string value, string xmlDefault)
        {
            Ensure();
            string key = KeyOf(eventType);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Remove(key);
            string normalized = NormalizeValue(value, xmlDefault);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                overrides[key] = normalized;
            }
        }

        /// <summary>Clears one key so XML supplies the text again.</summary>
        public void Reset(string eventType)
        {
            Ensure();
            Remove(KeyOf(eventType));
        }

        /// <summary>True when a non-blank override is stored for the key.</summary>
        public bool HasOverride(string eventType)
        {
            return !string.IsNullOrWhiteSpace(OverrideFor(eventType));
        }

        /// <summary>Adds non-blank override keys to the set (used to union across dictionaries).</summary>
        public void AddKeysTo(HashSet<string> bucket)
        {
            Ensure();
            if (bucket == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    bucket.Add(pair.Key.Trim());
                }
            }
        }

        /// <summary>Clears every override.</summary>
        public void Clear()
        {
            Ensure();
            overrides.Clear();
        }

        /// <summary>Drops blank-key/blank-value rows left by hand-edited or partial saves.</summary>
        public void Normalize()
        {
            Ensure();
            List<string> keysToRemove = null;
            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    if (keysToRemove == null)
                    {
                        keysToRemove = new List<string>();
                    }

                    keysToRemove.Add(pair.Key);
                }
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                overrides.Remove(keysToRemove[i]);
            }
        }

        // Reads/writes the override map on save and load. This is called from
        // PawnDiarySettings.ExposeData while Scribe's cur parent node is still the settings node,
        // so <scribeKey> is written at the same level as before extraction — existing saves keep
        // loading unchanged.
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

        // Case-insensitive lookup that matches the old settings plumbing: an exact-key hit first,
        // then a fall-back scan so hand-edited saves with differently-cased keys still resolve.
        private string OverrideFor(string eventType)
        {
            Ensure();
            string key = KeyOf(eventType);
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string value;
            if (overrides.TryGetValue(key, out value))
            {
                return value ?? string.Empty;
            }

            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        // Removes every entry matching the key case-insensitively (defensive against duplicate
        // differently-cased keys in hand-edited saves).
        private void Remove(string key)
        {
            Ensure();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            List<string> keysToRemove = null;
            foreach (string existingKey in overrides.Keys)
            {
                if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (keysToRemove == null)
                    {
                        keysToRemove = new List<string>();
                    }

                    keysToRemove.Add(existingKey);
                }
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                overrides.Remove(keysToRemove[i]);
            }
        }

        private static string KeyOf(string eventType)
        {
            return (eventType ?? string.Empty).Trim();
        }

        private static string OverrideOrDefault(string overrideText, string xmlDefault)
        {
            return string.IsNullOrWhiteSpace(overrideText) ? xmlDefault ?? string.Empty : overrideText;
        }

        // Blank or XML-matching text collapses to empty so "use the default" is never stored.
        private static string NormalizeValue(string value, string xmlDefault)
        {
            string text = value ?? string.Empty;
            string defaultValue = xmlDefault ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, defaultValue, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return text;
        }
    }
}
