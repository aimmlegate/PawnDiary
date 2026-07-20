// Pure spoiler-firewall projection for Phase A2.0. The DTO has no secret fields, and this formatter
// emits only the verified visible phase/result, stable subject identity/visible label, writer role,
// and terminal marker captured at the callback boundary.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Builds bounded source identity and prompt context for a visible creepjoiner outcome.</summary>
    internal static class CreepJoinerOutcomeContextFormatter
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Returns the bounded visible-only context fields for one verified outcome plan.</summary>
        public static string Format(CreepJoinerOutcomeFacts facts, CreepJoinerOutcomePlan plan)
        {
            if (facts == null || plan == null || !plan.valid) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.CreepJoinerOutcome);
            Add(parts, AnomalyContextKeys.CreepJoinerPhase, plan.phase);
            Add(parts, AnomalyContextKeys.VisibleResult, plan.visibleResultToken);
            Add(parts, AnomalyContextKeys.CreepJoinerSubjectId, facts.pawnId);
            Add(parts, AnomalyContextKeys.CreepJoinerSubjectLabel, facts.subjectLabel);
            Add(parts, AnomalyContextKeys.WitnessRole, plan.selectedWriter?.roleToken);
            Add(parts, AnomalyContextKeys.Terminal, plan.nextArc?.terminal == true ? "true" : string.Empty);
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
