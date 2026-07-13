// PURE prompt-context formatter for the Rimpsyche bridge.
//
// It selects at most N known nodes above the XML-supplied magnitude floor, resolves each sign to one
// bucketed adjective, and optionally appends already-ranked interest labels. Values are rounded for
// selection but NEVER rendered, so no raw float can leak into an LLM prompt. The schema words
// `psyche=` and `interests=` intentionally remain English machine labels (AGENTS.md localization
// carve-out); adjectives and interest labels are localized by the impure caller.
//
// No Verse/Unity/RimPsyche dependencies: tests/RimpsycheBridgeLogicTests links this file directly.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryRimpsyche.Pure
{
    /// <summary>Deterministic node-vector to one-line prompt-context formatter.</summary>
    public static class PsycheSummaryFormat
    {
        private const string PsycheSchemaKey = "psyche=";
        private const string InterestsSchemaKey = "interests=";

        /// <summary>
        /// Builds `psyche=...; interests=...`, or an empty string when no known node clears the floor.
        /// The resolver receives an adjective Keyed key; blank results use the English fallback table.
        /// </summary>
        public static string Format(
            IEnumerable<PsycheNodeValue> values,
            IEnumerable<string> rankedInterestLabels,
            float magnitudeFloor,
            int maxDescriptors,
            int maxInterests,
            Func<string, string> adjectiveResolver = null)
        {
            if (maxDescriptors <= 0)
            {
                return string.Empty;
            }

            if (float.IsNaN(magnitudeFloor) || float.IsInfinity(magnitudeFloor))
            {
                magnitudeFloor = 0.35f;
            }

            magnitudeFloor = Math.Max(0f, Math.Min(1f, magnitudeFloor));
            Dictionary<string, float> lookup = BuildValueLookup(values);
            List<DescriptorCandidate> candidates = new List<DescriptorCandidate>();
            IReadOnlyList<PsycheNodeDefinition> definitions = PsycheLensMapping.Definitions;

            for (int i = 0; i < definitions.Count; i++)
            {
                PsycheNodeDefinition definition = definitions[i];
                float raw;
                if (!lookup.TryGetValue(definition.DefName, out raw))
                {
                    continue;
                }

                int rounded = PsycheLensMapping.RoundedHundredths(raw);
                float magnitude = Math.Abs(rounded) / 100f;
                // The plan says "above" the 0.35 floor, so equality is deliberately excluded.
                if (!(magnitude > magnitudeFloor))
                {
                    continue;
                }

                candidates.Add(new DescriptorCandidate
                {
                    Definition = definition,
                    DefinitionIndex = i,
                    MagnitudeHundredths = Math.Abs(rounded),
                    Positive = rounded > 0
                });
            }

            candidates.Sort(delegate(DescriptorCandidate left, DescriptorCandidate right)
            {
                int magnitude = right.MagnitudeHundredths.CompareTo(left.MagnitudeHundredths);
                return magnitude != 0 ? magnitude : left.DefinitionIndex.CompareTo(right.DefinitionIndex);
            });

            if (candidates.Count == 0)
            {
                return string.Empty;
            }

            List<string> descriptors = new List<string>();
            int descriptorLimit = Math.Min(maxDescriptors, candidates.Count);
            for (int i = 0; i < descriptorLimit; i++)
            {
                DescriptorCandidate candidate = candidates[i];
                string fallback = candidate.Definition.EnglishAdjective(candidate.Positive);
                string resolved = adjectiveResolver != null
                    ? adjectiveResolver(candidate.Definition.AdjectiveKey(candidate.Positive))
                    : null;
                string adjective = Clean(string.IsNullOrWhiteSpace(resolved) ? fallback : resolved);
                if (adjective.Length > 0)
                {
                    descriptors.Add(adjective);
                }
            }

            if (descriptors.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder line = new StringBuilder(PsycheSchemaKey);
            line.Append(string.Join(", ", descriptors.ToArray()));

            List<string> interests = CleanInterests(rankedInterestLabels, maxInterests);
            if (interests.Count > 0)
            {
                line.Append("; ").Append(InterestsSchemaKey)
                    .Append(string.Join(", ", interests.ToArray()));
            }

            return line.ToString();
        }

        /// <summary>Builds the compact, schema-labelled input used by the optional LLM rewrite.</summary>
        public static string BuildTransformInput(string psycheSummary, string baseOutlook)
        {
            string summary = Clean(psycheSummary);
            string outlook = Clean(baseOutlook);
            if (summary.Length == 0 && outlook.Length == 0)
            {
                return null;
            }

            List<string> lines = new List<string>();
            if (summary.Length > 0)
            {
                lines.Add(summary);
            }
            if (outlook.Length > 0)
            {
                lines.Add("base outlook: " + outlook);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static Dictionary<string, float> BuildValueLookup(IEnumerable<PsycheNodeValue> values)
        {
            Dictionary<string, float> lookup = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
            {
                return lookup;
            }

            foreach (PsycheNodeValue value in values)
            {
                if (!string.IsNullOrWhiteSpace(value.DefName))
                {
                    lookup[value.DefName.Trim()] = value.Value;
                }
            }

            return lookup;
        }

        private static List<string> CleanInterests(IEnumerable<string> labels, int maxInterests)
        {
            List<string> result = new List<string>();
            if (labels == null || maxInterests <= 0)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string label in labels)
            {
                string cleaned = Clean(label);
                if (cleaned.Length == 0 || !seen.Add(cleaned))
                {
                    continue;
                }

                result.Add(cleaned);
                if (result.Count >= maxInterests)
                {
                    break;
                }
            }

            return result;
        }

        // Provider output is one line. Collapse control whitespace and replace semicolons so an
        // embedded label cannot forge another key=value field inside our schema line.
        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(value.Length);
            bool previousWhitespace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == ';')
                {
                    character = ',';
                }

                if (char.IsWhiteSpace(character) || char.IsControl(character))
                {
                    if (!previousWhitespace && result.Length > 0)
                    {
                        result.Append(' ');
                    }

                    previousWhitespace = true;
                    continue;
                }

                result.Append(character);
                previousWhitespace = false;
            }

            return result.ToString().Trim();
        }

        private sealed class DescriptorCandidate
        {
            public PsycheNodeDefinition Definition;
            public int DefinitionIndex;
            public int MagnitudeHundredths;
            public bool Positive;
        }
    }
}
