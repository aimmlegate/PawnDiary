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
    /// cannot be carried into a literal-text field without printing the raw key. The prompt-enchantment
    /// <c>frequency</c> XML alias was also removed from Advanced settings because <c>chance</c> is the
    /// canonical editable control. The signal-mirror <c>Diary_Tuning.*</c> rows (thought/ambient/
    /// progression/work knobs) were removed because <see cref="DiarySignalPolicies"/> reads the policy
    /// def first, masking them. Rather than mis-migrate these values, we drop the orphaned entries so
    /// they stop lingering in the settings file and shadowing nothing. Harmless when absent (players who
    /// never ran an interim build that exposed the removed editors).
    /// </summary>
    internal static class TuningOverrideMigration
    {
        // Full override keys ("defName.fieldName") for editors removed because the field's runtime value
        // is owned elsewhere. These are matched in FULL, not by suffix: each dead Diary_Tuning.* field
        // shares its trailing field name with a LIVE DiarySignalPolicy_*.<field> row (most sharply
        // thoughtProgressionRules, whose name is identical on both defs), so a suffix match would wrongly
        // prune the live signal override too. See AdvancedFieldCatalog signal-policy section.
        private static readonly string[] RemovedExactKeys =
        {
            "Diary_Tuning.thoughtDedupTicks",
            "Diary_Tuning.thoughtMinMoodOffset",
            "Diary_Tuning.thoughtEatingMinMoodOffset",
            "Diary_Tuning.thoughtIgnoreTokens",
            "Diary_Tuning.thoughtBypassThresholdTokens",
            "Diary_Tuning.thoughtEatingTokens",
            "Diary_Tuning.thoughtAmbientTokens",
            "Diary_Tuning.thoughtAmbientWindowTicks",
            "Diary_Tuning.thoughtAmbientMinEventsToWrite",
            "Diary_Tuning.thoughtAmbientMaxSampleLines",
            "Diary_Tuning.thoughtProgressionScanIntervalTicks",
            "Diary_Tuning.thoughtProgressionDedupTicks",
            "Diary_Tuning.thoughtProgressionRules",
            "Diary_Tuning.progressionScanIntervalTicks",
            "Diary_Tuning.workScanIntervalTicks",
            "Diary_Tuning.workBaseChance",
            "Diary_Tuning.workSameTypeCooldownTicks",
            "Diary_Tuning.workRecentDifferentTypeMultiplier",
            "Diary_Tuning.workPassionChanceMultiplier",
            "Diary_Tuning.workNegativeChanceMultiplier",
            "Diary_Tuning.workDarkStudyChanceMultiplier",
            "Diary_Tuning.workLowSkillThreshold",
        };
        // Override keys are "defName.fieldName" (nested policy fields keep their batch./hediff. prefix),
        // so each removed field below is matched as a trailing ".<field name>". The leading dot anchors
        // the match to a whole segment, so e.g. ".priorityKey" never matches a retained ".promptPriorityText".
        private static readonly string[] RemovedFieldKeySuffixes =
        {
            ".conditionKey", ".intensityKey", ".priorityKey", ".descriptionOverrideKey", ".cueKeys",
            ".frequency",
            ".startTextKey", ".endTextKey", ".timeoutTextKey",
            ".promptPriorityKey", ".promptConditionKey", ".promptDescriptionKey", ".promptCueKeys",
            ".textKey",
            ".batch.labelKey", ".batch.briefKey", ".batch.headerKey", ".batch.fallbackKey", ".batch.instructionKey",
            ".hediff.appearedTextKey", ".hediff.progressedTextKey",
        };

        /// <summary>True when the override key targets a field whose Advanced editor was removed.</summary>
        public static bool IsRemovedFieldKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            // Full-key matches first (signal-mirror tuning rows): these must NOT be reachable through
            // IsRemovedFieldName, whose "." + fieldName probe can never equal a "defName.fieldName" key,
            // so the shared field name (thoughtProgressionRules) stays editable on the live signal def.
            for (int i = 0; i < RemovedExactKeys.Length; i++)
            {
                if (string.Equals(key, RemovedExactKeys[i], StringComparison.Ordinal))
                {
                    return true;
                }
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
        /// True when a catalog field name is one of the removed raw translation-key fields. Nested
        /// policy names such as <c>batch.labelKey</c> use the same dotted shape as override keys.
        /// </summary>
        public static bool IsRemovedFieldName(string fieldName)
        {
            return !string.IsNullOrEmpty(fieldName)
                && IsRemovedFieldKey("." + fieldName);
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
