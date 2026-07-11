// Pure trait -> psychotype weighting data for the psychotype roll. Traits used to feed the roll only
// as two vetoes (Psychopath never rolls Dependent, Kind never rolls Ruthless); this table adds the
// deliberate second trait channel on top: each supported trait nudges the roll toward the psychotypes
// it is compatible with, and the three EXTREME traits (Psychopath / Cannibal / Bloodlust) each unlock
// a psychotype of their own that no other pawn can roll (the def's requiredTrait gate).
//
// Two kinds of trait input, both additive on top of the skill-passion signals — and deliberately
// SCALED ABOVE them, so a trait's pull dominates the profile's (a Sanguine pawn leans Content even
// when their passions say otherwise; the passions still break ties and colour the rest):
//   * Family bonuses  - stage-1 pull toward the family holding the compatible members, sized to
//                       outweigh the profile signals (zero-passion inward +4, creepjoiner +4).
//   * Member bonuses  - stage-2 additive weight for specific defNames, sized to outweigh the combo
//                       signatures (+2) and skill nudges (1-4).
// Gated psychotypes additionally ride the roll policy's takeover branch (GatedTakeoverChance): the
// trait's owner adopts the gated psychotype outright almost half the time, and the large member
// bonus (+6) keeps it favored in the normal roll too — but the skill profile, jitter, and duplicate
// penalty still get a vote, so the outcome is dominant, not guaranteed.
//
// Trait identity is a CANONICAL KEY, not a raw defName: spectrum traits (NaturalMood, Nerves,
// Neurotic) map each degree to its own key ("Depressive" vs "Pessimist"), simple traits map to their
// defName. The impure adapter (Source/Generation/PsychotypeRolls.cs) calls CanonicalTraitKey per pawn
// trait; everything in this file is plain C# and unit-tested in DiaryPipelineTests.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Pure trait-to-psychotype weighting table: canonical trait keys, per-family and per-member
    /// weight bonuses, and the unlock check for trait-gated psychotypes. See file header.
    /// </summary>
    internal static class PsychotypeTraitAffinities
    {
        // ---- Canonical trait keys (simple traits use their vanilla defName) ----
        public const string KeyPsychopath = "Psychopath";
        public const string KeyCannibal = "Cannibal";
        public const string KeyBloodlust = "Bloodlust";
        public const string KeyJealous = "Jealous";
        public const string KeyGreedy = "Greedy";
        public const string KeyTooSmart = "TooSmart";
        public const string KeyTorturedArtist = "TorturedArtist";
        public const string KeyKind = "Kind";
        public const string KeyAbrasive = "Abrasive";
        public const string KeyRecluse = "Recluse";
        // NaturalMood spectrum degrees.
        public const string KeyDepressive = "Depressive";
        public const string KeyPessimist = "Pessimist";
        public const string KeyOptimist = "Optimist";
        public const string KeySanguine = "Sanguine";
        // Nerves spectrum degrees (negative side only; the calm side carries no diary lean).
        public const string KeyNervous = "Nervous";
        public const string KeyVolatile = "Volatile";
        // Neurotic spectrum degrees.
        public const string KeyNeurotic = "Neurotic";
        public const string KeyVeryNeurotic = "VeryNeurotic";

        // ---- Spectrum trait defNames the canonical mapping understands ----
        private const string TraitNaturalMood = "NaturalMood";
        private const string TraitNerves = "Nerves";
        private const string TraitNeurotic = "Neurotic";

        // ---- Bonus scale (deliberately above the roll policy's signal scale; see file header) ----
        public const float GatedMemberBonus = 6f;
        public const float StrongBonus = 6f;
        public const float ModerateBonus = 4f;
        public const float MildBonus = 2f;

        // ---- Target defNames (adult catalog + the three trait-gated additions) ----
        private const string DefContent = "DiaryPsychotype_Content";
        private const string DefAmbitious = "DiaryPsychotype_Ambitious";
        private const string DefDutiful = "DiaryPsychotype_Dutiful";
        private const string DefNostalgic = "DiaryPsychotype_Nostalgic";
        private const string DefPragmatic = "DiaryPsychotype_Pragmatic";
        private const string DefWry = "DiaryPsychotype_Wry";
        private const string DefParanoid = "DiaryPsychotype_Paranoid";
        private const string DefDetached = "DiaryPsychotype_Detached";
        private const string DefRuthless = "DiaryPsychotype_Ruthless";
        private const string DefVolatile = "DiaryPsychotype_Volatile";
        private const string DefTheatrical = "DiaryPsychotype_Theatrical";
        private const string DefNarcissistic = "DiaryPsychotype_Narcissistic";
        private const string DefResentful = "DiaryPsychotype_Resentful";
        private const string DefAvoidant = "DiaryPsychotype_Avoidant";
        private const string DefDependent = "DiaryPsychotype_Dependent";
        private const string DefPerfectionist = "DiaryPsychotype_Perfectionist";
        private const string DefHollow = "DiaryPsychotype_Hollow";
        private const string DefRavenous = "DiaryPsychotype_Ravenous";
        private const string DefBloodthirsty = "DiaryPsychotype_Bloodthirsty";

        /// <summary>One trait's pull on the roll: stage-1 family bonuses and stage-2 member bonuses.</summary>
        private sealed class TraitAffinity
        {
            public Dictionary<string, float> familyBonuses = new Dictionary<string, float>(StringComparer.Ordinal);
            public Dictionary<string, float> memberBonuses = new Dictionary<string, float>(StringComparer.Ordinal);
        }

        // The whole trait table. Key = canonical trait key. Kept as data in one place so retuning a
        // trait's pull never touches the roll algorithm.
        private static readonly Dictionary<string, TraitAffinity> Rules = BuildRules();

        private static Dictionary<string, TraitAffinity> BuildRules()
        {
            Dictionary<string, TraitAffinity> rules = new Dictionary<string, TraitAffinity>(StringComparer.Ordinal);

            void Rule(string key, (string family, float bonus)[] families, (string def, float bonus)[] members)
            {
                TraitAffinity affinity = new TraitAffinity();
                for (int i = 0; i < families.Length; i++)
                {
                    affinity.familyBonuses[families[i].family] = families[i].bonus;
                }

                for (int i = 0; i < members.Length; i++)
                {
                    affinity.memberBonuses[members[i].def] = members[i].bonus;
                }

                rules[key] = affinity;
            }

            // Extreme traits: unlock + dominate their gated psychotype, with a spillover toward the
            // closest existing members so even a non-gated outcome stays in character.
            Rule(KeyPsychopath,
                new[] { (PsychotypeRollPolicy.FamilyIntense, StrongBonus) },
                new[] { (DefHollow, GatedMemberBonus), (DefRuthless, ModerateBonus), (DefDetached, MildBonus) });
            Rule(KeyCannibal,
                new[] { (PsychotypeRollPolicy.FamilyIntense, ModerateBonus) },
                new[] { (DefRavenous, GatedMemberBonus), (DefRuthless, MildBonus) });
            Rule(KeyBloodlust,
                new[] { (PsychotypeRollPolicy.FamilyIntense, ModerateBonus) },
                new[] { (DefBloodthirsty, GatedMemberBonus), (DefVolatile, MildBonus) });

            // Non-extreme traits: weight toward the compatible members of the existing catalog.
            Rule(KeyJealous,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, ModerateBonus), (PsychotypeRollPolicy.FamilyIntense, MildBonus) },
                new[] { (DefResentful, StrongBonus), (DefNarcissistic, ModerateBonus) });
            Rule(KeyGreedy,
                new[] { (PsychotypeRollPolicy.FamilyIntense, MildBonus) },
                new[] { (DefRuthless, ModerateBonus), (DefAmbitious, ModerateBonus) });
            Rule(KeyTooSmart,
                new[] { (PsychotypeRollPolicy.FamilyInward, ModerateBonus) },
                new[] { (DefDetached, StrongBonus), (DefWry, MildBonus) });
            Rule(KeyTorturedArtist,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, MildBonus), (PsychotypeRollPolicy.FamilyIntense, MildBonus) },
                new[] { (DefResentful, ModerateBonus), (DefNostalgic, ModerateBonus), (DefTheatrical, MildBonus) });
            Rule(KeyKind,
                new[] { (PsychotypeRollPolicy.FamilyGrounded, ModerateBonus) },
                new[] { (DefContent, ModerateBonus), (DefDutiful, ModerateBonus) });
            Rule(KeyAbrasive,
                new (string, float)[0],
                new[] { (DefWry, ModerateBonus), (DefPragmatic, MildBonus), (DefResentful, MildBonus) });
            Rule(KeyRecluse,
                new[] { (PsychotypeRollPolicy.FamilyInward, ModerateBonus), (PsychotypeRollPolicy.FamilyAnxious, MildBonus) },
                new[] { (DefDetached, StrongBonus), (DefAvoidant, ModerateBonus) });
            Rule(KeyDepressive,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, ModerateBonus), (PsychotypeRollPolicy.FamilyInward, MildBonus) },
                new[] { (DefNostalgic, ModerateBonus), (DefAvoidant, ModerateBonus), (DefResentful, MildBonus) });
            Rule(KeyPessimist,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, MildBonus) },
                new[] { (DefResentful, MildBonus), (DefParanoid, MildBonus), (DefWry, MildBonus) });
            Rule(KeySanguine,
                new[] { (PsychotypeRollPolicy.FamilyGrounded, StrongBonus) },
                new[] { (DefContent, StrongBonus), (DefWry, MildBonus) });
            Rule(KeyOptimist,
                new[] { (PsychotypeRollPolicy.FamilyGrounded, ModerateBonus) },
                new[] { (DefContent, ModerateBonus), (DefAmbitious, MildBonus) });
            Rule(KeyNervous,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, ModerateBonus) },
                new[] { (DefAvoidant, ModerateBonus), (DefDependent, MildBonus) });
            Rule(KeyVolatile,
                new[] { (PsychotypeRollPolicy.FamilyIntense, ModerateBonus) },
                new[] { (DefVolatile, StrongBonus), (DefTheatrical, MildBonus) });
            Rule(KeyNeurotic,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, ModerateBonus) },
                new[] { (DefPerfectionist, ModerateBonus), (DefParanoid, MildBonus) });
            Rule(KeyVeryNeurotic,
                new[] { (PsychotypeRollPolicy.FamilyAnxious, StrongBonus) },
                new[] { (DefPerfectionist, StrongBonus), (DefParanoid, ModerateBonus), (DefDependent, MildBonus) });

            return rules;
        }

        /// <summary>
        /// Maps a trait defName + degree to its canonical key, or empty when the trait carries no
        /// psychotype pull. Spectrum traits key each mapped degree separately; simple traits ignore
        /// the degree. Unknown traits (including modded ones) return empty and contribute nothing.
        /// </summary>
        public static string CanonicalTraitKey(string traitDefName, int degree)
        {
            if (string.IsNullOrWhiteSpace(traitDefName))
            {
                return string.Empty;
            }

            switch (traitDefName.Trim())
            {
                case TraitNaturalMood:
                    switch (degree)
                    {
                        case -2: return KeyDepressive;
                        case -1: return KeyPessimist;
                        case 1: return KeyOptimist;
                        case 2: return KeySanguine;
                        default: return string.Empty;
                    }

                case TraitNerves:
                    switch (degree)
                    {
                        case -1: return KeyNervous;
                        case -2: return KeyVolatile;
                        default: return string.Empty;
                    }

                case TraitNeurotic:
                    switch (degree)
                    {
                        case 1: return KeyNeurotic;
                        case 2: return KeyVeryNeurotic;
                        default: return string.Empty;
                    }

                case KeyPsychopath:
                case KeyCannibal:
                case KeyBloodlust:
                case KeyJealous:
                case KeyGreedy:
                case KeyTooSmart:
                case KeyTorturedArtist:
                case KeyKind:
                case KeyAbrasive:
                case KeyRecluse:
                    return traitDefName.Trim();

                default:
                    return string.Empty;
            }
        }

        /// <summary>Total stage-1 family bonus contributed by the pawn's trait keys for one family.</summary>
        public static float FamilyBonus(string family, IReadOnlyList<string> traitKeys)
        {
            if (string.IsNullOrEmpty(family) || traitKeys == null || traitKeys.Count == 0)
            {
                return 0f;
            }

            float bonus = 0f;
            for (int i = 0; i < traitKeys.Count; i++)
            {
                if (traitKeys[i] != null
                    && Rules.TryGetValue(traitKeys[i], out TraitAffinity affinity)
                    && affinity.familyBonuses.TryGetValue(family, out float value))
                {
                    bonus += value;
                }
            }

            return bonus;
        }

        /// <summary>Total stage-2 member bonus contributed by the pawn's trait keys for one psychotype.</summary>
        public static float MemberBonus(string defName, IReadOnlyList<string> traitKeys)
        {
            if (string.IsNullOrEmpty(defName) || traitKeys == null || traitKeys.Count == 0)
            {
                return 0f;
            }

            float bonus = 0f;
            for (int i = 0; i < traitKeys.Count; i++)
            {
                if (traitKeys[i] != null
                    && Rules.TryGetValue(traitKeys[i], out TraitAffinity affinity)
                    && affinity.memberBonuses.TryGetValue(defName, out float value))
                {
                    bonus += value;
                }
            }

            return bonus;
        }

        /// <summary>
        /// Whether a candidate whose def declares <paramref name="requiredTraitKey"/> is rollable for
        /// a pawn holding <paramref name="traitKeys"/>. A blank requirement is always unlocked; a
        /// non-blank one is a hard eligibility gate (it applies on every roll branch, wildcard and
        /// flat included, exactly like the trait vetoes).
        /// </summary>
        public static bool IsUnlocked(string requiredTraitKey, IReadOnlyList<string> traitKeys)
        {
            if (string.IsNullOrWhiteSpace(requiredTraitKey))
            {
                return true;
            }

            if (traitKeys == null)
            {
                return false;
            }

            for (int i = 0; i < traitKeys.Count; i++)
            {
                if (string.Equals(traitKeys[i], requiredTraitKey.Trim(), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
