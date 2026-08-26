// Normal-play profile adapters for one pawn's durable important memories and player-authored
// background, plus the existing developer-only cultural-lore diagnostics.
//
// The Diary profile consumes detached snapshots and commits memory edits/removals only through the
// guarded methods below. That keeps RimWorld's
// repeated IMGUI draw passes from mutating save state and keeps stable retrieval identity (record
// id, event kind, participants, subjects, facts) out of the editor. Text changes affect future
// relevant-past selection output; already-frozen DiaryEvent memoryContext fields remain historical
// snapshots.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "UI — diary tab") and docs/lore/ui.md.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Detached developer view of one requested culture and the profile that will interpret it.
    /// A different resolved name means the fallback profile is active.
    /// </summary>
    internal sealed class LoreMemoryProfileForDev
    {
        public string requestedCultureDefName = string.Empty;
        public string resolvedCultureDefName = string.Empty;
        public bool authored;
    }

    /// <summary>
    /// One cultural topic as the lore-memory UI needs to display it: localized clauses plus the
    /// exact lexical and structured triggers that can select the topic.
    /// </summary>
    internal sealed class LoreMemoryTopicForDev
    {
        public string topicKey = string.Empty;
        public string label = string.Empty;
        public string originClause = string.Empty;
        public string adoptedClause = string.Empty;
        public List<string> triggerTextTerms = new List<string>();
        public List<string> triggerContextKeys = new List<string>();
        public List<string> triggerContextPairs = new List<string>();
        public List<string> triggerValueMarkers = new List<string>();
        public List<string> triggerDefNames = new List<string>();
    }

    /// <summary>
    /// Read-only snapshot used by the developer lore-memory section. It contains no Pawn or Def
    /// references, so repeated UI draws cannot mutate live game state.
    /// </summary>
    internal sealed class LoreMemorySnapshotForDev
    {
        public bool hasKnowledgeState;
        public bool injectionEnabled;
        public string originCultureSource = string.Empty;
        public LoreMemoryProfileForDev originProfile = new LoreMemoryProfileForDev();
        public LoreMemoryProfileForDev adoptedProfile = new LoreMemoryProfileForDev();
        public List<LoreMemoryTopicForDev> topics = new List<LoreMemoryTopicForDev>();
        public bool hasLastPromptReport;
        public List<string> matchedCultureTopics = new List<string>();
        public List<string> annotatedFieldSources = new List<string>();
    }

    public partial class DiaryGameComponent
    {
        private static readonly IReadOnlyList<ImportantMemoryRecordSnapshot>
            NoImportantMemoriesForProfile = new List<ImportantMemoryRecordSnapshot>();
        private static readonly IReadOnlyList<ImportantMemoryRecord> NoImportantMemoriesForDev =
            new List<ImportantMemoryRecord>();

        /// <summary>
        /// Returns detached captured/contextual rows for the exact eligible pawn without creating or
        /// normalizing save state. Player/background rows are intentionally absent so the canonical
        /// background cannot appear twice in the profile.
        /// </summary>
        internal IReadOnlyList<ImportantMemoryRecordSnapshot> ImportantMemoriesForProfile(Pawn pawn)
        {
            PawnKnowledgeState state = ExistingKnowledgeStateForProfile(pawn);
            if (state?.records == null)
            {
                return NoImportantMemoriesForProfile;
            }

            string ownerPawnId = pawn.GetUniqueLoadID();
            List<ImportantMemoryRecordSnapshot> saved = state.ToRecordSnapshots();
            List<ImportantMemoryRecordSnapshot> visible =
                new List<ImportantMemoryRecordSnapshot>(saved.Count);
            for (int i = 0; i < saved.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = saved[i];
                if (record == null)
                {
                    continue;
                }

                // The owning state is attached to the exact diary looked up above. Stamp that detached
                // owner even for an old blank state id; no saved object is changed during this read.
                record.ownerPawnId = ownerPawnId;
                if (IsCapturedContextual(record))
                {
                    visible.Add(record);
                }
            }

            return visible.Count == 0 ? NoImportantMemoriesForProfile : visible;
        }

        /// <summary>Returns the XML-owned edit/render limit for captured contextual memory prose.</summary>
        internal int ImportantMemoryTextLimitForProfile()
        {
            return Math.Max(
                0,
                DiaryKnowledgePolicy.Snapshot(applyGlobalMemorySetting: false)
                    .fallbackSummaryMaxChars);
        }

        /// <summary>
        /// Replaces only one captured row's prompt/display prose. Stable ownership, matching facts,
        /// participants, subjects, and event identity remain read-only.
        /// </summary>
        internal bool TrySetImportantMemoryTextForProfile(
            Pawn pawn,
            string recordId,
            string text)
        {
            if (string.IsNullOrWhiteSpace(recordId))
            {
                return false;
            }

            PawnKnowledgeState state = ExistingKnowledgeStateForProfile(pawn);
            if (state == null)
            {
                return false;
            }

            // This is an explicit Save action, so repairing null lists/schema defaults is safe here.
            state.Normalize();
            ImportantMemoryRecord record = FindImportantMemoryRecord(state, recordId);
            if (!IsCapturedContextual(record))
            {
                return false;
            }

            string cleaned = ImportantMemoryLineRenderer.CleanManualOverride(
                text,
                ImportantMemoryTextLimitForProfile());
            if (string.Equals(record.manualTextOverride, cleaned, StringComparison.Ordinal))
            {
                return true;
            }

            record.manualTextOverride = cleaned;
            InvalidateKnowledgeAfterProfileMutation(pawn.GetUniqueLoadID());
            return true;
        }

        /// <summary>
        /// Removes exactly one captured/contextual row owned by the requested pawn, except the
        /// faction-joined marker that the load bootstrap uses as a one-time arrival boundary.
        /// </summary>
        internal bool TryRemoveImportantMemoryForProfile(Pawn pawn, string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId))
            {
                return false;
            }

            PawnKnowledgeState state = ExistingKnowledgeStateForProfile(pawn);
            if (state == null)
            {
                return false;
            }

            state.Normalize();
            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && IsCapturedContextual(record)
                    && !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                        state.pawnId,
                        record.recordId,
                        record.dedupKey,
                        record.eventKind,
                        record.sourceKind,
                        record.recallScope)
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    state.records.RemoveAt(i);
                    InvalidateKnowledgeAfterProfileMutation(pawn.GetUniqueLoadID());
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the exact canonical background text for an eligible pawn without creating or
        /// normalizing any save row. Corrupt lookalikes are ignored rather than promoted to canon.
        /// </summary>
        internal string BackgroundMemoryForProfile(Pawn pawn)
        {
            PawnKnowledgeState state = ExistingKnowledgeStateForProfile(pawn);
            if (state == null)
            {
                return string.Empty;
            }

            if (MemorySystemActivationGate.IsCurrentRelease && state.IsCurrentSchema())
            {
                return PlayerMemoryPolicy.NormalizePlayerText(state.playerBackground);
            }

            ImportantMemoryRecordSnapshot record = CanonicalBackgroundSnapshot(
                state,
                pawn.GetUniqueLoadID());
            return PlayerMemoryPolicy.NormalizePlayerText(record?.manualTextOverride);
        }

        /// <summary>Returns the XML-owned maximum for the singleton player-authored background.</summary>
        internal int BackgroundMemoryTextLimitForProfile()
        {
            return Math.Max(
                0,
                DiaryKnowledgePolicy.Snapshot(applyGlobalMemorySetting: false)
                    .playerAuthoredMemoryMaxChars);
        }

        /// <summary>
        /// Applies the pure create/update/blank-to-delete plan for one exact eligible pawn. This is an
        /// explicit profile Save boundary, so creating/repairing the pawn's save state is allowed here.
        /// </summary>
        internal bool TrySetBackgroundMemoryForProfile(Pawn pawn, string text)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            string ownerPawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = LookupDiaryByPawnId(ownerPawnId);
            if (diary != null
                && !string.Equals(diary.pawnId, ownerPawnId, StringComparison.Ordinal))
            {
                return false;
            }

            PawnKnowledgeState state = diary?.KnowledgeStateOrNull();
            if (state != null
                && !string.IsNullOrWhiteSpace(state.pawnId)
                && !string.Equals(state.pawnId, ownerPawnId, StringComparison.Ordinal))
            {
                return false;
            }

            if (MemorySystemActivationGate.IsCurrentRelease
                && (state == null || state.IsCurrentSchema()))
            {
                return TrySetCurrentBackgroundMemory(
                    pawn,
                    diary,
                    state,
                    ownerPawnId,
                    text);
            }

            ImportantMemoryRecordSnapshot existing = state == null
                ? null
                : CanonicalBackgroundSnapshot(state, ownerPawnId);
            PlayerMemoryMutationPlan plan = PlayerMemoryPolicy.PlanBackstoryMutation(
                ownerPawnId,
                existing,
                text,
                BackgroundMemoryTextLimitForProfile());
            if (plan.action == PlayerMemoryMutationAction.None)
            {
                return true;
            }

            if (plan.action == PlayerMemoryMutationAction.Rejected || plan.record == null
                && plan.action != PlayerMemoryMutationAction.Delete)
            {
                return false;
            }

            if (diary == null)
            {
                // Blank input planned None above, so only a real create reaches this explicit mutation.
                diary = FindDiary(pawn, true);
            }

            if (diary == null)
            {
                return false;
            }

            state = EnsureKnowledgeState(diary);
            int existingIndex = FindCanonicalBackgroundIndex(state, ownerPawnId);
            if (plan.action == PlayerMemoryMutationAction.Create)
            {
                if (existingIndex >= 0)
                {
                    return false;
                }

                state.records.Add(ImportantMemoryRecord.FromSnapshot(plan.record));
                // The protected background counts toward the same per-pawn cap as captured rows.
                // Enforce that cap now so profile creation cannot expose an over-cap store until the
                // much slower global maintenance pass or the next save.
                EnforcePerPawnKnowledgeCap(
                    state,
                    DiaryKnowledgePolicy.Snapshot(applyGlobalMemorySetting: false)
                        .maxRecordsPerPawn);
            }
            else if (plan.action == PlayerMemoryMutationAction.Update)
            {
                if (existingIndex < 0)
                {
                    return false;
                }

                state.records[existingIndex] = ImportantMemoryRecord.FromSnapshot(plan.record);
            }
            else if (plan.action == PlayerMemoryMutationAction.Delete)
            {
                if (existingIndex < 0)
                {
                    return false;
                }

                state.records.RemoveAt(existingIndex);
            }
            else
            {
                return false;
            }

            InvalidateKnowledgeAfterProfileMutation(ownerPawnId);
            return true;
        }

        /// <summary>
        /// Commits the CurrentRelease background singleton directly to the unified envelope. It is
        /// independently recallable before an autobiographical epoch exists and never creates a
        /// legacy ImportantMemoryRecord shadow row.
        /// </summary>
        private bool TrySetCurrentBackgroundMemory(
            Pawn pawn,
            PawnDiaryRecord diary,
            PawnKnowledgeState state,
            string ownerPawnId,
            string text)
        {
            string normalized = PlayerMemoryPolicy.NormalizePlayerText(text);
            if (normalized.Length > BackgroundMemoryTextLimitForProfile()) return false;
            string existing = state?.playerBackground ?? string.Empty;
            if (string.Equals(existing, normalized, StringComparison.Ordinal)) return true;
            if (state != null && state.structuralRevision == long.MaxValue) return false;
            if (state == null && normalized.Length == 0) return true;

            RebuildMemorySizeIndexes();
            MemoryPayloadBudgetTotals global = GetGlobalBudgetTotals();
            if (global.globalActiveBytes < 0 || global.globalImportedBytes < 0) return false;

            bool newOwner = state == null;
            long delta;
            MemoryOwnerByteTotals ownerTotals;
            if (newOwner)
            {
                int ownerCap = (int)ReadCapacityTuplePart("ownerSlotTriple", 0, 1000, 4000);
                int activeOwners = CountMemoryObservationActiveOwners(ownerCap + 1);
                if (!KnowledgeRelationPolicy.CanAdmitObservationOwner(activeOwners, ownerCap))
                    return false;

                state = PawnKnowledgeState.CreateCurrent(ownerPawnId);
                state.playerBackground = normalized;
                MemoryLogicalSizeResult measured = MemoryLogicalPayloadSizer.Size(state);
                if (!measured.valid) return false;
                delta = measured.totalBytes;
                ownerTotals = new MemoryOwnerByteTotals
                {
                    valid = true,
                    activeBytes = 0,
                    importedBytes = 0
                };
            }
            else
            {
                ownerTotals = GetOwnerByteTotals(ownerPawnId);
                if (!ownerTotals.valid) return false;
                try
                {
                    var utf8 = new System.Text.UTF8Encoding(false, true);
                    delta = checked(
                        utf8.GetByteCount(normalized) - utf8.GetByteCount(existing));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            if (delta > 0)
            {
                MemoryBudgetDecision decision = ActiveMemoryPayloadBudget.TryAdmit(
                    new MemoryBudgetLimits
                    {
                        activeOwnerBytes = ReadCapacityLong(
                            "activeOwnerBytes", 196608, 2097152),
                        combinedOwnerBytes = ReadCapacityLong(
                            "combinedOwnerBytes", 262144, 4194304),
                        activeGlobalBytes = ReadCapacityLong(
                            "activeGlobalBytes", 6291456, 25165824),
                        combinedGlobalBytes = ReadCapacityLong(
                            "combinedGlobalBytes", 8388608, 33554432)
                    },
                    ownerTotals.activeBytes,
                    ownerTotals.importedBytes,
                    delta,
                    0,
                    global);
                if (decision.outcome != MemoryBudgetOutcome.Admitted) return false;
            }

            if (newOwner)
            {
                diary = diary ?? FindDiary(pawn, true);
                if (diary == null || diary.knowledgeState != null) return false;
                diary.knowledgeState = state;
                MarkMemoryM4IndexesDirty();
            }
            else
            {
                state.playerBackground = normalized;
            }
            state.structuralRevision++;
            RebuildMemorySizeIndexes();
            InvalidateKnowledgeAfterProfileMutation(ownerPawnId);
            return true;
        }

        /// <summary>
        /// Returns the existing saved memory list for developer display without creating or
        /// normalizing knowledge state. Callers must treat the returned records as read-only.
        /// </summary>
        internal IReadOnlyList<ImportantMemoryRecord> ImportantMemoriesForDev(Pawn pawn)
        {
            PawnKnowledgeState state = ExistingKnowledgeStateForDev(pawn);
            return state?.records ?? NoImportantMemoriesForDev;
        }

        /// <summary>
        /// Builds a detached view of the pawn's saved culture state and the currently loaded lore
        /// policy. Unlike prompt generation, this is inspection-only: it never creates a diary,
        /// resolves a missing origin, normalizes records, or updates the last-prompt report.
        /// </summary>
        internal LoreMemorySnapshotForDev LoreMemoryForDev(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null)
            {
                return null;
            }

            return LoreMemoryForDev(pawn.GetUniqueLoadID());
        }

        /// <summary>
        /// Owner-id form used by the Library for living, dead, away, and archive-listed owners. The
        /// lookup reuses the existing lore inspector projection and never resolves culture from a live
        /// Pawn, creates a diary, or normalizes persisted state.
        /// </summary>
        internal LoreMemorySnapshotForDev LoreMemoryForDev(string ownerPawnId)
        {
            if (!Prefs.DevMode || string.IsNullOrWhiteSpace(ownerPawnId))
            {
                return null;
            }

            PawnDiaryRecord diary = LookupDiaryByPawnId(ownerPawnId);
            PawnKnowledgeState state = diary?.KnowledgeStateOrNull();
            if (state != null && !string.IsNullOrWhiteSpace(state.pawnId)
                && !string.Equals(state.pawnId, ownerPawnId, StringComparison.Ordinal))
            {
                state = null;
            }
            LoreMemorySnapshotForDev snapshot = new LoreMemorySnapshotForDev
            {
                hasKnowledgeState = state != null,
                injectionEnabled = DiaryKnowledgePolicy.Snapshot().injectionEnabled,
                originCultureSource = state?.originCultureSource ?? string.Empty,
                originProfile = LoreProfileForDev(state?.originCultureDefName),
                adoptedProfile = LoreProfileForDev(state?.adoptedCultureDefName)
            };

            CultureProfile originProfile = DiaryKnowledgePolicy.ProfileFor(
                snapshot.originProfile.requestedCultureDefName);
            CultureProfile adoptedProfile = DiaryKnowledgePolicy.ProfileFor(
                snapshot.adoptedProfile.requestedCultureDefName);
            List<CultureTopicRule> topics = new List<CultureTopicRule>(
                DiaryKnowledgePolicy.CultureTopics());
            topics.Sort(CompareLoreTopicsForDev);
            for (int i = 0; i < topics.Count; i++)
            {
                CultureTopicRule topic = topics[i];
                if (topic == null || !topic.enabled || string.IsNullOrWhiteSpace(topic.topicKey))
                {
                    continue;
                }

                LoreMemoryTopicForDev row = new LoreMemoryTopicForDev
                {
                    topicKey = topic.topicKey,
                    label = LoreTopicLabelForDev(topic.topicKey),
                    originClause = originProfile?.ClauseFor(topic.topicKey) ?? string.Empty,
                    adoptedClause = adoptedProfile?.ClauseFor(topic.topicKey) ?? string.Empty
                };
                CopyLoreStringsForDev(topic.triggerTextTerms, row.triggerTextTerms);
                CopyLoreStringsForDev(topic.triggerContextKeys, row.triggerContextKeys);
                CopyLoreStringsForDev(topic.triggerContextPairs, row.triggerContextPairs);
                CopyLoreStringsForDev(topic.triggerValueMarkers, row.triggerValueMarkers);
                CopyLoreStringsForDev(topic.triggerDefNames, row.triggerDefNames);
                snapshot.topics.Add(row);
            }

            if (state != null)
            {
                KnowledgeDebugReport report = LastKnowledgeReportFor(state.pawnId);
                if (report != null)
                {
                    snapshot.hasLastPromptReport = true;
                    CopyLoreStringsForDev(
                        report.matchedCultureTopics, snapshot.matchedCultureTopics);
                    CopyLoreStringsForDev(
                        report.annotatedFieldSources, snapshot.annotatedFieldSources);
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Returns the XML-owned one-line character limit used by memory rendering/editing.
        /// Zero outside developer mode prevents a hidden caller from presenting an editor.
        /// </summary>
        internal int ImportantMemoryTextLimitForDev()
        {
            return Prefs.DevMode ? ImportantMemoryTextLimitForProfile() : 0;
        }

        /// <summary>
        /// Replaces only the prompt/display prose of one stable memory. Blank text clears the
        /// override and restores the canonical localized template; matching metadata is unchanged.
        /// </summary>
        internal bool TrySetImportantMemoryTextForDev(Pawn pawn, string recordId, string text)
        {
            return Prefs.DevMode
                && TrySetImportantMemoryTextForProfile(pawn, recordId, text);
        }

        /// <summary>
        /// Permanently removes exactly one captured/contextual memory by its stable record id. Unlike
        /// the normal profile, this intentionally permits lifecycle rows as a diagnostic repair escape
        /// hatch, but it retains the same pawn eligibility/state-owner authorization and still cannot
        /// delete player/background or malformed future record classes. The developer-mode guard is
        /// repeated here so hiding the UI is never the only authorization boundary.
        /// </summary>
        internal bool TryRemoveImportantMemoryForDev(Pawn pawn, string recordId)
        {
            if (!Prefs.DevMode || string.IsNullOrWhiteSpace(recordId))
            {
                return false;
            }

            PawnKnowledgeState state = ExistingKnowledgeStateForProfile(pawn);
            if (state == null)
            {
                return false;
            }

            state.Normalize();
            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && IsCapturedContextual(record)
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    state.records.RemoveAt(i);
                    InvalidateKnowledgeAfterProfileMutation(pawn.GetUniqueLoadID());
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Exact no-create diary lookup for the normal profile. Eligibility is checked here as a second
        /// authorization boundary rather than relying on the window having hidden itself correctly.
        /// </summary>
        private PawnDiaryRecord ExistingDiaryForProfile(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            return diary != null
                && string.Equals(diary.pawnId, pawnId, StringComparison.Ordinal)
                ? diary
                : null;
        }

        /// <summary>
        /// Existing state for profile reads/actions. A blank legacy state owner is safely identified by
        /// its exact diary attachment; a conflicting nonblank owner is rejected, never silently repaired.
        /// </summary>
        private PawnKnowledgeState ExistingKnowledgeStateForProfile(Pawn pawn)
        {
            PawnDiaryRecord diary = ExistingDiaryForProfile(pawn);
            PawnKnowledgeState state = diary?.KnowledgeStateOrNull();
            if (state == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            return string.IsNullOrWhiteSpace(state.pawnId)
                || string.Equals(state.pawnId, pawnId, StringComparison.Ordinal)
                ? state
                : null;
        }

        private static bool IsCapturedContextual(ImportantMemoryRecordSnapshot record)
        {
            return record != null
                && string.Equals(
                    PlayerMemoryPolicy.NormalizeSourceKind(record.sourceKind),
                    KnowledgeTokens.SourceKindCaptured,
                    StringComparison.Ordinal)
                && string.Equals(
                    PlayerMemoryPolicy.NormalizeRecallScope(record.recallScope),
                    KnowledgeTokens.RecallScopeContextual,
                    StringComparison.Ordinal);
        }

        private static bool IsCapturedContextual(ImportantMemoryRecord record)
        {
            return record != null
                && string.Equals(
                    PlayerMemoryPolicy.NormalizeSourceKind(record.sourceKind),
                    KnowledgeTokens.SourceKindCaptured,
                    StringComparison.Ordinal)
                && string.Equals(
                    PlayerMemoryPolicy.NormalizeRecallScope(record.recallScope),
                    KnowledgeTokens.RecallScopeContextual,
                    StringComparison.Ordinal);
        }

        private static ImportantMemoryRecordSnapshot CanonicalBackgroundSnapshot(
            PawnKnowledgeState state,
            string ownerPawnId)
        {
            if (state?.records == null)
            {
                return null;
            }

            List<ImportantMemoryRecordSnapshot> records = state.ToRecordSnapshots();
            for (int i = 0; i < records.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = records[i];
                if (record == null)
                {
                    continue;
                }

                // The state was reached through this owner's exact diary attachment. Stamping only the
                // detached mirror preserves no-mutation reads while allowing legacy blank state ids.
                record.ownerPawnId = ownerPawnId ?? string.Empty;
                if (PlayerMemoryPolicy.IsCanonicalBackstory(record, ownerPawnId))
                {
                    return record;
                }
            }

            return null;
        }

        private static int FindCanonicalBackgroundIndex(
            PawnKnowledgeState state,
            string ownerPawnId)
        {
            if (state?.records == null)
            {
                return -1;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null && PlayerMemoryPolicy.IsCanonicalBackstory(
                    ownerPawnId,
                    record.recordId,
                    record.dedupKey,
                    record.eventKind,
                    record.sourceKind,
                    record.recallScope))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Read-only lookup for the dev editor. It deliberately avoids EnsureKnowledgeState so
        /// merely opening or repainting the window cannot add data to an old save.
        /// </summary>
        private PawnKnowledgeState ExistingKnowledgeStateForDev(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            return diary?.KnowledgeStateOrNull();
        }

        private static ImportantMemoryRecord FindImportantMemoryRecord(
            PawnKnowledgeState state,
            string recordId)
        {
            if (state?.records == null)
            {
                return null;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        private static LoreMemoryProfileForDev LoreProfileForDev(string cultureDefName)
        {
            LoreMemoryProfileForDev view = new LoreMemoryProfileForDev
            {
                requestedCultureDefName = (cultureDefName ?? string.Empty).Trim()
            };
            if (string.IsNullOrWhiteSpace(view.requestedCultureDefName))
            {
                return view;
            }

            CultureProfile profile = DiaryKnowledgePolicy.ProfileFor(
                view.requestedCultureDefName);
            view.authored = DiaryKnowledgePolicy.HasAuthoredProfile(
                view.requestedCultureDefName);
            view.resolvedCultureDefName = profile?.cultureDefName ?? string.Empty;
            return view;
        }

        private static int CompareLoreTopicsForDev(CultureTopicRule left, CultureTopicRule right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int order = left.order.CompareTo(right.order);
            return order != 0
                ? order
                : string.Compare(left.topicKey, right.topicKey, StringComparison.OrdinalIgnoreCase);
        }

        private static string LoreTopicLabelForDev(string topicKey)
        {
            foreach (DiaryCultureTopicDef def
                in DefDatabase<DiaryCultureTopicDef>.AllDefsListForReading)
            {
                if (def != null
                    && string.Equals(def.topicKey, topicKey, StringComparison.OrdinalIgnoreCase))
                {
                    return def.LabelCap.ToString();
                }
            }

            return topicKey ?? string.Empty;
        }

        private static void CopyLoreStringsForDev(
            List<string> source,
            List<string> destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    destination.Add(value.Trim());
                }
            }
        }

        private void InvalidateKnowledgeAfterProfileMutation(string pawnId)
        {
            // The last selection report may name the old text or a now-removed record.
            if (!string.IsNullOrWhiteSpace(pawnId))
            {
                knowledgeReportsByPawnId.Remove(pawnId);
            }

            // The diary UI's shared cache token also covers this profile and its developer diagnostics.
            DiaryStateVersion.Bump();
        }
    }
}
