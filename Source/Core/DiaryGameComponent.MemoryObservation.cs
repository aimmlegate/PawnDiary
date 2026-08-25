// DiaryGameComponent.MemoryObservation.cs — Phase M6's impure shadow-current-truth adapter.
// It observes live RimWorld pawns/factions only on the main thread, converts them immediately to
// detached policy inputs, and commits bounded SavedMemoryAwarenessSnapshot/open-episode/global-
// faction rows. It never creates a DiaryEvent, prompt fact, reflection, or LLM request.
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
            public readonly Dictionary<string, MemoryOwnerByteTotals> owners =
                new Dictionary<string, MemoryOwnerByteTotals>(StringComparer.Ordinal);
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
        private readonly HashSet<string> memoryObservationFullScanUnseenStartingOwnerIds =
            new HashSet<string>(StringComparer.Ordinal);
        private bool memoryObservationFullScanRequested;
        private bool memoryObservationFullScanSilent;
        private bool memoryObservationFinishFullAfterQueue;
        private bool memoryObservationBootstrapPending;
        private bool memoryObservationFullFactionsComplete;
        private string memoryObservationFullFactionAfterId = string.Empty;
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

            int cap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            SortedDictionary<string, Pawn> related =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            if (pawn.relations != null)
            {
                foreach (Pawn other in pawn.relations.RelatedPawns)
                    OfferBoundedMemoryObservationCandidate(related, pawn, other, cap, null);
            }
            foreach (KeyValuePair<string, Pawn> pair in related)
            {
                OfferMemoryObservationWork(DirectedPairWork(pawn, pair.Value, false));
                OfferMemoryObservationWork(DirectedPairWork(pair.Value, pawn, false));
            }
        }

        /// <summary>
        /// Runs at the start of the existing social-reflection tick seam. Work is elapsed-time driven,
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
                if (memoryObservationFinishFullAfterQueue)
                {
                    MemoryObservationBudgetSession emptySession =
                        CreateMemoryObservationBudgetSession();
                    FinishMemoryObservationFullScan(now, emptySession);
                }
                CompleteMemoryObservationTick();
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
            CompleteMemoryObservationTick();
        }

        private void CompleteMemoryObservationTick()
        {
            if (memoryObservationMutatedThisTick)
            {
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
                ObserveGlobalFaction(work.faction, work.removedFaction, now, silent, budget);
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
            List<string> outbound = DirectRelationDefNames(owner, subject);
            List<string> inbound = DirectRelationDefNames(subject, owner);
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

            PawnKnowledgeState state = EnsureMemoryObservationOwner(owner, budget);
            if (state == null) return;
            string snapshotId;
            if (!KnowledgeRelationPolicy.TryCreateAwarenessId(
                    ownerId,
                    state.autobiographicalEpochToken,
                    KnowledgeObservationTokens.ScopeRelationship,
                    KnowledgeObservationTokens.SubjectPawn,
                    subjectId,
                    KnowledgeObservationTokens.StreamDirectedSocial,
                    out snapshotId)) return;
            SavedMemoryAwarenessSnapshot previousSaved =
                FindAwarenessSnapshot(state, snapshotId);
            string pairKey;
            KnowledgeRelationPolicy.TryCreateDirectedPairKey(ownerId, subjectId, out pairKey);
            SavedMemoryCaptureEpisode priorEpisode = FindOpinionEpisode(state, pairKey);
            long generation = capturePolicy.CaptureGeneration(MemoryCategoryBits.Relationships);
            KnowledgeOpinionPlan plan = KnowledgeRelationPolicy.PlanDirectedOpinion(
                ToPlainAwareness(previousSaved),
                ToPlainOpinionEpisode(priorEpisode),
                new KnowledgeOpinionObservation
                {
                    ownerPawnId = ownerId,
                    ownerEpochToken = state.autobiographicalEpochToken,
                    subjectPawnId = subjectId,
                    opinion = opinion,
                    outboundRelationDefNames = outbound,
                    inboundRelationDefNames = inbound,
                    captureInvalidationGeneration = generation,
                    observedTick = now,
                    captureAllowed = capturePolicy.AllowsCapture(
                        MemoryCategoryBits.Relationships),
                    forceSilentBaseline = forceSilent
                },
                bands,
                observationPolicy);
            if (plan.valid && plan.savedMutationRequired)
            {
                // Intentionally ignore plan.qualifiedForFutureCapture: M6 owns shadow state only.
                ApplyMemoryAwarenessPlan(state, plan.replacement, plan.openEpisode,
                    pairKey, budget);
            }

            bool family = HasVisibleFamilyRelation(owner, subject);
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
            KnowledgeAwarenessPlan relativePlan = KnowledgeRelationPolicy.PlanCurrentTruth(
                FindPlainAwareness(state,
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.StreamRelativeState,
                    subjectId),
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = ownerId,
                    ownerEpochToken = state.autobiographicalEpochToken,
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
                            MemoryRelativeLocation(subject)),
                        NewObservationFact(KnowledgeObservationTokens.FactFactionSubject,
                            factionSubject.Length == 0 ? "none" : factionSubject),
                        NewObservationFact(KnowledgeObservationTokens.FactRelationDefs, relationSet)
                    },
                    observedTick = now,
                    captureAllowed = capturePolicy.AllowsCapture(MemoryCategoryBits.Family),
                    forceSilentBaseline = forceSilent
                },
                observationPolicy);
            if (relativePlan.valid)
            {
                if (relativePlan.savedMutationRequired)
                    ApplyMemoryAwarenessPlan(state, relativePlan.replacement, null, null, budget);
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
            PawnKnowledgeState state = EnsureMemoryObservationOwner(owner, budget);
            if (state == null) return;
            string factionSubject = FactionSubjectIdFor(faction);
            if (factionSubject.Length == 0) return;
            KnowledgeAwarenessState previous = FindPlainAwareness(state,
                KnowledgeObservationTokens.ScopeFaction,
                KnowledgeObservationTokens.StreamFactionConnection,
                factionSubject);
            string effectiveConnection = KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                FactValue(previous?.stateFacts,
                    KnowledgeObservationTokens.FactConnectionKind),
                connectionKind);
            KnowledgeAwarenessPlan plan = KnowledgeRelationPolicy.PlanCurrentTruth(
                previous,
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = SafePawnId(owner),
                    ownerEpochToken = state.autobiographicalEpochToken,
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
                },
                observationPolicy);
            if (plan.valid && plan.savedMutationRequired)
                ApplyMemoryAwarenessPlan(state, plan.replacement, null, null, budget);
        }

        private void ObserveGlobalFaction(
            Faction faction,
            bool removed,
            int now,
            bool forceSilent,
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
            KnowledgeFactionPlan plan = KnowledgeRelationPolicy.PlanFactionSnapshot(
                ToPlainFaction(previous),
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
                    forceSilentBaseline = forceSilent,
                    maximumFrozenDisplayLabelCharacters = labelCap
                });
            if (plan.valid && plan.savedMutationRequired
                && ApplyGlobalFactionPlan(plan.replacement, budget)
                && allocatedGeneration)
            {
                // Allocation and its first exact-key snapshot publish together. A cap refusal does
                // not consume a generation and cannot grow the high-water on every reconciliation.
                globalFactionSnapshotAllocatorGeneration = allocatorGeneration;
            }
        }

        private void FinishMemoryObservationFullScan(
            int now,
            MemoryObservationBudgetSession budget)
        {
            List<SavedGlobalFactionSnapshot> missing = new List<SavedGlobalFactionSnapshot>();
            for (int i = 0; i < globalFactionSnapshots.Count; i++)
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
                        forceSilentBaseline = memoryObservationFullScanSilent,
                        maximumFrozenDisplayLabelCharacters = (int)ReadCapacityLong(
                            "frozenDisplayLabelUnits", 80, 320)
                    });
                if (plan.valid && plan.savedMutationRequired)
                    ApplyGlobalFactionPlan(plan.replacement, budget);
            }

            foreach (string ownerId in memoryObservationFullScanUnseenStartingOwnerIds)
            {
                memoryObservationAttachedOwnerIds.Remove(ownerId);
                memoryObservationOwnersNeedingSilentBaseline.Remove(ownerId);
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
            if (!memoryObservationFullFactionsComplete)
            {
                List<Faction> factions = new List<Faction>();
                IEnumerable<Faction> allFactions = Find.FactionManager?.AllFactions;
                if (allFactions != null)
                {
                    foreach (Faction faction in allFactions)
                        if (faction != null && SafeFactionId(faction).Length > 0)
                            factions.Add(faction);
                }
                factions.Sort((left, right) => string.CompareOrdinal(
                    SafeFactionId(left), SafeFactionId(right)));
                for (int i = 0; i < factions.Count; i++)
                {
                    Faction faction = factions[i];
                    string factionId = SafeFactionId(faction);
                    if (string.CompareOrdinal(
                            factionId, memoryObservationFullFactionAfterId) <= 0) continue;
                    memoryObservationFullFactionAfterId = factionId;
                    memoryObservationSeenFactionIds.Add(factionId);
                    if (QueueFullMemoryObservationWork(
                            FactionWork(faction, false, memoryObservationFullScanSilent),
                            queueCap)) return;
                }
                memoryObservationFullFactionsComplete = true;
                memoryObservationFullFactionAfterId = string.Empty;
            }

            List<Pawn> owners = SnapshotFreeColonists();
            owners.RemoveAll(pawn => pawn == null || !IsDiaryEligible(pawn)
                || SafePawnId(pawn).Length == 0);
            owners.Sort((left, right) => string.CompareOrdinal(
                SafePawnId(left), SafePawnId(right)));
            int candidateCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            while (true)
            {
                Pawn owner = FindFullScanOwner(owners);
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

                SortedDictionary<string, Pawn> candidates =
                    FullMemoryObservationCandidates(owner, candidateCap);
                foreach (KeyValuePair<string, Pawn> candidate in candidates)
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
            }

            memoryObservationFullScanRequested = false;
            memoryObservationFinishFullAfterQueue = true;
            ResetMemoryObservationFullCursor();
        }

        private Pawn FindFullScanOwner(List<Pawn> owners)
        {
            if (!string.IsNullOrEmpty(memoryObservationFullCurrentOwnerId))
            {
                for (int i = 0; owners != null && i < owners.Count; i++)
                    if (SafePawnId(owners[i]) == memoryObservationFullCurrentOwnerId)
                        return owners[i];

                // The current owner left the eligible set between pages. Its exact dirty hooks or
                // the next periodic pass will settle it; continue after its stable id without rewind.
                memoryObservationFullOwnerAfterId = memoryObservationFullCurrentOwnerId;
                memoryObservationFullCurrentOwnerId = string.Empty;
                memoryObservationFullOwnerFactionDone = false;
                memoryObservationFullCandidateAfterId = string.Empty;
            }

            for (int i = 0; owners != null && i < owners.Count; i++)
            {
                string ownerId = SafePawnId(owners[i]);
                if (string.CompareOrdinal(ownerId, memoryObservationFullOwnerAfterId) <= 0) continue;
                memoryObservationFullCurrentOwnerId = ownerId;
                return owners[i];
            }
            return null;
        }

        private void ResetMemoryObservationFullCursor()
        {
            memoryObservationFullFactionsComplete = false;
            memoryObservationFullFactionAfterId = string.Empty;
            memoryObservationFullOwnerAfterId = string.Empty;
            memoryObservationFullCurrentOwnerId = string.Empty;
            memoryObservationFullOwnerFactionDone = false;
            memoryObservationFullCandidateAfterId = string.Empty;
        }

        private SortedDictionary<string, Pawn> FullMemoryObservationCandidates(
            Pawn owner,
            int cap)
        {
            SortedDictionary<string, Pawn> result =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            if (owner?.relations != null)
            {
                foreach (Pawn related in owner.relations.RelatedPawns)
                    OfferBoundedMemoryObservationCandidate(
                        result, owner, related, cap, null);
            }

            PawnKnowledgeState state = FindDiaryByPawnId(SafePawnId(owner))?.KnowledgeStateOrNull();
            if (result.Count < cap)
            {
                SortedDictionary<string, Pawn> savedCandidates =
                    ResolveSavedMemoryObservationCandidates(state, cap);
                foreach (KeyValuePair<string, Pawn> candidate in savedCandidates)
                {
                    if (result.Count >= cap) break;
                    AddMemoryObservationCandidate(result, owner, candidate.Value);
                }
            }

            int remaining = cap - result.Count;
            SortedDictionary<string, Pawn> socialCandidates =
                new SortedDictionary<string, Pawn>(StringComparer.Ordinal);
            foreach (Pawn candidate in MemorySocialContextPawns(owner))
            {
                if (IsCurrentMemorySocialCandidate(owner, candidate))
                    OfferBoundedMemoryObservationCandidate(
                        socialCandidates, owner, candidate, remaining, result);
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
        private static SortedDictionary<string, Pawn> ResolveSavedMemoryObservationCandidates(
            PawnKnowledgeState state,
            int cap)
        {
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
            if (requested.Count == 0) return found;
            if (Find.Maps != null)
            {
                for (int mapIndex = 0;
                    mapIndex < Find.Maps.Count && found.Count < requested.Count;
                    mapIndex++)
                {
                    IndexRequestedObservedPawns(
                        Find.Maps[mapIndex]?.mapPawns?.AllPawns, requested, found);
                }
            }
            IndexRequestedObservedPawns(
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive,
                requested,
                found);
            IndexRequestedObservedPawns(
                Find.WorldPawns?.AllPawnsAliveOrDead,
                requested,
                found);
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

        private static void IndexRequestedObservedPawns(
            IEnumerable<Pawn> source,
            SortedSet<string> requested,
            SortedDictionary<string, Pawn> found)
        {
            if (source == null || requested == null || found == null
                || found.Count >= requested.Count) return;
            foreach (Pawn pawn in source)
            {
                if (found.Count >= requested.Count) break;
                string id = SafePawnId(pawn);
                if (requested.Contains(id) && !found.ContainsKey(id)) found.Add(id, pawn);
            }
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

        private PawnKnowledgeState EnsureMemoryObservationOwner(
            Pawn owner,
            MemoryObservationBudgetSession budget)
        {
            if (owner == null || !IsDiaryEligible(owner)) return null;
            string ownerId = SafePawnId(owner);
            PawnDiaryRecord diary = FindDiaryByPawnId(ownerId);
            if (diary == null) return null;
            bool created = diary.knowledgeState == null;
            PawnKnowledgeState state = EnsureCurrentMemoryEnvelope(diary);
            if (state == null || !string.Equals(state.pawnId, ownerId, StringComparison.Ordinal))
                return null;
            bool ignoredFallback;
            if (!string.IsNullOrEmpty(state.autobiographicalEpochToken))
            {
                return MemoryIdentityCodec.TryValidateEpochToken(
                    state.autobiographicalEpochToken, out ignoredFallback) ? state : null;
            }
            if (state.structuralRevision == long.MaxValue
                || state.statusRevision == long.MaxValue) return null;

            CollectAndPublishAllocatorCarriers();
            MemoryEpochAllocationPlan allocation = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = ownerId,
                    lastIssuedSequence = lastIssuedAutobiographicalEpochSequence,
                    fallbackChain = lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                    liveEpochCarriers = SnapshotAutobiographicalEpochCarriers(),
                    isTargetBrainwipe = false
                });
            if (!allocation.canMutate) return null;
            lastIssuedAutobiographicalEpochSequence = allocation.nextSequence;
            lastIssuedAutobiographicalEpochFallbackChain =
                allocation.nextFallbackChain ?? string.Empty;
            state.autobiographicalEpochToken = allocation.epochToken;
            state.epochFenceOnly = false;
            state.structuralRevision = AdvanceMemoryObservationRevision(state.structuralRevision);
            state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
            PawnReflectionState reflection = diary.EnsureReflectionState();
            reflection.memoryReflectionSchemaVersion = 1;
            reflection.memoryOwnerEpochToken = allocation.epochToken;
            memoryObservationMutatedThisTick = true;
            if (created) state.requestCancellationGeneration = Math.Max(
                1, state.requestCancellationGeneration);
            RefreshMemoryObservationBudgetSession(budget);
            return state;
        }

        private bool ApplyMemoryAwarenessPlan(
            PawnKnowledgeState state,
            KnowledgeAwarenessState replacement,
            KnowledgeOpinionEpisodeState desiredEpisode,
            string opinionPairKey,
            MemoryObservationBudgetSession budget)
        {
            if (state == null || replacement == null
                || state.statusRevision == long.MaxValue) return false;
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
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
            else
            {
                RemoveOpinionEpisodeForPair(state, opinionPairKey, budget);
                return false;
            }

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

            if (!TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget))
            {
                KnowledgeAwarenessState marker = CapacityMarker(replacement);
                for (int i = 0; i < awareness.Count; i++)
                    if (awareness[i]?.snapshotId == marker.snapshotId)
                        awareness[i] = ToSavedAwareness(marker);
                if (!string.IsNullOrEmpty(opinionPairKey))
                    episodes.RemoveAll(row => row != null
                        && row.pairOrStreamKey == opinionPairKey);
                if (!TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget))
                    return false;
            }

            state.ownerAwarenessSnapshots = awareness;
            state.openCaptureEpisodes = episodes;
            state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
            memoryObservationMutatedThisTick = true;
            return true;
        }

        private bool TryAdmitMemoryObservationOwnerLists(
            PawnKnowledgeState state,
            List<SavedMemoryAwarenessSnapshot> awareness,
            List<SavedMemoryCaptureEpisode> episodes,
            MemoryObservationBudgetSession budget)
        {
            MemoryLogicalSizeResult oldAwareness = SizeListValidated(state.ownerAwarenessSnapshots);
            MemoryLogicalSizeResult newAwareness = SizeListValidated(awareness);
            MemoryLogicalSizeResult oldEpisodes = SizeListValidated(state.openCaptureEpisodes);
            MemoryLogicalSizeResult newEpisodes = SizeListValidated(episodes);
            if (!oldAwareness.valid || !newAwareness.valid
                || !oldEpisodes.valid || !newEpisodes.valid) return false;
            long delta;
            try
            {
                delta = checked(newAwareness.totalBytes + newEpisodes.totalBytes
                    - oldAwareness.totalBytes - oldEpisodes.totalBytes);
            }
            catch (OverflowException)
            {
                return false;
            }
            MemoryOwnerByteTotals ownerTotals;
            if (!budget.owners.TryGetValue(state.pawnId ?? string.Empty, out ownerTotals)
                || !ownerTotals.valid)
            {
                RefreshMemoryObservationBudgetSession(budget);
                if (!budget.owners.TryGetValue(state.pawnId ?? string.Empty, out ownerTotals)
                    || !ownerTotals.valid) return false;
            }
            MemoryBudgetDecision decision = ActiveMemoryPayloadBudget.TryAdmit(
                budget.limits,
                ownerTotals.activeBytes,
                ownerTotals.importedBytes,
                delta,
                0,
                budget.global);
            if (decision.outcome != MemoryBudgetOutcome.Admitted) return false;
            budget.global = decision.newTotals;
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
                long active = checked(budget.global.globalActiveBytes + delta);
                long combined = checked(active + budget.global.globalImportedBytes);
                if (active < 0 || active > budget.limits.activeGlobalBytes
                    || combined < 0 || combined > budget.limits.combinedGlobalBytes) return false;
                budget.global = new MemoryPayloadBudgetTotals
                {
                    globalActiveBytes = active,
                    globalImportedBytes = budget.global.globalImportedBytes
                };
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private MemoryObservationBudgetSession CreateMemoryObservationBudgetSession()
        {
            RebuildMemorySizeIndexes();
            MemoryObservationBudgetSession result = new MemoryObservationBudgetSession
            {
                limits = new MemoryBudgetLimits
                {
                    activeOwnerBytes = ReadCapacityLong("activeOwnerBytes", 196608, 2097152),
                    combinedOwnerBytes = ReadCapacityLong("combinedOwnerBytes", 262144, 4194304),
                    activeGlobalBytes = ReadCapacityLong("activeGlobalBytes", 6291456, 25165824),
                    combinedGlobalBytes = ReadCapacityLong("combinedGlobalBytes", 8388608, 33554432)
                }
            };
            PublishMemoryObservationBudgetTotals(result);
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
        }

        private void RemoveHiddenMemoryObservation(
            string ownerId,
            string subjectId,
            MemoryObservationBudgetSession budget)
        {
            PawnKnowledgeState state = FindCurrentMemoryEnvelope(ownerId);
            if (state == null || state.statusRevision == long.MaxValue) return;
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
            if (state == null || state.statusRevision == long.MaxValue) return;
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
            if (state?.ownerAwarenessSnapshots == null
                || state.statusRevision == long.MaxValue) return;
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

        private void RemoveOpinionEpisodeForPair(
            PawnKnowledgeState state,
            string pairKey,
            MemoryObservationBudgetSession budget)
        {
            if (state == null || state.statusRevision == long.MaxValue
                || string.IsNullOrEmpty(pairKey)) return;
            List<SavedMemoryCaptureEpisode> episodes =
                new List<SavedMemoryCaptureEpisode>(state.openCaptureEpisodes);
            if (episodes.RemoveAll(row => row != null && row.pairOrStreamKey == pairKey) == 0)
                return;
            List<SavedMemoryAwarenessSnapshot> awareness =
                new List<SavedMemoryAwarenessSnapshot>(state.ownerAwarenessSnapshots);
            if (TryAdmitMemoryObservationOwnerLists(state, awareness, episodes, budget))
            {
                state.openCaptureEpisodes = episodes;
                state.statusRevision = AdvanceMemoryObservationRevision(state.statusRevision);
                memoryObservationMutatedThisTick = true;
            }
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

        private static List<string> DirectRelationDefNames(Pawn owner, Pawn subject)
        {
            List<string> result = new List<string>();
            if (owner == null || subject == null) return result;
            foreach (PawnRelationDef relation in owner.GetRelations(subject))
            {
                if (relation != null && !string.IsNullOrWhiteSpace(relation.defName))
                    result.Add(relation.defName);
            }
            return result;
        }

        private static bool HasVisibleFamilyRelation(Pawn owner, Pawn subject)
        {
            return HasFamilyRelationOneWay(owner, subject)
                || HasFamilyRelationOneWay(subject, owner);
        }

        private static bool HasFamilyRelationOneWay(Pawn owner, Pawn subject)
        {
            if (owner == null || subject == null) return false;
            foreach (PawnRelationDef relation in owner.GetRelations(subject))
            {
                if (relation != null
                    && (relation.familyByBloodRelation || relation == PawnRelationDefOf.Spouse))
                    return true;
            }
            return false;
        }

        private static string MemoryRelativeLocation(Pawn pawn)
        {
            if (pawn == null) return KnowledgeObservationTokens.LocationUnknown;
            if (pawn.Spawned && pawn.Map != null)
                return OrdinalSegmentCodec.Segment("map")
                    + OrdinalSegmentCodec.Segment(
                        pawn.Map.uniqueID.ToString(CultureInfo.InvariantCulture));
            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
                return OrdinalSegmentCodec.Segment("caravan")
                    + OrdinalSegmentCodec.Segment(caravan.GetUniqueLoadID());
            if (pawn.Dead && pawn.Corpse?.Spawned == true && pawn.Corpse.Map != null)
                return OrdinalSegmentCodec.Segment("corpse_map")
                    + OrdinalSegmentCodec.Segment(
                        pawn.Corpse.Map.uniqueID.ToString(CultureInfo.InvariantCulture));
            if (Find.WorldPawns?.Contains(pawn) == true)
                return KnowledgeObservationTokens.LocationWorld;
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
                changed |= NormalizeOwnerAwarenessRows(
                    state, awarenessCap, observationPolicy, capturePolicy);
                changed |= NormalizeOwnerEpisodeRows(
                    state, awarenessCap, episodeCap, observationPolicy, capturePolicy);
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
