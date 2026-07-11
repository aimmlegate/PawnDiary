// Pure trait-to-psychotype policy. The tunable mapping and bonuses are authored in XML and projected
// by Source/Defs/DiaryPsychotypeTraitPolicyDef.cs into the plain DTOs below. Keeping the algorithm in
// this file free of Verse makes canonicalization and weight arithmetic independently testable.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One XML-authored trait mapping after it has crossed into the pure pipeline.</summary>
    internal sealed class PsychotypeTraitAffinityRule
    {
        public string traitDefName = string.Empty;
        public bool matchDegree;
        public int degree;
        public string key = string.Empty;
        public Dictionary<string, float> familyBonuses = new Dictionary<string, float>(StringComparer.Ordinal);
        public Dictionary<string, float> memberBonuses = new Dictionary<string, float>(StringComparer.Ordinal);
    }

    /// <summary>Plain snapshot of all trait-affinity tuning needed by one psychotype roll.</summary>
    internal sealed class PsychotypeTraitAffinityPolicy
    {
        // Defensive fallback for a missing/malformed Def; the shipped value is owned by XML.
        public float gatedTakeoverChance = 0.45f;
        public List<PsychotypeTraitAffinityRule> rules = new List<PsychotypeTraitAffinityRule>();
    }

    /// <summary>Pure lookups and arithmetic over an XML-projected trait-affinity policy.</summary>
    internal static class PsychotypeTraitAffinities
    {
        // Stable canonical keys are also referenced by psychotype Def gates and save-independent tests.
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
        public const string KeyDepressive = "Depressive";
        public const string KeyPessimist = "Pessimist";
        public const string KeyOptimist = "Optimist";
        public const string KeySanguine = "Sanguine";
        public const string KeyNervous = "Nervous";
        public const string KeyVolatile = "Volatile";
        public const string KeyNeurotic = "Neurotic";
        public const string KeyVeryNeurotic = "VeryNeurotic";

        /// <summary>
        /// Maps a trait defName and degree to the first matching XML rule's canonical key. Rules that
        /// set matchDegree=false accept every degree; unknown traits contribute no psychotype pull.
        /// </summary>
        public static string CanonicalTraitKey(string traitDefName, int degree,
            PsychotypeTraitAffinityPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(traitDefName) || policy?.rules == null)
            {
                return string.Empty;
            }

            string normalized = traitDefName.Trim();
            for (int i = 0; i < policy.rules.Count; i++)
            {
                PsychotypeTraitAffinityRule rule = policy.rules[i];
                if (rule != null
                    && string.Equals(rule.traitDefName?.Trim(), normalized, StringComparison.Ordinal)
                    && (!rule.matchDegree || rule.degree == degree))
                {
                    return rule.key?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>Total stage-one family bonus contributed by the pawn's canonical trait keys.</summary>
        public static float FamilyBonus(string family, IReadOnlyList<string> traitKeys,
            PsychotypeTraitAffinityPolicy policy)
        {
            return SumBonus(family, traitKeys, policy, familyBonus: true);
        }

        /// <summary>Total stage-two member bonus contributed by the pawn's canonical trait keys.</summary>
        public static float MemberBonus(string defName, IReadOnlyList<string> traitKeys,
            PsychotypeTraitAffinityPolicy policy)
        {
            return SumBonus(defName, traitKeys, policy, familyBonus: false);
        }

        private static float SumBonus(string target, IReadOnlyList<string> traitKeys,
            PsychotypeTraitAffinityPolicy policy, bool familyBonus)
        {
            if (string.IsNullOrEmpty(target) || traitKeys == null || policy?.rules == null)
            {
                return 0f;
            }

            float total = 0f;
            for (int traitIndex = 0; traitIndex < traitKeys.Count; traitIndex++)
            {
                string key = traitKeys[traitIndex];
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                for (int ruleIndex = 0; ruleIndex < policy.rules.Count; ruleIndex++)
                {
                    PsychotypeTraitAffinityRule rule = policy.rules[ruleIndex];
                    if (rule == null || !string.Equals(rule.key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Dictionary<string, float> bonuses = familyBonus ? rule.familyBonuses : rule.memberBonuses;
                    if (bonuses != null && bonuses.TryGetValue(target, out float value))
                    {
                        total += value;
                    }
                }
            }

            return total;
        }

        /// <summary>Whether the pawn's canonical traits satisfy a candidate's optional trait gate.</summary>
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

            string required = requiredTraitKey.Trim();
            for (int i = 0; i < traitKeys.Count; i++)
            {
                if (string.Equals(traitKeys[i], required, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
