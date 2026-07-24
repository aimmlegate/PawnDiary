// PawnKnowledgeState.cs — the persisted per-pawn knowledge state
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2.2, §4.1): origin/adopted culture plus the pawn's
// lifelong important-event records. Hangs off PawnDiaryRecord as a Scribe_Deep sub-object
// (mirroring beliefState), so it saves/loads with the diary and survives the pawn's death for
// resurrection.
//
// The record stores gameplay facts only — never a generated diary entry or an LLM summary.
// Everything here is strings/scalars/bounded lists; no live Pawn/Def references are retained.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable"): ExposeData is called for BOTH save and load;
// Scribe_* mirrors each field to XML. PostLoadInit-style repair lives in Normalize(), called by
// the owning record after load.
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref recordId, "id");
            Scribe_Values.Look(ref dedupKey, "dedup");
            Scribe_Values.Look(ref sourceEventId, "sourceEventId");
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
        }

        /// <summary>Repairs nulls and keeps the parallel lists aligned after a hand-edited save.</summary>
        public void Normalize()
        {
            recordId = recordId ?? string.Empty;
            dedupKey = dedupKey ?? string.Empty;
            sourceEventId = sourceEventId ?? string.Empty;
            eventKind = eventKind ?? string.Empty;
            topicKey = topicKey ?? string.Empty;
            dateLabel = dateLabel ?? string.Empty;
            fallbackSummary = fallbackSummary ?? string.Empty;
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
                eventKind = snapshot.eventKind ?? string.Empty,
                topicKey = snapshot.topicKey ?? string.Empty,
                tick = snapshot.tick,
                dateLabel = snapshot.dateLabel ?? string.Empty,
                fallbackSummary = snapshot.fallbackSummary ?? string.Empty
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
                eventKind = eventKind ?? string.Empty,
                topicKey = topicKey ?? string.Empty,
                tick = tick,
                dateLabel = dateLabel ?? string.Empty,
                fallbackSummary = fallbackSummary ?? string.Empty
            };
            for (int i = 0; i < participantIds.Count; i++)
            {
                snapshot.participants.Add(new KnowledgeParticipant
                {
                    pawnId = participantIds[i] ?? string.Empty,
                    name = i < participantNames.Count ? (participantNames[i] ?? string.Empty) : string.Empty
                });
            }

            snapshot.subjectKeys.AddRange(subjectKeys);
            for (int i = 0; i < factKeys.Count; i++)
            {
                snapshot.facts.Add(new KnowledgeFact
                {
                    key = factKeys[i] ?? string.Empty,
                    value = i < factValues.Count ? (factValues[i] ?? string.Empty) : string.Empty
                });
            }

            return snapshot;
        }
    }

    /// <summary>The per-pawn knowledge state (§4.1): culture provenance + important events.</summary>
    public class PawnKnowledgeState : IExposable
    {
        /// <summary>Current save schema for this state. Version 1 = the redesign's clean start;
        /// old associative fragments are never migrated in (§6).</summary>
        public const int CurrentSchemaVersion = 1;

        public string pawnId = string.Empty;
        public int schemaVersion = CurrentSchemaVersion;
        public string originCultureDefName = string.Empty;
        /// <summary>"captured" or "inferred" (legacy saves); empty while unresolved.</summary>
        public string originCultureSource = string.Empty;
        public string adoptedCultureDefName = string.Empty;
        public List<ImportantMemoryRecord> records = new List<ImportantMemoryRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", CurrentSchemaVersion);
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref originCultureDefName, "originCulture");
            Scribe_Values.Look(ref originCultureSource, "originCultureSource");
            Scribe_Values.Look(ref adoptedCultureDefName, "adoptedCulture");
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
        }

        /// <summary>Load repair: null lists, per-record normalization, and dedup-key uniqueness
        /// (a hand-edited or interrupted save must not double a record).</summary>
        public void Normalize()
        {
            pawnId = pawnId ?? string.Empty;
            originCultureDefName = originCultureDefName ?? string.Empty;
            originCultureSource = originCultureSource ?? string.Empty;
            adoptedCultureDefName = adoptedCultureDefName ?? string.Empty;
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

            if (schemaVersion < CurrentSchemaVersion)
            {
                schemaVersion = CurrentSchemaVersion;
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
            List<ImportantMemoryRecordSnapshot> snapshots = new List<ImportantMemoryRecordSnapshot>(records.Count);
            for (int i = 0; i < records.Count; i++)
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
