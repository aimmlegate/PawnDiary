// PawnKnowledgeState.cs — the persisted per-pawn knowledge state
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2.2, §4.1): origin/adopted culture plus the pawn's
// lifelong important-event records. Hangs off PawnDiaryRecord as a Scribe_Deep sub-object
// (mirroring beliefState), so it saves/loads with the diary and survives the pawn's death for
// resurrection.
//
// The record stores gameplay facts or the one player-authored background row plus an optional
// prompt/display prose override — never a generated diary entry or an LLM summary. Everything here
// is strings/scalars/bounded lists; no live Pawn/Def references are retained.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable"): ExposeData is called for BOTH save and load;
// Scribe_* mirrors each field to XML. PostLoadInit-style repair lives in Normalize(), called by
// the owning record after load.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One saved important-event memory record (§2.2).</summary>
    public class ImportantMemoryRecord : IExposable
    {
        public string recordId = string.Empty;
        public string dedupKey = string.Empty;
        public string sourceEventId = string.Empty;
        /// <summary>"captured" for gameplay rows or "player" for the profile background row.</summary>
        public string sourceKind = KnowledgeTokens.SourceKindCaptured;
        /// <summary>"contextual" for matched facts or "background" for the profile fallback.</summary>
        public string recallScope = KnowledgeTokens.RecallScopeContextual;
        /// <summary>Stable event-kind token from the matched DiaryImportantEventDef.</summary>
        public string eventKind = string.Empty;
        public string topicKey = string.Empty;
        public int tick;
        /// <summary>Localized game-date label captured with the record.</summary>
        public string dateLabel = string.Empty;
        /// <summary>Parallel lists: participant ids + saved display-name fallbacks.</summary>
        public List<string> participantIds = new List<string>();
        public List<string> participantNames = new List<string>();
        /// <summary>Exact subject/entity keys ("part:Heart", "title", …).</summary>
        public List<string> subjectKeys = new List<string>();
        /// <summary>Parallel lists: structured fact keys + localized display values.</summary>
        public List<string> factKeys = new List<string>();
        public List<string> factValues = new List<string>();
        /// <summary>Bounded capture-time summary used when the event Def is missing.</summary>
        public string fallbackSummary = string.Empty;
        /// <summary>
        /// Optional player/editor-authored replacement for the rendered memory line. Stable identity,
        /// matching keys, and structured facts remain untouched; only prompt/display prose changes.
        /// </summary>
        public string manualTextOverride = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref recordId, "id");
            Scribe_Values.Look(ref dedupKey, "dedup");
            Scribe_Values.Look(ref sourceEventId, "sourceEventId");
            Scribe_Values.Look(ref sourceKind, "sourceKind", KnowledgeTokens.SourceKindCaptured);
            Scribe_Values.Look(ref recallScope, "recallScope", KnowledgeTokens.RecallScopeContextual);
            Scribe_Values.Look(ref eventKind, "kind");
            Scribe_Values.Look(ref topicKey, "topic");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref dateLabel, "date");
            Scribe_Collections.Look(ref participantIds, "participantIds", LookMode.Value);
            Scribe_Collections.Look(ref participantNames, "participantNames", LookMode.Value);
            Scribe_Collections.Look(ref subjectKeys, "subjectKeys", LookMode.Value);
            Scribe_Collections.Look(ref factKeys, "factKeys", LookMode.Value);
            Scribe_Collections.Look(ref factValues, "factValues", LookMode.Value);
            Scribe_Values.Look(ref fallbackSummary, "fallback");
            Scribe_Values.Look(ref manualTextOverride, "manualTextOverride");
        }

        /// <summary>Repairs nulls and keeps the parallel lists aligned after a hand-edited save.</summary>
        public void Normalize()
        {
            recordId = recordId ?? string.Empty;
            dedupKey = dedupKey ?? string.Empty;
            sourceEventId = sourceEventId ?? string.Empty;
            sourceKind = PlayerMemoryPolicy.NormalizeSourceKind(sourceKind);
            recallScope = PlayerMemoryPolicy.NormalizeRecallScope(recallScope);
            eventKind = eventKind ?? string.Empty;
            topicKey = topicKey ?? string.Empty;
            dateLabel = dateLabel ?? string.Empty;
            fallbackSummary = fallbackSummary ?? string.Empty;
            manualTextOverride = manualTextOverride ?? string.Empty;
            participantIds = participantIds ?? new List<string>();
            participantNames = participantNames ?? new List<string>();
            subjectKeys = subjectKeys ?? new List<string>();
            factKeys = factKeys ?? new List<string>();
            factValues = factValues ?? new List<string>();
            AlignParallel(participantIds, participantNames);
            AlignParallel(factKeys, factValues);
        }

        private static void AlignParallel(List<string> keys, List<string> values)
        {
            while (values.Count < keys.Count)
            {
                values.Add(string.Empty);
            }

            while (values.Count > keys.Count)
            {
                values.RemoveAt(values.Count - 1);
            }
        }

        /// <summary>Copies the pure classifier draft into a savable record.</summary>
        internal static ImportantMemoryRecord FromSnapshot(ImportantMemoryRecordSnapshot snapshot)
        {
            ImportantMemoryRecord record = new ImportantMemoryRecord
            {
                recordId = snapshot.recordId ?? string.Empty,
                dedupKey = snapshot.dedupKey ?? string.Empty,
                sourceEventId = snapshot.sourceEventId ?? string.Empty,
                sourceKind = PlayerMemoryPolicy.NormalizeSourceKind(snapshot.sourceKind),
                recallScope = PlayerMemoryPolicy.NormalizeRecallScope(snapshot.recallScope),
                eventKind = snapshot.eventKind ?? string.Empty,
                topicKey = snapshot.topicKey ?? string.Empty,
                tick = snapshot.tick,
                dateLabel = snapshot.dateLabel ?? string.Empty,
                fallbackSummary = snapshot.fallbackSummary ?? string.Empty,
                manualTextOverride = snapshot.manualTextOverride ?? string.Empty
            };
            if (snapshot.participants != null)
            {
                for (int i = 0; i < snapshot.participants.Count; i++)
                {
                    KnowledgeParticipant participant = snapshot.participants[i];
                    if (participant != null && !string.IsNullOrWhiteSpace(participant.pawnId))
                    {
                        record.participantIds.Add(participant.pawnId);
                        record.participantNames.Add(participant.name ?? string.Empty);
                    }
                }
            }

            if (snapshot.subjectKeys != null)
            {
                record.subjectKeys.AddRange(snapshot.subjectKeys);
            }

            if (snapshot.facts != null)
            {
                for (int i = 0; i < snapshot.facts.Count; i++)
                {
                    KnowledgeFact fact = snapshot.facts[i];
                    if (fact != null && !string.IsNullOrWhiteSpace(fact.key))
                    {
                        record.factKeys.Add(fact.key);
                        record.factValues.Add(fact.value ?? string.Empty);
                    }
                }
            }

            return record;
        }

        /// <summary>Detached pure mirror for the selectors/renderers.</summary>
        internal ImportantMemoryRecordSnapshot ToSnapshot()
        {
            ImportantMemoryRecordSnapshot snapshot = new ImportantMemoryRecordSnapshot
            {
                recordId = recordId ?? string.Empty,
                dedupKey = dedupKey ?? string.Empty,
                ownerPawnId = string.Empty, // filled by the owning state
                sourceEventId = sourceEventId ?? string.Empty,
                sourceKind = PlayerMemoryPolicy.NormalizeSourceKind(sourceKind),
                recallScope = PlayerMemoryPolicy.NormalizeRecallScope(recallScope),
                eventKind = eventKind ?? string.Empty,
                topicKey = topicKey ?? string.Empty,
                tick = tick,
                dateLabel = dateLabel ?? string.Empty,
                fallbackSummary = fallbackSummary ?? string.Empty,
                manualTextOverride = manualTextOverride ?? string.Empty
            };
            // Read-only UI/profile snapshots must tolerate a hand-edited or partially loaded row
            // without calling Normalize(), because Normalize mutates saved state during IMGUI draw.
            List<string> safeParticipantIds = participantIds;
            List<string> safeParticipantNames = participantNames;
            for (int i = 0; safeParticipantIds != null && i < safeParticipantIds.Count; i++)
            {
                snapshot.participants.Add(new KnowledgeParticipant
                {
                    pawnId = safeParticipantIds[i] ?? string.Empty,
                    name = safeParticipantNames != null && i < safeParticipantNames.Count
                        ? (safeParticipantNames[i] ?? string.Empty)
                        : string.Empty
                });
            }

            if (subjectKeys != null)
            {
                snapshot.subjectKeys.AddRange(subjectKeys);
            }

            List<string> safeFactKeys = factKeys;
            List<string> safeFactValues = factValues;
            for (int i = 0; safeFactKeys != null && i < safeFactKeys.Count; i++)
            {
                snapshot.facts.Add(new KnowledgeFact
                {
                    key = safeFactKeys[i] ?? string.Empty,
                    value = safeFactValues != null && i < safeFactValues.Count
                        ? (safeFactValues[i] ?? string.Empty)
                        : string.Empty
                });
            }

            return snapshot;
        }
    }

    /// <summary>The per-pawn knowledge state (§4.1): culture provenance + important events.</summary>
    public class PawnKnowledgeState : IExposable
    {
        /// <summary>
        /// Current save schema for this state (memory plan §T6.1). Version 1 = the redesign's
        /// clean start; version 2 adds provenance/scope to each record; version 3 adds the unified
        /// memory envelope (epoch, standalone blocks, thread roots, background, awareness,
        /// episodes, guards, archive rows). Version 3 is the ONLY writable shape; loading 1/2 must
        /// stay legacy until component migration commits that owner atomically — Normalize() is
        /// therefore forbidden from bumping the saved version eagerly.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        public string pawnId = string.Empty;
        public int schemaVersion = CurrentSchemaVersion;
        public string originCultureDefName = string.Empty;
        /// <summary>"captured" or "inferred" (legacy saves); empty while unresolved.</summary>
        public string originCultureSource = string.Empty;
        public string adoptedCultureDefName = string.Empty;
        public List<ImportantMemoryRecord> records = new List<ImportantMemoryRecord>();

        // ---- Unified memory envelope (§T6.1). Additive tokens; old saves load defaults. ----

        /// <summary>Blank until this owner receives an autobiographical epoch through the checked
        /// allocator; never reused once issued.</summary>
        public string autobiographicalEpochToken = string.Empty;
        /// <summary>True only for an inert resolved-owner Imported envelope; mutually exclusive
        /// with epochFenceOnly.</summary>
        public bool archiveOnly;
        /// <summary>True only for an empty target epoch/cancellation fence envelope.</summary>
        public bool epochFenceOnly;
        /// <summary>Bumped before Brainwipe clears target data so old-epoch work fails closed.</summary>
        public long requestCancellationGeneration;
        /// <summary>Display-affecting structural mutations advance this checked revision.</summary>
        public long structuralRevision;
        /// <summary>Narrative inclusion/status changes advance this separate revision.</summary>
        public long statusRevision;
        /// <summary>One-based owner-local completed automatic diary-entry counter; starts at 1.</summary>
        public long completedDiaryEntryOrdinal;
        public List<SavedMemoryBlock> standaloneBlocks = new List<SavedMemoryBlock>();
        public List<SavedMemoryThreadRoot> threadRoots = new List<SavedMemoryThreadRoot>();
        /// <summary>The one player-authored background prose row (never a memory record).</summary>
        public string playerBackground = string.Empty;
        public List<SavedMemoryAwarenessSnapshot> ownerAwarenessSnapshots =
            new List<SavedMemoryAwarenessSnapshot>();
        public List<SavedMemoryCaptureEpisode> openCaptureEpisodes =
            new List<SavedMemoryCaptureEpisode>();
        public List<SavedMemoryRepetitionGuardRow> repetitionGuardRows =
            new List<SavedMemoryRepetitionGuardRow>();
        public List<SavedImportedMemoryRow> importedArchiveRows =
            new List<SavedImportedMemoryRow>();
        /// <summary>Nonnegative signed-64 diagnostic bitmask; unknown bits make state inert.</summary>
        public long migrationDiagnosticFlags;

        /// <summary>
        /// Creates a current-shape (version 3) envelope per §T6.0/T6.1 factory rules: positive
        /// current invariants start at 1 because zero means missing/invalid for these fields.
        /// </summary>
        public static PawnKnowledgeState CreateCurrent(string ownerPawnId)
        {
            return new PawnKnowledgeState
            {
                pawnId = ownerPawnId ?? string.Empty,
                schemaVersion = CurrentSchemaVersion,
                requestCancellationGeneration = 1,
                structuralRevision = 1,
                statusRevision = 1,
                completedDiaryEntryOrdinal = 1
            };
        }

        /// <summary>True when this envelope already carries the only writable current schema.</summary>
        public bool IsCurrentSchema()
        {
            return schemaVersion == CurrentSchemaVersion;
        }

        public void ExposeData()
        {
            // Keep the missing-key default pinned to the actual legacy schema. Using the current
            // value here would make an old save silently appear pre-migrated before Normalize().
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref originCultureDefName, "originCulture");
            Scribe_Values.Look(ref originCultureSource, "originCultureSource");
            Scribe_Values.Look(ref adoptedCultureDefName, "adoptedCulture");
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);

            // Unified-memory additive tokens (§T6.1). Missing keys on pre-feature saves read the
            // zero-value defaults; migration owns every semantic stamp.
            Scribe_Values.Look(
                ref autobiographicalEpochToken, "autobiographicalEpochToken", string.Empty);
            Scribe_Values.Look(ref archiveOnly, "archiveOnly", false);
            Scribe_Values.Look(ref epochFenceOnly, "epochFenceOnly", false);
            Scribe_Values.Look(
                ref requestCancellationGeneration, "requestCancellationGeneration", 0);
            Scribe_Values.Look(ref structuralRevision, "structuralRevision", 0);
            Scribe_Values.Look(ref statusRevision, "statusRevision", 0);
            Scribe_Values.Look(ref completedDiaryEntryOrdinal, "completedDiaryEntryOrdinal", 0);
            Scribe_Collections.Look(ref standaloneBlocks, "standaloneBlocks", LookMode.Deep);
            Scribe_Collections.Look(ref threadRoots, "threadRoots", LookMode.Deep);
            Scribe_Values.Look(ref playerBackground, "playerBackground", string.Empty);
            Scribe_Collections.Look(
                ref ownerAwarenessSnapshots, "ownerAwarenessSnapshots", LookMode.Deep);
            Scribe_Collections.Look(ref openCaptureEpisodes, "openCaptureEpisodes", LookMode.Deep);
            Scribe_Collections.Look(ref repetitionGuardRows, "repetitionGuardRows", LookMode.Deep);
            Scribe_Collections.Look(ref importedArchiveRows, "importedArchiveRows", LookMode.Deep);
            Scribe_Values.Look(ref migrationDiagnosticFlags, "migrationDiagnosticFlags", 0);
        }

        /// <summary>
        /// Load repair: null lists, per-record normalization, dedup-key uniqueness, and null-safe
        /// healing of the unified-memory collections. Deliberately does NOT change schemaVersion:
        /// §T6.1 forbids the old eager bump because a v1/v2 owner must stay wholly legacy and
        /// retryable until component migration swaps the complete owner state in one commit.
        /// </summary>
        public void Normalize()
        {
            pawnId = pawnId ?? string.Empty;
            originCultureDefName = originCultureDefName ?? string.Empty;
            originCultureSource = originCultureSource ?? string.Empty;
            adoptedCultureDefName = adoptedCultureDefName ?? string.Empty;
            autobiographicalEpochToken = autobiographicalEpochToken ?? string.Empty;
            playerBackground = playerBackground ?? string.Empty;
            records = records ?? new List<ImportantMemoryRecord>();
            HashSet<string> seen = new HashSet<string>();
            for (int i = records.Count - 1; i >= 0; i--)
            {
                ImportantMemoryRecord record = records[i];
                if (record == null)
                {
                    records.RemoveAt(i);
                    continue;
                }

                record.Normalize();
                if (string.IsNullOrWhiteSpace(record.recordId) || !seen.Add(record.dedupKey))
                {
                    records.RemoveAt(i);
                }
            }

            NormalizeList(standaloneBlocks, row => row.Normalize());
            NormalizeList(threadRoots, row => row.Normalize());
            NormalizeList(ownerAwarenessSnapshots, row => row.Normalize());
            NormalizeList(openCaptureEpisodes, row => row.Normalize());
            NormalizeList(repetitionGuardRows, row => row.Normalize());
            NormalizeList(importedArchiveRows, row => row.Normalize());
        }

        private static void NormalizeList<T>(List<T> rows, Action<T> repair) where T : class
        {
            if (rows == null)
            {
                return;
            }

            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i] == null)
                {
                    rows.RemoveAt(i);
                    continue;
                }

                repair(rows[i]);
            }
        }

        /// <summary>True when a record with this dedup key already exists (§2.2).</summary>
        public bool HasDedupKey(string dedupKey)
        {
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null
                    && string.Equals(records[i].dedupKey, dedupKey, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when any durable record has the supplied stable event-kind token.</summary>
        public bool HasEventKind(string eventKind)
        {
            return FirstEventKindTick(eventKind).HasValue;
        }

        /// <summary>
        /// Returns the earliest captured tick for one stable event-kind token. Arrival lifecycle code
        /// uses this when the player disabled the visible arrival page but durable knowledge still owns
        /// the truthful joining boundary.
        /// </summary>
        public int? FirstEventKindTick(string eventKind)
        {
            if (string.IsNullOrWhiteSpace(eventKind) || records == null)
            {
                return null;
            }

            int? firstTick = null;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null
                    && string.Equals(
                        records[i].eventKind,
                        eventKind,
                        System.StringComparison.Ordinal))
                {
                    int tick = records[i].tick;
                    if (!firstTick.HasValue || tick < firstTick.Value)
                    {
                        firstTick = tick;
                    }
                }
            }

            return firstTick;
        }

        /// <summary>Pure culture mirror for the resolver/annotation planner.</summary>
        internal CultureStateSnapshot ToCultureSnapshot()
        {
            return new CultureStateSnapshot
            {
                originCultureDefName = originCultureDefName ?? string.Empty,
                originSource = originCultureSource ?? string.Empty,
                adoptedCultureDefName = adoptedCultureDefName ?? string.Empty
            };
        }

        /// <summary>Pure record mirrors with the owner id stamped on.</summary>
        internal List<ImportantMemoryRecordSnapshot> ToRecordSnapshots()
        {
            List<ImportantMemoryRecordSnapshot> snapshots = new List<ImportantMemoryRecordSnapshot>(
                records?.Count ?? 0);
            for (int i = 0; records != null && i < records.Count; i++)
            {
                if (records[i] == null)
                {
                    continue;
                }

                ImportantMemoryRecordSnapshot snapshot = records[i].ToSnapshot();
                snapshot.ownerPawnId = pawnId ?? string.Empty;
                snapshots.Add(snapshot);
            }

            return snapshots;
        }
    }
}
