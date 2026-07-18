// Pure structural persona-trait selection. Localized wording is display data only and never affects
// relevance: exact Def/worker tokens and boolean structure are the complete decision boundary.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Selects normally one and never more than two deterministic event-relevant traits.</summary>
    internal static class PersonaTraitPolicy
    {
        private const int HardMaximumTraits = 2;
        private const int HardMaximumCandidates = 128;

        private sealed class Ranked
        {
            public PersonaTraitFact fact;
            public int score;
            public uint tie;
        }

        public static List<PersonaTraitFact> Select(
            IList<PersonaTraitFact> source,
            string eventToken,
            string eventIdentity,
            RoyaltyPolicySnapshot policy)
        {
            List<PersonaTraitFact> result = new List<PersonaTraitFact>();
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled || source == null || !PersonaTraitEventTokens.IsKnown(eventToken))
                return result;

            List<Ranked> ranked = new List<Ranked>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int candidateCap = NormalizeCandidateCap(effective.maximumTraitCandidates);
            for (int i = 0; i < source.Count; i++)
            {
                PersonaTraitFact normalized = Normalize(source[i], effective);
                if (normalized == null || !seen.Add(normalized.traitDefName)) continue;
                int score = Score(normalized, eventToken, effective);
                if (score <= 0) continue;
                ranked.Add(new Ranked
                {
                    fact = normalized,
                    score = score,
                    tie = StableHash((eventIdentity ?? string.Empty) + "|" + normalized.traitDefName)
                });
            }

            ranked.Sort((left, right) =>
            {
                int score = right.score.CompareTo(left.score);
                if (score != 0) return score;
                int tie = left.tie.CompareTo(right.tie);
                return tie != 0 ? tie : string.CompareOrdinal(left.fact.traitDefName, right.fact.traitDefName);
            });
            if (ranked.Count > candidateCap) ranked.RemoveRange(candidateCap, ranked.Count - candidateCap);

            int outputCap = NormalizeOutputCap(effective.maximumSelectedTraits);
            for (int i = 0; i < ranked.Count && result.Count < outputCap; i++)
                result.Add(ranked[i].fact);
            return result;
        }

        /// <summary>Sanitizes and caps facts for a future saved-state adapter without selecting them.</summary>
        public static List<PersonaTraitFact> NormalizeFacts(
            IList<PersonaTraitFact> source,
            RoyaltyPolicySnapshot policy)
        {
            List<PersonaTraitFact> result = new List<PersonaTraitFact>();
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (source == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count && result.Count < NormalizeCandidateCap(effective.maximumTraitCandidates); i++)
            {
                PersonaTraitFact fact = Normalize(source[i], effective);
                if (fact != null && seen.Add(fact.traitDefName)) result.Add(fact);
            }
            result.Sort((left, right) => string.CompareOrdinal(left.traitDefName, right.traitDefName));
            return result;
        }

        internal static List<PersonaTraitFact> CopyFacts(IList<PersonaTraitFact> source)
        {
            List<PersonaTraitFact> result = new List<PersonaTraitFact>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                PersonaTraitFact row = source[i];
                if (row == null) continue;
                result.Add(new PersonaTraitFact
                {
                    traitDefName = row.traitDefName ?? string.Empty,
                    label = row.label ?? string.Empty,
                    description = row.description ?? string.Empty,
                    workerTypeToken = row.workerTypeToken ?? string.Empty,
                    hasKillThought = row.hasKillThought,
                    hasBondedThought = row.hasBondedThought,
                    hasBondedHediff = row.hasBondedHediff,
                    hasEquippedHediff = row.hasEquippedHediff
                });
            }
            return result;
        }

        private static int Score(PersonaTraitFact fact, string eventToken, RoyaltyPolicySnapshot policy)
        {
            RoyaltyTraitOverrideRule exact = FindOverride(fact.traitDefName, eventToken, policy.traitOverrides);
            if (exact != null && exact.excluded) return 0;

            int score = 0;
            if (eventToken == PersonaTraitEventTokens.Kill && fact.hasKillThought)
                score = Math.Max(score, Math.Max(1, policy.killThoughtWeight));

            int worker = WorkerScore(fact.workerTypeToken, eventToken, policy.traitWorkerRules);
            score = Math.Max(score, worker);

            if (eventToken != PersonaTraitEventTokens.Kill)
            {
                if (fact.hasBondedThought) score = Math.Max(score, Math.Max(1, policy.bondedThoughtWeight));
                if (fact.hasBondedHediff) score = Math.Max(score, Math.Max(1, policy.bondedHediffWeight));
                if (fact.hasEquippedHediff) score = Math.Max(score, Math.Max(1, policy.equippedHediffWeight));
            }

            if (exact != null)
            {
                int bounded = Math.Max(1, Math.Min(Math.Max(1, policy.exactOverrideMaximumWeight), exact.weight));
                score = Math.Max(score, bounded);
            }
            return score;
        }

        private static int WorkerScore(
            string workerTypeToken,
            string eventToken,
            IList<RoyaltyTraitWorkerRule> rules)
        {
            string worker = CleanId(workerTypeToken);
            if (worker.Length == 0 || rules == null) return 0;
            int best = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                RoyaltyTraitWorkerRule row = rules[i];
                if (row != null && Same(worker, row.workerTypeToken)
                    && string.Equals(eventToken, CleanId(row.eventToken), StringComparison.Ordinal))
                {
                    best = Math.Max(best, Math.Max(0, row.weight));
                }
            }
            return best;
        }

        private static RoyaltyTraitOverrideRule FindOverride(
            string traitDefName,
            string eventToken,
            IList<RoyaltyTraitOverrideRule> rules)
        {
            if (rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                RoyaltyTraitOverrideRule row = rules[i];
                if (row != null && Same(traitDefName, row.traitDefName)
                    && string.Equals(eventToken, CleanId(row.eventToken), StringComparison.Ordinal))
                    return row;
            }
            return null;
        }

        private static PersonaTraitFact Normalize(PersonaTraitFact source, RoyaltyPolicySnapshot policy)
        {
            if (source == null) return null;
            string defName = CleanId(source.traitDefName);
            if (defName.Length == 0) return null;
            return new PersonaTraitFact
            {
                traitDefName = defName,
                label = CleanText(source.label, Positive(policy.maximumTraitLabelCharacters, 80)),
                description = CleanText(source.description, Positive(policy.maximumTraitDescriptionCharacters, 240)),
                workerTypeToken = CleanId(source.workerTypeToken),
                hasKillThought = source.hasKillThought,
                hasBondedThought = source.hasBondedThought,
                hasBondedHediff = source.hasBondedHediff,
                hasEquippedHediff = source.hasEquippedHediff
            };
        }

        private static string CleanText(string value, int cap)
        {
            string cleaned = (value ?? string.Empty)
                .Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
            while (cleaned.IndexOf("  ", StringComparison.Ordinal) >= 0)
                cleaned = cleaned.Replace("  ", " ");
            return cleaned.Length <= cap ? cleaned : cleaned.Substring(0, cap).TrimEnd();
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }

        private static bool Same(string left, string right)
        {
            string a = CleanId(left);
            string b = CleanId(right);
            return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int NormalizeOutputCap(int value)
        {
            return value < 1 || value > HardMaximumTraits ? HardMaximumTraits : value;
        }

        private static int NormalizeCandidateCap(int value)
        {
            return value < 1 || value > HardMaximumCandidates ? 32 : value;
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            string text = value ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
