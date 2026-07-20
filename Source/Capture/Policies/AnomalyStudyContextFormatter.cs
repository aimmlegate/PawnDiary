// Pure, bounded structured context for exact Anomaly study milestones and monolith enrichment.
// It projects semantic facts only: raw progress, private thresholds, letters, hidden abilities,
// containment chances, and undiscovered codex descriptions have no input field and cannot leak.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Formats stable source identity and prompt-safe key/value evidence.</summary>
    internal static class AnomalyStudyContextFormatter
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Builds one stable study identity without localized or hidden values.</summary>
        public static string SourceKey(AnomalyStudyFacts facts, AnomalyStudyPlan plan)
        {
            if (facts == null || plan == null || facts.tick < 0) return string.Empty;
            string studiedDef = Stable(facts.studiedDefName);
            string entityId = Stable(facts.studiedEntityId);
            string stage = Stable(plan.stageToken);
            string promotion = Stable(plan.promotionToken);
            if (studiedDef.Length == 0 || stage.Length == 0) return string.Empty;
            return studiedDef + "|" + entityId + "|" + stage + "|" + promotion + "|" + facts.tick;
        }

        /// <summary>Projects one study milestone to bounded semantic key/value context.</summary>
        public static string FormatStudy(AnomalyStudyFacts facts, AnomalyStudyPlan plan)
        {
            if (facts == null || plan == null) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.StudyBreakthrough);
            Add(parts, AnomalyContextKeys.StudyStage, plan.stageToken);
            Add(parts, AnomalyContextKeys.StudyPromotion, plan.promotionToken);
            Add(parts, AnomalyContextKeys.StudiedDef, facts.studiedDefName);
            Add(parts, AnomalyContextKeys.StudiedLabel, facts.studiedLabel);
            Add(parts, AnomalyContextKeys.CodexEntry, facts.codexEntryDefName);
            Add(parts, AnomalyContextKeys.CodexEntryLabel, facts.codexEntryLabel);
            Add(parts, AnomalyContextKeys.KnowledgeCategory, facts.knowledgeCategoryDefName);
            Add(parts, AnomalyContextKeys.KnowledgeCategoryLabel, facts.knowledgeCategoryLabel);
            Add(parts, AnomalyContextKeys.ContainedEntity, facts.isContainedEntity ? "true" : string.Empty);
            Add(parts, AnomalyContextKeys.Monolith, facts.isMonolith ? "true" : string.Empty);
            Add(parts, AnomalyContextKeys.Setting, facts.setting);
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>Projects one exact activation boundary and optional remembered study.</summary>
        public static string FormatMonolithActivation(
            AnomalyMonolithKnowledgeDecision decision,
            string researcherLabel)
        {
            if (decision == null) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.MonolithPreviousLevel, decision.previousLevelDefName);
            Add(parts, AnomalyContextKeys.MonolithReachedLevel, decision.reachedLevelDefName);
            if (decision.attach)
            {
                Add(parts, AnomalyContextKeys.MonolithLastResearcher, researcherLabel);
                Add(parts, AnomalyContextKeys.MonolithStudyStage, decision.studyStage);
                Add(parts, AnomalyContextKeys.MonolithBecameActivatable,
                    decision.becameActivatable ? "true" : string.Empty);
            }
            return string.Join("; ", parts.ToArray());
        }

        private static void Add(List<string> parts, string key, string value)
        {
            string clean = Context(value);
            if (clean.Length > 0) parts.Add(key + "=" + clean);
        }

        private static string Stable(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length > 0 && clean.Length <= MaximumValueCharacters
                && clean.IndexOf('|') < 0 && clean.IndexOf(';') < 0 && clean.IndexOf('=') < 0
                && clean.IndexOf('\r') < 0 && clean.IndexOf('\n') < 0
                ? clean
                : string.Empty;
        }

        private static string Context(string value)
        {
            string clean = (value ?? string.Empty).Trim()
                .Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', '-');
            if (clean.Length > MaximumValueCharacters)
                clean = clean.Substring(0, MaximumValueCharacters).Trim();
            return clean;
        }
    }
}
