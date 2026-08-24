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
        /// <summary>Frozen §T6.8 payload preserved whole for planning/winner comparison.</summary>
        public string dateLabel = string.Empty;
        public string fallbackSummary = string.Empty;
        public List<string> participantIds = new List<string>();
        public List<string> participantNames = new List<string>();
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
        /// <summary>Load-time tick boundary: ticks above it are future/corrupt and map to
        /// ageUnknown/Important instead of a guessed date (§T15.2).</summary>
        public long maxKnownTick = long.MaxValue;
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
        /// <summary>The legacy singleton/player-background row migrates to the envelope's
        /// playerBackground field, never a thread/block (§T15.2/§T13.3).</summary>
        public const string DispositionPlayerBackground = "player_background";

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

        // ---- Full Imported-candidate evidence for authored alternates (§T6.8/§T13.4): every
        // distinct authored alternate archives with its complete bounded payload, never a stub. ----
        public string importedWording = string.Empty;
        public string originRecordId = string.Empty;
        public string dedupKey = string.Empty;
        public string originSourceEventId = string.Empty;
        public string sourceKind = string.Empty;
        public string recallScope = string.Empty;
        public string eventKind = string.Empty;
        public string topicKey = string.Empty;
        public string dateLabel = string.Empty;
        public string fallbackSummary = string.Empty;
        public List<string> participantIds = new List<string>();
        public List<string> participantNames = new List<string>();
        public List<string> subjectKeys = new List<string>();
        public List<string> factKeys = new List<string>();
        public List<string> factValues = new List<string>();

        /// <summary>Background-singleton wording (manual override, else capture fallback).</summary>
        public string backgroundText = string.Empty;
    }

    /// <summary>The bounded dry-run report for one owner group.</summary>
    internal sealed class MemoryLegacyMigrationReport
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        /// <summary>Load-time tick boundary echoed for fingerprint-stable future-tick mapping.</summary>
        public long maxKnownTick = long.MaxValue;
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
                ownerEpochToken = Safe(input?.ownerEpochToken),
                maxKnownTick = input?.maxKnownTick ?? long.MaxValue
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

                // Structural safety belongs to the complete INPUT set, not only the eventual
                // winner. Otherwise a malformed automatic alternate could lose selection and be
                // silently dropped while the owner was incorrectly declared migratable (§T13.1).
                if (!HasSafeParallelListShape(snapshot))
                {
                    report.ownerRemainsRaw = true;
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
                        // Every irreconcilable AUTHORED alternative archives as one COMPLETE
                        // Imported candidate carrying its own bounded payload (§T6.8/§T13.4) —
                        // never a payload-free stub and never a second active identity.
                        var archived = new MemoryLegacyMappedRecord
                        {
                            disposition = MemoryLegacyMappedRecord.DispositionArchiveAuthored,
                            sourceOccurrenceId = occurrence,
                            playerEdited = alternateAuthored
                        };
                        FillImportedEvidence(archived, alternate);
                        report.rows.Add(archived);
                        report.archivedAuthoredConflictCount++;
                    }
                    else
                    {
                        // Conflicting unedited automatic alternates drop with one diagnostic
                        // rather than becoming permanent rows (§T6.8/§T8.4).
                        report.droppedAutomaticAlternateCount++;
                    }
                }

                report.rows.Add(mapped);
            }


            // Canonical §T7.4-style total order: every field participates so equal-key rows stay
            // deterministically ordered regardless of input position or unstable sorts.
            report.rows.Sort(CompareRows);
            report.reportFingerprint = Fingerprint(report);
            return report;
        }

        internal static int CompareRows(
            MemoryLegacyMappedRecord left, MemoryLegacyMappedRecord right)
        {
            // Ordering and fingerprinting share this ONE exhaustive row encoding. Adding a report
            // field without adding it here therefore cannot make two distinct reports compare equal
            // in one place but hash differently in another.
            return string.CompareOrdinal(
                CanonicalMappedRecordEncoding(left),
                CanonicalMappedRecordEncoding(right));
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

            // Hash the normalized occurrence tuple: kinds/scopes/topic/tick plus FRAMED list
            // counts and members, so a value can never move between sets without changing the
            // tuple (§T13.3 arm 1). Values/names/labels stay payload.
            var tupleParts = new List<string>
            {
                Safe(snapshot.sourceKind),
                Safe(snapshot.recallScope),
                Safe(snapshot.eventKind),
                Safe(snapshot.topicKey),
                snapshot.tick.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            // Framed set sizes first, then members — one count per distinct list so values can
            // never migrate between sets without changing the tuple (§T13.3 arm 1).
            List<string> participants = SortedDistinct(snapshot.participantIds);
            List<string> subjects = SortedDistinct(snapshot.subjectKeys);
            List<string> factKeys = SortedDistinct(snapshot.factKeys);
            tupleParts.Add(participants.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tupleParts.AddRange(participants);
            tupleParts.Add(subjects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tupleParts.AddRange(subjects);
            tupleParts.Add(factKeys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tupleParts.AddRange(factKeys);

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
            // Unequal parallel fact lists are structurally unsafe input: the WHOLE owner stays
            // raw/unstamped rather than mapping a truncated shape (§T13.3/§T13.5).
            int keyCount = snapshot.factKeys?.Count ?? 0;
            int valueCount = snapshot.factValues?.Count ?? 0;
            if (keyCount != valueCount)
            {
                report.ownerRemainsRaw = true;
                return null;
            }

            // The legacy singleton/player-background row becomes the envelope's playerBackground
            // field — never a thread/block and never an active memory record (§T15.2/§T13.3).
            bool isBackground = IsPlayerSource(snapshot.sourceKind)
                || string.Equals(
                    snapshot.recallScope, KnowledgeTokens.RecallScopeBackground,
                    StringComparison.OrdinalIgnoreCase);
            if (isBackground)
            {
                var background = new MemoryLegacyMappedRecord
                {
                    disposition = MemoryLegacyMappedRecord.DispositionPlayerBackground,
                    sourceOccurrenceId = occurrence,
                    playerEdited = !string.IsNullOrWhiteSpace(snapshot.manualTextOverride),
                    originalEventTick = snapshot.tick > 0 ? snapshot.tick : 0,
                    ageUnknown = snapshot.tick <= 0
                };
                FillImportedEvidence(background, snapshot);
                background.backgroundText =
                    !string.IsNullOrWhiteSpace(snapshot.manualTextOverride)
                        ? snapshot.manualTextOverride
                        : (snapshot.fallbackSummary ?? string.Empty);
                return background;
            }

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

            // Tick handling: missing/zero/FUTURE/corrupt ticks become ageUnknown Important, never
            // a guessed date (§T15.2). The load-time boundary arrives via input.maxKnownTick.
            if (snapshot.tick > 0 && snapshot.tick <= report.maxKnownTick)
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

            bool anyInvalidFacts = MapFacts(report, mapped, snapshot, rule);
            if (report.ownerRemainsRaw)
            {
                return null;
            }

            // An AUTHORED row whose fact values fail the current grammar keeps its evidence as an
            // archived Imported candidate instead of silently losing facts (§T13.4).
            if (mapped.playerEdited && anyInvalidFacts)
            {
                mapped.disposition = MemoryLegacyMappedRecord.DispositionArchiveAuthored;
                mapped.facts.Clear();
                FillImportedEvidence(mapped, snapshot);
                report.archivedAuthoredConflictCount++;
                return mapped;
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

            FillImportedEvidence(mapped, snapshot);
            return mapped;
        }

        /// <summary>Copies the complete frozen legacy payload onto the row so authored alternates
        /// round-trip as full Imported candidates (§T6.8) and winner comparison sees every field.</summary>
        private static void FillImportedEvidence(
            MemoryLegacyMappedRecord mapped, MemoryLegacyRecordSnapshot snapshot)
        {
            mapped.importedWording = !string.IsNullOrWhiteSpace(snapshot.manualTextOverride)
                ? snapshot.manualTextOverride
                : (snapshot.fallbackSummary ?? string.Empty);
            mapped.originRecordId = Safe(snapshot.recordId);
            mapped.dedupKey = Safe(snapshot.dedupKey);
            mapped.originSourceEventId = Safe(snapshot.sourceEventId);
            mapped.sourceKind = Safe(snapshot.sourceKind);
            mapped.recallScope = Safe(snapshot.recallScope);
            mapped.eventKind = Safe(snapshot.eventKind);
            mapped.topicKey = Safe(snapshot.topicKey);
            mapped.dateLabel = Safe(snapshot.dateLabel);
            mapped.fallbackSummary = Safe(snapshot.fallbackSummary);
            CopyList(snapshot.participantIds, mapped.participantIds);
            CopyList(snapshot.participantNames, mapped.participantNames);
            CopyList(snapshot.subjectKeys, mapped.subjectKeys);
            CopyList(snapshot.factKeys, mapped.factKeys);
            CopyList(snapshot.factValues, mapped.factValues);
        }

        private static void CopyList(List<string> source, List<string> target)
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

        /// <summary>Maps legacy fact key/value pairs through the rule's descriptors. Returns true
        /// when at least one value failed the current grammar (callers decide the conservative
        /// disposition for authored rows); safely parsed automatic data without a descriptor drops
        /// with one bounded diagnostic rather than guessing a grammar (§T13.3).</summary>
        private static bool MapFacts(
            MemoryLegacyMigrationReport report,
            MemoryLegacyMappedRecord mapped,
            MemoryLegacyRecordSnapshot snapshot,
            MemoryLegacyRuleMapEntry rule)
        {
            bool anyInvalid = false;
            if (snapshot.factKeys == null || snapshot.factValues == null)
            {
                return false;
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
                    anyInvalid = true;
                    continue;
                }

                if (!MemoryThreadRoutingPolicy.IsValidCanonicalValue(descriptor, value))
                {
                    report.invalidFactValueCount++;
                    anyInvalid = true;
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
                    return true;
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
            // collapse; a conflicting VALUE under one fact identity is an §T8.4 collision — the
            // alternate drops with one bounded diagnostic, never two facts under one identity
            // (§T13.3 step 4). Ordinals are zero-based in the surviving canonical order.
            candidates.Sort((left, right) =>
            {
                int compare = string.CompareOrdinal(left.factId, right.factId);
                if (compare != 0) return compare;
                compare = string.CompareOrdinal(left.canonicalValue, right.canonicalValue);
                if (compare != 0) return compare;
                return string.CompareOrdinal(left.canonicalValueKind, right.canonicalValueKind);
            });

            MemoryLegacyMappedFact previous = null;
            foreach (MemoryLegacyMappedFact fact in candidates)
            {
                if (previous != null
                    && string.Equals(previous.factId, fact.factId, StringComparison.Ordinal))
                {
                    bool byteEqual =
                        string.Equals(previous.canonicalValue, fact.canonicalValue, StringComparison.Ordinal)
                        && string.Equals(previous.canonicalValueKind, fact.canonicalValueKind, StringComparison.Ordinal);
                    if (byteEqual)
                    {
                        continue;
                    }

                    // Same canonical identity, different payload: collision, drop this alternate.
                    report.invalidFactValueCount++;
                    continue;
                }

                previous = fact;
                fact.originFactOrdinal = mapped.facts.Count;
                mapped.facts.Add(fact);
            }

            return anyInvalid;
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
            // Complete semantic equality over every frozen legacy field. Identity precedence may
            // group rows under one occurrence, but no preserved payload field may disappear merely
            // because the other row won canonical selection (§T8.4/§T13.4).
            return left != null && right != null
                && string.Equals(left.recordId, right.recordId, StringComparison.Ordinal)
                && string.Equals(left.dedupKey, right.dedupKey, StringComparison.Ordinal)
                && string.Equals(left.sourceEventId, right.sourceEventId, StringComparison.Ordinal)
                && string.Equals(left.sourceKind, right.sourceKind, StringComparison.Ordinal)
                && string.Equals(left.recallScope, right.recallScope, StringComparison.Ordinal)
                && string.Equals(left.eventKind, right.eventKind, StringComparison.Ordinal)
                && string.Equals(left.topicKey, right.topicKey, StringComparison.Ordinal)
                && left.tick == right.tick
                && string.Equals(left.dateLabel, right.dateLabel, StringComparison.Ordinal)
                && string.Equals(left.fallbackSummary, right.fallbackSummary, StringComparison.Ordinal)
                && SameLists(left.participantIds, right.participantIds)
                && SameLists(left.participantNames, right.participantNames)
                && SameLists(left.subjectKeys, right.subjectKeys)
                && SameLists(left.factKeys, right.factKeys)
                && SameLists(left.factValues, right.factValues)
                && string.Equals(
                    left.manualTextOverride,
                    right.manualTextOverride,
                    StringComparison.Ordinal)
                && mapped != null;
        }

        private static bool HasSafeParallelListShape(MemoryLegacyRecordSnapshot snapshot)
        {
            return snapshot != null
                && (snapshot.factKeys?.Count ?? 0) == (snapshot.factValues?.Count ?? 0);
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

        /// <summary>The COMPLETE canonical saved-field comparison tuple (§T8.4 step 8): every
        /// identity and payload field participates, so equal keys can never retain an
        /// input-position-dependent row under permutation.</summary>
        private static string CanonicalTuple(MemoryLegacyRecordSnapshot snapshot)
        {
            var builder = new System.Text.StringBuilder();
            AppendField(builder, snapshot.recordId);
            AppendField(builder, snapshot.dedupKey);
            AppendField(builder, snapshot.sourceEventId);
            AppendField(builder, snapshot.sourceKind);
            AppendField(builder, snapshot.recallScope);
            AppendField(builder, snapshot.eventKind);
            AppendField(builder, snapshot.topicKey);
            AppendField(builder, snapshot.dateLabel);
            AppendField(builder, snapshot.fallbackSummary);
            AppendField(builder, snapshot.manualTextOverride);
            AppendList(builder, snapshot.participantNames);
            builder.Append(OrdinalSegmentCodec.Segment(
                snapshot.tick.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            AppendList(builder, snapshot.participantIds);
            AppendList(builder, snapshot.subjectKeys);
            AppendList(builder, snapshot.factKeys);
            AppendList(builder, snapshot.factValues);
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

        /// <summary>
        /// Complete canonical row encoding used by BOTH report ordering and fingerprinting. It
        /// includes every scalar, every preserved legacy list, and every nested fact field.
        /// </summary>
        internal static string CanonicalMappedRecordEncoding(MemoryLegacyMappedRecord row)
        {
            var builder = new System.Text.StringBuilder();
            row = row ?? new MemoryLegacyMappedRecord();
            AppendField(builder, row.disposition);
            AppendField(builder, row.sourceOccurrenceId);
            AppendField(builder, row.captureRuleId);
            AppendField(builder, row.factDiscriminator);
            AppendField(builder, row.kindToken);
            AppendField(builder, row.categoryToken);
            AppendField(builder, row.importanceToken);
            AppendField(builder, row.originalEventTick.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendField(builder, row.ageUnknown ? "1" : "0");
            AppendField(builder, row.playerEdited ? "1" : "0");
            AppendField(builder, row.suppressed ? "1" : "0");
            AppendField(builder, row.provenanceRefId);
            AppendField(builder, row.importedWording);
            AppendField(builder, row.originRecordId);
            AppendField(builder, row.dedupKey);
            AppendField(builder, row.originSourceEventId);
            AppendField(builder, row.sourceKind);
            AppendField(builder, row.recallScope);
            AppendField(builder, row.eventKind);
            AppendField(builder, row.topicKey);
            AppendField(builder, row.dateLabel);
            AppendField(builder, row.fallbackSummary);
            AppendList(builder, row.participantIds);
            AppendList(builder, row.participantNames);
            AppendList(builder, row.subjectKeys);
            AppendList(builder, row.factKeys);
            AppendList(builder, row.factValues);
            AppendField(builder, row.backgroundText);

            List<MemoryLegacyMappedFact> facts = row.facts ?? new List<MemoryLegacyMappedFact>();
            AppendField(builder, facts.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < facts.Count; i++)
            {
                MemoryLegacyMappedFact fact = facts[i] ?? new MemoryLegacyMappedFact();
                AppendField(builder, fact.originFactOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                AppendField(builder, fact.factId);
                AppendField(builder, fact.factKind);
                AppendField(builder, fact.canonicalSubjectKind);
                AppendField(builder, fact.canonicalSubjectId);
                AppendField(builder, fact.aggregationToken);
                AppendField(builder, fact.canonicalValueKind);
                AppendField(builder, fact.canonicalValue);
            }

            return builder.ToString();
        }

        /// <summary>Canonical report fingerprint: framed ordinal serialization hashed with SHA-256.
        /// Equal plans (idempotent rerun) produce equal fingerprints (§T13.5 fixtures).</summary>
        internal static string Fingerprint(MemoryLegacyMigrationReport report)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(OrdinalSegmentCodec.Segment("memory-legacy-migration-report-v1"));
            builder.Append(OrdinalSegmentCodec.Segment(report.ownerPawnId ?? string.Empty));
            builder.Append(OrdinalSegmentCodec.Segment(report.ownerEpochToken ?? string.Empty));
            builder.Append(OrdinalSegmentCodec.Segment(
                report.maxKnownTick.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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
                builder.Append(OrdinalSegmentCodec.Segment(
                    CanonicalMappedRecordEncoding(row)));
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
