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

            return DiaryContextBuilder.BuildPawnSummarySnapshot(pawn);
        }

        /// <summary>
        /// Collects prompt-enchantment candidates for the public integration API (API v6, capability
        /// C-CTX-3). Exports the candidate SET the planner would choose among right now (the
        /// deterministic input), never the single rolled winner. Returns an empty list when
        /// enchantments are disabled in settings or no candidates match.
        /// </summary>
        internal List<DiaryPromptEnchantmentCandidateSnapshot> PromptEnchantmentCandidatesFor(
            Pawn pawn, bool includeImportantEventContext)
        {
            List<DiaryPromptEnchantmentCandidateSnapshot> snapshots =
                new List<DiaryPromptEnchantmentCandidateSnapshot>();
            if (pawn == null)
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
            List<DiaryPromptEnchantmentDef> defs =
                DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return snapshots;
            }

            // Mirrors PromptEnchantments.RuleFor's collection step without the suppression pass or the
            // final Rand roll. Suppression (suppressedHediffDefNames) is intentionally not applied:
            // the export shows what COULD be chosen right now, including candidates a prompt-time call
            // might suppress because they are already voiced by a hediff style override.
            List<PromptEnchantmentCandidate> candidates = PromptEnchantmentCollector.Collect(
                pawn,
                defs,
                includeImportantEventContext,
                tuning);

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
                archived = archived
            });
        }

        private static bool ViewHasCompletedDiaryPage(DiaryEntryView view)
        {
            return view != null
                && (!string.IsNullOrWhiteSpace(view.GeneratedText)
                    || !string.IsNullOrWhiteSpace(view.Title));
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
                archived = archived
            };
        }
    }
}
