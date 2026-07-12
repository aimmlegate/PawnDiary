// PURE Rimpsyche personality-to-outlook mapping. No Verse, Unity, Harmony, or RimPsyche types live
// here, so tests/RimpsycheBridgeLogicTests can exercise every decision without booting the game.
//
// Rimpsyche exposes 34 signed personality nodes. This helper gives each known node:
//   * one of six broad behavioral families used by the diary outlook;
//   * a polarity that aligns mixed axes inside a family (for example, high SelfInterest points away
//     from the pro-social side of the Moral family);
//   * a localized adjective key plus an English fallback for each raw sign.
//
// The dominant outlook uses the two largest rounded magnitudes from DIFFERENT families. Rounding to
// two decimals supplies the requested hysteresis: tiny floating-point jitter neither changes the
// selected pair nor causes a new runtime hash. Missing/renamed nodes and unknown future nodes are
// ignored by defName rather than indexed by position.
//
// New to C#? This is like a TypeScript data table plus deterministic pure selector: input DTOs in,
// plain DTO/text out, and no game state touched. See AGENTS.md / SKILL.md.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PawnDiaryRimpsyche.Pure
{
    /// <summary>The six coarse behavioral families used to build a two-part outlook rule.</summary>
    public enum PsycheFamily
    {
        Social,
        Mind,
        Drive,
        Emotion,
        Moral,
        Order
    }

    /// <summary>One plain defName/value pair read at the impure Rimpsyche edge.</summary>
    public readonly struct PsycheNodeValue
    {
        public PsycheNodeValue(string defName, float value)
        {
            DefName = defName;
            Value = value;
        }

        /// <summary>Rimpsyche PersonalityDef defName; unknown names are safely ignored.</summary>
        public string DefName { get; }

        /// <summary>Signed Rimpsyche value. It is selected/bucketed here and never formatted raw.</summary>
        public float Value { get; }
    }

    /// <summary>Static metadata for one known Rimpsyche personality node.</summary>
    public sealed class PsycheNodeDefinition
    {
        internal PsycheNodeDefinition(
            string defName,
            PsycheFamily family,
            int lensPolarity,
            string highAdjective,
            string lowAdjective)
        {
            DefName = defName;
            Family = family;
            LensPolarity = lensPolarity < 0 ? -1 : 1;
            HighAdjective = highAdjective;
            LowAdjective = lowAdjective;
        }

        /// <summary>Version-tolerant identity used for lookup, never a list index contract.</summary>
        public string DefName { get; }

        /// <summary>Broad family used by dominant-pair selection.</summary>
        public PsycheFamily Family { get; }

        /// <summary>
        /// +1 when the node's positive sign points toward the family's positive rule; -1 when the
        /// underlying node is semantically reversed (for example SelfInterest and Stability).
        /// </summary>
        public int LensPolarity { get; }

        /// <summary>English fallback adjective for a raw positive value.</summary>
        public string HighAdjective { get; }

        /// <summary>English fallback adjective for a raw negative value.</summary>
        public string LowAdjective { get; }

        /// <summary>Keyed localization key for this node's raw sign.</summary>
        public string AdjectiveKey(bool positive)
        {
            return PsycheLensMapping.AdjectiveKeyPrefix + DefName + (positive ? ".High" : ".Low");
        }

        /// <summary>English adjective for this node's raw sign.</summary>
        public string EnglishAdjective(bool positive)
        {
            return positive ? HighAdjective : LowAdjective;
        }
    }

    /// <summary>
    /// Selected dominant family pair. It carries only stable enums/signs; the impure caller may
    /// localize the corresponding Keyed rules before composing them.
    /// </summary>
    public sealed class PsycheLensPlan
    {
        internal PsycheLensPlan(
            PsycheFamily primaryFamily,
            bool primaryPositive,
            PsycheFamily secondaryFamily,
            bool secondaryPositive,
            bool hasSecondary)
        {
            PrimaryFamily = primaryFamily;
            PrimaryPositive = primaryPositive;
            SecondaryFamily = secondaryFamily;
            SecondaryPositive = secondaryPositive;
            HasSecondary = hasSecondary;
        }

        public PsycheFamily PrimaryFamily { get; }
        public bool PrimaryPositive { get; }
        public PsycheFamily SecondaryFamily { get; }
        public bool SecondaryPositive { get; }
        public bool HasSecondary { get; }
    }

    /// <summary>Deterministic node-vector selector, family-rule table, and stable vector hash.</summary>
    public static class PsycheLensMapping
    {
        public const string AdjectiveKeyPrefix = "PawnDiaryRimpsyche.Adjective.";
        public const string OutlookKeyPrefix = "PawnDiaryRimpsyche.Outlook.";

        // Verified against installed Rimpsyche v1.0.41 Personalities.xml on 2026-07-12. The table is
        // keyed by defName, so a missing node is skipped and XML list reordering cannot change meaning.
        private static readonly PsycheNodeDefinition[] DefinitionArray =
        {
            Node("Rimpsyche_Talkativeness", PsycheFamily.Social, 1, "outspoken", "taciturn"),
            Node("Rimpsyche_Sociability", PsycheFamily.Social, 1, "friendly", "reserved"),
            Node("Rimpsyche_Tact", PsycheFamily.Social, 1, "diplomatic", "brash"),
            Node("Rimpsyche_Playfulness", PsycheFamily.Social, 1, "playful", "serious"),

            Node("Rimpsyche_Openness", PsycheFamily.Mind, 1, "open-minded", "traditional"),
            Node("Rimpsyche_Inquisitiveness", PsycheFamily.Mind, 1, "inquisitive", "indifferent"),
            Node("Rimpsyche_Imagination", PsycheFamily.Mind, 1, "imaginative", "grounded"),
            Node("Rimpsyche_Reflectiveness", PsycheFamily.Mind, 1, "contemplative", "reactive"),
            Node("Rimpsyche_Experimentation", PsycheFamily.Mind, 1, "experimental", "conventional"),

            Node("Rimpsyche_Confidence", PsycheFamily.Drive, 1, "confident", "insecure"),
            Node("Rimpsyche_Bravery", PsycheFamily.Drive, 1, "courageous", "fearful"),
            Node("Rimpsyche_Diligence", PsycheFamily.Drive, 1, "diligent", "lazy"),
            Node("Rimpsyche_Passion", PsycheFamily.Drive, 1, "passionate", "apathetic"),
            Node("Rimpsyche_Ambition", PsycheFamily.Drive, 1, "ambitious", "content"),
            Node("Rimpsyche_Tenacity", PsycheFamily.Drive, 1, "tenacious", "fragile"),
            Node("Rimpsyche_Expectation", PsycheFamily.Drive, 1, "demanding", "spartan"),
            Node("Rimpsyche_Competitiveness", PsycheFamily.Drive, 1, "competitive", "cooperative"),

            Node("Rimpsyche_Tension", PsycheFamily.Emotion, 1, "high-strung", "laid-back"),
            Node("Rimpsyche_Emotionality", PsycheFamily.Emotion, 1, "emotional", "phlegmatic"),
            Node("Rimpsyche_Stability", PsycheFamily.Emotion, -1, "stable", "unstable"),
            Node("Rimpsyche_Spontaneity", PsycheFamily.Emotion, 1, "impulsive", "consistent"),
            Node("Rimpsyche_Optimism", PsycheFamily.Emotion, -1, "optimistic", "pessimistic"),

            Node("Rimpsyche_Morality", PsycheFamily.Moral, 1, "principled", "amoral"),
            Node("Rimpsyche_Compassion", PsycheFamily.Moral, 1, "compassionate", "coldhearted"),
            Node("Rimpsyche_Trust", PsycheFamily.Moral, 1, "trusting", "skeptical"),
            Node("Rimpsyche_Loyalty", PsycheFamily.Moral, 1, "loyal", "disloyal"),
            Node("Rimpsyche_Authenticity", PsycheFamily.Moral, 1, "authentic", "superficial"),
            Node("Rimpsyche_SelfInterest", PsycheFamily.Moral, -1, "egocentric", "altruistic"),
            Node("Rimpsyche_Propriety", PsycheFamily.Moral, 1, "modest", "uninhibited"),
            Node("Rimpsyche_Aggressiveness", PsycheFamily.Moral, -1, "aggressive", "gentle"),

            Node("Rimpsyche_Organization", PsycheFamily.Order, 1, "organized", "haphazard"),
            Node("Rimpsyche_Discipline", PsycheFamily.Order, 1, "disciplined", "undisciplined"),
            Node("Rimpsyche_Focus", PsycheFamily.Order, 1, "focused", "eclectic"),
            Node("Rimpsyche_Deliberation", PsycheFamily.Order, 1, "deliberate", "hasty"),
        };

        private static readonly ReadOnlyCollection<PsycheNodeDefinition> ReadOnlyDefinitions =
            Array.AsReadOnly(DefinitionArray);

        /// <summary>Canonical known-node metadata, in deterministic tie-break/hash order.</summary>
        public static IReadOnlyList<PsycheNodeDefinition> Definitions => ReadOnlyDefinitions;

        /// <summary>
        /// Picks the largest rounded-magnitude node, then the largest from a different family.
        /// Returns null when every known node rounds to zero.
        /// </summary>
        public static PsycheLensPlan SelectDominantPair(IEnumerable<PsycheNodeValue> values)
        {
            Dictionary<string, float> lookup = BuildValueLookup(values);
            List<Candidate> candidates = new List<Candidate>();

            for (int i = 0; i < DefinitionArray.Length; i++)
            {
                PsycheNodeDefinition definition = DefinitionArray[i];
                float raw;
                if (!lookup.TryGetValue(definition.DefName, out raw))
                {
                    continue;
                }

                int rounded = RoundedHundredths(raw);
                if (rounded == 0)
                {
                    continue;
                }

                candidates.Add(new Candidate
                {
                    Definition = definition,
                    DefinitionIndex = i,
                    Magnitude = Math.Abs(rounded),
                    Positive = rounded * definition.LensPolarity > 0
                });
            }

            candidates.Sort(delegate(Candidate left, Candidate right)
            {
                int magnitude = right.Magnitude.CompareTo(left.Magnitude);
                return magnitude != 0 ? magnitude : left.DefinitionIndex.CompareTo(right.DefinitionIndex);
            });

            if (candidates.Count == 0)
            {
                return null;
            }

            Candidate primary = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                Candidate secondary = candidates[i];
                if (secondary.Definition.Family != primary.Definition.Family)
                {
                    return new PsycheLensPlan(
                        primary.Definition.Family,
                        primary.Positive,
                        secondary.Definition.Family,
                        secondary.Positive,
                        true);
                }
            }

            // A heavily one-family vector still contributes one honest sentence rather than pairing
            // it with a zero-value or invented second axis.
            return new PsycheLensPlan(
                primary.Definition.Family,
                primary.Positive,
                default(PsycheFamily),
                false,
                false);
        }

        /// <summary>
        /// Composes the selected one/two family rules. The optional resolver receives a Keyed key;
        /// blank/missing translations fall back to the English source table below.
        /// </summary>
        public static string ComposeRule(PsycheLensPlan plan, Func<string, string> resolver = null)
        {
            if (plan == null)
            {
                return string.Empty;
            }

            string primary = ResolveRule(plan.PrimaryFamily, plan.PrimaryPositive, resolver);
            if (!plan.HasSecondary)
            {
                return primary;
            }

            string secondary = ResolveRule(plan.SecondaryFamily, plan.SecondaryPositive, resolver);
            if (secondary.Length == 0)
            {
                return primary;
            }

            return primary.Length == 0 ? secondary : primary + " " + secondary;
        }

        /// <summary>Stable Keyed key for one family/sign cell.</summary>
        public static string RuleKeyFor(PsycheFamily family, bool positive)
        {
            return OutlookKeyPrefix + family + (positive ? ".Positive" : ".Negative");
        }

        /// <summary>English source/fallback for one of the 6×2 family/sign cells.</summary>
        public static string EnglishRuleFor(PsycheFamily family, bool positive)
        {
            switch (family)
            {
                case PsycheFamily.Social:
                    return positive
                        ? "This pawn reads a day through the people around them, noticing easy connection and chances to reach out before solitude."
                        : "This pawn keeps their distance and judges company carefully; privacy and room to retreat weigh heavily in how a day feels.";
                case PsycheFamily.Mind:
                    return positive
                        ? "This pawn leans toward questions, possibilities, and meanings beneath the obvious; novelty makes a day feel alive."
                        : "This pawn trusts what is familiar, concrete, and already proven; a good day is one that stays understandable and grounded.";
                case PsycheFamily.Drive:
                    return positive
                        ? "This pawn measures the day by movement and progress, and a challenge matters most when it can be met head-on."
                        : "This pawn resists being hurried toward other people's goals and values room to settle, recover, or leave well enough alone.";
                case PsycheFamily.Emotion:
                    return positive
                        ? "This pawn lets the day's emotional weather decide what matters; strain and sudden feeling leave marks that linger."
                        : "This pawn returns quickly to an even surface and gives passing feelings little authority over the meaning of a day.";
                case PsycheFamily.Moral:
                    return positive
                        ? "This pawn weighs events by who was helped, who kept faith, and whether people acted with decency when it counted."
                        : "This pawn judges first by consequence for themselves and is slow to grant trust, sympathy, or obligation without proof.";
                case PsycheFamily.Order:
                    return positive
                        ? "This pawn feels steadier when duties are clear, details are in place, and the day ends with things properly finished."
                        : "This pawn works by instinct and adaptation, preferring room to improvise over plans, routines, or tidy conclusions.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Deterministic FNV-1a hash of every known node rounded to hundredths. Input ordering and
        /// unknown future node names do not matter; missing known nodes hash as zero.
        /// </summary>
        public static string StableVectorHash(IEnumerable<PsycheNodeValue> values)
        {
            Dictionary<string, float> lookup = BuildValueLookup(values);
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < DefinitionArray.Length; i++)
                {
                    PsycheNodeDefinition definition = DefinitionArray[i];
                    AddStringToHash(ref hash, definition.DefName);
                    float value;
                    int rounded = lookup.TryGetValue(definition.DefName, out value)
                        ? RoundedHundredths(value)
                        : 0;
                    AddIntToHash(ref hash, rounded);
                }
            }

            return hash.ToString("x16");
        }

        /// <summary>
        /// Converts a node value into signed hundredths with midpoint-away-from-zero rounding and a
        /// defensive -1..1 clamp. Exposed so the summary selector uses exactly the hash's hysteresis.
        /// </summary>
        public static int RoundedHundredths(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0;
            }

            int rounded = (int)Math.Round(value * 100f, MidpointRounding.AwayFromZero);
            if (rounded < -100)
            {
                return -100;
            }

            return rounded > 100 ? 100 : rounded;
        }

        private static string ResolveRule(PsycheFamily family, bool positive, Func<string, string> resolver)
        {
            string fallback = EnglishRuleFor(family, positive);
            if (resolver == null)
            {
                return fallback;
            }

            string localized = resolver(RuleKeyFor(family, positive));
            return string.IsNullOrWhiteSpace(localized) ? fallback : localized.Trim();
        }

        private static PsycheNodeDefinition Node(
            string defName,
            PsycheFamily family,
            int lensPolarity,
            string highAdjective,
            string lowAdjective)
        {
            return new PsycheNodeDefinition(defName, family, lensPolarity, highAdjective, lowAdjective);
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
                if (string.IsNullOrWhiteSpace(value.DefName))
                {
                    continue;
                }

                // Last duplicate wins. Runtime vectors are unique; this deterministic rule merely makes
                // malformed test/third-party inputs unsurprising.
                lookup[value.DefName.Trim()] = value.Value;
            }

            return lookup;
        }

        private static void AddStringToHash(ref ulong hash, string value)
        {
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash ^= (byte)character;
                    hash *= 1099511628211UL;
                    hash ^= (byte)(character >> 8);
                    hash *= 1099511628211UL;
                }

                // A delimiter keeps concatenated names unambiguous.
                hash ^= 0xff;
                hash *= 1099511628211UL;
            }
        }

        private static void AddIntToHash(ref ulong hash, int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(bits >> shift);
                    hash *= 1099511628211UL;
                }
            }
        }

        private sealed class Candidate
        {
            public PsycheNodeDefinition Definition;
            public int DefinitionIndex;
            public int Magnitude;
            public bool Positive;
        }
    }
}
