// Developer-only browsing and mutation endpoints for one pawn's durable important memories and
// read-only cultural lore.
//
// The Writing Style window consumes the detached lore snapshot and read-only memory list below,
// then commits memory edits/removals only through the guarded methods. That keeps RimWorld's
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
        private static readonly IReadOnlyList<ImportantMemoryRecord> NoImportantMemoriesForDev =
            new List<ImportantMemoryRecord>();

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

            PawnKnowledgeState state = ExistingKnowledgeStateForDev(pawn);
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
            if (!Prefs.DevMode)
            {
                return 0;
            }

            return Math.Max(
                0,
                DiaryKnowledgePolicy.Snapshot(applyGlobalMemorySetting: false)
                    .fallbackSummaryMaxChars);
        }

        /// <summary>
        /// Replaces only the prompt/display prose of one stable memory. Blank text clears the
        /// override and restores the canonical localized template; matching metadata is unchanged.
        /// </summary>
        internal bool TrySetImportantMemoryTextForDev(Pawn pawn, string recordId, string text)
        {
            if (!Prefs.DevMode || string.IsNullOrWhiteSpace(recordId))
            {
                return false;
            }

            PawnKnowledgeState state = ExistingKnowledgeStateForDev(pawn);
            if (state == null)
            {
                return false;
            }

            // This is an explicit mutation click, so repairing a hand-edited live row here is safe.
            state.Normalize();
            ImportantMemoryRecord record = FindImportantMemoryRecord(state, recordId);
            if (record == null)
            {
                return false;
            }

            int maxChars = DiaryKnowledgePolicy.Snapshot(applyGlobalMemorySetting: false)
                .fallbackSummaryMaxChars;
            string cleaned = ImportantMemoryLineRenderer.CleanManualOverride(text, maxChars);
            if (string.Equals(record.manualTextOverride, cleaned, StringComparison.Ordinal))
            {
                return true;
            }

            record.manualTextOverride = cleaned;
            InvalidateKnowledgeAfterDevMutation(state.pawnId);
            return true;
        }

        /// <summary>
        /// Permanently removes exactly one memory by its stable record id. The developer-mode guard
        /// is repeated here so hiding the UI is never the only authorization boundary.
        /// </summary>
        internal bool TryRemoveImportantMemoryForDev(Pawn pawn, string recordId)
        {
            if (!Prefs.DevMode || string.IsNullOrWhiteSpace(recordId))
            {
                return false;
            }

            PawnKnowledgeState state = ExistingKnowledgeStateForDev(pawn);
            if (state == null)
            {
                return false;
            }

            state.Normalize();
            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    state.records.RemoveAt(i);
                    InvalidateKnowledgeAfterDevMutation(state.pawnId);
                    return true;
                }
            }

            return false;
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

        private void InvalidateKnowledgeAfterDevMutation(string pawnId)
        {
            // The last selection report may name the old text or a now-removed record.
            if (!string.IsNullOrWhiteSpace(pawnId))
            {
                knowledgeReportsByPawnId.Remove(pawnId);
            }

            // The diary UI's shared cache token also covers this adjacent developer window.
            DiaryStateVersion.Bump();
        }
    }
}
