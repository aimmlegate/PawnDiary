// Plain contracts and stable schema tokens for Anomaly study and containment work. Live RimWorld
// adapters are intentionally absent from this Phase-A1.0 file: later phases must copy game state
// into these DTOs before invoking the pure policies beside it.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Stable synthetic event Def names frozen before catalog registration.</summary>
    internal static class AnomalyEventDefNames
    {
        public const string StudyBreakthrough = "PawnDiary_AnomalyStudyBreakthrough";
        public const string ContainmentBreach = "PawnDiary_ContainmentBreach";
        public const string CreepJoinerOutcome = "PawnDiary_CreepJoinerOutcome";
        public const string GhoulTransformation = "PawnDiary_GhoulTransformation";
        public const string VoidOutcome = "PawnDiary_VoidOutcome";
    }

    /// <summary>Stable structured context keys; these schema labels deliberately remain English.</summary>
    internal static class AnomalyContextKeys
    {
        public const string Kind = "anomaly_kind";
        public const string StudyStage = "study_stage";
        public const string StudiedDef = "studied_def";
        public const string KnowledgeCategory = "knowledge_category";
        public const string MonolithPreviousLevel = "monolith_previous_level";
        public const string MonolithReachedLevel = "monolith_reached_level";
        public const string MonolithBecameActivatable = "monolith_became_activatable";
        public const string EscapedCount = "escaped_count";
        public const string EscapedEntities = "escaped_entities";
        public const string AdditionalEscapedCount = "additional_escaped_count";
        public const string WitnessRole = "witness_role";
        public const string CreepJoinerPhase = "creepjoiner_phase";
        public const string VisibleResult = "visible_result";
        public const string Transformation = "transformation";
        public const string VoidOutcome = "void_outcome";
        public const string Terminal = "terminal";
    }

    /// <summary>Stable values for <c>anomaly_kind</c>.</summary>
    internal static class AnomalyKindTokens
    {
        public const string StudyBreakthrough = "study_breakthrough";
        public const string ContainmentBreach = "containment_breach";
        public const string CreepJoinerOutcome = "creepjoiner_outcome";
        public const string GhoulTransformation = "ghoul_transformation";
        public const string VoidOutcome = "void_outcome";
    }

    /// <summary>Stable semantic study-stage values; raw point totals never replace these tokens.</summary>
    internal static class AnomalyStudyStageTokens
    {
        public const string FirstBreakthrough = "first_breakthrough";
        public const string CompletedKind = "completed_kind";
        public const string Promoted = "promoted";
    }

    /// <summary>Truthful witness-role values assigned only from captured structural evidence.</summary>
    internal static class AnomalyWitnessRoleTokens
    {
        public const string Nearby = "nearby";
        public const string RecentStudier = "recent_studier";
        public const string ColonyWitness = "colony_witness";
    }

    /// <summary>Stable future-facing visible outcome tokens frozen with the A1 schema.</summary>
    internal static class AnomalyOutcomeTokens
    {
        public const string SurgicalReveal = "surgical_reveal";
        public const string Rejected = "rejected";
        public const string Aggressive = "aggressive";
        public const string Departed = "departed";
        public const string Ghoul = "ghoul";
        public const string Embraced = "embraced";
        public const string Disrupted = "disrupted";
    }

    /// <summary>Defensive structural ceilings which protect malformed XML and extreme mod input.</summary>
    internal static class AnomalyPolicyLimits
    {
        public const int DefaultContainmentWriters = 2;
        public const int MaximumContainmentWriters = 2;
        public const int DefaultEntityLabels = 3;
        public const int MaximumEntityLabels = 8;
        public const int DefaultWitnessRadius = 12;
        public const int MaximumWitnessRadius = 100;
        public const int DefaultTaleOwnershipDepth = 8;
        public const int MaximumTaleOwnershipDepth = 32;
    }

    /// <summary>Pure result category for one observed study transition.</summary>
    internal enum AnomalyStudyDisposition
    {
        DropInvalid,
        StateOnly,
        Generate
    }

    /// <summary>Committed before/after facts copied from one exact study call.</summary>
    internal sealed class AnomalyStudyFacts
    {
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public string studiedLabel = string.Empty;
        public string codexEntryDefName = string.Empty;
        public string knowledgeCategoryDefName = string.Empty;
        public string studierPawnId = string.Empty;
        public bool studierEligible;
        public int oldProgress;
        public int newProgress;
        public int noteCount;
        public bool completedBefore;
        public bool completedAfter;
        public bool isContainedEntity;
        public bool isMonolith;
        public bool monolithActivatableBefore;
        public bool monolithActivatableAfter;
    }

    /// <summary>Detached study history used to prevent retroactive or repeated milestones.</summary>
    internal sealed class AnomalyStudyHistorySnapshot
    {
        public bool firstBreakthroughObserved;
        public readonly List<string> completedStudyDefNames = new List<string>();
        public readonly List<string> observedPromotionKeys = new List<string>();
    }

    /// <summary>One XML-owned exact study promotion expressed without an optional Def reference.</summary>
    internal sealed class AnomalyStudyMilestoneRule
    {
        public string studiedDefName = string.Empty;
        public int minimumProgress;
        public string token = string.Empty;
    }

    /// <summary>Additive history mutation returned by the pure study policy.</summary>
    internal sealed class AnomalyStudyHistoryMutation
    {
        public bool observeFirstBreakthrough;
        public string completedStudyDefName = string.Empty;
        public readonly List<string> observedPromotionKeys = new List<string>();
    }

    /// <summary>Pure study decision plus the exact state that a later adapter may persist.</summary>
    internal sealed class AnomalyStudyPlan
    {
        public AnomalyStudyDisposition disposition;
        public string stageToken = string.Empty;
        public string promotionToken = string.Empty;
        public bool monolithBecameActivatable;
        public readonly AnomalyStudyHistoryMutation historyMutation = new AnomalyStudyHistoryMutation();
    }

    /// <summary>One verified entity captured before and after an involuntary escape.</summary>
    internal sealed class ContainedEntityFact
    {
        public string entityId = string.Empty;
        public string visibleLabel = string.Empty;
        public string defName = string.Empty;
        public string mutantDefName = string.Empty;
        public int platformX;
        public int platformZ;
        public bool escaped;
    }

    /// <summary>Plain candidate evidence used for deterministic containment witness selection.</summary>
    internal sealed class AnomalyWriterCandidate
    {
        public string pawnId = string.Empty;
        public bool eligible;
        public bool onAffectedMap;
        public bool nearbyCandidate;
        public bool recentExactStudier;
        public bool freeColonistOnHomeMap;
        public int distanceSquared;
        public int distanceBucket;
        public string tieBreakKey = string.Empty;
    }

    /// <summary>One outer containment escape and all verified nested entities/candidates.</summary>
    internal sealed class ContainmentEscapeFacts
    {
        public string escapeId = string.Empty;
        public int tick = -1;
        public int mapId = -1;
        public bool outerEscape;
        public bool sameRoomCascade;
        public readonly List<ContainedEntityFact> entities = new List<ContainedEntityFact>();
        public readonly List<AnomalyWriterCandidate> witnesses = new List<AnomalyWriterCandidate>();
    }

    /// <summary>Selected writer identity and the exact role that authorized it.</summary>
    internal sealed class AnomalyWriterSelection
    {
        public string pawnId = string.Empty;
        public string roleToken = string.Empty;
    }

    /// <summary>Pure containment plan with bounded context entities and deterministic authors.</summary>
    internal sealed class ContainmentBreachPlan
    {
        public bool valid;
        public bool writePage;
        public string dedupKey = string.Empty;
        public int escapedCount;
        public int additionalEscapedCount;
        public readonly List<ContainedEntityFact> contextEntities = new List<ContainedEntityFact>();
        public readonly List<AnomalyWriterSelection> selectedWriters = new List<AnomalyWriterSelection>();
    }

    /// <summary>Accepted dedicated-page ownership awaiting one exact generic StudiedEntity Tale.</summary>
    internal sealed class AnomalyStudyTaleClaim
    {
        public string studierPawnId = string.Empty;
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public int acceptedTick = -1;
        public bool consumed;
    }

    /// <summary>Plain identity facts copied from one generic StudiedEntity Tale.</summary>
    internal sealed class AnomalyStudiedTaleFacts
    {
        public string studierPawnId = string.Empty;
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public int tick = -1;
    }

    /// <summary>Pure outcome for one claim/Tale comparison.</summary>
    internal sealed class AnomalyTaleOwnershipDecision
    {
        public bool suppress;
        public bool removeClaim;
        public AnomalyStudyTaleClaim nextClaim;
    }

    /// <summary>XML-owned Anomaly policy detached from Verse and safe for standalone tests.</summary>
    internal sealed class AnomalyPolicySnapshot
    {
        public bool studyEnabled;
        public bool recordFirstStudyBreakthrough;
        public bool recordCompletedEntityKind;
        public readonly List<AnomalyStudyMilestoneRule> promotedStudyMilestones =
            new List<AnomalyStudyMilestoneRule>();
        public int monolithKnowledgeMaxAgeTicks;
        public int studyTaleSuppressionTicks;
        public bool containmentEnabled;
        public int containmentWitnessRadius;
        public int containmentMaxWriters;
        public int containmentMaxEntityLabelsInContext;
        public int containmentDedupTicks;
        public int recentStudierMaxAgeTicks;
        public bool creepJoinerEnabled;
        public int creepJoinerOutcomeDedupTicks;
        public int creepJoinerArcRetentionTicks;
        public int creepJoinerMaxWitnesses;
        public bool ghoulTransformationEnabled;
        public bool voidOutcomeEnabled;
        public int taleOwnershipMaxDepth;
        public int taleOwnershipExpiryTicks;

        /// <summary>Creates conservative defaults without prompt prose or DLC identifiers.</summary>
        public static AnomalyPolicySnapshot CreateDefault()
        {
            return new AnomalyPolicySnapshot
            {
                studyEnabled = true,
                recordFirstStudyBreakthrough = true,
                recordCompletedEntityKind = true,
                monolithKnowledgeMaxAgeTicks = 60000,
                studyTaleSuppressionTicks = 2500,
                containmentEnabled = true,
                containmentWitnessRadius = AnomalyPolicyLimits.DefaultWitnessRadius,
                containmentMaxWriters = AnomalyPolicyLimits.DefaultContainmentWriters,
                containmentMaxEntityLabelsInContext = AnomalyPolicyLimits.DefaultEntityLabels,
                containmentDedupTicks = 2500,
                recentStudierMaxAgeTicks = 60000,
                creepJoinerEnabled = true,
                creepJoinerOutcomeDedupTicks = 2500,
                creepJoinerArcRetentionTicks = 3600000,
                creepJoinerMaxWitnesses = 2,
                ghoulTransformationEnabled = true,
                voidOutcomeEnabled = true,
                taleOwnershipMaxDepth = AnomalyPolicyLimits.DefaultTaleOwnershipDepth,
                taleOwnershipExpiryTicks = 2500
            };
        }
    }
}
