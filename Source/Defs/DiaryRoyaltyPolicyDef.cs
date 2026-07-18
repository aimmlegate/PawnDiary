// XML-facing Royalty Phase-0 policy. Every gameplay identifier is stored as a plain string, so this
// Def loads in a base-game-only setup without resolving or requiring a Royalty Def object.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>XML row describing one consequential Tale and optional exact killer role.</summary>
    public sealed class DiaryRoyaltyTaleRuleDef
    {
        public string taleDefName = string.Empty;
        public string killerRoleToken = string.Empty;
        public int minimumSignificance;
        public bool requireVictimDeath = true;
    }

    /// <summary>XML structural mapping from a worker type-name token to one event weight.</summary>
    public sealed class DiaryRoyaltyTraitWorkerRuleDef
    {
        public string workerTypeToken = string.Empty;
        public string eventToken = string.Empty;
        public int weight;
    }

    /// <summary>XML exact compatibility correction for one modded persona trait.</summary>
    public sealed class DiaryRoyaltyTraitOverrideDef
    {
        public string traitDefName = string.Empty;
        public string eventToken = string.Empty;
        public int weight;
        public bool excluded;
    }

    /// <summary>Singleton XML-owned policy for pure R1 persona, title, and psylink decisions.</summary>
    public sealed class DiaryRoyaltyPolicyDef : Def
    {
        public bool enabled = true;
        public int separationThresholdTicks = 60000;
        public int reconciliationCadenceTicks = 2500;
        public int maximumSelectedTraits = 2;
        public int maximumTraitCandidates = 32;
        public int maximumTraitLabelCharacters = 80;
        public int maximumTraitDescriptionCharacters = 240;
        public int maximumDutyCategoryTokens = 2;
        public int titleCorrelationTicks = 2500;
        public int psylinkCorrelationTicks = 2500;
        public int killThoughtWeight = 100;
        public int bondedThoughtWeight = 70;
        public int bondedHediffWeight = 60;
        public int equippedHediffWeight = 40;
        public int exactOverrideMaximumWeight = 50;
        public string personaNarrativeFormat = string.Empty;
        public string titleNarrativeFormat = string.Empty;
        public string titleWithDutiesNarrativeFormat = string.Empty;
        public List<DiaryRoyaltyTaleRuleDef> qualifyingTales = new List<DiaryRoyaltyTaleRuleDef>();
        public List<DiaryRoyaltyTraitWorkerRuleDef> traitWorkerRules =
            new List<DiaryRoyaltyTraitWorkerRuleDef>();
        public List<DiaryRoyaltyTraitOverrideDef> traitOverrides =
            new List<DiaryRoyaltyTraitOverrideDef>();

        /// <summary>Reports malformed tunable policy during Def loading.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (separationThresholdTicks <= 0) yield return "separationThresholdTicks must be positive.";
            if (reconciliationCadenceTicks <= 0) yield return "reconciliationCadenceTicks must be positive.";
            if (maximumSelectedTraits < 1 || maximumSelectedTraits > 2)
                yield return "maximumSelectedTraits must be 1 or 2.";
            if (maximumTraitCandidates < 1 || maximumTraitCandidates > 128)
                yield return "maximumTraitCandidates must be between 1 and 128.";
            if (maximumTraitLabelCharacters <= 0 || maximumTraitDescriptionCharacters <= 0)
                yield return "persona trait text caps must be positive.";
            if (maximumDutyCategoryTokens < 1 || maximumDutyCategoryTokens > 8)
                yield return "maximumDutyCategoryTokens must be between 1 and 8.";
            if (titleCorrelationTicks <= 0 || psylinkCorrelationTicks <= 0)
                yield return "Royal mutation correlation windows must be positive.";
            if (killThoughtWeight <= 0 || bondedThoughtWeight <= 0 || bondedHediffWeight <= 0
                || equippedHediffWeight <= 0 || exactOverrideMaximumWeight <= 0)
                yield return "persona trait relevance weights must be positive.";
            if (string.IsNullOrWhiteSpace(personaNarrativeFormat))
                yield return "personaNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(titleNarrativeFormat))
                yield return "titleNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(titleWithDutiesNarrativeFormat))
                yield return "titleWithDutiesNarrativeFormat must contain DefInjected prompt prose.";

            HashSet<string> tales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (qualifyingTales == null || qualifyingTales.Count == 0)
            {
                yield return "qualifyingTales must contain at least one exact Tale defName.";
            }
            else
            {
                for (int i = 0; i < qualifyingTales.Count; i++)
                {
                    DiaryRoyaltyTaleRuleDef row = qualifyingTales[i];
                    string defName = Clean(row == null ? null : row.taleDefName);
                    string role = Clean(row == null ? null : row.killerRoleToken);
                    if (!SafeToken(defName)) yield return "qualifyingTales row " + i + " has an unsafe Tale defName.";
                    else if (!tales.Add(defName)) yield return "qualifyingTales repeats '" + defName + "'.";
                    if (!RoyaltyTaleRoleTokens.IsKnownOrEmpty(role))
                        yield return "qualifyingTales row " + i + " has an unknown killerRoleToken.";
                    if (row != null && row.minimumSignificance < 0)
                        yield return "qualifyingTales row " + i + " has negative minimumSignificance.";
                }
            }

            if (traitWorkerRules != null)
            {
                for (int i = 0; i < traitWorkerRules.Count; i++)
                {
                    DiaryRoyaltyTraitWorkerRuleDef row = traitWorkerRules[i];
                    if (row == null || !SafeToken(row.workerTypeToken)
                        || !PersonaTraitEventTokens.IsKnown(Clean(row.eventToken)) || row.weight <= 0)
                        yield return "traitWorkerRules row " + i + " needs safe tokens and a positive weight.";
                }
            }

            if (traitOverrides != null)
            {
                for (int i = 0; i < traitOverrides.Count; i++)
                {
                    DiaryRoyaltyTraitOverrideDef row = traitOverrides[i];
                    if (row == null || !SafeToken(row.traitDefName)
                        || !PersonaTraitEventTokens.IsKnown(Clean(row.eventToken))
                        || (!row.excluded && row.weight <= 0))
                        yield return "traitOverrides row " + i + " needs safe tokens and a positive weight unless excluded.";
                }
            }
        }

        private static bool SafeToken(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }

    /// <summary>Copies mutable XML policy into a detached pure snapshot with safe fallbacks.</summary>
    internal static class DiaryRoyaltyPolicy
    {
        internal const string DefName = "Diary_Royalty";

        public static RoyaltyPolicySnapshot Snapshot()
        {
            RoyaltyPolicySnapshot result = RoyaltyPolicySnapshot.CreateDefault();
            DiaryRoyaltyPolicyDef source = DefDatabase<DiaryRoyaltyPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null) return result;

            result.enabled = source.enabled;
            result.separationThresholdTicks = Positive(source.separationThresholdTicks, result.separationThresholdTicks);
            result.reconciliationCadenceTicks = Positive(source.reconciliationCadenceTicks, result.reconciliationCadenceTicks);
            result.maximumSelectedTraits = Between(source.maximumSelectedTraits, 1, 2, result.maximumSelectedTraits);
            result.maximumTraitCandidates = Between(source.maximumTraitCandidates, 1, 128, result.maximumTraitCandidates);
            result.maximumTraitLabelCharacters = Positive(
                source.maximumTraitLabelCharacters, result.maximumTraitLabelCharacters);
            result.maximumTraitDescriptionCharacters = Positive(
                source.maximumTraitDescriptionCharacters, result.maximumTraitDescriptionCharacters);
            result.maximumDutyCategoryTokens = Between(
                source.maximumDutyCategoryTokens, 1, 8, result.maximumDutyCategoryTokens);
            result.titleCorrelationTicks = Positive(source.titleCorrelationTicks, result.titleCorrelationTicks);
            result.psylinkCorrelationTicks = Positive(source.psylinkCorrelationTicks, result.psylinkCorrelationTicks);
            result.killThoughtWeight = Positive(source.killThoughtWeight, result.killThoughtWeight);
            result.bondedThoughtWeight = Positive(source.bondedThoughtWeight, result.bondedThoughtWeight);
            result.bondedHediffWeight = Positive(source.bondedHediffWeight, result.bondedHediffWeight);
            result.equippedHediffWeight = Positive(source.equippedHediffWeight, result.equippedHediffWeight);
            result.exactOverrideMaximumWeight = Positive(
                source.exactOverrideMaximumWeight, result.exactOverrideMaximumWeight);
            result.personaNarrativeFormat = source.personaNarrativeFormat ?? string.Empty;
            result.titleNarrativeFormat = source.titleNarrativeFormat ?? string.Empty;
            result.titleWithDutiesNarrativeFormat = source.titleWithDutiesNarrativeFormat ?? string.Empty;

            List<RoyaltyTaleQualificationRule> tales = CopyTales(source.qualifyingTales);
            if (tales.Count > 0) result.qualifyingTales = tales;
            result.traitWorkerRules = CopyWorkers(source.traitWorkerRules);
            result.traitOverrides = CopyOverrides(source.traitOverrides);
            return result;
        }

        private static List<RoyaltyTaleQualificationRule> CopyTales(List<DiaryRoyaltyTaleRuleDef> source)
        {
            List<RoyaltyTaleQualificationRule> result = new List<RoyaltyTaleQualificationRule>();
            if (source == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                DiaryRoyaltyTaleRuleDef row = source[i];
                string defName = Safe(row == null ? null : row.taleDefName);
                string role = Safe(row == null ? null : row.killerRoleToken);
                if (defName.Length == 0 || !RoyaltyTaleRoleTokens.IsKnownOrEmpty(role) || !seen.Add(defName)) continue;
                result.Add(new RoyaltyTaleQualificationRule
                {
                    taleDefName = defName,
                    killerRoleToken = role,
                    minimumSignificance = Math.Max(0, row.minimumSignificance),
                    requireVictimDeath = row.requireVictimDeath
                });
            }
            return result;
        }

        private static List<RoyaltyTraitWorkerRule> CopyWorkers(List<DiaryRoyaltyTraitWorkerRuleDef> source)
        {
            List<RoyaltyTraitWorkerRule> result = new List<RoyaltyTraitWorkerRule>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryRoyaltyTraitWorkerRuleDef row = source[i];
                string worker = Safe(row == null ? null : row.workerTypeToken);
                string eventToken = Safe(row == null ? null : row.eventToken);
                if (worker.Length == 0 || !PersonaTraitEventTokens.IsKnown(eventToken) || row.weight <= 0) continue;
                result.Add(new RoyaltyTraitWorkerRule
                {
                    workerTypeToken = worker,
                    eventToken = eventToken,
                    weight = row.weight
                });
            }
            return result;
        }

        private static List<RoyaltyTraitOverrideRule> CopyOverrides(List<DiaryRoyaltyTraitOverrideDef> source)
        {
            List<RoyaltyTraitOverrideRule> result = new List<RoyaltyTraitOverrideRule>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                DiaryRoyaltyTraitOverrideDef row = source[i];
                string defName = Safe(row == null ? null : row.traitDefName);
                string eventToken = Safe(row == null ? null : row.eventToken);
                if (defName.Length == 0 || !PersonaTraitEventTokens.IsKnown(eventToken)
                    || (!row.excluded && row.weight <= 0)) continue;
                result.Add(new RoyaltyTraitOverrideRule
                {
                    traitDefName = defName,
                    eventToken = eventToken,
                    weight = row.weight,
                    excluded = row.excluded
                });
            }
            return result;
        }

        private static string Safe(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static int Between(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }
    }
}
