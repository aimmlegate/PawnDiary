// MemoryCurrentTruthSavedModels.cs — saved replaceable current-state rows
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.1.1 and the §T6.9 faction snapshot).
//
// Awareness snapshots and open capture episodes are bounded, versioned accumulators owned by the
// PawnKnowledgeState envelope; the global faction snapshot is component-owned current truth keyed
// by exact faction-instance/allocator-generation identity (never Def name or display label).
// Shape-only saved rows: no policy, no live game reads.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One versioned replaceable-current-state row for one exact fact stream (§T6.1.1). At most one
    /// row exists per canonical snapshotId key inside an owner envelope.
    /// </summary>
    public partial class SavedMemoryAwarenessSnapshot : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        /// <summary>Length-prefixed ("memory-awareness-v1", owner, epoch, scope, subject, stream).</summary>
        public string snapshotId = string.Empty;
        /// <summary>Exactly relationship | relative | faction | personal_status.</summary>
        public string scopeKindToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        /// <summary>One allowlisted current-truth stream token.</summary>
        public string factStreamToken = string.Empty;
        public long captureInvalidationGeneration;
        /// <summary>direct | captured | existing_news | repair_conflict.</summary>
        public string knownnessEvidenceToken = string.Empty;
        public List<SavedMemoryStateFact> stateFacts = new List<SavedMemoryStateFact>();
        public long firstObservedTick;
        public long lastObservedTick;
        public string lastSourceOccurrenceId = string.Empty;
        /// <summary>Exactly tracked | capacity_untracked.</summary>
        public string trackingStateToken = string.Empty;
        public long snapshotRevision;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref snapshotId, "snapshotId", string.Empty);
            Scribe_Values.Look(ref scopeKindToken, "scopeKindToken", string.Empty);
            Scribe_Values.Look(ref subjectKind, "subjectKind", string.Empty);
            Scribe_Values.Look(ref subjectId, "subjectId", string.Empty);
            Scribe_Values.Look(ref factStreamToken, "factStreamToken", string.Empty);
            Scribe_Values.Look(ref captureInvalidationGeneration,
                "captureInvalidationGeneration", 0);
            Scribe_Values.Look(ref knownnessEvidenceToken, "knownnessEvidenceToken", string.Empty);
            Scribe_Collections.Look(ref stateFacts, "stateFacts", LookMode.Deep);
            Scribe_Values.Look(ref firstObservedTick, "firstObservedTick", 0);
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick", 0);
            Scribe_Values.Look(ref lastSourceOccurrenceId, "lastSourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref trackingStateToken, "trackingStateToken", string.Empty);
            Scribe_Values.Look(ref snapshotRevision, "snapshotRevision", 0);
        }

        public void Normalize()
        {
            snapshotId = snapshotId ?? string.Empty;
            scopeKindToken = scopeKindToken ?? string.Empty;
            subjectKind = subjectKind ?? string.Empty;
            subjectId = subjectId ?? string.Empty;
            factStreamToken = factStreamToken ?? string.Empty;
            knownnessEvidenceToken = knownnessEvidenceToken ?? string.Empty;
            lastSourceOccurrenceId = lastSourceOccurrenceId ?? string.Empty;
            trackingStateToken = trackingStateToken ?? string.Empty;
            stateFacts = stateFacts ?? new List<SavedMemoryStateFact>();
            for (int i = stateFacts.Count - 1; i >= 0; i--)
            {
                if (stateFacts[i] == null)
                {
                    stateFacts.RemoveAt(i);
                    continue;
                }

                stateFacts[i].Normalize();
            }
        }
    }

    /// <summary>One deterministic open capture-episode accumulator (§T6.1.1). At most one open
    /// episode per exact episodeId key.</summary>
    public partial class SavedMemoryCaptureEpisode : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
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
        public List<SavedMemoryStateFact> baselineFacts = new List<SavedMemoryStateFact>();
        public List<SavedMemoryStateFact> currentFacts = new List<SavedMemoryStateFact>();
        public long firstObservedTick;
        public long lastObservedTick;
        public string lastSourceOccurrenceId = string.Empty;
        public long episodeRevision;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref episodeId, "episodeId", string.Empty);
            Scribe_Values.Look(ref captureRuleId, "captureRuleId", string.Empty);
            Scribe_Values.Look(ref scopeKindToken, "scopeKindToken", string.Empty);
            Scribe_Values.Look(ref factStreamToken, "factStreamToken", string.Empty);
            Scribe_Values.Look(ref category, "category", string.Empty);
            Scribe_Values.Look(ref captureInvalidationGeneration,
                "captureInvalidationGeneration", 0);
            Scribe_Values.Look(ref episodeKindToken, "episodeKindToken", string.Empty);
            Scribe_Values.Look(ref subjectKind, "subjectKind", string.Empty);
            Scribe_Values.Look(ref subjectId, "subjectId", string.Empty);
            Scribe_Values.Look(ref pairOrStreamKey, "pairOrStreamKey", string.Empty);
            Scribe_Values.Look(ref directionToken, "directionToken", string.Empty);
            Scribe_Collections.Look(ref baselineFacts, "baselineFacts", LookMode.Deep);
            Scribe_Collections.Look(ref currentFacts, "currentFacts", LookMode.Deep);
            Scribe_Values.Look(ref firstObservedTick, "firstObservedTick", 0);
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick", 0);
            Scribe_Values.Look(ref lastSourceOccurrenceId, "lastSourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref episodeRevision, "episodeRevision", 0);
        }

        public void Normalize()
        {
            episodeId = episodeId ?? string.Empty;
            captureRuleId = captureRuleId ?? string.Empty;
            scopeKindToken = scopeKindToken ?? string.Empty;
            factStreamToken = factStreamToken ?? string.Empty;
            category = category ?? string.Empty;
            episodeKindToken = episodeKindToken ?? string.Empty;
            subjectKind = subjectKind ?? string.Empty;
            subjectId = subjectId ?? string.Empty;
            pairOrStreamKey = pairOrStreamKey ?? string.Empty;
            directionToken = directionToken ?? string.Empty;
            lastSourceOccurrenceId = lastSourceOccurrenceId ?? string.Empty;
            baselineFacts = baselineFacts ?? new List<SavedMemoryStateFact>();
            currentFacts = currentFacts ?? new List<SavedMemoryStateFact>();
            NormalizeFactList(baselineFacts);
            NormalizeFactList(currentFacts);
        }

        private static void NormalizeFactList(List<SavedMemoryStateFact> facts)
        {
            for (int i = facts.Count - 1; i >= 0; i--)
            {
                if (facts[i] == null)
                {
                    facts.RemoveAt(i);
                    continue;
                }

                facts[i].Normalize();
            }
        }
    }

    /// <summary>
    /// One bounded component-global faction current-state snapshot (§T6.9). Its exact key is
    /// Seg("memory-faction-subject-v1") + Seg(factionInstanceId) + Seg(allocatorGeneration) — the
    /// same tuple every faction root/awareness primarySubjectId uses — so a reused load ID with a
    /// new allocator generation never merges with the old instance and neither Def name nor display
    /// label is ever identity.
    /// </summary>
    public partial class SavedGlobalFactionSnapshot : IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string factionInstanceId = string.Empty;
        public long allocatorGeneration;
        /// <summary>Diagnostic/display provenance only; never identity.</summary>
        public string factionDefName = string.Empty;
        public string frozenDisplayLabel = string.Empty;
        public int goodwill;
        public string relationKindToken = string.Empty;
        public string leaderPawnId = string.Empty;
        public bool defeated;
        public bool removed;
        public long observedTick;
        /// <summary>Exactly tracked | capacity_untracked.</summary>
        public string trackingStateToken = string.Empty;
        public long snapshotRevision;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref factionInstanceId, "factionInstanceId", string.Empty);
            Scribe_Values.Look(ref allocatorGeneration, "allocatorGeneration", 0);
            Scribe_Values.Look(ref factionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref frozenDisplayLabel, "frozenDisplayLabel", string.Empty);
            Scribe_Values.Look(ref goodwill, "goodwill", 0);
            Scribe_Values.Look(ref relationKindToken, "relationKindToken", string.Empty);
            Scribe_Values.Look(ref leaderPawnId, "leaderPawnId", string.Empty);
            Scribe_Values.Look(ref defeated, "defeated", false);
            Scribe_Values.Look(ref removed, "removed", false);
            Scribe_Values.Look(ref observedTick, "observedTick", 0);
            Scribe_Values.Look(ref trackingStateToken, "trackingStateToken", string.Empty);
            Scribe_Values.Look(ref snapshotRevision, "snapshotRevision", 0);
        }

        public void Normalize()
        {
            factionInstanceId = factionInstanceId ?? string.Empty;
            factionDefName = factionDefName ?? string.Empty;
            frozenDisplayLabel = frozenDisplayLabel ?? string.Empty;
            relationKindToken = relationKindToken ?? string.Empty;
            leaderPawnId = leaderPawnId ?? string.Empty;
            trackingStateToken = trackingStateToken ?? string.Empty;
        }
    }
}
