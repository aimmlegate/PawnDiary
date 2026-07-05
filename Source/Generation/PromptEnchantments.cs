// Prompt enchantments: optional, one-shot live pawn context chosen right before a first-person
// prompt is queued. Most candidates are health/capacity facts; important events may also include
// DLC social-status facts. XML controls which candidates are eligible and how strongly each one is
// weighted.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Optional hediff-severity override for a prompt enchantment. XML authors name one of the
    /// fixed levels understood by the prompt-enchantment collector and may retune chance/weight.
    /// </summary>
    public class PromptEnchantmentSeverityTier
    {
        public string level;

        // Defaults below zero mean "inherit the parent Def's value".
        public float chance = -1f;
        public float frequency = -1f;
        public float weight = -1f;
        public float severity = -1f;
    }

    /// <summary>
    /// XML-configured weighting rule for live hediff prompt context. The prompt text itself comes
    /// from RimWorld's hediff label, body part, urgency, and strongest live impact cues.
    /// </summary>
    public class DiaryPromptEnchantmentDef : Def
    {
        // Chance/frequency controls whether a matching hediff appears on this prompt at all.
        // "frequency" is accepted as an alias; when set to 0 or greater it overrides chance.
        public float chance = 1f;
        public float frequency = -1f;

        // Selection controls once several matching hediffs pass their chance roll.
        public float weight = 1f;
        public float severity = 1f;

        // Visible hediffs matched by defName string. String matching keeps DLC/modded names safe:
        // absent defs simply never appear on a pawn and therefore never match.
        public List<string> hediffDefNames = new List<string>();
        public bool visibleOnly = true;
        public float minHediffSeverity = 0f;
        public List<PromptEnchantmentSeverityTier> hediffSeverityTiers = new List<PromptEnchantmentSeverityTier>();

        // Optional non-hediff source. "Capacity" lets XML add live pawn capacities such as
        // Consciousness into the same weighted random pool as hediffs. "RoyalTitle" and
        // "IdeologyRole" are DLC-safe context sources that only enter the pool for important events.
        public string source = "Hediff";
        public string capacityDefName;
        public float minCapacity = -1f;
        public float maxCapacity = -1f;

        // Optional model-facing text controls. Keys are Keyed translations; the matching *Text / label
        // fields are literal settings overrides that the in-game editor can write. Empty values use the
        // same generic health wording as hediff enchantments. descriptionOverrideKey lets XML replace
        // RimWorld's live HediffDef.description when the game text is too mechanical or vague for diary
        // prose; descriptionOverrideText lets settings override that with plain text.
        public string conditionKey;
        public string conditionLabel;
        public string intensityKey;
        public string intensityText;
        public string priorityKey;
        public string priorityText;
        public string descriptionOverrideKey;
        public string descriptionOverrideText;
        public List<string> cueKeys = new List<string>();
        public List<string> cueTexts = new List<string>();
    }

    /// <summary>
    /// Public facade for optional prompt enchantments. It gates settings/Def lookup, asks the impure
    /// collector for plain candidates, then supplies the final RimWorld random roll to the planner.
    /// </summary>
    public static class PromptEnchantments
    {
        /// <summary>
        /// Returns one live context prompt for this pawn, or empty when disabled/no match.
        /// </summary>
        public static string RuleFor(Pawn pawn, bool includeImportantEventContext = false,
            IList<PromptEnchantmentCandidate> extraCandidates = null,
            float normalCandidateWeightMultiplier = 1f,
            IList<string> suppressedHediffDefNames = null)
        {
            if (pawn == null || PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.enablePromptEnchantments)
            {
                return string.Empty;
            }

            PromptEnchantmentTuning tuning = DiaryTuning.PromptEnchantmentTuning;
            List<PromptEnchantmentCandidate> normalCandidates = new List<PromptEnchantmentCandidate>();
            List<DiaryPromptEnchantmentDef> defs = DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading;
            if (defs != null && defs.Count > 0)
            {
                normalCandidates = PromptEnchantmentCollector.Collect(
                    pawn,
                    defs,
                    includeImportantEventContext,
                    tuning);
            }

            List<PromptEnchantmentCandidate> candidates = PromptEnchantmentPlanner.PrepareCandidatesForBuild(
                normalCandidates,
                extraCandidates,
                normalCandidateWeightMultiplier,
                suppressedHediffDefNames);
            return PromptEnchantmentPlanner.Build(candidates, tuning, Rand.Range(0f, 1f));
        }
    }
}
