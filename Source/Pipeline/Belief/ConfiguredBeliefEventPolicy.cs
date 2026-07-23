// Pure XML gate for attaching factual belief evidence to pages that existing listeners already
// authorized. Callers pass only primitive event facts; this file neither reads RimWorld objects nor
// decides whether a diary page exists.
using System;

namespace PawnDiary
{
    /// <summary>Primitive facts already known by one existing event source.</summary>
    internal sealed class ConfiguredBeliefEventRequest
    {
        public string pawnId = string.Empty;
        public int tick;
        public string sourceDomain = string.Empty;
        public string sourceDefName = string.Empty;
        public string groupKey = string.Empty;
        public string povRole = string.Empty;
        public string visibleLabel = string.Empty;
        public string visibleField = "event_label";
        public string detailLabel = string.Empty;
        public string detailField = string.Empty;
        public string phase = string.Empty;
    }

    /// <summary>Admits evidence only when at least one loaded event-evidence rule matches exactly.</summary>
    internal static class ConfiguredBeliefEventPolicy
    {
        /// <summary>
        /// Builds one bounded detached row, or null when Ideology/policy is inactive, facts are unsafe,
        /// or XML has no exact owner. The null result leaves the caller's ordinary page unchanged.
        /// </summary>
        public static BeliefEventEvidence Capture(
            ConfiguredBeliefEventRequest request,
            bool ideologyActive,
            BeliefPolicySnapshot policy)
        {
            if (!ideologyActive || request == null || policy == null || !policy.enabled)
                return null;
            string domain = SafeToken(request.sourceDomain);
            string defName = SafeId(request.sourceDefName);
            string label = BeliefContextFormatter.WholeWord(request.visibleLabel, 320);
            string field = SafeField(request.visibleField);
            if (domain.Length == 0 || defName.Length == 0 || label.Length == 0 || field.Length == 0)
                return null;

            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                request.pawnId, request.tick, domain, defName, request.povRole, label,
                SafeToken(request.groupKey));
            evidence.narrative.phase = SafeToken(request.phase);
            evidence.matchFields.Add(new BeliefEvidenceTextFact { field = field, value = label });
            string detail = BeliefContextFormatter.WholeWord(request.detailLabel, 320);
            string detailField = SafeField(request.detailField);
            if (detail.Length > 0 && detailField.Length > 0)
                evidence.matchFields.Add(new BeliefEvidenceTextFact
                    { field = detailField, value = detail });

            ExpandedBeliefEvidence expanded = BeliefEventEvidencePolicy.Expand(evidence, policy);
            return expanded.matchedRuleKeys.Count > 0 ? evidence : null;
        }

        private static string SafeField(string value)
        {
            string token = SafeToken(value);
            switch (token)
            {
                case "event_label":
                case "subject_label":
                case "object_label":
                case "weapon_label":
                case "ritual_label":
                case "condition_label":
                    return token;
                default:
                    return string.Empty;
            }
        }

        private static string SafeToken(string value)
        {
            string token = SafeId(value);
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
            string result = BeliefContextFormatter.Clean(value, 160);
            return result.IndexOf('\n') >= 0 ? string.Empty : result;
        }
    }
}
