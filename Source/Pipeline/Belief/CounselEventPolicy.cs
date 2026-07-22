// Pure exact-policy seam for Ideology's Counsel ability. Vanilla applies a mood thought and then
// emits one configured success/failure PlayLog interaction. XML owns those exact names and result
// tokens; this helper appends context only, without invoking doctrine resolution or reading Pawn,
// Ideo, Thought, Verse, or Unity state.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Pure exact rule matching and context formatting for Counsel.</summary>
    internal static class CounselEventPolicy
    {
        public const string GroupDefName = "counsel";

        /// <summary>
        /// Returns the XML row for one exact installed interaction under the Counsel group. Ordinal
        /// matching keeps case variants and similarly named modded events silent; every gate fails closed.
        /// </summary>
        public static CounselEventRule RuleFor(
            string interactionDefName,
            string effectiveGroupDefName,
            bool ideologyActive,
            bool policyEnabled,
            IReadOnlyList<CounselEventRule> rules)
        {
            if (!ideologyActive || !policyEnabled || rules == null
                || !string.Equals(effectiveGroupDefName, GroupDefName, StringComparison.Ordinal))
                return null;

            for (int i = 0; i < rules.Count; i++)
            {
                CounselEventRule row = rules[i];
                if (row != null
                    && string.Equals(row.sourceDefName, interactionDefName, StringComparison.Ordinal)
                    && string.Equals(row.downstreamGroupDefName, effectiveGroupDefName, StringComparison.Ordinal)
                    && SafeToken(row.resultToken) && SafeToken(row.moodEffectToken))
                    return row;
            }

            return null;
        }

        /// <summary>
        /// Appends the exact mood outcome once. It never emits conversion, certainty, or ideology facts.
        /// </summary>
        public static string AppendGameContext(string gameContext, CounselEventRule rule)
        {
            string current = gameContext ?? string.Empty;
            if (rule == null || !SafeToken(rule.resultToken) || !SafeToken(rule.moodEffectToken)
                || current.IndexOf("counsel_result=", StringComparison.Ordinal) >= 0)
                return current;

            string marker = "counsel_result=" + rule.resultToken
                + "; counsel_mood_effect=" + rule.moodEffectToken;
            string prefix = current.Trim().TrimEnd(';');
            return prefix.Length == 0 ? marker : prefix + "; " + marker;
        }

        private static bool SafeToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }
    }

    /// <summary>Pure save-compatibility policy for the new Counsel group setting.</summary>
    internal static class CounselSettingsInheritance
    {
        /// <summary>New explicit intent wins, then legacy conversion intent, then the XML default.</summary>
        public static bool Enabled(bool? counselOverride, bool? conversionOverride, bool counselDefault)
        {
            if (counselOverride.HasValue) return counselOverride.Value;
            if (conversionOverride.HasValue) return conversionOverride.Value;
            return counselDefault;
        }

        /// <summary>
        /// True when a saved Counsel value is required to preserve player intent. A value equal to the
        /// XML default still matters when it deliberately overrides the opposite inherited legacy value.
        /// </summary>
        public static bool ShouldStoreOverride(
            bool desiredValue,
            bool counselDefault,
            bool? conversionOverride)
        {
            return desiredValue != counselDefault
                || conversionOverride.HasValue && desiredValue != conversionOverride.Value;
        }
    }
}
