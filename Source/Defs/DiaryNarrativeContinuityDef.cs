// XML boundary for the Narrative Continuity Layer. RimWorld loads this Def at startup, while
// NarrativeContextSelector stays pure by receiving a copied NarrativePolicySnapshot instead of this
// live Verse Def. N1 reads the prompt wording and selection policy at the main-thread adapter. Wave 2
// adds exact source-owned monolith evidence through an existing hook, but still no live DLC provider.
//
// New to C#/RimWorld? See AGENTS.md ("XML Defs" and "DLC-safety").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One token-to-score row authored in <c>DiaryNarrativeContinuityDefs.xml</c>.</summary>
    public class DiaryNarrativeTokenWeightDef
    {
        public string token;
        public float score;
    }

    /// <summary>One source-evidence token to candidate token affinity row authored in XML.</summary>
    public class DiaryNarrativeAffinityDef
    {
        public string evidenceToken;
        public string candidateToken;
        public float score;
    }

    /// <summary>One optional category pair override; absent pairs retain the pure selector's safe default.</summary>
    public class DiaryNarrativeCategoryCoexistenceDef
    {
        public string firstCategory;
        public string secondCategory;
        public bool allowed = true;
    }

    /// <summary>One Full/Balanced/Compact narrative-lens cap and complete-fact character budget.</summary>
    public class DiaryNarrativeDetailBudgetDef
    {
        public string detailLevel;
        public int maxLenses;
        public int characterBudget;
        public bool allowExactArcPair;
        public int exactArcPairMaxCharacters;
    }

    /// <summary>One reflection kind's priority and cooldown authored in XML.</summary>
    public class DiaryNarrativeReflectionPriorityDef
    {
        public string kind;
        public int priority;
        public int cooldownTicks;
    }

    /// <summary>
    /// Singleton XML-owned policy for generic Narrative Continuity selection and later reflection
    /// arbitration. All fields are plain tuning data—no DLC Def reference or live DLC object is allowed.
    /// </summary>
    public class DiaryNarrativeContinuityDef : Def
    {
        public bool enabled = true;
        public int maxEvidencePerPov = 3;
        public int maxCandidates = 12;
        public int maxSelectedCandidates = 2;
        public int maxRecentSelectedCandidateKeys = 12;
        public int maximumCandidateAgeTicks = 180000;
        public int ageDecayWindowTicks = 60000;
        public float ageDecayFloor = 0.35f;
        public float repetitionPenalty = 45f;
        public float exactArcRepetitionPenalty;
        // This is a structured prompt-schema label, which remains English by the localization carve-out.
        public string promptFieldLabel = "narrative context";
        // Prompt prose is localized through DefInjected. Blank remains a safe fallback when a Def is
        // missing or an old/custom translation has not supplied the optional instruction.
        public string promptFieldInstruction = string.Empty;
        public List<DiaryNarrativeDetailBudgetDef> detailBudgets;
        public List<DiaryNarrativeTokenWeightDef> relationshipScores;
        public List<DiaryNarrativeTokenWeightDef> facetScores;
        public List<DiaryNarrativeTokenWeightDef> categoryScores;
        public List<DiaryNarrativeTokenWeightDef> salienceScores;
        public List<DiaryNarrativeTokenWeightDef> providerScores;
        public List<DiaryNarrativeAffinityDef> affinityRules;
        public List<DiaryNarrativeCategoryCoexistenceDef> categoryCoexistence;
        public int reflectionGlobalCooldownTicks = 60000;
        public int reflectionMinimumLinkedMemories = 2;
        public int reflectionMemoryCap = 8;
        public int reflectionMaximumSpanTicks = 3600000;
        public List<DiaryNarrativeReflectionPriorityDef> reflectionPriorities;
    }

    /// <summary>
    /// Copies the live Def into a fresh plain snapshot on the main thread. A missing/partial Def cannot
    /// crash a source event: valid code defaults remain in force, and malformed rows are ignored.
    /// </summary>
    internal static class DiaryNarrativeContinuityPolicy
    {
        private const string DefName = "Diary_NarrativeContinuity";

        /// <summary>Returns an immutable-by-convention snapshot for one pure selection/planning call.</summary>
        public static NarrativePolicySnapshot Snapshot()
        {
            NarrativePolicySnapshot snapshot = NarrativePolicySnapshot.CreateDefault();
            DiaryNarrativeContinuityDef source =
                DefDatabase<DiaryNarrativeContinuityDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return snapshot;
            }

            snapshot.enabled = source.enabled;
            snapshot.maxEvidencePerPov = PositiveOrFallback(source.maxEvidencePerPov, snapshot.maxEvidencePerPov);
            snapshot.maxCandidates = PositiveOrFallback(source.maxCandidates, snapshot.maxCandidates);
            snapshot.maxSelectedCandidates = PositiveOrFallback(
                source.maxSelectedCandidates, snapshot.maxSelectedCandidates);
            snapshot.maxRecentSelectedCandidateKeys = PositiveOrFallback(
                source.maxRecentSelectedCandidateKeys, snapshot.maxRecentSelectedCandidateKeys);
            snapshot.maximumCandidateAgeTicks = PositiveOrFallback(
                source.maximumCandidateAgeTicks, snapshot.maximumCandidateAgeTicks);
            snapshot.ageDecayWindowTicks = PositiveOrFallback(source.ageDecayWindowTicks, snapshot.ageDecayWindowTicks);
            snapshot.ageDecayFloor = Clamp01(source.ageDecayFloor);
            snapshot.repetitionPenalty = Math.Max(0f, source.repetitionPenalty);
            snapshot.exactArcRepetitionPenalty = Math.Max(0f, source.exactArcRepetitionPenalty);
            snapshot.promptFieldLabel = string.IsNullOrWhiteSpace(source.promptFieldLabel)
                ? snapshot.promptFieldLabel
                : source.promptFieldLabel.Trim();
            snapshot.promptFieldInstruction = source.promptFieldInstruction ?? string.Empty;
            snapshot.reflectionGlobalCooldownTicks = Math.Max(0, source.reflectionGlobalCooldownTicks);
            snapshot.reflectionMinimumLinkedMemories = PositiveOrFallback(
                source.reflectionMinimumLinkedMemories, snapshot.reflectionMinimumLinkedMemories);
            snapshot.reflectionMemoryCap = PositiveOrFallback(source.reflectionMemoryCap, snapshot.reflectionMemoryCap);
            snapshot.reflectionMaximumSpanTicks = PositiveOrFallback(
                source.reflectionMaximumSpanTicks, snapshot.reflectionMaximumSpanTicks);

            CopyDetailBudgets(source.detailBudgets, snapshot);
            CopyWeights(source.relationshipScores, snapshot.relationshipScores);
            CopyWeights(source.facetScores, snapshot.facetScores);
            CopyWeights(source.categoryScores, snapshot.categoryScores);
            CopyWeights(source.salienceScores, snapshot.salienceScores);
            CopyWeights(source.providerScores, snapshot.providerScores);
            CopyAffinity(source.affinityRules, snapshot.affinityRules);
            CopyCategoryCoexistence(source.categoryCoexistence, snapshot.categoryCoexistence);
            CopyReflectionPriorities(source.reflectionPriorities, snapshot.reflectionPriorities);
            return snapshot;
        }

        private static void CopyDetailBudgets(
            List<DiaryNarrativeDetailBudgetDef> source,
            NarrativePolicySnapshot destination)
        {
            if (source == null)
            {
                return;
            }

            List<NarrativeDetailBudget> copied = new List<NarrativeDetailBudget>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryNarrativeDetailBudgetDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.detailLevel))
                {
                    continue;
                }

                string level = NarrativeDetailLevelTokens.Normalize(row.detailLevel);
                if (ContainsDetailLevel(copied, level))
                {
                    continue;
                }

                NarrativeDetailBudget fallback = FindDetailBudget(destination.detailBudgets, level);
                copied.Add(new NarrativeDetailBudget
                {
                    detailLevel = level,
                    maxLenses = PositiveOrFallback(row.maxLenses, fallback.maxLenses),
                    characterBudget = PositiveOrFallback(row.characterBudget, fallback.characterBudget),
                    allowExactArcPair = row.allowExactArcPair,
                    exactArcPairMaxCharacters = PositiveOrFallback(
                        row.exactArcPairMaxCharacters, fallback.exactArcPairMaxCharacters)
                });
            }

            if (copied.Count > 0)
            {
                // Preserve an authored row while retaining the code fallback for any omitted preset.
                for (int i = 0; i < destination.detailBudgets.Count; i++)
                {
                    NarrativeDetailBudget fallback = destination.detailBudgets[i];
                    if (!ContainsDetailLevel(copied, fallback.detailLevel))
                    {
                        copied.Add(fallback);
                    }
                }

                destination.detailBudgets = copied;
            }
        }

        private static void CopyWeights(
            List<DiaryNarrativeTokenWeightDef> source,
            List<NarrativeTokenWeight> destination)
        {
            if (source == null)
            {
                return;
            }

            List<NarrativeTokenWeight> copied = new List<NarrativeTokenWeight>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryNarrativeTokenWeightDef row = source[i];
                if (row != null && !string.IsNullOrWhiteSpace(row.token))
                {
                    copied.Add(new NarrativeTokenWeight { token = row.token.Trim(), score = row.score });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static void CopyAffinity(
            List<DiaryNarrativeAffinityDef> source,
            List<NarrativeAffinityRule> destination)
        {
            if (source == null)
            {
                return;
            }

            destination.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryNarrativeAffinityDef row = source[i];
                if (row != null && !string.IsNullOrWhiteSpace(row.evidenceToken)
                    && !string.IsNullOrWhiteSpace(row.candidateToken))
                {
                    destination.Add(new NarrativeAffinityRule
                    {
                        evidenceToken = row.evidenceToken.Trim(),
                        candidateToken = row.candidateToken.Trim(),
                        score = row.score
                    });
                }
            }
        }

        private static void CopyCategoryCoexistence(
            List<DiaryNarrativeCategoryCoexistenceDef> source,
            List<NarrativeCategoryCoexistenceRule> destination)
        {
            if (source == null)
            {
                return;
            }

            destination.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryNarrativeCategoryCoexistenceDef row = source[i];
                if (row != null && NarrativeCategoryTokens.IsKnown(row.firstCategory)
                    && NarrativeCategoryTokens.IsKnown(row.secondCategory)
                    && !string.Equals(row.firstCategory, row.secondCategory, StringComparison.Ordinal))
                {
                    destination.Add(new NarrativeCategoryCoexistenceRule
                    {
                        firstCategory = row.firstCategory,
                        secondCategory = row.secondCategory,
                        allowed = row.allowed
                    });
                }
            }
        }

        private static void CopyReflectionPriorities(
            List<DiaryNarrativeReflectionPriorityDef> source,
            List<NarrativeReflectionPriority> destination)
        {
            if (source == null)
            {
                return;
            }

            List<NarrativeReflectionPriority> copied = new List<NarrativeReflectionPriority>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryNarrativeReflectionPriorityDef row = source[i];
                if (row != null && NarrativeReflectionKindTokens.IsKnown(row.kind)
                    && !ContainsReflectionKind(copied, row.kind))
                {
                    copied.Add(new NarrativeReflectionPriority
                    {
                        kind = row.kind,
                        priority = row.priority,
                        cooldownTicks = Math.Max(0, row.cooldownTicks)
                    });
                }
            }

            if (copied.Count > 0)
            {
                destination.Clear();
                destination.AddRange(copied);
            }
        }

        private static bool ContainsDetailLevel(List<NarrativeDetailBudget> values, string level)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && values[i].detailLevel == level)
                {
                    return true;
                }
            }

            return false;
        }

        private static NarrativeDetailBudget FindDetailBudget(List<NarrativeDetailBudget> values, string level)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && values[i].detailLevel == level)
                {
                    return values[i];
                }
            }

            return new NarrativeDetailBudget { detailLevel = level };
        }

        private static bool ContainsReflectionKind(List<NarrativeReflectionPriority> values, string kind)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && values[i].kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static int PositiveOrFallback(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
