// Pure bounded formatter for a resolved belief snapshot. The full block is frozen on DiaryEvent at
// capture time; prompt detail presets only filter those saved lines and never re-read live doctrine.
// Labels below are stable structured prompt-schema labels; all descriptive phrases and values come
// from guarded game facts or XML/DefInjected policy.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Formats Full/Balanced/Compact belief facts without re-reading live doctrine.</summary>
    internal static class BeliefContextFormatter
    {
        private static readonly HashSet<string> AllowedLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "ideoligion", "role", "certainty", "certainty trend", "certainty outlook",
            "certainty before", "certainty after", "certainty delta", "conversion result",
            "belief change subject", "mutation cause",
            "previous ideoligion", "current ideoligion", "attempted ideoligion",
            "relevant precept", "precept meaning", "relevant meme", "structure",
            "structure outlook", "deity"
        };

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

            if (resolution.mutation != null)
            {
                BeliefMutationSnapshot mutation = resolution.mutation;
                // Pair/solo converters can receive mechanics observed on the target pawn. Name that
                // subject explicitly before the facts so the POV's own ideology/certainty above cannot
                // be confused with the target's transition below.
                if (!resolution.mutationSubjectIsPov)
                    Add(lines, "belief change subject", resolution.mutationSubjectLabel,
                        effective, maximumLines);
                // Outcome and delta are the most compact mechanical facts, so add them before the
                // longer identity/before/after detail. Balanced/Compact budgets must not keep names
                // while silently dropping whether the conversion succeeded.
                if (mutation.hasBeforeCertainty && mutation.hasAfterCertainty)
                    Add(lines, "certainty delta",
                        FormatDeltaPercent(mutation.afterCertainty - mutation.beforeCertainty),
                        effective, maximumLines);
                if (mutation.conversionSucceeded.HasValue)
                    Add(lines, "conversion result",
                        mutation.conversionSucceeded.Value ? "success" : "failure",
                        effective, maximumLines);
                if (!compact)
                {
                    Add(lines, "previous ideoligion", mutation.beforeIdeologyName, effective, maximumLines);
                    Add(lines, "current ideoligion", mutation.afterIdeologyName, effective, maximumLines);
                    Add(lines, "attempted ideoligion", mutation.attemptedIdeologyName, effective, maximumLines);
                    if (mutation.hasBeforeCertainty)
                        Add(lines, "certainty before", FormatPercent(mutation.beforeCertainty),
                            effective, maximumLines);
                    if (mutation.hasAfterCertainty)
                        Add(lines, "certainty after", FormatPercent(mutation.afterCertainty),
                            effective, maximumLines);
                }
                if (normalizedDetail == NarrativeDetailLevelTokens.Full)
                    Add(lines, "mutation cause", JoinCauseTokens(mutation.causeTokens),
                        effective, maximumLines);
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

        /// <summary>
        /// Re-sanitizes a persisted full block with the same line/character budgets used at capture.
        /// Old saves return empty, malformed labels fail closed, and already-normalized blocks remain
        /// byte-identical across a save/load round trip.
        /// </summary>
        public static string NormalizeSaved(string saved, BeliefPolicySnapshot policy = null)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            return FilterSaved(saved, NarrativeDetailLevelTokens.Full, effective, normalizeOnly: true);
        }

        /// <summary>
        /// Projects Full/Balanced/Compact from the event-time saved block. It never reconstructs or
        /// reevaluates a belief and never truncates a line mid-fact.
        /// </summary>
        public static string ForDetail(
            string saved,
            string detailLevel,
            BeliefPolicySnapshot policy = null)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            return FilterSaved(saved, NarrativeDetailLevelTokens.Normalize(detailLevel), effective,
                normalizeOnly: false);
        }

        /// <summary>Strips rich text/control characters and collapses untrusted multiline text.</summary>
        public static string Clean(string value, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value) || maximumCharacters <= 0) return string.Empty;
            string bounded = value.Length <= maximumCharacters * 2
                ? value
                : value.Substring(0, maximumCharacters * 2);
            StringBuilder builder = new StringBuilder(Math.Min(bounded.Length, maximumCharacters));
            bool pendingSpace = false;
            for (int i = 0; i < bounded.Length && builder.Length < maximumCharacters; i++)
            {
                char character = bounded[i];
                if (character == '<')
                {
                    pendingSpace = builder.Length > 0;
                    int closingBracket = bounded.IndexOf('>', i + 1);
                    // Strip a complete markup-looking span. With an unmatched '<', discard only the
                    // unsafe delimiter and keep sanitizing the ordinary text that follows it.
                    if (closingBracket >= 0) i = closingBracket;
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

        private static string FilterSaved(
            string saved,
            string detailLevel,
            BeliefPolicySnapshot policy,
            bool normalizeOnly)
        {
            if (string.IsNullOrWhiteSpace(saved)) return string.Empty;
            BeliefDetailBudget budget = policy.DetailBudget(detailLevel);
            int maximumLines = Math.Min(policy.maximumTotalLines, Math.Max(1, budget.maximumLines));
            int maximumCharacters = Math.Min(policy.maximumTotalCharacters,
                Math.Max(64, budget.maximumCharacters));
            List<string> kept = new List<string>();
            string[] rows = saved.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int stanceCount = 0;
            for (int i = 0; i < rows.Length && i < 64 && kept.Count < maximumLines; i++)
            {
                string row = rows[i] ?? string.Empty;
                int separator = row.IndexOf(':');
                if (separator <= 0) continue;
                string label = Clean(row.Substring(0, separator), 40).ToLowerInvariant();
                if (!AllowedLabels.Contains(label)) continue;
                if (!normalizeOnly && !AllowedForDetail(label, detailLevel, budget, ref stanceCount))
                    continue;
                string value = Clean(row.Substring(separator + 1), policy.maximumFieldCharacters);
                if (value.Length == 0) continue;
                kept.Add(label + ": " + value);
            }
            return JoinWithinBudget(kept, maximumCharacters);
        }

        private static bool AllowedForDetail(
            string label,
            string detailLevel,
            BeliefDetailBudget budget,
            ref int stanceCount)
        {
            if (label == "precept meaning" && !budget.includeDescriptions) return false;
            if (label == "structure outlook" && !budget.includeDescriptions) return false;
            if ((label == "structure" || label == "structure outlook") && !budget.includeStructure)
                return false;
            if (label == "relevant meme" && !budget.includeMemes) return false;
            if (label == "deity" && !budget.includeDeity) return false;
            if (label == "mutation cause" && detailLevel != NarrativeDetailLevelTokens.Full)
                return false;

            if (detailLevel == NarrativeDetailLevelTokens.Compact)
            {
                if (label == "role" || label == "certainty outlook"
                    || label == "previous ideoligion" || label == "current ideoligion"
                    || label == "attempted ideoligion" || label == "certainty before"
                    || label == "certainty after") return false;
                if (label == "relevant precept")
                {
                    stanceCount++;
                    return stanceCount == 1;
                }
            }
            return true;
        }

        private static string FormatPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return string.Empty;
            float clamped = Math.Max(0f, Math.Min(1f, value));
            return Math.Round(clamped * 100f, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatDeltaPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return string.Empty;
            float clamped = Math.Max(-1f, Math.Min(1f, value));
            return Math.Round(clamped * 100f, MidpointRounding.AwayFromZero)
                .ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%";
        }

        private static string JoinCauseTokens(IList<string> values)
        {
            if (values == null) return string.Empty;
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < values.Count && i < 16; i++)
            {
                string token = values[i];
                if (!BeliefMutationCauseTokens.IsKnown(token)) continue;
                if (result.Length > 0) result.Append(',');
                result.Append(token);
            }
            return result.ToString();
        }

        private static string FirstNonBlank(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
        }
    }

    /// <summary>Stable prompt source and localized-instruction composition for saved belief context.</summary>
    internal static class BeliefContextPrompt
    {
        public const string Source = "BeliefContext";

        /// <summary>Filters one saved block for the requested detail preset, then prefixes guidance.</summary>
        public static string Compose(
            string savedContext,
            string detailLevel,
            BeliefPolicySnapshot policy,
            string instruction)
        {
            string context = BeliefContextFormatter.ForDetail(savedContext, detailLevel, policy);
            if (string.IsNullOrWhiteSpace(context)) return string.Empty;
            if (string.IsNullOrWhiteSpace(instruction)) return context;
            return instruction.Trim() + "\n" + context;
        }
    }
}
