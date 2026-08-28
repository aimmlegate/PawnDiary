// DiaryGameComponent.MemoryObservation.cs — M6 current truth plus M7 factual-capture adapter.
// It observes live RimWorld pawns/factions only on the main thread, converts them immediately to
// detached policy inputs, and commits bounded SavedMemoryAwarenessSnapshot/open-episode/global-
// faction rows. M7 may additionally admit a detached factual block, but this file never creates a
// DiaryEvent, reflection opportunity, provider request, or prompt-format change.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety"). Every API used
// here is base-game; no DLC tracker, DLC Def, or paid-content instance is required.
using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private enum MemoryObservationWorkKind
        {
            Faction = 0,
            DirectedPair = 1,
            PawnFaction = 2
        }

        /// <summary>
        /// Transient adapter work may retain live references for only the bounded scheduler delay.
        /// The queue is never Scribed and is cleared on every new/load Game boundary.
        /// </summary>
        private sealed class MemoryObservationWorkItem
        {
            public string key = string.Empty;
            public MemoryObservationWorkKind kind;
            public Pawn owner;
            public Pawn subject;
            public Faction faction;
            public Faction previousFaction;
            public Faction currentFaction;
            public bool removedFaction;
            public bool forceSilentBaseline;
        }

        /// <summary>Running byte totals for one bounded scheduler slice.</summary>
        private sealed class MemoryObservationBudgetSession
        {
            public MemoryBudgetLimits limits;
            public MemoryPayloadBudgetTotals global;
            public int activeOwnerCount;
            public int nonArchiveEpochOwnerCount;
            public long epochCarrierSequence = -1;
            public string epochCarrierChain = string.Empty;
            public List<string> epochCarriers;
            public bool epochCarrierScanRefused;
            public readonly Dictionary<string, MemoryOwnerByteTotals> owners =
                new Dictionary<string, MemoryOwnerByteTotals>(StringComparer.Ordinal);
        }

        /// <summary>Bounded exact counts for the active directory and its active-plus-fence union.</summary>
        private struct MemoryObservationOwnerDirectoryCounts
        {
            public int active;
            public int nonArchiveEpoch;
        }

        // Non-null only while the main-thread observation slice is executing. Factual admissions
        // emitted by that slice reuse the same proven totals instead of rebuilding the colony graph
        // once per memory. It is cleared in finally before control returns to ordinary game work.
        private MemoryObservationBudgetSession memoryObservationActiveAdmissionBudget;
        private MemoryObservationBudgetSession memoryObservationRetainedBudget;
        private long memoryObservationRetainedBudgetIndexGeneration = -1;

        /// <summary>
        /// Detached owner/epoch enrollment. A new envelope and allocator high-water remain private
        /// until the first exact baseline or capacity marker passes the complete owner/global budget.
        /// </summary>
        private sealed class MemoryObservationOwnerEnrollment
        {
            public PawnDiaryRecord diary;
            public PawnKnowledgeState state;
            public string epochToken = string.Empty;
            public bool pendingNewEnvelope;
            public bool pendingEpoch;
            public long expectedAllocatorSequence;
            public string expectedAllocatorChain = string.Empty;
            public MemoryEpochAllocationPlan allocation;
        }

        /// <summary>Per-owner rotating offsets for bounded saved-edge resolution.</summary>
        private sealed class MemoryObservationSavedCandidateCursor
        {
            public int liveOffset;
            public int worldOffset;
            public int liveRemaining;
            public int worldRemaining;
            public bool cycleActive;
            public readonly SortedSet<string> requested =
                new SortedSet<string>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, Pawn> found =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
        }

        private readonly SortedDictionary<string, MemoryObservationWorkItem>
            memoryObservationDirty =
                new SortedDictionary<string, MemoryObservationWorkItem>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationSeenFactionIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationAttachedOwnerIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationOwnersNeedingSilentBaseline =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationAwarenessKeysNeedingSilentBaseline =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationFactionKeysNeedingSilentBaseline =
            new HashSet<string>(StringComparer.Ordinal);
        private bool memoryObservationForceAllSettlementsSilent;
        private readonly Dictionary<string, MemoryObservationSavedCandidateCursor>
            memoryObservationSavedCandidateCursors =
                new Dictionary<string, MemoryObservationSavedCandidateCursor>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationFullScanUnseenStartingOwnerIds =
            new HashSet<string>(StringComparer.Ordinal);
        private bool memoryObservationFullScanRequested;
        private bool memoryObservationFullScanSilent;
        private bool memoryObservationFinishFullAfterQueue;
        private bool memoryObservationBootstrapPending;
        private bool memoryObservationFullFactionsComplete;
        private bool memoryObservationFullFactionSourceCaptured;
        private int memoryObservationFullFactionCaptureIndex;
        private int memoryObservationFullFactionSourceIndex;
        private readonly List<Faction> memoryObservationFullFactionSource =
            new List<Faction>();
        private bool memoryObservationFullFactionSourceOverflow;
        private readonly SortedDictionary<string, Faction> memoryObservationFullFactionCandidates =
            new SortedDictionary<string, Faction>(StringComparer.Ordinal);
        private readonly HashSet<string> memoryObservationFullSavedFactionIds =
            new HashSet<string>(StringComparer.Ordinal);
        private string memoryObservationFullFactionAfterId = string.Empty;
        private bool memoryObservationFullOwnersCollected;
        private bool memoryObservationFullOwnerSourceCaptured;
        private int memoryObservationFullOwnerCaptureIndex;
        private int memoryObservationFullOwnerSourceIndex;
        private readonly List<Pawn> memoryObservationFullOwnerSource = new List<Pawn>();
        private readonly List<Pawn> memoryObservationFullLivePawnSource = new List<Pawn>();
        private readonly List<Pawn> memoryObservationFullWorldPawnSource = new List<Pawn>();
        private int memoryObservationFullLivePawnCaptureIndex;
        private int memoryObservationFullWorldPawnCaptureIndex;
        private bool memoryObservationFullPawnSourcesCaptured;
        private bool memoryObservationFullOwnerSourceOverflow;
        private readonly SortedDictionary<string, Pawn> memoryObservationFullOwners =
            new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Pawn> memoryObservationFullCurrentCandidates =
            new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
        private bool memoryObservationFullCurrentCandidatesReady;
        private string memoryObservationFullOwnerAfterId = string.Empty;
        private string memoryObservationFullCurrentOwnerId = string.Empty;
        private bool memoryObservationFullOwnerFactionDone;
        private string memoryObservationFullCandidateAfterId = string.Empty;
        private int lastMemoryObservationFullScanTick = -1;
        private bool memoryObservationMutatedThisTick;
        private int memoryObservationReconciliationIntervalTicks =
            KnowledgeObservationPolicySnapshot.DefaultReconciliationIntervalTicks;
        private long memoryObservationPublicationRevision = 1;
        private bool memoryObservationPublicationDirty;

        /// <summary>
        /// True only while the saved M6 snapshot is a complete Library publication source. The
        /// Library adapter uses this transient fence; Diary rendering remains independent.
        /// </summary>
        private bool MemoryObservationPublicationIsStable
        {
            get
            {
                return !memoryObservationPublicationDirty
                    && memoryObservationDirty.Count == 0
                    && !memoryObservationFullScanRequested
                    && !memoryObservationFinishFullAfterQueue;
            }
        }

        /// <summary>Starts a brand-new game with an empty transient queue and a silent first baseline.</summary>
        private void ResetMemoryObservationForNewGame()
        {
            memoryObservationDirty.Clear();
            memoryObservationSeenFactionIds.Clear();
            memoryObservationAttachedOwnerIds.Clear();
            memoryObservationOwnersNeedingSilentBaseline.Clear();
            memoryObservationAwarenessKeysNeedingSilentBaseline.Clear();
            memoryObservationFactionKeysNeedingSilentBaseline.Clear();
            memoryObservationForceAllSettlementsSilent = false;
            memoryObservationSavedCandidateCursors.Clear();
            memoryObservationFullScanUnseenStartingOwnerIds.Clear();
            memoryObservationFullScanRequested = false;
            memoryObservationFullScanSilent = false;
            memoryObservationFinishFullAfterQueue = false;
            memoryObservationBootstrapPending = true;
            ResetMemoryObservationFullCursor();
            lastMemoryObservationFullScanTick = -1;
            ResetMemoryObservationPublicationState();
            RequestMemoryObservationFullReconciliation(true);
        }

        /// <summary>
        /// Clears live references after load and forces a silent reattach baseline. Saved current truth
        /// remains intact; reconciliation cannot turn load/repair differences into retrospective facts.
        /// </summary>
        private void ResetMemoryObservationTransientState()
        {
            memoryObservationDirty.Clear();
            memoryObservationSeenFactionIds.Clear();
            memoryObservationAttachedOwnerIds.Clear();
            memoryObservationOwnersNeedingSilentBaseline.Clear();
            memoryObservationAwarenessKeysNeedingSilentBaseline.Clear();
            memoryObservationFactionKeysNeedingSilentBaseline.Clear();
            memoryObservationForceAllSettlementsSilent = false;
            memoryObservationSavedCandidateCursors.Clear();
            memoryObservationFullScanUnseenStartingOwnerIds.Clear();
            memoryObservationFullScanRequested = false;
            memoryObservationFullScanSilent = false;
            memoryObservationFinishFullAfterQueue = false;
            memoryObservationBootstrapPending = true;
            ResetMemoryObservationFullCursor();
            lastMemoryObservationFullScanTick = -1;
            ResetMemoryObservationPublicationState();
            NormalizeMemoryObservationSavedState();
            RequestMemoryObservationFullReconciliation(true);
        }

        private void ResetMemoryObservationPublicationState()
        {
            memoryObservationReconciliationIntervalTicks =
                KnowledgeObservationPolicySnapshot.DefaultReconciliationIntervalTicks;
            memoryObservationPublicationRevision = 1;
            memoryObservationPublicationDirty = false;
        }

        /// <summary>Marks both directed views of one known social seam; duplicate hooks coalesce.</summary>
        internal void MarkMemoryObservationPairDirty(Pawn first, Pawn second)
        {
            if (!GamePlaying || first == null || second == null || first == second) return;
            OfferMemoryObservationWork(DirectedPairWork(first, second, false));
            OfferMemoryObservationWork(DirectedPairWork(second, first, false));
        }

        /// <summary>Marks exact faction current truth after goodwill/relation/leader mutation.</summary>
        internal void MarkMemoryObservationFactionDirty(Faction first, Faction second = null)
        {
            if (!GamePlaying) return;
            OfferMemoryObservationWork(FactionWork(first, false, false));
            OfferMemoryObservationWork(FactionWork(second, false, false));
        }

        /// <summary>Captures the removal seam while the exact Faction instance is still available.</summary>
        internal void MarkMemoryObservationFactionRemoved(Faction faction)
        {
            if (!GamePlaying) return;
            OfferMemoryObservationWork(FactionWork(faction, true, false));
        }

        /// <summary>
        /// Marks a pawn's old/new exact faction instances and the personally connected owner row.
        /// Related owners are dirtied too; visibility and family knownness are checked later.
        /// </summary>
        internal void MarkMemoryObservationPawnFactionChanged(
            Pawn pawn,
            Faction previousFaction,
            Faction currentFaction)
        {
            if (!GamePlaying || pawn == null || previousFaction == currentFaction) return;
            if (!IsDiaryEligible(pawn)) DetachMemoryObservationOwner(pawn);
            OfferMemoryObservationWork(FactionWork(previousFaction, false, false));
            OfferMemoryObservationWork(FactionWork(currentFaction, false, false));
            OfferMemoryObservationWork(PawnFactionWork(
                pawn, previousFaction, currentFaction, false));

            // Do not walk an arbitrarily large implied-relation graph inside Harmony's SetFaction
            // postfix. The exact pawn/faction work lands immediately; the sliced reconciliation pass
            // updates every saved/visible counterpart without extending the patched game call.
            RequestMemoryObservationFullReconciliation(false);
        }

        /// <summary>
        /// Runs at the start of the existing social-reflection tick seam, plus bounded real-time
        /// updates while an open Library is blocked by paused observation. Work is elapsed-time driven,
        /// sliced by the shared XML capacity vector, and never loops across skipped game time.
        /// </summary>
        private void TickMemoryObservation(int now)
        {
            if (!GamePlaying || now < 0) return;
            memoryObservationMutatedThisTick = false;
            KnowledgeReconciliationSchedulePlan schedule =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    now,
                    lastMemoryObservationFullScanTick,
                    memoryObservationFullScanRequested,
                    memoryObservationFinishFullAfterQueue,
                    memoryObservationReconciliationIntervalTicks);
            if (schedule.consumeCompletedTick)
            {
                // Consume the future tick before requesting work. Otherwise every rolled-back game
                // tick restarts the cursor after it reaches its finish-after-queue state.
                lastMemoryObservationFullScanTick = -1;
            }
            if (schedule.forceSilentBaseline)
            {
                memoryObservationFullScanSilent = true;
                memoryObservationBootstrapPending = true;
            }
            if (schedule.requestFullReconciliation)
                RequestMemoryObservationFullReconciliation(schedule.forceSilentBaseline);

            if (memoryObservationDirty.Count == 0 && memoryObservationFullScanRequested)
            {
                FillMemoryObservationFullPage();
            }
            if (memoryObservationDirty.Count == 0)
            {
                MemoryObservationBudgetSession emptySession = null;
                if (memoryObservationFinishFullAfterQueue)
                {
                    emptySession = CreateMemoryObservationBudgetSession();
                    memoryObservationActiveAdmissionBudget = emptySession;
                    try
                    {
                        FinishMemoryObservationFullScan(now, emptySession);
                    }
                    finally
                    {
                        memoryObservationActiveAdmissionBudget = null;
                    }
                }
                CompleteMemoryObservationTick(emptySession);
                return;
            }

            KnowledgeObservationPolicySnapshot observationPolicy =
                DiaryKnowledgePolicy.MemoryObservationSnapshot();
            observationPolicy.maximumStateFacts = (int)ReadCapacityLong(
                "awarenessFacts", 4, 16);
            ReadCapacityPair(
                "factKeyValueUnits",
                48,
                128,
                192,
                512,
                out observationPolicy.maximumFactKeyCharacters,
                out observationPolicy.maximumFactValueCharacters);
            observationPolicy = observationPolicy.Normalized();
            memoryObservationReconciliationIntervalTicks =
                observationPolicy.reconciliationIntervalTicks;
            MemoryPolicySnapshot capturePolicy = MemoryEffectivePolicyProvider.Current;
            KnowledgeOpinionBandThresholds opinionBands = SnapshotMemoryOpinionBands();
            MemoryObservationBudgetSession budget = CreateMemoryObservationBudgetSession();
            int workCap = (int)ReadCapacityLong("sliceWorkItems", 30, 240);
            memoryObservationActiveAdmissionBudget = budget;
            try
            {
                for (int processed = 0;
                    processed < workCap && memoryObservationDirty.Count > 0;
                    processed++)
                {
                    KeyValuePair<string, MemoryObservationWorkItem> first =
                        FirstMemoryObservationWork();
                    memoryObservationDirty.Remove(first.Key);
                    try
                    {
                        ProcessMemoryObservationWork(
                            first.Value,
                            now,
                            capturePolicy,
                            opinionBands,
                            observationPolicy,
                            budget);
                    }
                    catch (Exception)
                    {
                        // Shadow observation must never break vanilla relation/faction gameplay. One
                        // malformed modded object costs only its exact work item. Keep both the key and
                        // exception body out of diagnostics: either may contain unbounded modded text.
                        Log.WarningOnce(
                            "[Pawn Diary] Shadow memory observation skipped malformed current-state "
                            + "data; further items share this bounded diagnostic.",
                            "PawnDiary.MemoryObservation.MalformedCurrentState".GetHashCode());
                    }
                }

                if (memoryObservationDirty.Count == 0 && memoryObservationFinishFullAfterQueue)
                {
                    FinishMemoryObservationFullScan(now, budget);
                }
            }
            finally
            {
                memoryObservationActiveAdmissionBudget = null;
            }
            CompleteMemoryObservationTick(budget);
        }

        private void CompleteMemoryObservationTick(MemoryObservationBudgetSession budget)
        {
            if (memoryObservationMutatedThisTick)
            {
                // Every observation commit first advances this slice's exact detached budget.
                // Publish those already-proven totals instead of walking the whole colony a second
                // time. A malformed/incomplete session retains the full rebuild as recovery only.
                if (!TryPublishMemoryObservationBudgetIndexes(budget))
                    RebuildMemorySizeIndexes();
                memoryObservationPublicationDirty = true;
            }
            if (!KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    memoryObservationPublicationDirty,
                    memoryObservationDirty.Count > 0,
                    memoryObservationFullScanRequested,
                    memoryObservationFinishFullAfterQueue)) return;

            memoryObservationPublicationRevision = AdvanceMemoryObservationRevision(
                memoryObservationPublicationRevision);
            memoryObservationPublicationDirty = false;
        }

        private void ProcessMemoryObservationWork(
            MemoryObservationWorkItem work,
            int now,
            MemoryPolicySnapshot capturePolicy,
            KnowledgeOpinionBandThresholds bands,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryObservationBudgetSession budget)
        {
            if (work == null) return;
            Pawn observationOwner = work.kind == MemoryObservationWorkKind.Faction
                ? null
                : work.owner;
            bool ownerReattachmentSilent = observationOwner != null
                && MemoryObservationOwnerNeedsSilentBaseline(observationOwner);
            bool silent = memoryObservationBootstrapPending
                || memoryObservationFullScanSilent
                || work.forceSilentBaseline
                || ownerReattachmentSilent;
            if (work.kind == MemoryObservationWorkKind.Faction)
            {
                ObserveGlobalFaction(
                    work.faction, work.removedFaction, now, silent, capturePolicy, budget);
            }
            else if (work.kind == MemoryObservationWorkKind.PawnFaction)
            {
                ObservePawnFactionConnection(work, now, silent, capturePolicy,
                    observationPolicy, budget);
            }
            else
            {
                ObserveDirectedSocialPair(work.owner, work.subject, now, silent,
                    capturePolicy, bands, observationPolicy, budget);
            }
        }

        /// <summary>
        /// Returns whether this owner's whole first observation pass must be baseline-only. Attachment
        /// is transient: load starts empty, leaving eligibility removes the id, and the next exact
        /// work item schedules a bounded reconciliation so every visible edge shares the silent pass.
        /// </summary>
        private bool MemoryObservationOwnerNeedsSilentBaseline(Pawn owner)
        {
            string ownerId = SafePawnId(owner);
            bool eligible = ownerId.Length > 0 && IsDiaryEligible(owner);
            bool attached = ownerId.Length > 0
                && memoryObservationAttachedOwnerIds.Contains(ownerId);
            if (!eligible)
            {
                DetachMemoryObservationOwner(owner);
                return false;
            }
            if (!KnowledgeRelationPolicy.OwnerAttachmentNeedsSilentBaseline(
                    attached, eligible))
            {
                return memoryObservationOwnersNeedingSilentBaseline.Contains(ownerId);
            }

            int cap = (int)ReadCapacityLong("dirtyObservationKeys", 1024, 4096);
            if (memoryObservationAttachedOwnerIds.Count >= cap)
            {
                // Failing to remember transient attachment is safe: this owner remains baseline-only
                // instead of ever inferring a transition across an unobserved eligibility interval.
                RequestMemoryObservationFullReconciliation(false);
                return true;
            }
            memoryObservationAttachedOwnerIds.Add(ownerId);
            memoryObservationOwnersNeedingSilentBaseline.Add(ownerId);
            RequestMemoryObservationFullReconciliation(false);
            return true;
        }

        /// <summary>Forgets one transient owner attachment without changing any saved current truth.</summary>
        private void DetachMemoryObservationOwner(Pawn owner)
        {
            string ownerId = SafePawnId(owner);
            if (ownerId.Length == 0) return;
            memoryObservationAttachedOwnerIds.Remove(ownerId);
            memoryObservationOwnersNeedingSilentBaseline.Remove(ownerId);
            memoryObservationFullScanUnseenStartingOwnerIds.Remove(ownerId);
            memoryObservationSavedCandidateCursors.Remove(ownerId);
        }

        private void ObserveDirectedSocialPair(
            Pawn owner,
            Pawn subject,
            int now,
            bool forceSilent,
            MemoryPolicySnapshot capturePolicy,
            KnowledgeOpinionBandThresholds bands,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryObservationBudgetSession budget)
        {
            if (owner == null || subject == null || owner == subject) return;
            string ownerId = SafePawnId(owner);
            string subjectId = SafePawnId(subject);
            if (ownerId.Length == 0 || subjectId.Length == 0) return;
            KnowledgeRelationVisibilityInput visibility =
                MemoryRelationVisibility(owner, subject);
            if (!KnowledgeRelationPolicy.IsKnownVisibleRelation(visibility))
            {
                RemoveHiddenMemoryObservation(ownerId, subjectId, budget);
                return;
            }

            int opinion;
            if (!TryReadOpinion(owner, subject, out opinion)) return;
            int inboundOpinion;
            if (!TryReadOpinion(subject, owner, out inboundOpinion)) return;
            List<string> outbound = DirectRelationDefNames(
                owner, subject, observationPolicy.maximumFactValueCharacters);
            List<string> inbound = DirectRelationDefNames(
                subject, owner, observationPolicy.maximumFactValueCharacters);
            PawnKnowledgeState existingState = FindCurrentMemoryEnvelope(ownerId);
            visibility.previouslyKnown = FindPlainAwareness(
                    existingState,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    subjectId) != null
                || FindPlainAwareness(
                    existingState,
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.StreamRelativeState,
                    subjectId) != null;
            visibility.hasKnownRelation = outbound.Count > 0 || inbound.Count > 0;
            visibility.candidateIsHumanlike = subject.RaceProps?.Humanlike == true;
            visibility.sharesSocialContext = SharesMemorySocialContext(owner, subject);
            visibility.ownerOpinionOfCandidate = opinion;
            visibility.candidateOpinionOfOwner = inboundOpinion;
            if (!KnowledgeRelationPolicy.IsKnownSocialEntry(visibility)) return;

            MemoryObservationOwnerEnrollment enrollment =
                PrepareMemoryObservationOwner(owner, budget);
            PawnKnowledgeState state = enrollment?.state;
            if (state == null) return;
            string snapshotId;
            if (!KnowledgeRelationPolicy.TryCreateAwarenessId(
                    ownerId,
                    enrollment.epochToken,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.SubjectPawn,
                    subjectId,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    out snapshotId)) return;
            SavedMemoryAwarenessSnapshot previousSaved =
                FindAwarenessSnapshot(state, snapshotId);
            KnowledgeAwarenessState previousSocial = ToPlainAwareness(previousSaved);
            string pairKey;
            KnowledgeRelationPolicy.TryCreateDirectedPairKey(ownerId, subjectId, out pairKey);
            SavedMemoryCaptureEpisode priorEpisode = FindOpinionEpisode(state, pairKey);
            long generation = capturePolicy.CaptureGeneration(MemoryCategoryBits.Relationships);
            string socialOccurrenceId = MemoryObservationOccurrenceId(
                "social_observation", now,
                previousSocial == null ? 1 : NextObservationSequence(previousSocial.snapshotRevision),
                "social", ownerId, subjectId, null);
            KnowledgeOpinionPlan plan = KnowledgeRelationPolicy.PlanDirectedOpinion(
                previousSocial,
                ToPlainOpinionEpisode(priorEpisode),
                new KnowledgeOpinionObservation
                {
                    ownerPawnId = ownerId,
                    ownerEpochToken = enrollment.epochToken,
                    subjectPawnId = subjectId,
                    opinion = opinion,
                    outboundRelationDefNames = outbound,
                    inboundRelationDefNames = inbound,
                    captureInvalidationGeneration = generation,
                    observedTick = now,
                    sourceOccurrenceId = socialOccurrenceId,
                    captureAllowed = capturePolicy.AllowsCapture(
                        MemoryCategoryBits.Relationships),
                    forceSilentBaseline = forceSilent
                        || MemoryObservationSettlementNeedsSilent(
                            memoryObservationAwarenessKeysNeedingSilentBaseline,
                            snapshotId)
                },
                bands,
                observationPolicy);
            if (plan.valid && plan.savedMutationRequired)
            {
                bool settled = ApplyMemoryAwarenessPlan(
                    enrollment, plan.replacement, plan.openEpisode, pairKey, budget);
                RecordMemoryObservationSettlementResult(
                    memoryObservationAwarenessKeysNeedingSilentBaseline,
                    snapshotId, settled);
                bool factualAdmitted = false;
                if (settled && plan.qualifiedForFutureCapture)
                {
                    factualAdmitted |= CaptureOpinionEpisodeMemory(
                        owner, subject, previousSocial, priorEpisode, plan,
                        socialOccurrenceId, opinion, now);
                }
                if (settled && plan.formalRelationChanged && !plan.silentBaseline
                    && !AuthoritativePageOwnsSocialTransition(
                        previousSocial, outbound, inbound))
                {
                    factualAdmitted |= CaptureFormalRelationMemory(
                        owner, subject, outbound, inbound, socialOccurrenceId, now);
                }
                if (factualAdmitted) PublishMemoryObservationBudgetTotals(budget);
            }

            bool family = HasVisibleFamilyRelation(owner, subject, observationPolicy);
            if (!family)
            {
                RemoveRelativeMemoryObservation(ownerId, subjectId, budget);
                return;
            }

            string relationSet;
            List<string> union = new List<string>(outbound.Count + inbound.Count);
            union.AddRange(outbound);
            union.AddRange(inbound);
            if (!KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                    union, observationPolicy.maximumFactValueCharacters, out relationSet))
            {
                relationSet = new string('x', observationPolicy.maximumFactValueCharacters + 1);
            }
            string factionSubject = FactionSubjectIdFor(subject.Faction);
            KnowledgeAwarenessState previousRelative = FindPlainAwareness(state,
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.StreamRelativeState,
                    subjectId);
            string relativeSnapshotId;
            KnowledgeRelationPolicy.TryCreateAwarenessId(
                ownerId, enrollment.epochToken,
                KnowledgeObservationTokens.ScopeRelative,
                KnowledgeObservationTokens.SubjectPawn,
                subjectId,
                KnowledgeObservationTokens.StreamRelativeState,
                out relativeSnapshotId);
            string relativeOccurrenceId = MemoryObservationOccurrenceId(
                "relative_observation", now,
                previousRelative == null ? 1
                    : NextObservationSequence(previousRelative.snapshotRevision),
                "relative", ownerId, subjectId, null);
            KnowledgeAwarenessPlan relativePlan = KnowledgeRelationPolicy.PlanCurrentTruth(
                previousRelative,
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = ownerId,
                    ownerEpochToken = enrollment.epochToken,
                    scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                    subjectKind = KnowledgeObservationTokens.SubjectPawn,
                    subjectId = subjectId,
                    factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                    captureInvalidationGeneration = capturePolicy.CaptureGeneration(
                        MemoryCategoryBits.Family),
                    knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                    stateFacts = new List<KnowledgeStateFact>
                    {
                        NewObservationFact(KnowledgeObservationTokens.FactLifeState,
                            subject.Dead
                                ? KnowledgeObservationTokens.LifeDead
                                : KnowledgeObservationTokens.LifeAlive),
                        NewObservationFact(KnowledgeObservationTokens.FactLocationState,
                            MemoryRelativeLocation(owner, subject)),
                        NewObservationFact(KnowledgeObservationTokens.FactFactionSubject,
                            factionSubject.Length == 0 ? "none" : factionSubject),
                        NewObservationFact(KnowledgeObservationTokens.FactRelationDefs, relationSet)
                    },
                    observedTick = now,
                    sourceOccurrenceId = relativeOccurrenceId,
                    captureAllowed = capturePolicy.AllowsCapture(MemoryCategoryBits.Family),
                    forceSilentBaseline = forceSilent
                        || MemoryObservationSettlementNeedsSilent(
                            memoryObservationAwarenessKeysNeedingSilentBaseline,
                            relativeSnapshotId)
                },
                observationPolicy);
            if (relativePlan.valid)
            {
                bool relativeSettled = !relativePlan.savedMutationRequired;
                if (relativePlan.savedMutationRequired)
                {
                    relativeSettled = ApplyMemoryAwarenessPlan(
                        enrollment, relativePlan.replacement, null, null, budget);
                    RecordMemoryObservationSettlementResult(
                        memoryObservationAwarenessKeysNeedingSilentBaseline,
                        relativePlan.replacement?.snapshotId, relativeSettled);
                }
                if (relativeSettled && relativePlan.savedMutationRequired
                    && relativePlan.authoritativeStateChanged && !relativePlan.silentBaseline
                    && !AuthoritativePageOwnsRelativeTransition(
                        previousRelative, relativePlan.replacement, union))
                {
                    bool factualAdmitted = CaptureRelativeStateMemory(
                        owner, subject, relativePlan.replacement,
                        relativeOccurrenceId, now);
                    if (factualAdmitted) PublishMemoryObservationBudgetTotals(budget);
                }
                PruneOrphanedFamilyFactionConnections(state, budget);
            }
            if (subject.Faction != null && !subject.Faction.IsPlayer)
            {
                ObserveOwnerFactionConnection(owner, subject.Faction,
                    KnowledgeObservationTokens.ConnectionFamily, now,
                    forceSilent, capturePolicy, observationPolicy, budget);
            }
        }

        private void ObservePawnFactionConnection(
            MemoryObservationWorkItem work,
            int now,
            bool forceSilent,
            MemoryPolicySnapshot capturePolicy,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryObservationBudgetSession budget)
        {
            Pawn pawn = work?.owner;
            if (pawn == null || !IsDiaryEligible(pawn)) return;
            if (work.previousFaction != null && !work.previousFaction.IsPlayer)
            {
                ObserveOwnerFactionConnection(pawn, work.previousFaction,
                    KnowledgeRelationPolicy.OwnerFactionConnectionKind(
                        pawn.Faction == work.previousFaction),
                    now, forceSilent, capturePolicy, observationPolicy, budget);
            }
            if (work.currentFaction != null && !work.currentFaction.IsPlayer)
            {
                ObserveOwnerFactionConnection(pawn, work.currentFaction,
                    KnowledgeRelationPolicy.OwnerFactionConnectionKind(
                        pawn.Faction == work.currentFaction),
                    now, forceSilent, capturePolicy, observationPolicy, budget);
            }
        }

        private void ObserveOwnerFactionConnection(
            Pawn owner,
            Faction faction,
            string connectionKind,
            int now,
            bool forceSilent,
            MemoryPolicySnapshot capturePolicy,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryObservationBudgetSession budget)
        {
            if (owner == null || faction == null || faction.Hidden) return;
            MemoryObservationOwnerEnrollment enrollment =
                PrepareMemoryObservationOwner(owner, budget);
            PawnKnowledgeState state = enrollment?.state;
            if (state == null) return;
            string factionSubject = FactionSubjectIdFor(faction);
            if (factionSubject.Length == 0) return;
            KnowledgeAwarenessState previous = FindPlainAwareness(state,
                KnowledgeObservationTokens.ScopeFaction,
                KnowledgeObservationTokens.StreamFactionConnection,
                factionSubject);
            string ownerFactionSnapshotId;
            KnowledgeRelationPolicy.TryCreateAwarenessId(
                SafePawnId(owner), enrollment.epochToken,
                KnowledgeObservationTokens.ScopeFaction,
                KnowledgeObservationTokens.SubjectFaction,
                factionSubject,
                KnowledgeObservationTokens.StreamFactionConnection,
                out ownerFactionSnapshotId);
            string effectiveConnection = KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                FactValue(previous?.stateFacts,
                    KnowledgeObservationTokens.FactConnectionKind),
                connectionKind);
            KnowledgeAwarenessPlan plan = KnowledgeRelationPolicy.PlanCurrentTruth(
                previous,
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = SafePawnId(owner),
                    ownerEpochToken = enrollment.epochToken,
                    scopeKindToken = KnowledgeObservationTokens.ScopeFaction,
                    subjectKind = KnowledgeObservationTokens.SubjectFaction,
                    subjectId = factionSubject,
                    factStreamToken = KnowledgeObservationTokens.StreamFactionConnection,
                    captureInvalidationGeneration = capturePolicy.CaptureGeneration(
                        MemoryCategoryBits.Factions),
                    knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                    // Deliberately no goodwill/relation fact here. Exact diplomacy lives once in
                    // globalFactionSnapshots and owner rows retain awareness only.
                    stateFacts = new List<KnowledgeStateFact>
                    {
                        NewObservationFact(
                            KnowledgeObservationTokens.FactConnectionKind,
                            effectiveConnection)
                    },
                    observedTick = now,
                    captureAllowed = capturePolicy.AllowsCapture(MemoryCategoryBits.Factions),
                    forceSilentBaseline = forceSilent
                        || MemoryObservationSettlementNeedsSilent(
                            memoryObservationAwarenessKeysNeedingSilentBaseline,
                            ownerFactionSnapshotId)
                },
                observationPolicy);
            if (plan.valid && plan.savedMutationRequired)
            {
                bool settled = ApplyMemoryAwarenessPlan(
                    enrollment, plan.replacement, null, null, budget);
                RecordMemoryObservationSettlementResult(
                    memoryObservationAwarenessKeysNeedingSilentBaseline,
                    plan.replacement?.snapshotId, settled);
            }
        }

        private void ObserveGlobalFaction(
            Faction faction,
            bool removed,
            int now,
            bool forceSilent,
            MemoryPolicySnapshot capturePolicy,
            MemoryObservationBudgetSession budget)
        {
            if (faction == null) return;
            string instanceId = SafeFactionId(faction);
            if (instanceId.Length == 0) return;
            if (faction.Hidden)
            {
                RemoveGlobalFactionSnapshots(instanceId, budget);
                return;
            }

            SavedGlobalFactionSnapshot previous = LatestFactionSnapshot(instanceId, false);
            bool allocatedGeneration = false;
            long allocatorGeneration;
            if (previous == null)
            {
                long generation;
                List<long> live = new List<long>();
                for (int i = 0; i < globalFactionSnapshots.Count; i++)
                    if (globalFactionSnapshots[i] != null)
                        live.Add(globalFactionSnapshots[i].allocatorGeneration);
                if (!KnowledgeRelationPolicy.TryAllocateFactionGeneration(
                        globalFactionSnapshotAllocatorGeneration, live, out generation)) return;
                allocatorGeneration = generation;
                allocatedGeneration = true;
            }
            else allocatorGeneration = previous.allocatorGeneration;
            Faction player = Faction.OfPlayer;
            int goodwill = faction == player ? 100 : 0;
            string relationKind = faction == player
                ? FactionRelationKind.Ally.ToString()
                : FactionRelationKind.Neutral.ToString();
            if (player != null && faction != player)
            {
                try
                {
                    goodwill = Math.Max(-100, Math.Min(100, faction.GoodwillWith(player)));
                    relationKind = faction.RelationKindWith(player).ToString();
                }
                catch
                {
                    goodwill = 0;
                    relationKind = FactionRelationKind.Neutral.ToString();
                }
            }

            int labelCap = (int)ReadCapacityLong("frozenDisplayLabelUnits", 80, 320);
            string label = DiaryLineCleaner.CleanLine(faction.Name);
            if (label.Length > labelCap) label = TextTruncation.SafePrefix(label, labelCap);
            KnowledgeFactionState previousFaction = ToPlainFaction(previous);
            string factionSettlementKey;
            MemoryIdentityCodec.TryCreateFactionSubjectId(
                instanceId, allocatorGeneration, out factionSettlementKey);
            KnowledgeFactionPlan plan = KnowledgeRelationPolicy.PlanFactionSnapshot(
                previousFaction,
                new KnowledgeFactionObservation
                {
                    factionInstanceId = instanceId,
                    allocatorGeneration = allocatorGeneration,
                    factionDefName = faction.def?.defName ?? string.Empty,
                    frozenDisplayLabel = label,
                    goodwill = goodwill,
                    relationKindToken = relationKind,
                    leaderPawnId = SafePawnId(faction.leader),
                    defeated = faction.defeated,
                    removed = removed,
                    observedTick = now,
                    forceSilentBaseline = forceSilent
                        || MemoryObservationSettlementNeedsSilent(
                            memoryObservationFactionKeysNeedingSilentBaseline,
                            factionSettlementKey),
                    maximumFrozenDisplayLabelCharacters = labelCap
                });
            bool factionSettled = plan.valid && plan.savedMutationRequired
                && ApplyGlobalFactionPlan(plan.replacement, budget);
            if (plan.valid && plan.savedMutationRequired)
            {
                RecordMemoryObservationSettlementResult(
                    memoryObservationFactionKeysNeedingSilentBaseline,
                    factionSettlementKey, factionSettled);
            }
            if (factionSettled && allocatedGeneration)
            {
                // Allocation and its first exact-key snapshot publish together. A cap refusal does
                // not consume a generation and cannot grow the high-water on every reconciliation.
                globalFactionSnapshotAllocatorGeneration = allocatorGeneration;
            }
            if (factionSettled && plan.authoritativeStateChanged && !plan.silentBaseline
                && capturePolicy != null
                && capturePolicy.AllowsCapture(MemoryCategoryBits.Factions))
            {
                string factionSubject;
                if (MemoryIdentityCodec.TryCreateFactionSubjectId(
                    instanceId, allocatorGeneration, out factionSubject))
                {
                    string occurrenceId = MemoryObservationOccurrenceId(
                        "faction_observation", now,
                        previousFaction == null ? 1
                            : NextObservationSequence(previousFaction.snapshotRevision),
                        "faction", null, null, factionSubject);
                    bool factualAdmitted = CaptureFactionStateMemories(
                        previousFaction, plan.replacement, factionSubject,
                        occurrenceId, now);
                    if (factualAdmitted) PublishMemoryObservationBudgetTotals(budget);
                }
            }
        }

        private void FinishMemoryObservationFullScan(
            int now,
            MemoryObservationBudgetSession budget)
        {
            List<SavedGlobalFactionSnapshot> missing = new List<SavedGlobalFactionSnapshot>();
            for (int i = 0; !memoryObservationFullFactionSourceOverflow
                && i < globalFactionSnapshots.Count; i++)
            {
                SavedGlobalFactionSnapshot row = globalFactionSnapshots[i];
                if (KnowledgeRelationPolicy.CanInferMissingFactionRemoval(ToPlainFaction(row))
                    && !memoryObservationSeenFactionIds.Contains(row.factionInstanceId))
                {
                    missing.Add(row);
                }
            }
            for (int i = 0; i < missing.Count; i++)
            {
                SavedGlobalFactionSnapshot row = missing[i];
                string factionSettlementKey;
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    row.factionInstanceId, row.allocatorGeneration,
                    out factionSettlementKey);
                KnowledgeFactionPlan plan = KnowledgeRelationPolicy.PlanFactionSnapshot(
                    ToPlainFaction(row),
                    new KnowledgeFactionObservation
                    {
                        factionInstanceId = row.factionInstanceId,
                        allocatorGeneration = row.allocatorGeneration,
                        factionDefName = row.factionDefName,
                        frozenDisplayLabel = row.frozenDisplayLabel,
                        goodwill = row.goodwill,
                        relationKindToken = string.IsNullOrWhiteSpace(row.relationKindToken)
                            ? FactionRelationKind.Neutral.ToString()
                            : row.relationKindToken,
                        leaderPawnId = row.leaderPawnId,
                        defeated = row.defeated,
                        removed = true,
                        observedTick = now,
                        forceSilentBaseline = memoryObservationFullScanSilent
                            || MemoryObservationSettlementNeedsSilent(
                                memoryObservationFactionKeysNeedingSilentBaseline,
                                factionSettlementKey),
                        maximumFrozenDisplayLabelCharacters = (int)ReadCapacityLong(
                            "frozenDisplayLabelUnits", 80, 320)
                    });
                if (plan.valid && plan.savedMutationRequired)
                {
                    bool settled = ApplyGlobalFactionPlan(plan.replacement, budget);
                    RecordMemoryObservationSettlementResult(
                        memoryObservationFactionKeysNeedingSilentBaseline,
                        factionSettlementKey, settled);
                }
            }

            foreach (string ownerId in memoryObservationFullScanUnseenStartingOwnerIds)
            {
                if (memoryObservationFullOwnerSourceOverflow) break;
                memoryObservationAttachedOwnerIds.Remove(ownerId);
                memoryObservationOwnersNeedingSilentBaseline.Remove(ownerId);
                memoryObservationSavedCandidateCursors.Remove(ownerId);
            }
            memoryObservationFullScanUnseenStartingOwnerIds.Clear();
            memoryObservationOwnersNeedingSilentBaseline.Clear();
            memoryObservationDirty.Clear();
            memoryObservationSeenFactionIds.Clear();
            memoryObservationFullScanRequested = false;
            memoryObservationFinishFullAfterQueue = false;
            memoryObservationFullScanSilent = false;
            memoryObservationBootstrapPending = false;
            ResetMemoryObservationFullCursor();
            lastMemoryObservationFullScanTick = now;
        }

        private void RequestMemoryObservationFullReconciliation(bool forceSilent)
        {
            if (!memoryObservationFullScanRequested)
            {
                ResetMemoryObservationFullCursor();
                memoryObservationSeenFactionIds.Clear();
                memoryObservationFullScanUnseenStartingOwnerIds.Clear();
                foreach (string ownerId in memoryObservationAttachedOwnerIds)
                    memoryObservationFullScanUnseenStartingOwnerIds.Add(ownerId);
            }
            memoryObservationFullScanRequested = true;
            memoryObservationFinishFullAfterQueue = false;
            memoryObservationFullScanSilent |= forceSilent;
            memoryObservationBootstrapPending |= forceSilent;
        }

        private void FillMemoryObservationFullPage()
        {
            if (!memoryObservationFullScanRequested || memoryObservationDirty.Count > 0) return;
            int queueCap = (int)ReadCapacityLong("dirtyObservationKeys", 1024, 4096);
            int scanCap = (int)ReadCapacityLong("sliceWorkItems", 30, 240);
            if (!memoryObservationFullFactionsComplete)
            {
                int factionCap = (int)ReadCapacityLong("factionSnapshots", 256, 1024);
                if (!memoryObservationFullFactionSourceCaptured)
                {
                    int copied;
                    bool complete;
                    memoryObservationFullFactionSourceOverflow =
                        !AdvanceBoundedMemoryObservationSourceCopy(
                            Find.FactionManager?.AllFactionsListForReading,
                            memoryObservationFullFactionSource,
                            ref memoryObservationFullFactionCaptureIndex,
                            checked(factionCap * 4),
                            scanCap,
                            out copied,
                            out complete);
                    memoryObservationFullFactionSourceCaptured = complete;
                    if (!complete) return;
                }
                int processed = 0;
                while (memoryObservationFullFactionSourceIndex
                        < memoryObservationFullFactionSource.Count
                    && processed < scanCap)
                {
                    OfferFullMemoryObservationFaction(
                        memoryObservationFullFactionSource[
                            memoryObservationFullFactionSourceIndex++], factionCap);
                    processed++;
                }
                if (memoryObservationFullFactionSourceIndex
                    < memoryObservationFullFactionSource.Count) return;

                foreach (KeyValuePair<string, Faction> pair
                    in memoryObservationFullFactionCandidates)
                {
                    string factionId = pair.Key;
                    if (string.CompareOrdinal(
                            factionId, memoryObservationFullFactionAfterId) <= 0) continue;
                    memoryObservationFullFactionAfterId = factionId;
                    memoryObservationSeenFactionIds.Add(factionId);
                    if (QueueFullMemoryObservationWork(
                            FactionWork(pair.Value, false, memoryObservationFullScanSilent),
                            queueCap)) return;
                }
                memoryObservationFullFactionsComplete = true;
                memoryObservationFullFactionAfterId = string.Empty;
                memoryObservationFullFactionCandidates.Clear();
            }

            if (!memoryObservationFullOwnersCollected)
            {
                int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
                if (!memoryObservationFullOwnerSourceCaptured)
                {
                    int copied;
                    bool complete;
                    memoryObservationFullOwnerSourceOverflow =
                        !AdvanceBoundedMemoryObservationSourceCopy(
                            PawnsFinder.AllMaps_FreeColonists,
                            memoryObservationFullOwnerSource,
                            ref memoryObservationFullOwnerCaptureIndex,
                            checked(ownerCap * 4),
                            scanCap,
                            out copied,
                            out complete);
                    memoryObservationFullOwnerSourceCaptured = complete;
                    if (!complete) return;
                }
                int processed = 0;
                while (memoryObservationFullOwnerSourceIndex
                        < memoryObservationFullOwnerSource.Count
                    && processed < scanCap)
                {
                    Pawn candidate = memoryObservationFullOwnerSource[
                        memoryObservationFullOwnerSourceIndex++];
                    if (candidate != null && IsDiaryEligible(candidate))
                        OfferBoundedMemoryObservationCandidate(
                            memoryObservationFullOwners, null, candidate, ownerCap, null);
                    processed++;
                }
                if (memoryObservationFullOwnerSourceIndex
                    < memoryObservationFullOwnerSource.Count) return;
                memoryObservationFullOwnersCollected = true;
            }

            int candidateCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            if (!memoryObservationFullPawnSourcesCaptured)
            {
                int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
                int pawnSourceCap = checked(ownerCap * 256);
                int copiedLive;
                bool liveComplete;
                bool liveValid = AdvanceBoundedMemoryObservationSourceCopy(
                    PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive,
                    memoryObservationFullLivePawnSource,
                    ref memoryObservationFullLivePawnCaptureIndex,
                    pawnSourceCap,
                    scanCap,
                    out copiedLive,
                    out liveComplete);
                if (!liveValid)
                {
                    memoryObservationFullLivePawnSource.Clear();
                    memoryObservationFullWorldPawnSource.Clear();
                    memoryObservationFullPawnSourcesCaptured = true;
                }
                else if (!liveComplete)
                {
                    return;
                }
                else
                {
                    int copiedWorld;
                    bool worldComplete;
                    bool worldValid = AdvanceBoundedMemoryObservationSourceCopy(
                        Find.WorldPawns?.AllPawnsAliveOrDead,
                        memoryObservationFullWorldPawnSource,
                        ref memoryObservationFullWorldPawnCaptureIndex,
                        pawnSourceCap,
                        Math.Max(1, scanCap - copiedLive),
                        out copiedWorld,
                        out worldComplete);
                    if (!worldValid)
                    {
                        memoryObservationFullLivePawnSource.Clear();
                        memoryObservationFullWorldPawnSource.Clear();
                        memoryObservationFullPawnSourcesCaptured = true;
                    }
                    else if (!worldComplete)
                    {
                        return;
                    }
                    else
                    {
                        memoryObservationFullPawnSourcesCaptured = true;
                    }
                }
            }
            int ownersProcessed = 0;
            while (true)
            {
                if (ownersProcessed >= scanCap) return;
                Pawn owner = FindFullScanOwner(memoryObservationFullOwners);
                if (owner == null) break;
                string ownerId = SafePawnId(owner);
                memoryObservationFullScanUnseenStartingOwnerIds.Remove(ownerId);
                MemoryObservationOwnerNeedsSilentBaseline(owner);
                if (!memoryObservationFullOwnerFactionDone)
                {
                    memoryObservationFullOwnerFactionDone = true;
                    if (owner.Faction != null && !owner.Faction.IsPlayer
                        && QueueFullMemoryObservationWork(
                            PawnFactionWork(owner, null, owner.Faction,
                                memoryObservationFullScanSilent),
                            queueCap)) return;
                }

                if (!memoryObservationFullCurrentCandidatesReady)
                {
                    bool candidatesComplete;
                    SortedDictionary<string, Pawn> candidates =
                        FullMemoryObservationCandidates(
                            owner, candidateCap,
                            memoryObservationFullLivePawnSource,
                            memoryObservationFullWorldPawnSource,
                            out candidatesComplete);
                    if (!candidatesComplete) return;
                    memoryObservationFullCurrentCandidates.Clear();
                    foreach (KeyValuePair<string, Pawn> candidate in candidates)
                        memoryObservationFullCurrentCandidates.Add(
                            candidate.Key, candidate.Value);
                    memoryObservationFullCurrentCandidatesReady = true;
                }
                foreach (KeyValuePair<string, Pawn> candidate
                    in memoryObservationFullCurrentCandidates)
                {
                    if (string.CompareOrdinal(
                            candidate.Key, memoryObservationFullCandidateAfterId) <= 0) continue;
                    memoryObservationFullCandidateAfterId = candidate.Key;
                    if (QueueFullMemoryObservationWork(
                            DirectedPairWork(owner, candidate.Value,
                                memoryObservationFullScanSilent),
                            queueCap)) return;
                }

                memoryObservationFullOwnerAfterId = ownerId;
                memoryObservationFullCurrentOwnerId = string.Empty;
                memoryObservationFullOwnerFactionDone = false;
                memoryObservationFullCandidateAfterId = string.Empty;
                memoryObservationFullCurrentCandidates.Clear();
                memoryObservationFullCurrentCandidatesReady = false;
                ownersProcessed++;
            }

            memoryObservationFullScanRequested = false;
            memoryObservationFinishFullAfterQueue = true;
        }

        private Pawn FindFullScanOwner(SortedDictionary<string, Pawn> owners)
        {
            if (!string.IsNullOrEmpty(memoryObservationFullCurrentOwnerId))
            {
                Pawn current;
                if (owners != null
                    && owners.TryGetValue(memoryObservationFullCurrentOwnerId, out current))
                    return current;

                // The current owner left the eligible set between pages. Its exact dirty hooks or
                // the next periodic pass will settle it; continue after its stable id without rewind.
                memoryObservationFullOwnerAfterId = memoryObservationFullCurrentOwnerId;
                memoryObservationFullCurrentOwnerId = string.Empty;
                memoryObservationFullOwnerFactionDone = false;
                memoryObservationFullCandidateAfterId = string.Empty;
            }

            if (owners == null) return null;
            foreach (KeyValuePair<string, Pawn> pair in owners)
            {
                string ownerId = pair.Key;
                if (string.CompareOrdinal(ownerId, memoryObservationFullOwnerAfterId) <= 0) continue;
                memoryObservationFullCurrentOwnerId = ownerId;
                return pair.Value;
            }
            return null;
        }

        /// <summary>
        /// Retains every already-saved exact faction instance before filling remaining global slots
        /// with the lexical minimum of new instances. The temporary dictionary never exceeds the
        /// saved faction-row cap while the source list is consumed in tick-sized slices.
        /// </summary>
        private void OfferFullMemoryObservationFaction(Faction faction, int cap)
        {
            if (faction == null || cap <= 0) return;
            string id = SafeFactionId(faction);
            if (id.Length == 0 || memoryObservationFullFactionCandidates.ContainsKey(id)) return;
            bool saved = false;
            saved = memoryObservationFullSavedFactionIds.Contains(id);
            if (memoryObservationFullFactionCandidates.Count < cap)
            {
                memoryObservationFullFactionCandidates.Add(id, faction);
                return;
            }

            string removable = string.Empty;
            foreach (KeyValuePair<string, Faction> pair in memoryObservationFullFactionCandidates)
            {
                if (!memoryObservationFullSavedFactionIds.Contains(pair.Key)) removable = pair.Key;
            }
            if (removable.Length == 0) return;
            if (!saved && string.CompareOrdinal(id, removable) >= 0) return;
            memoryObservationFullFactionCandidates.Remove(removable);
            memoryObservationFullFactionCandidates.Add(id, faction);
        }

        private void ResetMemoryObservationFullCursor()
        {
            memoryObservationFullFactionsComplete = false;
            memoryObservationFullFactionSourceCaptured = false;
            memoryObservationFullFactionCaptureIndex = 0;
            memoryObservationFullFactionSourceIndex = 0;
            memoryObservationFullFactionSource.Clear();
            memoryObservationFullFactionSourceOverflow = false;
            memoryObservationFullFactionCandidates.Clear();
            memoryObservationFullSavedFactionIds.Clear();
            int factionCap = (int)ReadCapacityLong("factionSnapshots", 256, 1024);
            for (int i = 0; i < globalFactionSnapshots.Count
                && memoryObservationFullSavedFactionIds.Count < factionCap; i++)
            {
                string id = globalFactionSnapshots[i]?.factionInstanceId ?? string.Empty;
                if (id.Length > 0) memoryObservationFullSavedFactionIds.Add(id);
            }
            memoryObservationFullFactionAfterId = string.Empty;
            memoryObservationFullOwnersCollected = false;
            memoryObservationFullOwnerSourceCaptured = false;
            memoryObservationFullOwnerCaptureIndex = 0;
            memoryObservationFullOwnerSourceIndex = 0;
            memoryObservationFullOwnerSource.Clear();
            memoryObservationFullLivePawnSource.Clear();
            memoryObservationFullWorldPawnSource.Clear();
            memoryObservationFullLivePawnCaptureIndex = 0;
            memoryObservationFullWorldPawnCaptureIndex = 0;
            memoryObservationFullPawnSourcesCaptured = false;
            memoryObservationFullOwnerSourceOverflow = false;
            memoryObservationFullOwners.Clear();
            memoryObservationFullCurrentCandidates.Clear();
            memoryObservationFullCurrentCandidatesReady = false;
            memoryObservationFullOwnerAfterId = string.Empty;
            memoryObservationFullCurrentOwnerId = string.Empty;
            memoryObservationFullOwnerFactionDone = false;
            memoryObservationFullCandidateAfterId = string.Empty;
            foreach (MemoryObservationSavedCandidateCursor cursor
                in memoryObservationSavedCandidateCursors.Values)
            {
                cursor.cycleActive = false;
                cursor.requested.Clear();
                cursor.found.Clear();
            }
        }

        /// <summary>
        /// Copies one live source once for a reconciliation pass. The persistent slice cursor then
        /// indexes a stable list, so removals or reorderings between ticks cannot skip an identity.
        /// </summary>
        private static bool AdvanceBoundedMemoryObservationSourceCopy<T>(
            List<T> source,
            List<T> destination,
            ref int nextIndex,
            int maximumRows,
            int maximumWorkItems,
            out int copiedRows,
            out bool complete)
        {
            copiedRows = 0;
            MemoryObservationSourceCopyPlan plan = MemoryObservationSourceCopyPolicy.Plan(
                source?.Count ?? 0,
                destination?.Count ?? -1,
                nextIndex,
                maximumRows,
                maximumWorkItems);
            complete = plan.complete;
            if (!plan.valid || plan.overflow)
            {
                destination?.Clear();
                nextIndex = 0;
                complete = true;
                return false;
            }
            for (int i = 0; i < plan.workItems; i++)
                destination.Add(source[plan.startIndex + i]);
            nextIndex = plan.nextIndex;
            copiedRows = plan.workItems;
            return true;
        }

        /// <summary>
        /// Takes a bounded prefix before candidate inspection calls back into modded pawn APIs. Those
        /// calls may reentrantly add or remove a pawn from RimWorld's live list; iterating this detached
        /// prefix prevents that routine churn from invalidating a List enumerator mid-pass.
        /// </summary>
        private static List<T> CopyBoundedMemoryObservationPrefix<T>(
            IEnumerable<T> source,
            int maximumRows)
        {
            int cap = Math.Max(0, maximumRows);
            IList<T> indexed = source as IList<T>;
            int count = indexed == null ? 0 : Math.Min(cap, indexed.Count);
            List<T> result = new List<T>(count);
            if (cap == 0 || source == null) return result;

            // RimWorld exposes the relation/map/caravan sources as Lists. Copying by index avoids
            // holding their version-checked enumerator across any later, potentially patched getter.
            if (indexed != null)
            {
                for (int i = 0; i < count; i++) result.Add(indexed[i]);
                return result;
            }

            foreach (T item in source)
            {
                if (result.Count >= cap) break;
                result.Add(item);
            }
            return result;
        }

        private SortedDictionary<string, Pawn> FullMemoryObservationCandidates(
            Pawn owner,
            int cap,
            List<Pawn> livePawnSource,
            List<Pawn> worldPawnSource,
            out bool complete)
        {
            complete = false;
            SortedDictionary<string, Pawn> result =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            // Existing exact edges own reconciliation priority. Otherwise a stable lexical prefix of
            // newly related pawns can permanently starve one saved high-ID subject at cap pressure.
            PawnKnowledgeState state = FindDiaryByPawnId(SafePawnId(owner))?.KnowledgeStateOrNull();
            SortedDictionary<string, Pawn> savedCandidates =
                ResolveSavedMemoryObservationCandidates(
                    state, cap, livePawnSource, worldPawnSource, out complete);
            if (!complete) return result;
            SortedDictionary<string, Pawn> relatedCandidates =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            if (savedCandidates.Count < cap && owner?.relations != null)
            {
                int inspected = 0;
                int inspectionCap = checked(Math.Max(cap, cap * 4));
                List<Pawn> relatedPawns = CopyBoundedMemoryObservationPrefix(
                    owner.relations.RelatedPawns, inspectionCap);
                foreach (Pawn related in relatedPawns)
                {
                    if (inspected++ >= inspectionCap) break;
                    OfferBoundedMemoryObservationCandidate(
                        relatedCandidates, owner, related, cap, savedCandidates);
                }
            }
            List<string> prioritized = KnowledgeRelationPolicy.PrioritizeObservationCandidateIds(
                savedCandidates.Keys, relatedCandidates.Keys, cap);
            for (int i = 0; i < prioritized.Count; i++)
            {
                Pawn candidate;
                if (!savedCandidates.TryGetValue(prioritized[i], out candidate))
                    relatedCandidates.TryGetValue(prioritized[i], out candidate);
                AddMemoryObservationCandidate(result, owner, candidate);
            }

            int remaining = cap - result.Count;
            SortedDictionary<string, Pawn> socialCandidates =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            int socialInspected = 0;
            int socialInspectionCap = checked(Math.Max(cap, cap * 4));
            if (remaining > 0)
            {
                List<Pawn> socialPawns = CopyBoundedMemoryObservationPrefix(
                    MemorySocialContextPawns(owner), socialInspectionCap);
                foreach (Pawn candidate in socialPawns)
                {
                    if (socialInspected++ >= socialInspectionCap) break;
                    if (IsCurrentMemorySocialCandidate(owner, candidate))
                        OfferBoundedMemoryObservationCandidate(
                            socialCandidates, owner, candidate, remaining, result);
                }
            }
            foreach (KeyValuePair<string, Pawn> pair in socialCandidates)
                AddMemoryObservationCandidate(result, owner, pair.Value);
            return result;
        }

        /// <summary>
        /// Resolves all bounded saved pawn ids with one pass over each live pawn source. Building a
        /// world-wide id map would be unbounded, while resolving each saved row separately repeats the
        /// full world scan; this keeps both work and temporary state proportional to the saved cap.
        /// </summary>
        private SortedDictionary<string, Pawn> ResolveSavedMemoryObservationCandidates(
            PawnKnowledgeState state,
            int cap,
            List<Pawn> livePawnSource,
            List<Pawn> worldPawnSource,
            out bool complete)
        {
            complete = false;
            SortedSet<string> requested = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; cap > 0 && state?.ownerAwarenessSnapshots != null
                && i < state.ownerAwarenessSnapshots.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = state.ownerAwarenessSnapshots[i];
                if (row == null || row.subjectKind != KnowledgeObservationTokens.SubjectPawn
                    || !KnowledgeRelationPolicy.IsKnownObservationStreamShape(
                        row.scopeKindToken, row.subjectKind, row.factStreamToken)
                    || !KnowledgeRelationPolicy.IsValidObservationSubject(
                        row.subjectKind, row.subjectId)) continue;
                OfferBoundedMemoryObservationId(requested, row.subjectId, cap);
            }

            SortedDictionary<string, Pawn> found =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            if (requested.Count == 0)
            {
                complete = true;
                return found;
            }
            string ownerId = state?.pawnId ?? string.Empty;
            MemoryObservationSavedCandidateCursor cursor;
            if (!memoryObservationSavedCandidateCursors.TryGetValue(ownerId, out cursor))
            {
                int cursorCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
                if (ownerId.Length > 0 && memoryObservationSavedCandidateCursors.Count < cursorCap)
                {
                    cursor = new MemoryObservationSavedCandidateCursor();
                    memoryObservationSavedCandidateCursors.Add(ownerId, cursor);
                }
                else
                {
                    // A malformed owner directory beyond its configured cap cannot retain another
                    // cursor. Skip saved-edge discovery for this owner instead of restarting forever.
                    complete = true;
                    return found;
                }
            }
            if (!cursor.cycleActive || !cursor.requested.SetEquals(requested))
            {
                cursor.cycleActive = true;
                cursor.requested.Clear();
                foreach (string id in requested) cursor.requested.Add(id);
                cursor.found.Clear();
                cursor.liveRemaining = livePawnSource?.Count ?? 0;
                cursor.worldRemaining = worldPawnSource?.Count ?? 0;
            }
            int scanCap = (int)ReadCapacityLong("sliceWorkItems", 30, 240);
            int remainingWork = Math.Max(1, scanCap);
            int inspected = IndexRequestedObservedPawns(
                livePawnSource, requested, cursor.found,
                Math.Min(remainingWork, cursor.liveRemaining), ref cursor.liveOffset);
            cursor.liveRemaining -= inspected;
            remainingWork -= inspected;
            if (remainingWork > 0 && cursor.found.Count < requested.Count)
            {
                inspected = IndexRequestedObservedPawns(
                    worldPawnSource, requested, cursor.found,
                    Math.Min(remainingWork, cursor.worldRemaining), ref cursor.worldOffset);
                cursor.worldRemaining -= inspected;
            }
            if (cursor.found.Count < requested.Count
                && (cursor.liveRemaining > 0 || cursor.worldRemaining > 0)) return found;

            foreach (KeyValuePair<string, Pawn> pair in cursor.found)
                found.Add(pair.Key, pair.Value);
            cursor.cycleActive = false;
            cursor.requested.Clear();
            cursor.found.Clear();
            complete = true;
            return found;
        }

        private static void OfferBoundedMemoryObservationId(
            SortedSet<string> target,
            string id,
            int cap)
        {
            if (target == null || cap <= 0 || string.IsNullOrWhiteSpace(id)) return;
            target.Add(id);
            if (target.Count <= cap) return;
            string greatest = string.Empty;
            foreach (string candidate in target) greatest = candidate;
            target.Remove(greatest);
        }

        private static int IndexRequestedObservedPawns(
            List<Pawn> source,
            SortedSet<string> requested,
            SortedDictionary<string, Pawn> found,
            int scanCap,
            ref int offset)
        {
            if (source == null || requested == null || found == null
                || found.Count >= requested.Count || scanCap <= 0) return 0;
            if (source.Count == 0)
            {
                offset = 0;
                return 0;
            }
            int start = offset < 0 || offset >= source.Count ? 0 : offset;
            int inspected = 0;
            int limit = Math.Min(source.Count, Math.Max(1, scanCap));
            while (inspected < limit)
            {
                Pawn pawn = source[(start + inspected) % source.Count];
                if (found.Count >= requested.Count) break;
                string id = SafePawnId(pawn);
                if (requested.Contains(id) && !found.ContainsKey(id)) found.Add(id, pawn);
                inspected++;
            }
            offset = (start + inspected) % source.Count;
            return inspected;
        }

        private static void AddMemoryObservationCandidate(
            SortedDictionary<string, Pawn> result,
            Pawn owner,
            Pawn candidate)
        {
            if (result == null || candidate == null || candidate == owner) return;
            string id = SafePawnId(candidate);
            if (id.Length > 0 && !result.ContainsKey(id)) result.Add(id, candidate);
        }

        private static void OfferBoundedMemoryObservationCandidate(
            SortedDictionary<string, Pawn> target,
            Pawn owner,
            Pawn candidate,
            int cap,
            SortedDictionary<string, Pawn> excluded)
        {
            if (target == null || candidate == null || cap <= 0) return;
            string id = SafePawnId(candidate);
            if (id.Length == 0 || id == SafePawnId(owner) || target.ContainsKey(id)
                || (excluded != null && excluded.ContainsKey(id))) return;
            if (target.Count < cap)
            {
                target.Add(id, candidate);
                return;
            }

            string greatest = string.Empty;
            foreach (string key in target.Keys) greatest = key;
            if (string.CompareOrdinal(id, greatest) >= 0) return;
            target.Remove(greatest);
            target.Add(id, candidate);
        }

        private bool QueueFullMemoryObservationWork(
            MemoryObservationWorkItem work,
            int cap)
        {
            if (work == null || work.key.Length == 0) return false;
            if (!memoryObservationDirty.ContainsKey(work.key))
                memoryObservationDirty.Add(work.key, work);
            return memoryObservationDirty.Count >= cap;
        }

        private void OfferMemoryObservationWork(MemoryObservationWorkItem work)
        {
            if (work == null || work.key.Length == 0) return;
            if (memoryObservationDirty.ContainsKey(work.key))
            {
                MemoryObservationWorkItem existing = memoryObservationDirty[work.key];
                KnowledgeObservationWorkMergePlan merge =
                    KnowledgeRelationPolicy.MergeObservationWorkFlags(
                        existing.removedFaction,
                        existing.forceSilentBaseline,
                        work.removedFaction,
                        work.forceSilentBaseline);
                existing.removedFaction = merge.removedFaction;
                existing.forceSilentBaseline = merge.forceSilentBaseline;
                return;
            }
            int cap = (int)ReadCapacityLong("dirtyObservationKeys", 1024, 4096);
            if (memoryObservationDirty.Count >= cap)
            {
                // Lost ordering means no transition can be inferred. Restart a cursor-driven full
                // pass and silently baseline its current truth; never retain retry/catch-up debt.
                memoryObservationDirty.Clear();
                RequestMemoryObservationFullReconciliation(true);
                return;
            }
            memoryObservationDirty.Add(work.key, work);
        }

        private KeyValuePair<string, MemoryObservationWorkItem> FirstMemoryObservationWork()
        {
            foreach (KeyValuePair<string, MemoryObservationWorkItem> pair in memoryObservationDirty)
                return pair;
            return new KeyValuePair<string, MemoryObservationWorkItem>();
        }

        private static MemoryObservationWorkItem DirectedPairWork(
            Pawn owner,
            Pawn subject,
            bool silent)
        {
            string ownerId = SafePawnId(owner);
            string subjectId = SafePawnId(subject);
            return new MemoryObservationWorkItem
            {
                key = MemoryObservationWorkKey("1", ownerId, subjectId),
                kind = MemoryObservationWorkKind.DirectedPair,
                owner = owner,
                subject = subject,
                forceSilentBaseline = silent
            };
        }

        private static MemoryObservationWorkItem FactionWork(
            Faction faction,
            bool removed,
            bool silent)
        {
            string id = SafeFactionId(faction);
            return new MemoryObservationWorkItem
            {
                key = MemoryObservationWorkKey("0", id),
                kind = MemoryObservationWorkKind.Faction,
                faction = faction,
                removedFaction = removed,
                forceSilentBaseline = silent
            };
        }

        private static MemoryObservationWorkItem PawnFactionWork(
            Pawn pawn,
            Faction previous,
            Faction current,
            bool silent)
        {
            return new MemoryObservationWorkItem
            {
                key = MemoryObservationWorkKey(
                    "2", SafePawnId(pawn),
                    previous == null ? "none" : SafeFactionId(previous),
                    current == null ? "none" : SafeFactionId(current)),
                kind = MemoryObservationWorkKind.PawnFaction,
                owner = pawn,
                previousFaction = previous,
                currentFaction = current,
                forceSilentBaseline = silent
            };
        }

        private static string MemoryObservationWorkKey(string kind, params string[] ids)
        {
            if (string.IsNullOrWhiteSpace(kind)) return string.Empty;
            string key = OrdinalSegmentCodec.Segment(kind);
            for (int i = 0; ids != null && i < ids.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i])) return string.Empty;
                key += OrdinalSegmentCodec.Segment(ids[i]);
            }
            return key.Length <= MemoryIdentityCodec.MaximumCompleteKeyCharacters
                ? key
                : string.Empty;
        }

        /// <summary>
        /// A failed mandatory settlement leaves its prior tracked value stale. The exact transient
        /// fence makes the first later successful observation baseline-only instead of reconstructing
        /// a transition across the capacity-refused interval.
        /// </summary>
        private bool MemoryObservationSettlementNeedsSilent(
            HashSet<string> exactKeys,
            string exactKey)
        {
            return memoryObservationForceAllSettlementsSilent
                || (!string.IsNullOrEmpty(exactKey) && exactKeys.Contains(exactKey));
        }

        private void RecordMemoryObservationSettlementResult(
            HashSet<string> exactKeys,
            string exactKey,
            bool settled)
        {
            if (string.IsNullOrEmpty(exactKey))
            {
                if (!settled) memoryObservationForceAllSettlementsSilent = true;
                return;
            }
            if (settled)
            {
                exactKeys.Remove(exactKey);
                return;
            }
            int cap = (int)ReadCapacityLong("dirtyObservationKeys", 1024, 4096);
            if (exactKeys.Contains(exactKey) || exactKeys.Count < cap)
                exactKeys.Add(exactKey);
            else
                memoryObservationForceAllSettlementsSilent = true;
        }

        private MemoryObservationOwnerEnrollment PrepareMemoryObservationOwner(
            Pawn owner,
            MemoryObservationBudgetSession budget)
        {
            if (owner == null || !IsDiaryEligible(owner)) return null;
            string ownerId = SafePawnId(owner);
            PawnDiaryRecord diary = FindDiaryByPawnId(ownerId);
            if (diary == null) return null;
            bool created = diary.knowledgeState == null;
            PawnKnowledgeState state = created
                ? PawnKnowledgeState.CreateCurrent(ownerId)
                : diary.knowledgeState;
            if (state == null || !string.Equals(state.pawnId, ownerId, StringComparison.Ordinal))
                return null;
            if (!state.IsCurrentSchema()) return null;
            // Observation is never allowed to turn an archive or the empty post-Brainwipe fence
            // into an active owner. The fence is consumed only by the required lifecycle Landmark.
            if (state.archiveOnly || state.epochFenceOnly
                || state.structuralRevision == long.MaxValue
                || state.statusRevision == long.MaxValue) return null;
            bool ignoredFallback;
            if (!string.IsNullOrEmpty(state.autobiographicalEpochToken))
            {
                return MemoryIdentityCodec.TryValidateEpochToken(
                    state.autobiographicalEpochToken, out ignoredFallback)
                    ? new MemoryObservationOwnerEnrollment
                    {
                        diary = diary,
                        state = state,
                        epochToken = state.autobiographicalEpochToken
                    }
                    : null;
            }
            int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
            int epochFenceCap = (int)ReadCapacityTuplePart(
                "ownerSlotTriple", 1, 1001, 4001);
            if (budget == null || !KnowledgeRelationPolicy.CanAdmitObservationOwner(
                    budget.activeOwnerCount,
                    ownerCap,
                    budget.nonArchiveEpochOwnerCount,
                    epochFenceCap)) return null;

            // Epoch allocation must see every live carrier to remain collision-safe. Refuse a new
            // owner/epoch when corrupt saved collections would make that exact scan unbounded.
            int epochCarrierScanCap = checked(ownerCap * 256);
            long expectedSequence = lastIssuedAutobiographicalEpochSequence;
            string expectedChain = lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty;
            if (budget == null) return null;
            if (budget.epochCarriers == null
                || budget.epochCarrierSequence != expectedSequence
                || !string.Equals(
                    budget.epochCarrierChain, expectedChain, StringComparison.Ordinal))
            {
                budget.epochCarrierSequence = expectedSequence;
                budget.epochCarrierChain = expectedChain;
                budget.epochCarriers = null;
                budget.epochCarrierScanRefused =
                    !CanBoundMemoryObservationEpochCarrierScan(epochCarrierScanCap);
                if (!budget.epochCarrierScanRefused)
                    budget.epochCarriers = SnapshotAutobiographicalEpochCarriers();
            }
            if (budget.epochCarrierScanRefused || budget.epochCarriers == null) return null;
            MemoryEpochAllocationPlan allocation = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = ownerId,
                    lastIssuedSequence = expectedSequence,
                    fallbackChain = expectedChain,
                    liveEpochCarriers = budget.epochCarriers,
                    isTargetBrainwipe = false
                });
            if (!allocation.canMutate) return null;
            return new MemoryObservationOwnerEnrollment
            {
                diary = diary,
                state = state,
                epochToken = allocation.epochToken,
                pendingNewEnvelope = created,
                pendingEpoch = true,
                expectedAllocatorSequence = expectedSequence,
                expectedAllocatorChain = expectedChain,
                allocation = allocation
            };
        }

        private bool CanBoundMemoryObservationEpochCarrierScan(int maximumRows)
        {
            int remaining = Math.Max(1, maximumRows);
            if (!TryReserveScaledMemoryObservationCarrierRows(
                    diaries?.Count ?? 0, 2, ref remaining)
                || !TryReserveMemoryObservationCarrierRows(
                    summaryWordingOpportunities?.Count ?? 0, ref remaining)
                || !TryReserveMemoryObservationCarrierRows(
                    memoryAttemptAuditRows?.Count ?? 0, ref remaining)
                || !TryReserveMemoryObservationCarrierRows(
                    activeMemoryCoordinatorRequests?.Count ?? 0, ref remaining)) return false;

            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            if (!TryReserveScaledMemoryObservationCarrierRows(
                    hotEvents?.Count ?? 0, 3, ref remaining))
                return false;
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnKnowledgeState state = diaries[index]?.knowledgeState;
                if (state == null) continue;
                if (!TryReserveMemoryObservationCarrierRows(
                        state.standaloneBlocks?.Count ?? 0, ref remaining)
                    || !TryReserveMemoryObservationCarrierRows(
                        state.repetitionGuardRows?.Count ?? 0, ref remaining)
                    || !TryReserveScaledMemoryObservationCarrierRows(
                        state.threadRoots?.Count ?? 0, 2, ref remaining)) return false;
                for (int rootIndex = 0; state.threadRoots != null
                    && rootIndex < state.threadRoots.Count; rootIndex++)
                {
                    if (!TryReserveMemoryObservationCarrierRows(
                        state.threadRoots[rootIndex]?.visibleBlocks?.Count ?? 0,
                        ref remaining)) return false;
                }
            }
            return true;
        }

        private static bool TryReserveMemoryObservationCarrierRows(int count, ref int remaining)
        {
            if (count < 0 || count > remaining) return false;
            remaining -= count;
            return true;
        }

        private static bool TryReserveScaledMemoryObservationCarrierRows(
            int count,
            int maximumTokensPerRow,
            ref int remaining)
        {
            if (count < 0 || maximumTokensPerRow < 0) return false;
            try
            {
                return TryReserveMemoryObservationCarrierRows(
                    checked(count * maximumTokensPerRow), ref remaining);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private bool ApplyMemoryAwarenessPlan(
            MemoryObservationOwnerEnrollment enrollment,
            KnowledgeAwarenessState replacement,
            KnowledgeOpinionEpisodeState desiredEpisode,
            string opinionPairKey,
            MemoryObservationBudgetSession budget)
        {
            PawnKnowledgeState state = enrollment?.state;
            if (state == null || replacement == null
                || state.statusRevision == long.MaxValue) return false;
            if (enrollment.pendingEpoch
                && (lastIssuedAutobiographicalEpochSequence
                        != enrollment.expectedAllocatorSequence
                    || !string.Equals(
                        lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                        enrollment.expectedAllocatorChain,
                        StringComparison.Ordinal))) return false;
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            if (!MemoryObservationOwnerRowsWithinRuntimeBounds(
                    state, awarenessCap, episodeCap)) return false;
            List<SavedMemoryAwarenessSnapshot> awareness =
                new List<SavedMemoryAwarenessSnapshot>(
                    state.ownerAwarenessSnapshots ?? new List<SavedMemoryAwarenessSnapshot>());
            int existingIndex = -1;
            for (int i = awareness.Count - 1; i >= 0; i--)
            {
                if (awareness[i] != null
                    && awareness[i].snapshotId == replacement.snapshotId)
                {
                    if (existingIndex < 0) existingIndex = i;
                    else
                    {
                        awareness.RemoveAt(i);
                        existingIndex--;
                    }
                }
            }
            if (existingIndex >= awareness.Count) existingIndex = awareness.Count - 1;
            if (existingIndex >= 0) awareness[existingIndex] = ToSavedAwareness(replacement);
            else if (awareness.Count < awarenessCap) awareness.Add(ToSavedAwareness(replacement));
            else return false;

            List<SavedMemoryCaptureEpisode> episodes =
                new List<SavedMemoryCaptureEpisode>(
                    state.openCaptureEpisodes ?? new List<SavedMemoryCaptureEpisode>());
            if (!string.IsNullOrEmpty(opinionPairKey))
            {
                episodes.RemoveAll(row => row != null
                    && row.captureRuleId == KnowledgeObservationTokens.OpinionEpisodeRule
                    && row.pairOrStreamKey == opinionPairKey);
                if (desiredEpisode != null && episodes.Count < episodeCap)
                    episodes.Add(ToSavedOpinionEpisode(desiredEpisode));
            }
            awareness.Sort((left, right) => string.CompareOrdinal(
                left?.snapshotId, right?.snapshotId));
            episodes.Sort((left, right) => string.CompareOrdinal(
                left?.episodeId, right?.episodeId));

            if (!TryAdmitMemoryObservationOwnerLists(
                    state, awareness, episodes, budget, enrollment))
            {
                KnowledgeAwarenessState marker = CapacityMarker(replacement);
                for (int i = 0; i < awareness.Count; i++)
                    if (awareness[i]?.snapshotId == marker.snapshotId)
                        awareness[i] = ToSavedAwareness(marker);
                if (!string.IsNullOrEmpty(opinionPairKey))
                    episodes.RemoveAll(row => row != null
                        && row.pairOrStreamKey == opinionPairKey);
                if (!TryAdmitMemoryObservationOwnerLists(
                        state, awareness, episodes, budget, enrollment))
                    return false;
            }

            if (enrollment.pendingEpoch)
            {
                lastIssuedAutobiographicalEpochSequence = enrollment.allocation.nextSequence;
                lastIssuedAutobiographicalEpochFallbackChain =
                    enrollment.allocation.nextFallbackChain ?? string.Empty;
                state.autobiographicalEpochToken = enrollment.epochToken;
                state.epochFenceOnly = false;
                state.structuralRevision =
                    AdvanceMemoryObservationRevision(state.structuralRevision);
                if (budget != null) budget.epochCarriers = null;
            }
            state.ownerAwarenessSnapshots = awareness;
            state.openCaptureEpisodes = episodes;
            state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
            bool becameActiveOwner = enrollment.pendingEpoch;
            if (enrollment.pendingNewEnvelope)
            {
                enrollment.diary.knowledgeState = state;
                // The slice's M4 snapshot was built before this envelope existed. Mark it now so
                // same-tick factual capture and the fast budget-index publication can resolve the
                // newly enrolled owner instead of returning MigrationPending indefinitely.
                memoryM4IndexesDirty = true;
            }
            // A pre-existing culture/empty envelope with a blank epoch also consumes an active slot
            // when it enrolls. Counting only newly allocated envelopes allowed many blank-current
            // owners to reuse one remaining slot during the same bounded slice.
            if (becameActiveOwner)
            {
                budget.activeOwnerCount++;
                budget.nonArchiveEpochOwnerCount++;
            }
            if (enrollment.pendingEpoch)
            {
                PawnReflectionState reflection = enrollment.diary.EnsureReflectionState();
                reflection.memoryReflectionSchemaVersion = 1;
                reflection.memoryOwnerEpochToken = enrollment.epochToken;
            }
            enrollment.pendingNewEnvelope = false;
            enrollment.pendingEpoch = false;
            memoryObservationMutatedThisTick = true;
            return true;
        }

        private static bool MemoryObservationOwnerRowsWithinRuntimeBounds(
            PawnKnowledgeState state,
            int awarenessCap,
            int episodeCap)
        {
            if (state == null) return false;
            return (state.ownerAwarenessSnapshots?.Count ?? 0) <= checked(awarenessCap * 4)
                && (state.openCaptureEpisodes?.Count ?? 0) <= checked(episodeCap * 4);
        }

        private bool TryAdmitMemoryObservationOwnerLists(
            PawnKnowledgeState state,
            List<SavedMemoryAwarenessSnapshot> awareness,
            List<SavedMemoryCaptureEpisode> episodes,
            MemoryObservationBudgetSession budget,
            MemoryObservationOwnerEnrollment enrollment = null)
        {
            MemoryLogicalSizeResult oldAwareness = SizeListValidated(state.ownerAwarenessSnapshots);
            MemoryLogicalSizeResult newAwareness = SizeListValidated(awareness);
            MemoryLogicalSizeResult oldEpisodes = SizeListValidated(state.openCaptureEpisodes);
            MemoryLogicalSizeResult newEpisodes = SizeListValidated(episodes);
            if (!oldAwareness.valid || !newAwareness.valid
                || !oldEpisodes.valid || !newEpisodes.valid) return false;
            long delta;
            long importedDelta = 0;
            long componentActiveDelta = 0;
            try
            {
                delta = checked(newAwareness.totalBytes + newEpisodes.totalBytes
                    - oldAwareness.totalBytes - oldEpisodes.totalBytes);
                if (enrollment?.pendingEpoch == true)
                {
                    if (!enrollment.pendingNewEnvelope)
                    {
                        // Epoch tokens are canonical ASCII. The four-byte string prefix already
                        // exists, so only payload bytes change when a blank current owner enrolls.
                        delta = checked(delta + enrollment.epochToken.Length
                            - (state.autobiographicalEpochToken ?? string.Empty).Length);
                    }
                    componentActiveDelta = checked(
                        (enrollment.allocation?.nextFallbackChain ?? string.Empty).Length
                        - (enrollment.expectedAllocatorChain ?? string.Empty).Length);
                }
            }
            catch (OverflowException)
            {
                return false;
            }
            MemoryOwnerByteTotals ownerTotals;
            if (enrollment?.pendingNewEnvelope == true)
            {
                state.autobiographicalEpochToken = enrollment.epochToken;
                state.epochFenceOnly = false;
                state.ownerAwarenessSnapshots = awareness;
                state.openCaptureEpisodes = episodes;
                MemoryLogicalSizeResult whole = MemoryLogicalPayloadSizer.Size(state);
                MemoryLogicalSizeResult imported = SizeListValidated(state.importedArchiveRows);
                if (!whole.valid || !imported.valid || whole.totalBytes < imported.totalBytes)
                    return false;
                delta = whole.totalBytes - imported.totalBytes;
                importedDelta = imported.totalBytes;
                ownerTotals = new MemoryOwnerByteTotals
                {
                    valid = true,
                    activeBytes = 0,
                    importedBytes = 0
                };
            }
            else if (!budget.owners.TryGetValue(state.pawnId ?? string.Empty, out ownerTotals)
                || !ownerTotals.valid)
            {
                RefreshMemoryObservationBudgetSession(budget);
                if (!budget.owners.TryGetValue(state.pawnId ?? string.Empty, out ownerTotals)
                    || !ownerTotals.valid) return false;
            }
            if (delta <= 0 && importedDelta == 0)
            {
                // Cleanup/shrink remains admissible even when a loaded save or a settings change
                // leaves the starting totals above today's caps. Growth gates must not make stale
                // awareness or episode rows permanent.
                if (ownerTotals.activeBytes < 0 || ownerTotals.importedBytes < 0
                    || budget.global.globalActiveBytes < 0
                    || budget.global.globalImportedBytes < 0) return false;
                try
                {
                    long ownerActive = checked(ownerTotals.activeBytes + delta);
                    if (ownerActive < 0) return false;
                    // A blank current owner can shrink its saved rows while saturated enrollment
                    // grows component-owned fallback metadata. Gate the aggregate global change:
                    // a net shrink remains admissible above today's caps, while net growth still
                    // observes both hard caps and the Brainwipe metadata reserve.
                    long aggregateGlobalDelta = checked(delta + componentActiveDelta);
                    MemoryGlobalBudgetDecision globalDecision =
                        ActiveMemoryPayloadBudget.TryAdmitComponentActive(
                            budget.limits, aggregateGlobalDelta, budget.global);
                    if (globalDecision.outcome != MemoryBudgetOutcome.Admitted) return false;
                    budget.owners[state.pawnId] = new MemoryOwnerByteTotals
                    {
                        valid = true,
                        activeBytes = ownerActive,
                        importedBytes = ownerTotals.importedBytes
                    };
                    budget.global = globalDecision.newTotals;
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }
            MemoryBudgetDecision decision = ActiveMemoryPayloadBudget.TryAdmit(
                budget.limits,
                ownerTotals.activeBytes,
                ownerTotals.importedBytes,
                delta,
                importedDelta,
                budget.global);
            if (decision.outcome != MemoryBudgetOutcome.Admitted) return false;
            MemoryPayloadBudgetTotals admittedGlobal = decision.newTotals;
            if (componentActiveDelta != 0)
            {
                MemoryGlobalBudgetDecision componentDecision =
                    ActiveMemoryPayloadBudget.TryAdmitComponentActive(
                        budget.limits, componentActiveDelta, admittedGlobal);
                if (componentDecision.outcome != MemoryBudgetOutcome.Admitted) return false;
                admittedGlobal = componentDecision.newTotals;
            }
            budget.global = admittedGlobal;
            budget.owners[state.pawnId] = new MemoryOwnerByteTotals
            {
                valid = true,
                activeBytes = decision.newOwnerActiveBytes,
                importedBytes = decision.newOwnerImportedBytes
            };
            return true;
        }

        private bool ApplyGlobalFactionPlan(
            KnowledgeFactionState replacement,
            MemoryObservationBudgetSession budget)
        {
            if (replacement == null) return false;
            int cap = (int)ReadCapacityLong("factionSnapshots", 256, 1024);
            List<SavedGlobalFactionSnapshot> candidate =
                new List<SavedGlobalFactionSnapshot>(globalFactionSnapshots);
            int existing = -1;
            for (int i = candidate.Count - 1; i >= 0; i--)
            {
                SavedGlobalFactionSnapshot row = candidate[i];
                if (row != null && row.factionInstanceId == replacement.factionInstanceId
                    && row.allocatorGeneration == replacement.allocatorGeneration)
                {
                    if (existing < 0) existing = i;
                    else
                    {
                        candidate.RemoveAt(i);
                        existing--;
                    }
                }
            }
            if (existing >= candidate.Count) existing = candidate.Count - 1;
            if (existing >= 0) candidate[existing] = ToSavedFaction(replacement);
            else if (candidate.Count < cap) candidate.Add(ToSavedFaction(replacement));
            else return false;
            SortGlobalFactionSnapshots(candidate);
            if (!TryAdmitGlobalFactionList(candidate, budget))
            {
                KnowledgeFactionState marker = FactionCapacityMarker(replacement);
                for (int i = 0; i < candidate.Count; i++)
                    if (candidate[i]?.factionInstanceId == marker.factionInstanceId
                        && candidate[i].allocatorGeneration == marker.allocatorGeneration)
                        candidate[i] = ToSavedFaction(marker);
                if (!TryAdmitGlobalFactionList(candidate, budget)) return false;
            }
            globalFactionSnapshots = candidate;
            memoryObservationMutatedThisTick = true;
            return true;
        }

        private bool TryAdmitGlobalFactionList(
            List<SavedGlobalFactionSnapshot> candidate,
            MemoryObservationBudgetSession budget)
        {
            MemoryLogicalSizeResult oldSize = SizeListValidated(globalFactionSnapshots);
            MemoryLogicalSizeResult newSize = SizeListValidated(candidate);
            if (!oldSize.valid || !newSize.valid) return false;
            try
            {
                long delta = checked(newSize.totalBytes - oldSize.totalBytes);
                MemoryGlobalBudgetDecision decision =
                    ActiveMemoryPayloadBudget.TryAdmitComponentActive(
                        budget.limits, delta, budget.global);
                if (decision.outcome != MemoryBudgetOutcome.Admitted) return false;
                budget.global = decision.newTotals;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private MemoryObservationBudgetSession CreateMemoryObservationBudgetSession()
        {
            if (memorySizeIndexGeneration <= 0) RebuildMemorySizeIndexes();
            if (memoryObservationRetainedBudget != null
                && memoryObservationRetainedBudgetIndexGeneration
                    == memorySizeIndexGeneration)
            {
                memoryObservationRetainedBudget.limits = CurrentMemoryBudgetLimits();
                return memoryObservationRetainedBudget;
            }
            MemoryObservationBudgetSession result = new MemoryObservationBudgetSession
            {
                limits = CurrentMemoryBudgetLimits()
            };
            PublishMemoryObservationBudgetTotals(result);
            memoryObservationRetainedBudget = result;
            memoryObservationRetainedBudgetIndexGeneration = memorySizeIndexGeneration;
            return result;
        }

        private void RefreshMemoryObservationBudgetSession(MemoryObservationBudgetSession session)
        {
            if (session == null) return;
            RebuildMemorySizeIndexes();
            PublishMemoryObservationBudgetTotals(session);
        }

        private void PublishMemoryObservationBudgetTotals(MemoryObservationBudgetSession session)
        {
            session.owners.Clear();
            foreach (KeyValuePair<string, MemoryOwnerByteTotals> pair in memoryByteTotalsByOwner)
                session.owners[pair.Key] = pair.Value;
            session.global = GetGlobalBudgetTotals();
            int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
            int epochFenceCap = (int)ReadCapacityTuplePart(
                "ownerSlotTriple", 1, 1001, 4001);
            MemoryObservationOwnerDirectoryCounts counts =
                CountMemoryObservationOwnerDirectory(ownerCap + 1, epochFenceCap + 1);
            session.activeOwnerCount = counts.active;
            session.nonArchiveEpochOwnerCount = counts.nonArchiveEpoch;
        }

        private bool TryPublishMemoryObservationBudgetIndexes(
            MemoryObservationBudgetSession session)
        {
            if (session == null || session.global.globalActiveBytes < 0
                || session.global.globalImportedBytes < 0) return false;
            MemoryLogicalSizeResult unknown = SizeListValidated(unresolvedOwnerArchiveRows);
            if (!unknown.valid || unknown.totalBytes < 0) return false;
            try
            {
                long ownerActive = 0;
                long ownerImported = 0;
                foreach (KeyValuePair<string, MemoryOwnerByteTotals> pair in session.owners)
                {
                    MemoryOwnerByteTotals totals = pair.Value;
                    if (string.IsNullOrWhiteSpace(pair.Key) || !totals.valid
                        || totals.activeBytes < 0 || totals.importedBytes < 0) return false;
                    ownerActive = checked(ownerActive + totals.activeBytes);
                    ownerImported = checked(ownerImported + totals.importedBytes);
                }
                long componentActive = checked(
                    session.global.globalActiveBytes - ownerActive);
                long expectedImported = checked(ownerImported + unknown.totalBytes);
                if (componentActive < 0
                    || expectedImported != session.global.globalImportedBytes) return false;

                memoryByteTotalsByOwner.Clear();
                foreach (KeyValuePair<string, MemoryOwnerByteTotals> pair in session.owners)
                    memoryByteTotalsByOwner.Add(pair.Key, pair.Value);
                memoryComponentActiveBytesTotal = componentActive;
                if (memoryM4IndexesDirty) RebuildMemoryM4Indexes();
                AdvanceMemorySizeIndexGeneration();
                memoryObservationRetainedBudget = session;
                memoryObservationRetainedBudgetIndexGeneration = memorySizeIndexGeneration;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Counts only active current envelopes and stops at the first impossible admission. The
        /// count is session-derived, duplicate-safe, and never scans a pathological diary list past
        /// the configured owner cap merely to prove that the cap is already full.
        /// </summary>
        private int CountMemoryObservationActiveOwners(int stopAfter)
        {
            return CountMemoryObservationOwnerDirectory(stopAfter, stopAfter).active;
        }

        /// <summary>
        /// Counts both directory domains in one bounded pass. Duplicate holders are collapsed by
        /// exact owner id; an uninspectable pathological tail fails both domains closed as full.
        /// </summary>
        private MemoryObservationOwnerDirectoryCounts CountMemoryObservationOwnerDirectory(
            int activeStopAfter,
            int nonArchiveEpochStopAfter)
        {
            int activeLimit = Math.Max(1, activeStopAfter);
            int epochLimit = Math.Max(1, nonArchiveEpochStopAfter);
            int inspectionLimit = checked(Math.Max(activeLimit, epochLimit) * 4);
            HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> nonArchiveEpoch = new HashSet<string>(StringComparer.Ordinal);
            int inspected = 0;
            for (int i = 0; diaries != null && i < diaries.Count
                && (active.Count < activeLimit || nonArchiveEpoch.Count < epochLimit)
                && inspected < inspectionLimit; i++, inspected++)
            {
                PawnDiaryRecord diary = diaries[i];
                PawnKnowledgeState state = diary?.knowledgeState;
                if (state == null || string.IsNullOrWhiteSpace(diary.pawnId)) continue;
                if (active.Count < activeLimit
                    && KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                        state.IsCurrentSchema(), state.archiveOnly, state.epochFenceOnly,
                        state.autobiographicalEpochToken)) active.Add(diary.pawnId);
                if (nonArchiveEpoch.Count < epochLimit
                    && KnowledgeRelationPolicy.CountsAsNonArchiveEpochOwner(
                        state.IsCurrentSchema(), state.archiveOnly,
                        state.autobiographicalEpochToken))
                    nonArchiveEpoch.Add(diary.pawnId);
            }
            if (diaries != null && inspected < diaries.Count
                && (active.Count < activeLimit || nonArchiveEpoch.Count < epochLimit))
            {
                return new MemoryObservationOwnerDirectoryCounts
                {
                    active = activeLimit,
                    nonArchiveEpoch = epochLimit
                };
            }
            return new MemoryObservationOwnerDirectoryCounts
            {
                active = active.Count,
                nonArchiveEpoch = nonArchiveEpoch.Count
            };
        }

        /// <summary>
        /// Checks the saved owner directories outside an observation budget session. Activating an
        /// existing Brainwipe fence consumes only active headroom; ordinary blank/new enrollment
        /// consumes one slot in both the active directory and the active-plus-fence union.
        /// </summary>
        private bool CanAdmitMemoryOwnerEpoch(bool activatesExistingFence)
        {
            int activeCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
            int epochFenceCap = (int)ReadCapacityTuplePart(
                "ownerSlotTriple", 1, 1001, 4001);
            MemoryObservationOwnerDirectoryCounts counts =
                CountMemoryObservationOwnerDirectory(activeCap + 1, epochFenceCap + 1);
            return activatesExistingFence
                ? KnowledgeRelationPolicy.CanAdmitObservationOwner(counts.active, activeCap)
                : KnowledgeRelationPolicy.CanAdmitObservationOwner(
                    counts.active, activeCap,
                    counts.nonArchiveEpoch, epochFenceCap);
        }

        private void RemoveHiddenMemoryObservation(
            string ownerId,
            string subjectId,
            MemoryObservationBudgetSession budget)
        {
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(ownerId);
            if (!IsWritableMemoryObservationEnvelope(state)) return;
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            if (!MemoryObservationOwnerRowsWithinRuntimeBounds(
                    state, awarenessCap, episodeCap)) return;
            List<SavedMemoryAwarenessSnapshot> awareness =
                new List<SavedMemoryAwarenessSnapshot>(
                    state.ownerAwarenessSnapshots ?? new List<SavedMemoryAwarenessSnapshot>());
            int removed = awareness.RemoveAll(row => row != null
                && KnowledgeRelationPolicy.CanRemoveShadowSnapshot(row.snapshotRevision)
                && row.subjectKind == KnowledgeObservationTokens.SubjectPawn
                && row.subjectId == subjectId
                && (row.scopeKindToken == KnowledgeObservationTokens.ScopeRelationship
                    || row.scopeKindToken == KnowledgeObservationTokens.ScopeRelative));
            List<SavedMemoryCaptureEpisode> episodes =
                new List<SavedMemoryCaptureEpisode>(
                    state.openCaptureEpisodes ?? new List<SavedMemoryCaptureEpisode>());
            removed += episodes.RemoveAll(row => row != null
                && row.subjectKind == KnowledgeObservationTokens.SubjectPawn
                && row.subjectId == subjectId);
            if (removed > 0
                && TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget))
            {
                state.ownerAwarenessSnapshots = awareness;
                state.openCaptureEpisodes = episodes;
                state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
                memoryObservationMutatedThisTick = true;
                PruneOrphanedFamilyFactionConnections(state, budget);
            }
        }

        private void RemoveRelativeMemoryObservation(
            string ownerId,
            string subjectId,
            MemoryObservationBudgetSession budget)
        {
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(ownerId);
            if (!IsWritableMemoryObservationEnvelope(state)) return;
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            if (!MemoryObservationOwnerRowsWithinRuntimeBounds(
                    state, awarenessCap, episodeCap)) return;
            List<SavedMemoryAwarenessSnapshot> awareness =
                new List<SavedMemoryAwarenessSnapshot>(
                    state.ownerAwarenessSnapshots ?? new List<SavedMemoryAwarenessSnapshot>());
            if (awareness.RemoveAll(row => row != null
                    && KnowledgeRelationPolicy.CanRemoveShadowSnapshot(row.snapshotRevision)
                    && row.scopeKindToken == KnowledgeObservationTokens.ScopeRelative
                    && row.subjectKind == KnowledgeObservationTokens.SubjectPawn
                    && row.subjectId == subjectId) == 0) return;
            List<SavedMemoryCaptureEpisode> episodes =
                new List<SavedMemoryCaptureEpisode>(
                    state.openCaptureEpisodes ?? new List<SavedMemoryCaptureEpisode>());
            if (TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget))
            {
                state.ownerAwarenessSnapshots = awareness;
                state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
                memoryObservationMutatedThisTick = true;
                PruneOrphanedFamilyFactionConnections(state, budget);
            }
        }

        private void PruneOrphanedFamilyFactionConnections(
            PawnKnowledgeState state,
            MemoryObservationBudgetSession budget)
        {
            if (!IsWritableMemoryObservationEnvelope(state)
                || state.ownerAwarenessSnapshots == null) return;
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            if (!MemoryObservationOwnerRowsWithinRuntimeBounds(
                    state, awarenessCap, episodeCap)) return;
            List<KnowledgeAwarenessState> relativeSnapshots =
                new List<KnowledgeAwarenessState>();
            for (int i = 0; i < state.ownerAwarenessSnapshots.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = state.ownerAwarenessSnapshots[i];
                if (row == null || row.scopeKindToken != KnowledgeObservationTokens.ScopeRelative)
                    continue;
                relativeSnapshots.Add(ToPlainAwareness(row));
            }

            List<SavedMemoryAwarenessSnapshot> awareness =
                new List<SavedMemoryAwarenessSnapshot>(state.ownerAwarenessSnapshots);
            int removed = awareness.RemoveAll(row => row != null
                && KnowledgeRelationPolicy.CanRemoveShadowSnapshot(row.snapshotRevision)
                && row.scopeKindToken == KnowledgeObservationTokens.ScopeFaction
                && row.factStreamToken == KnowledgeObservationTokens.StreamFactionConnection
                && SavedFactValue(row.stateFacts,
                    KnowledgeObservationTokens.FactConnectionKind)
                    == KnowledgeObservationTokens.ConnectionFamily
                && KnowledgeRelationPolicy.CanPruneFamilyFactionConnection(
                    row.subjectId, relativeSnapshots));
            if (removed == 0) return;
            List<SavedMemoryCaptureEpisode> episodes =
                new List<SavedMemoryCaptureEpisode>(state.openCaptureEpisodes
                    ?? new List<SavedMemoryCaptureEpisode>());
            if (!TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget)) return;
            state.ownerAwarenessSnapshots = awareness;
            state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
            memoryObservationMutatedThisTick = true;
        }

        private static bool IsWritableMemoryObservationEnvelope(PawnKnowledgeState state)
        {
            if (state == null || !state.IsCurrentSchema() || state.archiveOnly
                || state.epochFenceOnly || state.statusRevision == long.MaxValue
                || state.structuralRevision == long.MaxValue
                || string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)) return false;
            bool ignoredFallback;
            return MemoryIdentityCodec.TryValidateEpochToken(
                state.autobiographicalEpochToken, out ignoredFallback);
        }

        private void RemoveGlobalFactionSnapshots(
            string instanceId,
            MemoryObservationBudgetSession budget)
        {
            List<SavedGlobalFactionSnapshot> candidate =
                new List<SavedGlobalFactionSnapshot>(globalFactionSnapshots);
            if (candidate.RemoveAll(row => row != null
                    && KnowledgeRelationPolicy.CanRemoveShadowSnapshot(row.snapshotRevision)
                    && row.factionInstanceId == instanceId) == 0) return;
            if (TryAdmitGlobalFactionList(candidate, budget))
            {
                globalFactionSnapshots = candidate;
                memoryObservationMutatedThisTick = true;
            }
        }

        private static KnowledgeRelationVisibilityInput MemoryRelationVisibility(
            Pawn owner,
            Pawn candidate)
        {
            if (owner?.relations == null || candidate?.relations == null) return null;
            Name name = candidate.Name;
            return new KnowledgeRelationVisibilityInput
            {
                candidateIsDeadAnimalWithoutCorpse = candidate.RaceProps?.Animal == true
                    && candidate.Dead && candidate.Corpse == null,
                candidateHasName = name != null,
                candidateNameIsNumerical = name != null && name.Numerical,
                candidateHidesRelations = candidate.relations.hidePawnRelations,
                ownerHidesRelations = owner.relations.hidePawnRelations,
                candidateEverSeenByPlayer = candidate.relations.everSeenByPlayer
            };
        }

        private static bool IsCurrentMemorySocialCandidate(Pawn owner, Pawn candidate)
        {
            if (owner == null || candidate == null || owner == candidate) return false;
            int outboundOpinion;
            int inboundOpinion;
            if (!TryReadOpinion(owner, candidate, out outboundOpinion)
                || !TryReadOpinion(candidate, owner, out inboundOpinion)) return false;
            KnowledgeRelationVisibilityInput visibility =
                MemoryRelationVisibility(owner, candidate);
            if (visibility == null) return false;
            visibility.candidateIsHumanlike = candidate.RaceProps?.Humanlike == true;
            visibility.sharesSocialContext = SharesMemorySocialContext(owner, candidate);
            visibility.ownerOpinionOfCandidate = outboundOpinion;
            visibility.candidateOpinionOfOwner = inboundOpinion;
            return KnowledgeRelationPolicy.IsKnownSocialEntry(visibility);
        }

        private static bool SharesMemorySocialContext(Pawn owner, Pawn candidate)
        {
            if (owner == null || candidate == null) return false;
            Map map = owner.MapHeld;
            if (map != null) return candidate.MapHeld == map;
            Caravan caravan = owner.GetCaravan();
            return caravan != null && candidate.GetCaravan() == caravan;
        }

        private static IEnumerable<Pawn> MemorySocialContextPawns(Pawn owner)
        {
            if (owner?.MapHeld?.mapPawns?.AllPawns != null)
                return owner.MapHeld.mapPawns.AllPawns;
            Caravan caravan = owner?.GetCaravan();
            if (caravan?.PawnsListForReading != null)
                return caravan.PawnsListForReading;
            return new Pawn[0];
        }

        private static List<string> DirectRelationDefNames(
            Pawn owner,
            Pawn subject,
            int inspectionCap)
        {
            List<string> result = new List<string>();
            if (owner == null || subject == null) return result;
            int cap = Math.Max(1, inspectionCap);
            int inspected = 0;
            foreach (PawnRelationDef relation in owner.GetRelations(subject))
            {
                inspected++;
                if (inspected > cap)
                {
                    // A deliberately over-cap value makes the pure encoder return false. The
                    // resulting capacity_untracked row is exact about what the adapter could prove.
                    result.Clear();
                    result.Add(new string('x', cap + 1));
                    break;
                }
                if (relation != null && !string.IsNullOrWhiteSpace(relation.defName))
                    result.Add(relation.defName);
            }
            return result;
        }

        private static bool HasVisibleFamilyRelation(
            Pawn owner,
            Pawn subject,
            KnowledgeObservationPolicySnapshot policy)
        {
            return HasFamilyRelationOneWay(owner, subject, policy)
                || HasFamilyRelationOneWay(subject, owner, policy);
        }

        private static bool HasFamilyRelationOneWay(
            Pawn owner,
            Pawn subject,
            KnowledgeObservationPolicySnapshot policy)
        {
            if (owner == null || subject == null) return false;
            int cap = Math.Max(1, policy?.maximumFactValueCharacters ?? 128);
            int inspected = 0;
            foreach (PawnRelationDef relation in owner.GetRelations(subject))
            {
                inspected++;
                if (inspected > cap) return false;
                if (relation != null
                    && KnowledgeRelationPolicy.IsFamilyRelation(
                        relation.familyByBloodRelation,
                        relation == PawnRelationDefOf.Spouse,
                        relation.defName,
                        policy))
                    return true;
            }
            return false;
        }

        private static string MemoryRelativeLocation(Pawn owner, Pawn subject)
        {
            if (owner == null || subject == null)
                return KnowledgeObservationTokens.LocationUnknown;
            bool sharesMap = owner.Spawned && owner.Map != null
                && subject.Spawned && ReferenceEquals(owner.Map, subject.Map);
            Caravan ownerCaravan = owner.GetCaravan();
            Caravan subjectCaravan = subject.GetCaravan();
            bool sharesCaravan = ownerCaravan != null
                && ReferenceEquals(ownerCaravan, subjectCaravan);
            bool corpseOnOwnersMap = owner.Spawned && owner.Map != null
                && subject.Dead && subject.Corpse?.Spawned == true
                && ReferenceEquals(owner.Map, subject.Corpse.Map);
            if (!KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation(
                    sharesMap, sharesCaravan, corpseOnOwnersMap))
                return KnowledgeObservationTokens.LocationUnknown;
            if (sharesMap)
                return OrdinalSegmentCodec.Segment("map")
                    + OrdinalSegmentCodec.Segment(
                        subject.Map.uniqueID.ToString(CultureInfo.InvariantCulture));
            if (sharesCaravan)
                return OrdinalSegmentCodec.Segment("caravan")
                    + OrdinalSegmentCodec.Segment(subjectCaravan.GetUniqueLoadID());
            if (corpseOnOwnersMap)
                return OrdinalSegmentCodec.Segment("corpse_map")
                    + OrdinalSegmentCodec.Segment(
                        subject.Corpse.Map.uniqueID.ToString(CultureInfo.InvariantCulture));
            return KnowledgeObservationTokens.LocationUnknown;
        }

        private string FactionSubjectIdFor(Faction faction)
        {
            string instanceId = SafeFactionId(faction);
            SavedGlobalFactionSnapshot row = LatestFactionSnapshot(instanceId, false);
            string subjectId;
            return row != null && MemoryIdentityCodec.TryCreateFactionSubjectId(
                row.factionInstanceId, row.allocatorGeneration, out subjectId)
                ? subjectId
                : string.Empty;
        }

        private SavedGlobalFactionSnapshot LatestFactionSnapshot(
            string instanceId,
            bool includeRemoved)
        {
            SavedGlobalFactionSnapshot result = null;
            for (int i = 0; i < globalFactionSnapshots.Count; i++)
            {
                SavedGlobalFactionSnapshot row = globalFactionSnapshots[i];
                if (row == null || row.factionInstanceId != instanceId
                    || (!includeRemoved && row.removed)) continue;
                if (result == null || row.allocatorGeneration > result.allocatorGeneration)
                    result = row;
            }
            return result;
        }

        private static void SortGlobalFactionSnapshots(List<SavedGlobalFactionSnapshot> rows)
        {
            rows.Sort((left, right) =>
            {
                string leftId;
                string rightId;
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    left?.factionInstanceId, left?.allocatorGeneration ?? 0, out leftId);
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    right?.factionInstanceId, right?.allocatorGeneration ?? 0, out rightId);
                return string.CompareOrdinal(leftId, rightId);
            });
        }

        private SavedMemoryAwarenessSnapshot FindAwarenessSnapshot(
            PawnKnowledgeState state,
            string snapshotId)
        {
            for (int i = 0; state?.ownerAwarenessSnapshots != null
                && i < state.ownerAwarenessSnapshots.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = state.ownerAwarenessSnapshots[i];
                if (row != null && row.snapshotId == snapshotId) return row;
            }
            return null;
        }

        private static KnowledgeAwarenessState FindPlainAwareness(
            PawnKnowledgeState state,
            string scope,
            string stream,
            string subjectId)
        {
            for (int i = 0; state?.ownerAwarenessSnapshots != null
                && i < state.ownerAwarenessSnapshots.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = state.ownerAwarenessSnapshots[i];
                if (row != null && row.scopeKindToken == scope
                    && row.factStreamToken == stream && row.subjectId == subjectId)
                    return ToPlainAwareness(row);
            }
            return null;
        }

        private static SavedMemoryCaptureEpisode FindOpinionEpisode(
            PawnKnowledgeState state,
            string pairKey)
        {
            for (int i = 0; state?.openCaptureEpisodes != null
                && i < state.openCaptureEpisodes.Count; i++)
            {
                SavedMemoryCaptureEpisode row = state.openCaptureEpisodes[i];
                if (row != null
                    && row.captureRuleId == KnowledgeObservationTokens.OpinionEpisodeRule
                    && row.pairOrStreamKey == pairKey) return row;
            }
            return null;
        }

        private static KnowledgeAwarenessState ToPlainAwareness(
            SavedMemoryAwarenessSnapshot source)
        {
            if (source == null) return null;
            return new KnowledgeAwarenessState
            {
                snapshotId = source.snapshotId ?? string.Empty,
                scopeKindToken = source.scopeKindToken ?? string.Empty,
                subjectKind = source.subjectKind ?? string.Empty,
                subjectId = source.subjectId ?? string.Empty,
                factStreamToken = source.factStreamToken ?? string.Empty,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                knownnessEvidenceToken = source.knownnessEvidenceToken ?? string.Empty,
                stateFacts = ToPlainFacts(source.stateFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId ?? string.Empty,
                trackingStateToken = source.trackingStateToken ?? string.Empty,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static KnowledgeOpinionEpisodeState ToPlainOpinionEpisode(
            SavedMemoryCaptureEpisode source)
        {
            if (source == null) return null;
            return new KnowledgeOpinionEpisodeState
            {
                episodeId = source.episodeId ?? string.Empty,
                captureRuleId = source.captureRuleId ?? string.Empty,
                scopeKindToken = source.scopeKindToken ?? string.Empty,
                factStreamToken = source.factStreamToken ?? string.Empty,
                category = source.category ?? string.Empty,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                episodeKindToken = source.episodeKindToken ?? string.Empty,
                subjectKind = source.subjectKind ?? string.Empty,
                subjectId = source.subjectId ?? string.Empty,
                pairOrStreamKey = source.pairOrStreamKey ?? string.Empty,
                directionToken = source.directionToken ?? string.Empty,
                baselineFacts = ToPlainFacts(source.baselineFacts),
                currentFacts = ToPlainFacts(source.currentFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId ?? string.Empty,
                episodeRevision = source.episodeRevision
            };
        }

        private static List<KnowledgeStateFact> ToPlainFacts(List<SavedMemoryStateFact> source)
        {
            List<KnowledgeStateFact> result = new List<KnowledgeStateFact>();
            for (int i = 0; source != null && i < source.Count; i++)
            {
                SavedMemoryStateFact row = source[i];
                if (row != null) result.Add(NewObservationFact(row.factKey, row.factValue));
            }
            return result;
        }

        private static SavedMemoryAwarenessSnapshot ToSavedAwareness(
            KnowledgeAwarenessState source)
        {
            return new SavedMemoryAwarenessSnapshot
            {
                schemaVersion = 1,
                snapshotId = source.snapshotId ?? string.Empty,
                scopeKindToken = source.scopeKindToken ?? string.Empty,
                subjectKind = source.subjectKind ?? string.Empty,
                subjectId = source.subjectId ?? string.Empty,
                factStreamToken = source.factStreamToken ?? string.Empty,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                knownnessEvidenceToken = source.knownnessEvidenceToken ?? string.Empty,
                stateFacts = ToSavedFacts(source.stateFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId ?? string.Empty,
                trackingStateToken = source.trackingStateToken ?? string.Empty,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static SavedMemoryCaptureEpisode ToSavedOpinionEpisode(
            KnowledgeOpinionEpisodeState source)
        {
            return new SavedMemoryCaptureEpisode
            {
                schemaVersion = 1,
                episodeId = source.episodeId ?? string.Empty,
                captureRuleId = source.captureRuleId ?? string.Empty,
                scopeKindToken = source.scopeKindToken ?? string.Empty,
                factStreamToken = source.factStreamToken ?? string.Empty,
                category = source.category ?? string.Empty,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                episodeKindToken = source.episodeKindToken ?? string.Empty,
                subjectKind = source.subjectKind ?? string.Empty,
                subjectId = source.subjectId ?? string.Empty,
                pairOrStreamKey = source.pairOrStreamKey ?? string.Empty,
                directionToken = source.directionToken ?? string.Empty,
                baselineFacts = ToSavedFacts(source.baselineFacts),
                currentFacts = ToSavedFacts(source.currentFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId ?? string.Empty,
                episodeRevision = source.episodeRevision
            };
        }

        private static List<SavedMemoryStateFact> ToSavedFacts(List<KnowledgeStateFact> source)
        {
            List<SavedMemoryStateFact> result = new List<SavedMemoryStateFact>();
            for (int i = 0; source != null && i < source.Count; i++)
            {
                KnowledgeStateFact row = source[i];
                if (row != null) result.Add(new SavedMemoryStateFact
                {
                    schemaVersion = 1,
                    factKey = row.key ?? string.Empty,
                    factValue = row.value ?? string.Empty
                });
            }
            return result;
        }

        private static KnowledgeFactionState ToPlainFaction(SavedGlobalFactionSnapshot source)
        {
            if (source == null) return null;
            return new KnowledgeFactionState
            {
                factionInstanceId = source.factionInstanceId ?? string.Empty,
                allocatorGeneration = source.allocatorGeneration,
                factionDefName = source.factionDefName ?? string.Empty,
                frozenDisplayLabel = source.frozenDisplayLabel ?? string.Empty,
                goodwill = source.goodwill,
                relationKindToken = source.relationKindToken ?? string.Empty,
                leaderPawnId = source.leaderPawnId ?? string.Empty,
                defeated = source.defeated,
                removed = source.removed,
                observedTick = source.observedTick,
                trackingStateToken = source.trackingStateToken ?? string.Empty,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static SavedGlobalFactionSnapshot ToSavedFaction(KnowledgeFactionState source)
        {
            return new SavedGlobalFactionSnapshot
            {
                schemaVersion = 1,
                factionInstanceId = source.factionInstanceId ?? string.Empty,
                allocatorGeneration = source.allocatorGeneration,
                factionDefName = source.factionDefName ?? string.Empty,
                frozenDisplayLabel = source.frozenDisplayLabel ?? string.Empty,
                goodwill = source.goodwill,
                relationKindToken = source.relationKindToken ?? string.Empty,
                leaderPawnId = source.leaderPawnId ?? string.Empty,
                defeated = source.defeated,
                removed = source.removed,
                observedTick = source.observedTick,
                trackingStateToken = source.trackingStateToken ?? string.Empty,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static KnowledgeAwarenessState CapacityMarker(KnowledgeAwarenessState source)
        {
            source.stateFacts = new List<KnowledgeStateFact>();
            source.knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceRepairConflict;
            source.trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked;
            return source;
        }

        private static KnowledgeFactionState FactionCapacityMarker(KnowledgeFactionState source)
        {
            source.factionDefName = string.Empty;
            source.frozenDisplayLabel = string.Empty;
            source.goodwill = 0;
            source.relationKindToken = string.Empty;
            source.leaderPawnId = string.Empty;
            source.defeated = false;
            source.removed = false;
            source.trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked;
            return source;
        }

        /// <summary>Creates the canonical no-page identity for one settled observation transition.</summary>
        private static string MemoryObservationOccurrenceId(
            string signalToken,
            long tick,
            long sequence,
            string factDiscriminator,
            string ownerPawnId,
            string subjectPawnId,
            string factionSubjectId)
        {
            if (sequence < 0) return string.Empty;
            List<MemoryTypedSubject> subjects = new List<MemoryTypedSubject>();
            if (!string.IsNullOrWhiteSpace(ownerPawnId))
                subjects.Add(new MemoryTypedSubject
                {
                    subjectKind = MemoryContractTokens.SubjectPawn,
                    subjectId = ownerPawnId
                });
            if (!string.IsNullOrWhiteSpace(subjectPawnId))
                subjects.Add(new MemoryTypedSubject
                {
                    subjectKind = MemoryContractTokens.SubjectPawn,
                    subjectId = subjectPawnId
                });
            if (!string.IsNullOrWhiteSpace(factionSubjectId))
                subjects.Add(new MemoryTypedSubject
                {
                    subjectKind = MemoryContractTokens.SubjectFaction,
                    subjectId = factionSubjectId
                });
            string occurrenceId;
            return MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(
                new MemorySourceOccurrenceFallback
                {
                    stableSignalToken = signalToken ?? string.Empty,
                    eventTickInvariant = Math.Max(0, tick),
                    sourceLocalSequenceInvariant = sequence,
                    factDiscriminator = factDiscriminator ?? string.Empty,
                    subjects = subjects,
                    sourceProvesUniqueness = true
                },
                out occurrenceId)
                ? occurrenceId
                : string.Empty;
        }

        private static long NextObservationSequence(long current)
        {
            if (current == long.MaxValue) return -1;
            return current < 0 ? 1 : current + 1;
        }

        private bool CaptureOpinionEpisodeMemory(
            Pawn owner,
            Pawn subject,
            KnowledgeAwarenessState previous,
            SavedMemoryCaptureEpisode priorEpisode,
            KnowledgeOpinionPlan plan,
            string sourceOccurrenceId,
            int currentOpinion,
            int now)
        {
            // Defense in depth: current policy has exactly one durable opinion-memory door. If a
            // future caller supplies point drift or a within-band reversal, current truth remains
            // settled but no generic block reaches the person's thread.
            if (plan == null || plan.qualificationReasonToken != "band_crossing") return false;
            string baselineOpinion = SavedFactValue(
                priorEpisode?.baselineFacts, KnowledgeObservationTokens.FactOpinionValue);
            if (baselineOpinion.Length == 0)
                baselineOpinion = FactValue(
                    previous?.stateFacts, KnowledgeObservationTokens.FactOpinionValue);
            string baselineBand = SavedFactValue(
                priorEpisode?.baselineFacts, KnowledgeObservationTokens.FactOpinionBand);
            if (baselineBand.Length == 0)
                baselineBand = FactValue(
                    previous?.stateFacts, KnowledgeObservationTokens.FactOpinionBand);
            string currentBand = FactValue(
                plan.replacement?.stateFacts, KnowledgeObservationTokens.FactOpinionBand);
            if (baselineBand.Length == 0 || currentBand.Length == 0
                || string.Equals(baselineBand, currentBand, StringComparison.Ordinal)) return false;
            int baselineOpinionValue;
            if (!int.TryParse(
                    baselineOpinion,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out baselineOpinionValue)) return false;
            // This adapter runs on RimWorld's main thread, so localized bucket labels are safe to
            // freeze into the readable memory wording. Canonical identity/facts keep their stable
            // English schema tokens in episodeValue below.
            string baselineBandLabel = DiaryLineCleaner.CleanLine(
                DiaryBuckets.FormatOpinion(baselineOpinionValue));
            string currentBandLabel = DiaryLineCleaner.CleanLine(
                DiaryBuckets.FormatOpinion(currentOpinion));
            if (baselineBandLabel.Length == 0 || currentBandLabel.Length == 0) return false;
            string episodeValue = "reason:" + (plan.qualificationReasonToken ?? string.Empty)
                + "|from:" + baselineOpinion
                + "|to:" + currentOpinion.ToString(CultureInfo.InvariantCulture)
                + "|from_band:" + baselineBand
                + "|to_band:" + currentBand;
            string subjectId = SafePawnId(subject);
            string subjectName = DiaryLineCleaner.CleanLine(subject?.LabelShortCap);
            string context = "subject_pawn_id=" + GameContextValue.Sanitize(subjectId)
                + "; subject_name=" + GameContextValue.Sanitize(subjectName)
                + "; episode_value=" + GameContextValue.Sanitize(episodeValue)
                + "; from_opinion_band=" + GameContextValue.Sanitize(baselineBandLabel)
                + "; to_opinion_band=" + GameContextValue.Sanitize(currentBandLabel);
            return EmitObservationMemory(
                KnowledgeTokens.SignalMemoryOpinionEpisode,
                "memory.opinion.episode", owner, subject,
                context, sourceOccurrenceId, now);
        }

        private bool AuthoritativePageOwnsSocialTransition(
            KnowledgeAwarenessState previous,
            List<string> outbound,
            List<string> inbound)
        {
            List<string> prior = new List<string>();
            List<string> decoded;
            if (KnowledgeRelationPolicy.TryDecodeRelationDefSet(
                    FactValue(previous?.stateFacts,
                        KnowledgeObservationTokens.FactOutboundRelations), out decoded))
                prior.AddRange(decoded);
            if (KnowledgeRelationPolicy.TryDecodeRelationDefSet(
                    FactValue(previous?.stateFacts,
                        KnowledgeObservationTokens.FactInboundRelations), out decoded))
                prior.AddRange(decoded);
            List<string> current = new List<string>();
            if (outbound != null) current.AddRange(outbound);
            if (inbound != null) current.AddRange(inbound);
            return ImportantEventClassifier.AuthoritativePageOwnsRelationTransition(
                prior, current, DiaryKnowledgePolicy.ImportantEventRules());
        }

        private bool CaptureFormalRelationMemory(
            Pawn owner,
            Pawn subject,
            List<string> outbound,
            List<string> inbound,
            string sourceOccurrenceId,
            int now)
        {
            string encodedOutbound;
            string encodedInbound;
            if (!KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                    outbound, 192, out encodedOutbound)
                || !KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                    inbound, 192, out encodedInbound)) return false;
            string subjectId = SafePawnId(subject);
            string subjectName = DiaryLineCleaner.CleanLine(subject?.LabelShortCap);
            string relationValue = "out:" + encodedOutbound + "|in:" + encodedInbound;
            string context = "subject_pawn_id=" + GameContextValue.Sanitize(subjectId)
                + "; subject_name=" + GameContextValue.Sanitize(subjectName)
                + "; relation_value=" + GameContextValue.Sanitize(relationValue);
            return EmitObservationMemory(
                KnowledgeTokens.SignalMemoryFormalRelation,
                "memory.formal.relation", owner, subject,
                context, sourceOccurrenceId, now);
        }

        private bool AuthoritativePageOwnsRelativeTransition(
            KnowledgeAwarenessState previous,
            KnowledgeAwarenessState current,
            List<string> currentRelations)
        {
            string priorLife = FactValue(
                previous?.stateFacts, KnowledgeObservationTokens.FactLifeState);
            string currentLife = FactValue(
                current?.stateFacts, KnowledgeObservationTokens.FactLifeState);
            if (priorLife == KnowledgeObservationTokens.LifeAlive
                && currentLife == KnowledgeObservationTokens.LifeDead) return true;
            List<string> previousRelations;
            if (!KnowledgeRelationPolicy.TryDecodeRelationDefSet(
                FactValue(previous?.stateFacts, KnowledgeObservationTokens.FactRelationDefs),
                out previousRelations)) previousRelations = new List<string>();
            return ImportantEventClassifier.AuthoritativePageOwnsRelationTransition(
                previousRelations, currentRelations,
                DiaryKnowledgePolicy.ImportantEventRules());
        }

        private bool CaptureRelativeStateMemory(
            Pawn owner,
            Pawn subject,
            KnowledgeAwarenessState current,
            string sourceOccurrenceId,
            int now)
        {
            string value = "life:" + FactValue(
                    current.stateFacts, KnowledgeObservationTokens.FactLifeState)
                + "|location:" + FactValue(
                    current.stateFacts, KnowledgeObservationTokens.FactLocationState)
                + "|faction:" + FactValue(
                    current.stateFacts, KnowledgeObservationTokens.FactFactionSubject)
                + "|relations:" + FactValue(
                    current.stateFacts, KnowledgeObservationTokens.FactRelationDefs);
            string subjectId = SafePawnId(subject);
            string subjectName = DiaryLineCleaner.CleanLine(subject?.LabelShortCap);
            string context = "subject_pawn_id=" + GameContextValue.Sanitize(subjectId)
                + "; subject_name=" + GameContextValue.Sanitize(subjectName)
                + "; relative_state_value=" + GameContextValue.Sanitize(value);
            return EmitObservationMemory(
                KnowledgeTokens.SignalMemoryRelativeState,
                "memory.relative.state", owner, subject,
                context, sourceOccurrenceId, now);
        }

        private bool CaptureFactionStateMemories(
            KnowledgeFactionState previous,
            KnowledgeFactionState current,
            string factionSubjectId,
            string sourceOccurrenceId,
            int now)
        {
            if (current == null || diaries == null || sourceOccurrenceId.Length == 0) return false;
            bool diplomacyChanged = KnowledgeRelationPolicy.IsFactionDiplomacyEpisode(
                previous, current);
            bool lifecycleChanged = previous != null
                && (previous.defeated != current.defeated || previous.removed != current.removed);
            if (!diplomacyChanged && !lifecycleChanged) return false;

            bool factualAdmitted = false;

            string diplomacyValue = "relation:" + current.relationKindToken
                + "|goodwill:" + current.goodwill.ToString(CultureInfo.InvariantCulture)
                + "|leader:" + (current.leaderPawnId.Length == 0
                    ? "none" : current.leaderPawnId);
            string lifecycleValue = "defeated:" + (current.defeated ? "true" : "false")
                + "|removed:" + (current.removed ? "true" : "false");
            // Faction truth is global, but episodic faction memories are owner-private. Restrict
            // fan-out to live first-person diary owners before checking their already-known exact
            // faction edge; this cannot discover a relationship through a world scan.
            HashSet<string> eligibleOwnerIds = new HashSet<string>(StringComparer.Ordinal);
            List<Pawn> freeColonists = SnapshotFreeColonists();
            for (int i = 0; freeColonists != null && i < freeColonists.Count; i++)
            {
                Pawn pawn = freeColonists[i];
                if (IsDiaryEligible(pawn)) eligibleOwnerIds.Add(SafePawnId(pawn));
            }
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                PawnKnowledgeState state = diary?.knowledgeState;
                if (diary == null || !eligibleOwnerIds.Contains(diary.pawnId)
                    || state == null || !state.IsCurrentSchema()
                    || string.IsNullOrEmpty(state.autobiographicalEpochToken)
                    || !OwnerHasFactionConnection(state, factionSubjectId)) continue;
                string common = "faction_subject_id=" + GameContextValue.Sanitize(factionSubjectId)
                    + "; faction_name=" + GameContextValue.Sanitize(current.frozenDisplayLabel);
                if (diplomacyChanged)
                    factualAdmitted |= EmitObservationMemory(
                        KnowledgeTokens.SignalMemoryFactionDiplomacy,
                        "memory.faction.diplomacy", diary.pawnId, null,
                        common + "; faction_state_value="
                            + GameContextValue.Sanitize(diplomacyValue),
                        sourceOccurrenceId, now);
                if (lifecycleChanged)
                    factualAdmitted |= EmitObservationMemory(
                        KnowledgeTokens.SignalMemoryFactionLifecycle,
                        "memory.faction.lifecycle", diary.pawnId, null,
                        common + "; faction_lifecycle_value="
                            + GameContextValue.Sanitize(lifecycleValue),
                        sourceOccurrenceId, now);
            }
            return factualAdmitted;
        }

        private static bool OwnerHasFactionConnection(
            PawnKnowledgeState state, string factionSubjectId)
        {
            List<SavedMemoryAwarenessSnapshot> rows = state?.ownerAwarenessSnapshots;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = rows[i];
                if (row == null
                    || row.scopeKindToken != KnowledgeObservationTokens.ScopeFaction
                    || row.subjectKind != KnowledgeObservationTokens.SubjectFaction
                    || row.subjectId != factionSubjectId
                    || row.factStreamToken != KnowledgeObservationTokens.StreamFactionConnection
                    || row.trackingStateToken != KnowledgeObservationTokens.TrackingTracked) continue;
                string connection = SavedFactValue(
                    row.stateFacts, KnowledgeObservationTokens.FactConnectionKind);
                return connection == KnowledgeObservationTokens.ConnectionCurrent
                    || connection == KnowledgeObservationTokens.ConnectionRecentFormer
                    || connection == KnowledgeObservationTokens.ConnectionFamily;
            }
            return false;
        }

        private bool EmitObservationMemory(
            string channel,
            string defName,
            Pawn owner,
            Pawn subject,
            string context,
            string sourceOccurrenceId,
            int now)
        {
            return EmitObservationMemory(
                channel, defName, SafePawnId(owner), subject,
                context, sourceOccurrenceId, now);
        }

        private bool EmitObservationMemory(
            string channel,
            string defName,
            string ownerPawnId,
            Pawn subject,
            string context,
            string sourceOccurrenceId,
            int now)
        {
            if (string.IsNullOrWhiteSpace(ownerPawnId)
                || string.IsNullOrWhiteSpace(sourceOccurrenceId)) return false;
            KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
            {
                signal = channel,
                defName = defName,
                sourceOccurrenceId = sourceOccurrenceId,
                tick = Math.Max(0, now),
                gameContext = context ?? string.Empty,
                providedOwnerPawnId = ownerPawnId
            };
            string subjectId = SafePawnId(subject);
            if (subjectId.Length > 0)
                signal.extraParticipants.Add(new KnowledgeParticipant
                {
                    pawnId = subjectId,
                    name = DiaryLineCleaner.CleanLine(subject.LabelShortCap)
                });
            return PersistDrafts(ImportantEventClassifier.Classify(
                signal, DiaryKnowledgePolicy.ImportantEventRules(),
                DiaryKnowledgePolicy.Snapshot()), false);
        }

        private static KnowledgeStateFact NewObservationFact(string key, string value)
        {
            return new KnowledgeStateFact
            {
                key = key ?? string.Empty,
                value = value ?? string.Empty
            };
        }

        private static string FactValue(List<KnowledgeStateFact> facts, string key)
        {
            for (int i = 0; facts != null && i < facts.Count; i++)
                if (facts[i]?.key == key) return facts[i].value ?? string.Empty;
            return string.Empty;
        }

        private static string SavedFactValue(List<SavedMemoryStateFact> facts, string key)
        {
            for (int i = 0; facts != null && i < facts.Count; i++)
                if (facts[i]?.factKey == key) return facts[i].factValue ?? string.Empty;
            return string.Empty;
        }

        private static KnowledgeOpinionBandThresholds SnapshotMemoryOpinionBands()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            return new KnowledgeOpinionBandThresholds
            {
                devoted = tuning.opinionDevoted,
                friendly = tuning.opinionFriendly,
                neutralAbove = tuning.opinionNeutralAbove,
                strainedAbove = tuning.opinionStrainedAbove
            };
        }

        private static long AdvanceMemoryObservationRevision(long revision)
        {
            if (revision <= 0) return 1;
            return revision == long.MaxValue ? long.MaxValue : revision + 1;
        }

        private static string SafePawnId(Pawn pawn)
        {
            try { return pawn?.GetUniqueLoadID() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeFactionId(Faction faction)
        {
            try { return faction?.GetUniqueLoadID() ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Load repair keeps the existing shape bounded and canonical before the forced silent scan.
        /// Full semantic duplicate/conflict selection is deterministic and never emits history.
        /// </summary>
        private void NormalizeMemoryObservationSavedState()
        {
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            KnowledgeObservationPolicySnapshot observationPolicy =
                DiaryKnowledgePolicy.MemoryObservationSnapshot();
            observationPolicy.maximumStateFacts = (int)ReadCapacityLong(
                "awarenessFacts", 4, 16);
            ReadCapacityPair(
                "factKeyValueUnits",
                48,
                128,
                192,
                512,
                out observationPolicy.maximumFactKeyCharacters,
                out observationPolicy.maximumFactValueCharacters);
            observationPolicy = observationPolicy.Normalized();
            MemoryPolicySnapshot capturePolicy = MemoryEffectivePolicyProvider.Current;
            bool changed = false;
            for (int diaryIndex = 0; diaries != null && diaryIndex < diaries.Count; diaryIndex++)
            {
                PawnKnowledgeState state = diaries[diaryIndex]?.knowledgeState;
                if (state == null || !state.IsCurrentSchema()) continue;
                int awarenessInputCap = checked(awarenessCap * 4);
                int episodeInputCap = checked(episodeCap * 4);
                bool oversized = (state.ownerAwarenessSnapshots?.Count ?? 0) > awarenessInputCap
                    || (state.openCaptureEpisodes?.Count ?? 0) > episodeInputCap;
                PawnKnowledgeState working = oversized
                    ? new PawnKnowledgeState
                    {
                        pawnId = state.pawnId,
                        autobiographicalEpochToken = state.autobiographicalEpochToken,
                        ownerAwarenessSnapshots = new List<SavedMemoryAwarenessSnapshot>(),
                        openCaptureEpisodes = new List<SavedMemoryCaptureEpisode>()
                    }
                    : MemoryObservationRepairCopy(state);
                bool ownerChanged = oversized || state.ownerAwarenessSnapshots == null
                    || state.openCaptureEpisodes == null;
                if (!oversized)
                {
                    ownerChanged |= NormalizeOwnerAwarenessRows(
                        working, awarenessCap, observationPolicy, capturePolicy);
                    ownerChanged |= NormalizeOwnerEpisodeRows(
                        working, awarenessCap, episodeCap, observationPolicy, capturePolicy);
                }
                KnowledgeOwnerRepairRevisionPlan revisionPlan =
                    KnowledgeRelationPolicy.PlanOwnerRepairRevision(
                        state.statusRevision, ownerChanged);
                if (revisionPlan.canCommit)
                {
                    state.ownerAwarenessSnapshots = working.ownerAwarenessSnapshots;
                    state.openCaptureEpisodes = working.openCaptureEpisodes;
                    state.statusRevision = revisionPlan.nextRevision;
                    changed = true;
                }
            }
            changed |= NormalizeGlobalFactionRows((int)ReadCapacityLong(
                "factionSnapshots", 256, 1024),
                (int)ReadCapacityLong("frozenDisplayLabelUnits", 80, 320));
            if (changed)
            {
                RebuildMemorySizeIndexes();
                memoryObservationPublicationDirty = true;
            }
        }

        /// <summary>
        /// Copies only the owner identity plus M6 rows used by load repair. Normalization can then
        /// plan and merge duplicates without mutating live saved rows before the revision fence passes.
        /// </summary>
        private static PawnKnowledgeState MemoryObservationRepairCopy(PawnKnowledgeState source)
        {
            PawnKnowledgeState copy = new PawnKnowledgeState
            {
                pawnId = source.pawnId,
                autobiographicalEpochToken = source.autobiographicalEpochToken,
                ownerAwarenessSnapshots = new List<SavedMemoryAwarenessSnapshot>(),
                openCaptureEpisodes = new List<SavedMemoryCaptureEpisode>()
            };
            for (int i = 0; source.ownerAwarenessSnapshots != null
                && i < source.ownerAwarenessSnapshots.Count; i++)
            {
                copy.ownerAwarenessSnapshots.Add(CloneSavedAwareness(
                    source.ownerAwarenessSnapshots[i]));
            }
            for (int i = 0; source.openCaptureEpisodes != null
                && i < source.openCaptureEpisodes.Count; i++)
            {
                copy.openCaptureEpisodes.Add(CloneSavedEpisode(source.openCaptureEpisodes[i]));
            }
            return copy;
        }

        private static SavedMemoryAwarenessSnapshot CloneSavedAwareness(
            SavedMemoryAwarenessSnapshot source)
        {
            if (source == null) return null;
            return new SavedMemoryAwarenessSnapshot
            {
                schemaVersion = source.schemaVersion,
                snapshotId = source.snapshotId,
                scopeKindToken = source.scopeKindToken,
                subjectKind = source.subjectKind,
                subjectId = source.subjectId,
                factStreamToken = source.factStreamToken,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                knownnessEvidenceToken = source.knownnessEvidenceToken,
                stateFacts = CloneSavedFacts(source.stateFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId,
                trackingStateToken = source.trackingStateToken,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static SavedMemoryCaptureEpisode CloneSavedEpisode(
            SavedMemoryCaptureEpisode source)
        {
            if (source == null) return null;
            return new SavedMemoryCaptureEpisode
            {
                schemaVersion = source.schemaVersion,
                episodeId = source.episodeId,
                captureRuleId = source.captureRuleId,
                scopeKindToken = source.scopeKindToken,
                factStreamToken = source.factStreamToken,
                category = source.category,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                episodeKindToken = source.episodeKindToken,
                subjectKind = source.subjectKind,
                subjectId = source.subjectId,
                pairOrStreamKey = source.pairOrStreamKey,
                directionToken = source.directionToken,
                baselineFacts = CloneSavedFacts(source.baselineFacts),
                currentFacts = CloneSavedFacts(source.currentFacts),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId,
                episodeRevision = source.episodeRevision
            };
        }

        private static List<SavedMemoryStateFact> CloneSavedFacts(
            List<SavedMemoryStateFact> source)
        {
            List<SavedMemoryStateFact> copy = new List<SavedMemoryStateFact>();
            for (int i = 0; source != null && i < source.Count; i++)
            {
                SavedMemoryStateFact row = source[i];
                copy.Add(row == null ? null : new SavedMemoryStateFact
                {
                    schemaVersion = row.schemaVersion,
                    factKey = row.factKey,
                    factValue = row.factValue
                });
            }
            return copy;
        }

        private static bool NormalizeOwnerAwarenessRows(
            PawnKnowledgeState state,
            int cap,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryPolicySnapshot capturePolicy)
        {
            List<SavedMemoryAwarenessSnapshot> source = state.ownerAwarenessSnapshots
                ?? new List<SavedMemoryAwarenessSnapshot>();
            SortedDictionary<string, List<SavedMemoryAwarenessSnapshot>> groups =
                new SortedDictionary<string, List<SavedMemoryAwarenessSnapshot>>(
                    StringComparer.Ordinal);
            bool changed = state.ownerAwarenessSnapshots == null;
            for (int i = 0; i < source.Count; i++)
            {
                SavedMemoryAwarenessSnapshot row = source[i];
                string expected;
                if (row == null || !KnowledgeRelationPolicy.TryCreateAwarenessId(
                        state.pawnId,
                        state.autobiographicalEpochToken,
                        row.scopeKindToken,
                        row.subjectKind,
                        row.subjectId,
                        row.factStreamToken,
                        out expected)
                    || row.snapshotId != expected
                    || !KnowledgeRelationPolicy.IsKnownObservationStreamShape(
                        row.scopeKindToken, row.subjectKind, row.factStreamToken)
                    || !KnowledgeRelationPolicy.IsValidObservationSubject(
                        row.subjectKind, row.subjectId))
                {
                    changed = true;
                    continue;
                }
                List<SavedMemoryAwarenessSnapshot> group;
                if (!groups.TryGetValue(expected, out group))
                {
                    group = new List<SavedMemoryAwarenessSnapshot>();
                    groups.Add(expected, group);
                }
                group.Add(row);
            }

            List<SavedMemoryAwarenessSnapshot> rows =
                new List<SavedMemoryAwarenessSnapshot>();
            foreach (KeyValuePair<string, List<SavedMemoryAwarenessSnapshot>> pair in groups)
            {
                List<SavedMemoryAwarenessSnapshot> group = pair.Value;
                if (group.Count > 1) changed = true;
                bool invalid = false;
                List<KnowledgeAwarenessState> plainGroup =
                    new List<KnowledgeAwarenessState>();
                for (int i = 0; i < group.Count; i++)
                {
                    SavedMemoryAwarenessSnapshot row = group[i];
                    bool exactMarker = IsExactAwarenessCapacityMarker(row);
                    List<SavedMemoryStateFact> normalizedFacts = null;
                    bool rowValid = row.schemaVersion == 1
                        && row.firstObservedTick >= 0
                        && row.lastObservedTick >= 0
                        && row.snapshotRevision >= 0
                        && (exactMarker || (row.snapshotRevision > 0
                            && row.snapshotRevision < long.MaxValue))
                        && row.captureInvalidationGeneration >= 0
                        && (exactMarker || (row.trackingStateToken
                                == KnowledgeObservationTokens.TrackingTracked
                            && KnowledgeObservationTokens.IsKnownEvidence(
                                row.knownnessEvidenceToken)
                            && row.knownnessEvidenceToken
                                != KnowledgeObservationTokens.EvidenceRepairConflict
                            && TryNormalizeSavedFacts(
                                row.stateFacts,
                                row.scopeKindToken,
                                row.subjectKind,
                                row.factStreamToken,
                                observationPolicy,
                                false,
                                out normalizedFacts)));
                    if (!rowValid)
                    {
                        invalid = true;
                    }
                    else if (!exactMarker)
                    {
                        if (!SavedFactsEqual(row.stateFacts, normalizedFacts)) changed = true;
                        row.stateFacts = normalizedFacts;
                    }
                    plainGroup.Add(ToPlainAwareness(row));
                }

                KnowledgeAwarenessRepairPlan repair =
                    KnowledgeRelationPolicy.PlanAwarenessDuplicateRepair(
                        plainGroup,
                        invalid,
                        CaptureGenerationForScope(capturePolicy, group[0].scopeKindToken));
                if (!repair.valid)
                {
                    changed = true;
                    continue;
                }
                SavedMemoryAwarenessSnapshot winner = repair.conflict
                    ? ToSavedAwareness(repair.repairMarker)
                    : group[repair.retainedIndex];
                if (repair.conflict) changed = true;
                rows.Add(winner);
            }
            if (rows.Count > cap)
            {
                rows.RemoveRange(cap, rows.Count - cap);
                changed = true;
            }
            bool orderChanged = !SameAwarenessOrder(source, rows);
            if (changed || orderChanged) state.ownerAwarenessSnapshots = rows;
            return changed || orderChanged;
        }

        private static bool AwarenessRowsEqual(
            SavedMemoryAwarenessSnapshot first,
            SavedMemoryAwarenessSnapshot second)
        {
            return first.scopeKindToken == second.scopeKindToken
                && first.snapshotId == second.snapshotId
                && first.subjectKind == second.subjectKind
                && first.subjectId == second.subjectId
                && first.factStreamToken == second.factStreamToken
                && first.captureInvalidationGeneration == second.captureInvalidationGeneration
                && first.knownnessEvidenceToken == second.knownnessEvidenceToken
                && first.firstObservedTick == second.firstObservedTick
                && first.lastObservedTick == second.lastObservedTick
                && first.lastSourceOccurrenceId == second.lastSourceOccurrenceId
                && first.trackingStateToken == second.trackingStateToken
                && first.snapshotRevision == second.snapshotRevision
                && SavedFactsEqual(first.stateFacts, second.stateFacts);
        }

        private static bool NormalizeOwnerEpisodeRows(
            PawnKnowledgeState state,
            int awarenessCap,
            int episodeCap,
            KnowledgeObservationPolicySnapshot observationPolicy,
            MemoryPolicySnapshot capturePolicy)
        {
            List<SavedMemoryCaptureEpisode> source = state.openCaptureEpisodes
                ?? new List<SavedMemoryCaptureEpisode>();
            SortedDictionary<string, List<SavedMemoryCaptureEpisode>> groups =
                new SortedDictionary<string, List<SavedMemoryCaptureEpisode>>(
                    StringComparer.Ordinal);
            bool changed = state.openCaptureEpisodes == null;
            for (int i = 0; i < source.Count; i++)
            {
                SavedMemoryCaptureEpisode row = source[i];
                string expected;
                if (row == null || !KnowledgeRelationPolicy.TryCreateEpisodeId(
                        state.pawnId,
                        state.autobiographicalEpochToken,
                        row.scopeKindToken,
                        row.factStreamToken,
                        row.captureRuleId,
                        row.episodeKindToken,
                        row.subjectKind,
                        row.subjectId,
                        row.pairOrStreamKey,
                        row.directionToken,
                        out expected)
                    || row.episodeId != expected)
                {
                    changed = true;
                    continue;
                }
                string expectedPair;
                if (row.scopeKindToken != KnowledgeObservationTokens.ScopeRelationship
                    || row.factStreamToken != KnowledgeObservationTokens.StreamDirectedSocial
                    || row.captureRuleId != KnowledgeObservationTokens.OpinionEpisodeRule
                    || row.episodeKindToken != KnowledgeObservationTokens.OpinionEpisodeKind
                    || row.subjectKind != KnowledgeObservationTokens.SubjectPawn
                    || row.category != MemoryContractTokens.CategoryRelationships
                    || (row.directionToken != KnowledgeObservationTokens.DirectionRising
                        && row.directionToken != KnowledgeObservationTokens.DirectionFalling)
                    || !KnowledgeRelationPolicy.TryCreateDirectedPairKey(
                        state.pawnId, row.subjectId, out expectedPair)
                    || row.pairOrStreamKey != expectedPair)
                {
                    // Incomplete stream identity is orphaned. Do not guess an awareness marker.
                    changed = true;
                    continue;
                }
                long currentGeneration = capturePolicy?.CaptureGeneration(
                    MemoryCategoryBits.Relationships) ?? 0;
                if (capturePolicy == null
                    || !capturePolicy.AllowsCapture(MemoryCategoryBits.Relationships)
                    || currentGeneration <= 0
                    || currentGeneration == long.MaxValue
                    || row.captureInvalidationGeneration != currentGeneration)
                {
                    // Off/on and generation changes discard the accumulator without catch-up.
                    changed = true;
                    continue;
                }
                if (row.episodeRevision == long.MaxValue)
                {
                    changed = true;
                    continue;
                }
                List<SavedMemoryCaptureEpisode> group;
                if (!groups.TryGetValue(expected, out group))
                {
                    group = new List<SavedMemoryCaptureEpisode>();
                    groups.Add(expected, group);
                }
                group.Add(row);
            }

            List<SavedMemoryCaptureEpisode> rows = new List<SavedMemoryCaptureEpisode>();
            bool awarenessChanged = false;
            foreach (KeyValuePair<string, List<SavedMemoryCaptureEpisode>> pair in groups)
            {
                List<SavedMemoryCaptureEpisode> group = pair.Value;
                if (group.Count > 1) changed = true;
                bool invalid = false;
                List<KnowledgeOpinionEpisodeState> plainGroup =
                    new List<KnowledgeOpinionEpisodeState>();
                for (int i = 0; i < group.Count; i++)
                {
                    SavedMemoryCaptureEpisode row = group[i];
                    List<SavedMemoryStateFact> baseline = null;
                    List<SavedMemoryStateFact> current = null;
                    bool rowValid = row.schemaVersion == 1
                        && row.firstObservedTick >= 0
                        && row.lastObservedTick >= 0
                        && row.episodeRevision > 0
                        && TryNormalizeSavedFacts(
                            row.baselineFacts,
                            row.scopeKindToken,
                            row.subjectKind,
                            row.factStreamToken,
                            observationPolicy,
                            true,
                            out baseline)
                        && TryNormalizeSavedFacts(
                            row.currentFacts,
                            row.scopeKindToken,
                            row.subjectKind,
                            row.factStreamToken,
                            observationPolicy,
                            true,
                            out current);
                    if (!rowValid)
                    {
                        invalid = true;
                    }
                    else
                    {
                        if (!SavedFactsEqual(row.baselineFacts, baseline)
                            || !SavedFactsEqual(row.currentFacts, current)) changed = true;
                        row.baselineFacts = baseline;
                        row.currentFacts = current;
                    }
                    plainGroup.Add(ToPlainOpinionEpisode(row));
                }

                long currentGeneration = capturePolicy.CaptureGeneration(
                    MemoryCategoryBits.Relationships);
                KnowledgeEpisodeRepairPlan repair =
                    KnowledgeRelationPolicy.PlanEpisodeDuplicateRepair(
                        state.pawnId,
                        state.autobiographicalEpochToken,
                        plainGroup,
                        invalid,
                        currentGeneration);
                if (!repair.valid)
                {
                    changed = true;
                    continue;
                }
                KnowledgeAwarenessState backingAwareness = FindPlainAwareness(
                    state,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    group[repair.conflict ? 0 : repair.retainedIndex].subjectId);
                KnowledgeEpisodeBackingDisposition disposition =
                    KnowledgeRelationPolicy.EpisodeBackingDisposition(
                        backingAwareness,
                        repair.conflict ? plainGroup[0] : plainGroup[repair.retainedIndex],
                        repair.conflict);
                if (disposition == KnowledgeEpisodeBackingDisposition.DropWithoutMarker)
                {
                    // A missing pair is orphaned; an untracked/mismatched pair is already settled.
                    // Neither case guesses a marker, and the next real observation baselines silently.
                    changed = true;
                    continue;
                }
                if (disposition == KnowledgeEpisodeBackingDisposition.PublishConflictMarker)
                {
                    awarenessChanged |= MergeAwarenessRepairMarker(
                        state, ToSavedAwareness(repair.repairMarker), awarenessCap);
                    changed = true;
                    continue;
                }
                rows.Add(group[repair.retainedIndex]);
            }
            if (rows.Count > episodeCap)
            {
                rows.RemoveRange(episodeCap, rows.Count - episodeCap);
                changed = true;
            }
            bool orderChanged = !SameEpisodeOrder(source, rows);
            if (changed || orderChanged) state.openCaptureEpisodes = rows;
            if (awarenessChanged)
            {
                state.ownerAwarenessSnapshots.Sort((left, right) => string.CompareOrdinal(
                    left?.snapshotId, right?.snapshotId));
            }
            return changed || orderChanged || awarenessChanged;
        }

        private bool NormalizeGlobalFactionRows(int cap, int labelCap)
        {
            int inputCap = checked(cap * 4);
            if (globalFactionSnapshots.Count > inputCap)
            {
                // Corrupt oversized shadow truth is discarded silently; the forced full scan will
                // rebuild exact current instances without spending unbounded load-time work.
                globalFactionSnapshots = new List<SavedGlobalFactionSnapshot>();
                return true;
            }
            SortedDictionary<string, List<SavedGlobalFactionSnapshot>> groups =
                new SortedDictionary<string, List<SavedGlobalFactionSnapshot>>(
                    StringComparer.Ordinal);
            bool changed = false;
            for (int i = 0; i < globalFactionSnapshots.Count; i++)
            {
                SavedGlobalFactionSnapshot row = globalFactionSnapshots[i];
                string key;
                if (row == null || !MemoryIdentityCodec.TryCreateFactionSubjectId(
                        row.factionInstanceId, row.allocatorGeneration, out key))
                {
                    changed = true;
                    continue;
                }
                List<SavedGlobalFactionSnapshot> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<SavedGlobalFactionSnapshot>();
                    groups.Add(key, group);
                }
                group.Add(row);
            }

            List<SavedGlobalFactionSnapshot> rows = new List<SavedGlobalFactionSnapshot>();
            foreach (KeyValuePair<string, List<SavedGlobalFactionSnapshot>> pair in groups)
            {
                List<SavedGlobalFactionSnapshot> group = pair.Value;
                if (group.Count > 1) changed = true;
                bool invalid = false;
                List<KnowledgeFactionState> plainGroup = new List<KnowledgeFactionState>();
                for (int i = 0; i < group.Count; i++)
                {
                    SavedGlobalFactionSnapshot row = group[i];
                    if (!IsValidSavedFactionRow(row, labelCap)) invalid = true;
                    plainGroup.Add(ToPlainFaction(row));
                }
                KnowledgeFactionRepairPlan repair =
                    KnowledgeRelationPolicy.PlanFactionDuplicateRepair(plainGroup, invalid);
                if (!repair.valid)
                {
                    changed = true;
                    continue;
                }
                SavedGlobalFactionSnapshot winner = repair.conflict
                    ? ToSavedFaction(repair.repairMarker)
                    : group[repair.retainedIndex];
                if (repair.conflict) changed = true;
                rows.Add(winner);
            }
            if (rows.Count > cap)
            {
                rows.RemoveRange(cap, rows.Count - cap);
                changed = true;
            }
            bool orderChanged = !SameFactionOrder(globalFactionSnapshots, rows);
            if (changed || orderChanged)
                globalFactionSnapshots = rows;
            return changed || orderChanged;
        }

        private static bool TryNormalizeSavedFacts(
            List<SavedMemoryStateFact> source,
            string scopeKindToken,
            string subjectKind,
            string factStreamToken,
            KnowledgeObservationPolicySnapshot observationPolicy,
            bool opinionEpisode,
            out List<SavedMemoryStateFact> normalized)
        {
            normalized = new List<SavedMemoryStateFact>();
            List<KnowledgeStateFact> plain = new List<KnowledgeStateFact>();
            for (int i = 0; source != null && i < source.Count; i++)
            {
                SavedMemoryStateFact fact = source[i];
                if (fact == null || fact.schemaVersion != 1) return false;
                plain.Add(new KnowledgeStateFact
                {
                    key = fact.factKey ?? string.Empty,
                    value = fact.factValue ?? string.Empty
                });
            }
            List<KnowledgeStateFact> canonical;
            bool valid = opinionEpisode
                ? KnowledgeRelationPolicy.TryNormalizeOpinionEpisodeFacts(
                    plain, observationPolicy, out canonical)
                : KnowledgeRelationPolicy.TryNormalizeStateFacts(
                    plain,
                    scopeKindToken,
                    subjectKind,
                    factStreamToken,
                    observationPolicy,
                    out canonical);
            if (!valid) return false;
            normalized = ToSavedFacts(canonical);
            return true;
        }

        private static bool IsExactAwarenessCapacityMarker(
            SavedMemoryAwarenessSnapshot row)
        {
            return row != null
                && row.trackingStateToken == KnowledgeObservationTokens.TrackingCapacityUntracked
                && row.knownnessEvidenceToken == KnowledgeObservationTokens.EvidenceRepairConflict
                && (row.stateFacts == null || row.stateFacts.Count == 0);
        }

        private static long CaptureGenerationForScope(
            MemoryPolicySnapshot policy,
            string scopeKindToken)
        {
            if (policy == null) return 0;
            if (scopeKindToken == KnowledgeObservationTokens.ScopeRelationship)
                return policy.CaptureGeneration(MemoryCategoryBits.Relationships);
            if (scopeKindToken == KnowledgeObservationTokens.ScopeRelative)
                return policy.CaptureGeneration(MemoryCategoryBits.Family);
            if (scopeKindToken == KnowledgeObservationTokens.ScopeFaction)
                return policy.CaptureGeneration(MemoryCategoryBits.Factions);
            return 0;
        }

        private static bool MergeAwarenessRepairMarker(
            PawnKnowledgeState state,
            SavedMemoryAwarenessSnapshot incoming,
            int cap)
        {
            if (state == null || incoming == null) return false;
            List<SavedMemoryAwarenessSnapshot> rows = state.ownerAwarenessSnapshots
                ?? new List<SavedMemoryAwarenessSnapshot>();
            for (int i = 0; i < rows.Count; i++)
            {
                SavedMemoryAwarenessSnapshot existing = rows[i];
                if (existing?.snapshotId != incoming.snapshotId) continue;
                if (IsExactAwarenessCapacityMarker(existing))
                {
                    incoming.firstObservedTick = MinimumNonnegative(
                        existing.firstObservedTick, incoming.firstObservedTick);
                    incoming.lastObservedTick = Math.Max(
                        existing.lastObservedTick, incoming.lastObservedTick);
                    incoming.snapshotRevision = Math.Max(
                        existing.snapshotRevision, incoming.snapshotRevision);
                }
                if (AwarenessRowsEqual(existing, incoming)) return false;
                rows[i] = incoming;
                state.ownerAwarenessSnapshots = rows;
                return true;
            }
            if (rows.Count >= cap) return false;
            rows.Add(incoming);
            state.ownerAwarenessSnapshots = rows;
            return true;
        }

        private static long MinimumNonnegative(long first, long second)
        {
            if (first < 0) return Math.Max(0, second);
            if (second < 0) return Math.Max(0, first);
            return Math.Min(first, second);
        }

        private static bool IsValidSavedFactionRow(
            SavedGlobalFactionSnapshot row,
            int labelCap)
        {
            if (row == null || row.schemaVersion != 1 || row.observedTick < 0
                || row.snapshotRevision < 0) return false;
            if (row.trackingStateToken == KnowledgeObservationTokens.TrackingCapacityUntracked)
            {
                return string.IsNullOrEmpty(row.factionDefName)
                    && string.IsNullOrEmpty(row.frozenDisplayLabel)
                    && row.goodwill == 0
                    && string.IsNullOrEmpty(row.relationKindToken)
                    && string.IsNullOrEmpty(row.leaderPawnId)
                    && !row.defeated
                    && !row.removed;
            }
            return row.trackingStateToken == KnowledgeObservationTokens.TrackingTracked
                && row.snapshotRevision > 0
                && row.snapshotRevision < long.MaxValue
                && row.goodwill >= -100 && row.goodwill <= 100
                && (row.factionDefName ?? string.Empty).Length
                    <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && (row.leaderPawnId ?? string.Empty).Length
                    <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && (row.frozenDisplayLabel ?? string.Empty).Length <= labelCap
                && MemoryIdentityCodec.IsWellFormedUtf16(row.factionDefName ?? string.Empty)
                && MemoryIdentityCodec.IsWellFormedUtf16(row.frozenDisplayLabel ?? string.Empty)
                && MemoryIdentityCodec.IsWellFormedUtf16(row.leaderPawnId ?? string.Empty)
                && KnowledgeRelationPolicy.IsKnownFactionRelationKind(
                    row.relationKindToken);
        }

        private static bool SameAwarenessOrder(
            List<SavedMemoryAwarenessSnapshot> first,
            List<SavedMemoryAwarenessSnapshot> second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
                if (!ReferenceEquals(first[i], second[i])) return false;
            return true;
        }

        private static bool SameEpisodeOrder(
            List<SavedMemoryCaptureEpisode> first,
            List<SavedMemoryCaptureEpisode> second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
                if (!ReferenceEquals(first[i], second[i])) return false;
            return true;
        }

        private static bool SameFactionOrder(
            List<SavedGlobalFactionSnapshot> first,
            List<SavedGlobalFactionSnapshot> second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
                if (!ReferenceEquals(first[i], second[i])) return false;
            return true;
        }

        private static bool SavedFactsEqual(
            List<SavedMemoryStateFact> first,
            List<SavedMemoryStateFact> second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null || first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
                if (first[i]?.factKey != second[i]?.factKey
                    || first[i]?.factValue != second[i]?.factValue) return false;
            return true;
        }
    }
}
