// Pure, bounded prompt context for one verified containment breach. The live adapter supplies only
// visible labels, stable Def names, an already-localized pre-ejection setting, and truthful witness
// roles; hidden containment mechanics and cell/platform identities have no output path here.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Formats one breach plan without exposing hidden mechanics or exact map positions.</summary>
    internal static class ContainmentBreachContextFormatter
    {
        private const int MaximumValueCharacters = 240;
        private const int MaximumEntityCharacters = 120;

        /// <summary>Projects bounded escaped-entity, witness-role, cascade, and setting evidence.</summary>
        public static string Format(ContainmentBreachPlan plan)
        {
            if (plan == null || !plan.valid || plan.escapedCount < 1) return string.Empty;

            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.ContainmentBreach);
            Add(parts, AnomalyContextKeys.EscapedCount, plan.escapedCount.ToString());
            Add(parts, AnomalyContextKeys.EscapedEntities, EntitySummary(plan));
            if (plan.additionalEscapedCount > 0)
            {
                Add(parts, AnomalyContextKeys.AdditionalEscapedCount,
                    plan.additionalEscapedCount.ToString());
            }

            if (plan.selectedWriters.Count == 1)
            {
                Add(parts, AnomalyContextKeys.WitnessRole,
                    plan.selectedWriters[0]?.roleToken);
            }
            else if (plan.selectedWriters.Count > 1)
            {
                Add(parts, AnomalyContextKeys.InitiatorWitnessRole,
                    plan.selectedWriters[0]?.roleToken);
                Add(parts, AnomalyContextKeys.RecipientWitnessRole,
                    plan.selectedWriters[1]?.roleToken);
            }

            Add(parts, AnomalyContextKeys.Setting, plan.preEjectionSetting);
            Add(parts, AnomalyContextKeys.SameRoomCascade,
                plan.sameRoomCascade ? "true" : "false");
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>Returns the same bounded visible entity list used by fallback event text.</summary>
        public static string EntitySummary(ContainmentBreachPlan plan)
        {
            if (plan?.contextEntities == null) return string.Empty;
            List<string> entities = new List<string>();
            for (int i = 0; i < plan.contextEntities.Count; i++)
            {
                ContainedEntityFact fact = plan.contextEntities[i];
                if (fact == null) continue;
                string label = Context(fact.visibleLabel, MaximumEntityCharacters);
                string defName = Stable(fact.defName, MaximumEntityCharacters);
                string value = label.Length > 0 && defName.Length > 0
                    ? label + " [" + defName + "]"
                    : label.Length > 0 ? label : defName;
                if (value.Length > 0) entities.Add(value);
            }
            return string.Join(", ", entities.ToArray());
        }

        private static void Add(List<string> parts, string key, string value)
        {
            string clean = Context(value, MaximumValueCharacters);
            if (clean.Length > 0) parts.Add(key + "=" + clean);
        }

        private static string Stable(string value, int maximum)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length > 0 && clean.Length <= maximum
                && clean.IndexOf('|') < 0 && clean.IndexOf(';') < 0 && clean.IndexOf('=') < 0
                && clean.IndexOf('\r') < 0 && clean.IndexOf('\n') < 0
                ? clean
                : string.Empty;
        }

        private static string Context(string value, int maximum)
        {
            string clean = (value ?? string.Empty).Trim()
                .Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', '-');
            if (clean.Length > maximum) clean = clean.Substring(0, maximum).Trim();
            return clean;
        }
    }
}
