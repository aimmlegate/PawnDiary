// Pure bounded formatter for a resolved belief snapshot. Phase 0 does not attach this block to any
// event or prompt route. Labels below are stable structured prompt-schema labels; all descriptive
// phrases and values come from guarded game facts or XML/DefInjected policy.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Formats Full/Balanced/Compact belief facts without re-reading live doctrine.</summary>
    internal static class BeliefContextFormatter
    {
        /// <summary>Returns an empty string for an empty resolution and otherwise emits complete bounded lines.</summary>
        public static string Format(
            BeliefStanceResolution resolution,
            string detailLevel,
            BeliefPolicySnapshot policy)
        {
            if (resolution == null || !resolution.HasUsefulContext) return string.Empty;
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            BeliefDetailBudget budget = effective.DetailBudget(detailLevel);
            string normalizedDetail = NarrativeDetailLevelTokens.Normalize(detailLevel);
            bool compact = normalizedDetail == NarrativeDetailLevelTokens.Compact;
            int maximumLines = Math.Min(effective.maximumTotalLines, Math.Max(1, budget.maximumLines));
            int maximumCharacters = Math.Min(effective.maximumTotalCharacters, Math.Max(64, budget.maximumCharacters));
            List<string> lines = new List<string>();

            Add(lines, "ideoligion", resolution.ideologyName, effective, maximumLines);
            if (!compact) Add(lines, "role", resolution.roleName, effective, maximumLines);
            if (resolution.hasCertainty)
            {
                string value = Math.Round(resolution.certainty * 100f, MidpointRounding.AwayFromZero)
                    .ToString("0", CultureInfo.InvariantCulture) + "%";
                if (!string.IsNullOrWhiteSpace(resolution.certaintyBand))
                    value += " (" + Clean(resolution.certaintyBand, effective.maximumFieldCharacters) + ")";
                Add(lines, "certainty", value, effective, maximumLines);
                if (resolution.certaintyTrend != BeliefCertaintyTrendTokens.Unknown)
                {
                    string trend = resolution.certaintyTrend;
                    if (resolution.certaintyMagnitude != BeliefCertaintyMagnitudeTokens.Unknown)
                        trend += " (" + resolution.certaintyMagnitude + ")";
                    Add(lines, "certainty trend", trend, effective, maximumLines);
                }
                if (normalizedDetail == NarrativeDetailLevelTokens.Full)
                    Add(lines, "certainty outlook", resolution.certaintyPhrase, effective, maximumLines);
            }

            if (!compact && resolution.mutation != null)
            {
                Add(lines, "previous ideoligion", resolution.mutation.beforeIdeologyName, effective, maximumLines);
                Add(lines, "current ideoligion", resolution.mutation.afterIdeologyName, effective, maximumLines);
                Add(lines, "attempted ideoligion", resolution.mutation.attemptedIdeologyName, effective, maximumLines);
            }

            int stanceCap = compact ? 1 : 2;
            for (int i = 0; i < resolution.stances.Count && i < stanceCap; i++)
            {
                BeliefPreceptFact precept = resolution.stances[i] == null ? null : resolution.stances[i].precept;
                if (precept == null) continue;
                Add(lines, "relevant precept", FirstNonBlank(precept.displayLabel, precept.defName), effective, maximumLines);
                if (budget.includeDescriptions)
                    Add(lines, "precept meaning", WholeWord(precept.description, effective.maximumDescriptionCharacters),
                        effective, maximumLines);
            }

            if (budget.includeMemes)
                for (int i = 0; i < resolution.supportingMemes.Count; i++)
                    Add(lines, "relevant meme", FirstNonBlank(resolution.supportingMemes[i].label,
                        resolution.supportingMemes[i].defName), effective, maximumLines);

            if (budget.includeStructure && resolution.structure != null)
            {
                Add(lines, "structure", FirstNonBlank(resolution.structure.label, resolution.structure.defName),
                    effective, maximumLines);
                if (budget.includeDescriptions)
                    Add(lines, "structure outlook", WholeWord(resolution.structure.description,
                        effective.maximumDescriptionCharacters), effective, maximumLines);
            }

            if (budget.includeDeity && resolution.deity != null)
                Add(lines, "deity", resolution.deity.name, effective, maximumLines);

            return JoinWithinBudget(lines, maximumCharacters);
        }

        /// <summary>Strips rich text/control characters and collapses untrusted multiline text.</summary>
        public static string Clean(string value, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value) || maximumCharacters <= 0) return string.Empty;
            string bounded = value.Length <= maximumCharacters * 2
                ? value
                : value.Substring(0, maximumCharacters * 2);
            StringBuilder builder = new StringBuilder(Math.Min(bounded.Length, maximumCharacters));
            bool insideTag = false;
            bool pendingSpace = false;
            for (int i = 0; i < bounded.Length && builder.Length < maximumCharacters; i++)
            {
                char character = bounded[i];
                if (character == '<')
                {
                    insideTag = true;
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (insideTag)
                {
                    if (character == '>') insideTag = false;
                    continue;
                }
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace && builder.Length < maximumCharacters) builder.Append(' ');
                pendingSpace = false;
                if (builder.Length < maximumCharacters) builder.Append(character == ';' ? ',' : character);
            }
            return builder.ToString().Trim();
        }

        /// <summary>Trims at a whole-word boundary whenever a usable boundary exists.</summary>
        public static string WholeWord(string value, int maximumCharacters)
        {
            string cleaned = Clean(value, maximumCharacters);
            if (string.IsNullOrEmpty(cleaned) || cleaned.Length < maximumCharacters) return cleaned;
            int boundary = cleaned.LastIndexOf(' ');
            return boundary >= maximumCharacters / 2 ? cleaned.Substring(0, boundary).TrimEnd() : cleaned;
        }

        private static void Add(
            List<string> lines,
            string label,
            string value,
            BeliefPolicySnapshot policy,
            int maximumLines)
        {
            if (lines.Count >= maximumLines) return;
            string cleaned = Clean(value, policy.maximumFieldCharacters);
            if (cleaned.Length == 0) return;
            lines.Add(label + ": " + cleaned);
        }

        private static string JoinWithinBudget(List<string> lines, int maximumCharacters)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                int separator = result.Length == 0 ? 0 : 1;
                if (result.Length + separator + line.Length > maximumCharacters) continue;
                if (separator > 0) result.Append('\n');
                result.Append(line);
            }
            return result.ToString();
        }

        private static string FirstNonBlank(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
        }
    }
}
