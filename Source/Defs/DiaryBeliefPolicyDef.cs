// XML boundary for Ideology belief policy. RimWorld loads this Def, then the future main-thread
// adapter copies it into an immutable BeliefPolicySnapshot before pure matching. No DLC Def or live
// Ideology object is referenced here, so the policy is safe when Ideology is absent.
//
// New to C#/RimWorld? See AGENTS.md ("XML Defs" and "DLC-safety").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One XML-authored token score.</summary>
    public class DiaryBeliefTokenScoreDef
    {
        public string token;
        public float score;
    }

    /// <summary>One belief-topic concept plus localized guarded lexical equivalents.</summary>
    public class DiaryBeliefSemanticAliasDef
    {
        public string topicToken;
        public List<string> aliases;
    }

    /// <summary>One exact event-fact rule which expands topics/aliases but never names doctrine.</summary>
    public class DiaryBeliefEventEvidenceRuleDef
    {
        public string key;
        public string sourceDomain;
        public string sourceDefName;
        public string groupKey;
        public string facet;
        public string phase;
        public string povRole;
        public string mutationCauseToken;
        public List<string> addTopics;
        public List<string> addSemanticAliases;
    }

    /// <summary>Optional explicit compatibility correction. The shipped list remains empty.</summary>
    public class DiaryBeliefCorrelationCorrectionDef
    {
        public string key;
        public string action;
        public string preceptDefName;
        public string issueDefName;
        public string memeDefName;
        public string sourceDomain;
        public string sourceDefName;
        public string groupKey;
        public string topicToken;
    }

    /// <summary>One certainty boundary and DefInjected model-facing phrase.</summary>
    public class DiaryBeliefCertaintyBandDef
    {
        public string token;
        public float minimum;
        public string phrase;
    }

    /// <summary>One Full/Balanced/Compact formatter budget.</summary>
    public class DiaryBeliefDetailBudgetDef
    {
        public string detailLevel;
        public int maximumLines;
        public int maximumCharacters;
        public bool includeDescriptions;
        public bool includeStructure;
        public bool includeMemes;
        public bool includeDeity;
    }

    /// <summary>
    /// Singleton DLC-neutral policy for pure correlation, lexical matching, certainty, formatting, and
    /// future reflection decisions. All player/model-facing phrases are Def text, not C# literals.
    /// </summary>
    public class DiaryBeliefPolicyDef : Def
    {
        public bool enabled = true;
        public int maximumPreceptCandidates = 128;
        public int maximumMemeCandidates = 32;
        public int maximumDeityCandidates = 16;
        public int maximumSelectedStances = 2;
        public int defaultSelectedStances = 1;
        public int maximumSupportingMemes = 2;
        public int maximumRecentSelections = 16;
        public int maximumFieldCharacters = 320;
        public int maximumNormalizedTokensPerField = 48;
        public int maximumLexicalFieldsPerDocument = 96;
        public int maximumLexicalTokensPerDocument = 256;
        public int maximumDescriptionCharacters = 240;
        public int maximumIdentifierCharacters = 160;
        public int maximumTotalCharacters = 1800;
        public int maximumTotalLines = 16;
        public float minimumLexicalConfidence = 65f;
        public float lexicalRunnerUpMargin = 18f;
        public float fuzzyRunnerUpMargin = 26f;
        public int minimumDistinctiveTokenMatches = 2;
        public int uniqueTokenMinimumCharacters = 8;
        public int fuzzyTokenMinimumCharacters = 7;
        public int fuzzyMinimumDistinctiveMatches = 2;
        public float fuzzySimilarityMinimum = 0.84f;
        public int commonTokenMinimumDocuments = 2;
        public float commonTokenDocumentFraction = 0.55f;
        public float phraseMatchScore = 42f;
        public float tokenMatchScore = 18f;
        public float uniqueTokenBonus = 18f;
        public float fuzzyMatchScore = 16f;
        public float selectionWeightBase = 100f;
        public float secondSlotMinimumScore = 900f;
        public float recentSelectionPenalty = 450f;
        public float requiredByMemeBonus = 120f;
        public float proselytizingRoleBonus = 100f;
        public float meaningfulSalienceBonus = 45f;
        public float majorSalienceBonus = 90f;
        public float terminalSalienceBonus = 120f;
        public float highImpactBonus = 220f;
        public float mediumImpactBonus = 120f;
        public float lowImpactBonus = 40f;
        public float certaintyMeaningfulDelta = 0.05f;
        public float certaintyMajorDelta = 0.15f;
        public bool includeStructure = true;
        public bool includeRelatedDeity = true;
        public bool includeKeyDeity = true;
        public bool allowDeterministicAlternativeDeity;
        public float quietReflectionChance = 0.08f;
        public int beliefReflectionCooldownTicks = 900000;
        public int maximumBeliefReflectionsPerQuadrum = 2;
        public List<DiaryBeliefTokenScoreDef> tierScores;
        public List<DiaryBeliefTokenScoreDef> eventFieldWeights;
        public List<DiaryBeliefTokenScoreDef> beliefFieldWeights;
        public List<DiaryBeliefCertaintyBandDef> certaintyBands;
        public List<DiaryBeliefSemanticAliasDef> semanticAliases;
        public List<DiaryBeliefEventEvidenceRuleDef> eventEvidenceRules;
        public List<string> lexicalExclusions;
        public List<string> proselytizingPovRoles;
        public List<DiaryBeliefCorrelationCorrectionDef> correlationOverrides;
        public List<DiaryBeliefDetailBudgetDef> detailBudgets;
    }

    /// <summary>Copies the singleton live Def into one immutable pure snapshot with safe fallbacks.</summary>
    internal static class DiaryBeliefPolicy
    {
        private const string DefName = "Diary_BeliefPolicy";

        /// <summary>Returns a fresh immutable snapshot; missing or malformed XML retains code defaults.</summary>
        public static BeliefPolicySnapshot Snapshot()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null) return builder.Build();

            builder.enabled = source.enabled;
            builder.maximumPreceptCandidates = source.maximumPreceptCandidates;
            builder.maximumMemeCandidates = source.maximumMemeCandidates;
            builder.maximumDeityCandidates = source.maximumDeityCandidates;
            builder.maximumSelectedStances = source.maximumSelectedStances;
            builder.defaultSelectedStances = source.defaultSelectedStances;
            builder.maximumSupportingMemes = source.maximumSupportingMemes;
            builder.maximumRecentSelections = source.maximumRecentSelections;
            builder.maximumFieldCharacters = source.maximumFieldCharacters;
            builder.maximumNormalizedTokensPerField = source.maximumNormalizedTokensPerField;
            builder.maximumLexicalFieldsPerDocument = source.maximumLexicalFieldsPerDocument;
            builder.maximumLexicalTokensPerDocument = source.maximumLexicalTokensPerDocument;
            builder.maximumDescriptionCharacters = source.maximumDescriptionCharacters;
            builder.maximumIdentifierCharacters = source.maximumIdentifierCharacters;
            builder.maximumTotalCharacters = source.maximumTotalCharacters;
            builder.maximumTotalLines = source.maximumTotalLines;
            builder.minimumLexicalConfidence = source.minimumLexicalConfidence;
            builder.lexicalRunnerUpMargin = source.lexicalRunnerUpMargin;
            builder.fuzzyRunnerUpMargin = source.fuzzyRunnerUpMargin;
            builder.minimumDistinctiveTokenMatches = source.minimumDistinctiveTokenMatches;
            builder.uniqueTokenMinimumCharacters = source.uniqueTokenMinimumCharacters;
            builder.fuzzyTokenMinimumCharacters = source.fuzzyTokenMinimumCharacters;
            builder.fuzzyMinimumDistinctiveMatches = source.fuzzyMinimumDistinctiveMatches;
            builder.fuzzySimilarityMinimum = source.fuzzySimilarityMinimum;
            builder.commonTokenMinimumDocuments = source.commonTokenMinimumDocuments;
            builder.commonTokenDocumentFraction = source.commonTokenDocumentFraction;
            builder.phraseMatchScore = source.phraseMatchScore;
            builder.tokenMatchScore = source.tokenMatchScore;
            builder.uniqueTokenBonus = source.uniqueTokenBonus;
            builder.fuzzyMatchScore = source.fuzzyMatchScore;
            builder.selectionWeightBase = source.selectionWeightBase;
            builder.secondSlotMinimumScore = source.secondSlotMinimumScore;
            builder.recentSelectionPenalty = source.recentSelectionPenalty;
            builder.requiredByMemeBonus = source.requiredByMemeBonus;
            builder.proselytizingRoleBonus = source.proselytizingRoleBonus;
            builder.meaningfulSalienceBonus = source.meaningfulSalienceBonus;
            builder.majorSalienceBonus = source.majorSalienceBonus;
            builder.terminalSalienceBonus = source.terminalSalienceBonus;
            builder.highImpactBonus = source.highImpactBonus;
            builder.mediumImpactBonus = source.mediumImpactBonus;
            builder.lowImpactBonus = source.lowImpactBonus;
            builder.certaintyMeaningfulDelta = source.certaintyMeaningfulDelta;
            builder.certaintyMajorDelta = source.certaintyMajorDelta;
            builder.includeStructure = source.includeStructure;
            builder.includeRelatedDeity = source.includeRelatedDeity;
            builder.includeKeyDeity = source.includeKeyDeity;
            builder.allowDeterministicAlternativeDeity = source.allowDeterministicAlternativeDeity;
            builder.quietReflectionChance = source.quietReflectionChance;
            builder.beliefReflectionCooldownTicks = source.beliefReflectionCooldownTicks;
            builder.maximumBeliefReflectionsPerQuadrum = source.maximumBeliefReflectionsPerQuadrum;

            CopyScores(source.tierScores, builder.tierScores);
            CopyScores(source.eventFieldWeights, builder.eventFieldWeights);
            CopyScores(source.beliefFieldWeights, builder.beliefFieldWeights);
            CopyBands(source.certaintyBands, builder.certaintyBands);
            CopyAliases(source.semanticAliases, builder.semanticAliases);
            CopyRules(source.eventEvidenceRules, builder.eventEvidenceRules);
            CopyStrings(source.lexicalExclusions, builder.lexicalExclusions, replaceWhenEmpty: false);
            CopyStrings(source.proselytizingPovRoles, builder.proselytizingPovRoles, replaceWhenEmpty: false);
            CopyCorrections(source.correlationOverrides, builder.correlationOverrides);
            CopyBudgets(source.detailBudgets, builder.detailBudgets);
            return builder.Build();
        }

        private static void CopyScores(List<DiaryBeliefTokenScoreDef> source, List<BeliefTokenScore> destination)
        {
            if (source == null || source.Count == 0) return;
            List<BeliefTokenScore> copy = new List<BeliefTokenScore>();
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && !string.IsNullOrWhiteSpace(source[i].token))
                    copy.Add(new BeliefTokenScore(source[i].token.Trim(), source[i].score));
            if (copy.Count == 0) return;
            destination.Clear();
            destination.AddRange(copy);
        }

        private static void CopyBands(List<DiaryBeliefCertaintyBandDef> source, List<BeliefCertaintyBand> destination)
        {
            if (source == null || source.Count == 0) return;
            List<BeliefCertaintyBand> copy = new List<BeliefCertaintyBand>();
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && !string.IsNullOrWhiteSpace(source[i].token))
                    copy.Add(new BeliefCertaintyBand(source[i].token.Trim(), source[i].minimum, source[i].phrase));
            if (copy.Count == 0) return;
            destination.Clear();
            destination.AddRange(copy);
        }

        private static void CopyAliases(List<DiaryBeliefSemanticAliasDef> source, List<BeliefSemanticAlias> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && !string.IsNullOrWhiteSpace(source[i].topicToken))
                    destination.Add(new BeliefSemanticAlias(source[i].topicToken.Trim(), source[i].aliases));
        }

        private static void CopyRules(
            List<DiaryBeliefEventEvidenceRuleDef> source,
            List<BeliefEventEvidenceRule> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefEventEvidenceRuleDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.key)) continue;
                destination.Add(new BeliefEventEvidenceRule(row.key.Trim(), row.sourceDomain, row.sourceDefName,
                    row.groupKey, row.facet, row.phase, row.povRole, row.mutationCauseToken,
                    row.addTopics, row.addSemanticAliases));
            }
        }

        private static void CopyCorrections(
            List<DiaryBeliefCorrelationCorrectionDef> source,
            List<BeliefCorrelationCorrection> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefCorrelationCorrectionDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.key)
                    || !BeliefCorrectionActionTokens.IsKnown((row.action ?? string.Empty).Trim())) continue;
                destination.Add(new BeliefCorrelationCorrection(row.key.Trim(), row.action.Trim(),
                    row.preceptDefName, row.issueDefName, row.memeDefName, row.sourceDomain,
                    row.sourceDefName, row.groupKey, row.topicToken));
            }
        }

        private static void CopyBudgets(List<DiaryBeliefDetailBudgetDef> source, List<BeliefDetailBudget> destination)
        {
            if (source == null || source.Count == 0) return;
            List<BeliefDetailBudget> copy = new List<BeliefDetailBudget>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefDetailBudgetDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.detailLevel)) continue;
                copy.Add(new BeliefDetailBudget(row.detailLevel, row.maximumLines, row.maximumCharacters,
                    row.includeDescriptions, row.includeStructure, row.includeMemes, row.includeDeity));
            }
            if (copy.Count == 0) return;
            destination.Clear();
            destination.AddRange(copy);
        }

        private static void CopyStrings(List<string> source, List<string> destination, bool replaceWhenEmpty)
        {
            if (source == null || source.Count == 0 && !replaceWhenEmpty) return;
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
                if (!string.IsNullOrWhiteSpace(source[i])) destination.Add(source[i].Trim());
        }
    }
}
