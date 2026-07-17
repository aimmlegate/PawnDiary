// Plain gene-identity contracts for Biotech Phase 5. Live RimWorld adapters will project Gene
// objects into these detached facts later; pure selection and tests must never receive a Gene,
// Pawn, Def, or other Verse object.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Stable structural category tokens used by XML scoring and prompt-safe themes.</summary>
    internal static class GeneCategoryTokens
    {
        public const string Ability = "ability";
        public const string Trait = "trait";
        public const string Resource = "resource";
        public const string Need = "need";
        public const string Appearance = "appearance";
        public const string Aging = "aging";
        public const string Environment = "environment";
        public const string Violence = "violence";
        public const string Emotion = "emotion";
        public const string Social = "social";
        public const string Capacity = "capacity";
        public const string Stat = "stat";
        public const string Other = "other";
    }

    /// <summary>
    /// One cleaned, prompt-safe gene fact. Flags describe structural effects only; raw stat values
    /// and live game references deliberately do not cross this contract.
    /// </summary>
    internal class GeneFact
    {
        public string defName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public bool isEndogene;
        public bool isXenogene;
        public bool hidden;
        public bool active = true;
        public bool suppressed;
        public bool affectsAbility;
        public bool affectsTrait;
        public bool affectsResource;
        public bool affectsNeed;
        public bool affectsAppearance;
        public bool affectsAging;
        public bool affectsEnvironment;
        public bool affectsViolence;
        public bool affectsEmotion;
        public bool affectsSocial;
        public bool affectsCapacity;
        public bool affectsStat;
        public string magnitudeBand = string.Empty;
    }

    /// <summary>Current detached gene membership used as the unchanged salience candidate pool.</summary>
    internal class GeneIdentitySnapshot
    {
        public string xenotypeDefName = string.Empty;
        public string xenotypeLabel = string.Empty;
        // Bounded installed membership is separate from active salience facts. Temporary override/
        // suppression changes therefore cannot masquerade as gene addition/removal.
        public List<string> installedGeneDefNames = new List<string>();
        public List<GeneFact> genes = new List<GeneFact>();
    }

    /// <summary>
    /// Detached saved-state shape used by the pure observation policy. The Verse-facing model copies
    /// these fields into Scribe keys; this DTO itself has no persistence or game dependency.
    /// </summary>
    internal class GeneIdentityObservationSnapshot
    {
        public int observationVersion;
        public string xenotypeDefName = string.Empty;
        public string xenotypeLabel = string.Empty;
        public List<string> geneDefNames = new List<string>();
    }

    /// <summary>
    /// Exact gene deltas captured around a mutation. Removed facts retain their pre-change cleaned
    /// description, so selection does not need to recover a despawned or deleted live Gene.
    /// </summary>
    internal class GeneMutationSnapshot
    {
        public List<GeneFact> addedGenes = new List<GeneFact>();
        public List<GeneFact> removedGenes = new List<GeneFact>();
    }

    /// <summary>Pure before/after result used by exact hooks and the slow fallback observer.</summary>
    internal class GeneIdentityTransitionDecision
    {
        public bool xenotypeIdentityChanged;
        public int addedGeneCount;
        public int removedGeneCount;
        public GeneMutationSnapshot mutation = new GeneMutationSnapshot();
        public List<GeneTheme> themes = new List<GeneTheme>();

        public bool HasAnyChange => xenotypeIdentityChanged
            || addedGeneCount > 0 || removedGeneCount > 0;
    }

    /// <summary>Stable mutation tokens emitted by the pure selector.</summary>
    internal static class GeneChangeTokens
    {
        public const string Added = "added";
        public const string Removed = "removed";
        public const string Unchanged = "unchanged";
    }

    /// <summary>Stable, non-localized cause tokens stored in event-time context.</summary>
    internal static class GeneChangeCauseTokens
    {
        public const string XenogermImplant = "xenogerm_implant";
        public const string XenogermReimplant = "xenogerm_reimplant";
        public const string ObservedChange = "observed_change";
    }

    /// <summary>One bounded gene theme selected for later prompt formatting.</summary>
    internal class GeneTheme
    {
        public string defName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public string category = GeneCategoryTokens.Other;
        public string change = GeneChangeTokens.Unchanged;
        public string magnitudeBand = string.Empty;
        public int score;
    }
}
