// Pure policy for resolving the effective writing-style rule from already-collected strings. Runtime
// code snapshots external/hediff/base/custom text on the main thread, then this helper picks the
// winner and records *why* so the UI can explain an inactive custom prompt. No RimWorld/Verse
// dependency, so it is covered by the pure test harness.
//
// Effective priority: External API override > Hediff override > Pawn custom prompt > Base style.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Which layer is supplying the effective writing-style rule. Used by the UI to explain why a
    /// pawn's saved custom prompt is not currently active.
    /// </summary>
    internal enum WritingStyleRuleSource
    {
        /// <summary>The pawn's selected base style Def.</summary>
        BaseStyle,

        /// <summary>A pawn-specific custom prompt authored by the player.</summary>
        PawnCustom,

        /// <summary>A temporary style forced by an active hediff.</summary>
        HediffOverride,

        /// <summary>An external integration override owned by an adapter sourceId.</summary>
        ExternalApiOverride
    }

    /// <summary>
    /// Plain snapshot of the four candidate writing-style rules plus the selected winner. Runtime
    /// adapters fill the candidates; the pure policy picks <see cref="source"/> and <see cref="rule"/>.
    /// </summary>
    internal sealed class WritingStyleResolution
    {
        public WritingStyleRuleSource source = WritingStyleRuleSource.BaseStyle;

        /// <summary>The final prompt-facing rule string. This is the only field generation needs.</summary>
        public string rule = string.Empty;

        // Base style Def candidates.
        public string baseStyleDefName = string.Empty;
        public string baseStyleLabel = string.Empty;
        public string baseStyleRule = string.Empty;

        // Pawn-specific custom prompt authored from the Diary tab.
        public string customRule = string.Empty;

        // External integration override candidates.
        public string externalSourceId = string.Empty;
        public string externalRule = string.Empty;

        // Hediff-driven override candidates.
        public string hediffStyleDefName = string.Empty;
        public string hediffStyleLabel = string.Empty;
        public string hediffRule = string.Empty;
    }

    /// <summary>
    /// Picks the effective writing-style rule from already-sanitized candidate strings. Pure: it does
    /// not touch live Pawn/Def/settings state, so it is fully testable.
    /// </summary>
    internal static class WritingStyleResolutionPolicy
    {
        /// <summary>
        /// Resolves a <see cref="WritingStyleResolution"/> from the four candidate rules. Each candidate
        /// is treated as absent when null/whitespace. The winner is chosen by the documented priority:
        /// External API override > Hediff override > Pawn custom > Base style.
        /// </summary>
        public static WritingStyleResolution Resolve(
            string baseStyleRule,
            string customRule,
            string hediffStyleDefName,
            string hediffStyleLabel,
            string hediffRule,
            string externalSourceId,
            string externalRule)
        {
            WritingStyleResolution resolution = new WritingStyleResolution
            {
                baseStyleRule = baseStyleRule ?? string.Empty,
                customRule = customRule ?? string.Empty,
                hediffStyleDefName = hediffStyleDefName ?? string.Empty,
                hediffStyleLabel = hediffStyleLabel ?? string.Empty,
                hediffRule = hediffRule ?? string.Empty,
                externalSourceId = externalSourceId ?? string.Empty,
                externalRule = externalRule ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(externalRule))
            {
                resolution.source = WritingStyleRuleSource.ExternalApiOverride;
                resolution.rule = externalRule;
                return resolution;
            }

            if (!string.IsNullOrWhiteSpace(hediffRule))
            {
                resolution.source = WritingStyleRuleSource.HediffOverride;
                resolution.rule = hediffRule;
                return resolution;
            }

            if (!string.IsNullOrWhiteSpace(customRule))
            {
                resolution.source = WritingStyleRuleSource.PawnCustom;
                resolution.rule = customRule;
                return resolution;
            }

            resolution.source = WritingStyleRuleSource.BaseStyle;
            resolution.rule = baseStyleRule ?? string.Empty;
            return resolution;
        }

        /// <summary>
        /// True when an active override (external API or hediff) is shadowing a non-empty custom
        /// prompt, so the UI can show "your custom prompt is temporarily inactive."
        /// </summary>
        public static bool CustomSuppressedByOverride(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(resolution.customRule)
                && (resolution.source == WritingStyleRuleSource.ExternalApiOverride
                    || resolution.source == WritingStyleRuleSource.HediffOverride);
        }
    }
}
