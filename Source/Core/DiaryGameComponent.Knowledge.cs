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
        /// <summary>Component-level knowledge schema. 0 = save predates the redesign; the load
        /// pass marks its pawns for legacy culture inference, then stamps 1 (§6).</summary>
        private int knowledgeSchemaVersion;

        /// <summary>Pawn ids whose diary existed before the redesign's first load — their origin
        /// cultures resolve as "inferred" rather than "captured" (§4.1). Transient.</summary>
        private readonly HashSet<string> legacyCulturePawnIds = new HashSet<string>();

        /// <summary>Last retrieval report per pawn for the dev tab (§7). Transient, bounded by
        /// the number of diaried pawns.</summary>
        private readonly Dictionary<string, KnowledgeDebugReport> knowledgeReportsByPawnId =
            new Dictionary<string, KnowledgeDebugReport>();

        private int lastKnowledgeEvictionScanTick = -1;

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

        /// <summary>Scribes the component-level schema version. The per-pawn state itself rides
        /// PawnDiaryRecord.knowledgeState (Scribe_Deep), not this partial.</summary>
        private void ExposeKnowledgeData()
        {
            Scribe_Values.Look(ref knowledgeSchemaVersion, "knowledgeSchemaVersion", 0);
        }

        /// <summary>
        /// One-time clean start (§6): a save from before the redesign (version 0) keeps its diary
        /// records but starts important-event history from now; old associative fragments and
        /// lore-seed rosters are simply never read again. Existing pawns are marked so their
        /// origin culture resolves as "inferred".
        /// </summary>
        private void PostLoadInitKnowledge()
        {
            legacyCulturePawnIds.Clear();
            if (knowledgeSchemaVersion < 1)
            {
                if (diaries != null)
                {
                    for (int i = 0; i < diaries.Count; i++)
                    {
                        if (diaries[i] != null && !string.IsNullOrWhiteSpace(diaries[i].pawnId))
                        {
                            legacyCulturePawnIds.Add(diaries[i].pawnId);
                        }
                    }
                }

                knowledgeSchemaVersion = 1;
            }
        }

        private void ResetKnowledgeForNewGame()
        {
            knowledgeSchemaVersion = 1;
            legacyCulturePawnIds.Clear();
            knowledgeReportsByPawnId.Clear();
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
            if (diaryEvent == null)
            {
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
                PawnDiaryRecord diary = FindDiary(pawn, false);
                if (diary == null)
                {
                    return;
                }

                // The removal channel reuses the event channel's "<def>_addedpart" suffix naming
                // so XML rows can match structurally without enumerating every implant defName.
                string signalDefName = removed && addedPartOrImplant
                    ? hediffDefName + "_" + BodyPartEventPolicy.KindAddedPart
                    : hediffDefName;
                string context = "hediff=" + hediffDefName
                    + "; label=" + (hediffLabel ?? string.Empty)
                    + (string.IsNullOrWhiteSpace(partLabel) ? string.Empty : "; body_part=" + partLabel)
                    + (string.IsNullOrWhiteSpace(partDefName) ? string.Empty : "; part_def=" + partDefName);
                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = removed ? KnowledgeTokens.SignalHediffRemoved : KnowledgeTokens.SignalHediffQuiet,
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
                PawnDiaryRecord diary = FindDiary(pawn, false);
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
                    gameContext = "role=" + roleLabel
                        + (string.IsNullOrWhiteSpace(ideoName) ? string.Empty : "; ideo=" + ideoName),
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
            if (pawn == null)
            {
                return;
            }

            try
            {
                PawnDiaryRecord diary = FindDiary(pawn, false);
                if (diary == null)
                {
                    return;
                }

                PawnKnowledgeState state = EnsureKnowledgeState(diary);
                EnsureCultureResolved(pawn);
                // Conversion REPLACES the latest adopted culture; earlier adopted cultures are
                // not retained (§4.1).
                if (!string.IsNullOrWhiteSpace(newCultureDefName))
                {
                    state.adoptedCultureDefName = newCultureDefName.Trim();
                }

                KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalIdeoConversion,
                    defName = "PawnDiary_IdeoConversion",
                    tick = Find.TickManager.TicksGame,
                    dateLabel = KnowledgeDateLabelNow(pawn),
                    gameContext = "previous_ideo=" + (previousIdeoName ?? string.Empty)
                        + "; new_ideo=" + (newIdeoName ?? string.Empty)
                        + "; new_culture=" + (newCultureDefName ?? string.Empty),
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

                Pawn instigator = dinfo.HasValue ? dinfo.Value.Instigator as Pawn : null;
                if (instigator != null && instigator != victim && FindDiary(instigator, false) != null)
                {
                    string weaponLabel = dinfo.Value.Weapon != null ? dinfo.Value.Weapon.label : string.Empty;
                    EmitDeathSignal(KnowledgeTokens.SignalDeathInstigator, "PawnDiary_DeathInstigator",
                        instigator, victimId, victimName, tick, date,
                        "victim=" + victimName
                        + (string.IsNullOrWhiteSpace(weaponLabel) ? string.Empty : "; weapon=" + weaponLabel));
                }

                // Close family only (§2.1). GetRelations yields the OTHER pawn's relations toward
                // the victim, so "Parent" means "other is the victim's parent". The relation FACT
                // stored on the record is the victim's role from the owner's view (their spouse,
                // their child…), so we label the inverse pair member.
                if (victim.relations == null)
                {
                    return;
                }

                int fanoutBudget = 12; // defensive cap: modded mega-families must not flood capture
                foreach (Pawn other in victim.relations.RelatedPawns)
                {
                    if (fanoutBudget <= 0)
                    {
                        break;
                    }

                    if (other == null || other == victim || other == instigator
                        || FindDiary(other, false) == null)
                    {
                        continue;
                    }

                    string relationLabel = CloseFamilyRelationLabel(victim, other);
                    if (string.IsNullOrWhiteSpace(relationLabel))
                    {
                        continue;
                    }

                    fanoutBudget--;
                    EmitDeathSignal(KnowledgeTokens.SignalDeathFamily, "PawnDiary_DeathFamily",
                        other, victimId, victimName, tick, date,
                        "victim=" + victimName + "; relation=" + relationLabel);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Death knowledge capture failed: " + e,
                    "PawnDiary.Knowledge.Death".GetHashCode());
            }
        }

        private void EmitDeathSignal(string channel, string defName, Pawn owner, string victimId,
            string victimName, int tick, string date, string context)
        {
            KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
            {
                signal = channel,
                defName = defName,
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

            return best != null ? best.GetGenderSpecificLabel(victim) : string.Empty;
        }

        // ── Culture (§4.1) ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the pawn's origin culture ONCE (never rewritten later). Ideology-active pawns
        /// use their current ideology culture; otherwise the faction's allowed cultures decide.
        /// Pawns from a pre-redesign save resolve as "inferred".
        /// </summary>
        private void EnsureCultureResolved(Pawn pawn)
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
                ideologyActive = ModsConfig.IdeologyActive,
                ideoCultureDefName = DlcContext.PawnIdeoCultureDefName(pawn),
                factionCultureDefNames = DlcContext.PawnFactionAllowedCultureDefNames(pawn),
                legacyInference = legacyCulturePawnIds.Contains(diary.pawnId)
            };
            CultureStateSnapshot resolved = CultureResolver.ResolveOrigin(input);
            if (!string.IsNullOrWhiteSpace(resolved.originCultureDefName))
            {
                state.originCultureDefName = resolved.originCultureDefName;
                state.originCultureSource = resolved.originSource;
            }
        }

        /// <summary>The knowledge state of one diary record, created and normalized on demand.</summary>
        private PawnKnowledgeState EnsureKnowledgeState(PawnDiaryRecord diary)
        {
            PawnKnowledgeState state = diary.EnsureKnowledgeState();
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
        private void PersistDrafts(List<ImportantMemoryDraft> drafts)
        {
            if (drafts == null || drafts.Count == 0)
            {
                return;
            }

            KnowledgePolicySnapshot policy = DiaryKnowledgePolicy.Snapshot();
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

                PawnKnowledgeState state = EnsureKnowledgeState(diary);
                if (state.HasDedupKey(draft.record.dedupKey))
                {
                    continue;
                }

                state.records.Add(ImportantMemoryRecord.FromSnapshot(draft.record));
                int cap = Math.Max(0, policy.maxRecordsPerPawn);
                while (state.records.Count > cap && state.records.Count > 0)
                {
                    RemoveOldestRecord(state);
                }
            }
        }

        private static void RemoveOldestRecord(PawnKnowledgeState state)
        {
            int oldestIndex = 0;
            for (int i = 1; i < state.records.Count; i++)
            {
                ImportantMemoryRecord candidate = state.records[i];
                ImportantMemoryRecord oldest = state.records[oldestIndex];
                if (candidate == null)
                {
                    oldestIndex = i;
                    break;
                }

                if (oldest == null)
                {
                    continue;
                }

                if (candidate.tick < oldest.tick
                    || (candidate.tick == oldest.tick
                        && string.CompareOrdinal(candidate.recordId, oldest.recordId) < 0))
                {
                    oldestIndex = i;
                }
            }

            state.records.RemoveAt(oldestIndex);
        }

        // ── Retrieval (§3) ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Freezes the "relevant past" block onto each first-person POV of a just-registered
        /// event: deterministic selection, at most two dated localized fact lines. Gated by the
        /// single player switch (injection only) and by template projectability, exactly like the
        /// narrative/belief context builders.
        /// </summary>
        private void ApplyRelevantPastForEvent(DiaryEvent diaryEvent)
        {
            try
            {
                KnowledgePolicySnapshot policy = DiaryKnowledgePolicy.Snapshot();
                if (!policy.injectionEnabled || !EventProjectsMemoryContext(diaryEvent))
                {
                    return;
                }

                ApplyRelevantPastForRole(diaryEvent, DiaryEvent.InitiatorRole, policy);
                if (!diaryEvent.solo && !string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId))
                {
                    ApplyRelevantPastForRole(diaryEvent, DiaryEvent.RecipientRole, policy);
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
        private static bool EventProjectsMemoryContext(DiaryEvent diaryEvent)
        {
            DiaryEventPayload payload = DiaryPipelineAdapters.ToPayload(diaryEvent);
            DiaryPolicySnapshot promptPolicy = DiaryPipelineAdapters.PolicyFor(payload);
            string templateKey = DiaryPromptPlanner.TemplateKeyFor(new DiaryPromptRequest
            {
                payload = payload,
                policy = promptPolicy
            });
            return DiaryPromptPlanner.ProjectsMemoryContext(promptPolicy.Template(templateKey));
        }

        private void ApplyRelevantPastForRole(DiaryEvent diaryEvent, string povRole,
            KnowledgePolicySnapshot policy)
        {
            string pawnId = povRole == DiaryEvent.RecipientRole
                ? diaryEvent.recipientPawnId
                : diaryEvent.initiatorPawnId;
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvent.IsSkipped(povRole))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null)
            {
                return;
            }

            PawnKnowledgeState state = EnsureKnowledgeState(diary);
            string otherPawnId = povRole == DiaryEvent.RecipientRole
                ? diaryEvent.initiatorPawnId
                : diaryEvent.recipientPawnId;
            KnowledgeQuery query = ImportantMemorySelector.BuildQuery(
                diaryEvent.eventId,
                pawnId,
                otherPawnId,
                diaryEvent.tick,
                diaryEvent.gameContext,
                diaryEvent.interactionDefName,
                DiaryKnowledgePolicy.ImportantEventRules(),
                policy);
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                query, state.ToRecordSnapshots(), policy);

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

            knowledgeReportsByPawnId[pawnId] = report;

            if (result.selected.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>(result.selected.Count);
            for (int i = 0; i < result.selected.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = result.selected[i];
                ImportantEventRule rule = DiaryKnowledgePolicy.RuleForKind(record.eventKind);
                string fact = ImportantMemoryLineRenderer.Render(
                    record, rule?.lineTemplate, policy.fallbackSummaryMaxChars);
                if (string.IsNullOrWhiteSpace(fact))
                {
                    continue;
                }

                string line = string.IsNullOrWhiteSpace(record.dateLabel)
                    ? fact
                    : SafeLineFormat(policy.relevantPastLineFormat, record.dateLabel, fact);
                lines.Add(line);
            }

            string block = ImportantMemoryLineRenderer.ComposeBlock(
                lines, policy.relevantPastMaxLines, policy.relevantPastMaxChars);
            if (!string.IsNullOrWhiteSpace(block))
            {
                diaryEvent.SetMemoryContext(povRole, block);
            }
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

        /// <summary>Cadenced global-cap scan; also runs before every save (mirrors the diary
        /// event-limit pass). Elapsed-time cadence, not modulo, so dev time-skips stay safe.</summary>
        private void MaybeRunKnowledgeEvictionScan(int nowTick)
        {
            int interval = Math.Max(1, DiaryKnowledgePolicy.EvictionScanIntervalTicks());
            if (lastKnowledgeEvictionScanTick >= 0
                && nowTick - lastKnowledgeEvictionScanTick < interval)
            {
                return;
            }

            lastKnowledgeEvictionScanTick = nowTick;
            ApplyKnowledgeEviction();
        }

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
                                tick = record.tick
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

                KnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.Plan(
                    loads, DiaryKnowledgePolicy.Snapshot());
                if (plan.dropRecordIds.Count == 0)
                {
                    return;
                }

                HashSet<string> drops = new HashSet<string>(plan.dropRecordIds);
                foreach (KeyValuePair<string, PawnKnowledgeState> pair in statesByOwner)
                {
                    List<ImportantMemoryRecord> records = pair.Value.records;
                    for (int i = records.Count - 1; i >= 0; i--)
                    {
                        if (records[i] == null || drops.Contains(records[i].recordId))
                        {
                            records.RemoveAt(i);
                        }
                    }
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
            try
            {
                UnityEngine.Vector2 location = UnityEngine.Vector2.zero;
                Map map = pawn?.MapHeld ?? Find.CurrentMap;
                if (map != null)
                {
                    location = Find.WorldGrid.LongLatOf(map.Tile);
                }

                return GenDate.DateFullStringAt(GenDate.TickGameToAbs(Find.TickManager.TicksGame),
                    location);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
