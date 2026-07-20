// Pure spoiler-firewall projection for A2.1 surgical disclosure. It names only the generic visible
// disclosure, exact examiner/patient identities and labels, and truthful per-POV roles. No appended
// surgical text or configured creepjoiner state can enter this formatter's DTO contract.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Formats bounded prompt context for one verified surgical disclosure.</summary>
    internal static class CreepJoinerSurgicalDisclosureContextFormatter
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Returns generic visible disclosure context, or empty for an invalid plan.</summary>
        public static string Format(
            CreepJoinerSurgicalDisclosureFacts facts,
            CreepJoinerSurgicalDisclosurePlan plan)
        {
            if (facts == null || plan == null || !plan.valid) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.CreepJoinerOutcome);
            Add(parts, AnomalyContextKeys.CreepJoinerPhase, plan.phase);
            Add(parts, AnomalyContextKeys.VisibleResult, plan.visibleResultToken);
            Add(parts, AnomalyContextKeys.CreepJoinerSubjectId, facts.subjectPawnId);
            Add(parts, AnomalyContextKeys.CreepJoinerSubjectLabel, facts.subjectLabel);
            Add(parts, AnomalyContextKeys.CreepJoinerSurgeonId, facts.surgeonPawnId);
            Add(parts, AnomalyContextKeys.CreepJoinerSurgeonLabel, facts.surgeonLabel);
            if (plan.selectedWriters.Count == 1)
            {
                Add(parts, AnomalyContextKeys.WitnessRole, plan.selectedWriters[0].roleToken);
            }
            else if (plan.selectedWriters.Count > 1)
            {
                Add(parts, AnomalyContextKeys.InitiatorWitnessRole,
                    plan.selectedWriters[0].roleToken);
                Add(parts, AnomalyContextKeys.RecipientWitnessRole,
                    plan.selectedWriters[1].roleToken);
            }
            return string.Join("; ", parts.ToArray());
        }

        private static void Add(List<string> parts, string key, string value)
        {
            string clean = (value ?? string.Empty).Trim()
                .Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', '-');
            if (clean.Length > MaximumValueCharacters)
                clean = clean.Substring(0, MaximumValueCharacters).Trim();
            if (clean.Length > 0) parts.Add(key + "=" + clean);
        }
    }
}
