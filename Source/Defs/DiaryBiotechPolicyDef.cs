// XML schema and main-thread snapshot adapter for Biotech growth, family, and gene-salience policy.
// The Def owns tunable thresholds, weights, exact matcher strings, and localized qualitative text.
// Pure policies receive a detached BiotechPolicySnapshot and never touch DefDatabase themselves.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>One inclusive internal growth-tier range and localized qualitative description.</summary>
    public class DiaryBiotechOpportunityBandDef
    {
        public int minimumTier;
        public int maximumTier;
        public string token;
        public string description;
    }

    /// <summary>One minimum exact-observation count and localized upbringing description.</summary>
    public class DiaryBiotechObservationBandDef
    {
        public int minimumEvidence;
        public string token;
        public string description;
    }

    /// <summary>One structural gene-category token and its XML-owned salience weight.</summary>
    public class DiaryGeneCategoryWeightDef
    {
        public string category;
        public int weight;
    }

    /// <summary>Singleton XML-owned Biotech policy; it contains no live DLC Def reference.</summary>
    public class DiaryBiotechPolicyDef : Def
    {
        public int growthPendingExpiryTicks = 180000;
        public int growthFallbackGraceTicks = 60000;
        public int maximumPendingGrowthRows = BiotechPendingOwnershipLimits.DefaultMaximumRows;
        public List<DiaryBiotechOpportunityBandDef> opportunityBands =
            new List<DiaryBiotechOpportunityBandDef>();
        // Localized prompt prose for passion transitions. Stable passion tokens stay in the pure
        // contract, while these DefInjected descriptions are what the LLM actually sees.
        public string newInterestDescription;
        public string deepenedInterestDescription;
        // N2/N3-B provider prose is DefInjected. {0} is the pawn's visible short name; identity
        // formats additionally use {1} for the visible current xenotype or one salient gene theme.
        public string familySinceBirthNarrativeFormat;
        public string familyObservedNarrativeFormat;
        public string familyBaselineNarrativeFormat;
        public string identityNarrativeFormat;
        public string geneIdentityNarrativeFormat;
        // N3-B bond prose. {0}/{1} are visible pawn names and {2} is localized phase wording.
        public string psychicBondNarrativeFormat;
        public List<string> familyActivityExactDefNames = new List<string>();
        public List<string> familyActivityPrefixes = new List<string>();
        public List<string> familyPregnancyHediffDefNames = new List<string>();
        public List<string> familyLaborHediffDefNames = new List<string>();
        public List<string> familyLessonAdultThoughtDefNames = new List<string>();
        public List<string> familyLessonChildThoughtDefNames = new List<string>();
        public List<string> matureBirthDefNames = new List<string>();
        public List<string> miscarriageBirtherThoughtDefNames = new List<string>();
        public List<string> miscarriagePartnerThoughtDefNames = new List<string>();
        public int familyActivityPairDedupTicks = 2500;
        public List<DiaryBiotechObservationBandDef> observationBands =
            new List<DiaryBiotechObservationBandDef>();
        public int supporterMinimumEvidence = 2;
        public int maximumSupporterRows = 12;
        public int familyArcRetentionTicks = 3600000;
        public int birthNamingPollTicks = 2500;
        public int birthNamingGraceTicks = 60000;
        public int birthCorrelationExpiryTicks = 2500;
        public int maximumBirthWriters = 2;
        public int maximumPendingBirthRows = BiotechPendingOwnershipLimits.DefaultMaximumRows;
        // Phase 5 policy uses only primitive values and plain Def-name strings. They are safe when
        // Biotech is absent because no XML row below resolves a DLC Def.
        public int geneMaximumThemes = 4;
        public int geneDeltaBonus = 100;
        public int geneXenogeneBonus = 4;
        public int geneEndogeneBonus = 2;
        public int geneDuplicateCategoryPenalty = 30;
        public int geneForcedDefNameBonus = 1000;
        public int geneLabelCharacterLimit = 80;
        public int geneDescriptionCharacterLimit = 240;
        public int geneTotalTextCharacterLimit = 640;
        public int geneMaximumObservedDefNames = 512;
        public int geneMinimumFallbackChanges = 2;
        public List<DiaryGeneCategoryWeightDef> geneCategoryWeights =
            new List<DiaryGeneCategoryWeightDef>();
        public List<string> geneForceIncludeDefNames = new List<string>();
        public List<string> geneExcludeDefNames = new List<string>();
        public List<string> geneAllowDuplicateCategories = new List<string>();
        // Phase 6 mechanitor policy. Tale role lists are split because vanilla's KilledBy Tale puts
        // the killer second while the combat Tales put the killer first.
        public int mechanitorLongServiceTicks = 900000;
        public int mechanitorMaximumObservedMechs = 64;
        public int mechanitorMaximumBossCalls = 16;
        public List<string> mechanitorCombatFirstPawnDefNames = new List<string>();
        public List<string> mechanitorCombatSecondPawnDefNames = new List<string>();
        // Phase 8 psychic-bond/deathrest policy. These are primitives only, so this Def stays safe
        // when Biotech content is absent and the guarded live adapters simply never run.
        public float deathrestSevereCompletionThreshold = 0.5f;
        public int deathrestCooldownTicks = 900000;
        public int deathrestLifetimePageLimit = 1;
        public int psychicBondCorrelationExpiryTicks = 2500;
        public int maximumBondObservationRows = 16;

        /// <summary>Reports malformed XML policy at Def load instead of failing inside a later hook.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (growthPendingExpiryTicks <= 0) yield return "growthPendingExpiryTicks must be positive.";
            if (growthFallbackGraceTicks < 0) yield return "growthFallbackGraceTicks cannot be negative.";
            if (maximumPendingGrowthRows <= 0
                || maximumPendingGrowthRows > BiotechPendingOwnershipLimits.HardMaximumRows)
                yield return "maximumPendingGrowthRows must stay between one and the defensive "
                    + BiotechPendingOwnershipLimits.HardMaximumRows + "-row cap.";
            if (string.IsNullOrWhiteSpace(newInterestDescription))
                yield return "newInterestDescription must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(deepenedInterestDescription))
                yield return "deepenedInterestDescription must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familySinceBirthNarrativeFormat))
                yield return "familySinceBirthNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familyObservedNarrativeFormat))
                yield return "familyObservedNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familyBaselineNarrativeFormat))
                yield return "familyBaselineNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(identityNarrativeFormat))
                yield return "identityNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(geneIdentityNarrativeFormat))
                yield return "geneIdentityNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(psychicBondNarrativeFormat))
                yield return "psychicBondNarrativeFormat must contain DefInjected prompt prose.";
            if (familyActivityPairDedupTicks < 0) yield return "familyActivityPairDedupTicks cannot be negative.";
            if (supporterMinimumEvidence <= 0) yield return "supporterMinimumEvidence must be positive.";
            if (maximumSupporterRows <= 0) yield return "maximumSupporterRows must be positive.";
            if (familyArcRetentionTicks <= 0) yield return "familyArcRetentionTicks must be positive.";
            if (birthNamingPollTicks <= 0) yield return "birthNamingPollTicks must be positive.";
            if (birthNamingGraceTicks < 0) yield return "birthNamingGraceTicks cannot be negative.";
            if (birthCorrelationExpiryTicks <= 0) yield return "birthCorrelationExpiryTicks must be positive.";
            if (maximumBirthWriters < 1 || maximumBirthWriters > 2)
            {
                yield return "maximumBirthWriters must stay between one and the hard two-writer cap.";
            }
            if (maximumPendingBirthRows <= 0
                || maximumPendingBirthRows > BiotechPendingOwnershipLimits.HardMaximumRows)
                yield return "maximumPendingBirthRows must stay between one and the defensive "
                    + BiotechPendingOwnershipLimits.HardMaximumRows + "-row cap.";
            if (!HasNonBlankMatcher(matureBirthDefNames))
                yield return "matureBirthDefNames must contain at least one exact Def name.";
            if (!HasNonBlankMatcher(miscarriageBirtherThoughtDefNames))
                yield return "miscarriageBirtherThoughtDefNames must contain at least one exact Def name.";
            if (!HasNonBlankMatcher(miscarriagePartnerThoughtDefNames))
                yield return "miscarriagePartnerThoughtDefNames must contain at least one exact Def name.";
            if (geneMaximumThemes < 1 || geneMaximumThemes > GeneSaliencePolicySnapshot.HardMaximumThemes)
                yield return "geneMaximumThemes must stay between one and the defensive "
                    + GeneSaliencePolicySnapshot.HardMaximumThemes + "-theme cap.";
            if (geneDeltaBonus < 0) yield return "geneDeltaBonus cannot be negative.";
            if (geneDuplicateCategoryPenalty < 0)
                yield return "geneDuplicateCategoryPenalty cannot be negative.";
            if (geneForcedDefNameBonus < 0) yield return "geneForcedDefNameBonus cannot be negative.";
            if (geneLabelCharacterLimit < 1
                || geneLabelCharacterLimit > GeneSaliencePolicySnapshot.HardMaximumTextCharacters)
                yield return "geneLabelCharacterLimit must stay within the defensive text cap.";
            if (geneDescriptionCharacterLimit < 1
                || geneDescriptionCharacterLimit > GeneSaliencePolicySnapshot.HardMaximumTextCharacters)
                yield return "geneDescriptionCharacterLimit must stay within the defensive text cap.";
            if (geneTotalTextCharacterLimit < 1
                || geneTotalTextCharacterLimit > GeneSaliencePolicySnapshot.HardMaximumTextCharacters)
                yield return "geneTotalTextCharacterLimit must stay within the defensive text cap.";
            if (geneMaximumObservedDefNames < 1
                || geneMaximumObservedDefNames > GeneIdentityObservationPolicy.HardMaximumGeneDefNames)
                yield return "geneMaximumObservedDefNames must stay between one and the defensive "
                    + GeneIdentityObservationPolicy.HardMaximumGeneDefNames + "-row cap.";
            if (geneMinimumFallbackChanges < 1
                || geneMinimumFallbackChanges > GeneIdentityObservationPolicy.HardMaximumGeneDefNames)
                yield return "geneMinimumFallbackChanges must stay between one and the defensive "
                    + GeneIdentityObservationPolicy.HardMaximumGeneDefNames + "-row cap.";
            if (mechanitorLongServiceTicks <= 0)
                yield return "mechanitorLongServiceTicks must be positive.";
            if (mechanitorMaximumObservedMechs < 1
                || mechanitorMaximumObservedMechs > MechanitorObservationState.HardMaximumMechs)
                yield return "mechanitorMaximumObservedMechs must stay within the defensive row cap.";
            if (mechanitorMaximumBossCalls < 1
                || mechanitorMaximumBossCalls > MechanitorObservationState.HardMaximumBossCalls)
                yield return "mechanitorMaximumBossCalls must stay within the defensive row cap.";
            if (!HasNonBlankMatcher(mechanitorCombatFirstPawnDefNames)
                && !HasNonBlankMatcher(mechanitorCombatSecondPawnDefNames))
                yield return "mechanitor combat Tale ownership requires at least one exact Def name.";
            if (deathrestSevereCompletionThreshold <= 0f
                || deathrestSevereCompletionThreshold >= 1f)
                yield return "deathrestSevereCompletionThreshold must be greater than zero and less than one.";
            if (deathrestCooldownTicks < 0)
                yield return "deathrestCooldownTicks cannot be negative.";
            if (deathrestLifetimePageLimit < 1)
                yield return "deathrestLifetimePageLimit must be positive.";
            if (psychicBondCorrelationExpiryTicks <= 0)
                yield return "psychicBondCorrelationExpiryTicks must be positive.";
            if (maximumBondObservationRows < 1
                || maximumBondObservationRows > PsychicBondLifecyclePolicy.HardMaximumObservationRows)
                yield return "maximumBondObservationRows must stay between one and the defensive "
                    + PsychicBondLifecyclePolicy.HardMaximumObservationRows + "-row cap.";

            foreach (string error in OpportunityBandErrors(opportunityBands)) yield return error;
            foreach (string error in ObservationBandErrors(observationBands)) yield return error;
            foreach (string error in GeneCategoryWeightErrors(geneCategoryWeights)) yield return error;
        }

        internal static bool HasNonBlankMatcher(List<string> values)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])) return true;
            }
            return false;
        }

        private static IEnumerable<string> OpportunityBandErrors(List<DiaryBiotechOpportunityBandDef> bands)
        {
            bool[] covered = new bool[9];
            if (bands == null || bands.Count == 0)
            {
                yield return "opportunityBands must cover internal tiers zero through eight.";
                yield break;
            }

            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bands.Count; i++)
            {
                DiaryBiotechOpportunityBandDef band = bands[i];
                if (band == null || string.IsNullOrWhiteSpace(band.token)
                    || band.minimumTier < 0 || band.maximumTier > 8 || band.minimumTier > band.maximumTier)
                {
                    yield return "opportunityBands rows require a token and an inclusive range within zero through eight.";
                    continue;
                }

                if (!tokens.Add(band.token.Trim()))
                {
                    yield return "opportunityBands tokens must be unique.";
                }

                if (string.IsNullOrWhiteSpace(band.description))
                {
                    yield return "opportunityBands descriptions must be non-blank DefInjected prompt prose.";
                }

                for (int tier = band.minimumTier; tier <= band.maximumTier; tier++)
                {
                    if (covered[tier]) yield return "opportunityBands ranges must not overlap.";
                    covered[tier] = true;
                }
            }

            for (int tier = 0; tier < covered.Length; tier++)
            {
                if (!covered[tier]) yield return "opportunityBands must cover every tier from zero through eight.";
            }
        }

        private static IEnumerable<string> ObservationBandErrors(List<DiaryBiotechObservationBandDef> bands)
        {
            if (bands == null || bands.Count == 0)
            {
                yield return "observationBands must contain at least one qualitative band.";
                yield break;
            }

            HashSet<int> thresholds = new HashSet<int>();
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bands.Count; i++)
            {
                DiaryBiotechObservationBandDef band = bands[i];
                if (band == null || band.minimumEvidence <= 0 || string.IsNullOrWhiteSpace(band.token))
                {
                    yield return "observationBands rows require a positive threshold and token.";
                    continue;
                }

                if (!thresholds.Add(band.minimumEvidence) || !tokens.Add(band.token.Trim()))
                {
                    yield return "observationBands thresholds and tokens must be unique.";
                }

                if (string.IsNullOrWhiteSpace(band.description))
                {
                    yield return "observationBands descriptions must be non-blank DefInjected prompt prose.";
                }
            }
        }

        private static IEnumerable<string> GeneCategoryWeightErrors(List<DiaryGeneCategoryWeightDef> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                yield return "geneCategoryWeights must contain at least one structural category.";
                yield break;
            }

            HashSet<string> categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < weights.Count; i++)
            {
                DiaryGeneCategoryWeightDef row = weights[i];
                if (row == null || string.IsNullOrWhiteSpace(row.category))
                {
                    yield return "geneCategoryWeights rows require a non-blank category token.";
                    continue;
                }
                if (!categories.Add(row.category.Trim()))
                    yield return "geneCategoryWeights category tokens must be unique.";
            }
        }
    }

    /// <summary>Copies the singleton live Def into a detached plain snapshot with safe fallbacks.</summary>
    internal static class DiaryBiotechPolicy
    {
        private const string DefName = "Diary_BiotechPolicy";

        // Def content is fixed once RimWorld finishes loading, and every consumer treats the snapshot
        // as read-only policy input (its lists are only mutated during construction), so one shared
        // copy is safe. Caching matters because Snapshot() sits on frequent capture paths — every
        // social interaction and gained thought builds one — and each rebuild deep-copies ~12 lists.
        // Unlike DiaryTuning's cached Def reference (through which DefInjected re-injection flows
        // automatically), this snapshot copies localized prose BY VALUE, so it is additionally keyed
        // to the active language: a mid-session language switch rebuilds it instead of serving stale
        // prose until restart. One reference compare per call.
        private static BiotechPolicySnapshot cached;
        private static LoadedLanguage cachedLanguage;

        /// <summary>
        /// Returns the shared, read-only policy snapshot, built once per XML-load/language with safe
        /// fallbacks. While the Def is not (yet) loaded, callers get fresh defaults and no cache is
        /// kept, so a later call can still pick up the XML row.
        /// </summary>
        public static BiotechPolicySnapshot Snapshot()
        {
            if (cached != null && cachedLanguage == LanguageDatabase.activeLanguage)
            {
                return cached;
            }

            BiotechPolicySnapshot result = BiotechPolicySnapshot.CreateDefault();
            DiaryBiotechPolicyDef source = DefDatabase<DiaryBiotechPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return result;
            }

            result.growthPendingExpiryTicks = Positive(source.growthPendingExpiryTicks, result.growthPendingExpiryTicks);
            result.growthFallbackGraceTicks = Math.Max(0, source.growthFallbackGraceTicks);
            result.maximumPendingGrowthRows = BiotechPendingOwnershipLimits.NormalizeMaximumRows(
                source.maximumPendingGrowthRows);
            result.familyActivityPairDedupTicks = Math.Max(0, source.familyActivityPairDedupTicks);
            result.supporterMinimumEvidence = Positive(source.supporterMinimumEvidence, result.supporterMinimumEvidence);
            result.maximumSupporterRows = Positive(source.maximumSupporterRows, result.maximumSupporterRows);
            result.familyArcRetentionTicks = Positive(source.familyArcRetentionTicks, result.familyArcRetentionTicks);
            result.birthNamingPollTicks = Positive(source.birthNamingPollTicks, result.birthNamingPollTicks);
            result.birthNamingGraceTicks = Math.Max(0, source.birthNamingGraceTicks);
            result.birthCorrelationExpiryTicks = Positive(
                source.birthCorrelationExpiryTicks,
                result.birthCorrelationExpiryTicks);
            result.maximumBirthWriters = source.maximumBirthWriters < 1 || source.maximumBirthWriters > 2
                ? result.maximumBirthWriters
                : source.maximumBirthWriters;
            result.maximumPendingBirthRows = BiotechPendingOwnershipLimits.NormalizeMaximumRows(
                source.maximumPendingBirthRows);
            result.newInterestDescription = source.newInterestDescription ?? string.Empty;
            result.deepenedInterestDescription = source.deepenedInterestDescription ?? string.Empty;
            result.familySinceBirthNarrativeFormat = source.familySinceBirthNarrativeFormat ?? string.Empty;
            result.familyObservedNarrativeFormat = source.familyObservedNarrativeFormat ?? string.Empty;
            result.familyBaselineNarrativeFormat = source.familyBaselineNarrativeFormat ?? string.Empty;
            result.identityNarrativeFormat = source.identityNarrativeFormat ?? string.Empty;
            result.geneIdentityNarrativeFormat = source.geneIdentityNarrativeFormat ?? string.Empty;
            result.psychicBondNarrativeFormat = source.psychicBondNarrativeFormat ?? string.Empty;
            result.geneSalience = CopyGeneSalience(source, result.geneSalience);
            result.mechanitorLongServiceTicks = Positive(
                source.mechanitorLongServiceTicks,
                result.mechanitorLongServiceTicks);
            result.mechanitorMaximumObservedMechs = source.mechanitorMaximumObservedMechs < 1
                || source.mechanitorMaximumObservedMechs > MechanitorObservationState.HardMaximumMechs
                ? result.mechanitorMaximumObservedMechs : source.mechanitorMaximumObservedMechs;
            result.mechanitorMaximumBossCalls = source.mechanitorMaximumBossCalls < 1
                || source.mechanitorMaximumBossCalls > MechanitorObservationState.HardMaximumBossCalls
                ? result.mechanitorMaximumBossCalls : source.mechanitorMaximumBossCalls;
            result.bondDeathrest.deathrestSevereCompletionThreshold =
                source.deathrestSevereCompletionThreshold <= 0f
                || source.deathrestSevereCompletionThreshold >= 1f
                    ? result.bondDeathrest.deathrestSevereCompletionThreshold
                    : source.deathrestSevereCompletionThreshold;
            result.bondDeathrest.deathrestCooldownTicks =
                Math.Max(0, source.deathrestCooldownTicks);
            result.bondDeathrest.deathrestLifetimePageLimit =
                Positive(
                    source.deathrestLifetimePageLimit,
                    result.bondDeathrest.deathrestLifetimePageLimit);
            result.bondDeathrest.psychicBondCorrelationExpiryTicks =
                Positive(
                    source.psychicBondCorrelationExpiryTicks,
                    result.bondDeathrest.psychicBondCorrelationExpiryTicks);
            result.bondDeathrest.maximumBondObservationRows =
                source.maximumBondObservationRows < 1
                || source.maximumBondObservationRows > PsychicBondLifecyclePolicy.HardMaximumObservationRows
                    ? result.bondDeathrest.maximumBondObservationRows
                    : source.maximumBondObservationRows;
            CopyOpportunityBands(source.opportunityBands, result);
            CopyObservationBands(source.observationBands, result);
            CopyStrings(source.familyActivityExactDefNames, result.familyActivityExactDefNames);
            CopyStrings(source.familyActivityPrefixes, result.familyActivityPrefixes);
            CopyStrings(source.familyPregnancyHediffDefNames, result.familyPregnancyHediffDefNames);
            CopyStrings(source.familyLaborHediffDefNames, result.familyLaborHediffDefNames);
            CopyStrings(source.familyLessonAdultThoughtDefNames, result.familyLessonAdultThoughtDefNames);
            CopyStrings(source.familyLessonChildThoughtDefNames, result.familyLessonChildThoughtDefNames);
            ReplaceStrings(source.matureBirthDefNames, result.matureBirthDefNames);
            ReplaceStrings(
                source.miscarriageBirtherThoughtDefNames,
                result.miscarriageBirtherThoughtDefNames);
            ReplaceStrings(
                source.miscarriagePartnerThoughtDefNames,
                result.miscarriagePartnerThoughtDefNames);
            ReplaceStrings(
                source.mechanitorCombatFirstPawnDefNames,
                result.mechanitorCombatFirstPawnDefNames);
            ReplaceStrings(
                source.mechanitorCombatSecondPawnDefNames,
                result.mechanitorCombatSecondPawnDefNames);
            cached = result;
            cachedLanguage = LanguageDatabase.activeLanguage;
            return result;
        }

        private static void CopyOpportunityBands(
            List<DiaryBiotechOpportunityBandDef> source,
            BiotechPolicySnapshot destination)
        {
            if (source == null || source.Count == 0) return;
            List<BiotechOpportunityBandRule> copied = new List<BiotechOpportunityBandRule>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBiotechOpportunityBandDef band = source[i];
                if (band != null && !string.IsNullOrWhiteSpace(band.token)
                    && band.minimumTier >= 0 && band.maximumTier <= 8 && band.minimumTier <= band.maximumTier)
                {
                    copied.Add(new BiotechOpportunityBandRule
                    {
                        minimumTier = band.minimumTier,
                        maximumTier = band.maximumTier,
                        token = band.token.Trim(),
                        description = band.description ?? string.Empty
                    });
                }
            }

            if (copied.Count > 0) destination.opportunityBands = copied;
        }

        private static void CopyObservationBands(
            List<DiaryBiotechObservationBandDef> source,
            BiotechPolicySnapshot destination)
        {
            if (source == null || source.Count == 0) return;
            List<BiotechObservationBandRule> copied = new List<BiotechObservationBandRule>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBiotechObservationBandDef band = source[i];
                if (band != null && band.minimumEvidence > 0 && !string.IsNullOrWhiteSpace(band.token))
                {
                    copied.Add(new BiotechObservationBandRule
                    {
                        minimumEvidence = band.minimumEvidence,
                        token = band.token.Trim(),
                        description = band.description ?? string.Empty
                    });
                }
            }

            if (copied.Count > 0) destination.observationBands = copied;
        }

        private static void CopyStrings(List<string> source, List<string> destination)
        {
            if (source == null) return;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string value = (source[i] ?? string.Empty).Trim();
                if (value.Length > 0 && seen.Add(value)) destination.Add(value);
            }
        }

        private static GeneSaliencePolicySnapshot CopyGeneSalience(
            DiaryBiotechPolicyDef source,
            GeneSaliencePolicySnapshot fallback)
        {
            GeneSaliencePolicySnapshot result = new GeneSaliencePolicySnapshot
            {
                maximumThemes = source.geneMaximumThemes < 1
                    || source.geneMaximumThemes > GeneSaliencePolicySnapshot.HardMaximumThemes
                    ? fallback.maximumThemes : source.geneMaximumThemes,
                deltaBonus = Math.Max(0, source.geneDeltaBonus),
                xenogeneBonus = source.geneXenogeneBonus,
                endogeneBonus = source.geneEndogeneBonus,
                duplicateCategoryPenalty = Math.Max(0, source.geneDuplicateCategoryPenalty),
                forcedDefNameBonus = Math.Max(0, source.geneForcedDefNameBonus),
                labelCharacterLimit = TextLimit(source.geneLabelCharacterLimit, fallback.labelCharacterLimit),
                descriptionCharacterLimit = TextLimit(
                    source.geneDescriptionCharacterLimit,
                    fallback.descriptionCharacterLimit),
                totalTextCharacterLimit = TextLimit(
                    source.geneTotalTextCharacterLimit,
                    fallback.totalTextCharacterLimit),
                maximumObservedGeneDefNames = source.geneMaximumObservedDefNames < 1
                    || source.geneMaximumObservedDefNames > GeneIdentityObservationPolicy.HardMaximumGeneDefNames
                    ? fallback.maximumObservedGeneDefNames : source.geneMaximumObservedDefNames,
                minimumFallbackGeneChanges = source.geneMinimumFallbackChanges < 1
                    || source.geneMinimumFallbackChanges > GeneIdentityObservationPolicy.HardMaximumGeneDefNames
                    ? fallback.minimumFallbackGeneChanges : source.geneMinimumFallbackChanges
            };

            if (source.geneCategoryWeights != null)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < source.geneCategoryWeights.Count; i++)
                {
                    DiaryGeneCategoryWeightDef row = source.geneCategoryWeights[i];
                    string category = row == null ? string.Empty : (row.category ?? string.Empty).Trim();
                    if (category.Length > 0 && seen.Add(category))
                    {
                        result.categoryWeights.Add(new GeneCategoryWeightRule
                        {
                            category = category,
                            weight = row.weight
                        });
                    }
                }
            }
            if (result.categoryWeights.Count == 0)
                result.categoryWeights = fallback.categoryWeights;

            CopyStrings(source.geneForceIncludeDefNames, result.forceIncludeDefNames);
            CopyStrings(source.geneExcludeDefNames, result.excludeDefNames);
            CopyStrings(source.geneAllowDuplicateCategories, result.allowDuplicateCategories);
            return result;
        }

        private static void ReplaceStrings(List<string> source, List<string> destination)
        {
            if (!DiaryBiotechPolicyDef.HasNonBlankMatcher(source)) return;
            destination.Clear();
            CopyStrings(source, destination);
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static int TextLimit(int value, int fallback)
        {
            return value < 1 || value > GeneSaliencePolicySnapshot.HardMaximumTextCharacters
                ? fallback : value;
        }

    }
}
