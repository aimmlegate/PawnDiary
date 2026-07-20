// Pure A2.2 prompt projection. It exposes only the visible ghoul transformation, exact role
// identities/labels, the irreversible nature of the choice, and truthful per-POV roles. Recipe
// checks, ingredients, mutation internals, health details, and other hidden mechanics cannot enter.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Formats bounded prompt context for one verified ghoul transformation.</summary>
    internal static class GhoulTransformationContextFormatter
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Returns visible transformation context, or empty for an invalid plan.</summary>
        public static string Format(GhoulTransformationFacts facts, GhoulTransformationPlan plan)
        {
            if (facts == null || plan == null || !plan.valid || !plan.transitionVerified)
                return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.GhoulTransformation);
            Add(parts, AnomalyContextKeys.Transformation, plan.transformationToken);
            Add(parts, AnomalyContextKeys.GhoulSubjectId, facts.subjectPawnId);
            Add(parts, AnomalyContextKeys.GhoulSubjectLabel, facts.subjectLabel);
            Add(parts, AnomalyContextKeys.GhoulSurgeonId, facts.surgeonPawnId);
            Add(parts, AnomalyContextKeys.GhoulSurgeonLabel, facts.surgeonLabel);
            Add(parts, AnomalyContextKeys.IrreversibleChoice, "true");
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
