// Pure event-prompt key resolution. Runtime adapters use this to decide which XML
// DiaryEventPromptDef keys to try for a captured event before falling back to the broad domain key.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Builds the ordered prompt-policy key list for one event. The pure layer does not know which
    /// XML Defs are loaded; it only supplies the candidate keys in priority order.
    /// </summary>
    internal static class DiaryEventPromptKeys
    {
        public static List<string> CandidateKeys(DiaryEventPayload payload, string groupDefName,
            string classifierKey, string fallbackEventKey, bool preferGroup = false)
        {
            List<string> keys = new List<string>();
            if (preferGroup)
            {
                // Composite classifiers (notably rituals) know more than the raw source Def name.
                // "Conversion", for example, is both a ritual Def and the case-insensitive key of
                // the ordinary social-conversion prompt. Let the exact ritual group win that collision.
                AddUnique(keys, groupDefName);
                AddUnique(keys, payload?.defName);
            }
            else
            {
                AddUnique(keys, payload?.defName);
                AddUnique(keys, groupDefName);
            }
            AddUnique(keys, classifierKey);
            AddUnique(keys, fallbackEventKey);
            return keys;
        }

        private static void AddUnique(List<string> keys, string key)
        {
            if (keys == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string trimmed = key.Trim();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            keys.Add(trimmed);
        }
    }
}
