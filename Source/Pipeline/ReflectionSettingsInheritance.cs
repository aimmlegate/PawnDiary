// Pure save-compatibility policy for the Reflection-domain rows that split off the single generic
// `reflection` settings row. Until each new row is deliberately touched, an explicit choice made on
// the old row is still what the player meant, so it is inherited rather than silently reversed.
// Mirrors CounselSettingsInheritance / ConversionRitualSettingsInheritance; no Verse, Unity, or
// settings state is read here.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Decides the effective enable state for a reflection row that used to be covered by the broad
    /// `reflection` toggle. Day, quadrum and belief reflections each own a row now; before the split
    /// the only way to turn any of them off was the generic row, and that intent must survive.
    /// </summary>
    internal static class ReflectionSettingsInheritance
    {
        /// <summary>The pre-split row every reflection kind used to share.</summary>
        public const string LegacyGroupDefName = "reflection";

        // Rows that were carved out of LegacyGroupDefName and therefore inherit its saved choice.
        // The generic row itself is absent: it keeps owning the life-arc page directly, so it has
        // nothing to inherit from. Ordinal comparison matches how group defNames are keyed elsewhere.
        private static readonly string[] SplitGroupDefNames =
        {
            "dayreflection",
            "quadrumreflection",
            "reflectionBelief"
        };

        /// <summary>True when this group defName was split off the generic reflection row.</summary>
        public static bool IsSplitRow(string groupDefName)
        {
            if (string.IsNullOrEmpty(groupDefName))
            {
                return false;
            }

            for (int i = 0; i < SplitGroupDefNames.Length; i++)
            {
                if (string.Equals(SplitGroupDefNames[i], groupDefName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>New explicit intent wins, then legacy reflection intent, then the XML default.</summary>
        public static bool Enabled(bool? rowOverride, bool? legacyOverride, bool rowDefault)
        {
            if (rowOverride.HasValue) return rowOverride.Value;
            if (legacyOverride.HasValue) return legacyOverride.Value;
            return rowDefault;
        }

        /// <summary>
        /// True when a saved value for the new row is required to preserve player intent. A value equal
        /// to the XML default still matters when it deliberately overrides the opposite inherited legacy
        /// value — otherwise re-enabling one reflection kind would be erased as "redundant" and the
        /// disabled legacy choice would immediately win it back.
        /// </summary>
        public static bool ShouldStoreOverride(bool desiredValue, bool rowDefault, bool? legacyOverride)
        {
            return desiredValue != rowDefault
                || legacyOverride.HasValue && desiredValue != legacyOverride.Value;
        }
    }
}
