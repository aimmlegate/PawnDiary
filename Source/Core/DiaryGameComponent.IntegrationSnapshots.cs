// Read-only snapshots for the public integration API. This partial keeps adapter-facing reads close
// to the saved diary stores while still returning only plain DTOs from PawnDiary.Integration.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Integration;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const int IntegrationRecentTitleHardCap = 20;

        /// <summary>
        /// Builds newest-first diary title snapshots for one pawn. Called only by PawnDiaryApi, which
        /// already checks main-thread/game-readiness rules and catches exceptions for adapter safety.
        /// </summary>
        internal List<DiaryEntryTitleSnapshot> RecentEntryTitleSnapshotsFor(Pawn pawn, int maxCount)
        {
            return RecentEntryTitleSnapshotsFor(pawn, maxCount, null);
        }

        /// <summary>
        /// Counts diary entries for one pawn without materializing title/prose snapshot rows. Uses the
        /// same hot/archive view path and query facts as the title/context readers.
        /// </summary>
        internal DiaryEntryStatsSnapshot EntryStatsFor(Pawn pawn)
        {
            return EntryStatsFor(pawn, null);
        }

        /// <summary>
        /// Counts diary entries for one pawn after applying the public query filters.
        /// </summary>
        internal DiaryEntryStatsSnapshot EntryStatsFor(Pawn pawn, DiaryEntryTitleQuery query)
        {
            if (!IsDiaryEligible(pawn))
            {
                return null;
            }

            DiaryEntryStatsSnapshot stats = new DiaryEntryStatsSnapshot();
            string pawnId = pawn.GetUniqueLoadID();
            HashSet<string> emittedKeys = new HashSet<string>();

            bool includeActive = query == null || query.includeActive;
            bool includeArchived = query == null || query.includeArchived;

            if (includeActive)
            {
                AppendHotEntryStats(pawn, pawnId, query, emittedKeys, stats);
            }

            if (includeArchived)
            {
                AppendArchivedEntryStats(pawnId, query, emittedKeys, stats);
            }

            return stats;
        }

        /// <summary>
        /// Archive scan cap for <see cref="EntryStatsFor"/>. The hot path is bounded by the live event
        /// list (small per pawn); the archive can grow without bound for a long-lived colonist, so the
        /// newest-first scan stops here. Counts are approximate beyond the cap, which is acceptable for
        /// an adapter building a picker or a "recent activity" badge.
        /// </summary>
        private static int StatsArchiveScanLimit => DiaryTuning.IntegrationStatsMaxArchiveScan;

        /// <summary>
        /// Builds newest-first diary title snapshots for one pawn, filtered by a plain public query.
        /// </summary>
        internal List<DiaryEntryTitleSnapshot> RecentEntryTitleSnapshotsFor(
            Pawn pawn,
            int maxCount,
            DiaryEntryTitleQuery query)
        {
            List<DiaryEntryTitleSnapshot> snapshots = new List<DiaryEntryTitleSnapshot>();
            if (pawn == null || maxCount <= 0)
            {
                return snapshots;
            }

            int limit = maxCount > IntegrationRecentTitleHardCap
                ? IntegrationRecentTitleHardCap
                : maxCount;
            string pawnId = pawn.GetUniqueLoadID();
            HashSet<string> emittedKeys = new HashSet<string>();

            // Skip a store entirely when the query excludes it: the per-entry filter would reject every
            // row anyway, and building each hot DiaryEntryView (linked previews included) only to
            // discard it is wasted main-thread work.
            bool includeActive = query == null || query.includeActive;
            bool includeArchived = query == null || query.includeArchived;

            if (includeActive)
            {
                AppendRecentHotTitleSnapshots(pawn, pawnId, limit, query, emittedKeys, snapshots);
            }

            if (includeArchived && snapshots.Count < limit)
            {
                AppendRecentArchivedTitleSnapshots(pawnId, limit, query, emittedKeys, snapshots);
            }

            return snapshots;
        }

        /// <summary>
        /// Builds a compact recent-prose context snapshot for one pawn. The snapshot contains only
        /// completed, player-visible generated text summaries: no prompts, raw provider responses, or
        /// fallback facts cross the integration boundary.
        /// </summary>
        internal DiaryContextSnapshot ContextSnapshotFor(Pawn pawn, int maxEntries)
        {
            return ContextSnapshotFor(pawn, maxEntries, null);
        }

        /// <summary>
        /// Builds a compact recent-prose context snapshot for one pawn, filtered by the same plain
        /// public query used by title reads.
        /// </summary>
        internal DiaryContextSnapshot ContextSnapshotFor(Pawn pawn, int maxEntries, DiaryEntryTitleQuery query)
        {
            if (!IsDiaryEligible(pawn))
            {
                return null;
            }

            DiaryContextSnapshot snapshot = new DiaryContextSnapshot();
            if (maxEntries <= 0)
            {
                return snapshot;
            }

            int configuredCap = DiaryTuning.IntegrationContextMaxEntries;
            int limit = maxEntries > configuredCap ? configuredCap : maxEntries;
            if (limit <= 0)
            {
                return snapshot;
            }

            int summaryMaxChars = DiaryTuning.IntegrationContextSummaryMaxChars;
            string pawnId = pawn.GetUniqueLoadID();
            HashSet<string> emittedKeys = new HashSet<string>();

            bool includeActive = query == null || query.includeActive;
            bool includeArchived = query == null || query.includeArchived;

            if (includeActive)
            {
                AppendRecentHotProseSnapshots(pawn, pawnId, limit, summaryMaxChars, query, emittedKeys, snapshot.entries);
            }

            if (includeArchived && snapshot.entries.Count < limit)
            {
                AppendRecentArchivedProseSnapshots(pawnId, limit, summaryMaxChars, query, emittedKeys, snapshot.entries);
            }

            PopulateContextSnapshotRange(snapshot);
            return snapshot;
        }

        /// <summary>
        /// Composes the public context read surfaces into one adapter-facing bundle. This adds no new
        /// policy: each field comes from the same helper as its standalone API method.
        /// </summary>
        internal DiaryContextBundleSnapshot ContextBundleSnapshotFor(
            Pawn pawn,
            int maxEntries,
            DiaryEntryTitleQuery query,
            bool includeImportantEventContext)
        {
            if (!IsDiaryEligible(pawn) || maxEntries <= 0)
            {
                return null;
            }

            DiaryContextBundleSnapshot bundle = new DiaryContextBundleSnapshot
            {
                writingStyle = WritingStyleSnapshotFor(pawn),
                pawnSummary = PawnSummarySnapshotFor(pawn),
                promptEnchantments = PromptEnchantmentCandidatesFor(pawn, includeImportantEventContext),
                recentContext = ContextSnapshotFor(pawn, maxEntries, query)
            };

            if (bundle.promptEnchantments == null)
            {
                bundle.promptEnchantments = new List<DiaryPromptEnchantmentCandidateSnapshot>();
            }

            if (bundle.recentContext == null)
            {
                bundle.recentContext = new DiaryContextSnapshot();
            }

            return bundle;
        }

        /// <summary>
        /// Builds a stable public handle for one saved event POV. Returns null when the event/role does
        /// not map to a pawn-owned diary page.
        /// </summary>
        internal static DiaryEntryHandle BuildEntryHandle(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(diaryEvent.eventId)
                || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            string pawnId = diaryEvent.PawnIdForRole(povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            return new DiaryEntryHandle
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                pawnId = pawnId,
                entryKey = EntryKeyFor(diaryEvent.eventId, povRole)
            };
        }

        /// <summary>
        /// Returns whether a direct-text integration may write a first-person page for this pawn right
        /// now. This mirrors the player's generation-enabled intent before a direct entry is created,
        /// so the API does not leave behind a not-generated event for a silenced pawn.
        /// </summary>
        internal bool CanWriteExternalDirectEntryFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary != null && !diary.diaryGenerationEnabled)
            {
                return false;
            }

            return !ShouldSkipFirstPersonGenerationForIncapacitation(pawn);
        }

        /// <summary>
        /// Writes caller-authored prose to an existing event POV and optionally queues a title-only
        /// follow-up. Returns false when the cleaned prose is blank or the POV was already skipped.
        /// </summary>
        internal bool ApplyExternalDirectEntryText(
            DiaryEvent diaryEvent,
            string povRole,
            string text,
            string title,
            bool generateTitleIfMissing)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole) || diaryEvent.IsSkipped(povRole))
            {
                return false;
            }

            string cleanedText = ExternalDirectEntryText.CleanProse(text, DiaryTuning.IntegrationDirectTextMaxChars);
            if (string.IsNullOrWhiteSpace(cleanedText))
            {
                return false;
            }

            diaryEvent.MarkInjectedTextComplete(povRole, cleanedText);
            MarkGeneratedEntryUnread(diaryEvent, povRole);

            string cleanedTitle = ExternalDirectEntryText.CleanTitle(title, DiaryTuning.IntegrationDirectTitleMaxChars);
            bool notificationSent = false;
            if (!string.IsNullOrWhiteSpace(cleanedTitle))
            {
                diaryEvent.MarkTitleComplete(povRole, cleanedTitle);
            }
            else if (generateTitleIfMissing
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.generateTitles)
            {
                notificationSent = QueueTitleRequest(diaryEvent, povRole, null);
            }

            if (!notificationSent)
            {
                NotifyEntryStatusChanged(diaryEvent, povRole);
            }

            return true;
        }

        /// <summary>
        /// Publishes the current public lifecycle snapshot for one event POV to API v10 listeners.
        /// This is intentionally component-owned rather than DiaryEvent-owned: building the public DTO
        /// needs the active/archive stores, group display data, and current integration settings.
        /// </summary>
        internal void NotifyEntryStatusChanged(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(diaryEvent.eventId)
                || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            EntryStatusListeners.Notify(EntryStatusFor(diaryEvent.eventId, povRole));
        }

        /// <summary>
        /// Builds a compact status snapshot for one handled diary entry. Called only by the public API,
        /// which owns main-thread/readiness checks and exception logging.
        /// </summary>
        internal DiaryEntryStatusSnapshot EntryStatusFor(string eventId, string povRole)
        {
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            DiaryEvent diaryEvent = events.FindEvent(eventId);
            if (diaryEvent == null)
            {
                return null;
            }

            string role = NormalizePovRole(povRole);
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            string pawnId = diaryEvent.PawnIdForRole(role);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            bool archived = EventIsArchivedForScans(diaryEvent, ActiveScanEventIds());
            DiaryEntryView view = diaryEvent.ToViewFor(pawnId, archived);
            if (view == null || !DiaryEvent.RoleEquals(view.PovRole, role))
            {
                return null;
            }

            string status = view.LlmStatus ?? string.Empty;
            string titleStatus = diaryEvent.TitleStatusForRole(view.PovRole);
            bool hasGeneratedText = !string.IsNullOrWhiteSpace(view.GeneratedText);
            DiaryTextDecorationContext decoration = view.TextDecorationContext;

            return new DiaryEntryStatusSnapshot
            {
                handle = new DiaryEntryHandle
                {
                    eventId = view.EventId ?? string.Empty,
                    povRole = view.PovRole ?? string.Empty,
                    pawnId = pawnId,
                    entryKey = view.EntryKey ?? EntryKeyFor(view.EventId, view.PovRole)
                },
                tick = view.Tick,
                date = view.Date ?? string.Empty,
                status = status,
                pending = DiaryEvent.RoleEquals(status, DiaryEvent.PendingStatus) && !view.ArchivedGenerationStale,
                complete = hasGeneratedText,
                failed = view.ArchivedGenerationStale || DiaryEvent.RoleEquals(status, DiaryEvent.FailedStatus),
                skipped = DiaryEvent.RoleEquals(status, DiaryEvent.SkippedStatus),
                promptOnly = DiaryEvent.RoleEquals(status, DiaryEvent.PromptOnlyStatus),
                archived = archived,
                archivedGenerationStale = view.ArchivedGenerationStale,
                hasGeneratedText = hasGeneratedText,
                title = view.Title ?? string.Empty,
                titleStatus = titleStatus,
                titlePending = view.TitlePending || DiaryEvent.RoleEquals(titleStatus, DiaryEvent.PendingStatus),
                titleComplete = !string.IsNullOrWhiteSpace(view.Title),
                groupLabel = view.GroupLabel ?? string.Empty,
                externallyAuthored = view.ExternallyAuthored,
                externalSourceId = view.ExternalSourceId ?? string.Empty,
                domain = decoration?.domain ?? string.Empty,
                atmosphereCue = view.AtmosphereCue ?? string.Empty,
                summary = hasGeneratedText
                    ? DiarySentenceExcerpt.FirstSentence(view.GeneratedText, DiaryTuning.IntegrationContextSummaryMaxChars)
                    : string.Empty
            };
        }

        /// <summary>
        /// Builds the prompt-free public snapshot for one handled diary entry. The handle carries the
        /// pawn id, so compact archive lookup can use the exact archive key.
        /// </summary>
        internal DiaryEntrySnapshot EntrySnapshotFor(DiaryEntryHandle handle)
        {
            if (handle == null)
            {
                return null;
            }

            return EntrySnapshotFor(handle.eventId, handle.povRole, handle.pawnId);
        }

        /// <summary>
        /// Builds the prompt-free public snapshot for one event id and POV role.
        /// </summary>
        internal DiaryEntrySnapshot EntrySnapshotFor(string eventId, string povRole)
        {
            return EntrySnapshotFor(eventId, povRole, string.Empty);
        }

        private DiaryEntrySnapshot EntrySnapshotFor(string eventId, string povRole, string pawnIdFromHandle)
        {
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            string role = NormalizePovRole(povRole);
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            DiaryEvent diaryEvent = events.FindEvent(eventId);
            if (diaryEvent != null)
            {
                string pawnId = diaryEvent.PawnIdForRole(role);
                if (string.IsNullOrWhiteSpace(pawnId))
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(pawnIdFromHandle)
                    && !string.Equals(pawnIdFromHandle, pawnId, System.StringComparison.Ordinal))
                {
                    return null;
                }

                bool archivedForScans = EventIsArchivedForScans(diaryEvent, ActiveScanEventIds());
                DiaryEntryView view = diaryEvent.ToViewFor(pawnId, archivedForScans);
                if (view == null || !DiaryEvent.RoleEquals(view.PovRole, role))
                {
                    return null;
                }

                return EntrySnapshotFromView(
                    view,
                    pawnId,
                    archivedForScans,
                    diaryEvent.TitleStatusForRole(view.PovRole));
            }

            ArchivedDiaryEntry archivedEntry = string.IsNullOrWhiteSpace(pawnIdFromHandle)
                ? archive.FindByEventAndRole(eventId, role)
                : archive.Find(eventId, pawnIdFromHandle, role);
            DiaryEntryView archivedView = archivedEntry?.ToView();
            if (archivedEntry == null || archivedView == null)
            {
                return null;
            }

            return EntrySnapshotFromView(
                archivedView,
                archivedEntry.pawnId,
                true,
                string.IsNullOrWhiteSpace(archivedView.Title)
                    ? DiaryEvent.NotGeneratedStatus
                    : DiaryEvent.CompleteStatus);
        }

        private static DiaryEntrySnapshot EntrySnapshotFromView(
            DiaryEntryView view,
            string pawnId,
            bool archived,
            string titleStatus)
        {
            if (view == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            string status = view.LlmStatus ?? string.Empty;
            bool hasGeneratedText = !string.IsNullOrWhiteSpace(view.GeneratedText);
            DiaryTextDecorationContext decoration = view.TextDecorationContext;
            string safeTitleStatus = titleStatus ?? string.Empty;

            return new DiaryEntrySnapshot
            {
                handle = new DiaryEntryHandle
                {
                    eventId = view.EventId ?? string.Empty,
                    povRole = view.PovRole ?? string.Empty,
                    pawnId = pawnId,
                    entryKey = view.EntryKey ?? EntryKeyFor(view.EventId, view.PovRole)
                },
                tick = view.Tick,
                date = view.Date ?? string.Empty,
                status = status,
                pending = DiaryEvent.RoleEquals(status, DiaryEvent.PendingStatus) && !view.ArchivedGenerationStale,
                complete = hasGeneratedText,
                failed = view.ArchivedGenerationStale || DiaryEvent.RoleEquals(status, DiaryEvent.FailedStatus),
                skipped = DiaryEvent.RoleEquals(status, DiaryEvent.SkippedStatus),
                promptOnly = DiaryEvent.RoleEquals(status, DiaryEvent.PromptOnlyStatus),
                archived = archived,
                archivedGenerationStale = view.ArchivedGenerationStale,
                hasGeneratedText = hasGeneratedText,
                generatedText = hasGeneratedText ? view.GeneratedText : string.Empty,
                summary = hasGeneratedText
                    ? DiarySentenceExcerpt.FirstSentence(view.GeneratedText, DiaryTuning.IntegrationContextSummaryMaxChars)
                    : string.Empty,
                title = view.Title ?? string.Empty,
                titleStatus = safeTitleStatus,
                titlePending = view.TitlePending || DiaryEvent.RoleEquals(safeTitleStatus, DiaryEvent.PendingStatus),
                titleComplete = !string.IsNullOrWhiteSpace(view.Title),
                groupLabel = view.GroupLabel ?? string.Empty,
                externallyAuthored = view.ExternallyAuthored,
                externalSourceId = view.ExternalSourceId ?? string.Empty,
                domain = decoration?.domain ?? string.Empty,
                atmosphereCue = view.AtmosphereCue ?? string.Empty
            };
        }

        /// <summary>
        /// Builds the structured pawn-summary snapshot for the public integration API (API v6,
        /// capability C-CTX-2). Side-effect free: the snapshot reads live pawn state only via
        /// <see cref="DiaryContextBuilder.BuildPawnSummarySnapshot"/>, so — unlike
        /// <see cref="WritingStyleSnapshotFor"/> — it never touches the diary record and cannot create
        /// one. Returns null for an ineligible pawn (the same humanlike-colonist gate the other
        /// readers use).
        /// </summary>
        internal DiaryPawnSummarySnapshot PawnSummarySnapshotFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return null;
            }

            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                return DiaryContextBuilder.BuildPawnSummarySnapshot(pawn);
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
        }

        /// <summary>
        /// Collects prompt-enchantment candidates for the public integration API (API v6, capability
        /// C-CTX-3). Exports the candidate SET the planner would choose among right now after
        /// suppression, live event/condition candidates, and weight multipliers, but before the
        /// single rolled winner. Returns an empty list when ineligible, disabled, or no candidates
        /// match.
        /// </summary>
        internal List<DiaryPromptEnchantmentCandidateSnapshot> PromptEnchantmentCandidatesFor(
            Pawn pawn, bool includeImportantEventContext)
        {
            List<DiaryPromptEnchantmentCandidateSnapshot> snapshots =
                new List<DiaryPromptEnchantmentCandidateSnapshot>();
            if (!IsDiaryEligible(pawn))
            {
                return snapshots;
            }

            // Honor the same player setting PromptEnchantments.RuleFor does: when the player has
            // disabled prompt enchantments, the candidate set is empty too — the export must reflect
            // what the diary would actually feed a prompt, not a parallel collection path.
            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.enablePromptEnchantments)
            {
                return snapshots;
            }

            PromptEnchantmentTuning tuning = DiaryTuning.PromptEnchantmentTuning;
            List<PromptEnchantmentCandidate> normalCandidates = new List<PromptEnchantmentCandidate>();
            List<DiaryPromptEnchantmentDef> defs =
                DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading;
            if (defs != null && defs.Count > 0)
            {
                Rand.PushState();
                try
                {
                    normalCandidates = PromptEnchantmentCollector.Collect(
                        pawn,
                        defs,
                        includeImportantEventContext,
                        tuning);
                }
                finally
                {
                    Rand.PopState();
                }
            }

            // Mirror PromptEnchantmentRuleFor: live event/condition biasing feeds the same pool as
            // ordinary XML health candidates, then suppression and normal-weight damping run before
            // the final weighted roll. The export stops just before that roll.
            float eventWindowNormalMultiplier;
            List<PromptEnchantmentCandidate> extraCandidates =
                ActiveEventWindowPromptCandidates(pawn, out eventWindowNormalMultiplier);
            float observedConditionNormalMultiplier;
            extraCandidates.AddRange(
                ActiveObservedConditionPromptCandidates(pawn, out observedConditionNormalMultiplier));
            List<PromptEnchantmentCandidate> candidates = PromptEnchantmentPlanner.PrepareCandidatesForBuild(
                normalCandidates,
                extraCandidates,
                eventWindowNormalMultiplier * observedConditionNormalMultiplier,
                HediffPersonaOverrides.SuppressedPromptHediffDefNamesFor(pawn));

            for (int i = 0; i < candidates.Count; i++)
            {
                DiaryPromptEnchantmentCandidateSnapshot snapshot =
                    DiaryPromptEnchantmentCandidateSnapshot.From(candidates[i]);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        private void AppendRecentHotTitleSnapshots(
            Pawn pawn,
            string pawnId,
            int limit,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            HashSet<string> activeEventIds = ActiveScanEventIds();
            for (int i = diary.eventIds.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                DiaryEntryView view = diaryEvent?.ToViewFor(pawnId, EventIsArchivedForScans(diaryEvent, activeEventIds));
                TryAppendTitleSnapshot(view, false, query, emittedKeys, snapshots);
            }
        }

        private void AppendRecentArchivedTitleSnapshots(
            string pawnId,
            int limit,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.EntriesForPawn(pawnId);
            if (archivedEntries == null)
            {
                return;
            }

            for (int i = archivedEntries.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                ArchivedDiaryEntry archivedEntry = archivedEntries[i];
                TryAppendTitleSnapshot(archivedEntry?.ToView(), true, query, emittedKeys, snapshots);
            }
        }

        private void AppendRecentHotProseSnapshots(
            Pawn pawn,
            string pawnId,
            int limit,
            int summaryMaxChars,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryProseSnapshot> snapshots)
        {
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            HashSet<string> activeEventIds = ActiveScanEventIds();
            for (int i = diary.eventIds.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                DiaryEntryView view = diaryEvent?.ToViewFor(pawnId, EventIsArchivedForScans(diaryEvent, activeEventIds));
                TryAppendProseSnapshot(view, false, summaryMaxChars, query, emittedKeys, snapshots);
            }
        }

        private void AppendRecentArchivedProseSnapshots(
            string pawnId,
            int limit,
            int summaryMaxChars,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryProseSnapshot> snapshots)
        {
            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.EntriesForPawn(pawnId);
            if (archivedEntries == null)
            {
                return;
            }

            for (int i = archivedEntries.Count - 1; i >= 0 && snapshots.Count < limit; i--)
            {
                ArchivedDiaryEntry archivedEntry = archivedEntries[i];
                TryAppendProseSnapshot(archivedEntry?.ToView(), true, summaryMaxChars, query, emittedKeys, snapshots);
            }
        }

        private void AppendHotEntryStats(
            Pawn pawn,
            string pawnId,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            DiaryEntryStatsSnapshot stats)
        {
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            HashSet<string> activeEventIds = ActiveScanEventIds();
            for (int i = diary.eventIds.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                DiaryEntryView view = diaryEvent?.ToViewFor(pawnId, EventIsArchivedForScans(diaryEvent, activeEventIds));
                TryAccumulateEntryStats(view, false, query, emittedKeys, stats);
            }
        }

        private void AppendArchivedEntryStats(
            string pawnId,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            DiaryEntryStatsSnapshot stats)
        {
            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.EntriesForPawn(pawnId);
            if (archivedEntries == null)
            {
                return;
            }

            // Newest-first, with a per-call row cap so a long-lived colonist's full archive is never
            // walked on one stats read. Counts are approximate beyond this cap. The sibling title/prose
            // reads are already bounded by their returned-list limit; stats has no such natural cap.
            int scanned = 0;
            for (int i = archivedEntries.Count - 1; i >= 0; i--)
            {
                if (scanned >= StatsArchiveScanLimit)
                {
                    break;
                }

                scanned++;
                ArchivedDiaryEntry archivedEntry = archivedEntries[i];
                TryAccumulateEntryStats(archivedEntry?.ToView(), true, query, emittedKeys, stats);
            }
        }

        private static void TryAppendTitleSnapshot(
            DiaryEntryView view,
            bool archived,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryTitleSnapshot> snapshots)
        {
            if (view == null || snapshots == null || !ViewHasCompletedDiaryPage(view))
            {
                return;
            }

            if (!DiaryEntryTitleFilter.Matches(FilterFactsFor(view, archived), query))
            {
                return;
            }

            string key = view.EntryKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && emittedKeys != null && !emittedKeys.Add(key))
            {
                return;
            }

            snapshots.Add(new DiaryEntryTitleSnapshot
            {
                tick = view.Tick,
                date = view.Date ?? string.Empty,
                eventId = view.EventId ?? string.Empty,
                povRole = view.PovRole ?? string.Empty,
                title = view.Title ?? string.Empty,
                groupLabel = view.GroupLabel ?? string.Empty,
                externallyAuthored = view.ExternallyAuthored,
                externalSourceId = view.ExternalSourceId ?? string.Empty,
                archived = archived
            });
        }

        private static void TryAccumulateEntryStats(
            DiaryEntryView view,
            bool archived,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            DiaryEntryStatsSnapshot stats)
        {
            if (view == null || stats == null)
            {
                return;
            }

            DiaryEntryTitleFilterFacts facts = FilterFactsFor(view, archived);
            if (!DiaryEntryTitleFilter.Matches(facts, query))
            {
                return;
            }

            string key = view.EntryKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && emittedKeys != null && !emittedKeys.Add(key))
            {
                return;
            }

            DiaryEntryStatsAccumulator.Add(stats, facts);
        }

        private static void TryAppendProseSnapshot(
            DiaryEntryView view,
            bool archived,
            int summaryMaxChars,
            DiaryEntryTitleQuery query,
            HashSet<string> emittedKeys,
            List<DiaryEntryProseSnapshot> snapshots)
        {
            if (view == null || snapshots == null || string.IsNullOrWhiteSpace(view.GeneratedText))
            {
                return;
            }

            if (!DiaryEntryTitleFilter.Matches(FilterFactsFor(view, archived), query))
            {
                return;
            }

            string summary = DiarySentenceExcerpt.FirstSentence(view.GeneratedText, summaryMaxChars);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            string key = view.EntryKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && emittedKeys != null && !emittedKeys.Add(key))
            {
                return;
            }

            DiaryTextDecorationContext decoration = view.TextDecorationContext;
            snapshots.Add(new DiaryEntryProseSnapshot
            {
                tick = view.Tick,
                date = view.Date ?? string.Empty,
                eventId = view.EventId ?? string.Empty,
                povRole = view.PovRole ?? string.Empty,
                title = view.Title ?? string.Empty,
                groupLabel = view.GroupLabel ?? string.Empty,
                externallyAuthored = view.ExternallyAuthored,
                externalSourceId = view.ExternalSourceId ?? string.Empty,
                domain = decoration?.domain ?? string.Empty,
                atmosphereCue = view.AtmosphereCue ?? string.Empty,
                summary = summary,
                archived = archived
            });
        }

        private static void PopulateContextSnapshotRange(DiaryContextSnapshot snapshot)
        {
            if (snapshot == null || snapshot.entries == null)
            {
                return;
            }

            snapshot.entryCount = snapshot.entries.Count;
            if (snapshot.entries.Count == 0)
            {
                snapshot.newestTick = 0;
                snapshot.oldestTick = 0;
                snapshot.newestDate = string.Empty;
                snapshot.oldestDate = string.Empty;
                return;
            }

            // Explicit min/max scan rather than positional reads. snapshot.entries is built hot-first
            // then archive, newest-first within each store, but it is NOT globally sorted by tick — a
            // backdated archive row appended late would otherwise be reported as the oldest (or a hot
            // row older than an archive row as the newest). Scan every entry so newest/oldest reflect
            // the true tick range regardless of insertion order. Ties on newest go to the first seen;
            // ties on oldest go to the first seen, matching the previous positional behavior for the
            // common fully-sorted case.
            int newestTick = int.MinValue;
            int oldestTick = int.MaxValue;
            string newestDate = string.Empty;
            string oldestDate = string.Empty;
            for (int i = 0; i < snapshot.entries.Count; i++)
            {
                DiaryEntryProseSnapshot entry = snapshot.entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.tick > newestTick)
                {
                    newestTick = entry.tick;
                    newestDate = entry.date ?? string.Empty;
                }

                if (entry.tick < oldestTick)
                {
                    oldestTick = entry.tick;
                    oldestDate = entry.date ?? string.Empty;
                }
            }

            // If every entry had the sentinel default tick (0), the loop above leaves newestTick at
            // int.MinValue; fall back to 0 so the snapshot reports a stable, sensible value rather
            // than the sentinel. This matches the previous behavior for all-zero-tick snapshots.
            snapshot.newestTick = newestTick == int.MinValue ? 0 : newestTick;
            snapshot.oldestTick = oldestTick == int.MaxValue ? 0 : oldestTick;
            snapshot.newestDate = newestDate;
            snapshot.oldestDate = oldestDate;
        }

        private static bool ViewHasCompletedDiaryPage(DiaryEntryView view)
        {
            return view != null
                && (!string.IsNullOrWhiteSpace(view.GeneratedText)
                    || !string.IsNullOrWhiteSpace(view.Title));
        }

        private static string NormalizePovRole(string povRole)
        {
            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.InitiatorRole))
            {
                return DiaryEvent.InitiatorRole;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
            {
                return DiaryEvent.RecipientRole;
            }

            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole))
            {
                return DiaryEvent.NeutralRole;
            }

            return string.Empty;
        }

        private static string EntryKeyFor(string eventId, string povRole)
        {
            return (eventId ?? string.Empty) + "|" + (povRole ?? string.Empty);
        }

        private static DiaryEntryTitleFilterFacts FilterFactsFor(DiaryEntryView view, bool archived)
        {
            DiaryTextDecorationContext decoration = view?.TextDecorationContext;
            return new DiaryEntryTitleFilterFacts
            {
                tick = view?.Tick ?? 0,
                date = view?.Date ?? string.Empty,
                povRole = view?.PovRole ?? string.Empty,
                domain = decoration?.domain ?? string.Empty,
                atmosphereCue = view?.AtmosphereCue ?? string.Empty,
                sourceId = view?.ExternalSourceId ?? string.Empty,
                eventKey = decoration?.defName ?? string.Empty,
                partnerPawnId = view?.LinkedEntry?.OtherPawnId ?? string.Empty,
                important = view != null && view.Important,
                hasTitle = view != null && !string.IsNullOrWhiteSpace(view.Title),
                hasGeneratedText = view != null && !string.IsNullOrWhiteSpace(view.GeneratedText),
                status = view?.LlmStatus ?? string.Empty,
                archivedGenerationStale = view != null && view.ArchivedGenerationStale,
                archived = archived
            };
        }
    }
}
