// Pure migration rules for the player Advanced-tab override store. Kept free of Verse/Unity so the
// intended cleanup is locked down by a standalone test (see tests/DiaryPipelineTests). TuningOverrideStore
// calls in from ExposeData on load; the logic lives here because it is a plain string/dictionary decision.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One-time cleanup for override keys whose editors were removed. When prompt overrides moved from
    /// Keyed translation keys (<c>*Key</c>/<c>cueKeys</c>) to literal text (<c>*Text</c>/<c>cueTexts</c>),
    /// the two stopped being interchangeable: a saved key value is a lookup token, not prose, so it
    /// cannot be carried into a literal-text field without printing the raw key. Rather than mis-migrate
    /// the value, we drop the orphaned entries so they stop lingering in the settings file and shadowing
    /// nothing. Harmless when absent (players who never ran the interim build that briefly exposed the
    /// key editors).
    /// </summary>
    internal static class TuningOverrideMigration
    {
        // Override keys are "defName.fieldName" (nested policy fields keep their batch./hediff. prefix),
        // so each removed field below is matched as a trailing ".<field name>". The leading dot anchors
        // the match to a whole segment, so e.g. ".priorityKey" never matches a retained ".promptPriorityText".
        private static readonly string[] RemovedFieldKeySuffixes =
        {
            ".conditionKey", ".intensityKey", ".priorityKey", ".descriptionOverrideKey", ".cueKeys",
            ".startTextKey", ".endTextKey", ".timeoutTextKey",
            ".promptPriorityKey", ".promptConditionKey", ".promptDescriptionKey", ".promptCueKeys",
            ".textKey",
            ".batch.labelKey", ".batch.briefKey", ".batch.headerKey", ".batch.fallbackKey", ".batch.instructionKey",
            ".hediff.appearedTextKey", ".hediff.progressedTextKey",
        };

        /// <summary>True when the override key targets a field whose editor was removed in the literal-text switch.</summary>
        public static bool IsRemovedFieldKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            for (int i = 0; i < RemovedFieldKeySuffixes.Length; i++)
            {
                if (key.EndsWith(RemovedFieldKeySuffixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes every orphaned override key from <paramref name="overrides"/> in place and returns how
        /// many were dropped. Idempotent: a second pass finds nothing. Null/empty input is a no-op.
        /// </summary>
        public static int PruneRemovedFieldKeys(IDictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0)
            {
                return 0;
            }

            // Collect first, then remove: mutating a dictionary while enumerating it throws.
            List<string> dead = null;
            foreach (KeyValuePair<string, string> pair in overrides)
            {
                if (!IsRemovedFieldKey(pair.Key))
                {
                    continue;
                }

                if (dead == null)
                {
                    dead = new List<string>();
                }

                dead.Add(pair.Key);
            }

            if (dead == null)
            {
                return 0;
            }

            for (int i = 0; i < dead.Count; i++)
            {
                overrides.Remove(dead[i]);
            }

            return dead.Count;
        }
    }
}
