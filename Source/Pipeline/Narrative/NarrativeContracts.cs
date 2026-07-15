// Shared, pure contracts for the future DLC Narrative Continuity Layer. Source-specific DLC adapters
// will eventually project visible event facts into these DTOs; this file deliberately knows nothing
// about RimWorld, Verse, settings, Defs, saves, or prompt transport.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety" and "architecture barriers").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Frozen schema tokens for why a source-owned event matters narratively.</summary>
    internal static class NarrativeFacetTokens
    {
        public const string IdentityTransition = "identity_transition";
        public const string BondLifecycle = "bond_lifecycle";
        public const string JourneyChapter = "journey_chapter";
        public const string AmbientPressure = "ambient_pressure";

        public static bool IsKnown(string value)
        {
            return value == IdentityTransition || value == BondLifecycle
                || value == JourneyChapter || value == AmbientPressure;
        }
    }

    /// <summary>Frozen source salience tokens. They are schema values, not player-facing prose.</summary>
    internal static class NarrativeSalienceTokens
    {
        public const string Minor = "minor";
        public const string Meaningful = "meaningful";
        public const string Major = "major";
        public const string Terminal = "terminal";

        public static bool IsKnown(string value)
        {
            return value == Minor || value == Meaningful || value == Major || value == Terminal;
        }
    }

    /// <summary>Frozen subject-kind tokens for plain references and exact-subject matching.</summary>
    internal static class NarrativeSubjectKindTokens
    {
        public const string Pawn = "pawn";
        public const string Weapon = "weapon";
        public const string Mech = "mech";
        public const string Animal = "animal";
        public const string Family = "family";
        public const string Colony = "colony";
        public const string Ship = "ship";
        public const string Entity = "entity";
        public const string Place = "place";

        public static bool IsKnownOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value)
                || value == Pawn || value == Weapon || value == Mech || value == Animal
                || value == Family || value == Colony || value == Ship || value == Entity
                || value == Place;
        }
    }

    /// <summary>Frozen provider keys. A provider is an adapter identity, never prompt prose.</summary>
    internal static class NarrativeProviderTokens
    {
        public const string Royalty = "royalty";
        public const string Ideology = "ideology";
        public const string Biotech = "biotech";
        public const string Anomaly = "anomaly";
        public const string Odyssey = "odyssey";
        public const string Core = "core";

        public static bool IsKnown(string value)
        {
            return value == Royalty || value == Ideology || value == Biotech || value == Anomaly
                || value == Odyssey || value == Core;
        }
    }

    /// <summary>Frozen lens categories. Selection allows at most one candidate per category.</summary>
    internal static class NarrativeCategoryTokens
    {
        public const string Identity = "identity";
        public const string Bond = "bond";
        public const string Interpretation = "interpretation";
        public const string Chapter = "chapter";
        public const string Pressure = "pressure";
        public const string Home = "home";

        public static bool IsKnown(string value)
        {
            return value == Identity || value == Bond || value == Interpretation
                || value == Chapter || value == Pressure || value == Home;
        }
    }

    /// <summary>Frozen relevance levels used for scoring and deterministic tie-breaking.</summary>
    internal static class NarrativeRelationshipTokens
    {
        public const string ExactArc = "exact_arc";
        public const string ExactSubject = "exact_subject";
        public const string DirectTopic = "direct_topic";
        public const string DirectFacet = "direct_facet";
        public const string Ambient = "ambient";
        public const string None = "none";

        public static int Rank(string value)
        {
            if (value == ExactArc) return 5;
            if (value == ExactSubject) return 4;
            if (value == DirectTopic) return 3;
            if (value == DirectFacet) return 2;
            if (value == Ambient) return 1;
            return 0;
        }
    }

    /// <summary>Stable request tokens that mirror the existing Full/Balanced/Compact prompt presets.</summary>
    internal static class NarrativeDetailLevelTokens
    {
        public const string Full = "full";
        public const string Balanced = "balanced";
        public const string Compact = "compact";

        public static string Normalize(string value)
        {
            if (string.Equals(value, Full, StringComparison.OrdinalIgnoreCase)) return Full;
            if (string.Equals(value, Compact, StringComparison.OrdinalIgnoreCase)) return Compact;
            return Balanced;
        }
    }

    /// <summary>Stable reflection-opportunity kinds, ordered by policy rather than call order.</summary>
    internal static class NarrativeReflectionKindTokens
    {
        public const string MajorArc = "major_arc";
        public const string CrossArc = "cross_arc";
        public const string Belief = "belief";
        public const string Quadrum = "quadrum";
        public const string Day = "day";

        public static bool IsKnown(string value)
        {
            return value == MajorArc || value == CrossArc || value == Belief
                || value == Quadrum || value == Day;
        }
    }

    /// <summary>Frozen additive Scribe-key suffixes used by N1's hot POV and archive save fields.</summary>
    internal static class NarrativeSaveKeys
    {
        public const string Evidence = "narrativeEvidence";
        public const string References = "narrativeReferences";
        public const string Context = "narrativeContext";
        public const string SelectedCandidateKeys = "narrativeSelectedCandidateKeys";
        public const string ArchiveReferences = "narrativeReferences";
        public const string ArchiveSelectedCandidateKeys = "narrativeSelectedCandidateKeys";
    }

    /// <summary>Stable diagnostic tokens emitted by the pure selector and reflection planner.</summary>
    internal static class NarrativeDiagnosticTokens
    {
        public const string Selected = "selected";
        public const string PolicyDisabled = "policy_disabled";
        public const string NoEvidence = "no_evidence";
        public const string CandidateCap = "candidate_cap";
        public const string UnknownKnowledge = "knowledge_unknown";
        public const string EmptyCandidateKey = "empty_candidate_key";
        public const string EmptyCandidateText = "empty_candidate_text";
        public const string UnknownProvider = "unknown_provider";
        public const string UnknownFacet = "unknown_facet";
        public const string UnknownCategory = "unknown_category";
        public const string ProviderUnavailable = "provider_unavailable";
        public const string FutureSource = "future_source";
        public const string TooOld = "too_old";
        public const string Unrelated = "unrelated";
        public const string PrimaryFactDuplicate = "primary_fact_duplicate";
        public const string SubjectConflict = "subject_conflict";
        public const string AmbientDisconnected = "ambient_disconnected";
        public const string CategoryCap = "category_cap";
        public const string InterpretationCap = "interpretation_cap";
        public const string PairConflict = "pair_conflict";
        public const string Redundant = "redundant";
        public const string DetailCap = "detail_cap";
        public const string CharacterBudget = "character_budget";
        public const string ReflectionNotDue = "reflection_not_due";
        public const string ReflectionAlreadyWritten = "reflection_already_written";
        public const string ReflectionCooldown = "reflection_cooldown";
        public const string ReflectionNeedsLink = "reflection_needs_link";
        public const string ReflectionNeedsMemories = "reflection_needs_memories";
        public const string ReflectionSpanExceeded = "reflection_span_exceeded";
    }

    /// <summary>
    /// One source-owned, event-time statement of why an already-authorized event matters to one POV.
    /// Hidden facts leave <see cref="pawnCanKnow"/> null/false and consequently fail closed.
    /// </summary>
    internal class NarrativeEvidence
    {
        public string eventId = string.Empty;
        public int tick;
        public string povPawnId = string.Empty;
        public string povRole = string.Empty;
        public string facet = string.Empty;
        public string phase = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string subjectLabel = string.Empty;
        public string arcKey = string.Empty;
        public string relatedEventId = string.Empty;
        public List<string> beliefTopics = new List<string>();
        public string salience = NarrativeSalienceTokens.Minor;
        public bool? pawnCanKnow;
        public string sourceDomain = string.Empty;
        public string sourceDefName = string.Empty;
    }

    /// <summary>Minimal, prose-free continuity pointer saved on a hot or archived POV row in N1.</summary>
    internal class NarrativeReference
    {
        public string facet = string.Empty;
        public string phase = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string arcKey = string.Empty;
        public string sourceEventId = string.Empty;
        public int sourceTick;
    }

    /// <summary>One guarded provider's factual proposal for optional cross-event prompt context.</summary>
    internal class NarrativeLensCandidate
    {
        public string candidateKey = string.Empty;
        public string provider = string.Empty;
        public string category = string.Empty;
        public string text = string.Empty;
        public string facet = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string arcKey = string.Empty;
        public List<string> topicTokens = new List<string>();
        public string sourceEventId = string.Empty;
        public int sourceTick;
        public string salience = NarrativeSalienceTokens.Minor;
        public string relationship = NarrativeRelationshipTokens.None;
        public bool pawnCanKnow;
        public bool isPrimaryEventFact;

        // The provider adapter sets these plain booleans after its DLC/configuration checks. They keep
        // the selector pure while making its fail-closed contract directly testable.
        public bool providerAvailable = true;
        public bool hasVerifiedPovConnection = true;
    }

    /// <summary>One XML-owned score row copied into a plain policy snapshot.</summary>
    internal class NarrativeTokenWeight
    {
        public string token = string.Empty;
        public float score = 0f;
    }

    /// <summary>One optional evidence-to-candidate topic/facet affinity row.</summary>
    internal class NarrativeAffinityRule
    {
        public string evidenceToken = string.Empty;
        public string candidateToken = string.Empty;
        public float score = 0f;
    }

    /// <summary>One explicit category-pair override. Missing pairs use the safe default: different is allowed.</summary>
    internal class NarrativeCategoryCoexistenceRule
    {
        public string firstCategory = string.Empty;
        public string secondCategory = string.Empty;
        public bool allowed = true;
    }

    /// <summary>Detail-level cap and character budget. The selector never truncates an individual fact.</summary>
    internal class NarrativeDetailBudget
    {
        public string detailLevel = NarrativeDetailLevelTokens.Balanced;
        public int maxLenses = 1;
        public int characterBudget = 180;
        public bool allowExactArcPair;
        public int exactArcPairMaxCharacters = 72;
    }

    /// <summary>XML-owned reflection priority and per-kind cooldown row.</summary>
    internal class NarrativeReflectionPriority
    {
        public string kind = string.Empty;
        public int priority;
        public int cooldownTicks;
    }

    /// <summary>
    /// Pure copy of the narrative-continuity Def. It has safe built-in defaults so missing or malformed
    /// XML produces no provider-side failure; N1 invokes it only through its main-thread adapter and
    /// still has no live DLC provider or source hook.
    /// </summary>
    internal class NarrativePolicySnapshot
    {
        public bool enabled = true;
        public int maxEvidencePerPov = 3;
        public int maxCandidates = 12;
        public int maxSelectedCandidates = 2;
        public int maxRecentSelectedCandidateKeys = 12;
        public int maximumCandidateAgeTicks = 180000;
        public int ageDecayWindowTicks = 60000;
        public float ageDecayFloor = 0.35f;
        public float repetitionPenalty = 45f;
        public float exactArcRepetitionPenalty = 0f;
        public string promptFieldLabel = "narrative context";
        public string promptFieldInstruction = string.Empty;
        public List<NarrativeDetailBudget> detailBudgets = new List<NarrativeDetailBudget>();
        public List<NarrativeTokenWeight> relationshipScores = new List<NarrativeTokenWeight>();
        public List<NarrativeTokenWeight> facetScores = new List<NarrativeTokenWeight>();
        public List<NarrativeTokenWeight> categoryScores = new List<NarrativeTokenWeight>();
        public List<NarrativeTokenWeight> salienceScores = new List<NarrativeTokenWeight>();
        public List<NarrativeTokenWeight> providerScores = new List<NarrativeTokenWeight>();
        public List<NarrativeAffinityRule> affinityRules = new List<NarrativeAffinityRule>();
        public List<NarrativeCategoryCoexistenceRule> categoryCoexistence =
            new List<NarrativeCategoryCoexistenceRule>();
        public int reflectionGlobalCooldownTicks = 60000;
        public int reflectionMinimumLinkedMemories = 2;
        public int reflectionMemoryCap = 8;
        public int reflectionMaximumSpanTicks = 3600000;
        public List<NarrativeReflectionPriority> reflectionPriorities =
            new List<NarrativeReflectionPriority>();

        /// <summary>Returns a fresh default policy; callers can safely adjust a test snapshot in place.</summary>
        public static NarrativePolicySnapshot CreateDefault()
        {
            NarrativePolicySnapshot policy = new NarrativePolicySnapshot();
            policy.detailBudgets.Add(new NarrativeDetailBudget
            {
                detailLevel = NarrativeDetailLevelTokens.Full,
                maxLenses = 2,
                characterBudget = 320,
                allowExactArcPair = true,
                exactArcPairMaxCharacters = 120
            });
            policy.detailBudgets.Add(new NarrativeDetailBudget
            {
                detailLevel = NarrativeDetailLevelTokens.Balanced,
                maxLenses = 1,
                characterBudget = 180,
                allowExactArcPair = true,
                exactArcPairMaxCharacters = 72
            });
            policy.detailBudgets.Add(new NarrativeDetailBudget
            {
                detailLevel = NarrativeDetailLevelTokens.Compact,
                maxLenses = 1,
                characterBudget = 110,
                allowExactArcPair = false,
                exactArcPairMaxCharacters = 56
            });

            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.ExactArc, 100f);
            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.ExactSubject, 80f);
            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.DirectTopic, 60f);
            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.DirectFacet, 40f);
            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.Ambient, 20f);
            AddWeight(policy.relationshipScores, NarrativeRelationshipTokens.None, 0f);
            AddWeight(policy.facetScores, NarrativeFacetTokens.IdentityTransition, 15f);
            AddWeight(policy.facetScores, NarrativeFacetTokens.BondLifecycle, 15f);
            AddWeight(policy.facetScores, NarrativeFacetTokens.JourneyChapter, 14f);
            AddWeight(policy.facetScores, NarrativeFacetTokens.AmbientPressure, 10f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Identity, 12f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Bond, 12f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Interpretation, 10f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Chapter, 12f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Pressure, 8f);
            AddWeight(policy.categoryScores, NarrativeCategoryTokens.Home, 8f);
            AddWeight(policy.salienceScores, NarrativeSalienceTokens.Minor, 2f);
            AddWeight(policy.salienceScores, NarrativeSalienceTokens.Meaningful, 8f);
            AddWeight(policy.salienceScores, NarrativeSalienceTokens.Major, 15f);
            AddWeight(policy.salienceScores, NarrativeSalienceTokens.Terminal, 20f);
            policy.reflectionPriorities.Add(new NarrativeReflectionPriority
            {
                kind = NarrativeReflectionKindTokens.MajorArc,
                priority = 500,
                cooldownTicks = 120000
            });
            policy.reflectionPriorities.Add(new NarrativeReflectionPriority
            {
                kind = NarrativeReflectionKindTokens.CrossArc,
                priority = 400,
                cooldownTicks = 120000
            });
            policy.reflectionPriorities.Add(new NarrativeReflectionPriority
            {
                kind = NarrativeReflectionKindTokens.Belief,
                priority = 300,
                cooldownTicks = 120000
            });
            policy.reflectionPriorities.Add(new NarrativeReflectionPriority
            {
                kind = NarrativeReflectionKindTokens.Quadrum,
                priority = 200,
                cooldownTicks = 60000
            });
            policy.reflectionPriorities.Add(new NarrativeReflectionPriority
            {
                kind = NarrativeReflectionKindTokens.Day,
                priority = 100,
                cooldownTicks = 60000
            });
            return policy;
        }

        private static void AddWeight(List<NarrativeTokenWeight> target, string token, float score)
        {
            target.Add(new NarrativeTokenWeight { token = token, score = score });
        }
    }

    /// <summary>Selector input. All values are plain event-time data and an already-copied policy.</summary>
    internal class NarrativeContextRequest
    {
        public List<NarrativeEvidence> evidence = new List<NarrativeEvidence>();
        public List<NarrativeLensCandidate> candidates = new List<NarrativeLensCandidate>();
        public NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
        public int currentTick;
        public int deterministicSeed = 1;
        public List<string> recentSelectedCandidateKeys = new List<string>();
        public string detailLevel = NarrativeDetailLevelTokens.Balanced;
        public int promptCharacterBudget = 0;
    }

    /// <summary>One accepted/rejected candidate diagnostic. Values are stable debug/save-safe tokens.</summary>
    internal class NarrativeCandidateDiagnostic
    {
        public string candidateKey = string.Empty;
        public bool selected;
        public string reason = string.Empty;
        public string relationship = NarrativeRelationshipTokens.None;
        public float score;
    }

    /// <summary>Pure selection output copied into a bounded per-POV N1 persistence seam.</summary>
    internal class NarrativeContextSelection
    {
        public List<NarrativeLensCandidate> selectedCandidates = new List<NarrativeLensCandidate>();
        public string narrativeContext = string.Empty;
        public List<NarrativeReference> references = new List<NarrativeReference>();
        public List<string> selectionReasons = new List<string>();
        public List<NarrativeCandidateDiagnostic> diagnostics = new List<NarrativeCandidateDiagnostic>();
        public bool selectedInterpretation;
    }

    /// <summary>One reflection possibility. It authorizes nothing until a runtime scheduler dispatches it.</summary>
    internal class ReflectionOpportunity
    {
        public string kind = string.Empty;
        public string pawnId = string.Empty;
        public int nowTick;
        public List<string> sourceEventIds = new List<string>();
        public List<string> arcKeys = new List<string>();
        public int candidateMemoryCount;
        public int linkedMemoryCount;
        public int memorySpanTicks = 0;
        public string importance = NarrativeSalienceTokens.Minor;
        public bool due;
        public bool alreadyWritten;
        public bool cooldownSatisfied = true;
        public bool hasCoherentLink;
        public bool hasPhaseChange;
        public bool groupEnabled = true;
    }

    /// <summary>One recorded reflection time for global/per-kind pure cooldown evaluation.</summary>
    internal class ReflectionHistoryEntry
    {
        public string kind = string.Empty;
        public int writtenTick = -1;
    }

    /// <summary>Reflection-planning input. The impure scheduler owns the actual success/failure outcome.</summary>
    internal class ReflectionPlanningRequest
    {
        public List<ReflectionOpportunity> opportunities = new List<ReflectionOpportunity>();
        public NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
        public int currentTick;
        public int lastReflectionTick = -1;
        public List<ReflectionHistoryEntry> history = new List<ReflectionHistoryEntry>();
    }

    /// <summary>Exact deferred-state instruction: consume only after the runtime creates the chosen page.</summary>
    internal class ReflectionStateConsumption
    {
        public string kind = string.Empty;
        public List<string> sourceEventIds = new List<string>();
        public List<string> arcKeys = new List<string>();
        public bool consumeAfterSuccessfulDispatch = true;
        public bool advanceDebtWhenGroupDisabled;
    }

    /// <summary>At-most-one reflection choice and stable diagnostics for every rejected opportunity.</summary>
    internal class ReflectionPlan
    {
        public ReflectionOpportunity selectedOpportunity;
        public ReflectionStateConsumption consumption;
        public List<ReflectionStateConsumption> stateInstructions = new List<ReflectionStateConsumption>();
        public List<NarrativeCandidateDiagnostic> diagnostics = new List<NarrativeCandidateDiagnostic>();
    }
}
