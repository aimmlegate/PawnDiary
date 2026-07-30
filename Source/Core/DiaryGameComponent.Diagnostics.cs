// Runtime telemetry and persistence-integrity adapter for DiaryGameComponent.
//
// The pure ledger/policy know only stable labels and detached facts. This partial projects the live
// repositories at deliberate maintenance boundaries (load, save, developer export), reports only
// identifier-free summaries, and provides a cheap per-new-event owner-reference check.
using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Registers a newly built event or fails loudly before any pawn reference can point at an
        /// unregistered/duplicate row. Runtime GUID collisions are impossible in healthy state.
        /// </summary>
        private void RegisterNewEventOrThrow(DiaryEvent diaryEvent)
        {
            DiaryEventRegistrationOutcome outcome = events.Register(diaryEvent);
            if (outcome == DiaryEventRegistrationOutcome.Registered)
            {
                return;
            }

            DiaryTelemetryOutcome telemetryOutcome =
                outcome == DiaryEventRegistrationOutcome.DuplicateEventId
                    ? DiaryTelemetryOutcome.RepositoryDuplicateIdRejected
                    : DiaryTelemetryOutcome.RepositoryRegistrationInvalid;
            string eventType = diaryEvent?.interactionDefName ?? string.Empty;
            string summary = "registration_outcome=" + outcome;
            DiaryTelemetryReporter.ReportInvariant(
                telemetryOutcome,
                "event.register",
                "DiaryEventRepository",
                eventType,
                summary,
                diaryEvent?.tick ?? DiaryTelemetryReporter.CurrentGameTick());
            throw new InvalidOperationException(
                "Diary event registration failed before references were committed: " + outcome + ".");
        }

        /// <summary>
        /// Atomically registers a new event and all diary owners required by that page. A life-boundary
        /// or eligibility change may legitimately reject a reference after capture; in that case this
        /// rolls back the master row and any first owner already added, so no orphan can reach generation
        /// or persistence.
        /// </summary>
        private bool TryCommitNewEvent(
            DiaryEvent diaryEvent,
            Pawn initiator,
            Pawn recipient,
            bool includeRecipientOwner,
            bool insertChronologically)
        {
            RegisterNewEventOrThrow(diaryEvent);

            bool initiatorCommitted =
                AddEventRef(initiator, diaryEvent.eventId, insertChronologically);
            bool recipientCommitted = !includeRecipientOwner
                || AddEventRef(recipient, diaryEvent.eventId, insertChronologically);
            if (initiatorCommitted && recipientCommitted)
            {
                ValidateNewEventCommit(diaryEvent, includeRecipientOwner);
                return true;
            }

            RollBackNewEventCommit(diaryEvent.eventId);
            return false;
        }

        /// <summary>Removes both halves of a newly rejected event commit.</summary>
        private void RollBackNewEventCommit(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    List<string> eventIds = diaries[i]?.eventIds;
                    if (eventIds == null)
                    {
                        continue;
                    }

                    for (int j = eventIds.Count - 1; j >= 0; j--)
                    {
                        if (string.Equals(
                            eventIds[j],
                            eventId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            eventIds.RemoveAt(j);
                        }
                    }
                }
            }

            events.RemoveEvent(eventId);
        }

        /// <summary>
        /// Checks the narrow post-commit invariant for a freshly created event. Callers run this after
        /// adding every initial owner reference and before retention is allowed to transform those refs.
        /// </summary>
        private void ValidateNewEventCommit(DiaryEvent diaryEvent, bool includeRecipientOwner = true)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(diaryEvent.eventId))
            {
                return;
            }

            DiaryEvent indexed = events.FindEvent(diaryEvent.eventId);
            if (indexed == null)
            {
                // A zero/extreme retention boundary may have archived the new page immediately.
                return;
            }

            if (!ReferenceEquals(indexed, diaryEvent))
            {
                DiaryTelemetryReporter.ReportInvariant(
                    DiaryTelemetryOutcome.IntegrityIssue,
                    "event.commit",
                    "DiaryEventRepository",
                    diaryEvent.interactionDefName,
                    "index_points_to_different_event=1",
                    diaryEvent.tick);
                return;
            }

            HashSet<string> owners = new HashSet<string>(StringComparer.Ordinal);
            AddExpectedOwner(owners, diaryEvent.initiatorPawnId);
            if (includeRecipientOwner)
            {
                AddExpectedOwner(owners, diaryEvent.recipientPawnId);
            }

            int missing = 0;
            foreach (string owner in owners)
            {
                PawnDiaryRecord diary = FindDiaryByPawnId(owner);
                if (diary == null || !ContainsEventId(diary.eventIds, diaryEvent.eventId))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                DiaryTelemetryReporter.ReportInvariant(
                    DiaryTelemetryOutcome.IntegrityIssue,
                    "event.commit",
                    "DiaryEventRepository",
                    diaryEvent.interactionDefName,
                    "missing_owner_refs=" + missing,
                    diaryEvent.tick,
                    missing);
            }
        }

        /// <summary>Records exactly what load repair discarded before those rows disappear.</summary>
        private static void RecordLoadedEventRepair(LoadedEventRepairPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            int repaired = plan.repairedIdSourceIndexes.Count;
            if (repaired > 0)
            {
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.LoadedEventIdRepaired,
                    "load.repair",
                    "DiaryEventRepository",
                    null,
                    DiaryTelemetryReporter.CurrentGameTick(),
                    repaired);
            }

            int invalid = plan.invalidRowCount + plan.duplicateSourceIndexCount;
            if (invalid > 0)
            {
                DiaryTelemetryReporter.ReportInvariant(
                    DiaryTelemetryOutcome.LoadedEventInvalidDiscarded,
                    "load.repair",
                    "DiaryEventRepository",
                    null,
                    "discarded_invalid_rows=" + invalid,
                    DiaryTelemetryReporter.CurrentGameTick(),
                    invalid);
            }

            if (plan.duplicateEventIdCount > 0)
            {
                DiaryTelemetryReporter.ReportInvariant(
                    DiaryTelemetryOutcome.LoadedEventDuplicateDiscarded,
                    "load.repair",
                    "DiaryEventRepository",
                    null,
                    "discarded_duplicate_event_ids=" + plan.duplicateEventIdCount,
                    DiaryTelemetryReporter.CurrentGameTick(),
                    plan.duplicateEventIdCount);
            }

            if (plan.DiscardedRowCount > 0)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Loaded event repair discarded invalid or duplicate rows: "
                    + "invalid=" + invalid + ", duplicate_ids=" + plan.duplicateEventIdCount + ".",
                    ("PawnDiary.LoadRepair." + invalid + "." + plan.duplicateEventIdCount).GetHashCode());
            }
        }

        /// <summary>
        /// Audits detached repository/reference facts. On a real maintenance boundary, persistent
        /// issues are logged locally and passed to the existing opt-in scrubbed reporter.
        /// </summary>
        private DiaryIntegrityReport RunDiaryIntegrityAudit(string stage, bool reportPersistentIssues)
        {
            try
            {
                DiaryIntegrityReport report = BuildDiaryIntegrityReport();
                int tick = DiaryTelemetryReporter.CurrentGameTick();
                if (report.IsHealthy)
                {
                    DiaryTelemetry.Record(
                        DiaryTelemetryOutcome.IntegrityAuditHealthy,
                        stage,
                        "persistence",
                        null,
                        tick);
                }
                else if (reportPersistentIssues)
                {
                    string summary = report.CompactSummary();
                    DiaryTelemetryReporter.ReportInvariant(
                        DiaryTelemetryOutcome.IntegrityIssue,
                        stage,
                        "persistence",
                        null,
                        summary,
                        tick,
                        report.IssueCount);
                    Log.WarningOnce(
                        "[Pawn Diary] Persistence integrity audit found inconsistencies at "
                        + stage + ": " + summary,
                        ("PawnDiary.Integrity." + stage + "." + summary).GetHashCode());
                }
                else
                {
                    DiaryTelemetry.Record(
                        DiaryTelemetryOutcome.IntegrityIssue,
                        stage,
                        "persistence",
                        null,
                        tick,
                        report.IssueCount,
                        report.CompactSummary());
                }

                return report;
            }
            catch (Exception exception)
            {
                string fingerprint = DiaryTelemetryReporter.RecordException(
                    DiaryTelemetryOutcome.IntegrityAuditException,
                    stage,
                    "persistence",
                    null,
                    exception,
                    DiaryTelemetryReporter.CurrentGameTick());
                Log.ErrorOnce(
                    "[Pawn Diary] Persistence integrity audit failed at " + stage + ": " + exception,
                    DiaryTelemetryReporter.ErrorOnceKey(
                        "PawnDiary.PersistenceIntegrity." + stage,
                        fingerprint));
                return null;
            }
        }

        /// <summary>Adds current telemetry and a fresh integrity summary to the developer export.</summary>
        private void AppendRuntimeDiagnosticsExport(StringBuilder builder)
        {
            DiaryIntegrityReport integrity = RunDiaryIntegrityAudit("developer_export", true);
            builder.AppendLine();
            builder.Append(DiaryTelemetryFormatter.Format(DiaryTelemetry.Snapshot()));
            builder.AppendLine();
            builder.AppendLine("== Current persistence integrity ==");
            builder.AppendLine(integrity == null
                ? "audit_failed=1"
                : integrity.CompactSummary());
        }

        private DiaryIntegrityReport BuildDiaryIntegrityReport()
        {
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            List<DiaryIntegrityEventFact> eventFacts =
                new List<DiaryIntegrityEventFact>(allEvents.Count);
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    eventFacts.Add(null);
                    continue;
                }

                // Do not infer durable required owners from initiator/recipient here. Retention may
                // legitimately drop one side of a shared page (outside diary bounds, cold undisplayable,
                // or later trimmed from the compact archive) while the other side keeps the hot event.
                // The creation path checks all initial owners before retention; this maintenance audit
                // checks only invariants that remain true after every valid retention transformation.
                eventFacts.Add(new DiaryIntegrityEventFact
                {
                    eventId = diaryEvent.eventId
                });
            }

            List<DiaryIntegrityDiaryFact> diaryFacts =
                new List<DiaryIntegrityDiaryFact>(diaries?.Count ?? 0);
            Dictionary<string, DiaryIntegrityDiaryFact> firstFactByPawn =
                new Dictionary<string, DiaryIntegrityDiaryFact>(StringComparer.Ordinal);
            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord diary = diaries[i];
                    if (diary == null)
                    {
                        diaryFacts.Add(null);
                        continue;
                    }

                    DiaryIntegrityDiaryFact fact = new DiaryIntegrityDiaryFact
                    {
                        pawnId = diary.pawnId
                    };
                    if (diary.eventIds != null)
                    {
                        fact.eventIds.AddRange(diary.eventIds);
                    }
                    diaryFacts.Add(fact);

                    if (!string.IsNullOrWhiteSpace(fact.pawnId)
                        && !firstFactByPawn.ContainsKey(fact.pawnId))
                    {
                        firstFactByPawn.Add(fact.pawnId, fact);
                    }
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.AllEntries;
            for (int i = 0; i < archivedEntries.Count; i++)
            {
                ArchivedDiaryEntry archived = archivedEntries[i];
                if (archived == null
                    || string.IsNullOrWhiteSpace(archived.pawnId)
                    || string.IsNullOrWhiteSpace(archived.eventId))
                {
                    continue;
                }

                DiaryIntegrityDiaryFact fact;
                if (!firstFactByPawn.TryGetValue(archived.pawnId, out fact))
                {
                    fact = new DiaryIntegrityDiaryFact
                    {
                        pawnId = archived.pawnId,
                        isSavedDiaryRow = false
                    };
                    diaryFacts.Add(fact);
                    firstFactByPawn.Add(archived.pawnId, fact);
                }
                fact.archivedEventIds.Add(archived.eventId);
            }

            return DiaryIntegrityPolicy.Audit(eventFacts, diaryFacts);
        }

        private static bool ContainsEventId(IList<string> eventIds, string eventId)
        {
            if (eventIds == null || string.IsNullOrWhiteSpace(eventId))
            {
                return false;
            }

            for (int i = 0; i < eventIds.Count; i++)
            {
                if (string.Equals(eventIds[i], eventId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddExpectedOwner(ICollection<string> owners, string pawnId)
        {
            if (owners == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            if (!owners.Contains(pawnId))
            {
                owners.Add(pawnId);
            }
        }
    }
}
