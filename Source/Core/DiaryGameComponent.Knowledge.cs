// DiaryGameComponent.Knowledge.cs — the impure adapter for the deterministic pawn-knowledge
// system (design/MEMORY_SYSTEM_REDESIGN_PLAN.md). Replaces the old associative memory partial.
//
// Responsibilities:
//  - CAPTURE: classify gameplay signals against the XML important-event allowlist and persist
//    detached ImportantMemoryRecords on the owners' PawnKnowledgeState. Capture runs regardless
//    of the player's memory switch — that switch gates PROMPT INJECTION only (§3.2).
//  - CULTURE: resolve each pawn's origin culture once (ideology culture with Ideology active,
//    else origin faction's allowed cultures) and replace the adopted culture on conversion
//    (§4.1). Legacy saves mark their inferred origins and never silently rewrite them.
//  - RETRIEVAL: for each just-registered event, run the deterministic selector over the writer's
//    records and freeze at most two localized "relevant past" lines onto the PovSlot's
//    memoryContext (§3), reusing the existing MemoryContext prompt plumbing.
//  - LIMITS: per-pawn/global caps with absent-owner-first global eviction (§2.3).
//
// New to C#/RimWorld? This is a `partial class` — one class split across files by concern. All
// pure decisions live in Source/Pipeline/Knowledge; this file only gathers snapshots, calls the
// pure helpers, and persists/freezes their results.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Component-level knowledge schema. Versions below 2 used a transient legacy
        /// culture marker; the load pass durably marks unresolved cultures, then stamps 2 (§6).</summary>
        private int knowledgeSchemaVersion;

        /// <summary>Last retrieval report per pawn for the dev tab (§7). Transient, bounded by
        /// the number of diaried pawns.</summary>
        private readonly Dictionary<string, KnowledgeDebugReport> knowledgeReportsByPawnId =
            new Dictionary<string, KnowledgeDebugReport>();

        /// <summary>Dev-tab view of one retrieval run (§7).</summary>
        internal sealed class KnowledgeDebugReport
        {
            public string eventId = string.Empty;
            public int tick;
            public List<string> queryParticipantIds = new List<string>();
            public List<string> querySubjectKeys = new List<string>();
            public List<string> queryTopicKeys = new List<string>();
            public List<KnowledgeCandidateReport> candidates = new List<KnowledgeCandidateReport>();
            public List<string> selectedRecordIds = new List<string>();
            public List<string> matchedCultureTopics = new List<string>();
            public List<string> annotatedFieldSources = new List<string>();
        }

        // ── Persistence ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Scribes the component-level schema version plus every unified-memory §T6.9
        /// component field (see DiaryGameComponent.MemoryStore.cs). The per-pawn state itself rides
        /// PawnDiaryRecord.knowledgeState (Scribe_Deep), not this partial.</summary>
        private void ExposeKnowledgeData()
        {
            Scribe_Values.Look(ref knowledgeSchemaVersion, "knowledgeSchemaVersion", 0);
            ExposeMemoryComponentData();
        }

        /// <summary>
        /// One-time clean start (§6): a save from before the redesign (version 0) keeps its diary
        /// records but starts important-event history from now; old associative fragments and
        /// lore-seed rosters are simply never read again. Existing pawns are marked so their
        /// origin culture resolves as "inferred".
        /// </summary>
        private void PostLoadInitKnowledge()
        {
            if (knowledgeSchemaVersion < 2)
            {
                if (diaries != null)
                {
                    for (int i = 0; i < diaries.Count; i++)
                    {
                        PawnDiaryRecord diary = diaries[i];
                        if (diary != null && !string.IsNullOrWhiteSpace(diary.pawnId))
                        {
                            PawnKnowledgeState state = EnsureKnowledgeState(diary);
                            if (string.IsNullOrWhiteSpace(state.originCultureDefName)
                                && string.IsNullOrWhiteSpace(state.originCultureSource))
                            {
                                // Persist the provenance marker immediately. A player may save again
                                // before this pawn emits another event; a transient id set would lose
                                // the fact that the later resolution is inferred from a legacy save.
                                state.originCultureSource = KnowledgeTokens.CultureSourceInferred;
                            }
                        }
                    }
                }

                knowledgeSchemaVersion = 2;
            }

            // The outer ExposeData PostLoadInit boundary already performed the recursive §14.6
            // newer-schema refusal before any repository/index publication. This M1 phase gate now
            // repairs allocator carriers before report-only migration planning and index rebuild.
            try
            {
                CollectAndPublishAllocatorCarriers();
                RunMemoryMigration();
                RebuildMemorySizeIndexes();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Memory load-repair pipeline failed: " + e,
                    "PawnDiary.MemoryLoadRepair".GetHashCode());
            }
        }

        /// <summary>Thrown when any saved memory row carries a schema version newer than this
        /// build understands; RimWorld surfaces the exception as a failed load (§14.6).</summary>
        internal sealed class NewerPawnDiarySaveFormatException : Exception
        {
            public NewerPawnDiarySaveFormatException(string message)
                : base(message)
            {
            }
        }

        /// <summary>
        /// §14.6 downgrade boundary: ordinary IExposable loading cannot preserve unknown child
        /// nodes, so ANY row whose schemaVersion exceeds this build's current value fails the
        /// whole save load closed BEFORE FinalizeInit/gameplay and before any mutation.
        /// </summary>
        internal void ScanForNewerMemorySchemas()
        {
            if (memoryComponentSchemaVersion > 1
                || memoryCoordinatorSchemaVersion > 1
                || memoryDispatchSchemaVersion > 1)
            {
                throw new NewerPawnDiarySaveFormatException(
                    "[Pawn Diary] " + "PawnDiary.SaveFormatNewer".Translate()
                    + " (component schema "
                    + Math.Max(memoryComponentSchemaVersion,
                        Math.Max(memoryCoordinatorSchemaVersion, memoryDispatchSchemaVersion))
                    + ")");
            }

            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord diary = diaries[i];
                    PawnKnowledgeState state = diary?.knowledgeState;
                    if (state == null)
                    {
                        continue;
                    }

                    if (PawnKnowledgeStateSchemaPolicy.Classify(state.schemaVersion)
                        == PawnKnowledgeStateSchemaPolicy.VersionClass.NewerThanCurrent)
                    {
                        throw new NewerPawnDiarySaveFormatException(
                            "[Pawn Diary] " + "PawnDiary.SaveFormatNewer".Translate()
                            + " (" + (diary.pawnId ?? "?") + " schema " + state.schemaVersion + ")");
                    }

                    RequireNotNewer(
                        state.threadRoots,
                        r => r.SchemaVersionForBoundaryCheck,
                        RequireNestedSchemas);
                    RequireNotNewer(
                        state.standaloneBlocks,
                        r => r.SchemaVersionForBoundaryCheck,
                        RequireNestedSchemas);
                    RequireNotNewer(
                        state.ownerAwarenessSnapshots,
                        r => r.SchemaVersionForBoundaryCheck,
                        RequireNestedSchemas);
                    RequireNotNewer(
                        state.openCaptureEpisodes,
                        r => r.SchemaVersionForBoundaryCheck,
                        RequireNestedSchemas);
                    RequireNotNewer(state.repetitionGuardRows, r => r.SchemaVersionForBoundaryCheck);
                    RequireNotNewer(
                        state.importedArchiveRows,
                        r => r.SchemaVersionForBoundaryCheck,
                        RequireNestedSchemas);
                    if (diary.reflectionState != null
                        && diary.reflectionState.memoryReflectionSchemaVersion > 1)
                    {
                        throw new NewerPawnDiarySaveFormatException(
                            "[Pawn Diary] " + "PawnDiary.SaveFormatNewer".Translate()
                            + " (reflection schema "
                            + diary.reflectionState.memoryReflectionSchemaVersion + ")");
                    }
                }
            }

            RequireNotNewer(globalFactionSnapshots, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(legacyOwnerEpochReservations, r => r.SchemaVersionForBoundaryCheck);
            RequireRowNotNewer(
                lastAppliedMemoryPolicyState,
                r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(
                unresolvedOwnerArchiveRows,
                r => r.SchemaVersionForBoundaryCheck,
                RequireNestedSchemas);
            RequireNotNewer(rawUnresolvedOwnerArchiveInput, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(summaryWordingOpportunities, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(memoryDiagnosticCounters, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(memoryAttemptAuditRows, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(
                activeMemoryCoordinatorRequests,
                r => r.SchemaVersionForBoundaryCheck,
                RequireNestedSchemas);

            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                RequireActiveMemoryRequestNotNewer(
                    hotEvents[index]?.ActiveMemoryLogicalRequestForRole(
                        DiaryEvent.InitiatorRole));
                RequireActiveMemoryRequestNotNewer(
                    hotEvents[index]?.ActiveMemoryLogicalRequestForRole(
                        DiaryEvent.RecipientRole));
                RequireActiveMemoryRequestNotNewer(
                    hotEvents[index]?.ActiveMemoryLogicalRequestForRole(
                        DiaryEvent.NeutralRole));
            }
        }

        /// <summary>
        /// Applies the same recursive downgrade boundary to component- and DiaryEvent-owned M2 rows.
        /// DiaryEvent invokes this before load normalization so an older build never mutates a newer
        /// outer or nested schema before the component-level safety sweep runs.
        /// </summary>
        internal static void RequireActiveMemoryRequestNotNewer(
            SavedActiveLogicalRequestV1 request)
        {
            RequireRowNotNewer(request, r => r.SchemaVersionForBoundaryCheck);
            if (request != null)
            {
                RequireNestedSchemas(request);
            }
        }

        private static void RequireNotNewer<T>(
            List<T> rows,
            Func<T, int> schemaVersionOf,
            Action<T> requireNested = null)
            where T : class
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (row == null)
                {
                    continue;
                }

                RequireRowNotNewer(row, schemaVersionOf);
                requireNested?.Invoke(row);
            }
        }

        private static void RequireRowNotNewer<T>(T row, Func<T, int> schemaVersionOf)
            where T : class
        {
            if (row == null)
            {
                return;
            }

            int schemaVersion = schemaVersionOf(row);
            if (schemaVersion > 1)
            {
                throw new NewerPawnDiarySaveFormatException(
                    "[Pawn Diary] " + "PawnDiary.SaveFormatNewer".Translate()
                    + " (" + row.GetType().Name + " schema " + schemaVersion + ")");
            }
        }

        private static void RequireNestedSchemas(SavedMemoryThreadRoot root)
        {
            RequireNotNewer(root.chapters, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(
                root.visibleBlocks,
                r => r.SchemaVersionForBoundaryCheck,
                RequireNestedSchemas);
            RequireRowNotNewer(
                root.rollingSummaryBlock,
                r => r.SchemaVersionForBoundaryCheck);
            if (root.rollingSummaryBlock != null)
            {
                RequireNestedSchemas(root.rollingSummaryBlock);
            }
        }

        private static void RequireNestedSchemas(SavedMemoryBlock block)
        {
            RequireRowNotNewer(block.primarySubject, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(block.secondarySubjects, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(block.facts, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(block.provenance, r => r.SchemaVersionForBoundaryCheck);
            RequireRowNotNewer(block.summaryPayload, r => r.SchemaVersionForBoundaryCheck);
            if (block.summaryPayload != null)
            {
                RequireNestedSchemas(block.summaryPayload);
            }
        }

        private static void RequireNestedSchemas(SavedMemorySummaryPayload payload)
        {
            RequireNotNewer(
                payload.factBuckets,
                r => r.SchemaVersionForBoundaryCheck,
                RequireNestedSchemas);
            RequireNotNewer(payload.subjectRefs, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(payload.provenanceRefs, r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedMemoryFactBucket bucket)
        {
            RequireNotNewer(bucket.contributions, r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedMemoryAwarenessSnapshot snapshot)
        {
            RequireNotNewer(snapshot.stateFacts, r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedMemoryCaptureEpisode episode)
        {
            RequireNotNewer(episode.baselineFacts, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(episode.currentFacts, r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedImportedMemoryRow row)
        {
            RequireRowNotNewer(row.primarySubject, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(row.secondarySubjects, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(row.canonicalFacts, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(row.provenance, r => r.SchemaVersionForBoundaryCheck);
            RequireRowNotNewer(
                row.summaryContributionEvidence,
                r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedActiveLogicalRequestV1 request)
        {
            RequireNotNewer(
                request.frozenVariants,
                r => r.SchemaVersionForBoundaryCheck,
                RequireNestedSchemas);
            RequireNotNewer(request.activeAttempts, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(request.reservedEvidenceEntries, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(request.reservedGuardEntries, r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedFrozenPromptVariantV1 variant)
        {
            RequireRowNotNewer(variant.receiptPlan, r => r.SchemaVersionForBoundaryCheck);
            if (variant.receiptPlan != null)
            {
                RequireNestedSchemas(variant.receiptPlan);
            }

            RequireNotNewer(
                variant.diagnosticProvenance,
                r => r.SchemaVersionForBoundaryCheck);
        }

        private static void RequireNestedSchemas(SavedFrozenEvidenceReceiptPlanV1 receipt)
        {
            RequireNotNewer(receipt.evidenceEntries, r => r.SchemaVersionForBoundaryCheck);
            RequireNotNewer(receipt.guardEntries, r => r.SchemaVersionForBoundaryCheck);
        }

        private void ResetKnowledgeForNewGame()
        {
            knowledgeSchemaVersion = 2;
            knowledgeReportsByPawnId.Clear();
            ResetMemoryComponentForNewGame();
        }

        // ── Capture: diary-event channel (called from the EventFactory funnels) ──────────────────────

        /// <summary>
        /// Classifies a just-registered diary event against the important-event allowlist and
        /// persists records for its owners, then resolves culture for both live POVs. Failure
        /// isolation per the NarrativeContextBuilder convention: a knowledge failure must never
        /// abort event registration.
        /// </summary>
        private void CaptureKnowledgeForEvent(DiaryEvent diaryEvent, Pawn initiator, Pawn recipient)
        {
            if (diaryEvent == null
                || string.Equals(
                    diaryEvent.interactionDefName,
                    SocialReflectionEventData.DefNameToken,
                    StringComparison.OrdinalIgnoreCase))
            {
                // H7 may consume at most two subject-specific memories, but can never deposit itself
                // as new durable evidence for H7/day/quadrum/arc recursion.
                return;
            }

            try
            {
                EnsureCultureResolved(initiator);
                EnsureCultureResolved(recipient);

                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalEvent,
                    defName = diaryEvent.interactionDefName ?? string.Empty,
                    sourceEventId = diaryEvent.eventId ?? string.Empty,
                    sourceOccurrenceId = diaryEvent.eventId ?? string.Empty,
                    tick = diaryEvent.tick,
                    dateLabel = diaryEvent.date ?? string.Empty,
                    gameContext = diaryEvent.gameContext ?? string.Empty,
                    initiatorPawnId = diaryEvent.initiatorPawnId ?? string.Empty,
                    initiatorName = diaryEvent.initiatorName ?? string.Empty,
                    recipientPawnId = diaryEvent.recipientPawnId ?? string.Empty,
                    recipientName = diaryEvent.recipientName ?? string.Empty
                };
                PersistDrafts(ImportantEventClassifier.Classify(
                    signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Knowledge capture failed for event "
                    + (diaryEvent.eventId ?? "?") + ": " + e,
                    "PawnDiary.Knowledge.Capture".GetHashCode());
            }
        }

        /// <summary>
        /// Captures one allowlisted event directly from its source snapshot when page policy rejected
        /// the DiaryEvent. This is intentionally the same classifier/persistence path as
        /// <see cref="CaptureKnowledgeForEvent"/> but creates missing diary records for eligible POVs,
        /// because an arrival or first disabled page may be the pawn's first durable state.
        /// </summary>
        internal void CaptureEventKnowledgeWithoutPage(Pawn initiator, Pawn recipient, string defName,
            string gameContext, int tick, string sourceOwnedOccurrenceId = null)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            try
            {
                PawnDiaryRecord initiatorDiary = EnsureKnowledgeOwner(initiator);
                PawnDiaryRecord recipientDiary = EnsureKnowledgeOwner(recipient);
                if (initiatorDiary == null && recipientDiary == null)
                {
                    return;
                }

                EnsureCultureResolved(initiator);
                EnsureCultureResolved(recipient);
                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalEvent,
                    defName = defName,
                    // DiarySignals reach this seam only after source dedup accepts the occurrence.
                    // A source whose dedup domain distinguishes same-tick occurrences carries that
                    // stable key; otherwise the generic type/subject key proves the bounded fallback.
                    sourceOccurrenceId = sourceOwnedOccurrenceId ?? string.Empty,
                    sourceLocalSequenceInvariant = 0,
                    sourceProvesUniqueness = true,
                    tick = Math.Max(0, tick),
                    dateLabel = KnowledgeDateLabelAt(initiator ?? recipient, tick),
                    gameContext = gameContext ?? string.Empty,
                    initiatorPawnId = initiatorDiary?.pawnId ?? string.Empty,
                    initiatorName = initiator == null
                        ? string.Empty
                        : DiaryLineCleaner.CleanLine(initiator.LabelShortCap),
                    recipientPawnId = recipientDiary?.pawnId ?? string.Empty,
                    recipientName = recipient == null
                        ? string.Empty
                        : DiaryLineCleaner.CleanLine(recipient.LabelShortCap)
                };
                PersistDrafts(ImportantEventClassifier.Classify(
                    signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] No-page knowledge capture failed for "
                    + defName + ": " + e,
                    ("PawnDiary.Knowledge.NoPage." + defName).GetHashCode());
            }
        }

        private PawnDiaryRecord EnsureKnowledgeOwner(Pawn pawn)
        {
            return IsDiaryEligible(pawn) ? FindDiary(pawn, true) : null;
        }

        /// <summary>
        /// Resolves origin culture at the arrival boundary even when the arrival page is disabled.
        /// A captured pre-SetFaction value wins; starting pawns use the ordinary current-state fallback.
        /// </summary>
        internal void CaptureOriginCulture(Pawn pawn, string capturedOriginCultureDefName)
        {
            if (EnsureKnowledgeOwner(pawn) != null)
            {
                EnsureCultureResolved(pawn, capturedOriginCultureDefName);
            }
        }

        // ── Capture: dedicated channels (quiet hediffs, roles, conversion, death fan-out) ────────────

        /// <summary>
        /// Quiet-hediff channel (§2.1): sees every appeared persistent hediff BEFORE diary-page
        /// policy, so XML-allowlisted conditions (luciferium, sterilization) are remembered even
        /// when no page is generated. `removed` switches to the removal channel used for
        /// implant/prosthetic removal, which has no diary page at all.
        /// </summary>
        internal void CaptureHediffKnowledge(Pawn pawn, string hediffDefName, string hediffLabel,
            string partDefName, string partLabel, bool addedPartOrImplant, bool removed)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(hediffDefName))
            {
                return;
            }

            try
            {
                // AddHediff is a combat-hot hook. Reject the overwhelming majority of wounds and
                // temporary conditions by XML-derived identity before allocating context strings,
                // resolving dates, or snapshotting the full policy.
                string signalDefName = removed && addedPartOrImplant
                    ? hediffDefName + "_" + BodyPartEventPolicy.KindAddedPart
                    : hediffDefName;
                string signalToken = removed
                    ? KnowledgeTokens.SignalHediffRemoved
                    : KnowledgeTokens.SignalHediffQuiet;
                if (!ImportantEventClassifier.MayMatchIdentity(
                    signalToken, signalDefName, DiaryKnowledgePolicy.ImportantEventRules()))
                {
                    return;
                }

                PawnDiaryRecord diary = EnsureKnowledgeOwner(pawn);
                if (diary == null)
                {
                    return;
                }

                // The removal channel reuses the event channel's "<def>_addedpart" suffix naming
                // so XML rows can match structurally without enumerating every implant defName.
                string context = "hediff=" + GameContextValue.Sanitize(hediffDefName)
                    + "; label=" + GameContextValue.Sanitize(hediffLabel)
                    + (string.IsNullOrWhiteSpace(partLabel)
                        ? string.Empty
                        : "; body_part=" + GameContextValue.Sanitize(partLabel))
                    + (string.IsNullOrWhiteSpace(partDefName)
                        ? string.Empty
                        : "; part_def=" + GameContextValue.Sanitize(partDefName));
                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = signalToken,
                    defName = signalDefName,
                    tick = Find.TickManager.TicksGame,
                    dateLabel = KnowledgeDateLabelNow(pawn),
                    gameContext = context,
                    providedOwnerPawnId = diary.pawnId
                };
                PersistDrafts(ImportantEventClassifier.Classify(
                    signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Hediff knowledge capture failed: " + e,
                    "PawnDiary.Knowledge.Hediff".GetHashCode());
            }
        }

        /// <summary>Ideological role appointment/removal (§2.1) — capture-only, no diary page.</summary>
        internal void CaptureRoleKnowledge(Pawn pawn, string roleLabel, string ideoName, bool assigned)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(roleLabel))
            {
                return;
            }

            try
            {
                PawnDiaryRecord diary = EnsureKnowledgeOwner(pawn);
                if (diary == null)
                {
                    return;
                }

                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = assigned ? KnowledgeTokens.SignalRoleAssigned : KnowledgeTokens.SignalRoleUnassigned,
                    defName = assigned ? "PawnDiary_RoleAssigned" : "PawnDiary_RoleUnassigned",
                    tick = Find.TickManager.TicksGame,
                    dateLabel = KnowledgeDateLabelNow(pawn),
                    gameContext = "role=" + GameContextValue.Sanitize(roleLabel)
                        + (string.IsNullOrWhiteSpace(ideoName)
                            ? string.Empty
                            : "; ideo=" + GameContextValue.Sanitize(ideoName)),
                    providedOwnerPawnId = diary.pawnId
                };
                PersistDrafts(ImportantEventClassifier.Classify(
                    signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Role knowledge capture failed: " + e,
                    "PawnDiary.Knowledge.Role".GetHashCode());
            }
        }

        /// <summary>
        /// Ideology conversion (§2.1, §4.1): replaces the pawn's adopted culture and records the
        /// conversion. Called from the SetIdeo listener with the change already proven (old ideo
        /// non-null and different).
        /// </summary>
        internal void CaptureIdeoConversionKnowledge(Pawn pawn, string previousIdeoName,
            string newIdeoName, string newCultureDefName)
        {
            CaptureIdeoConversionKnowledge(
                pawn, previousIdeoName, newIdeoName, newCultureDefName, string.Empty);
        }

        /// <summary>
        /// Conversion capture with the pre-mutation culture used to resolve origin before adopted
        /// culture changes. The four-argument overload remains for test/binary compatibility.
        /// </summary>
        internal void CaptureIdeoConversionKnowledge(Pawn pawn, string previousIdeoName,
            string newIdeoName, string newCultureDefName, string previousCultureDefName)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                PawnDiaryRecord diary = EnsureKnowledgeOwner(pawn);
                if (diary == null)
                {
                    return;
                }

                PawnKnowledgeState state = EnsureKnowledgeState(diary);
                string priorOrigin = state.originCultureDefName ?? string.Empty;
                string priorSource = state.originCultureSource ?? string.Empty;
                string priorAdopted = state.adoptedCultureDefName ?? string.Empty;
                EnsureCultureResolved(pawn, previousCultureDefName, false);
                // Conversion REPLACES the latest adopted culture; earlier adopted cultures are
                // not retained (§4.1).
                CultureStateSnapshot converted = CultureResolver.ApplyConversion(
                    state.ToCultureSnapshot(), newCultureDefName);
                state.originCultureDefName = converted.originCultureDefName;
                state.originCultureSource = converted.originSource;
                state.adoptedCultureDefName = converted.adoptedCultureDefName;
                if (MemoryLibraryPolicy.CultureProjectionChanged(
                    priorOrigin,
                    priorSource,
                    priorAdopted,
                    state.originCultureDefName,
                    state.originCultureSource,
                    state.adoptedCultureDefName))
                {
                    // EnsureCultureResolved may already have published an origin change. The
                    // conversion then replaces adopted culture, so publish the final exact owner
                    // size as one more cheap owner-only refresh.
                    if (!RefreshMemorySizeIndexForOwner(state)) RebuildMemorySizeIndexes();
                    MarkMemoryLibraryCultureProjectionDirty();
                }

                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalIdeoConversion,
                    defName = "PawnDiary_IdeoConversion",
                    tick = Find.TickManager.TicksGame,
                    dateLabel = KnowledgeDateLabelNow(pawn),
                    gameContext = "previous_ideo=" + GameContextValue.Sanitize(previousIdeoName)
                        + "; new_ideo=" + GameContextValue.Sanitize(newIdeoName)
                        + "; new_culture=" + GameContextValue.Sanitize(newCultureDefName),
                    providedOwnerPawnId = diary.pawnId
                };
                PersistDrafts(ImportantEventClassifier.Classify(
                    signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Conversion knowledge capture failed: " + e,
                    "PawnDiary.Knowledge.Conversion".GetHashCode());
            }
        }

        /// <summary>
        /// Death fan-out (§2.1): the instigator pawn and the deceased's lover/spouse/fiance,
        /// parents, children, and siblings each keep one record. Ordinary witnesses never do.
        /// Runs from the Pawn.Kill listener while the victim's relations are still readable.
        /// </summary>
        internal void CaptureDeathKnowledge(Pawn victim, DamageInfo? dinfo)
        {
            if (victim == null)
            {
                return;
            }

            try
            {
                string victimName = victim.LabelShort ?? string.Empty;
                int tick = Find.TickManager.TicksGame;
                string date = KnowledgeDateLabelNow(victim);
                string victimId = victim.GetUniqueLoadID();
                string deathOccurrenceId;
                MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(
                    new MemorySourceOccurrenceFallback
                    {
                        stableSignalToken = "death",
                        eventTickInvariant = tick,
                        sourceLocalSequenceInvariant = 0,
                        factDiscriminator = "death",
                        sourceProvesUniqueness = true,
                        subjects = new List<MemoryTypedSubject>
                        {
                            new MemoryTypedSubject
                            {
                                subjectKind = MemoryContractTokens.SubjectPawn,
                                subjectId = victimId
                            }
                        }
                    },
                    out deathOccurrenceId);

                Pawn instigator = dinfo.HasValue ? dinfo.Value.Instigator as Pawn : null;
                if (instigator != null && instigator != victim
                    && EnsureKnowledgeOwner(instigator) != null)
                {
                    string weaponLabel = dinfo.Value.Weapon != null ? dinfo.Value.Weapon.label : string.Empty;
                    EmitDeathSignal(KnowledgeTokens.SignalDeathInstigator, "PawnDiary_DeathInstigator",
                        instigator, victimId, victimName, tick, date,
                        "victim=" + GameContextValue.Sanitize(victimName)
                        + (string.IsNullOrWhiteSpace(weaponLabel)
                            ? string.Empty
                            : "; weapon=" + GameContextValue.Sanitize(weaponLabel)),
                        deathOccurrenceId);
                }

                // Close family only (§2.1). PotentiallyRelatedPawns gives us candidates without first
                // asking every relation worker to evaluate them. That distinction matters when another
                // mod leaves one malformed direct-relation row: RelatedPawns would throw before we could
                // isolate the bad candidate and would prevent healthy family owners later in the list
                // from receiving their record.
                if (victim.relations == null)
                {
                    return;
                }

                int familyOwnersEmitted = 0;
                foreach (Pawn other in victim.relations.PotentiallyRelatedPawns)
                {
                    if (!KnowledgeRelationPolicy.CanEmitDeathFamilyOwner(familyOwnersEmitted))
                    {
                        break;
                    }

                    if (other == null || other == victim || other == instigator
                        || !IsDiaryEligible(other))
                    {
                        continue;
                    }

                    string relationLabel;
                    try
                    {
                        // GetRelations yields the OTHER pawn's relations toward the victim, so
                        // "Parent" means "other is the victim's parent". The saved fact is the
                        // victim's role from that owner's view, hence CloseFamilyRelationLabel
                        // inverts parent/child after choosing the strongest close-family relation.
                        relationLabel = CloseFamilyRelationLabel(victim, other);
                    }
                    catch (Exception exception)
                    {
                        // A broken relation graph belongs to one candidate, not to the whole death.
                        // Do not include pawn names here: this is a structural diagnostic, and the
                        // exception type is enough to group repeated reports without log spam.
                        Log.WarningOnce(
                            "[Pawn Diary] Skipped one malformed family relation while capturing death knowledge: "
                            + exception,
                            ("PawnDiary.Knowledge.Death.RelationProjection."
                                + exception.GetType().FullName).GetHashCode());
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(relationLabel)
                        || EnsureKnowledgeOwner(other) == null)
                    {
                        continue;
                    }

                    EmitDeathSignal(KnowledgeTokens.SignalDeathFamily, "PawnDiary_DeathFamily",
                        other, victimId, victimName, tick, date,
                        "victim=" + GameContextValue.Sanitize(victimName)
                        + "; relation=" + GameContextValue.Sanitize(relationLabel),
                        deathOccurrenceId);
                    familyOwnersEmitted++;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Death knowledge capture failed: " + e,
                    "PawnDiary.Knowledge.Death".GetHashCode());
            }
        }

        private void EmitDeathSignal(string channel, string defName, Pawn owner, string victimId,
            string victimName, int tick, string date, string context, string sourceOccurrenceId)
        {
            KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
            {
                signal = channel,
                defName = defName,
                sourceOccurrenceId = sourceOccurrenceId ?? string.Empty,
                tick = tick,
                dateLabel = date,
                gameContext = context,
                providedOwnerPawnId = owner.GetUniqueLoadID()
            };
            signal.extraParticipants.Add(new KnowledgeParticipant { pawnId = victimId, name = victimName });
            PersistDrafts(ImportantEventClassifier.Classify(
                signal, DiaryKnowledgePolicy.ImportantEventRules(), DiaryKnowledgePolicy.Snapshot()));
        }

        /// <summary>
        /// The victim's role from the survivor's point of view when the pair is CLOSE family
        /// (spouse/fiance/lover, parent, child, sibling), else empty. Gender-specific labels come
        /// from the vanilla relation defs so translations are the game's own.
        /// </summary>
        private static string CloseFamilyRelationLabel(Pawn victim, Pawn other)
        {
            PawnRelationDef best = null;
            foreach (PawnRelationDef def in victim.GetRelations(other))
            {
                if (def == PawnRelationDefOf.Spouse || def == PawnRelationDefOf.Fiance
                    || def == PawnRelationDefOf.Lover)
                {
                    best = def;
                    break;
                }

                if (def == PawnRelationDefOf.Parent || def == PawnRelationDefOf.Child
                    || def == PawnRelationDefOf.Sibling)
                {
                    best = best ?? def;
                }
            }

            if (best == null)
            {
                return string.Empty;
            }

            string victimRelation = KnowledgeRelationPolicy.VictimRelationDefName(best.defName);
            PawnRelationDef ownerView = string.Equals(
                    victimRelation, PawnRelationDefOf.Parent.defName, StringComparison.OrdinalIgnoreCase)
                ? PawnRelationDefOf.Parent
                : string.Equals(
                    victimRelation, PawnRelationDefOf.Child.defName, StringComparison.OrdinalIgnoreCase)
                    ? PawnRelationDefOf.Child
                    : best;
            return ownerView.GetGenderSpecificLabel(victim);
        }

        // ── Culture (§4.1) ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the pawn's origin culture ONCE (never rewritten later). Ideology-active pawns
        /// use their current ideology culture; otherwise the faction's allowed cultures decide.
        /// Pawns from a pre-redesign save resolve as "inferred".
        /// </summary>
        private void EnsureCultureResolved(
            Pawn pawn,
            string capturedOriginCultureDefName = null,
            bool notifyLibraryProjection = true)
        {
            if (pawn == null)
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return;
            }

            PawnKnowledgeState state = EnsureKnowledgeState(diary);
            if (!CultureResolver.NeedsOriginResolution(state.ToCultureSnapshot()))
            {
                return;
            }

            CultureResolutionInput input = new CultureResolutionInput
            {
                capturedOriginCultureDefName = capturedOriginCultureDefName ?? string.Empty,
                ideologyActive = ModsConfig.IdeologyActive,
                ideoCultureDefName = DlcContext.PawnIdeoCultureDefName(pawn),
                factionCultureDefNames = DlcContext.PawnFactionAllowedCultureDefNames(pawn),
                legacyInference = string.Equals(
                    state.originCultureSource,
                    KnowledgeTokens.CultureSourceInferred,
                    StringComparison.OrdinalIgnoreCase)
            };
            CultureStateSnapshot resolved = CultureResolver.ResolveOrigin(input);
            if (!string.IsNullOrWhiteSpace(resolved.originCultureDefName))
            {
                bool changed = MemoryLibraryPolicy.CultureProjectionChanged(
                    state.originCultureDefName,
                    state.originCultureSource,
                    state.adoptedCultureDefName,
                    resolved.originCultureDefName,
                    resolved.originSource,
                    state.adoptedCultureDefName);
                state.originCultureDefName = resolved.originCultureDefName;
                state.originCultureSource = resolved.originSource;
                // Culture is part of the logically sized owner envelope. Observation keeps a
                // retained budget between slices, so publish this rare owner-only mutation before
                // that budget can be reused. This avoids returning to a full colony byte walk.
                if (!RefreshMemorySizeIndexForOwner(state)) RebuildMemorySizeIndexes();
                if (changed && notifyLibraryProjection)
                    MarkMemoryLibraryCultureProjectionDirty();
            }
        }

        /// <summary>The knowledge state of one diary record, created and normalized on demand.</summary>
        private PawnKnowledgeState EnsureKnowledgeState(PawnDiaryRecord diary)
        {
            // CurrentRelease must create new owners in the writable schema. Raw v1/v2 states that
            // actually came from a save remain untouched until the migration transaction commits;
            // this branch runs only when the diary has no knowledge state at all.
            PawnKnowledgeState state;
            if (diary.knowledgeState == null && MemorySystemActivationGate.IsCurrentRelease)
            {
                diary.knowledgeState = PawnKnowledgeState.CreateCurrent(
                    diary.pawnId ?? string.Empty);
                MarkMemoryM4IndexesDirty();
                state = diary.knowledgeState;
            }
            else
            {
                state = diary.EnsureKnowledgeState();
            }
            if (string.IsNullOrWhiteSpace(state.pawnId))
            {
                state.pawnId = diary.pawnId ?? string.Empty;
            }

            return state;
        }

        // ── Persist drafts ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stores classifier drafts on their owners: deterministic dedup (§2.2), per-pawn cap
        /// enforcement at insert (oldest of that owner drops first).
        /// </summary>
        private bool PersistDrafts(List<ImportantMemoryDraft> drafts, bool persistLegacy = true)
        {
            if (drafts == null || drafts.Count == 0)
            {
                return false;
            }

            KnowledgePolicySnapshot policy = DiaryKnowledgePolicy.Snapshot();
            bool factualAdmitted = false;
            for (int i = 0; i < drafts.Count; i++)
            {
                ImportantMemoryDraft draft = drafts[i];
                if (draft == null || draft.record == null || string.IsNullOrWhiteSpace(draft.ownerPawnId))
                {
                    continue;
                }

                PawnDiaryRecord diary = FindDiaryByPawnId(draft.ownerPawnId);
                if (diary == null)
                {
                    continue;
                }

                // Current-schema factual capture is canonical in CurrentRelease and a shadow write in
                // LegacyShadow. Save/category policy remains independent of page/request scheduling.
                factualAdmitted |= PersistFactualDraft(draft.factual);

                // A current owner never receives a second legacy copy. A still-raw migration-pending
                // owner may keep accepting raw evidence, but it cannot mix in a new-format row; the
                // next migration pass will consume the complete legacy input atomically.
                if (!persistLegacy || MemorySystemActivationGate.IsCurrentRelease
                    || diary.knowledgeState?.IsCurrentSchema() == true) continue;

                PawnKnowledgeState state = EnsureKnowledgeState(diary);
                if (state.HasDedupKey(draft.record.dedupKey))
                {
                    continue;
                }

                state.records.Add(ImportantMemoryRecord.FromSnapshot(draft.record));
                EnforcePerPawnKnowledgeCap(state, policy.maxRecordsPerPawn);
            }
            return factualAdmitted;
        }

        /// <summary>
        /// Maps one pure M7 draft into saved rows and asks the atomic owner store to admit it. Every
        /// refusal is optional: authoritative pages and mandatory observation baselines stay committed.
        /// </summary>
        private bool PersistFactualDraft(FactualMemoryDraft draft)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.ownerPawnId)) return false;
            bool requiredLifecycleLandmark = FactualDraftIsRequiredLifecycleLandmark(draft);
            bool brainwipeCompletionLandmark = FactualDraftIsBrainwipeCompletionLandmark(draft);
            string captureStatus;
            if (!requiredLifecycleLandmark
                && !MemoryCategoryAllowsCapture(draft.category, out captureStatus)) return false;

            PawnDiaryRecord diary = FindDiaryByPawnId(draft.ownerPawnId);
            if (diary == null) return false;

            // Create a writable envelope only after the capture policy admits this occurrence. Raw
            // migration-pending owners stay raw, and a disabled category does not consume an owner slot.
            PawnKnowledgeState state = diary.knowledgeState;
            if (state == null && MemorySystemActivationGate.IsCurrentRelease)
            {
                // Avoid creating even a blank envelope when its immediately required epoch cannot
                // enter both bounded owner directories.
                if (!CanAdmitMemoryOwnerEpoch(false)) return false;
                state = EnsureKnowledgeState(diary);
            }
            if (state == null || !state.IsCurrentSchema()) return false;

            FactualOwnerEpochEnrollment enrollment = BeginFactualOwnerEpochEnrollment(
                state, brainwipeCompletionLandmark);
            if (enrollment == null) return false;
            string recordId;
            if (!MemoryIdentityCodec.TryCreateRecordId(
                    new MemoryRecordIdentity
                    {
                        ownerPawnId = draft.ownerPawnId,
                        ownerEpochToken = state.autobiographicalEpochToken,
                        sourceOccurrenceId = draft.sourceOccurrenceId,
                        captureRuleId = draft.captureRuleId,
                        factDiscriminator = draft.factDiscriminator
                    },
                    out recordId))
            {
                RollBackFactualOwnerEpochEnrollment(enrollment);
                return false;
            }

            SavedMemoryBlock block = new SavedMemoryBlock
            {
                schemaVersion = 1,
                recordId = recordId,
                sourceOccurrenceId = draft.sourceOccurrenceId,
                sourceEventId = draft.sourceEventId,
                captureRuleId = draft.captureRuleId,
                factDiscriminator = draft.factDiscriminator,
                ownerPawnId = draft.ownerPawnId,
                ownerEpochToken = state.autobiographicalEpochToken,
                kind = draft.kind,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = draft.category,
                importance = draft.importance,
                originalEventTick = Math.Max(0, draft.originalEventTick),
                automaticWording = draft.automaticWording ?? string.Empty,
                primarySubject = ToSavedSubject(draft.primarySubject),
                requiredLifecycleLandmark = requiredLifecycleLandmark,
                providerExposureState = "not_sent"
            };
            for (int i = 0; i < draft.secondarySubjects.Count; i++)
            {
                SavedMemorySubjectRef subject = ToSavedSubject(draft.secondarySubjects[i]);
                if (subject != null) block.secondarySubjects.Add(subject);
            }
            for (int i = 0; i < draft.facts.Count; i++)
            {
                FactualMemoryFactDraft fact = draft.facts[i];
                if (fact == null)
                {
                    RollBackFactualOwnerEpochEnrollment(enrollment);
                    return false;
                }
                block.facts.Add(new SavedMemoryCanonicalFact
                {
                    schemaVersion = 1,
                    factId = fact.factId,
                    factKind = fact.factKind,
                    canonicalSubjectKind = fact.canonicalSubjectKind,
                    canonicalSubjectId = fact.canonicalSubjectId,
                    aggregationToken = fact.aggregationToken,
                    canonicalValueKind = fact.canonicalValueKind,
                    canonicalValue = fact.canonicalValue,
                    majorTurningPoint = fact.majorTurningPoint,
                    reversal = fact.reversal
                });
            }
            block.provenance.Add(new SavedMemoryProvenance
            {
                schemaVersion = 1,
                provenanceRefId = draft.provenanceRefId,
                sourceKindToken = draft.sourceKindToken,
                sourceOccurrenceId = draft.sourceOccurrenceId,
                sourceEventId = draft.sourceEventId,
                captureRuleId = draft.captureRuleId,
                factDiscriminator = draft.factDiscriminator,
                integrationToken = string.Empty
            });

            MemoryStoreAdmissionResult admission = TryAdmitMemoryBlock(new MemoryStoreAdmissionRequest
            {
                ownerPawnId = draft.ownerPawnId,
                ownerEpochToken = state.autobiographicalEpochToken,
                expectedOwnerStructuralRevision = state.structuralRevision,
                expectedIndexGeneration = -1,
                routeReliable = draft.routeReliable,
                subjectKind = draft.subjectKind,
                subjectId = draft.subjectId,
                frozenSubjectLabel = draft.frozenSubjectLabel,
                chapterPhaseToken = draft.chapterPhaseToken,
                chapterDirective = draft.chapterDirective,
                chapterClosureReasonToken = draft.chapterClosureReasonToken,
                requiredLifecycleLandmark = block.requiredLifecycleLandmark,
                nowTick = block.originalEventTick,
                block = block
            });
            if (admission.outcome == MemoryStoreMutationOutcome.Admitted)
            {
                CommitFactualOwnerEpochEnrollment(enrollment);
            }
            else
            {
                RollBackFactualOwnerEpochEnrollment(enrollment);
                if (brainwipeCompletionLandmark
                    && admission.outcome
                        == MemoryStoreMutationOutcome.RequiredLandmarkCapacityRefused)
                {
                    RecordMemoryDiagnosticOnce("brainwipe_capacity", "component");
                }
            }
            return admission.outcome == MemoryStoreMutationOutcome.Admitted;
        }

        /// <summary>
        /// Main-thread-only provisional epoch publication for an owner's first factual block. The
        /// allocator high-water is monotonic even when block admission later refuses, while the owner
        /// epoch itself rolls back so a failed optional capture cannot leave an empty active owner.
        /// </summary>
        private sealed class FactualOwnerEpochEnrollment
        {
            public PawnDiaryRecord diary;
            public PawnKnowledgeState state;
            public bool pending;
            public string priorEpochToken;
            public bool priorEpochFenceOnly;
            public long priorStructuralRevision;
        }

        private FactualOwnerEpochEnrollment BeginFactualOwnerEpochEnrollment(
            PawnKnowledgeState state,
            bool brainwipeCompletionLandmark)
        {
            if (state == null || !state.IsCurrentSchema()
                || string.IsNullOrWhiteSpace(state.pawnId)) return null;
            // Archive envelopes are immutable. A Brainwipe fence is also immutable to every
            // ordinary capture; only the required first-new-epoch Landmark may transactionally
            // turn that exact fence into an active owner.
            if (state.archiveOnly) return null;

            bool ignoredFallback;
            if (!string.IsNullOrEmpty(state.autobiographicalEpochToken))
            {
                if (!MemoryIdentityCodec.TryValidateEpochToken(
                        state.autobiographicalEpochToken, out ignoredFallback)) return null;
                if (!state.epochFenceOnly)
                    return new FactualOwnerEpochEnrollment { state = state };
                if (!brainwipeCompletionLandmark || state.structuralRevision == long.MaxValue)
                    return null;
                // The fence already occupies the non-archive union, but its required completion
                // Landmark still cannot exceed the stricter active-owner directory.
                if (!CanAdmitMemoryOwnerEpoch(true))
                {
                    RecordMemoryDiagnosticOnce("brainwipe_capacity", "component");
                    return null;
                }

                PawnDiaryRecord fencedDiary = FindDiaryByPawnId(state.pawnId);
                if (fencedDiary == null || !ReferenceEquals(fencedDiary.knowledgeState, state))
                    return null;
                var fenceEnrollment = new FactualOwnerEpochEnrollment
                {
                    diary = fencedDiary,
                    state = state,
                    pending = true,
                    priorEpochToken = state.autobiographicalEpochToken,
                    priorEpochFenceOnly = true,
                    priorStructuralRevision = state.structuralRevision
                };
                state.epochFenceOnly = false;
                state.structuralRevision++;
                MarkMemoryM4IndexesDirty();
                return fenceEnrollment;
            }
            if (state.epochFenceOnly || state.structuralRevision == long.MaxValue) return null;

            PawnDiaryRecord diary = FindDiaryByPawnId(state.pawnId);
            if (diary == null || !ReferenceEquals(diary.knowledgeState, state)) return null;

            if (!CanAdmitMemoryOwnerEpoch(false)) return null;

            int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
            int epochCarrierScanCap = checked(ownerCap * 256);
            if (!CanBoundMemoryObservationEpochCarrierScan(epochCarrierScanCap)) return null;
            MemoryEpochAllocationPlan allocation = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = state.pawnId,
                    lastIssuedSequence = lastIssuedAutobiographicalEpochSequence,
                    fallbackChain = lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                    liveEpochCarriers = SnapshotAutobiographicalEpochCarriers(),
                    isTargetBrainwipe = false
                });
            if (!allocation.canMutate) return null;

            var enrollment = new FactualOwnerEpochEnrollment
            {
                diary = diary,
                state = state,
                pending = true,
                priorEpochToken = state.autobiographicalEpochToken ?? string.Empty,
                priorEpochFenceOnly = state.epochFenceOnly,
                priorStructuralRevision = state.structuralRevision
            };

            // Allocator publication is intentionally not rolled back. Reusing an issued token after a
            // later capacity/validation refusal would be worse than leaving a harmless high-water gap.
            lastIssuedAutobiographicalEpochSequence = allocation.nextSequence;
            lastIssuedAutobiographicalEpochFallbackChain =
                allocation.nextFallbackChain ?? string.Empty;
            state.autobiographicalEpochToken = allocation.epochToken;
            state.epochFenceOnly = false;
            state.structuralRevision++;
            MarkMemoryM4IndexesDirty();
            return enrollment;
        }

        private void CommitFactualOwnerEpochEnrollment(FactualOwnerEpochEnrollment enrollment)
        {
            if (enrollment?.pending != true) return;
            PawnReflectionState reflection = enrollment.diary.EnsureReflectionState();
            reflection.memoryReflectionSchemaVersion = 1;
            reflection.memoryOwnerEpochToken = enrollment.state.autobiographicalEpochToken;
            enrollment.pending = false;
        }

        private void RollBackFactualOwnerEpochEnrollment(FactualOwnerEpochEnrollment enrollment)
        {
            if (enrollment?.pending != true) return;
            enrollment.state.autobiographicalEpochToken = enrollment.priorEpochToken ?? string.Empty;
            enrollment.state.epochFenceOnly = enrollment.priorEpochFenceOnly;
            enrollment.state.structuralRevision = enrollment.priorStructuralRevision;
            enrollment.pending = false;
            MarkMemoryM4IndexesDirty();
            RebuildMemorySizeIndexes();
        }

        private static bool FactualDraftIsRequiredLifecycleLandmark(FactualMemoryDraft draft)
        {
            if (draft?.facts == null) return false;
            for (int index = 0; index < draft.facts.Count; index++)
            {
                if (string.Equals(
                        draft.facts[index]?.factKind,
                        KnowledgeTokens.EventKindFactionJoined,
                        StringComparison.Ordinal)
                    || string.Equals(
                        draft.facts[index]?.factKind,
                        KnowledgeTokens.EventKindBrainwipeCompleted,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool FactualDraftIsBrainwipeCompletionLandmark(FactualMemoryDraft draft)
        {
            if (draft?.facts == null) return false;
            for (int index = 0; index < draft.facts.Count; index++)
                if (string.Equals(
                        draft.facts[index]?.factKind,
                        KnowledgeTokens.EventKindBrainwipeCompleted,
                        StringComparison.Ordinal)) return true;
            return false;
        }

        private static SavedMemorySubjectRef ToSavedSubject(FactualMemorySubjectDraft draft)
        {
            if (draft == null) return null;
            return new SavedMemorySubjectRef
            {
                schemaVersion = 1,
                subjectRefId = draft.subjectRefId,
                subjectKind = draft.subjectKind,
                subjectId = draft.subjectId,
                frozenLabel = draft.frozenLabel,
                roleToken = draft.roleToken,
                knownnessToken = draft.knownnessToken
            };
        }

        /// <summary>
        /// Applies the insert-time per-pawn cap while preserving protected player/background canon and
        /// captured arrival lifecycle boundaries.
        /// Shared by gameplay capture and explicit profile creation so both mutation paths have the
        /// same immediate retention semantics; the separate global cap remains cadence/pre-save work.
        /// </summary>
        private static void EnforcePerPawnKnowledgeCap(PawnKnowledgeState state, int configuredCap)
        {
            if (state?.records == null)
            {
                return;
            }

            state.records.RemoveAll(record => record == null);
            KnowledgeOwnerLoad owner = new KnowledgeOwnerLoad
            {
                ownerPawnId = state.pawnId ?? string.Empty
            };
            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                owner.records.Add(new KnowledgeRecordStub
                {
                    recordId = record.recordId,
                    tick = record.tick,
                    sourceIndex = i,
                    protectedFromAutomaticEviction =
                        PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                            state.pawnId, record.recordId, record.dedupKey, record.eventKind,
                            record.sourceKind, record.recallScope)
                });
            }
            QualifiedKnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.PlanQualified(
                new List<KnowledgeOwnerLoad> { owner },
                new KnowledgePolicySnapshot
                {
                    maxRecordsPerPawn = Math.Max(0, configuredCap),
                    maxRecordsGlobal = int.MaxValue
                });
            if (plan.drops.Count == 0) return;
            HashSet<int> drops = new HashSet<int>();
            for (int i = 0; i < plan.drops.Count; i++) drops.Add(plan.drops[i].sourceIndex);
            for (int i = state.records.Count - 1; i >= 0; i--)
            {
                if (drops.Contains(i)) state.records.RemoveAt(i);
            }
        }

        // ── Retrieval (§3) ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Freezes the "relevant past" block onto each first-person POV of a just-registered
        /// event: deterministic contextual selection plus an optional owner background fallback,
        /// at most two localized fact lines. Gated by the single player switch (injection only) and
        /// by template projectability, exactly like the narrative/belief context builders.
        /// </summary>
        private void ApplyRelevantPastForEvent(
            DiaryEvent diaryEvent,
            bool recordDiagnostics = true,
            bool createMissingKnowledgeState = true,
            string requestedTemplateKey = null,
            string povRole = null)
        {
            try
            {
                // Freeze the richest available relevant-past snapshot before an API lane is known.
                // Dispatch later applies the selected lane's Full/Balanced/Compact feature policy.
                KnowledgePolicySnapshot policy = DiaryKnowledgePolicy.Snapshot(
                    applyGlobalMemorySetting: false);
                if (!policy.injectionEnabled || !EventProjectsMemoryContext(
                    diaryEvent,
                    readOnlyKnowledge: !createMissingKnowledgeState,
                    requestedTemplateKey: requestedTemplateKey,
                    povRole: povRole))
                {
                    return;
                }

                if (MemorySystemActivationGate.IsCurrentRelease)
                {
                    // M11 freezes the richest two-line shortlist at event time. QueuePrompt later
                    // revalidates only that shortlist against the current exact owner/epoch state;
                    // a newly eligible lower-ranked row can never replace frozen evidence.
                    FreezeMemoryRecallV2Projection(
                        diaryEvent,
                        DiaryEvent.InitiatorRole,
                        PromptContextDetailLevel.Full,
                        persistSelection: createMissingKnowledgeState);
                    if (!diaryEvent.solo
                        && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                    {
                        FreezeMemoryRecallV2Projection(
                            diaryEvent,
                            DiaryEvent.RecipientRole,
                            PromptContextDetailLevel.Full,
                            persistSelection: createMissingKnowledgeState);
                    }
                    return;
                }

                ApplyRelevantPastForRole(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    policy,
                    recordDiagnostics,
                    createMissingKnowledgeState);
                if (!diaryEvent.solo && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                {
                    ApplyRelevantPastForRole(
                        diaryEvent,
                        DiaryEvent.RecipientRole,
                        policy,
                        recordDiagnostics,
                        createMissingKnowledgeState);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Relevant-past retrieval failed for event "
                    + (diaryEvent?.eventId ?? "?") + ": " + e,
                    "PawnDiary.Knowledge.Retrieve".GetHashCode());
            }
        }

        /// <summary>
        /// True when the finally chosen template declares an enabled MemoryContext field — reuses
        /// the exact generation-time template resolution so the prediction cannot drift. Neutral
        /// death/arrival and title pages never render the block, so retrieval skips them.
        /// </summary>
        private static bool EventProjectsMemoryContext(
            DiaryEvent diaryEvent,
            bool readOnlyKnowledge = false,
            string requestedTemplateKey = null,
            string povRole = null)
        {
            return DiaryPipelineAdapters.ProjectsMemoryContext(
                diaryEvent,
                readOnlyKnowledge,
                requestedTemplateKey,
                povRole);
        }

        private void ApplyRelevantPastForRole(
            DiaryEvent diaryEvent,
            string povRole,
            KnowledgePolicySnapshot policy,
            bool recordDiagnostics,
            bool createMissingKnowledgeState)
        {
            string pawnId = povRole == DiaryEvent.RecipientRole
                ? diaryEvent.recipientPawnId
                : diaryEvent.initiatorPawnId;
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvent.IsSkipped(povRole))
            {
                return;
            }

            PawnDiaryRecord diary = createMissingKnowledgeState
                ? FindDiaryByPawnId(pawnId)
                : LookupDiaryByPawnId(pawnId);
            if (diary == null)
            {
                return;
            }

            PawnKnowledgeState state = createMissingKnowledgeState
                ? EnsureKnowledgeState(diary)
                : diary.KnowledgeStateOrNull();
            if (state == null) return;
            string otherPawnId = povRole == DiaryEvent.RecipientRole
                ? diaryEvent.initiatorPawnId
                : diaryEvent.recipientPawnId;
            bool socialReflection = string.Equals(
                diaryEvent.interactionDefName,
                SocialReflectionEventData.DefNameToken,
                StringComparison.OrdinalIgnoreCase);
            int relevantPastMaxLines = policy.relevantPastMaxLines;
            KnowledgePolicySnapshot selectionPolicy = policy;
            if (socialReflection)
            {
                // H7 is a solo event structurally, so its subject is carried in frozen context rather
                // than recipientPawnId. Require exact participant overlap and exclude the canonical
                // source page: neither a broad entity key nor the interaction being reflected on may
                // masquerade as an older memory about this pawn.
                otherPawnId = DiaryContextFields.Value(
                    diaryEvent.gameContext,
                    SocialReflectionEventData.SubjectIdContextKey);
                SocialReflectionPolicySnapshot reflectionPolicy =
                    DiarySocialReflectionPolicy.Snapshot();
                relevantPastMaxLines = Math.Max(
                    0,
                    Math.Min(
                        policy.relevantPastMaxLines,
                        reflectionPolicy.maximumMemoryLines));
                selectionPolicy = CopyKnowledgePolicyWithLineCap(
                    policy, relevantPastMaxLines);
            }
            KnowledgeQuery query = ImportantMemorySelector.BuildQuery(
                diaryEvent.eventId,
                pawnId,
                otherPawnId,
                diaryEvent.tick,
                diaryEvent.gameContext,
                diaryEvent.interactionDefName,
                DiaryKnowledgePolicy.ImportantEventRules(),
                selectionPolicy);
            if (socialReflection)
            {
                query.requireParticipantOverlap = true;
                string sourceEventId = DiaryContextFields.Value(
                    diaryEvent.gameContext,
                    SocialReflectionEventData.SourceEventIdContextKey);
                if (!string.IsNullOrWhiteSpace(sourceEventId))
                {
                    query.excludedSourceEventIds.Add(sourceEventId.Trim());
                }
            }
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                query, state.ToRecordSnapshots(), selectionPolicy);

            // Dev report (§7) — stored even when nothing selected so "why not" stays inspectable.
            KnowledgeDebugReport report = new KnowledgeDebugReport
            {
                eventId = diaryEvent.eventId ?? string.Empty,
                tick = diaryEvent.tick
            };
            report.queryParticipantIds.AddRange(query.participantIds);
            report.querySubjectKeys.AddRange(query.subjectKeys);
            report.queryTopicKeys.AddRange(query.topicKeys);
            report.candidates.AddRange(result.report);
            for (int i = 0; i < result.selected.Count; i++)
            {
                report.selectedRecordIds.Add(result.selected[i].recordId);
            }

            if (recordDiagnostics) knowledgeReportsByPawnId[pawnId] = report;

            if (result.selected.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>(result.selected.Count);
            for (int i = 0; i < result.selected.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = result.selected[i];
                bool playerBackground = PlayerMemoryPolicy.IsCanonicalBackstory(record, pawnId);
                ImportantEventRule rule = playerBackground
                    ? null
                    : DiaryKnowledgePolicy.RuleForKind(record.eventKind);
                string fact = ImportantMemoryLineRenderer.Render(
                    record,
                    rule?.lineTemplate,
                    playerBackground
                        ? policy.playerAuthoredMemoryMaxChars
                        : policy.fallbackSummaryMaxChars);
                if (string.IsNullOrWhiteSpace(fact))
                {
                    continue;
                }

                string line = playerBackground
                    ? ImportantMemoryLineRenderer.FormatBackground(
                        fact,
                        policy.backgroundMemoryLineFormat)
                    : (string.IsNullOrWhiteSpace(record.dateLabel)
                        ? fact
                        : SafeLineFormat(policy.relevantPastLineFormat, record.dateLabel, fact));
                lines.Add(line);
            }

            string block = ImportantMemoryLineRenderer.ComposeBlock(
                lines, relevantPastMaxLines, policy.relevantPastMaxChars);
            if (!string.IsNullOrWhiteSpace(block))
            {
                diaryEvent.SetMemoryContext(povRole, block);
            }
        }

        /// <summary>
        /// Makes a detached retrieval-only policy with a stricter line cap. List values are read-only
        /// during selection, so shallow list copies preserve the loaded XML policy without mutating it.
        /// </summary>
        private static KnowledgePolicySnapshot CopyKnowledgePolicyWithLineCap(
            KnowledgePolicySnapshot source,
            int lineCap)
        {
            KnowledgePolicySnapshot safe =
                source ?? KnowledgePolicySnapshot.CreateDefault();
            return new KnowledgePolicySnapshot
            {
                injectionEnabled = safe.injectionEnabled,
                maxRecordsPerPawn = safe.maxRecordsPerPawn,
                maxRecordsGlobal = safe.maxRecordsGlobal,
                fallbackSummaryMaxChars = safe.fallbackSummaryMaxChars,
                playerAuthoredMemoryMaxChars = safe.playerAuthoredMemoryMaxChars,
                relevantPastMaxLines = Math.Max(0, lineCap),
                relevantPastMaxChars = safe.relevantPastMaxChars,
                relevantPastLineFormat = safe.relevantPastLineFormat,
                backgroundMemoryLineFormat = safe.backgroundMemoryLineFormat,
                relevantPastInstruction = safe.relevantPastInstruction,
                currentStateInstruction = safe.currentStateInstruction,
                maxCultureTopicsPerPrompt = safe.maxCultureTopicsPerPrompt,
                annotationSingleFormat = safe.annotationSingleFormat,
                annotationDualFormat = safe.annotationDualFormat,
                scannableSources = safe.scannableSources == null
                    ? new List<string>()
                    : new List<string>(safe.scannableSources),
                querySubjectKeyRules = safe.querySubjectKeyRules == null
                    ? new List<KnowledgeSubjectKeyRule>()
                    : new List<KnowledgeSubjectKeyRule>(
                        safe.querySubjectKeyRules)
            };
        }

        private static string SafeLineFormat(string format, string date, string fact)
        {
            try
            {
                return string.Format(
                    string.IsNullOrWhiteSpace(format) ? "- ({0}) {1}" : format, date, fact);
            }
            catch (FormatException)
            {
                return "- (" + date + ") " + fact;
            }
        }

        /// <summary>The pawn's knowledge state for prompt/dev consumers; null when undiared.</summary>
        internal PawnKnowledgeState KnowledgeStateForPawnId(string pawnId)
        {
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return diary != null ? EnsureKnowledgeState(diary) : null;
        }

        /// <summary>Read-only prompt-preview lookup that never creates or normalizes saved state.</summary>
        internal PawnKnowledgeState KnowledgeStateForPawnIdReadOnly(string pawnId)
        {
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            return diary?.KnowledgeStateOrNull();
        }

        /// <summary>Culture snapshot + last retrieval report for the dev tab (§7).</summary>
        internal KnowledgeDebugReport LastKnowledgeReportFor(string pawnId)
        {
            KnowledgeDebugReport report;
            return !string.IsNullOrWhiteSpace(pawnId)
                && knowledgeReportsByPawnId.TryGetValue(pawnId, out report)
                ? report
                : null;
        }

        /// <summary>Records the annotation outcome of the latest prompt build for the dev tab.</summary>
        internal void RecordKnowledgeAnnotationReport(string pawnId, List<string> matchedTopics,
            List<string> annotatedSources)
        {
            KnowledgeDebugReport report = LastKnowledgeReportFor(pawnId);
            if (report == null)
            {
                return;
            }

            report.matchedCultureTopics.Clear();
            report.annotatedFieldSources.Clear();
            if (matchedTopics != null)
            {
                report.matchedCultureTopics.AddRange(matchedTopics);
            }

            if (annotatedSources != null)
            {
                report.annotatedFieldSources.AddRange(annotatedSources);
            }
        }

        /// <summary>
        /// The full dev diagnostic (§7): culture provenance, profile found/missing, every stored
        /// important event, and the last prompt-selection report. Rendered on demand from the dev
        /// action — never written to the log unprompted.
        /// </summary>
        internal string KnowledgeDiagnosticsForDev(Pawn pawn)
        {
            if (pawn == null)
            {
                return "no pawn";
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return "no diary record for " + pawn.LabelShortCap;
            }

            PawnKnowledgeState state = EnsureKnowledgeState(diary);
            System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
            builder.AppendLine("pawn=" + diary.pawnId + " (" + pawn.LabelShortCap + ")");
            builder.AppendLine("originCulture=" + Display(state.originCultureDefName)
                + " source=" + Display(state.originCultureSource)
                + " adoptedCulture=" + Display(state.adoptedCultureDefName));

            string effectiveOrigin = state.originCultureDefName ?? string.Empty;
            string effectiveAdopted = state.adoptedCultureDefName ?? string.Empty;
            builder.AppendLine("originProfile=" + ProfileStatus(effectiveOrigin)
                + " adoptedProfile=" + ProfileStatus(effectiveAdopted));

            builder.AppendLine("records=" + state.records.Count);
            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record == null)
                {
                    continue;
                }

                builder.Append("  [").Append(i).Append("] ").Append(record.eventKind)
                    .Append(" source=").Append(record.sourceKind)
                    .Append(" scope=").Append(record.recallScope)
                    .Append(" @").Append(record.tick)
                    .Append(" (").Append(Display(record.dateLabel)).Append(")");
                if (record.subjectKeys.Count > 0)
                {
                    builder.Append(" subjects=").Append(string.Join(",", record.subjectKeys.ToArray()));
                }

                if (record.participantIds.Count > 0)
                {
                    builder.Append(" with=").Append(string.Join(",", record.participantNames.ToArray()));
                }

                ImportantEventRule rule = DiaryKnowledgePolicy.RuleForKind(record.eventKind);
                string line = ImportantMemoryLineRenderer.Render(
                    record.ToSnapshot(), rule?.lineTemplate, 240);
                builder.Append(" | ").AppendLine(line);
            }

            KnowledgeDebugReport report = LastKnowledgeReportFor(diary.pawnId);
            if (report == null)
            {
                builder.AppendLine("lastSelection=none (no prompt built since load)");
            }
            else
            {
                builder.AppendLine("lastSelection event=" + report.eventId + " @" + report.tick);
                builder.AppendLine("  queryParticipants="
                    + string.Join(",", report.queryParticipantIds.ToArray()));
                builder.AppendLine("  querySubjects=" + string.Join(",", report.querySubjectKeys.ToArray()));
                builder.AppendLine("  queryTopics=" + string.Join(",", report.queryTopicKeys.ToArray()));
                for (int i = 0; i < report.candidates.Count; i++)
                {
                    KnowledgeCandidateReport candidate = report.candidates[i];
                    builder.Append("  ").Append(candidate.selected ? "PICK " : "skip ")
                        .Append(candidate.recordId)
                        .Append(" participant=").Append(candidate.sharedParticipant)
                        .Append(" subject=").Append(candidate.sharedSubject)
                        .Append(" topic=").Append(candidate.sharedTopic);
                    if (!string.IsNullOrEmpty(candidate.rejectReason))
                    {
                        builder.Append(" reason=").Append(candidate.rejectReason);
                    }

                    builder.AppendLine();
                }

                builder.AppendLine("  matchedCultureTopics="
                    + string.Join(",", report.matchedCultureTopics.ToArray()));
                builder.AppendLine("  annotatedFields="
                    + string.Join(",", report.annotatedFieldSources.ToArray()));
            }

            return builder.ToString();
        }

        private static string ProfileStatus(string cultureDefName)
        {
            if (string.IsNullOrWhiteSpace(cultureDefName))
            {
                return "n/a";
            }

            if (DiaryKnowledgePolicy.HasAuthoredProfile(cultureDefName))
            {
                return "found";
            }

            return DiaryKnowledgePolicy.ProfileFor(cultureDefName) != null ? "fallback" : "missing";
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        // ── Defensive limits (§2.3) ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies the pure eviction plan: per-pawn caps, then the global cap with absent owners'
        /// oldest records first (§2.3). Absent = the owner pawn no longer exists in the game at
        /// all; dead-but-present owners keep their records for resurrection.
        /// </summary>
        private void ApplyKnowledgeEviction()
        {
            try
            {
                if (diaries == null || diaries.Count == 0)
                {
                    return;
                }

                HashSet<string> existingPawnIds = null;
                List<KnowledgeOwnerLoad> loads = new List<KnowledgeOwnerLoad>();
                Dictionary<string, PawnKnowledgeState> statesByOwner =
                    new Dictionary<string, PawnKnowledgeState>();
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord diary = diaries[i];
                    PawnKnowledgeState state = diary?.KnowledgeStateOrNull();
                    if (state == null || state.records.Count == 0)
                    {
                        continue;
                    }

                    if (existingPawnIds == null)
                    {
                        existingPawnIds = SnapshotExistingPawnIds();
                    }

                    KnowledgeOwnerLoad load = new KnowledgeOwnerLoad
                    {
                        ownerPawnId = diary.pawnId ?? string.Empty,
                        ownerAbsent = !existingPawnIds.Contains(diary.pawnId ?? string.Empty)
                    };
                    for (int j = 0; j < state.records.Count; j++)
                    {
                        ImportantMemoryRecord record = state.records[j];
                        if (record != null)
                        {
                            load.records.Add(new KnowledgeRecordStub
                            {
                                recordId = record.recordId,
                                tick = record.tick,
                                sourceIndex = j,
                                protectedFromAutomaticEviction =
                                    PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                                        state.pawnId,
                                        record.recordId,
                                        record.dedupKey,
                                        record.eventKind,
                                        record.sourceKind,
                                        record.recallScope)
                            });
                        }
                    }

                    loads.Add(load);
                    statesByOwner[load.ownerPawnId] = state;
                }

                if (loads.Count == 0)
                {
                    return;
                }

                QualifiedKnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.PlanQualified(
                    loads, DiaryKnowledgePolicy.Snapshot());
                if (plan.drops.Count == 0)
                {
                    return;
                }

                Dictionary<string, HashSet<int>> dropsByOwner =
                    new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
                for (int i = 0; i < plan.drops.Count; i++)
                {
                    KnowledgeEvictionHandle drop = plan.drops[i];
                    HashSet<int> ownerDrops;
                    if (!dropsByOwner.TryGetValue(drop.ownerPawnId, out ownerDrops))
                    {
                        ownerDrops = new HashSet<int>();
                        dropsByOwner.Add(drop.ownerPawnId, ownerDrops);
                    }
                    ownerDrops.Add(drop.sourceIndex);
                }
                bool removedAny = false;
                foreach (KeyValuePair<string, PawnKnowledgeState> pair in statesByOwner)
                {
                    HashSet<int> drops;
                    dropsByOwner.TryGetValue(pair.Key, out drops);
                    List<ImportantMemoryRecord> records = pair.Value.records;
                    for (int i = records.Count - 1; i >= 0; i--)
                    {
                        if (records[i] == null || (drops != null && drops.Contains(i)))
                        {
                            records.RemoveAt(i);
                            removedAny = true;
                        }
                    }
                }

                if (removedAny)
                {
                    // Legacy records back the compatibility panel and count toward byte limits.
                    // Eviction can be the only maintenance mutation, so publish it immediately.
                    // Null guards preserve reflection fixtures that intentionally construct only
                    // this adapter without running the component field initializers.
                    if (memoryByteTotalsByOwner != null) RebuildMemorySizeIndexes();
                    if (memoryLibraryOwners != null) MarkMemoryLibrarySavedProjectionDirty();
                }

                if (plan.globalCapHit)
                {
                    // The ONE bounded warning (§2.3).
                    Log.WarningOnce("[Pawn Diary] Important-memory global cap reached; oldest "
                        + "records of absent owners were evicted first.",
                        "PawnDiary.Knowledge.GlobalCap".GetHashCode());
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Knowledge eviction failed: " + e,
                    "PawnDiary.Knowledge.Evict".GetHashCode());
            }
        }

        /// <summary>Every pawn id that still exists anywhere (alive or dead) — resurrection stays
        /// possible for them, so their records are never "absent".</summary>
        private static HashSet<string> SnapshotExistingPawnIds()
        {
            HashSet<string> ids = new HashSet<string>();
            List<Pawn> all = PawnsFinder.All_AliveOrDead;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                {
                    string id = all[i].GetUniqueLoadID();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }

        /// <summary>The game-date label for a fresh non-event capture, using the same date style
        /// diary pages use. Falls back to a tile-less date when the pawn has no map.</summary>
        private static string KnowledgeDateLabelNow(Pawn pawn)
        {
            return KnowledgeDateLabelAt(pawn, Find.TickManager?.TicksGame ?? 0);
        }

        /// <summary>Renders the diary-style date for a captured event tick, including delayed signals.</summary>
        private static string KnowledgeDateLabelAt(Pawn pawn, int tick)
        {
            try
            {
                UnityEngine.Vector2 location = UnityEngine.Vector2.zero;
                Map map = pawn?.MapHeld ?? Find.CurrentMap;
                if (map != null)
                {
                    location = Find.WorldGrid.LongLatOf(map.Tile);
                }

                return GenDate.DateFullStringAt(GenDate.TickGameToAbs(Math.Max(0, tick)),
                    location);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
