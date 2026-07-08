// Pure policy for resolving the effective PSYCHOTYPE rule from already-collected strings. It mirrors
// WritingStyleResolutionPolicy but has one fewer layer: there is no hediff psychotype override in v1
// (hediff *style* overrides already cover altered-state prose). Runtime code snapshots the external/
// custom/base text on the main thread, then this helper picks the winner and records *why* so the UI
// can explain an inactive custom rule. No RimWorld/Verse dependency, so it is covered by the pure
// test harness.
//
// Effective priority: External API override > Pawn custom rule > Base psychotype def.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Which layer is supplying the effective psychotype rule. Used by the UI to explain why a pawn's
    /// saved custom psychotype rule is not currently active.
    /// </summary>
    internal enum PsychotypeRuleSource
    {
        /// <summary>The pawn's selected base psychotype Def.</summary>
        BaseType,

        /// <summary>A pawn-specific custom psychotype rule authored by the player.</summary>
        PawnCustom,

        /// <summary>An external integration override owned by an adapter sourceId.</summary>
        ExternalApiOverride
    }

    /// <summary>
    /// Plain snapshot of the three candidate psychotype rules plus the selected winner. Runtime adapters
    /// fill the candidates; the pure policy picks <see cref="source"/> and <see cref="rule"/>.
    /// </summary>
    internal sealed class PsychotypeResolution
    {
        public PsychotypeRuleSource source = PsychotypeRuleSource.BaseType;

        /// <summary>The final prompt-facing rule string. This is the only field generation needs.</summary>
        public string rule = string.Empty;

        // Base psychotype Def candidates.
        public string baseTypeDefName = string.Empty;
        public string baseTypeLabel = string.Empty;
        public string baseTypeRule = string.Empty;

        // Pawn-specific custom rule authored from the Diary tab.
        public string customRule = string.Empty;

        // External integration override candidates.
        public string externalSourceId = string.Empty;
        public string externalRule = string.Empty;
    }

    /// <summary>
    /// Picks the effective psychotype rule from already-sanitized candidate strings. Pure: it does not
    /// touch live Pawn/Def/settings state, so it is fully testable.
    /// </summary>
    internal static class PsychotypeResolutionPolicy
    {
        /// <summary>
        /// Resolves a <see cref="PsychotypeResolution"/> from the three candidate rules. Each candidate is
        /// treated as absent when null/whitespace. The winner is chosen by the documented priority:
        /// External API override > Pawn custom > Base type.
        /// </summary>
        public static PsychotypeResolution Resolve(
            string baseTypeRule,
            string customRule,
            string externalSourceId,
            string externalRule)
        {
            PsychotypeResolution resolution = new PsychotypeResolution
            {
                baseTypeRule = baseTypeRule ?? string.Empty,
                customRule = customRule ?? string.Empty,
                externalSourceId = externalSourceId ?? string.Empty,
                externalRule = externalRule ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(externalRule))
            {
                resolution.source = PsychotypeRuleSource.ExternalApiOverride;
                resolution.rule = externalRule;
                return resolution;
            }

            if (!string.IsNullOrWhiteSpace(customRule))
            {
                resolution.source = PsychotypeRuleSource.PawnCustom;
                resolution.rule = customRule;
                return resolution;
            }

            resolution.source = PsychotypeRuleSource.BaseType;
            resolution.rule = baseTypeRule ?? string.Empty;
            return resolution;
        }

        /// <summary>
        /// True when an active external override is shadowing a non-empty custom rule, so the UI can
        /// show "your custom psychotype is temporarily inactive."
        /// </summary>
        public static bool CustomSuppressedByOverride(PsychotypeResolution resolution)
        {
            if (resolution == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(resolution.customRule)
                && resolution.source == PsychotypeRuleSource.ExternalApiOverride;
        }
    }
}
