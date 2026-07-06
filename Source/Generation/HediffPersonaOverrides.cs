// Runtime adapter for hediff-driven writing-style overrides. It reads live Pawn health state and XML
// Defs on the main thread, then delegates matching to HediffPersonaOverridePolicy's pure DTO logic.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Resolves temporary writing-style overrides caused by active pawn hediffs.
    /// </summary>
    internal static class HediffPersonaOverrides
    {
        /// <summary>
        /// Returns the prompt-ready writing-style rule. Effective priority is External API override
        /// &gt; Hediff override &gt; Pawn-specific custom prompt &gt; saved base style. The custom prompt
        /// keeps line breaks and is passed through verbatim; the other layers are one-line sanitized.
        /// </summary>
        public static string RuleFor(Pawn pawn, string fallbackPersonaDefName, string externalOverrideRule = null,
            string customWritingStyleRule = null)
        {
            return ResolveWritingStyle(
                pawn,
                fallbackPersonaDefName,
                externalOverrideRule,
                customWritingStyleRule,
                null).rule;
        }

        /// <summary>
        /// Builds the full <see cref="WritingStyleResolution"/> for a pawn: snapshots the external,
        /// hediff, base, and custom candidates from live Pawn/Def state on the main thread, then lets
        /// the pure <see cref="WritingStyleResolutionPolicy"/> pick the winner. Generation uses only
        /// <see cref="WritingStyleResolution.rule"/>; the UI uses the metadata to explain overrides.
        /// </summary>
        /// <param name="externalSourceId">
        /// The adapter sourceId owning the external override, if known. Generation does not need it;
        /// the UI shows it so the player knows which integration is supplying the style.
        /// </param>
        public static WritingStyleResolution ResolveWritingStyle(
            Pawn pawn,
            string fallbackPersonaDefName,
            string externalOverrideRule = null,
            string customWritingStyleRule = null,
            string externalSourceId = null)
        {
            // External API override: adapter-owned, one-line sanitized.
            string externalRule = ExternalWritingStyleOverrideText.CleanRule(externalOverrideRule);
            string externalSource = string.IsNullOrWhiteSpace(externalRule)
                ? string.Empty
                : ExternalWritingStyleOverrideText.CleanSourceId(externalSourceId);

            // Hediff override: resolved against active health state. May be empty.
            HediffPersonaOverrideSelection hediffSelection = SelectionFor(pawn);
            string hediffDefName = hediffSelection.personaDefName ?? string.Empty;
            DiaryPersonaDef hediffPersona = DiaryPersonas.ForDefName(hediffDefName);
            string hediffRule = hediffPersona == null
                ? string.Empty
                : DiaryPersonas.RuleFor(hediffPersona.defName);
            string hediffLabel = hediffPersona == null ? string.Empty : hediffPersona.label ?? string.Empty;
            // If the hediff-selected Def is somehow missing, treat the override as inactive so we fall
            // back to custom/base rather than emit an empty hediff rule.
            if (hediffPersona == null)
            {
                hediffDefName = string.Empty;
            }

            // Base style: the pawn's selected Def (falls back to default when blank/unknown).
            string baseDefName = string.IsNullOrWhiteSpace(fallbackPersonaDefName)
                ? DiaryPersonas.Default.defName
                : fallbackPersonaDefName;
            DiaryPersonaDef basePersona = DiaryPersonas.Resolve(baseDefName);
            string baseStyleDefName = basePersona == null ? string.Empty : basePersona.defName ?? string.Empty;
            string baseStyleLabel = basePersona == null ? string.Empty : basePersona.label ?? string.Empty;
            string baseStyleRule = basePersona == null ? string.Empty : DiaryPersonas.RuleFor(basePersona.defName);

            // Pawn-specific custom prompt: player-authored, multiline sanitized. The runtime adapter
            // passes the already-saved value; we re-sanitize defensively so stale or hand-edited saves
            // cannot inject control chars or oversized text into the prompt.
            string customRule = PlayerWritingStyleText.CleanRule(customWritingStyleRule);

            WritingStyleResolution resolution = WritingStyleResolutionPolicy.Resolve(
                baseStyleRule,
                customRule,
                hediffDefName,
                hediffLabel,
                hediffRule,
                externalSource,
                externalRule);
            // Carry the catalog metadata the UI needs to render the dialog.
            resolution.baseStyleDefName = baseStyleDefName;
            resolution.baseStyleLabel = baseStyleLabel;
            return resolution;
        }

        /// <summary>
        /// Returns the DiaryPersonaDef name forced by this pawn's active hediffs, or empty.
        /// </summary>
        public static string PersonaDefNameFor(Pawn pawn)
        {
            return SelectionFor(pawn).personaDefName;
        }

        /// <summary>
        /// Returns hediff defNames already represented by the active writing-style override. Prompt
        /// enchantments skip these so the final prompt does not repeat the same condition twice.
        /// </summary>
        public static List<string> SuppressedPromptHediffDefNamesFor(Pawn pawn)
        {
            return SelectionFor(pawn).matchedHediffDefNames;
        }

        private static HediffPersonaOverrideSelection SelectionFor(Pawn pawn)
        {
            List<DiaryHediffPersonaOverrideDef> defs =
                DefDatabase<DiaryHediffPersonaOverrideDef>.AllDefsListForReading;
            if (pawn == null || defs == null || defs.Count == 0)
            {
                return new HediffPersonaOverrideSelection();
            }

            HediffPersonaOverrideSelection selected = HediffPersonaOverridePolicy.SelectOverride(
                RulesFor(defs),
                FactsFor(pawn));
            return DiaryPersonas.ForDefName(selected.personaDefName) == null
                ? new HediffPersonaOverrideSelection()
                : selected;
        }

        private static List<HediffPersonaOverrideRule> RulesFor(
            List<DiaryHediffPersonaOverrideDef> defs)
        {
            List<HediffPersonaOverrideRule> rules = new List<HediffPersonaOverrideRule>();
            if (defs == null)
            {
                return rules;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryHediffPersonaOverrideDef def = defs[i];
                if (def != null && DiaryPersonas.ForDefName(def.personaDefName) != null)
                {
                    rules.Add(def.ToPolicyRule());
                }
            }

            return rules;
        }

        private static List<HediffPersonaOverrideFact> FactsFor(Pawn pawn)
        {
            List<HediffPersonaOverrideFact> facts = new List<HediffPersonaOverrideFact>();
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return facts;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null)
                {
                    continue;
                }

                facts.Add(new HediffPersonaOverrideFact
                {
                    defName = hediff.def?.defName ?? string.Empty,
                    label = DiaryLineCleaner.CleanLine(hediff.Label),
                    severity = hediff.Severity,
                    visible = hediff.Visible
                });
            }

            return facts;
        }
    }
}
