// MemoryThreadMigrationPolicy.cs — pure REPORT/dry-run legacy migration planning
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §15, §§T13.1–T13.5, phase M1 item 4).
//
// M1 delivers this planner in report mode ONLY against detached legacy snapshots: it never stamps
// an owner, never mutates saved state, and never creates events/pages/requests. The impure adapter
// (DiaryGameComponent.MemoryMigration.cs) extracts plain snapshots from loaded v1/v2 envelopes and
// the shipped Def catalog, then publishes bounded diagnostics from the returned report. The M11
// commit slice will reuse these exact plans atomically per owner.
//
// Determinism/idempotence: the report is a pure function of its input; PlanDryRun run twice over
// equal inputs yields equal reports and equal fingerprints. Identity never depends on source
// container/list position (§T13.3): occurrence identity follows the exact precedence arms, and all
// ordering ends in ordinal tie-breaks.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One detached legacy record snapshot. Payload prose is carried separately from
    /// identity: fact VALUES, names, date labels, fallback wording, and manual wording are payload
    /// (§T13.3) — only manualTextOverride participates in PlayerEdited detection.</summary>
    internal sealed class MemoryLegacyRecordSnapshot
    {
        public string recordId = string.Empty;
        public string dedupKey = string.Empty;
        public string sourceEventId = string.Empty;
        /// <summary>Shipped defaults captured/contextual (ImportantMemoryRecord's nested legacy exception).</summary>
        public string sourceKind = KnowledgeTokens.SourceKindCaptured;
        public string recallScope = KnowledgeTokens.RecallScopeContextual;
        public string eventKind = string.Empty;
        public string topicKey = string.Empty;
        public int tick;
        public List<string> participantIds = new List<string>();
        public List<string> subjectKeys = new List<string>();
        public List<string> factKeys = new List<string>();
        public List<string> factValues = new List<string>();
        public string manualTextOverride = string.Empty;
    }

    /// <summary>The frozen memory-legacy-map-v1 entry for one known eventKind, extracted by the
    /// adapter from the shipped capture Defs (keeps this file free of DefDatabase).</summary>
    internal sealed class MemoryLegacyRuleMapEntry
    {
        public string eventKind = string.Empty;
        public string captureRuleId = string.Empty;
        /// <summary>Stable kind token: event | landmark.</summary>
        public string memoryKind = string.Empty;
        /// <summary>One of the four category tokens.</summary>
        public string category = string.Empty;
        /// <summary>low | medium | high.</summary>
        public string baseImportance = string.Empty;
        /// <summary>contextKey → fact descriptor lookups for canonical-fact reconstruction.</summary>
        public List<MemoryFactDescriptor> factDescriptors = new List<MemoryFactDescriptor>();
    }

    /// <summary>Detached dry-run input for exactly one legacy owner group.</summary>
    internal sealed class MemoryLegacyOwnerMigrationInput
    {
        public string ownerPawnId = string.Empty;
        /// <summary>Already-reserved/reused epoch token for the group (empty while unresolved).</summary>
        public string ownerEpochToken = string.Empty;
        public List<MemoryLegacyRecordSnapshot> records = new List<MemoryLegacyRecordSnapshot>();
        /// <summary>Frozen memory-legacy-map-v1 catalog rows keyed by eventKind.</summary>
        public List<MemoryLegacyRuleMapEntry> ruleMap = new List<MemoryLegacyRuleMapEntry>();
    }

    /// <summary>One planned canonical fact with its zero-based canonical ordinal.</summary>
    internal sealed class MemoryLegacyMappedFact
    {
        public int originFactOrdinal;
        public string factId = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public string canonicalValueKind = string.Empty;
        public string canonicalValue = string.Empty;
    }

    /// <summary>One planned record row with its T13.3/T13.4 disposition.</summary>
    internal sealed class MemoryLegacyMappedRecord
    {
        public const string DispositionActive = "active";
        public const string DispositionArchiveAuthored = "archive_authored";
        public const string DispositionDropAutomatic = "drop_automatic_unedited";

        public string disposition = DispositionActive;
        public string sourceOccurrenceId = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
        public string kindToken = string.Empty;
        public string categoryToken = string.Empty;
        public string importanceToken = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        public bool playerEdited;
        public bool suppressed;
        public List<MemoryLegacyMappedFact> facts = new List<MemoryLegacyMappedFact>();
        public string provenanceRefId = string.Empty;
    }

    /// <summary>The bounded dry-run report for one owner group.</summary>
    internal sealed class MemoryLegacyMigrationReport
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public List<MemoryLegacyMappedRecord> rows = new List<MemoryLegacyMappedRecord>();
        public int droppedAutomaticAlternateCount;
        public int archivedAuthoredConflictCount;
        public int unmappedEventKindCount;
        public int invalidFactValueCount;
        /// <summary>True when any derived identity exceeded composite caps: the WHOLE owner stays
        /// raw/unstamped on commit (§T13.3); dry-run just reports it.</summary>
        public bool ownerRemainsRaw;
        /// <summary>Lowercase SHA-256 over the canonical report encoding; equal inputs match.</summary>
        public string reportFingerprint = string.Empty;

        public bool IsIdempotentWith(MemoryLegacyMigrationReport other)
        {
            return other != null
                && string.Equals(reportFingerprint, other.reportFingerprint, StringComparison.Ordinal);
        }
    }

    internal static class MemoryThreadMigrationPolicy
    {
        public const string MapSchemaToken = "memory-legacy-map-v1";

        private const string OccurrenceDedupDomain = "memory-legacy-occurrence-dedup-v1";
        private const string OccurrenceRecordDomain = "memory-legacy-occurrence-record-v1";
        private const string OccurrenceIdentityDomain = "memory-legacy-occurrence-identity-v1";
        private const string CaptureRuleDomain = "memory-legacy-capture-rule-v1";
        private const string FactDiscriminatorDomain = "memory-legacy-fact-discriminator-v1";

        /// <summary>
        /// Plans the conservative mapping for one legacy owner group WITHOUT committing anything
        /// (phase-M1 report mode). See §T13.3 for every precedence arm mirrored here.
        /// </summary>
        public static MemoryLegacyMigrationReport PlanDryRun(MemoryLegacyOwnerMigrationInput input)
        {
            MemoryLegacyMigrationReport report = new MemoryLegacyMigrationReport
            {
                ownerPawnId = Safe(input?.ownerPawnId),
                ownerEpochToken = Safe(input?.ownerEpochToken)
            };
            if (input == null || string.IsNullOrWhiteSpace(report.ownerPawnId))
            {
                report.ownerRemainsRaw = true;
                report.reportFingerprint = Fingerprint(report);
                return report;
            }

            Dictionary<string, MemoryLegacyRuleMapEntry> map =
                new Dictionary<string, MemoryLegacyRuleMapEntry>(StringComparer.Ordinal);
            foreach (MemoryLegacyRuleMapEntry entry in input.ruleMap ?? new List<MemoryLegacyRuleMapEntry>())
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.eventKind))
                {
                    map[entry.eventKind.Trim()] = entry;
                }
            }

            // Group rows by their resolved occurrence identity first: equal fallback tuples are ONE
            // semantic occurrence regardless of input order (§T13.3 arm 1). Winner selection is
            // then fully order-independent: authored rows beat automatic ones, and within each
            // class the lowest length-prefixed canonical saved-field tuple wins (§T8.4 step 8).
            Dictionary<string, List<MemoryLegacyRecordSnapshot>> groups =
                new Dictionary<string, List<MemoryLegacyRecordSnapshot>>(StringComparer.Ordinal);
            foreach (MemoryLegacyRecordSnapshot snapshot
                in input.records ?? new List<MemoryLegacyRecordSnapshot>())
            {
                if (snapshot == null)
                {
                    continue;
                }

                string occurrence = ResolveOccurrenceId(report.ownerPawnId, snapshot, report);
                if (occurrence == null)
                {
                    // Over-cap derived identity: the whole owner remains raw on commit (§T13.3).
                    report.ownerRemainsRaw = true;
                    continue;
                }

                if (!groups.TryGetValue(occurrence, out List<MemoryLegacyRecordSnapshot> bucket))
                {
                    bucket = new List<MemoryLegacyRecordSnapshot>();
                    groups[occurrence] = bucket;
                }

                bucket.Add(snapshot);
            }

            foreach (KeyValuePair<string, List<MemoryLegacyRecordSnapshot>> group in groups)
            {
                string occurrence = group.Key;
                MemoryLegacyRecordSnapshot winner =
                    SelectCanonicalWinner(group.Value, map);
                if (winner == null)
                {
                    continue;
                }

                MemoryLegacyMappedRecord mapped = MapRecord(report, winner, occurrence, map);
                if (mapped == null)
                {
                    report.ownerRemainsRaw = true;
                    continue;
                }

                bool winnerAuthored = !string.IsNullOrWhiteSpace(winner.manualTextOverride)
                    || IsPlayerSource(winner.sourceKind);
                mapped.playerEdited |= winnerAuthored;

                int archiveStubCount = 0;
                foreach (MemoryLegacyRecordSnapshot alternate in group.Value)
                {
                    if (ReferenceEquals(alternate, winner))
                    {
                        continue;
                    }

                    if (IsSemanticDuplicate(mapped, alternate, winner))
                    {
                        // Byte-equal semantic duplicates collapse silently (§T8.4 step 5).
                        continue;
                    }

                    bool alternateAuthored =
                        !string.IsNullOrWhiteSpace(alternate.manualTextOverride)
                        || IsPlayerSource(alternate.sourceKind);
                    if (alternateAuthored || winnerAuthored)
                    {
                        // Preserve every irreconcilable AUTHORED alternate as an archive stub
                        // (§T13.4); bounded diagnostic, never a second active identity.
                        archiveStubCount++;
                        report.archivedAuthoredConflictCount++;
                    }
                    else
                    {
                        // Conflicting unedited automatic alternates drop with one diagnostic
                        // rather than becoming permanent rows (§T6.8/§T8.4).
                        report.droppedAutomaticAlternateCount++;
                    }
                }

                if (archiveStubCount > 0)
                {
                    report.rows.Add(new MemoryLegacyMappedRecord
                    {
                        disposition = MemoryLegacyMappedRecord.DispositionArchiveAuthored,
                        sourceOccurrenceId = occurrence
                    });
                }

                report.rows.Add(mapped);
            }


            // Canonical §T7.4-style total order: every field participates so equal-key rows stay
            // deterministically ordered regardless of input position or unstable sorts.
            report.rows.Sort(CompareRows);
            report.reportFingerprint = Fingerprint(report);
            return report;
        }

        private static int CompareRows(
            MemoryLegacyMappedRecord left, MemoryLegacyMappedRecord right)
        {
            int compare = string.CompareOrdinal(
                left.sourceOccurrenceId ?? string.Empty,
                right.sourceOccurrenceId ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.disposition ?? string.Empty, right.disposition ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.captureRuleId ?? string.Empty, right.captureRuleId ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.factDiscriminator ?? string.Empty, right.factDiscriminator ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.kindToken ?? string.Empty, right.kindToken ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.categoryToken ?? string.Empty, right.categoryToken ?? string.Empty);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.importanceToken ?? string.Empty, right.importanceToken ?? string.Empty);
            if (compare != 0) return compare;
            compare = left.originalEventTick.CompareTo(right.originalEventTick);
            if (compare != 0) return compare;
            compare = left.ageUnknown.CompareTo(right.ageUnknown);
            if (compare != 0) return compare;
            compare = left.playerEdited.CompareTo(right.playerEdited);
            if (compare != 0) return compare;
            compare = left.suppressed.CompareTo(right.suppressed);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.provenanceRefId ?? string.Empty, right.provenanceRefId ?? string.Empty);
            if (compare != 0) return compare;
            compare = left.facts.Count.CompareTo(right.facts.Count);
            for (int i = 0; compare == 0 && i < left.facts.Count && i < right.facts.Count; i++)
            {
                compare = string.CompareOrdinal(left.facts[i].factId ?? string.Empty, right.facts[i].factId ?? string.Empty);
                if (compare != 0) return compare;
                compare = left.facts[i].originFactOrdinal.CompareTo(right.facts[i].originFactOrdinal);
                if (compare != 0) return compare;
                compare = string.CompareOrdinal(left.facts[i].canonicalValue ?? string.Empty, right.facts[i].canonicalValue ?? string.Empty);
                if (compare != 0) return compare;
                compare = string.CompareOrdinal(left.facts[i].canonicalValueKind ?? string.Empty, right.facts[i].canonicalValueKind ?? string.Empty);
            }

            return compare;
        }

        /// <summary>T13.3 arm 1: valid sourceEventId wins; then dedup-key; then recordId; then the
        /// hashed occurrence-tuple fallback. Null means an over-cap identity (owner stays raw).</summary>
        private static string ResolveOccurrenceId(
            string ownerPawnId,
            MemoryLegacyRecordSnapshot snapshot,
            MemoryLegacyMigrationReport report)
        {
            if (IsBounded(snapshot.sourceEventId))
            {
                return snapshot.sourceEventId;
            }

            if (IsBounded(snapshot.dedupKey))
            {
                return OrdinalSegmentCodec.Segment(OccurrenceDedupDomain)
                    + OrdinalSegmentCodec.Segment(ownerPawnId)
                    + OrdinalSegmentCodec.Segment(snapshot.dedupKey);
            }

            if (IsBounded(snapshot.recordId))
            {
                return OrdinalSegmentCodec.Segment(OccurrenceRecordDomain)
                    + OrdinalSegmentCodec.Segment(ownerPawnId)
                    + OrdinalSegmentCodec.Segment(snapshot.recordId);
            }

            // Hash the normalized occurrence tuple: kinds/scopes/topic/tick + sorted distinct
            // participants + subject keys + fact KEYS. Values/names/labels are payload (§T13.3).
            var tupleParts = new List<string>
            {
                Safe(snapshot.sourceKind),
                Safe(snapshot.recallScope),
                Safe(snapshot.eventKind),
                Safe(snapshot.topicKey),
                snapshot.tick.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            tupleParts.AddRange(SortedDistinct(snapshot.participantIds));
            tupleParts.AddRange(SortedDistinct(snapshot.subjectKeys));
            tupleParts.AddRange(SortedDistinct(snapshot.factKeys));

            var framed = new System.Text.StringBuilder();
            foreach (string part in tupleParts)
            {
                if (part == null || part.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                    || !MemoryIdentityCodec.IsWellFormedUtf16(part))
                {
                    return null;
                }

                framed.Append(OrdinalSegmentCodec.Segment(part));
            }

            string tupleHash;
            if (!TryHashUtf8(framed.ToString(), out tupleHash))
            {
                return null;
            }

            string identity = OrdinalSegmentCodec.Segment(OccurrenceIdentityDomain)
                + OrdinalSegmentCodec.Segment(ownerPawnId)
                + OrdinalSegmentCodec.Segment(tupleHash);
            return identity.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                ? identity
                : null;
        }

        private static MemoryLegacyMappedRecord MapRecord(
            MemoryLegacyMigrationReport report,
            MemoryLegacyRecordSnapshot snapshot,
            string occurrence,
            Dictionary<string, MemoryLegacyRuleMapEntry> map)
        {
            MemoryLegacyRuleMapEntry rule = null;
            if (!string.IsNullOrWhiteSpace(snapshot.eventKind)
                && !map.TryGetValue(snapshot.eventKind.Trim(), out rule))
            {
                rule = null;
            }

            MemoryLegacyMappedRecord mapped = new MemoryLegacyMappedRecord();
            if (rule != null)
            {
                mapped.captureRuleId = rule.captureRuleId ?? string.Empty;
                mapped.kindToken = Safe(rule.memoryKind);
                mapped.categoryToken = Safe(rule.category);
                mapped.importanceToken = Safe(rule.baseImportance);
            }
            else
            {
                // Unmapped-but-structurally-valid row: generic rule ID plus conservative
                // Landmark/Important (unknown/removed-mod type, §T13.3).
                report.unmappedEventKindCount++;
                mapped.captureRuleId = OrdinalSegmentCodec.Segment(CaptureRuleDomain)
                    + OrdinalSegmentCodec.Segment(Safe(snapshot.sourceKind))
                    + OrdinalSegmentCodec.Segment(Safe(snapshot.recallScope))
                    + OrdinalSegmentCodec.Segment(Safe(snapshot.eventKind));
                mapped.kindToken = MemoryContractTokens.KindLandmark;
                mapped.categoryToken = MemoryContractTokens.CategoryPersonal;
                mapped.importanceToken = MemoryContractTokens.ImportanceImportant;
                if (mapped.captureRuleId.Length
                    > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters)
                {
                    report.ownerRemainsRaw = true;
                    return null;
                }
            }

            mapped.factDiscriminator = OrdinalSegmentCodec.Segment(FactDiscriminatorDomain)
                + OrdinalSegmentCodec.Segment(Safe(snapshot.sourceKind))
                + OrdinalSegmentCodec.Segment(Safe(snapshot.recallScope))
                + OrdinalSegmentCodec.Segment(Safe(snapshot.eventKind))
                + OrdinalSegmentCodec.Segment(Safe(snapshot.topicKey));
            if (mapped.factDiscriminator.Length
                > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters)
            {
                // Over-cap derived identity: none truncated; the owner stays raw (§T13.3).
                report.ownerRemainsRaw = true;
                return null;
            }

            mapped.sourceOccurrenceId = occurrence;
            mapped.playerEdited = !string.IsNullOrWhiteSpace(snapshot.manualTextOverride)
                || IsPlayerSource(snapshot.sourceKind);
            mapped.suppressed = false; // absent suppression becomes false (§T15.2)

            // Tick handling: missing/zero/corrupt ticks become ageUnknown Important, not guesses.
            if (snapshot.tick > 0)
            {
                mapped.originalEventTick = snapshot.tick;
                mapped.ageUnknown = false;
            }
            else
            {
                mapped.originalEventTick = 0;
                mapped.ageUnknown = true;
                mapped.importanceToken = MemoryContractTokens.ImportanceImportant;
            }

            MapFacts(report, mapped, snapshot, rule);
            if (report.ownerRemainsRaw)
            {
                return null;
            }

            // Provenance row: legacy_migration with the derived occurrence/rule/discriminator and
            // empty integration token; ID recomputed by the shared codec (§T13.3 step 5).
            if (!MemoryIdentityCodec.TryCreateProvenanceRefId(
                    "legacy_migration",
                    occurrence,
                    string.Empty,
                    mapped.captureRuleId,
                    mapped.factDiscriminator,
                    string.Empty,
                    out mapped.provenanceRefId))
            {
                report.ownerRemainsRaw = true;
                return null;
            }

            return mapped;
        }

        private static void MapFacts(
            MemoryLegacyMigrationReport report,
            MemoryLegacyMappedRecord mapped,
            MemoryLegacyRecordSnapshot snapshot,
            MemoryLegacyRuleMapEntry rule)
        {
            if (snapshot.factKeys == null || snapshot.factValues == null)
            {
                return;
            }

            var candidates = new List<MemoryLegacyMappedFact>();
            int limit = Math.Min(snapshot.factKeys.Count, snapshot.factValues.Count);
            for (int i = 0; i < limit; i++)
            {
                string key = snapshot.factKeys[i];
                string value = snapshot.factValues[i] ?? string.Empty;
                MemoryFactDescriptor descriptor = FindDescriptor(rule, key);
                if (descriptor == null || string.IsNullOrWhiteSpace(key))
                {
                    // Safely parsed automatic data without a current descriptor drops with one
                    // bounded diagnostic rather than guessing a grammar (§T13.3).
                    report.invalidFactValueCount++;
                    continue;
                }

                if (!MemoryThreadRoutingPolicy.IsValidCanonicalValue(descriptor, value))
                {
                    report.invalidFactValueCount++;
                    continue;
                }

                string canonicalSubjectId = mapped.sourceOccurrenceId;
                string factId;
                if (!MemoryIdentityCodec.TryCreateFactId(
                        mapped.captureRuleId,
                        mapped.factDiscriminator,
                        descriptor.factKind ?? string.Empty,
                        MemoryContractTokens.SubjectStream,
                        canonicalSubjectId,
                        descriptor.aggregationToken ?? string.Empty,
                        out factId))
                {
                    report.ownerRemainsRaw = true;
                    return;
                }

                candidates.Add(new MemoryLegacyMappedFact
                {
                    factId = factId,
                    factKind = descriptor.factKind ?? string.Empty,
                    canonicalSubjectKind = MemoryContractTokens.SubjectStream,
                    canonicalSubjectId = canonicalSubjectId,
                    aggregationToken = descriptor.aggregationToken ?? string.Empty,
                    canonicalValueKind = descriptor.canonicalValueKind ?? string.Empty,
                    canonicalValue = value
                });
            }

            // Canonical order is the complete fact identity/payload tuple; byte-equal duplicates
            // collapse; ordinals are zero-based in that canonical order (§T13.3 step 4).
            candidates.Sort((left, right) =>
            {
                int compare = string.CompareOrdinal(left.factId, right.factId);
                if (compare != 0) return compare;
                compare = string.CompareOrdinal(left.canonicalValue, right.canonicalValue);
                if (compare != 0) return compare;
                return string.CompareOrdinal(left.canonicalValueKind, right.canonicalValueKind);
            });

            string previousKey = null;
            foreach (MemoryLegacyMappedFact fact in candidates)
            {
                if (previousKey != null
                    && string.Equals(previousKey, fact.factId, StringComparison.Ordinal)
                    && fact.originFactOrdinal >= 0)
                {
                    // Byte-equal duplicate under one fact identity collapses.
                    if (fact.canonicalValue.Length == 0)
                    {
                        continue;
                    }
                }

                previousKey = fact.factId;
                fact.originFactOrdinal = mapped.facts.Count;
                mapped.facts.Add(fact);
            }
        }

        private static MemoryFactDescriptor FindDescriptor(
            MemoryLegacyRuleMapEntry rule, string contextKey)
        {
            if (rule == null || string.IsNullOrWhiteSpace(contextKey))
            {
                return null;
            }

            for (int i = 0; i < rule.factDescriptors.Count; i++)
            {
                MemoryFactDescriptor descriptor = rule.factDescriptors[i];
                if (descriptor != null && string.Equals(
                        descriptor.contextKey, contextKey, StringComparison.Ordinal))
                {
                    return descriptor;
                }
            }

            return null;
        }

        private static bool IsSemanticDuplicate(
            MemoryLegacyMappedRecord mapped,
            MemoryLegacyRecordSnapshot left,
            MemoryLegacyRecordSnapshot right)
        {
            // Complete semantic duplicate predicate over identity + payload-relevant fields.
            // Player wording disagreement is precisely the authored-conflict case, so the manual
            // override participates here rather than collapsing two authorings into one (§T8.4).
            return left != null && right != null
                && string.Equals(left.eventKind, right.eventKind, StringComparison.Ordinal)
                && string.Equals(left.topicKey, right.topicKey, StringComparison.Ordinal)
                && left.tick == right.tick
                && SameLists(left.subjectKeys, right.subjectKeys)
                && SameLists(left.factKeys, right.factKeys)
                && SameLists(left.factValues, right.factValues)
                && string.Equals(
                    left.manualTextOverride,
                    right.manualTextOverride,
                    StringComparison.Ordinal)
                && mapped != null;
        }

        /// <summary>
        /// Deterministic §T8.4-style winner choice inside one occurrence group. Authored rows beat
        /// automatic ones; ties resolve by the lowest length-prefixed canonical saved-field tuple
        /// (identity is already equal inside the group). Never uses source/list position.
        /// </summary>
        private static MemoryLegacyRecordSnapshot SelectCanonicalWinner(
            List<MemoryLegacyRecordSnapshot> group,
            Dictionary<string, MemoryLegacyRuleMapEntry> map)
        {
            MemoryLegacyRecordSnapshot best = null;
            string bestKey = null;
            bool bestAuthored = false;
            bool bestValid = false;
            foreach (MemoryLegacyRecordSnapshot candidate in group)
            {
                bool authored = !string.IsNullOrWhiteSpace(candidate.manualTextOverride)
                    || IsPlayerSource(candidate.sourceKind);
                bool valid = HasValidFactPayload(candidate, map);
                string key = CanonicalTuple(candidate);
                if (best == null)
                {
                    best = candidate;
                    bestKey = key;
                    bestAuthored = authored;
                    bestValid = valid;
                    continue;
                }

                // Rank: authored beats automatic; a fully-valid payload beats one whose values
                // fail the rule grammar (§T13.3 safe-parse preference); then the lowest tuple.
                bool takesOver = authored && !bestAuthored;
                if (!takesOver && authored == bestAuthored && valid && !bestValid)
                {
                    takesOver = true;
                }

                if (!takesOver && authored == bestAuthored && valid == bestValid)
                {
                    takesOver = string.CompareOrdinal(key, bestKey) < 0;
                }

                if (takesOver)
                {
                    best = candidate;
                    bestKey = key;
                    bestAuthored = authored;
                    bestValid = valid;
                }
            }

            return best;
        }

        /// <summary>True when every fact value in the snapshot parses under the current rule
        /// grammar — the §T13.3 "safely parsed" test that prefers keeping valid payloads.</summary>
        private static bool HasValidFactPayload(
            MemoryLegacyRecordSnapshot snapshot,
            Dictionary<string, MemoryLegacyRuleMapEntry> map)
        {
            if (snapshot.factKeys == null)
            {
                return true;
            }

            MemoryLegacyRuleMapEntry entry = null;
            map.TryGetValue((snapshot.eventKind ?? string.Empty).Trim(), out entry);
            int limit = Math.Min(snapshot.factKeys.Count, snapshot.factValues?.Count ?? 0);
            for (int i = 0; i < limit; i++)
            {
                MemoryFactDescriptor descriptor = FindDescriptor(entry, snapshot.factKeys[i]);
                if (descriptor == null)
                {
                    continue;
                }

                if (!MemoryThreadRoutingPolicy.IsValidCanonicalValue(
                        descriptor, snapshot.factValues[i] ?? string.Empty))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The canonical saved-field comparison tuple: identity is equal inside a group,
        /// so this covers topic/tick ordering keys plus payload lists and authored wording.</summary>
        private static string CanonicalTuple(MemoryLegacyRecordSnapshot snapshot)
        {
            var builder = new System.Text.StringBuilder();
            AppendField(builder, snapshot.topicKey);
            builder.Append(OrdinalSegmentCodec.Segment(
                snapshot.tick.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            AppendList(builder, snapshot.participantIds);
            AppendList(builder, snapshot.subjectKeys);
            AppendList(builder, snapshot.factKeys);
            AppendList(builder, snapshot.factValues);
            AppendField(builder, snapshot.manualTextOverride);
            return builder.ToString();
        }

        private static void AppendField(System.Text.StringBuilder builder, string value)
        {
            builder.Append(OrdinalSegmentCodec.Segment(value ?? string.Empty));
        }

        private static void AppendList(
            System.Text.StringBuilder builder, List<string> values)
        {
            int count = values?.Count ?? 0;
            builder.Append(OrdinalSegmentCodec.Segment(
                count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            for (int i = 0; values != null && i < count; i++)
            {
                AppendField(builder, values[i]);
            }
        }

        private static bool IsPlayerSource(string sourceKind)
        {
            return string.Equals(
                sourceKind, KnowledgeTokens.SourceKindPlayer, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameLists(List<string> left, List<string> right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> SortedDistinct(List<string> values)
        {
            var sorted = new SortedSet<string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        sorted.Add(value);
                    }
                }
            }

            return new List<string>(sorted);
        }

        private static bool IsBounded(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumRawIdentityCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }

        private static bool TryHashUtf8(string value, out string hash)
        {
            hash = string.Empty;
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(
                        new System.Text.UTF8Encoding(false, true).GetBytes(value));
                    var builder = new System.Text.StringBuilder(digest.Length * 2);
                    foreach (byte b in digest)
                    {
                        builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    }

                    hash = builder.ToString();
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Canonical report fingerprint: framed ordinal serialization hashed with SHA-256.
        /// Equal plans (idempotent rerun) produce equal fingerprints (§T13.5 fixtures).</summary>
        private static string Fingerprint(MemoryLegacyMigrationReport report)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(OrdinalSegmentCodec.Segment("memory-legacy-migration-report-v1"));
            builder.Append(OrdinalSegmentCodec.Segment(report.ownerPawnId ?? string.Empty));
            builder.Append(OrdinalSegmentCodec.Segment(report.ownerEpochToken ?? string.Empty));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.ownerRemainsRaw ? "1" : "0"));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.droppedAutomaticAlternateCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.archivedAuthoredConflictCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.unmappedEventKindCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.invalidFactValueCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            foreach (MemoryLegacyMappedRecord row in report.rows)
            {
                builder.Append(OrdinalSegmentCodec.Segment(row.disposition ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.sourceOccurrenceId ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.captureRuleId ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.factDiscriminator ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.kindToken ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.categoryToken ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(row.importanceToken ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(
                    row.originalEventTick.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                builder.Append(OrdinalSegmentCodec.Segment(row.ageUnknown ? "1" : "0"));
                builder.Append(OrdinalSegmentCodec.Segment(row.playerEdited ? "1" : "0"));
                builder.Append(OrdinalSegmentCodec.Segment(row.suppressed ? "1" : "0"));
                builder.Append(OrdinalSegmentCodec.Segment(row.provenanceRefId ?? string.Empty));
                builder.Append(OrdinalSegmentCodec.Segment(
                    row.facts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                foreach (MemoryLegacyMappedFact fact in row.facts)
                {
                    builder.Append(OrdinalSegmentCodec.Segment(fact.factId ?? string.Empty));
                    builder.Append(OrdinalSegmentCodec.Segment(
                        fact.originFactOrdinal.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)));
                    builder.Append(OrdinalSegmentCodec.Segment(fact.canonicalValue ?? string.Empty));
                    builder.Append(OrdinalSegmentCodec.Segment(
                        fact.canonicalValueKind ?? string.Empty));
                }
            }

            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(
                        new System.Text.UTF8Encoding(false, true).GetBytes(builder.ToString()));
                    var hex = new System.Text.StringBuilder(digest.Length * 2);
                    foreach (byte b in digest)
                    {
                        hex.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    }

                    return hex.ToString();
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
