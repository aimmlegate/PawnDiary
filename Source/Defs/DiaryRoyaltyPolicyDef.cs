// XML-facing Royalty policy frozen in Phase 0 and consumed by the Phase-2 persona lifecycle. Every
// gameplay identifier is a plain string, so this Def loads without resolving a Royalty Def object.
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
        public string victimRoleToken = string.Empty;
        public int minimumSignificance;
        public bool requireVictimDeath = true;
    }

    /// <summary>XML role row for one ordinary Tale emitted alongside a persona kill milestone.</summary>
    public sealed class DiaryRoyaltyTaleRoleDef
    {
        public string taleDefName = string.Empty;
        public string killerRoleToken = string.Empty;
        public string victimRoleToken = string.Empty;
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
        public int titleThoughtCorrelationTicks = 2500;
        // Cleanup lifetime for the transient exact-edge duplicate cache. Committed succession facts
        // have no time deadline because vanilla bestowing quests can be postponed indefinitely.
        public int successionCorrelationTicks = 2500;
        public int killThoughtCorrelationTicks = 60;
        public int maximumPendingRoyalMutations = 64;
        public int maximumPendingTitleThoughts = 128;
        public int maximumPendingSuccessions = 64;
        public int maximumRoyaltyContextCharacters = 120;
        public int killThoughtWeight = 100;
        public int bondedThoughtWeight = 70;
        public int bondedHediffWeight = 60;
        public int equippedHediffWeight = 40;
        public int exactOverrideMaximumWeight = 50;
        public string personaNarrativeFormat = string.Empty;
        public string titleNarrativeFormat = string.Empty;
        public string titleWithDutiesNarrativeFormat = string.Empty;
        public List<DiaryRoyaltyTaleRuleDef> qualifyingTales = new List<DiaryRoyaltyTaleRuleDef>();
        public List<DiaryRoyaltyTaleRoleDef> personaKillCompanionTales =
            new List<DiaryRoyaltyTaleRoleDef>();
        public List<DiaryRoyaltyTraitWorkerRuleDef> traitWorkerRules =
            new List<DiaryRoyaltyTraitWorkerRuleDef>();
        public List<DiaryRoyaltyTraitOverrideDef> traitOverrides =
            new List<DiaryRoyaltyTraitOverrideDef>();
        // Plain defName strings only: these do not create Royalty Def cross-references when the DLC
        // is absent. They identify exact canonical ritual/item routes at the guarded adapter edge.
        public List<string> bestowingRitualDefNames = new List<string>();
        public List<string> animaRitualDefNames = new List<string>();
        public List<string> neuroformerThingDefNames = new List<string>();

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
            if (titleCorrelationTicks <= 0 || psylinkCorrelationTicks <= 0
                || titleThoughtCorrelationTicks <= 0 || successionCorrelationTicks <= 0
                || killThoughtCorrelationTicks <= 0)
                yield return "Royal mutation correlation windows must be positive.";
            if (maximumPendingRoyalMutations < 1 || maximumPendingRoyalMutations > 256
                || maximumPendingTitleThoughts < 1 || maximumPendingTitleThoughts > 512
                || maximumPendingSuccessions < 1 || maximumPendingSuccessions > 256)
                yield return "Royal transient admission caps are outside their safe ranges.";
            if (maximumRoyaltyContextCharacters < 20 || maximumRoyaltyContextCharacters > 512)
                yield return "maximumRoyaltyContextCharacters must be between 20 and 512.";
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
                    string victimRole = Clean(row == null ? null : row.victimRoleToken);
                    if (!SafeToken(defName)) yield return "qualifyingTales row " + i + " has an unsafe Tale defName.";
                    else if (!tales.Add(defName)) yield return "qualifyingTales repeats '" + defName + "'.";
                    if (!RoyaltyTaleRoleTokens.IsKnown(role))
                        yield return "qualifyingTales row " + i + " needs a known killerRoleToken.";
                    if (!RoyaltyTaleRoleTokens.IsKnown(victimRole))
                        yield return "qualifyingTales row " + i + " needs a known victimRoleToken.";
                    if (role == victimRole)
                        yield return "qualifyingTales row " + i + " must use distinct killer and victim roles.";
                    if (row != null && row.minimumSignificance < 0)
                        yield return "qualifyingTales row " + i + " has negative minimumSignificance.";
                }
            }

            HashSet<string> companionTales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (personaKillCompanionTales == null || personaKillCompanionTales.Count == 0)
            {
                yield return "personaKillCompanionTales must contain at least one exact Tale defName.";
            }
            else
            {
                for (int i = 0; i < personaKillCompanionTales.Count; i++)
                {
                    DiaryRoyaltyTaleRoleDef row = personaKillCompanionTales[i];
                    string defName = Clean(row == null ? null : row.taleDefName);
                    string role = Clean(row == null ? null : row.killerRoleToken);
                    string victimRole = Clean(row == null ? null : row.victimRoleToken);
                    if (!SafeToken(defName))
                        yield return "personaKillCompanionTales row " + i + " has an unsafe Tale defName.";
                    else if (!companionTales.Add(defName))
                        yield return "personaKillCompanionTales repeats '" + defName + "'.";
                    if (!RoyaltyTaleRoleTokens.IsKnown(role)
                        || !RoyaltyTaleRoleTokens.IsKnown(victimRole) || role == victimRole)
                        yield return "personaKillCompanionTales row " + i + " needs distinct known roles.";
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

            foreach (string error in TokenListErrors(bestowingRitualDefNames, "bestowingRitualDefNames"))
                yield return error;
            foreach (string error in TokenListErrors(animaRitualDefNames, "animaRitualDefNames"))
                yield return error;
            foreach (string error in TokenListErrors(neuroformerThingDefNames, "neuroformerThingDefNames"))
                yield return error;
        }

        private static IEnumerable<string> TokenListErrors(List<string> values, string fieldName)
        {
            if (values == null || values.Count == 0)
            {
                yield return fieldName + " must contain at least one exact defName string.";
                yield break;
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Count; i++)
            {
                string token = Clean(values[i]);
                if (!SafeToken(token)) yield return fieldName + " row " + i + " is unsafe.";
                else if (!seen.Add(token)) yield return fieldName + " repeats '" + token + "'.";
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

        // Def content is stable after loading and callers treat the detached lists as read-only. The
        // localized DefInjected prose is copied by value, so a language switch invalidates the cache.
        // Avoiding a deep copy here matters because this snapshot is read from reconciliation and
        // event-capture paths.
        private static RoyaltyPolicySnapshot cached;
        private static LoadedLanguage cachedLanguage;

        public static RoyaltyPolicySnapshot Snapshot()
        {
            if (cached != null && cachedLanguage == LanguageDatabase.activeLanguage) return cached;

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
            result.titleThoughtCorrelationTicks = Positive(
                source.titleThoughtCorrelationTicks, result.titleThoughtCorrelationTicks);
            result.successionCorrelationTicks = Positive(
                source.successionCorrelationTicks, result.successionCorrelationTicks);
            result.killThoughtCorrelationTicks = Positive(
                source.killThoughtCorrelationTicks, result.killThoughtCorrelationTicks);
            result.maximumPendingRoyalMutations = Between(
                source.maximumPendingRoyalMutations, 1, 256, result.maximumPendingRoyalMutations);
            result.maximumPendingTitleThoughts = Between(
                source.maximumPendingTitleThoughts, 1, 512, result.maximumPendingTitleThoughts);
            result.maximumPendingSuccessions = Between(
                source.maximumPendingSuccessions, 1, 256, result.maximumPendingSuccessions);
            result.maximumRoyaltyContextCharacters = Between(
                source.maximumRoyaltyContextCharacters, 20, 512, result.maximumRoyaltyContextCharacters);
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
            List<RoyaltyTaleRoleRule> companionTales = CopyRoleTales(source.personaKillCompanionTales);
            if (companionTales.Count > 0) result.personaKillCompanionTales = companionTales;
            result.traitWorkerRules = CopyWorkers(source.traitWorkerRules);
            result.traitOverrides = CopyOverrides(source.traitOverrides);
            CopyTokens(source.bestowingRitualDefNames, result.bestowingRitualDefNames);
            CopyTokens(source.animaRitualDefNames, result.animaRitualDefNames);
            CopyTokens(source.neuroformerThingDefNames, result.neuroformerThingDefNames);
            cached = result;
            cachedLanguage = LanguageDatabase.activeLanguage;
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
                string victimRole = Safe(row == null ? null : row.victimRoleToken);
                if (defName.Length == 0 || !RoyaltyTaleRoleTokens.IsKnown(role)
                    || !RoyaltyTaleRoleTokens.IsKnown(victimRole) || role == victimRole
                    || !seen.Add(defName)) continue;
                result.Add(new RoyaltyTaleQualificationRule
                {
                    taleDefName = defName,
                    killerRoleToken = role,
                    victimRoleToken = victimRole,
                    minimumSignificance = Math.Max(0, row.minimumSignificance),
                    requireVictimDeath = row.requireVictimDeath
                });
            }
            return result;
        }

        private static List<RoyaltyTaleRoleRule> CopyRoleTales(List<DiaryRoyaltyTaleRoleDef> source)
        {
            List<RoyaltyTaleRoleRule> result = new List<RoyaltyTaleRoleRule>();
            if (source == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                DiaryRoyaltyTaleRoleDef row = source[i];
                string defName = Safe(row == null ? null : row.taleDefName);
                string role = Safe(row == null ? null : row.killerRoleToken);
                string victimRole = Safe(row == null ? null : row.victimRoleToken);
                if (defName.Length == 0 || !RoyaltyTaleRoleTokens.IsKnown(role)
                    || !RoyaltyTaleRoleTokens.IsKnown(victimRole) || role == victimRole
                    || !seen.Add(defName)) continue;
                result.Add(new RoyaltyTaleRoleRule
                {
                    taleDefName = defName,
                    killerRoleToken = role,
                    victimRoleToken = victimRole
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

        private static void CopyTokens(List<string> source, List<string> destination)
        {
            if (source == null || destination == null) return;
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string token = Safe(source[i]);
                if (token.Length > 0 && seen.Add(token)) result.Add(token);
            }
            if (result.Count == 0) return;
            destination.Clear();
            destination.AddRange(result);
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
