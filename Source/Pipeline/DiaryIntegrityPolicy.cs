// Pure consistency audit for the saved hot-event repository and per-pawn event references.
//
// The GameComponent projects mutable DiaryEvent/PawnDiaryRecord models into these plain facts. This
// policy then finds duplicates, dangling references, orphan events, and owner/ref mismatches without
// touching RimWorld state. The adapter decides when to log/report and never mutates a save here.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>Plain identity and expected-owner facts for one hot event row.</summary>
    internal sealed class DiaryIntegrityEventFact
    {
        public string eventId = string.Empty;
        public readonly List<string> expectedOwnerPawnIds = new List<string>();
    }

    /// <summary>Plain event-reference facts for one saved pawn diary row.</summary>
    internal sealed class DiaryIntegrityDiaryFact
    {
        public string pawnId = string.Empty;
        public bool isSavedDiaryRow = true;
        public readonly List<string> eventIds = new List<string>();

        // Compact archive ids satisfy historical ownership but are not hot repository references:
        // they must not keep a DiaryEvent alive and are therefore excluded from dangling/orphan checks.
        public readonly List<string> archivedEventIds = new List<string>();
    }

    /// <summary>Aggregate issue counts from one consistency audit.</summary>
    internal sealed class DiaryIntegrityReport
    {
        public int nullEventRows;
        public int blankEventIds;
        public int duplicateEventIds;
        public int nullDiaryRows;
        public int blankPawnIds;
        public int duplicatePawnDiaryIds;
        public int blankEventRefs;
        public int duplicateEventRefs;
        public int danglingEventRefs;
        public int orphanEvents;
        public int missingOwnerRefs;

        public int IssueCount
        {
            get
            {
                return nullEventRows
                    + blankEventIds
                    + duplicateEventIds
                    + nullDiaryRows
                    + blankPawnIds
                    + duplicatePawnDiaryIds
                    + blankEventRefs
                    + duplicateEventRefs
                    + danglingEventRefs
                    + orphanEvents
                    + missingOwnerRefs;
            }
        }

        public bool IsHealthy
        {
            get { return IssueCount == 0; }
        }

        /// <summary>Stable, identifier-free summary suitable for logs, fingerprints, and export.</summary>
        public string CompactSummary()
        {
            return "issues=" + IssueCount.ToString(CultureInfo.InvariantCulture)
                + ", null_events=" + nullEventRows.ToString(CultureInfo.InvariantCulture)
                + ", blank_event_ids=" + blankEventIds.ToString(CultureInfo.InvariantCulture)
                + ", duplicate_event_ids=" + duplicateEventIds.ToString(CultureInfo.InvariantCulture)
                + ", null_diaries=" + nullDiaryRows.ToString(CultureInfo.InvariantCulture)
                + ", blank_pawn_ids=" + blankPawnIds.ToString(CultureInfo.InvariantCulture)
                + ", duplicate_pawn_diaries=" + duplicatePawnDiaryIds.ToString(CultureInfo.InvariantCulture)
                + ", blank_refs=" + blankEventRefs.ToString(CultureInfo.InvariantCulture)
                + ", duplicate_refs=" + duplicateEventRefs.ToString(CultureInfo.InvariantCulture)
                + ", dangling_refs=" + danglingEventRefs.ToString(CultureInfo.InvariantCulture)
                + ", orphan_events=" + orphanEvents.ToString(CultureInfo.InvariantCulture)
                + ", missing_owner_refs=" + missingOwnerRefs.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Audits detached persistence facts without reading or mutating game objects.</summary>
    internal static class DiaryIntegrityPolicy
    {
        public static DiaryIntegrityReport Audit(
            IList<DiaryIntegrityEventFact> eventFacts,
            IList<DiaryIntegrityDiaryFact> diaryFacts)
        {
            DiaryIntegrityReport report = new DiaryIntegrityReport();
            Dictionary<string, DiaryIntegrityEventFact> eventsById =
                new Dictionary<string, DiaryIntegrityEventFact>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> refsByPawn =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> historicalRefsByPawn =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            HashSet<string> referencedEventIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (eventFacts != null)
            {
                for (int i = 0; i < eventFacts.Count; i++)
                {
                    DiaryIntegrityEventFact fact = eventFacts[i];
                    if (fact == null)
                    {
                        report.nullEventRows++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(fact.eventId))
                    {
                        report.blankEventIds++;
                        continue;
                    }

                    if (eventsById.ContainsKey(fact.eventId))
                    {
                        report.duplicateEventIds++;
                        continue;
                    }

                    eventsById.Add(fact.eventId, fact);
                }
            }

            HashSet<string> seenPawnIds = new HashSet<string>(StringComparer.Ordinal);
            if (diaryFacts != null)
            {
                for (int i = 0; i < diaryFacts.Count; i++)
                {
                    DiaryIntegrityDiaryFact diary = diaryFacts[i];
                    if (diary == null)
                    {
                        report.nullDiaryRows++;
                        continue;
                    }

                    string pawnId = diary.pawnId ?? string.Empty;
                    if (diary.isSavedDiaryRow && string.IsNullOrWhiteSpace(pawnId))
                    {
                        report.blankPawnIds++;
                    }
                    else if (diary.isSavedDiaryRow && !seenPawnIds.Add(pawnId))
                    {
                        report.duplicatePawnDiaryIds++;
                    }

                    HashSet<string> refs;
                    if (!refsByPawn.TryGetValue(pawnId, out refs))
                    {
                        refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        refsByPawn[pawnId] = refs;
                    }
                    HashSet<string> historicalRefs;
                    if (!historicalRefsByPawn.TryGetValue(pawnId, out historicalRefs))
                    {
                        historicalRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        historicalRefsByPawn[pawnId] = historicalRefs;
                    }

                    for (int j = 0; j < diary.eventIds.Count; j++)
                    {
                        string eventId = diary.eventIds[j];
                        if (string.IsNullOrWhiteSpace(eventId))
                        {
                            report.blankEventRefs++;
                            continue;
                        }

                        if (!refs.Add(eventId))
                        {
                            report.duplicateEventRefs++;
                            continue;
                        }

                        referencedEventIds.Add(eventId);
                        historicalRefs.Add(eventId);
                        if (!eventsById.ContainsKey(eventId))
                        {
                            report.danglingEventRefs++;
                        }
                    }

                    for (int j = 0; j < diary.archivedEventIds.Count; j++)
                    {
                        string archivedEventId = diary.archivedEventIds[j];
                        if (!string.IsNullOrWhiteSpace(archivedEventId))
                        {
                            historicalRefs.Add(archivedEventId);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, DiaryIntegrityEventFact> pair in eventsById)
            {
                if (!referencedEventIds.Contains(pair.Key))
                {
                    report.orphanEvents++;
                }

                HashSet<string> checkedOwners = new HashSet<string>(StringComparer.Ordinal);
                List<string> owners = pair.Value.expectedOwnerPawnIds;
                for (int i = 0; i < owners.Count; i++)
                {
                    string owner = owners[i];
                    if (string.IsNullOrWhiteSpace(owner) || !checkedOwners.Add(owner))
                    {
                        continue;
                    }

                    HashSet<string> ownerRefs;
                    if (!historicalRefsByPawn.TryGetValue(owner, out ownerRefs)
                        || !ownerRefs.Contains(pair.Key))
                    {
                        report.missingOwnerRefs++;
                    }
                }
            }

            return report;
        }
    }
}
