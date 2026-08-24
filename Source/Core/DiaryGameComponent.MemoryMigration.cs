// DiaryGameComponent.MemoryMigration.cs — impure adapter for the M1 dry-run migration report
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md phase M1 item 4, §T13.1 shape preservation).
//
// REPORT MODE ONLY while MemorySystemActivationGate is LegacyShadow: this partial extracts plain
// legacy snapshots from loaded v1/v2 knowledge envelopes plus the shipped capture-Def catalog,
// feeds them to the pure MemoryThreadMigrationPolicy.PlanDryRun planner, and records bounded
// diagnostics from the report. It NEVER stamps an owner current, never mutates saved rows, and
// never creates events/pages/requests. The M11 commit slice will reuse these exact plans.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Runs the bounded per-owner dry-run migration report over every still-legacy envelope.
        /// Failure-isolated like every other load repair: a throw here must never abort the load.
        /// </summary>
        private void RunMemoryMigrationDryRunReport()
        {
            if (diaries == null)
            {
                return;
            }

            List<MemoryLegacyRuleMapEntry> ruleMap = SnapshotLegacyRuleMap();
            int reportedOwners = 0;
            for (int i = 0; i < diaries.Count && reportedOwners < 64; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || diary.knowledgeState == null
                    || diary.knowledgeState.IsCurrentSchema()
                    || diary.knowledgeState.records == null
                    || diary.knowledgeState.records.Count == 0)
                {
                    continue;
                }

                var input = new MemoryLegacyOwnerMigrationInput
                {
                    ownerPawnId = diary.pawnId ?? string.Empty,
                    ownerEpochToken =
                        diary.knowledgeState.autobiographicalEpochToken ?? string.Empty
                };
                foreach (ImportantMemoryRecord record in diary.knowledgeState.records)
                {
                    // Raw-preservation rule (§T13.1): the snapshot copies the loaded shape as-is —
                    // no semantic Normalize of tokens, no list alignment beyond null-safety.
                    input.records.Add(SnapshotLegacyRecord(record));
                }

                MemoryLegacyMigrationReport report =
                    MemoryThreadMigrationPolicy.PlanDryRun(input);
                RecordMemoryDiagnostic("legacy_dry_run", "owner");
                if (report.ownerRemainsRaw)
                {
                    RecordMemoryDiagnostic("legacy_owner_raw", diary.pawnId ?? "owner");
                }

                if (report.droppedAutomaticAlternateCount > 0)
                {
                    RecordMemoryDiagnostic(
                        "legacy_automatic_duplicate", diary.pawnId ?? "owner");
                }

                if (report.archivedAuthoredConflictCount > 0)
                {
                    RecordMemoryDiagnostic("legacy_authored_conflict", diary.pawnId ?? "owner");
                }

                reportedOwners++;
            }
        }

        private static MemoryLegacyRecordSnapshot SnapshotLegacyRecord(ImportantMemoryRecord record)
        {
            var snapshot = new MemoryLegacyRecordSnapshot
            {
                recordId = record.recordId ?? string.Empty,
                dedupKey = record.dedupKey ?? string.Empty,
                sourceEventId = record.sourceEventId ?? string.Empty,
                sourceKind = record.sourceKind ?? KnowledgeTokens.SourceKindCaptured,
                recallScope = record.recallScope ?? KnowledgeTokens.RecallScopeContextual,
                eventKind = record.eventKind ?? string.Empty,
                topicKey = record.topicKey ?? string.Empty,
                tick = record.tick,
                manualTextOverride = record.manualTextOverride ?? string.Empty
            };
            CopySafe(record.participantIds, snapshot.participantIds);
            CopySafe(record.subjectKeys, snapshot.subjectKeys);
            CopySafe(record.factKeys, snapshot.factKeys);
            CopySafe(record.factValues, snapshot.factValues);
            return snapshot;
        }

        private static void CopySafe(List<string> source, List<string> target)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                target.Add(source[i] ?? string.Empty);
            }
        }

        /// <summary>Snapshots the frozen memory-legacy-map-v1 catalog from the shipped Defs:
        /// one entry per eventKind with its current rule ID and kind/category/importance tokens.</summary>
        private static List<MemoryLegacyRuleMapEntry> SnapshotLegacyRuleMap()
        {
            var map = new List<MemoryLegacyRuleMapEntry>();
            try
            {
                List<ImportantEventRule> rules = DiaryKnowledgePolicy.ImportantEventRules();
                var seenKinds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < rules.Count; i++)
                {
                    ImportantEventRule rule = rules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.eventKind)
                        || !seenKinds.Add(rule.eventKind))
                    {
                        continue;
                    }

                    var entry = new MemoryLegacyRuleMapEntry
                    {
                        eventKind = rule.eventKind,
                        captureRuleId = rule.defName ?? string.Empty,
                        memoryKind = NormalizeKind(rule.memoryKind),
                        category = MemoryContractTokens.IsKnownCategory(rule.memoryCategory)
                            ? rule.memoryCategory
                            : MemoryContractTokens.CategoryPersonal,
                        baseImportance =
                            MemoryContractTokens.IsKnownImportance(rule.baseImportance)
                                ? rule.baseImportance
                                : MemoryContractTokens.ImportanceImportant
                    };
                    entry.factDescriptors.AddRange(rule.memoryFacts ?? new List<MemoryFactDescriptor>());
                    map.Add(entry);
                }
            }
            catch (Exception)
            {
                // A missing/failed Def database simply maps nothing: every known-kind row then
                // follows the conservative unmapped arm. Never abort the load here.
            }

            return map;
        }

        private static string NormalizeKind(string memoryKind)
        {
            return memoryKind == MemoryContractTokens.KindEvent
                ? MemoryContractTokens.KindEvent
                : MemoryContractTokens.KindLandmark;
        }
    }
}
