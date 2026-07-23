// Pure prompt projection for Odyssey O3's verified Mechhive ending. Only exact source facts enter:
// the operator, destroy-versus-scavenge branch, quest root, terminal marker, and bounded event-time
// actor/surroundings summaries. Hidden quest text, motives, combat claims, and future effects have no
// input field and therefore cannot leak through this formatter.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Formats bounded structured context for one verified Mechhive outcome page.</summary>
    internal static class OdysseyMechhiveContextFormatter
    {
        /// <summary>Returns empty for an invalid plan; otherwise returns only safe terminal evidence.</summary>
        public static string Format(
            OdysseyMechhiveOutcomeFacts facts,
            OdysseyMechhiveOutcomePlan plan,
            OdysseyPolicySnapshot policy)
        {
            if (facts == null || plan == null || !plan.valid) return string.Empty;
            int cap = policy == null ? 120 : policy.maximumContextValueCharacters;
            List<string> parts = new List<string>();
            Add(parts, "odyssey_kind", "mechhive_outcome", cap);
            Add(parts, "mechhive_outcome", plan.outcomeToken, cap);
            Add(parts, "actor", facts.actorLabel, cap);
            Add(parts, "quest_root", facts.questRootDefName, cap);
            Add(parts, "terminal", "true", cap);
            Add(parts, "actor_summary", facts.actorSummary, cap);
            Add(parts, "setting", facts.setting, cap);
            string result = string.Join("; ", parts.ToArray());
            int totalCap = policy == null ? 900 : policy.maximumContextCharacters;
            return totalCap > 0 && result.Length > totalCap
                ? result.Substring(0, totalCap).TrimEnd()
                : result;
        }

        private static void Add(List<string> parts, string key, string value, int maximumCharacters)
        {
            int cap = maximumCharacters > 0 ? maximumCharacters : 120;
            string clean = (value ?? string.Empty).Trim()
                .Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', '-');
            if (clean.Length > cap) clean = clean.Substring(0, cap).Trim();
            if (clean.Length > 0) parts.Add(key + "=" + clean);
        }
    }
}
