// Pure constructors/copy helpers for event-relative belief evidence. Source adapters pass only facts
// they already know; these helpers never decide whether an event is captured and never inspect live
// game objects.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Builds bounded, POV-specific evidence rows for runtime adapters and dev fixtures.</summary>
    internal static class BeliefEventEvidenceFactory
    {
        public static BeliefEventEvidence ForThought(
            string pawnId,
            int tick,
            string thoughtDefName,
            string thoughtLabel,
            BeliefSourcePreceptFact sourcePrecept)
        {
            BeliefEventEvidence result = ForEvent(
                pawnId, tick, "thought", thoughtDefName, "initiator",
                thoughtLabel, string.Empty);
            result.narrative.facet = NarrativeFacetTokens.AmbientPressure;
            result.narrative.phase = "thought_gained";
            result.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
            result.narrative.subjectId = SafeId(pawnId);
            AddUnique(result.thoughtDefNames, thoughtDefName, 32);
            AddField(result, "event_label", thoughtLabel);
            if (sourcePrecept != null)
            {
                result.sourcePreceptInstanceId = SafeId(sourcePrecept.instanceId);
                result.sourcePreceptDefName = SafeId(sourcePrecept.defName);
            }
            return result;
        }

        public static BeliefEventEvidence ForBodyModification(
            string pawnId,
            int tick,
            string hediffDefName,
            string hediffLabel,
            string bodyPartLabel,
            string partKindToken,
            string partTierToken)
        {
            BeliefEventEvidence result = ForEvent(
                pawnId, tick, "medical", hediffDefName, "initiator",
                hediffLabel, "body_modification");
            result.narrative.facet = NarrativeFacetTokens.IdentityTransition;
            result.narrative.phase = "body_modified";
            result.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
            result.narrative.subjectId = SafeId(pawnId);
            result.narrative.salience = NarrativeSalienceTokens.Meaningful;
            AddUnique(result.narrative.beliefTopics, "body_modification", 16);
            AddField(result, "hediff_label", hediffLabel);
            AddField(result, "body_part_label", bodyPartLabel);
            AddField(result, "event_label", JoinVisible(partKindToken, partTierToken));
            return result;
        }

        public static BeliefEventEvidence ForEvent(
            string pawnId,
            int tick,
            string domain,
            string defName,
            string povRole,
            string subjectLabel,
            string groupKey)
        {
            return new BeliefEventEvidence
            {
                narrative = new NarrativeEvidence
                {
                    tick = Math.Max(0, tick),
                    povPawnId = SafeId(pawnId),
                    povRole = NormalizeRole(povRole),
                    subjectLabel = SafeText(subjectLabel),
                    pawnCanKnow = true,
                    sourceDomain = SafeToken(domain),
                    sourceDefName = SafeId(defName)
                },
                groupKey = SafeToken(groupKey)
            };
        }

        /// <summary>Deep-copies one row and stamps canonical event/POV identity without mutating callers.</summary>
        public static BeliefEventEvidence ForPov(
            BeliefEventEvidence source,
            string eventId,
            int eventTick,
            string pawnId,
            string povRole)
        {
            if (source == null) return null;
            BeliefEventEvidence result = new BeliefEventEvidence
            {
                narrative = CopyNarrative(source.narrative),
                groupKey = SafeToken(source.groupKey),
                currentBeliefFactsRelevant = source.currentBeliefFactsRelevant,
                sourcePreceptInstanceId = SafeId(source.sourcePreceptInstanceId),
                sourcePreceptDefName = SafeId(source.sourcePreceptDefName),
                mutation = source.mutation
            };
            CopyIds(source.thoughtDefNames, result.thoughtDefNames, 32);
            CopyIds(source.historyEventDefNames, result.historyEventDefNames, 32);
            CopyIds(source.issueDefNames, result.issueDefNames, 32);
            CopyIds(source.memeDefNames, result.memeDefNames, 32);
            CopyTokens(source.semanticAliasTokens, result.semanticAliasTokens, 32);
            if (source.matchFields != null)
            {
                for (int i = 0; i < source.matchFields.Count && result.matchFields.Count < 32; i++)
                {
                    BeliefEvidenceTextFact field = source.matchFields[i];
                    if (field != null) AddField(result, field.field, field.value);
                }
            }

            result.narrative.eventId = SafeId(eventId);
            result.narrative.tick = Math.Max(0, eventTick);
            result.narrative.povPawnId = SafeId(pawnId);
            result.narrative.povRole = NormalizeRole(povRole);
            return result;
        }

        public static bool HasUsefulVisibleEvidence(BeliefEventEvidence evidence)
        {
            if (evidence?.narrative?.pawnCanKnow != true) return false;
            return SafeId(evidence.sourcePreceptInstanceId).Length > 0
                || SafeId(evidence.sourcePreceptDefName).Length > 0
                || HasValues(evidence.thoughtDefNames)
                || HasValues(evidence.historyEventDefNames)
                || HasValues(evidence.issueDefNames)
                || HasValues(evidence.memeDefNames)
                || HasFields(evidence.matchFields)
                || HasValues(evidence.semanticAliasTokens)
                || HasValues(evidence.narrative.beliefTopics)
                || evidence.currentBeliefFactsRelevant
                || evidence.mutation != null && evidence.mutation.HasUsefulFact;
        }

        public static void AddHistoryDefNames(BeliefEventEvidence evidence, IEnumerable<string> values)
        {
            if (evidence == null || values == null) return;
            foreach (string value in values) AddUnique(evidence.historyEventDefNames, value, 32);
        }

        /// <summary>Developer-only structural fixture derived entirely from a detached live snapshot.</summary>
        public static BeliefEventEvidence SyntheticStructural(
            BeliefSnapshot snapshot,
            string pawnId,
            int tick,
            string eventLabel)
        {
            BeliefEventEvidence result = ForEvent(pawnId, tick, "developer", "BeliefStructuralPreview",
                "initiator", eventLabel, "belief_preview");
            if (snapshot?.precepts == null) return result;
            for (int i = 0; i < snapshot.precepts.Count; i++)
            {
                BeliefPreceptFact precept = snapshot.precepts[i];
                if (precept?.correlations == null) continue;
                for (int j = 0; j < precept.correlations.Count; j++)
                {
                    BeliefCorrelationFact correlation = precept.correlations[j];
                    if (correlation == null || SafeId(correlation.defName).Length == 0) continue;
                    if (correlation.kind == BeliefCorrelationKindTokens.Thought)
                        AddUnique(result.thoughtDefNames, correlation.defName, 32);
                    else if (correlation.kind == BeliefCorrelationKindTokens.HistoryEvent)
                        AddUnique(result.historyEventDefNames, correlation.defName, 32);
                    else
                        continue;
                    return result;
                }
            }
            return result;
        }

        /// <summary>Developer-only lexical fixture using sanitized detached text and no exact IDs.</summary>
        public static BeliefEventEvidence SyntheticLexical(
            BeliefSnapshot snapshot,
            string pawnId,
            int tick,
            string eventLabel)
        {
            BeliefEventEvidence result = ForEvent(pawnId, tick, "developer", "BeliefLexicalPreview",
                "initiator", eventLabel, "belief_preview");
            if (snapshot?.precepts == null) return result;
            for (int i = 0; i < snapshot.precepts.Count; i++)
            {
                BeliefPreceptFact precept = snapshot.precepts[i];
                string candidate = FirstUsefulLexicalText(precept);
                if (candidate.Length == 0) continue;
                AddField(result, "event_label", candidate);
                return result;
            }
            return result;
        }

        private static NarrativeEvidence CopyNarrative(NarrativeEvidence source)
        {
            NarrativeEvidence result = new NarrativeEvidence();
            if (source == null) return result;
            result.eventId = SafeId(source.eventId);
            result.tick = Math.Max(0, source.tick);
            result.povPawnId = SafeId(source.povPawnId);
            result.povRole = NormalizeRole(source.povRole);
            result.facet = SafeToken(source.facet);
            result.phase = SafeToken(source.phase);
            result.subjectKind = SafeToken(source.subjectKind);
            result.subjectId = SafeId(source.subjectId);
            result.subjectLabel = SafeText(source.subjectLabel);
            result.arcKey = SafeId(source.arcKey);
            result.relatedEventId = SafeId(source.relatedEventId);
            CopyTokens(source.beliefTopics, result.beliefTopics, 16);
            result.salience = SafeToken(source.salience);
            result.pawnCanKnow = source.pawnCanKnow;
            result.sourceDomain = SafeToken(source.sourceDomain);
            result.sourceDefName = SafeId(source.sourceDefName);
            return result;
        }

        private static void AddField(BeliefEventEvidence target, string kind, string value)
        {
            if (target == null || target.matchFields.Count >= 32) return;
            string field = SafeToken(kind);
            string text = SafeText(value);
            if (field.Length == 0 || text.Length == 0) return;
            target.matchFields.Add(new BeliefEvidenceTextFact { field = field, value = text });
        }

        private static string FirstUsefulLexicalText(BeliefPreceptFact precept)
        {
            if (precept == null) return string.Empty;
            if (precept.correlations != null)
                for (int i = 0; i < precept.correlations.Count; i++)
                {
                    BeliefCorrelationFact correlation = precept.correlations[i];
                    string value = SafeText(correlation?.description);
                    if (value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                        return value;
                }
            string issue = SafeText(precept.issue?.description);
            if (issue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2) return issue;
            return SafeText(precept.description);
        }

        private static void CopyIds(IEnumerable<string> source, List<string> target, int cap)
        {
            if (source == null) return;
            foreach (string value in source) AddUnique(target, value, cap);
        }

        private static void CopyTokens(IEnumerable<string> source, List<string> target, int cap)
        {
            if (source == null) return;
            foreach (string value in source)
            {
                if (target.Count >= cap) break;
                string token = SafeToken(value);
                if (token.Length > 0) AddUnique(target, token, cap);
            }
        }

        private static void AddUnique(List<string> target, string value, int cap)
        {
            if (target == null || target.Count >= cap) return;
            string cleaned = SafeId(value);
            if (cleaned.Length == 0) return;
            for (int i = 0; i < target.Count; i++)
                if (string.Equals(target[i], cleaned, StringComparison.OrdinalIgnoreCase)) return;
            target.Add(cleaned);
        }

        private static bool HasValues(List<string> values)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (SafeId(values[i]).Length > 0) return true;
            return false;
        }

        private static bool HasFields(List<BeliefEvidenceTextFact> values)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (SafeText(values[i]?.value).Length > 0) return true;
            return false;
        }

        private static string NormalizeRole(string value)
        {
            if (string.Equals(value, "recipient", StringComparison.OrdinalIgnoreCase))
                return "recipient";
            return "initiator";
        }

        private static string JoinVisible(string first, string second)
        {
            string left = SafeText(first);
            string right = SafeText(second);
            return left.Length == 0 ? right : right.Length == 0 ? left : left + " " + right;
        }

        private static string SafeText(string value)
        {
            return BeliefContextFormatter.WholeWord(value, 320);
        }

        private static string SafeToken(string value)
        {
            string token = SafeId(value);
            if (token.Length == 0) return string.Empty;
            for (int i = 0; i < token.Length; i++)
            {
                char character = token[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-'))
                    return string.Empty;
            }
            return token;
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string cleaned = BeliefContextFormatter.Clean(value, 160);
            return cleaned.IndexOf('\n') >= 0 ? string.Empty : cleaned;
        }
    }
}
