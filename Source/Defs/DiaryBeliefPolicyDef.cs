// XML boundary for Ideology belief policy. RimWorld loads this Def, then the main-thread adapter
// copies it into an immutable BeliefPolicySnapshot before pure matching/ownership. No DLC Def or live
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

    /// <summary>One exact ingredient-kind to belief-evidence vocabulary mapping.</summary>
    public class DiaryBeliefFoodEvidenceRuleDef
    {
        public string key;
        public string ingredientKind;
        public string groupKey;
        public string matchField;
    }

    /// <summary>One exact generic-source to effective-downstream-group ownership rule.</summary>
    public class DiaryBeliefCanonicalEventOwnershipRuleDef
    {
        public string sourceDomain;
        public string sourceDefName;
        public string downstreamGroupDefName;
    }

    /// <summary>One exact authorized event route and its required mechanical mutation shape.</summary>
    public class DiaryBeliefMutationEventRuleDef
    {
        public string sourceDomain;
        public string sourceDefName;
        public string downstreamGroupDefName;
        public string subjectRole;
        public string evidenceGroupKey;
        public string requiredCauseToken;
        public string conversionResult;
        public string certaintyDirection;
        public string ideologyChange;
        public bool requireAttemptedIdeology;
    }

    /// <summary>One exact Counsel PlayLog outcome and its stable prompt-context tokens.</summary>
    public class DiaryCounselEventRuleDef
    {
        public string sourceDefName;
        public string downstreamGroupDefName;
        public string resultToken;
        public string moodEffectToken;
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
        public int maximumReflectedBeliefSourceIds = 16;
        public int beliefScanIntervalTicks = 250;
        public int maximumBeliefPawnsPerScan = 4;
        public int pendingBeliefEvidenceMaxAgeTicks = 3600000;
        public int maximumHistoryCorrelationEntries = 256;
        public int historyCorrelationWindowTicks = 120;
        public int maximumMutationCorrelationEntries = 256;
        public int mutationCorrelationWindowTicks = 120;
        // Dev-mode automatic coverage retains aggregate counts only. This XML-owned cap bounds how
        // many resolver attempts one loaded session admits before additional samples are dropped.
        public int maximumAutomaticDiagnosticSamples = 4096;
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
        public int recentBeliefEventWindowTicks = 180000;
        public int beliefReflectionCooldownTicks = 900000;
        public int maximumBeliefReflectionsPerQuadrum = 2;
        public int beliefReflectionMaxTokens = 360;
        // DefInjected model-facing text. These are read on the main thread and copied into the plain
        // prompt-policy contract; pure/background code never calls Translate().
        public string promptFieldLabel = "belief context";
        public string promptFieldInstruction = string.Empty;
        // Narrative N3-I factual prose. Missing/malformed reviewed formats disable only the optional
        // interpretation candidate; shipped language rows must preserve the required placeholders.
        public string interpretationFactFormat = string.Empty;
        public string interpretationFactWithoutDescriptionFormat = string.Empty;
        public List<DiaryBeliefTokenScoreDef> tierScores;
        public List<DiaryBeliefTokenScoreDef> eventFieldWeights;
        public List<DiaryBeliefTokenScoreDef> beliefFieldWeights;
        public List<DiaryBeliefCertaintyBandDef> certaintyBands;
        public List<DiaryBeliefSemanticAliasDef> semanticAliases;
        public List<DiaryBeliefEventEvidenceRuleDef> eventEvidenceRules;
        public List<DiaryBeliefFoodEvidenceRuleDef> foodEvidenceRules;
        public List<string> lexicalExclusions;
        public List<string> proselytizingPovRoles;
        // Exact event-source ownership, not doctrine policy. The code fallback is intentionally empty
        // so missing XML never suppresses an unknown or modded source.
        public List<DiaryBeliefCanonicalEventOwnershipRuleDef> canonicalEventOwnershipRules;
        // Exact enrichment mappings. The code fallback is empty so missing/malformed XML attaches no
        // transient mutation to an unrelated event.
        public List<DiaryBeliefMutationEventRuleDef> mutationEventRules;
        // Exact context-only Counsel outcomes. The code fallback is empty, so missing or malformed
        // XML can never guess a result or classify a similarly named third-party interaction.
        public List<DiaryCounselEventRuleDef> counselEventRules;
        public List<DiaryBeliefCorrelationCorrectionDef> correlationOverrides;
        public List<DiaryBeliefDetailBudgetDef> detailBudgets;
    }

    /// <summary>Copies the singleton live Def into one immutable pure snapshot with safe fallbacks.</summary>
    internal static class DiaryBeliefPolicy
    {
        private const string DefName = "Diary_BeliefPolicy";
        private static BeliefPolicySnapshot cached;
        private static LoadedLanguage cachedLanguage;

        /// <summary>Fast main-thread gate used by the non-emitting history observer.</summary>
        public static bool Enabled
        {
            get
            {
                if (cached != null && cachedLanguage == LanguageDatabase.activeLanguage)
                    return cached.enabled;
                DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
                return source != null && Snapshot().enabled;
            }
        }

        public static string PromptFieldLabel
        {
            get
            {
                DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
                return PromptTextSanitizer.OneLine(source?.promptFieldLabel);
            }
        }

        public static string PromptFieldInstruction
        {
            get
            {
                DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
                return PromptTextSanitizer.LocalizedPromptText(source?.promptFieldInstruction);
            }
        }

        /// <summary>
        /// Returns a shared read-only snapshot for the active language. Missing XML is not cached, so
        /// an early startup lookup cannot hide a Def which loads later.
        /// </summary>
        public static BeliefPolicySnapshot Snapshot()
        {
            if (cached != null && cachedLanguage == LanguageDatabase.activeLanguage)
                return cached;

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
            builder.maximumReflectedBeliefSourceIds = source.maximumReflectedBeliefSourceIds;
            builder.beliefScanIntervalTicks = source.beliefScanIntervalTicks;
            builder.maximumBeliefPawnsPerScan = source.maximumBeliefPawnsPerScan;
            builder.pendingBeliefEvidenceMaxAgeTicks = source.pendingBeliefEvidenceMaxAgeTicks;
            builder.maximumHistoryCorrelationEntries = source.maximumHistoryCorrelationEntries;
            builder.historyCorrelationWindowTicks = source.historyCorrelationWindowTicks;
            builder.maximumMutationCorrelationEntries = source.maximumMutationCorrelationEntries;
            builder.mutationCorrelationWindowTicks = source.mutationCorrelationWindowTicks;
            builder.maximumAutomaticDiagnosticSamples = source.maximumAutomaticDiagnosticSamples;
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
            builder.recentBeliefEventWindowTicks = source.recentBeliefEventWindowTicks;
            builder.beliefReflectionCooldownTicks = source.beliefReflectionCooldownTicks;
            builder.maximumBeliefReflectionsPerQuadrum = source.maximumBeliefReflectionsPerQuadrum;
            builder.beliefReflectionMaxTokens = source.beliefReflectionMaxTokens;

            CopyScores(source.tierScores, builder.tierScores);
            CopyScores(source.eventFieldWeights, builder.eventFieldWeights);
            CopyScores(source.beliefFieldWeights, builder.beliefFieldWeights);
            CopyBands(source.certaintyBands, builder.certaintyBands);
            CopyAliases(source.semanticAliases, builder.semanticAliases);
            CopyRules(source.eventEvidenceRules, builder.eventEvidenceRules);
            CopyFoodRules(source.foodEvidenceRules, builder.foodEvidenceRules);
            CopyStrings(source.lexicalExclusions, builder.lexicalExclusions, replaceWhenEmpty: false);
            CopyStrings(source.proselytizingPovRoles, builder.proselytizingPovRoles, replaceWhenEmpty: false);
            CopyOwnershipRules(source.canonicalEventOwnershipRules,
                builder.canonicalEventOwnershipRules);
            CopyMutationEventRules(source.mutationEventRules, builder.mutationEventRules);
            CopyCounselEventRules(source.counselEventRules, builder.counselEventRules);
            CopyCorrections(source.correlationOverrides, builder.correlationOverrides);
            CopyBudgets(source.detailBudgets, builder.detailBudgets);
            cached = builder.Build();
            cachedLanguage = LanguageDatabase.activeLanguage;
            return cached;
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

        private static void CopyOwnershipRules(
            List<DiaryBeliefCanonicalEventOwnershipRuleDef> source,
            List<BeliefCanonicalEventOwnershipRule> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefCanonicalEventOwnershipRuleDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.sourceDomain)
                    || string.IsNullOrWhiteSpace(row.sourceDefName)
                    || string.IsNullOrWhiteSpace(row.downstreamGroupDefName)) continue;
                destination.Add(new BeliefCanonicalEventOwnershipRule(
                    row.sourceDomain.Trim(), row.sourceDefName.Trim(),
                    row.downstreamGroupDefName.Trim()));
            }
        }

        private static void CopyFoodRules(
            List<DiaryBeliefFoodEvidenceRuleDef> source,
            List<BeliefFoodEvidenceRule> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefFoodEvidenceRuleDef row = source[i];
                // Preserve every non-null row. The pure selector must observe malformed/duplicate
                // exact mappings and fail closed instead of silently choosing by XML order.
                if (row != null)
                    destination.Add(new BeliefFoodEvidenceRule(
                        row.key, row.ingredientKind, row.groupKey, row.matchField));
            }
        }

        /// <summary>Localized N3-I format with ideoligion, precept label, and description slots.</summary>
        public static string InterpretationFactFormat
        {
            get
            {
                DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
                return PromptTextSanitizer.OneLine(source?.interpretationFactFormat);
            }
        }

        /// <summary>Localized N3-I format used when the live precept has no safe description.</summary>
        public static string InterpretationFactWithoutDescriptionFormat
        {
            get
            {
                DiaryBeliefPolicyDef source = DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail(DefName);
                return PromptTextSanitizer.OneLine(source?.interpretationFactWithoutDescriptionFormat);
            }
        }

        private static void CopyMutationEventRules(
            List<DiaryBeliefMutationEventRuleDef> source,
            List<BeliefMutationEventRule> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBeliefMutationEventRuleDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.sourceDomain)
                    || string.IsNullOrWhiteSpace(row.sourceDefName)
                    || string.IsNullOrWhiteSpace(row.downstreamGroupDefName)
                    || string.IsNullOrWhiteSpace(row.subjectRole)
                    || string.IsNullOrWhiteSpace(row.evidenceGroupKey)
                    || string.IsNullOrWhiteSpace(row.requiredCauseToken)
                    || string.IsNullOrWhiteSpace(row.conversionResult)
                    || string.IsNullOrWhiteSpace(row.certaintyDirection)
                    || string.IsNullOrWhiteSpace(row.ideologyChange))
                    continue;
                destination.Add(new BeliefMutationEventRule(
                    row.sourceDomain.Trim(), row.sourceDefName.Trim(),
                    row.downstreamGroupDefName.Trim(), row.subjectRole.Trim(),
                    row.evidenceGroupKey.Trim(), row.requiredCauseToken.Trim(),
                    row.conversionResult.Trim(), row.certaintyDirection.Trim(),
                    row.ideologyChange.Trim(), row.requireAttemptedIdeology));
            }
        }

        private static void CopyCounselEventRules(
            List<DiaryCounselEventRuleDef> source,
            List<CounselEventRule> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count && i < 32; i++)
            {
                DiaryCounselEventRuleDef row = source[i];
                if (row == null || string.IsNullOrWhiteSpace(row.sourceDefName)
                    || string.IsNullOrWhiteSpace(row.downstreamGroupDefName)
                    || string.IsNullOrWhiteSpace(row.resultToken)
                    || string.IsNullOrWhiteSpace(row.moodEffectToken))
                    continue;
                destination.Add(new CounselEventRule(
                    row.sourceDefName.Trim(), row.downstreamGroupDefName.Trim(),
                    row.resultToken.Trim(), row.moodEffectToken.Trim()));
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
            for (int i = 0; i < source.Count; i++)
                if (!string.IsNullOrWhiteSpace(source[i])) destination.Add(source[i].Trim());
        }
    }
}
