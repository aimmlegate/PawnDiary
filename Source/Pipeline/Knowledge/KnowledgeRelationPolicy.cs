// KnowledgeRelationPolicy.cs — pure identity, visibility, and reconciliation rules for known
// social/family/faction current truth. RimWorld adapters supply detached primitive observations;
// this file never reads Pawn, Faction, Def, Verse, Unity, settings, or save objects.
//
// Phase M6 is deliberately shadow-only. The opinion planner can report that a future factual
// capture rule qualified, but the M6 adapter only persists the replacement snapshot/episode and
// never sends that signal to diary events, prompts, or the LLM pipeline.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Stable current-truth schema tokens. These are save grammar, not player prose.</summary>
    internal static class KnowledgeObservationTokens
    {
        public const string ScopeRelationship = "relationship";
        public const string ScopeRelative = "relative";
        public const string ScopeFaction = "faction";

        public const string SubjectPawn = "pawn";
        public const string SubjectFaction = "faction";

        public const string StreamDirectedSocial = "directed_social";
        public const string StreamRelativeState = "relative_state";
        public const string StreamFactionConnection = "faction_connection";

        public const string EvidenceDirect = "direct";
        public const string EvidenceCaptured = "captured";
        public const string EvidenceExistingNews = "existing_news";
        public const string EvidenceRepairConflict = "repair_conflict";

        public const string TrackingTracked = "tracked";
        public const string TrackingCapacityUntracked = "capacity_untracked";

        public const string OpinionDevoted = "devoted";
        public const string OpinionFriendly = "friendly";
        public const string OpinionNeutral = "neutral";
        public const string OpinionStrained = "strained";
        public const string OpinionHostile = "hostile";

        public const string DirectionRising = "rising";
        public const string DirectionFalling = "falling";

        public const string FactOpinionValue = "opinion_value";
        public const string FactOpinionBand = "opinion_band";
        public const string FactOutboundRelations = "outbound_relations";
        public const string FactInboundRelations = "inbound_relations";
        public const string FactLifeState = "life_state";
        public const string FactLocationState = "location_state";
        public const string FactFactionSubject = "faction_subject";
        public const string FactRelationDefs = "relation_defs";
        public const string FactConnectionKind = "connection_kind";

        public const string LifeAlive = "alive";
        public const string LifeDead = "dead";
        public const string LocationWorld = "world";
        public const string LocationUnknown = "unknown";
        public const string ConnectionCurrent = "current";
        public const string ConnectionRecentFormer = "recent_former";
        public const string ConnectionFamily = "family";
        public const string FactionRelationAlly = "Ally";
        public const string FactionRelationNeutral = "Neutral";
        public const string FactionRelationHostile = "Hostile";

        public const string OpinionEpisodeRule = "directed_opinion_episode_v1";
        public const string OpinionEpisodeKind = "opinion_change";

        /// <summary>True for one of the three persisted M6 scope tokens.</summary>
        public static bool IsKnownScope(string value)
        {
            return value == ScopeRelationship || value == ScopeRelative || value == ScopeFaction;
        }

        /// <summary>True for one of the allowlisted knownness-evidence tokens.</summary>
        public static bool IsKnownEvidence(string value)
        {
            return value == EvidenceDirect || value == EvidenceCaptured
                || value == EvidenceExistingNews || value == EvidenceRepairConflict;
        }
    }

    /// <summary>Detached inputs matching vanilla SocialCardUtility's non-dev visibility gates.</summary>
    internal sealed class KnowledgeRelationVisibilityInput
    {
        public bool candidateIsDeadAnimalWithoutCorpse;
        public bool candidateHasName;
        public bool candidateNameIsNumerical;
        public bool candidateHidesRelations;
        public bool ownerHidesRelations;
        public bool candidateEverSeenByPlayer;
        public bool previouslyKnown;
        public bool hasKnownRelation;
        public bool candidateIsHumanlike;
        public bool sharesSocialContext;
        public int ownerOpinionOfCandidate;
        public int candidateOpinionOfOwner;
    }

    /// <summary>The existing five opinion thresholds, copied from DiaryTuningDef by the adapter.</summary>
    internal sealed class KnowledgeOpinionBandThresholds
    {
        public int devoted = 60;
        public int friendly = 25;
        public int neutralAbove = -10;
        public int strainedAbove = -40;

        /// <summary>True when the copied five-band boundaries are strictly ordered.</summary>
        public bool IsOrdered()
        {
            return devoted > friendly && friendly > neutralAbove
                && neutralAbove > strainedAbove;
        }
    }

    /// <summary>XML-owned M6 cadence and deterministic episode thresholds.</summary>
    internal sealed class KnowledgeObservationPolicySnapshot
    {
        public const int MinimumReconciliationIntervalTicks = 250;
        public const int MaximumReconciliationIntervalTicks = 60000;
        public const int DefaultReconciliationIntervalTicks = 2500;

        public int reconciliationIntervalTicks = DefaultReconciliationIntervalTicks;
        public int opinionBandSustainTicks = 15000;
        public int opinionHysteresisPoints = 5;
        public int opinionCumulativeChangePoints = 20;
        public int opinionReversalChangePoints = 12;
        public int opinionEpisodeInactivityTicks = 60000;
        public int opinionEpisodeMaximumTicks = 300000;
        public int maximumStateFacts = 4;
        public int maximumFactKeyCharacters = 48;
        public int maximumFactValueCharacters = 128;

        /// <summary>Returns a bounded copy; malformed XML can never create zero-work or overflow loops.</summary>
        public KnowledgeObservationPolicySnapshot Normalized()
        {
            return new KnowledgeObservationPolicySnapshot
            {
                reconciliationIntervalTicks = NormalizeReconciliationInterval(
                    reconciliationIntervalTicks),
                opinionBandSustainTicks = Clamp(opinionBandSustainTicks, 0, 600000, 15000),
                opinionHysteresisPoints = Clamp(opinionHysteresisPoints, 0, 50, 5),
                opinionCumulativeChangePoints = Clamp(
                    opinionCumulativeChangePoints, 1, 200, 20),
                opinionReversalChangePoints = Clamp(
                    opinionReversalChangePoints, 1, 200, 12),
                opinionEpisodeInactivityTicks = Clamp(
                    opinionEpisodeInactivityTicks, 1, 6000000, 60000),
                opinionEpisodeMaximumTicks = Clamp(
                    opinionEpisodeMaximumTicks, 1, 12000000, 300000),
                maximumStateFacts = Clamp(maximumStateFacts, 1, 16, 4),
                maximumFactKeyCharacters = Clamp(maximumFactKeyCharacters, 1, 192, 48),
                maximumFactValueCharacters = Clamp(maximumFactValueCharacters, 1, 512, 128)
            };
        }

        /// <summary>
        /// Normalizes only the cadence field so the every-tick adapter can make its cheap scheduling
        /// decision without allocating the rest of the XML policy snapshot.
        /// </summary>
        public static int NormalizeReconciliationInterval(int value)
        {
            return Clamp(
                value,
                MinimumReconciliationIntervalTicks,
                MaximumReconciliationIntervalTicks,
                DefaultReconciliationIntervalTicks);
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }
    }

    /// <summary>Pure one-tick decision for the elapsed M6 full-reconciliation schedule.</summary>
    internal struct KnowledgeReconciliationSchedulePlan
    {
        public bool consumeCompletedTick;
        public bool requestFullReconciliation;
        public bool forceSilentBaseline;
    }

    /// <summary>Pure coalesced flags for duplicate M6 dirty-work keys.</summary>
    internal struct KnowledgeObservationWorkMergePlan
    {
        public bool removedFaction;
        public bool forceSilentBaseline;
    }

    /// <summary>One plain canonical state fact used on both sides of the saved-model adapter.</summary>
    internal sealed class KnowledgeStateFact
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    /// <summary>Plain mirror of one replaceable awareness row; contains no save-framework types.</summary>
    internal sealed class KnowledgeAwarenessState
    {
        public string snapshotId = string.Empty;
        public string scopeKindToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string factStreamToken = string.Empty;
        public long captureInvalidationGeneration;
        public string knownnessEvidenceToken = string.Empty;
        public List<KnowledgeStateFact> stateFacts = new List<KnowledgeStateFact>();
        public long firstObservedTick;
        public long lastObservedTick;
        public string lastSourceOccurrenceId = string.Empty;
        public string trackingStateToken = string.Empty;
        public long snapshotRevision;
    }

    /// <summary>Plain mirror of one deterministic open opinion accumulator.</summary>
    internal sealed class KnowledgeOpinionEpisodeState
    {
        public string episodeId = string.Empty;
        public string captureRuleId = string.Empty;
        public string scopeKindToken = string.Empty;
        public string factStreamToken = string.Empty;
        public string category = string.Empty;
        public long captureInvalidationGeneration;
        public string episodeKindToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string pairOrStreamKey = string.Empty;
        public string directionToken = string.Empty;
        public List<KnowledgeStateFact> baselineFacts = new List<KnowledgeStateFact>();
        public List<KnowledgeStateFact> currentFacts = new List<KnowledgeStateFact>();
        public long firstObservedTick;
        public long lastObservedTick;
        public string lastSourceOccurrenceId = string.Empty;
        public long episodeRevision;
    }

    /// <summary>Detached observation of one known current-truth stream.</summary>
    internal sealed class KnowledgeCurrentTruthObservation
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string scopeKindToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string factStreamToken = string.Empty;
        public long captureInvalidationGeneration;
        public string knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect;
        public List<KnowledgeStateFact> stateFacts = new List<KnowledgeStateFact>();
        public long observedTick;
        public string sourceOccurrenceId = string.Empty;
        public bool captureAllowed;
        public bool forceSilentBaseline;
    }

    /// <summary>Pure replacement decision for a current-truth observation.</summary>
    internal sealed class KnowledgeAwarenessPlan
    {
        public bool valid;
        public bool savedMutationRequired;
        public bool silentBaseline;
        public bool authoritativeStateChanged;
        public KnowledgeAwarenessState replacement;
    }

    /// <summary>Detached inputs for the directed opinion snapshot and its open episode.</summary>
    internal sealed class KnowledgeOpinionObservation
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string subjectPawnId = string.Empty;
        public int opinion;
        public List<string> outboundRelationDefNames = new List<string>();
        public List<string> inboundRelationDefNames = new List<string>();
        public long captureInvalidationGeneration;
        public long observedTick;
        public string sourceOccurrenceId = string.Empty;
        public bool captureAllowed;
        public bool forceSilentBaseline;
    }

    /// <summary>
    /// Pure social plan. qualifiedForFutureCapture is deliberately ignored by M6; M7 may consume an
    /// equivalent detached signal after factual capture is introduced.
    /// </summary>
    internal sealed class KnowledgeOpinionPlan
    {
        public bool valid;
        public bool savedMutationRequired;
        public bool silentBaseline;
        public bool formalRelationChanged;
        public bool qualifiedForFutureCapture;
        public string qualificationReasonToken = string.Empty;
        public KnowledgeAwarenessState replacement;
        public KnowledgeOpinionEpisodeState openEpisode;
    }

    /// <summary>Plain component-global faction snapshot used by the pure M6 reconciler.</summary>
    internal sealed class KnowledgeFactionState
    {
        public string factionInstanceId = string.Empty;
        public long allocatorGeneration;
        public string factionDefName = string.Empty;
        public string frozenDisplayLabel = string.Empty;
        public int goodwill;
        public string relationKindToken = string.Empty;
        public string leaderPawnId = string.Empty;
        public bool defeated;
        public bool removed;
        public long observedTick;
        public string trackingStateToken = string.Empty;
        public long snapshotRevision;
    }

    /// <summary>Detached live faction fields; display/Def fields are explicitly non-identity.</summary>
    internal sealed class KnowledgeFactionObservation
    {
        public string factionInstanceId = string.Empty;
        public long allocatorGeneration;
        public string factionDefName = string.Empty;
        public string frozenDisplayLabel = string.Empty;
        public int goodwill;
        public string relationKindToken = string.Empty;
        public string leaderPawnId = string.Empty;
        public bool defeated;
        public bool removed;
        public long observedTick;
        public bool forceSilentBaseline;
        public int maximumFrozenDisplayLabelCharacters = 80;
    }

    /// <summary>Pure replacement decision for one exact faction-instance/generation key.</summary>
    internal sealed class KnowledgeFactionPlan
    {
        public bool valid;
        public bool savedMutationRequired;
        public bool silentBaseline;
        public bool authoritativeStateChanged;
        public KnowledgeFactionState replacement;
    }

    /// <summary>Pure duplicate/conflict selection for one exact awareness-key group.</summary>
    internal sealed class KnowledgeAwarenessRepairPlan
    {
        public bool valid;
        public bool conflict;
        public int retainedIndex = -1;
        public KnowledgeAwarenessState repairMarker;
    }

    /// <summary>Pure duplicate/conflict selection for one exact open-episode group.</summary>
    internal sealed class KnowledgeEpisodeRepairPlan
    {
        public bool valid;
        public bool conflict;
        public int retainedIndex = -1;
        public KnowledgeAwarenessState repairMarker;
    }

    /// <summary>Pure duplicate/conflict selection for one exact faction-instance group.</summary>
    internal sealed class KnowledgeFactionRepairPlan
    {
        public bool valid;
        public bool conflict;
        public int retainedIndex = -1;
        public KnowledgeFactionState repairMarker;
    }

    /// <summary>Load-repair action for an episode and its already-normalized awareness stream.</summary>
    internal enum KnowledgeEpisodeBackingDisposition
    {
        DropWithoutMarker = 0,
        Retain = 1,
        PublishConflictMarker = 2
    }

    /// <summary>Pure direction, visibility, exact-identity, and state-transition policy.</summary>
    internal static class KnowledgeRelationPolicy
    {
        private const string AwarenessDomain = "memory-awareness-v1";
        private const string EpisodeDomain = "memory-capture-episode-v1";
        private const string DirectedPairDomain = "memory-directed-pair-v1";
        private const string RelationSetDomain = "memory-relation-def-set-v1";

        /// <summary>
        /// Defensive per-death family fanout cap. Modded relation graphs can contain very large
        /// sibling/child sets; one death must not allocate unbounded records in the kill hook.
        /// </summary>
        public const int MaximumDeathFamilyOwners = 12;

        /// <summary>True while another close-family owner fits inside the defensive death cap.</summary>
        public static bool CanEmitDeathFamilyOwner(int emittedFamilyOwners)
        {
            return emittedFamilyOwners >= 0
                && emittedFamilyOwners < MaximumDeathFamilyOwners;
        }

        /// <summary>Returns the victim's relation from the surviving owner's point of view.</summary>
        public static string VictimRelationDefName(string observedRelationDefName)
        {
            if (string.Equals(observedRelationDefName, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                return "Child";
            }

            if (string.Equals(observedRelationDefName, "Child", StringComparison.OrdinalIgnoreCase))
            {
                return "Parent";
            }

            return (observedRelationDefName ?? string.Empty).Trim();
        }

        /// <summary>
        /// Mirrors vanilla SocialCardUtility.ShouldShowPawnRelations without its dev-only reveal-all
        /// switch. Persisted truth must never change because a developer display toggle exposed it.
        /// </summary>
        public static bool IsKnownVisibleRelation(KnowledgeRelationVisibilityInput input)
        {
            return input != null
                && !input.candidateIsDeadAnimalWithoutCorpse
                && input.candidateHasName
                && !input.candidateNameIsNumerical
                && !input.candidateHidesRelations
                && !input.ownerHidesRelations
                && input.candidateEverSeenByPlayer;
        }

        /// <summary>
        /// Mirrors the non-dev Social tab's two admission paths after its visibility gate: an exact
        /// known relation, or a co-located humanlike pawn with a nonzero opinion in either direction.
        /// A saved edge remains eligible so reconciliation may update truth that was already known.
        /// </summary>
        public static bool IsKnownSocialEntry(KnowledgeRelationVisibilityInput input)
        {
            return IsKnownVisibleRelation(input)
                && (input.previouslyKnown
                    || input.hasKnownRelation
                    || (input.candidateIsHumanlike
                        && input.sharesSocialContext
                        && (input.ownerOpinionOfCandidate != 0
                            || input.candidateOpinionOfOwner != 0)));
        }

        /// <summary>
        /// True only for the first eligible observation after an owner was absent from the attached
        /// set. The adapter keeps this state transient so save/load always takes the same silent path.
        /// </summary>
        public static bool OwnerAttachmentNeedsSilentBaseline(
            bool wasAttached,
            bool isEligible)
        {
            return isEligible && !wasAttached;
        }

        /// <summary>Returns the stable counterpart of the existing localized five-band vocabulary.</summary>
        public static string OpinionBandToken(
            int opinion,
            KnowledgeOpinionBandThresholds thresholds)
        {
            KnowledgeOpinionBandThresholds safe = thresholds ?? new KnowledgeOpinionBandThresholds();
            if (!safe.IsOrdered()) safe = new KnowledgeOpinionBandThresholds();
            if (opinion >= safe.devoted) return KnowledgeObservationTokens.OpinionDevoted;
            if (opinion >= safe.friendly) return KnowledgeObservationTokens.OpinionFriendly;
            if (opinion > safe.neutralAbove) return KnowledgeObservationTokens.OpinionNeutral;
            if (opinion > safe.strainedAbove) return KnowledgeObservationTokens.OpinionStrained;
            return KnowledgeObservationTokens.OpinionHostile;
        }

        /// <summary>Creates the exact owner/epoch/scope/subject/stream awareness key.</summary>
        public static bool TryCreateAwarenessId(
            string ownerPawnId,
            string ownerEpochToken,
            string scopeKindToken,
            string subjectKind,
            string subjectId,
            string factStreamToken,
            out string snapshotId)
        {
            snapshotId = string.Empty;
            if (!RequiredRaw(ownerPawnId)
                || !ValidEpoch(ownerEpochToken)
                || !KnowledgeObservationTokens.IsKnownScope(scopeKindToken)
                || !RequiredRaw(subjectKind)
                || !RequiredComposite(subjectId)
                || !RequiredRaw(factStreamToken)) return false;

            return TryJoin(new[]
            {
                AwarenessDomain, ownerPawnId, ownerEpochToken, scopeKindToken,
                subjectKind, subjectId, factStreamToken
            }, out snapshotId);
        }

        /// <summary>Creates an exact directed pair key; reversing the pawns changes the key.</summary>
        public static bool TryCreateDirectedPairKey(
            string ownerPawnId,
            string subjectPawnId,
            out string pairKey)
        {
            pairKey = string.Empty;
            if (!RequiredRaw(ownerPawnId) || !RequiredRaw(subjectPawnId)
                || string.Equals(ownerPawnId, subjectPawnId, StringComparison.Ordinal)) return false;
            return TryJoin(new[] { DirectedPairDomain, ownerPawnId, subjectPawnId }, out pairKey);
        }

        /// <summary>Creates the exact eleven-segment open-episode identity from §T6.1.1.</summary>
        public static bool TryCreateEpisodeId(
            string ownerPawnId,
            string ownerEpochToken,
            string scopeKindToken,
            string factStreamToken,
            string captureRuleId,
            string episodeKindToken,
            string subjectKind,
            string subjectId,
            string pairOrStreamKey,
            string directionToken,
            out string episodeId)
        {
            episodeId = string.Empty;
            if (!RequiredRaw(ownerPawnId) || !ValidEpoch(ownerEpochToken)
                || !KnowledgeObservationTokens.IsKnownScope(scopeKindToken)
                || !RequiredRaw(factStreamToken) || !RequiredRaw(captureRuleId)
                || !RequiredRaw(episodeKindToken) || !RequiredRaw(subjectKind)
                || !RequiredComposite(subjectId) || !RequiredComposite(pairOrStreamKey)
                || !RequiredRaw(directionToken)) return false;
            return TryJoin(new[]
            {
                EpisodeDomain, ownerPawnId, ownerEpochToken, scopeKindToken,
                factStreamToken, captureRuleId, episodeKindToken, subjectKind,
                subjectId, pairOrStreamKey, directionToken
            }, out episodeId);
        }

        /// <summary>
        /// Advances the checked component-global faction generation only when the next value is not
        /// already live. Collision or long saturation fails closed; generations are never recycled.
        /// </summary>
        public static bool TryAllocateFactionGeneration(
            long highWater,
            IEnumerable<long> liveGenerations,
            out long nextGeneration)
        {
            nextGeneration = 0;
            if (highWater < 0 || highWater == long.MaxValue) return false;
            long candidate = highWater + 1;
            if (liveGenerations != null)
            {
                foreach (long live in liveGenerations)
                {
                    if (live == candidate) return false;
                }
            }
            nextGeneration = candidate;
            return true;
        }

        /// <summary>Plans one exact component-global faction current-state replacement.</summary>
        public static KnowledgeFactionPlan PlanFactionSnapshot(
            KnowledgeFactionState previous,
            KnowledgeFactionObservation observation)
        {
            KnowledgeFactionPlan result = new KnowledgeFactionPlan();
            string factionDefName = observation?.factionDefName ?? string.Empty;
            string displayLabel = observation?.frozenDisplayLabel ?? string.Empty;
            string relationKind = observation?.relationKindToken ?? string.Empty;
            string leaderPawnId = observation?.leaderPawnId ?? string.Empty;
            if (observation == null || !RequiredRaw(observation.factionInstanceId)
                || observation.allocatorGeneration <= 0 || observation.observedTick < 0
                || observation.goodwill < -100 || observation.goodwill > 100
                || !IsKnownFactionRelationKind(relationKind)
                || observation.maximumFrozenDisplayLabelCharacters <= 0
                || observation.maximumFrozenDisplayLabelCharacters > 320
                || factionDefName.Length
                    > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || leaderPawnId.Length
                    > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || displayLabel.Length
                    > observation.maximumFrozenDisplayLabelCharacters
                || !MemoryIdentityCodec.IsWellFormedUtf16(factionDefName)
                || !MemoryIdentityCodec.IsWellFormedUtf16(displayLabel)
                || !MemoryIdentityCodec.IsWellFormedUtf16(leaderPawnId)) return result;

            string subjectId;
            if (!MemoryIdentityCodec.TryCreateFactionSubjectId(
                    observation.factionInstanceId,
                    observation.allocatorGeneration,
                    out subjectId)) return result;
            bool sameIdentity = previous != null
                && previous.factionInstanceId == observation.factionInstanceId
                && previous.allocatorGeneration == observation.allocatorGeneration;
            long previousRevision = sameIdentity ? previous.snapshotRevision : 0;
            if (sameIdentity && previousRevision == long.MaxValue)
            {
                result.valid = true;
                result.savedMutationRequired = false;
                result.silentBaseline = true;
                result.replacement = previous;
                return result;
            }
            bool saturated = previousRevision >= long.MaxValue - 1;
            bool tracked = !saturated;
            result.replacement = new KnowledgeFactionState
            {
                factionInstanceId = observation.factionInstanceId,
                allocatorGeneration = observation.allocatorGeneration,
                factionDefName = tracked ? factionDefName : string.Empty,
                frozenDisplayLabel = tracked ? displayLabel : string.Empty,
                goodwill = tracked ? observation.goodwill : 0,
                relationKindToken = tracked ? relationKind : string.Empty,
                leaderPawnId = tracked ? leaderPawnId : string.Empty,
                defeated = tracked && observation.defeated,
                removed = tracked && observation.removed,
                observedTick = Math.Max(
                    observation.observedTick, sameIdentity ? previous.observedTick : 0),
                trackingStateToken = tracked
                    ? KnowledgeObservationTokens.TrackingTracked
                    : KnowledgeObservationTokens.TrackingCapacityUntracked,
                snapshotRevision = previousRevision <= 0
                    ? 1
                    : saturated ? long.MaxValue : previousRevision + 1
            };
            result.valid = true;
            result.savedMutationRequired = true;
            result.silentBaseline = observation.forceSilentBaseline || !sameIdentity
                || previous.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || !tracked;
            result.authoritativeStateChanged = tracked && (!sameIdentity
                || previous.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || previous.factionDefName != factionDefName
                || previous.frozenDisplayLabel != displayLabel
                || previous.goodwill != observation.goodwill
                || previous.relationKindToken != relationKind
                || previous.leaderPawnId != leaderPawnId
                || previous.defeated != observation.defeated
                || previous.removed != observation.removed);
            return result;
        }

        /// <summary>
        /// Encodes a sorted exact Def-name set with length prefixes. An over-cap set fails atomically;
        /// callers write capacity_untracked rather than saving a misleading truncated subset.
        /// </summary>
        public static bool TryEncodeRelationDefSet(
            IEnumerable<string> values,
            int maximumCharacters,
            out string encoded)
        {
            encoded = string.Empty;
            if (maximumCharacters <= 0) return false;
            SortedSet<string> canonical = new SortedSet<string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (string value in values)
                {
                    string cleaned = (value ?? string.Empty).Trim();
                    if (!RequiredRaw(cleaned)) return false;
                    canonical.Add(cleaned);
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(OrdinalSegmentCodec.Segment(RelationSetDomain));
            builder.Append(OrdinalSegmentCodec.Segment(
                canonical.Count.ToString(CultureInfo.InvariantCulture)));
            foreach (string value in canonical)
                builder.Append(OrdinalSegmentCodec.Segment(value));
            if (builder.Length > maximumCharacters) return false;
            encoded = builder.ToString();
            return true;
        }

        /// <summary>True only for vanilla's complete base-game diplomacy relation vocabulary.</summary>
        public static bool IsKnownFactionRelationKind(string value)
        {
            return value == KnowledgeObservationTokens.FactionRelationAlly
                || value == KnowledgeObservationTokens.FactionRelationNeutral
                || value == KnowledgeObservationTokens.FactionRelationHostile;
        }

        /// <summary>
        /// True when every retained relative snapshot is authoritative and none still names the exact
        /// faction subject. An untracked relative makes absence unknowable, so pruning fails closed.
        /// </summary>
        public static bool CanPruneFamilyFactionConnection(
            string factionSubjectId,
            IEnumerable<KnowledgeAwarenessState> relativeSnapshots)
        {
            string ignoredFaction;
            long ignoredGeneration;
            if (!MemoryIdentityCodec.TryParseFactionSubjectId(
                    factionSubjectId, out ignoredFaction, out ignoredGeneration)) return false;
            if (relativeSnapshots != null)
            {
                foreach (KnowledgeAwarenessState row in relativeSnapshots)
                {
                    if (row == null
                        || row.scopeKindToken != KnowledgeObservationTokens.ScopeRelative) continue;
                    if (row.trackingStateToken != KnowledgeObservationTokens.TrackingTracked)
                        return false;
                    if (FactEquals(
                            row.stateFacts,
                            KnowledgeObservationTokens.FactFactionSubject,
                            factionSubjectId)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True only when an open opinion accumulator still has its exact authoritative current-
        /// truth row. Missing, capacity-untracked, or generation-mismatched pairs are settled by
        /// dropping the episode; load repair must not invent an awareness marker for them.
        /// </summary>
        public static bool IsEpisodeBackedByTrackedAwareness(
            KnowledgeAwarenessState awareness,
            KnowledgeOpinionEpisodeState episode)
        {
            return awareness != null
                && episode != null
                && awareness.trackingStateToken == KnowledgeObservationTokens.TrackingTracked
                && awareness.scopeKindToken == KnowledgeObservationTokens.ScopeRelationship
                && awareness.factStreamToken == KnowledgeObservationTokens.StreamDirectedSocial
                && awareness.subjectKind == KnowledgeObservationTokens.SubjectPawn
                && awareness.scopeKindToken == episode.scopeKindToken
                && awareness.factStreamToken == episode.factStreamToken
                && awareness.subjectKind == episode.subjectKind
                && string.Equals(
                    awareness.subjectId, episode.subjectId, StringComparison.Ordinal)
                && awareness.captureInvalidationGeneration
                    == episode.captureInvalidationGeneration;
        }

        /// <summary>
        /// Resolves load-repair precedence: a missing pair never invents a marker; a duplicate
        /// conflict merges into an existing stream marker; otherwise only a tracked pair survives.
        /// </summary>
        public static KnowledgeEpisodeBackingDisposition EpisodeBackingDisposition(
            KnowledgeAwarenessState awareness,
            KnowledgeOpinionEpisodeState episode,
            bool duplicateConflict)
        {
            if (awareness == null)
                return KnowledgeEpisodeBackingDisposition.DropWithoutMarker;
            if (duplicateConflict)
                return KnowledgeEpisodeBackingDisposition.PublishConflictMarker;
            return IsEpisodeBackedByTrackedAwareness(awareness, episode)
                ? KnowledgeEpisodeBackingDisposition.Retain
                : KnowledgeEpisodeBackingDisposition.DropWithoutMarker;
        }

        /// <summary>
        /// A terminal snapshot marker is permanent inside its counter domain. Ordinary hidden-edge,
        /// orphan, or faction-removal cleanup may remove any other row but cannot erase and later
        /// reuse an exact key whose revision already reached MaxValue.
        /// </summary>
        public static bool CanRemoveShadowSnapshot(long snapshotRevision)
        {
            return snapshotRevision != long.MaxValue;
        }

        /// <summary>
        /// Plans one elapsed-time reconciliation decision. A clock rollback consumes the saved
        /// completion tick exactly once; an already-running or finishing pass is made silent without
        /// restarting its cursor.
        /// </summary>
        public static KnowledgeReconciliationSchedulePlan PlanReconciliationSchedule(
            int now,
            int lastCompletedTick,
            bool fullScanRequested,
            bool finishFullAfterQueue,
            int reconciliationIntervalTicks)
        {
            KnowledgeReconciliationSchedulePlan result =
                new KnowledgeReconciliationSchedulePlan();
            if (now < 0) return result;

            if (lastCompletedTick >= 0 && now < lastCompletedTick)
            {
                result.consumeCompletedTick = true;
                result.forceSilentBaseline = true;
                result.requestFullReconciliation = !fullScanRequested
                    && !finishFullAfterQueue;
                return result;
            }

            int interval = KnowledgeObservationPolicySnapshot
                .NormalizeReconciliationInterval(reconciliationIntervalTicks);
            if (!fullScanRequested && !finishFullAfterQueue
                && (lastCompletedTick < 0
                    || (long)now - lastCompletedTick >= interval))
            {
                result.requestFullReconciliation = true;
                result.forceSilentBaseline = lastCompletedTick < 0;
            }
            return result;
        }

        /// <summary>
        /// Publishes one transient Library fence only after every row in the current dirty/full-scan
        /// batch has settled. This prevents partial snapshots and per-row rebuild cancellation.
        /// </summary>
        public static bool ShouldPublishCompletedObservationBatch(
            bool publicationDirty,
            bool hasQueuedWork,
            bool fullScanRequested,
            bool finishFullAfterQueue)
        {
            return publicationDirty && !hasQueuedWork
                && !fullScanRequested && !finishFullAfterQueue;
        }

        /// <summary>
        /// Coalesces duplicate exact-hook work. Removal evidence is sticky, while any non-silent
        /// observation makes the merged item non-silent so a live transition cannot be hidden.
        /// </summary>
        public static KnowledgeObservationWorkMergePlan MergeObservationWorkFlags(
            bool existingRemovedFaction,
            bool existingForceSilentBaseline,
            bool incomingRemovedFaction,
            bool incomingForceSilentBaseline)
        {
            return new KnowledgeObservationWorkMergePlan
            {
                removedFaction = existingRemovedFaction || incomingRemovedFaction,
                forceSilentBaseline = existingForceSilentBaseline
                    && incomingForceSilentBaseline
            };
        }

        /// <summary>Classifies a queued faction instance against the pawn's live faction.</summary>
        public static string OwnerFactionConnectionKind(bool isCurrentFaction)
        {
            return isCurrentFaction
                ? KnowledgeObservationTokens.ConnectionCurrent
                : KnowledgeObservationTokens.ConnectionRecentFormer;
        }

        /// <summary>
        /// Applies owner-connection precedence: exact current/former observations replace old state,
        /// while family evidence cannot downgrade an already personal connection.
        /// </summary>
        public static string PreferPersonalFactionConnection(string previous, string observed)
        {
            if (observed != KnowledgeObservationTokens.ConnectionFamily) return observed;
            return FactionConnectionRank(previous) >= 2 ? previous : observed;
        }

        /// <summary>
        /// Missing-instance reconciliation may infer removal only from authoritative tracked truth.
        /// A capacity marker carries no factual fields and must remain an inert marker.
        /// </summary>
        public static bool CanInferMissingFactionRemoval(KnowledgeFactionState state)
        {
            return state != null && !state.removed
                && state.trackingStateToken == KnowledgeObservationTokens.TrackingTracked;
        }

        private static int FactionConnectionRank(string value)
        {
            if (value == KnowledgeObservationTokens.ConnectionCurrent) return 3;
            if (value == KnowledgeObservationTokens.ConnectionRecentFormer) return 2;
            if (value == KnowledgeObservationTokens.ConnectionFamily) return 1;
            return 0;
        }

        /// <summary>
        /// Selects the greatest awareness rank, collapses byte-equal top ties, and creates the exact
        /// baseline-only repair marker when a top tie or already-proven row-shape conflict exists.
        /// </summary>
        public static KnowledgeAwarenessRepairPlan PlanAwarenessDuplicateRepair(
            IList<KnowledgeAwarenessState> rows,
            bool containsInvalidRow,
            long currentCaptureGeneration)
        {
            KnowledgeAwarenessRepairPlan result = new KnowledgeAwarenessRepairPlan();
            if (rows == null || rows.Count == 0) return result;
            int winner = -1;
            bool conflict = containsInvalidRow;
            long minimumFirst = 0;
            long greatestLast = 0;
            long greatestRevision = 0;
            bool hasFirst = false;
            for (int i = 0; i < rows.Count; i++)
            {
                KnowledgeAwarenessState row = rows[i];
                if (row == null || (winner >= 0
                        && !SameAwarenessIdentity(rows[winner], row)))
                    return result;
                if (row.firstObservedTick >= 0
                    && (!hasFirst || row.firstObservedTick < minimumFirst))
                {
                    minimumFirst = row.firstObservedTick;
                    hasFirst = true;
                }
                greatestLast = Math.Max(greatestLast, row.lastObservedTick);
                greatestRevision = Math.Max(greatestRevision, row.snapshotRevision);
                if (winner < 0 || CompareAwarenessRank(row, rows[winner]) > 0)
                {
                    winner = i;
                    conflict = containsInvalidRow;
                }
                else if (CompareAwarenessRank(row, rows[winner]) == 0
                    && !AwarenessStatesEqual(rows[winner], row)) conflict = true;
            }

            result.valid = winner >= 0;
            result.conflict = conflict;
            if (!result.valid) return result;
            if (!conflict)
            {
                result.retainedIndex = winner;
                return result;
            }
            KnowledgeAwarenessState template = rows[0];
            result.repairMarker = new KnowledgeAwarenessState
            {
                snapshotId = template.snapshotId,
                scopeKindToken = template.scopeKindToken,
                subjectKind = template.subjectKind,
                subjectId = template.subjectId,
                factStreamToken = template.factStreamToken,
                captureInvalidationGeneration = Math.Max(0, currentCaptureGeneration),
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceRepairConflict,
                stateFacts = new List<KnowledgeStateFact>(),
                firstObservedTick = hasFirst ? minimumFirst : 0,
                lastObservedTick = Math.Max(0, greatestLast),
                lastSourceOccurrenceId = string.Empty,
                trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked,
                snapshotRevision = Math.Max(0, greatestRevision)
            };
            return result;
        }

        /// <summary>Applies the same top-rank rule to one exact opinion episode group.</summary>
        public static KnowledgeEpisodeRepairPlan PlanEpisodeDuplicateRepair(
            string ownerPawnId,
            string ownerEpochToken,
            IList<KnowledgeOpinionEpisodeState> rows,
            bool containsInvalidRow,
            long currentCaptureGeneration)
        {
            KnowledgeEpisodeRepairPlan result = new KnowledgeEpisodeRepairPlan();
            if (rows == null || rows.Count == 0) return result;
            int winner = -1;
            bool conflict = containsInvalidRow;
            long minimumFirst = 0;
            long greatestLast = 0;
            long greatestRevision = 0;
            bool hasFirst = false;
            for (int i = 0; i < rows.Count; i++)
            {
                KnowledgeOpinionEpisodeState row = rows[i];
                if (row == null || (winner >= 0 && !SameEpisodeIdentity(rows[winner], row)))
                    return result;
                if (row.firstObservedTick >= 0
                    && (!hasFirst || row.firstObservedTick < minimumFirst))
                {
                    minimumFirst = row.firstObservedTick;
                    hasFirst = true;
                }
                greatestLast = Math.Max(greatestLast, row.lastObservedTick);
                greatestRevision = Math.Max(greatestRevision, row.episodeRevision);
                if (winner < 0 || CompareEpisodeRank(row, rows[winner]) > 0)
                {
                    winner = i;
                    conflict = containsInvalidRow;
                }
                else if (CompareEpisodeRank(row, rows[winner]) == 0
                    && !EpisodeStatesEqual(rows[winner], row)) conflict = true;
            }

            result.valid = winner >= 0;
            result.conflict = conflict;
            if (!result.valid) return result;
            if (!conflict)
            {
                result.retainedIndex = winner;
                return result;
            }
            KnowledgeOpinionEpisodeState template = rows[0];
            string snapshotId;
            if (!TryCreateAwarenessId(
                    ownerPawnId,
                    ownerEpochToken,
                    template.scopeKindToken,
                    template.subjectKind,
                    template.subjectId,
                    template.factStreamToken,
                    out snapshotId)) return new KnowledgeEpisodeRepairPlan();
            result.repairMarker = new KnowledgeAwarenessState
            {
                snapshotId = snapshotId,
                scopeKindToken = template.scopeKindToken,
                subjectKind = template.subjectKind,
                subjectId = template.subjectId,
                factStreamToken = template.factStreamToken,
                captureInvalidationGeneration = Math.Max(0, currentCaptureGeneration),
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceRepairConflict,
                stateFacts = new List<KnowledgeStateFact>(),
                firstObservedTick = hasFirst ? minimumFirst : 0,
                lastObservedTick = Math.Max(0, greatestLast),
                lastSourceOccurrenceId = string.Empty,
                trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked,
                snapshotRevision = Math.Max(0, greatestRevision)
            };
            return result;
        }

        /// <summary>Applies deterministic top-rank repair to one exact global faction key.</summary>
        public static KnowledgeFactionRepairPlan PlanFactionDuplicateRepair(
            IList<KnowledgeFactionState> rows,
            bool containsInvalidRow)
        {
            KnowledgeFactionRepairPlan result = new KnowledgeFactionRepairPlan();
            if (rows == null || rows.Count == 0) return result;
            int winner = -1;
            bool conflict = containsInvalidRow;
            long greatestObserved = 0;
            long greatestRevision = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                KnowledgeFactionState row = rows[i];
                if (row == null || (winner >= 0 && !SameFactionIdentity(rows[winner], row)))
                    return result;
                greatestObserved = Math.Max(greatestObserved, row.observedTick);
                greatestRevision = Math.Max(greatestRevision, row.snapshotRevision);
                if (winner < 0 || CompareFactionRank(row, rows[winner]) > 0)
                {
                    winner = i;
                    conflict = containsInvalidRow;
                }
                else if (CompareFactionRank(row, rows[winner]) == 0
                    && !FactionStatesEqual(rows[winner], row)) conflict = true;
            }
            result.valid = winner >= 0;
            result.conflict = conflict;
            if (!result.valid) return result;
            if (!conflict)
            {
                result.retainedIndex = winner;
                return result;
            }
            KnowledgeFactionState template = rows[0];
            result.repairMarker = new KnowledgeFactionState
            {
                factionInstanceId = template.factionInstanceId,
                allocatorGeneration = template.allocatorGeneration,
                factionDefName = string.Empty,
                frozenDisplayLabel = string.Empty,
                goodwill = 0,
                relationKindToken = string.Empty,
                leaderPawnId = string.Empty,
                defeated = false,
                removed = false,
                observedTick = Math.Max(0, greatestObserved),
                trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked,
                snapshotRevision = Math.Max(0, greatestRevision)
            };
            return result;
        }

        /// <summary>
        /// True only for the three exact M6 stream shapes. The stream token alone is insufficient:
        /// its scope and typed subject must agree so a corrupt row cannot be reinterpreted.
        /// </summary>
        public static bool IsKnownObservationStreamShape(
            string scopeKindToken,
            string subjectKind,
            string factStreamToken)
        {
            return (scopeKindToken == KnowledgeObservationTokens.ScopeRelationship
                    && subjectKind == KnowledgeObservationTokens.SubjectPawn
                    && factStreamToken == KnowledgeObservationTokens.StreamDirectedSocial)
                || (scopeKindToken == KnowledgeObservationTokens.ScopeRelative
                    && subjectKind == KnowledgeObservationTokens.SubjectPawn
                    && factStreamToken == KnowledgeObservationTokens.StreamRelativeState)
                || (scopeKindToken == KnowledgeObservationTokens.ScopeFaction
                    && subjectKind == KnowledgeObservationTokens.SubjectFaction
                    && factStreamToken == KnowledgeObservationTokens.StreamFactionConnection);
        }

        /// <summary>Validates the exact typed subject rather than accepting a label or Def name.</summary>
        public static bool IsValidObservationSubject(string subjectKind, string subjectId)
        {
            if (subjectKind == KnowledgeObservationTokens.SubjectPawn) return RequiredRaw(subjectId);
            string ignoredFactionInstanceId;
            long ignoredGeneration;
            return subjectKind == KnowledgeObservationTokens.SubjectFaction
                && MemoryIdentityCodec.TryParseFactionSubjectId(
                    subjectId, out ignoredFactionInstanceId, out ignoredGeneration);
        }

        /// <summary>
        /// Canonicalizes one M6 state-fact list by ordinal key, collapses byte-equal duplicates, and
        /// rejects conflicts, unknown keys/values, malformed UTF-16, or configured-cap overflow.
        /// </summary>
        public static bool TryNormalizeStateFacts(
            IEnumerable<KnowledgeStateFact> source,
            string scopeKindToken,
            string subjectKind,
            string factStreamToken,
            KnowledgeObservationPolicySnapshot policy,
            out List<KnowledgeStateFact> facts)
        {
            KnowledgeObservationPolicySnapshot safe =
                (policy ?? new KnowledgeObservationPolicySnapshot()).Normalized();
            if (!IsKnownObservationStreamShape(
                    scopeKindToken, subjectKind, factStreamToken)
                || !TryCanonicalFacts(source, safe, out facts))
            {
                facts = new List<KnowledgeStateFact>();
                return false;
            }

            for (int i = 0; i < facts.Count; i++)
            {
                KnowledgeStateFact fact = facts[i];
                if (!IsValidFactForStream(factStreamToken, fact?.key, fact?.value))
                {
                    facts.Clear();
                    return false;
                }
            }
            return true;
        }

        /// <summary>Canonicalizes the exact two-fact opinion baseline/current episode payload.</summary>
        public static bool TryNormalizeOpinionEpisodeFacts(
            IEnumerable<KnowledgeStateFact> source,
            KnowledgeObservationPolicySnapshot policy,
            out List<KnowledgeStateFact> facts)
        {
            if (!TryNormalizeStateFacts(
                    source,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.SubjectPawn,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    policy,
                    out facts)
                || facts.Count != 2
                || !ContainsFact(facts, KnowledgeObservationTokens.FactOpinionValue)
                || !ContainsFact(facts, KnowledgeObservationTokens.FactOpinionBand))
            {
                facts = new List<KnowledgeStateFact>();
                return false;
            }
            return true;
        }

        /// <summary>Plans one generic replaceable current-truth row with silent rebaseline rules.</summary>
        public static KnowledgeAwarenessPlan PlanCurrentTruth(
            KnowledgeAwarenessState previous,
            KnowledgeCurrentTruthObservation observation,
            KnowledgeObservationPolicySnapshot policy)
        {
            KnowledgeAwarenessPlan result = new KnowledgeAwarenessPlan();
            KnowledgeObservationPolicySnapshot safe =
                (policy ?? new KnowledgeObservationPolicySnapshot()).Normalized();
            if (observation == null || observation.observedTick < 0
                || !KnowledgeObservationTokens.IsKnownEvidence(
                    observation.knownnessEvidenceToken)) return result;

            string snapshotId;
            if (!TryCreateAwarenessId(
                    observation.ownerPawnId,
                    observation.ownerEpochToken,
                    observation.scopeKindToken,
                    observation.subjectKind,
                    observation.subjectId,
                    observation.factStreamToken,
                    out snapshotId)) return result;

            if (!IsKnownObservationStreamShape(
                    observation.scopeKindToken,
                    observation.subjectKind,
                    observation.factStreamToken)
                || !IsValidObservationSubject(
                    observation.subjectKind, observation.subjectId)) return result;

            List<KnowledgeStateFact> facts;
            bool authoritative = TryNormalizeStateFacts(
                observation.stateFacts,
                observation.scopeKindToken,
                observation.subjectKind,
                observation.factStreamToken,
                safe,
                out facts);
            bool sameIdentity = previous != null
                && string.Equals(previous.snapshotId, snapshotId, StringComparison.Ordinal);
            bool generationUsable = observation.captureInvalidationGeneration > 0
                && observation.captureInvalidationGeneration < long.MaxValue;
            bool baselineOnly = observation.forceSilentBaseline
                || !observation.captureAllowed
                || !generationUsable
                || !sameIdentity
                || previous.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || previous.captureInvalidationGeneration
                    != observation.captureInvalidationGeneration;
            long previousRevision = sameIdentity ? previous.snapshotRevision : 0;
            if (sameIdentity && previousRevision == long.MaxValue)
            {
                // This exact saved counter is terminal. Live game truth remains available to its
                // ordinary renderer, but the shadow row cannot change, wrap, or silently reuse Max.
                result.valid = true;
                result.savedMutationRequired = false;
                result.silentBaseline = true;
                result.replacement = previous;
                return result;
            }
            bool revisionSaturated = previousRevision >= long.MaxValue - 1;
            if (!authoritative || revisionSaturated)
            {
                facts.Clear();
            }

            bool tracked = authoritative && !revisionSaturated;
            long revision = previousRevision <= 0
                ? 1
                : revisionSaturated ? long.MaxValue : previousRevision + 1;
            long firstTick = baselineOnly || !sameIdentity
                ? observation.observedTick
                : Math.Max(0, previous.firstObservedTick);
            result.replacement = new KnowledgeAwarenessState
            {
                snapshotId = snapshotId,
                scopeKindToken = observation.scopeKindToken,
                subjectKind = observation.subjectKind,
                subjectId = observation.subjectId,
                factStreamToken = observation.factStreamToken,
                captureInvalidationGeneration = observation.captureInvalidationGeneration,
                knownnessEvidenceToken = tracked
                    ? observation.knownnessEvidenceToken
                    : KnowledgeObservationTokens.EvidenceRepairConflict,
                stateFacts = facts,
                firstObservedTick = firstTick,
                lastObservedTick = Math.Max(
                    observation.observedTick, sameIdentity ? previous.lastObservedTick : 0),
                lastSourceOccurrenceId = observation.sourceOccurrenceId ?? string.Empty,
                trackingStateToken = tracked
                    ? KnowledgeObservationTokens.TrackingTracked
                    : KnowledgeObservationTokens.TrackingCapacityUntracked,
                snapshotRevision = revision
            };
            result.valid = true;
            result.savedMutationRequired = true;
            result.silentBaseline = baselineOnly || !tracked;
            result.authoritativeStateChanged = tracked && (!sameIdentity
                || previous.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || !FactsEqual(previous.stateFacts, facts));
            return result;
        }

        /// <summary>Plans the directed social snapshot plus its deterministic open opinion episode.</summary>
        public static KnowledgeOpinionPlan PlanDirectedOpinion(
            KnowledgeAwarenessState previous,
            KnowledgeOpinionEpisodeState priorEpisode,
            KnowledgeOpinionObservation observation,
            KnowledgeOpinionBandThresholds bands,
            KnowledgeObservationPolicySnapshot policy)
        {
            KnowledgeOpinionPlan result = new KnowledgeOpinionPlan();
            if (observation == null) return result;
            KnowledgeObservationPolicySnapshot safe =
                (policy ?? new KnowledgeObservationPolicySnapshot()).Normalized();
            KnowledgeOpinionBandThresholds safeBands = bands ?? new KnowledgeOpinionBandThresholds();
            if (!safeBands.IsOrdered()) safeBands = new KnowledgeOpinionBandThresholds();

            string outbound;
            string inbound;
            if (!TryEncodeRelationDefSet(
                    observation.outboundRelationDefNames,
                    safe.maximumFactValueCharacters,
                    out outbound)
                || !TryEncodeRelationDefSet(
                    observation.inboundRelationDefNames,
                    safe.maximumFactValueCharacters,
                    out inbound))
            {
                outbound = null;
                inbound = null;
            }

            List<KnowledgeStateFact> socialFacts = new List<KnowledgeStateFact>();
            if (outbound != null && inbound != null)
            {
                socialFacts.Add(Fact(KnowledgeObservationTokens.FactOpinionValue,
                    observation.opinion.ToString(CultureInfo.InvariantCulture)));
                socialFacts.Add(Fact(KnowledgeObservationTokens.FactOpinionBand,
                    OpinionBandToken(observation.opinion, safeBands)));
                socialFacts.Add(Fact(KnowledgeObservationTokens.FactOutboundRelations, outbound));
                socialFacts.Add(Fact(KnowledgeObservationTokens.FactInboundRelations, inbound));
            }

            KnowledgeAwarenessPlan awareness = PlanCurrentTruth(
                previous,
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = observation.ownerPawnId,
                    ownerEpochToken = observation.ownerEpochToken,
                    scopeKindToken = KnowledgeObservationTokens.ScopeRelationship,
                    subjectKind = KnowledgeObservationTokens.SubjectPawn,
                    subjectId = observation.subjectPawnId,
                    factStreamToken = KnowledgeObservationTokens.StreamDirectedSocial,
                    captureInvalidationGeneration = observation.captureInvalidationGeneration,
                    knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                    stateFacts = socialFacts,
                    observedTick = observation.observedTick,
                    sourceOccurrenceId = observation.sourceOccurrenceId,
                    captureAllowed = observation.captureAllowed,
                    forceSilentBaseline = observation.forceSilentBaseline
                },
                safe);
            if (!awareness.valid) return result;

            result.valid = true;
            result.savedMutationRequired = awareness.savedMutationRequired;
            result.replacement = awareness.replacement;
            result.silentBaseline = awareness.silentBaseline;
            result.formalRelationChanged = previous != null
                && previous.trackingStateToken == KnowledgeObservationTokens.TrackingTracked
                && (!FactEquals(previous.stateFacts,
                        KnowledgeObservationTokens.FactOutboundRelations, outbound)
                    || !FactEquals(previous.stateFacts,
                        KnowledgeObservationTokens.FactInboundRelations, inbound));

            int previousOpinion;
            bool previousOpinionKnown = TryOpinion(previous?.stateFacts, out previousOpinion);
            if (awareness.silentBaseline || !previousOpinionKnown
                || awareness.replacement.trackingStateToken
                    != KnowledgeObservationTokens.TrackingTracked
                || result.formalRelationChanged)
            {
                result.openEpisode = null;
                return result;
            }

            string pairKey;
            if (!TryCreateDirectedPairKey(
                    observation.ownerPawnId, observation.subjectPawnId, out pairKey)) return result;
            result.openEpisode = AdvanceOpinionEpisode(
                priorEpisode,
                pairKey,
                previousOpinion,
                observation,
                safeBands,
                safe,
                result);
            return result;
        }

        private static KnowledgeOpinionEpisodeState AdvanceOpinionEpisode(
            KnowledgeOpinionEpisodeState prior,
            string pairKey,
            int previousOpinion,
            KnowledgeOpinionObservation observation,
            KnowledgeOpinionBandThresholds bands,
            KnowledgeObservationPolicySnapshot policy,
            KnowledgeOpinionPlan result)
        {
            int currentOpinion = observation.opinion;
            string previousBand = OpinionBandToken(previousOpinion, bands);
            string currentBand = OpinionBandToken(currentOpinion, bands);
            int stepDirection = Math.Sign(currentOpinion - previousOpinion);
            bool priorValid = EpisodeMatches(prior, observation, pairKey);
            if (priorValid && (ElapsedAtLeast(
                    observation.observedTick,
                    prior.lastObservedTick,
                    policy.opinionEpisodeInactivityTicks)
                || ElapsedAtLeast(
                    observation.observedTick,
                    prior.firstObservedTick,
                    policy.opinionEpisodeMaximumTicks)))
            {
                // No catch-up across an inactive accumulator. Current truth is already committed
                // by the awareness replacement and becomes the next silent baseline.
                return null;
            }

            int baselineOpinion;
            int episodeCurrentOpinion;
            if (priorValid
                && TryOpinion(prior.baselineFacts, out baselineOpinion)
                && TryOpinion(prior.currentFacts, out episodeCurrentOpinion))
            {
                int prevailingDirection = Math.Sign(episodeCurrentOpinion - baselineOpinion);
                bool reversal = stepDirection != 0 && prevailingDirection != 0
                    && stepDirection != prevailingDirection;
                if (reversal)
                {
                    if (Math.Abs(currentOpinion - episodeCurrentOpinion)
                        >= policy.opinionReversalChangePoints)
                    {
                        result.qualifiedForFutureCapture = true;
                        result.qualificationReasonToken = "reversal";
                        return null;
                    }

                    return stepDirection == 0
                        ? null
                        : NewEpisode(previousOpinion, currentOpinion, pairKey, observation, bands);
                }

                bool justCrossedBand = string.Equals(
                        OpinionBandToken(baselineOpinion, bands),
                        OpinionBandToken(episodeCurrentOpinion, bands),
                        StringComparison.Ordinal)
                    && !string.Equals(previousBand, currentBand, StringComparison.Ordinal);
                if (justCrossedBand)
                {
                    prior = NewEpisode(previousOpinion, currentOpinion, pairKey, observation, bands);
                    baselineOpinion = previousOpinion;
                    episodeCurrentOpinion = currentOpinion;
                }
                else if (stepDirection != 0)
                {
                    if (prior.episodeRevision >= long.MaxValue - 1)
                    {
                        // The episode cannot safely advance. Permanently fence this exact stream at
                        // the awareness counter's terminal marker so no later observation can infer
                        // history by wrapping or reusing the saturated episode revision.
                        ConvertOpinionAwarenessToCapacityMarker(result);
                        return null;
                    }

                    prior.currentFacts = OpinionFacts(currentOpinion, bands);
                    prior.lastObservedTick = Math.Max(prior.lastObservedTick, observation.observedTick);
                    prior.lastSourceOccurrenceId = observation.sourceOccurrenceId ?? string.Empty;
                    prior.episodeRevision++;
                    episodeCurrentOpinion = currentOpinion;
                }

                int cumulativeDirection = Math.Sign(episodeCurrentOpinion - baselineOpinion);
                if (cumulativeDirection != 0
                    && Math.Abs(episodeCurrentOpinion - baselineOpinion)
                        >= policy.opinionCumulativeChangePoints)
                {
                    result.qualifiedForFutureCapture = true;
                    result.qualificationReasonToken = "cumulative";
                    return null;
                }

                string baselineBand = OpinionBandToken(baselineOpinion, bands);
                string episodeBand = OpinionBandToken(episodeCurrentOpinion, bands);
                if (!string.Equals(baselineBand, episodeBand, StringComparison.Ordinal)
                    && ElapsedAtLeast(
                        observation.observedTick,
                        prior.firstObservedTick,
                        policy.opinionBandSustainTicks)
                    && BeyondBandHysteresis(
                        baselineBand,
                        episodeBand,
                        episodeCurrentOpinion,
                        bands,
                        policy.opinionHysteresisPoints))
                {
                    result.qualifiedForFutureCapture = true;
                    result.qualificationReasonToken = "band_crossing";
                    return null;
                }

                return prior;
            }

            if (stepDirection == 0) return null;
            KnowledgeOpinionEpisodeState created = NewEpisode(
                previousOpinion, currentOpinion, pairKey, observation, bands);
            if (Math.Abs(currentOpinion - previousOpinion)
                >= policy.opinionCumulativeChangePoints)
            {
                result.qualifiedForFutureCapture = true;
                result.qualificationReasonToken = "cumulative";
                return null;
            }
            return created;
        }

        /// <summary>
        /// Applies the exhaustive-counter rule for an opinion episode that cannot advance.
        /// The exact awareness key remains saved, but its facts are no longer authoritative.
        /// </summary>
        private static void ConvertOpinionAwarenessToCapacityMarker(KnowledgeOpinionPlan result)
        {
            if (result?.replacement == null) return;
            result.replacement.knownnessEvidenceToken =
                KnowledgeObservationTokens.EvidenceRepairConflict;
            result.replacement.stateFacts = new List<KnowledgeStateFact>();
            result.replacement.trackingStateToken =
                KnowledgeObservationTokens.TrackingCapacityUntracked;
            result.replacement.snapshotRevision = long.MaxValue;
            result.silentBaseline = true;
            result.qualifiedForFutureCapture = false;
            result.qualificationReasonToken = string.Empty;
        }

        private static KnowledgeOpinionEpisodeState NewEpisode(
            int baselineOpinion,
            int currentOpinion,
            string pairKey,
            KnowledgeOpinionObservation observation,
            KnowledgeOpinionBandThresholds bands)
        {
            string direction = currentOpinion >= baselineOpinion
                ? KnowledgeObservationTokens.DirectionRising
                : KnowledgeObservationTokens.DirectionFalling;
            string episodeId;
            if (!TryCreateEpisodeId(
                    observation.ownerPawnId,
                    observation.ownerEpochToken,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    KnowledgeObservationTokens.OpinionEpisodeRule,
                    KnowledgeObservationTokens.OpinionEpisodeKind,
                    KnowledgeObservationTokens.SubjectPawn,
                    observation.subjectPawnId,
                    pairKey,
                    direction,
                    out episodeId)) return null;
            return new KnowledgeOpinionEpisodeState
            {
                episodeId = episodeId,
                captureRuleId = KnowledgeObservationTokens.OpinionEpisodeRule,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelationship,
                factStreamToken = KnowledgeObservationTokens.StreamDirectedSocial,
                category = MemoryContractTokens.CategoryRelationships,
                captureInvalidationGeneration = observation.captureInvalidationGeneration,
                episodeKindToken = KnowledgeObservationTokens.OpinionEpisodeKind,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = observation.subjectPawnId,
                pairOrStreamKey = pairKey,
                directionToken = direction,
                baselineFacts = OpinionFacts(baselineOpinion, bands),
                currentFacts = OpinionFacts(currentOpinion, bands),
                firstObservedTick = observation.observedTick,
                lastObservedTick = observation.observedTick,
                lastSourceOccurrenceId = observation.sourceOccurrenceId ?? string.Empty,
                episodeRevision = 1
            };
        }

        private static bool EpisodeMatches(
            KnowledgeOpinionEpisodeState episode,
            KnowledgeOpinionObservation observation,
            string pairKey)
        {
            return episode != null
                && episode.captureInvalidationGeneration
                    == observation.captureInvalidationGeneration
                && episode.scopeKindToken == KnowledgeObservationTokens.ScopeRelationship
                && episode.factStreamToken == KnowledgeObservationTokens.StreamDirectedSocial
                && episode.captureRuleId == KnowledgeObservationTokens.OpinionEpisodeRule
                && episode.episodeKindToken == KnowledgeObservationTokens.OpinionEpisodeKind
                && episode.subjectKind == KnowledgeObservationTokens.SubjectPawn
                && string.Equals(episode.subjectId,
                    observation.subjectPawnId, StringComparison.Ordinal)
                && string.Equals(episode.pairOrStreamKey, pairKey, StringComparison.Ordinal)
                && (episode.directionToken == KnowledgeObservationTokens.DirectionRising
                    || episode.directionToken == KnowledgeObservationTokens.DirectionFalling);
        }

        private static bool BeyondBandHysteresis(
            string baselineBand,
            string currentBand,
            int currentOpinion,
            KnowledgeOpinionBandThresholds bands,
            int hysteresis)
        {
            int baselineRank = BandRank(baselineBand);
            int currentRank = BandRank(currentBand);
            if (baselineRank == currentRank) return false;
            int h = Math.Max(0, hysteresis);
            if (currentRank > baselineRank)
            {
                if (currentBand == KnowledgeObservationTokens.OpinionDevoted)
                    return currentOpinion >= bands.devoted + h;
                if (currentBand == KnowledgeObservationTokens.OpinionFriendly)
                    return currentOpinion >= bands.friendly + h;
                if (currentBand == KnowledgeObservationTokens.OpinionNeutral)
                    return currentOpinion > bands.neutralAbove + h;
                return currentOpinion > bands.strainedAbove + h;
            }

            if (baselineBand == KnowledgeObservationTokens.OpinionDevoted)
                return currentOpinion < bands.devoted - h;
            if (baselineBand == KnowledgeObservationTokens.OpinionFriendly)
                return currentOpinion < bands.friendly - h;
            if (baselineBand == KnowledgeObservationTokens.OpinionNeutral)
                return currentOpinion <= bands.neutralAbove - h;
            return currentOpinion <= bands.strainedAbove - h;
        }

        private static int BandRank(string band)
        {
            if (band == KnowledgeObservationTokens.OpinionDevoted) return 4;
            if (band == KnowledgeObservationTokens.OpinionFriendly) return 3;
            if (band == KnowledgeObservationTokens.OpinionNeutral) return 2;
            if (band == KnowledgeObservationTokens.OpinionStrained) return 1;
            return 0;
        }

        private static bool ElapsedAtLeast(long now, long then, int duration)
        {
            return duration <= 0 || (now >= then && now - then >= duration);
        }

        private static List<KnowledgeStateFact> OpinionFacts(
            int opinion,
            KnowledgeOpinionBandThresholds bands)
        {
            return new List<KnowledgeStateFact>
            {
                Fact(KnowledgeObservationTokens.FactOpinionValue,
                    opinion.ToString(CultureInfo.InvariantCulture)),
                Fact(KnowledgeObservationTokens.FactOpinionBand,
                    OpinionBandToken(opinion, bands))
            };
        }

        private static KnowledgeStateFact Fact(string key, string value)
        {
            return new KnowledgeStateFact
            {
                key = key ?? string.Empty,
                value = value ?? string.Empty
            };
        }

        private static bool IsValidFactForStream(
            string factStreamToken,
            string key,
            string value)
        {
            if (factStreamToken == KnowledgeObservationTokens.StreamDirectedSocial)
            {
                if (key == KnowledgeObservationTokens.FactOpinionValue)
                    return IsCanonicalOpinion(value);
                if (key == KnowledgeObservationTokens.FactOpinionBand)
                    return IsOpinionBand(value);
                if (key == KnowledgeObservationTokens.FactOutboundRelations
                    || key == KnowledgeObservationTokens.FactInboundRelations)
                    return IsCanonicalRelationDefSet(value);
                return false;
            }

            if (factStreamToken == KnowledgeObservationTokens.StreamRelativeState)
            {
                if (key == KnowledgeObservationTokens.FactRelationDefs)
                    return IsCanonicalRelationDefSet(value);
                if (key == KnowledgeObservationTokens.FactLifeState)
                    return value == KnowledgeObservationTokens.LifeAlive
                        || value == KnowledgeObservationTokens.LifeDead;
                if (key == KnowledgeObservationTokens.FactLocationState)
                    return IsCanonicalLocationState(value);
                if (key == KnowledgeObservationTokens.FactFactionSubject)
                {
                    string ignoredFaction;
                    long ignoredGeneration;
                    return value == "none" || MemoryIdentityCodec.TryParseFactionSubjectId(
                        value, out ignoredFaction, out ignoredGeneration);
                }
                return false;
            }

            return factStreamToken == KnowledgeObservationTokens.StreamFactionConnection
                && key == KnowledgeObservationTokens.FactConnectionKind
                && (value == KnowledgeObservationTokens.ConnectionCurrent
                    || value == KnowledgeObservationTokens.ConnectionRecentFormer
                    || value == KnowledgeObservationTokens.ConnectionFamily);
        }

        private static bool IsCanonicalOpinion(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                && parsed >= -100 && parsed <= 100
                && string.Equals(parsed.ToString(CultureInfo.InvariantCulture), value,
                    StringComparison.Ordinal);
        }

        private static bool IsOpinionBand(string value)
        {
            return value == KnowledgeObservationTokens.OpinionDevoted
                || value == KnowledgeObservationTokens.OpinionFriendly
                || value == KnowledgeObservationTokens.OpinionNeutral
                || value == KnowledgeObservationTokens.OpinionStrained
                || value == KnowledgeObservationTokens.OpinionHostile;
        }

        private static bool IsCanonicalRelationDefSet(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int offset = 0;
            string domain;
            string countText;
            if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                    value, ref offset, MemoryIdentityCodec.MaximumRawIdentityCharacters,
                    false, out domain)
                || domain != RelationSetDomain
                || !OrdinalSegmentCodec.TryReadCanonicalSegment(
                    value, ref offset, 20, false, out countText)) return false;

            int count;
            if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count)
                || count < 0
                || !string.Equals(count.ToString(CultureInfo.InvariantCulture), countText,
                    StringComparison.Ordinal)) return false;

            string previous = null;
            for (int i = 0; i < count; i++)
            {
                string current;
                if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                        value, ref offset, MemoryIdentityCodec.MaximumRawIdentityCharacters,
                        false, out current)
                    || !RequiredRaw(current)
                    || (previous != null && string.CompareOrdinal(previous, current) >= 0))
                    return false;
                previous = current;
            }
            return offset == value.Length;
        }

        private static bool IsCanonicalLocationState(string value)
        {
            if (value == KnowledgeObservationTokens.LocationWorld
                || value == KnowledgeObservationTokens.LocationUnknown) return true;
            if (string.IsNullOrEmpty(value)) return false;

            int offset = 0;
            string kind;
            string exactId;
            if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                    value, ref offset, MemoryIdentityCodec.MaximumRawIdentityCharacters,
                    false, out kind)
                || !OrdinalSegmentCodec.TryReadCanonicalSegment(
                    value, ref offset, MemoryIdentityCodec.MaximumRawIdentityCharacters,
                    false, out exactId)
                || offset != value.Length
                || !RequiredRaw(exactId)) return false;
            return kind == "map" || kind == "caravan" || kind == "corpse_map";
        }

        private static bool ContainsFact(IList<KnowledgeStateFact> facts, string key)
        {
            for (int i = 0; facts != null && i < facts.Count; i++)
                if (string.Equals(facts[i]?.key, key, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool TryCanonicalFacts(
            IEnumerable<KnowledgeStateFact> source,
            KnowledgeObservationPolicySnapshot policy,
            out List<KnowledgeStateFact> facts)
        {
            facts = new List<KnowledgeStateFact>();
            SortedDictionary<string, string> unique =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (KnowledgeStateFact fact in source)
                {
                    string key = fact?.key ?? string.Empty;
                    string value = fact?.value ?? string.Empty;
                    if (key.Length == 0 || key.Length > policy.maximumFactKeyCharacters
                        || value.Length > policy.maximumFactValueCharacters
                        || !MemoryIdentityCodec.IsWellFormedUtf16(key)
                        || !MemoryIdentityCodec.IsWellFormedUtf16(value)) return false;
                    string existing;
                    if (unique.TryGetValue(key, out existing))
                    {
                        if (!string.Equals(existing, value, StringComparison.Ordinal)) return false;
                        continue;
                    }
                    unique.Add(key, value);
                }
            }

            if (unique.Count == 0 || unique.Count > policy.maximumStateFacts) return false;
            foreach (KeyValuePair<string, string> pair in unique)
                facts.Add(Fact(pair.Key, pair.Value));
            return true;
        }

        private static bool FactsEqual(
            IList<KnowledgeStateFact> left,
            IList<KnowledgeStateFact> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i]?.key, right[i]?.key, StringComparison.Ordinal)
                    || !string.Equals(left[i]?.value, right[i]?.value,
                        StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static int CompareAwarenessRank(
            KnowledgeAwarenessState first,
            KnowledgeAwarenessState second)
        {
            int rank = first.lastObservedTick.CompareTo(second.lastObservedTick);
            return rank != 0 ? rank : first.snapshotRevision.CompareTo(second.snapshotRevision);
        }

        private static bool SameAwarenessIdentity(
            KnowledgeAwarenessState first,
            KnowledgeAwarenessState second)
        {
            return first.snapshotId == second.snapshotId
                && first.scopeKindToken == second.scopeKindToken
                && first.subjectKind == second.subjectKind
                && first.subjectId == second.subjectId
                && first.factStreamToken == second.factStreamToken;
        }

        private static bool AwarenessStatesEqual(
            KnowledgeAwarenessState first,
            KnowledgeAwarenessState second)
        {
            return SameAwarenessIdentity(first, second)
                && first.captureInvalidationGeneration == second.captureInvalidationGeneration
                && first.knownnessEvidenceToken == second.knownnessEvidenceToken
                && FactsEqual(first.stateFacts, second.stateFacts)
                && first.firstObservedTick == second.firstObservedTick
                && first.lastObservedTick == second.lastObservedTick
                && first.lastSourceOccurrenceId == second.lastSourceOccurrenceId
                && first.trackingStateToken == second.trackingStateToken
                && first.snapshotRevision == second.snapshotRevision;
        }

        private static int CompareEpisodeRank(
            KnowledgeOpinionEpisodeState first,
            KnowledgeOpinionEpisodeState second)
        {
            int rank = first.lastObservedTick.CompareTo(second.lastObservedTick);
            return rank != 0 ? rank : first.episodeRevision.CompareTo(second.episodeRevision);
        }

        private static bool SameEpisodeIdentity(
            KnowledgeOpinionEpisodeState first,
            KnowledgeOpinionEpisodeState second)
        {
            return first.episodeId == second.episodeId
                && first.captureRuleId == second.captureRuleId
                && first.scopeKindToken == second.scopeKindToken
                && first.factStreamToken == second.factStreamToken
                && first.episodeKindToken == second.episodeKindToken
                && first.subjectKind == second.subjectKind
                && first.subjectId == second.subjectId
                && first.pairOrStreamKey == second.pairOrStreamKey
                && first.directionToken == second.directionToken;
        }

        private static bool EpisodeStatesEqual(
            KnowledgeOpinionEpisodeState first,
            KnowledgeOpinionEpisodeState second)
        {
            return SameEpisodeIdentity(first, second)
                && first.category == second.category
                && first.captureInvalidationGeneration == second.captureInvalidationGeneration
                && FactsEqual(first.baselineFacts, second.baselineFacts)
                && FactsEqual(first.currentFacts, second.currentFacts)
                && first.firstObservedTick == second.firstObservedTick
                && first.lastObservedTick == second.lastObservedTick
                && first.lastSourceOccurrenceId == second.lastSourceOccurrenceId
                && first.episodeRevision == second.episodeRevision;
        }

        private static int CompareFactionRank(
            KnowledgeFactionState first,
            KnowledgeFactionState second)
        {
            int rank = first.observedTick.CompareTo(second.observedTick);
            return rank != 0 ? rank : first.snapshotRevision.CompareTo(second.snapshotRevision);
        }

        private static bool SameFactionIdentity(
            KnowledgeFactionState first,
            KnowledgeFactionState second)
        {
            return first.factionInstanceId == second.factionInstanceId
                && first.allocatorGeneration == second.allocatorGeneration;
        }

        private static bool FactionStatesEqual(
            KnowledgeFactionState first,
            KnowledgeFactionState second)
        {
            return SameFactionIdentity(first, second)
                && first.factionDefName == second.factionDefName
                && first.frozenDisplayLabel == second.frozenDisplayLabel
                && first.goodwill == second.goodwill
                && first.relationKindToken == second.relationKindToken
                && first.leaderPawnId == second.leaderPawnId
                && first.defeated == second.defeated
                && first.removed == second.removed
                && first.observedTick == second.observedTick
                && first.trackingStateToken == second.trackingStateToken
                && first.snapshotRevision == second.snapshotRevision;
        }

        private static bool FactEquals(
            IList<KnowledgeStateFact> facts,
            string key,
            string expected)
        {
            if (expected == null) return false;
            for (int i = 0; facts != null && i < facts.Count; i++)
            {
                if (string.Equals(facts[i]?.key, key, StringComparison.Ordinal))
                    return string.Equals(facts[i]?.value, expected, StringComparison.Ordinal);
            }
            return false;
        }

        private static bool TryOpinion(IList<KnowledgeStateFact> facts, out int opinion)
        {
            opinion = 0;
            for (int i = 0; facts != null && i < facts.Count; i++)
            {
                KnowledgeStateFact fact = facts[i];
                if (fact != null && fact.key == KnowledgeObservationTokens.FactOpinionValue)
                {
                    return int.TryParse(fact.value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out opinion)
                        && opinion >= -100 && opinion <= 100;
                }
            }
            return false;
        }

        private static bool RequiredRaw(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool RequiredComposite(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ValidEpoch(string value)
        {
            bool ignored;
            return MemoryIdentityCodec.TryValidateEpochToken(value, out ignored);
        }

        private static bool TryJoin(IEnumerable<string> segments, out string encoded)
        {
            encoded = string.Empty;
            StringBuilder builder = new StringBuilder();
            foreach (string segment in segments)
            {
                if (segment == null || !MemoryIdentityCodec.IsWellFormedUtf16(segment)) return false;
                string framed = OrdinalSegmentCodec.Segment(segment);
                if (builder.Length > MemoryIdentityCodec.MaximumCompleteKeyCharacters
                    - framed.Length) return false;
                builder.Append(framed);
            }
            if (builder.Length == 0
                || builder.Length > MemoryIdentityCodec.MaximumCompleteKeyCharacters) return false;
            encoded = builder.ToString();
            return true;
        }
    }
}
