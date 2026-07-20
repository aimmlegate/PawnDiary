// Plain contracts and stable schema tokens for Anomaly study, containment, and visible creepjoiner
// work. Live RimWorld adapters are intentionally absent: callers copy game state into these DTOs
// before invoking the pure policies beside them.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
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

    /// <summary>Frozen additive save keys for A1 study history and monolith enrichment.</summary>
    internal static class AnomalySaveKeys
    {
        public const string SupportSchemaVersion = "anomalySupportSchemaVersion";
        public const string FirstStudyBreakthroughObserved =
            "anomalyFirstStudyBreakthroughObserved";
        public const string CompletedStudyDefNames = "anomalyCompletedStudyDefNames";
        public const string PromotedStudyMilestoneKeys = "anomalyPromotedStudyMilestoneKeys";
        public const string MonolithBaselineLevelDefName =
            "anomalyMonolithBaselineLevelDefName";
        public const string LastMonolithKnowledgeSnapshot =
            "anomalyLastMonolithKnowledgeSnapshot";
        public const string CreepJoinerArcs = "anomalyCreepJoinerArcs";
    }

    /// <summary>Stable structured context keys; these schema labels deliberately remain English.</summary>
    internal static class AnomalyContextKeys
    {
        public const string Kind = "anomaly_kind";
        public const string StudyStage = "study_stage";
        public const string StudyPromotion = "study_promotion";
        public const string StudiedDef = "studied_def";
        public const string StudiedLabel = "studied_label";
        public const string CodexEntry = "codex_entry";
        public const string CodexEntryLabel = "codex_entry_label";
        public const string KnowledgeCategory = "knowledge_category";
        public const string KnowledgeCategoryLabel = "knowledge_category_label";
        public const string ContainedEntity = "contained_entity";
        public const string Monolith = "monolith";
        public const string Setting = "setting";
        public const string MonolithPreviousLevel = "monolith_previous_level";
        public const string MonolithReachedLevel = "monolith_reached_level";
        public const string MonolithLastResearcher = "monolith_last_researcher";
        public const string MonolithStudyStage = "monolith_study_stage";
        public const string MonolithBecameActivatable = "monolith_became_activatable";
        public const string EscapedCount = "escaped_count";
        public const string EscapedEntities = "escaped_entities";
        public const string AdditionalEscapedCount = "additional_escaped_count";
        public const string WitnessRole = "witness_role";
        public const string InitiatorWitnessRole = "initiator_witness_role";
        public const string RecipientWitnessRole = "recipient_witness_role";
        public const string SameRoomCascade = "same_room_cascade";
        public const string CreepJoinerPhase = "creepjoiner_phase";
        public const string CreepJoinerSubjectId = "creepjoiner_subject_id";
        public const string CreepJoinerSubjectLabel = "creepjoiner_subject_label";
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
        public const string Speaker = "speaker";
        public const string Subject = "subject";
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

    /// <summary>Visible-only saved phases for one creepjoiner arc.</summary>
    internal static class CreepJoinerPhaseTokens
    {
        // Joined is state-only: it may be established by the canonical arrival or an old-save
        // baseline, but it is never submitted as a dedicated outcome page.
        public const string Joined = "joined";

        /// <summary>Returns whether a token is one of A2.0's three terminal visible phases.</summary>
        public static bool IsOutcome(string value)
        {
            string phase = (value ?? string.Empty).Trim();
            return phase == AnomalyOutcomeTokens.Rejected
                || phase == AnomalyOutcomeTokens.Aggressive
                || phase == AnomalyOutcomeTokens.Departed;
        }

        /// <summary>Returns whether a token is the joined baseline or a terminal visible phase.</summary>
        public static bool IsKnown(string value)
        {
            string phase = (value ?? string.Empty).Trim();
            return phase == Joined || IsOutcome(phase);
        }
    }

    /// <summary>Prompt-safe visible results; these never encode a configured response Def.</summary>
    internal static class CreepJoinerVisibleResultTokens
    {
        public const string Rejected = "rejected";
        public const string Hostile = "hostile";
        public const string Leaving = "leaving";

        /// <summary>Maps a terminal phase to its generic visible-only result token.</summary>
        public static string ForPhase(string phase)
        {
            if (phase == AnomalyOutcomeTokens.Rejected) return Rejected;
            if (phase == AnomalyOutcomeTokens.Aggressive) return Hostile;
            if (phase == AnomalyOutcomeTokens.Departed) return Leaving;
            return string.Empty;
        }
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
        public const int MaximumContainmentEntities = 64;
        public const int MaximumContainmentCandidates = 512;
        public const int DefaultRecentStudies = 128;
        public const int MaximumRecentStudies = 512;
        public const int DefaultRecentStudierMaxAgeTicks = 60000;
        public const int MaximumStudyMilestones = 128;
        public const int DefaultStudyTaleSuppressionTicks = 2500;
        public const int DefaultTaleOwnershipDepth = 8;
        public const int MaximumTaleOwnershipDepth = 32;
        public const int DefaultTaleOwnershipExpiryTicks = 2500;
        public const int MaximumCreepJoinerWitnesses = 2;
        public const int DefaultCreepJoinerWitnessRadius = 12;
        public const int MaximumCreepJoinerCandidates = 512;
        public const int MaximumCreepJoinerCandidateInputs = 4096;
        public const int MaximumCreepJoinerArcs = 512;
        public const int MaximumCreepJoinerArcInputs = 4096;
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
        public string codexEntryLabel = string.Empty;
        public string knowledgeCategoryDefName = string.Empty;
        public string knowledgeCategoryLabel = string.Empty;
        public string studierPawnId = string.Empty;
        // Unsaved vanilla job identity lets the later generic Tale prove that it belongs to this
        // exact study job even when a very slow work cycle lasts beyond the fallback tick window.
        public string studyJobId = string.Empty;
        public bool studierEligible;
        public int tick = -1;
        public int oldProgress;
        public int newProgress;
        // Number of study-note thresholds crossed by this exact call, not the subject's lifetime
        // total. The later live adapter must calculate this delta before invoking the pure policy.
        public int noteThresholdsCrossed;
        public bool completedBefore;
        public bool completedAfter;
        public bool isContainedEntity;
        public bool isMonolith;
        public bool monolithActivatableBefore;
        public bool monolithActivatableAfter;
        public string setting = string.Empty;
    }

    /// <summary>Detached study history used to prevent retroactive or repeated milestones.</summary>
    internal sealed class AnomalyStudyHistorySnapshot
    {
        public bool firstBreakthroughObserved;
        public readonly List<string> completedStudyDefNames = new List<string>();
        public readonly List<string> observedPromotionKeys = new List<string>();
    }

    /// <summary>
    /// Saved, event-time monolith-study enrichment. It contains no live monolith, comp, Pawn, Def,
    /// letter, or localized prose; the later activation adapter may resolve the researcher only for
    /// bounded contextual naming.
    /// </summary>
    internal sealed class AnomalyMonolithKnowledgeSnapshot
    {
        public string researcherPawnId = string.Empty;
        public string studyStage = string.Empty;
        public int tick = -1;
        public int reachedProgress;
        public bool becameActivatable;
        public bool consumed;
    }

    /// <summary>Detached aggregate of additive A1 study/monolith and A2 creepjoiner state.</summary>
    internal sealed class AnomalyPersistentStateSnapshot
    {
        public int schemaVersion;
        public bool firstStudyBreakthroughObserved;
        public List<string> completedStudyDefNames = new List<string>();
        public List<string> promotedStudyMilestoneKeys = new List<string>();
        public string monolithBaselineLevelDefName = string.Empty;
        public AnomalyMonolithKnowledgeSnapshot lastMonolithKnowledgeSnapshot;
        public List<CreepJoinerArcSnapshot> creepJoinerArcs = new List<CreepJoinerArcSnapshot>();
    }

    /// <summary>
    /// Best-effort facts captured once when a pre-A1 save first loads with Anomaly active. A false
    /// <c>historyComplete</c> deliberately suppresses an unprovable future "first" claim.
    /// </summary>
    internal sealed class AnomalyLegacyBaselineFacts
    {
        public bool anomalyAvailable;
        public bool historyComplete;
        public bool anyCommittedStudyProgress;
        public string currentMonolithLevelDefName = string.Empty;
        public List<string> completedStudyDefNames = new List<string>();
    }

    /// <summary>
    /// Visible-only saved creepjoiner continuity. It deliberately has no field capable of holding a
    /// benefit/downside/rejection/aggression Def, worker, trigger time, infection, or future form.
    /// </summary>
    internal sealed class CreepJoinerArcSnapshot
    {
        public string pawnId = string.Empty;
        public string arrivalEventId = string.Empty;
        public int joinedTick;
        public string lastVisiblePhase = string.Empty;
        public string lastVisibleEventId = string.Empty;
        public bool terminal;
        public int schemaVersion;
    }

    /// <summary>One-time guarded old-save scan result; every row describes current visible state only.</summary>
    internal sealed class CreepJoinerLegacyBaselineFacts
    {
        public bool anomalyAvailable;
        public readonly List<CreepJoinerArcSnapshot> currentJoined =
            new List<CreepJoinerArcSnapshot>();
    }

    /// <summary>Detached event-time writer evidence for one exact visible outcome.</summary>
    internal sealed class CreepJoinerWriterCandidate
    {
        public string pawnId = string.Empty;
        public bool eligible;
        public bool exactSpeaker;
        public bool subject;
        public bool onSubjectMap;
        public bool nearby;
        public int distanceSquared;
        public string tieBreakKey = string.Empty;
    }

    /// <summary>Before/after facts copied from one exact public creepjoiner lifecycle method.</summary>
    internal sealed class CreepJoinerOutcomeFacts
    {
        public string pawnId = string.Empty;
        public string subjectLabel = string.Empty;
        public string phase = string.Empty;
        public string visibleResultToken = string.Empty;
        public int tick = -1;
        public bool transitioned;
        public bool transitionVerified;
        public bool playerVisible;
        public bool joinedBefore;
        public bool subjectEligibleBefore;
        public bool nestedOutcomeOwnedByRejection;
        public readonly List<CreepJoinerWriterCandidate> writers =
            new List<CreepJoinerWriterCandidate>();
    }

    /// <summary>Pure terminal-state mutation and optional single-writer page decision.</summary>
    internal sealed class CreepJoinerOutcomePlan
    {
        public bool valid;
        public bool advanceArc;
        public bool writePage;
        public bool replaySuppressed;
        public string phase = string.Empty;
        public string visibleResultToken = string.Empty;
        public string sourceKey = string.Empty;
        public AnomalyWriterSelection selectedWriter;
        public CreepJoinerArcSnapshot nextArc;
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
        // Non-empty only when stageToken is Promoted. Crossed-but-lower-priority promotions still
        // enter historyMutation so one transition cannot replay as several pages.
        public string promotionToken = string.Empty;
        public bool monolithBecameActivatable;
        public readonly AnomalyStudyHistoryMutation historyMutation = new AnomalyStudyHistoryMutation();
    }

    /// <summary>One exact monolith activation boundary copied without live game objects.</summary>
    internal sealed class AnomalyMonolithActivationFacts
    {
        public int tick = -1;
        public string previousLevelDefName = string.Empty;
        public string reachedLevelDefName = string.Empty;
    }

    /// <summary>Pure decision for consuming and optionally attaching saved monolith-study context.</summary>
    internal sealed class AnomalyMonolithKnowledgeDecision
    {
        public bool consume;
        public bool attach;
        public string previousLevelDefName = string.Empty;
        public string reachedLevelDefName = string.Empty;
        public string researcherPawnId = string.Empty;
        public string studyStage = string.Empty;
        public bool becameActivatable;
    }

    /// <summary>One verified entity captured before and after an involuntary escape.</summary>
    internal sealed class ContainedEntityFact
    {
        public string entityId = string.Empty;
        public string visibleLabel = string.Empty;
        public string defName = string.Empty;
        public string mutantDefName = string.Empty;
        public string platformId = string.Empty;
        public int mapId = -1;
        public int platformX;
        public int platformZ;
        public bool escaped;
    }

    /// <summary>
    /// One detached exact study relationship retained independently from consume-once Tale ownership.
    /// </summary>
    internal sealed class AnomalyRecentStudyFact
    {
        public string studierPawnId = string.Empty;
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public int studiedTick = -1;
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
        public string preEjectionSetting = string.Empty;
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
        public bool sameRoomCascade;
        public string preEjectionSetting = string.Empty;
        public readonly List<ContainedEntityFact> contextEntities = new List<ContainedEntityFact>();
        public readonly List<AnomalyWriterSelection> selectedWriters = new List<AnomalyWriterSelection>();
    }

    /// <summary>Accepted dedicated-page ownership awaiting one exact generic StudiedEntity Tale.</summary>
    internal sealed class AnomalyStudyTaleClaim
    {
        public string studierPawnId = string.Empty;
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public string studyJobId = string.Empty;
        public int acceptedTick = -1;
        public bool consumed;
    }

    /// <summary>Plain identity facts copied from one generic StudiedEntity Tale.</summary>
    internal sealed class AnomalyStudiedTaleFacts
    {
        public string studierPawnId = string.Empty;
        public string studiedEntityId = string.Empty;
        public string studiedDefName = string.Empty;
        public string studyJobId = string.Empty;
        public int tick = -1;
    }

    /// <summary>Pure outcome for one claim/Tale comparison.</summary>
    internal sealed class AnomalyTaleOwnershipDecision
    {
        public bool suppress;
        public bool removeClaim;
        // The caller replaces its live claim with this detached clone when retaining the claim.
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
        public int creepJoinerWitnessRadius;
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
                studyTaleSuppressionTicks = AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks,
                containmentEnabled = true,
                containmentWitnessRadius = AnomalyPolicyLimits.DefaultWitnessRadius,
                containmentMaxWriters = AnomalyPolicyLimits.DefaultContainmentWriters,
                containmentMaxEntityLabelsInContext = AnomalyPolicyLimits.DefaultEntityLabels,
                containmentDedupTicks = 2500,
                recentStudierMaxAgeTicks = AnomalyPolicyLimits.DefaultRecentStudierMaxAgeTicks,
                creepJoinerEnabled = true,
                creepJoinerOutcomeDedupTicks = 2500,
                creepJoinerArcRetentionTicks = 3600000,
                creepJoinerMaxWitnesses = AnomalyPolicyLimits.MaximumCreepJoinerWitnesses,
                creepJoinerWitnessRadius = AnomalyPolicyLimits.DefaultCreepJoinerWitnessRadius,
                ghoulTransformationEnabled = true,
                voidOutcomeEnabled = true,
                taleOwnershipMaxDepth = AnomalyPolicyLimits.DefaultTaleOwnershipDepth,
                taleOwnershipExpiryTicks = AnomalyPolicyLimits.DefaultTaleOwnershipExpiryTicks
            };
        }
    }

    /// <summary>
    /// Pure normalization for the mutable XML policy copy. Keeping every clamp and promotion-row
    /// predicate here lets the standalone suite exercise malformed Def input without loading Verse.
    /// </summary>
    internal static class AnomalyPolicyNormalization
    {
        /// <summary>Returns a detached, bounded policy with conservative defaults for invalid values.</summary>
        public static AnomalyPolicySnapshot Normalize(AnomalyPolicySnapshot source)
        {
            AnomalyPolicySnapshot result = AnomalyPolicySnapshot.CreateDefault();
            if (source == null) return result;

            result.studyEnabled = source.studyEnabled;
            result.recordFirstStudyBreakthrough = source.recordFirstStudyBreakthrough;
            result.recordCompletedEntityKind = source.recordCompletedEntityKind;
            result.monolithKnowledgeMaxAgeTicks = NonNegative(
                source.monolithKnowledgeMaxAgeTicks, result.monolithKnowledgeMaxAgeTicks);
            result.studyTaleSuppressionTicks = NonNegative(
                source.studyTaleSuppressionTicks, result.studyTaleSuppressionTicks);
            result.containmentEnabled = source.containmentEnabled;
            result.containmentWitnessRadius = InRange(
                source.containmentWitnessRadius, 1, AnomalyPolicyLimits.MaximumWitnessRadius,
                result.containmentWitnessRadius);
            result.containmentMaxWriters = InRange(
                source.containmentMaxWriters, 1, AnomalyPolicyLimits.MaximumContainmentWriters,
                result.containmentMaxWriters);
            result.containmentMaxEntityLabelsInContext = InRange(
                source.containmentMaxEntityLabelsInContext, 1,
                AnomalyPolicyLimits.MaximumEntityLabels,
                result.containmentMaxEntityLabelsInContext);
            result.containmentDedupTicks = NonNegative(
                source.containmentDedupTicks, result.containmentDedupTicks);
            result.recentStudierMaxAgeTicks = NonNegative(
                source.recentStudierMaxAgeTicks, result.recentStudierMaxAgeTicks);
            result.creepJoinerEnabled = source.creepJoinerEnabled;
            result.creepJoinerOutcomeDedupTicks = NonNegative(
                source.creepJoinerOutcomeDedupTicks, result.creepJoinerOutcomeDedupTicks);
            result.creepJoinerArcRetentionTicks = NonNegative(
                source.creepJoinerArcRetentionTicks, result.creepJoinerArcRetentionTicks);
            result.creepJoinerMaxWitnesses = InRange(
                source.creepJoinerMaxWitnesses, 1,
                AnomalyPolicyLimits.MaximumCreepJoinerWitnesses,
                result.creepJoinerMaxWitnesses);
            result.creepJoinerWitnessRadius = InRange(
                source.creepJoinerWitnessRadius, 1,
                AnomalyPolicyLimits.MaximumWitnessRadius,
                result.creepJoinerWitnessRadius);
            result.ghoulTransformationEnabled = source.ghoulTransformationEnabled;
            result.voidOutcomeEnabled = source.voidOutcomeEnabled;
            result.taleOwnershipMaxDepth = InRange(
                source.taleOwnershipMaxDepth, 1,
                AnomalyPolicyLimits.MaximumTaleOwnershipDepth,
                result.taleOwnershipMaxDepth);
            result.taleOwnershipExpiryTicks = NonNegative(
                source.taleOwnershipExpiryTicks, result.taleOwnershipExpiryTicks);
            CopyPromotions(source.promotedStudyMilestones, result.promotedStudyMilestones);
            return result;
        }

        /// <summary>Returns whether one raw XML promotion has safe, stable key parts.</summary>
        public static bool ValidPromotion(string studiedDefName, int minimumProgress, string token)
        {
            return minimumProgress > 0
                && !string.IsNullOrWhiteSpace(studiedDefName)
                && studiedDefName.IndexOf('|') < 0
                && !string.IsNullOrWhiteSpace(token)
                && token.IndexOf('|') < 0;
        }

        /// <summary>Returns whether one detached promotion rule has safe, stable key parts.</summary>
        public static bool ValidPromotion(AnomalyStudyMilestoneRule rule)
        {
            return rule != null
                && ValidPromotion(rule.studiedDefName, rule.minimumProgress, rule.token);
        }

        /// <summary>Builds the stable persisted identity for one valid promotion rule.</summary>
        public static string PromotionKey(AnomalyStudyMilestoneRule rule)
        {
            return rule == null
                ? string.Empty
                : PromotionKey(rule.studiedDefName, rule.minimumProgress, rule.token);
        }

        /// <summary>Builds the stable persisted identity for validated raw promotion parts.</summary>
        public static string PromotionKey(string studiedDefName, int minimumProgress, string token)
        {
            if (!ValidPromotion(studiedDefName, minimumProgress, token)) return string.Empty;
            return studiedDefName.Trim() + "|" + minimumProgress + "|" + token.Trim();
        }

        private static void CopyPromotions(
            List<AnomalyStudyMilestoneRule> source,
            List<AnomalyStudyMilestoneRule> destination)
        {
            if (source == null) return;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int count = Math.Min(source.Count, AnomalyPolicyLimits.MaximumStudyMilestones);
            for (int i = 0; i < count; i++)
            {
                AnomalyStudyMilestoneRule row = source[i];
                string key = PromotionKey(row);
                if (key.Length == 0 || !seen.Add(key)) continue;
                destination.Add(new AnomalyStudyMilestoneRule
                {
                    studiedDefName = row.studiedDefName.Trim(),
                    minimumProgress = row.minimumProgress,
                    token = row.token.Trim()
                });
            }
        }

        private static int NonNegative(int value, int fallback)
        {
            return value < 0 ? fallback : value;
        }

        private static int InRange(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }
    }
}
