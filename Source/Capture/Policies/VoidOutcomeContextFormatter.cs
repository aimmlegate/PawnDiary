// Pure A3.0 prompt projection. It exposes only the verified terminal branch, the exact actor identity,
// the level actually reached, the terminal marker, and bounded event-time surroundings/actor summary.
// Off-map enemy fates, quest text, hidden mechanics, dialogue, and the actor's unexpressed motives
// have no input field and cannot enter the prompt through this formatter.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Formats bounded, prompt-safe context for one verified terminal void outcome.</summary>
    internal static class VoidOutcomeContextFormatter
    {
        private const int MaximumValueCharacters = 240;

        /// <summary>Returns terminal-outcome context, or empty for an invalid/unverified plan.</summary>
        public static string Format(VoidOutcomeFacts facts, VoidOutcomePlan plan)
        {
            if (facts == null || plan == null || !plan.valid
                || !AnomalyVoidOutcomePolicy.OwnsTerminalTale(plan)) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, AnomalyContextKeys.Kind, AnomalyKindTokens.VoidOutcome);
            Add(parts, AnomalyContextKeys.VoidOutcome, plan.outcomeToken);
            Add(parts, AnomalyContextKeys.Actor, facts.actorLabel);
            Add(parts, AnomalyContextKeys.MonolithLevel, plan.reachedLevelDefName);
            Add(parts, AnomalyContextKeys.Terminal, "true");
            Add(parts, AnomalyContextKeys.ActorSummary, facts.actorSummary);
            Add(parts, AnomalyContextKeys.Setting, facts.setting);
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
