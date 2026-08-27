// Dialog_MemoryLibrary.Labels.cs — cached, localized display text for the M9 Library.
//
// Formatting runs when detached publications change so hot draw paths reuse bounded strings.
// Translation remains on RimWorld's main thread and no saved memory is mutated here.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_MemoryLibrary
    {

        private static string QueryStateText(string status)
        {
            if (status == MemoryLibraryStatuses.Missing)
                return T("PawnDiary.Memory.Library.Missing");
            if (status == MemoryLibraryStatuses.Invalid)
                return T("PawnDiary.Memory.Library.Invalid");
            if (status == MemoryLibraryStatuses.Stale)
                return T("PawnDiary.Memory.Library.Refreshing");
            return T("PawnDiary.Memory.Library.Preparing");
        }

        private static void DrawCenteredState(Rect rect, string value)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect, value ?? string.Empty);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private string LifetimeLabel(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length > 0 && cachedLifetimeLabels.TryGetValue(key, out string cached))
                return cached;
            return BuildLifetimeLabel(row);
        }

        private string BuildLifetimeLabel(MemoryBlockRow row)
        {
            MemoryLibraryUiLifetime life = MemoryLibraryUiPolicy.Lifetime(row, detachedNowTick,
                detachedMinorLifetimeTicks, detachedRegularLifetimeTicks);
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Protected)
                return T("PawnDiary.Memory.Library.LifetimeProtected");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Important)
                return T("PawnDiary.Memory.Library.LifetimeImportant");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Due)
                return T("PawnDiary.Memory.Library.LifetimeDue");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Mixed)
                return T("PawnDiary.Memory.Library.LifetimeMixed",
                    life.expiryTick == long.MaxValue
                        ? T("PawnDiary.Memory.Library.DateUnknown")
                        : DateLabel(life.expiryTick));
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Unknown)
                return T("PawnDiary.Memory.Library.LifetimeUnknown");
            long days = Math.Max(1, (life.remainingTicks + 59999L) / 60000L);
            return life.stateToken == MemoryLibraryUiLifetimeTokens.Regular
                ? T("PawnDiary.Memory.Library.LifetimeRegular", days)
                : T("PawnDiary.Memory.Library.LifetimeMinor", days);
        }

        private static string ImportanceLabel(int mask)
        {
            if ((mask & MemoryLibraryPolicy.ImportanceImportant) != 0)
                return T("PawnDiary.Memory.Library.ImportanceImportant");
            if ((mask & MemoryLibraryPolicy.ImportanceRegular) != 0)
                return T("PawnDiary.Memory.Library.ImportanceRegular");
            if ((mask & MemoryLibraryPolicy.ImportanceMinor) != 0)
                return T("PawnDiary.Memory.Library.ImportanceMinor");
            return T("PawnDiary.Memory.Library.All");
        }

        private static string ContentCategoryLabel(int mask)
        {
            List<string> labels = new List<string>();
            if ((mask & MemoryCategoryBits.Personal) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryPersonal"));
            if ((mask & MemoryCategoryBits.Relationships) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryRelationships"));
            if ((mask & MemoryCategoryBits.Family) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryFamily"));
            if ((mask & MemoryCategoryBits.Factions) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryFactions"));
            return labels.Count == 0
                ? T("PawnDiary.Memory.Library.All") : string.Join(", ", labels.ToArray());
        }

        private static string RootTypeLabel(string token)
        {
            if (string.Equals(token, "person", StringComparison.OrdinalIgnoreCase))
                return T("PawnDiary.Memory.Library.RootPerson");
            if (string.Equals(token, "faction", StringComparison.OrdinalIgnoreCase))
                return T("PawnDiary.Memory.Library.RootFaction");
            return T("PawnDiary.Memory.Library.RootOngoingStory");
        }

        private string ChapterLabel(string chapterId)
        {
            if (threadDetail?.chapters == null) return T("PawnDiary.Memory.Library.ChapterUnknown");
            for (int index = 0; index < threadDetail.chapters.Count; index++)
                if (string.Equals(threadDetail.chapters[index]?.chapterId, chapterId,
                        StringComparison.Ordinal))
                {
                    MemoryChapterRow chapter = threadDetail.chapters[index];
                    string label = T("PawnDiary.Memory.Library.Chapter",
                        Math.Max(1L, chapter.ordinal));
                    string phase = ChapterPhaseLabel(chapter.phaseToken);
                    if (phase.Length > 0) label += " · " + phase;
                    if (chapter.continuedFromPrevious)
                        label += " · " + T("PawnDiary.Memory.Library.ChapterContinued");
                    return label;
                }
            return T("PawnDiary.Memory.Library.ChapterUnknown");
        }

        private static string ChapterPhaseLabel(string token)
        {
            switch (token)
            {
                case "relationship_phase": return T("PawnDiary.Memory.Library.PhaseRelationship");
                case "family_lifecycle": return T("PawnDiary.Memory.Library.PhaseFamily");
                case "body_state": return T("PawnDiary.Memory.Library.PhaseBody");
                case "membership_state": return T("PawnDiary.Memory.Library.PhaseMembership");
                case "growth_stage": return T("PawnDiary.Memory.Library.PhaseGrowth");
                case "belief_state": return T("PawnDiary.Memory.Library.PhaseBelief");
                case "role_state": return T("PawnDiary.Memory.Library.PhaseRole");
                case "title_state": return T("PawnDiary.Memory.Library.PhaseTitle");
                case "psylink_state": return T("PawnDiary.Memory.Library.PhasePsylink");
                case "genetic_state": return T("PawnDiary.Memory.Library.PhaseGenetic");
                case "mechlink_state": return T("PawnDiary.Memory.Library.PhaseMechlink");
                case "persona_bond_state": return T("PawnDiary.Memory.Library.PhasePersonaBond");
                case "opinion_episode": return T("PawnDiary.Memory.Library.PhaseOpinion");
                case "formal_relationship": return T("PawnDiary.Memory.Library.PhaseFormalRelationship");
                case "faction_diplomacy": return T("PawnDiary.Memory.Library.PhaseFactionDiplomacy");
                case "faction_lifecycle": return T("PawnDiary.Memory.Library.PhaseFactionLifecycle");
                default: return string.Empty;
            }
        }

        private static string SummaryRoleLabel(MemoryBlockRow row)
        {
            if (row?.rollingSummary == true) return T("PawnDiary.Memory.Library.RollingSummary");
            if (row?.closedSummary == true) return T("PawnDiary.Memory.Library.ClosedSummary");
            return T("PawnDiary.Memory.Library.EventMemory");
        }

        private static string ProviderExposureLabel(string state)
        {
            if (state == "not_sent") return T("PawnDiary.Memory.Library.ProviderNotSent");
            if (state == "potentially_sent")
                return T("PawnDiary.Memory.Library.ProviderPotential");
            if (state == "confirmed_sent")
                return T("PawnDiary.Memory.Library.ProviderConfirmed");
            return T("PawnDiary.Memory.Library.ProviderUnknown");
        }

        private static string ArchiveSourceLabel(MemoryArchiveHandle handle)
        {
            return handle?.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported
                ? T("PawnDiary.Memory.Library.ImportedSourceLegacy")
                : T("PawnDiary.Memory.Library.ImportedSourceArchive");
        }

        private static string MigrationReasonLabel(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? T("PawnDiary.Memory.Library.ImportedReasonUnavailable")
                : T("PawnDiary.Memory.Library.ImportedReasonMigration");
        }

        private string DiagnosticText(MemoryBlockDetail detail)
        {
            if (detail == null) return T("PawnDiary.Memory.Library.DiagnosticsUnavailable");
            MemoryBlockRow row = blockDetail?.row;
            StringBuilder builder = new StringBuilder(768);
            builder.AppendLine(T("PawnDiary.Memory.Library.DiagnosticsIdentity",
                row?.recordHandle?.ownerPawnId ?? string.Empty,
                row?.recordHandle?.epochToken ?? string.Empty,
                row?.recordHandle?.recordId ?? string.Empty,
                row?.rootHandle?.rootId ?? T("PawnDiary.Memory.Library.None"),
                row?.chapterId ?? T("PawnDiary.Memory.Library.None")));
            builder.AppendLine(T("PawnDiary.Memory.Library.DiagnosticsState",
                row?.kind ?? string.Empty, row?.targetStructuralRevision ?? 0,
                row?.originalTick ?? -1, row?.suppressed ?? false,
                row?.playerEdited ?? false, LifetimeLabel(row), detachedTextCap));
            builder.Append(T("PawnDiary.Memory.Library.DiagnosticsBody",
                Join(detail.factDescriptors), Join(detail.subjectDescriptors),
                Join(detail.provenanceDescriptors),
                EmptyFallback(detail.sourcePageLinkToken,
                    T("PawnDiary.Memory.Library.None")),
                EmptyFallback(detail.automaticWording,
                    T("PawnDiary.Memory.Library.None")),
                Join(detail.devIdentifiersAndReasons)));
            return MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                builder.ToString(), detachedDiagnosticTextCap);
        }

        private static string Join(List<string> values)
        {
            return values == null || values.Count == 0
                ? T("PawnDiary.Memory.Library.None") : string.Join(", ", values.ToArray());
        }

        private static string CommandStatusText(string status)
        {
            if (status == MemoryLibraryCommandStatuses.Success)
                return T("PawnDiary.Memory.Library.CommandSuccess");
            if (status == MemoryLibraryCommandStatuses.Stale)
                return T("PawnDiary.Memory.Library.CommandStale");
            if (status == MemoryLibraryCommandStatuses.CapFull)
                return T("PawnDiary.Memory.Library.EditCapFull");
            if (status == "QueueFull")
                return T("PawnDiary.Memory.Library.CommandBusy");
            if (status == MemoryLibraryCommandStatuses.Missing)
                return T("PawnDiary.Memory.Library.CommandMissing");
            return T("PawnDiary.Memory.Library.CommandRejected");
        }

        private string DateLabel(long tick)
        {
            if (tick < 0) return T("PawnDiary.Memory.Library.DateUnknown");
            if (cachedDateLabels.TryGetValue(tick, out string cached)) return cached;
            return FormatDateLabel(tick);
        }

        /// <summary>
        /// Formats bounded row display state once per detached publication/day/language tuple. Draw
        /// passes perform dictionary lookups and never translate or recalculate TTL for every row.
        /// </summary>
        private void RefreshDisplayCaches()
        {
            string signature = string.Join("|",
                owners?.directoryRevision ?? 0,
                OwnerKey(session.selectedOwnerHandle),
                list?.listSnapshotRevision ?? 0,
                list?.returnedStart ?? 0,
                threadDetail?.detailSnapshotRevision ?? 0,
                threadDetail?.returnedStart ?? 0,
                blockDetail?.targetStructuralRevision ?? 0,
                blockDetail?.targetStatusRevision ?? 0,
                blockDetail?.status ?? string.Empty,
                importedDetail?.archiveTextSnapshotRevision ?? 0,
                importedDetail?.status ?? string.Empty,
                importedDetail?.returnedTextStart ?? 0,
                RecordKey(session.selectedRecordHandle),
                session.selectedView ?? string.Empty,
                MemoryEffectivePolicyProvider.PublicationRevision,
                detachedNowTick / 60000L,
                Prefs.DevMode ? 1 : 0,
                LanguageDatabase.activeLanguage?.GetHashCode() ?? 0);
            if (string.Equals(displayCacheSignature, signature, StringComparison.Ordinal)) return;
            displayCacheSignature = signature;
            cachedBlockMeta.Clear();
            cachedBlockChips.Clear();
            cachedLifetimeLabels.Clear();
            cachedDateLabels.Clear();
            cachedUsageLabels.Clear();
            cachedListCardTitles.Clear();
            cachedListCardDetails.Clear();
            cachedListCardChips.Clear();
            cachedListCardDates.Clear();
            cachedBlockCardWording.Clear();
            cachedThreadHeaderText = string.Empty;
            cachedBlockFactsText = string.Empty;
            cachedDiagnosticText = string.Empty;
            if (list?.rows != null)
            {
                for (int index = 0; index < list.rows.Count; index++)
                {
                    MemoryLibraryListRow row = list.rows[index];
                    CacheBlockDisplay(row?.standalone);
                    CacheDate(row?.thread?.latestActivityTick ?? -1);
                    CacheDate(row?.imported?.originalTick ?? -1);
                    CacheListCardDisplay(row);
                }
            }
            if (threadDetail?.blocks != null)
                for (int index = 0; index < threadDetail.blocks.Count; index++)
                    CacheBlockDisplay(threadDetail.blocks[index]);
            CacheBlockDisplay(blockDetail?.row);
            CacheDate(threadDetail?.currentStatus?.capturedTick ?? -1);
            cachedThreadHeaderText = BuildThreadHeaderText();
            cachedBlockFactsText = BuildNormalFactsText();
            cachedDiagnosticText = Prefs.DevMode
                ? DiagnosticText(blockDetail?.detail) : string.Empty;
            MemoryOwnerCultureDto culture = selectedOwner?.culture;
            cachedCultureTitle = T("PawnDiary.Memory.Library.CulturalContext");
            cachedCultureOrigin = CultureLine(culture?.originStateToken,
                culture?.originDisplayLabel, culture?.originProvenanceToken);
            cachedCultureHasAdopted = culture != null
                && (culture.adoptedStateToken != "none"
                    || !string.IsNullOrWhiteSpace(culture.adoptedDisplayLabel));
            cachedCultureAdopted = cachedCultureHasAdopted
                ? T("PawnDiary.Memory.Library.CultureAdopted",
                    CultureStateLabel(culture.adoptedStateToken,
                        culture.adoptedDisplayLabel, false))
                : string.Empty;
            cachedCultureExplanation = T("PawnDiary.Memory.Library.CultureExplanation");
        }

        private string BuildUsageFacts(MemoryBlockRow row)
        {
            if (row == null) return string.Empty;
            string last = row.lastAutomaticIncludedTick < 0
                ? T("PawnDiary.Memory.Library.Never") : DateLabel(row.lastAutomaticIncludedTick);
            return T("PawnDiary.Memory.Library.Usage", last, row.automaticInclusionCount,
                ProviderExposureLabel(row.providerExposureState));
        }

        private string BuildThreadHeaderText()
        {
            if (threadDetail == null || threadDetail.status != MemoryLibraryStatuses.Ready)
                return string.Empty;
            MemoryCurrentStatusDto current = threadDetail.currentStatus;
            string status = string.Equals(current?.statusToken, "tracked",
                    StringComparison.Ordinal)
                ? T("PawnDiary.Memory.Library.CurrentKnown")
                : T("PawnDiary.Memory.Library.CurrentUnknown");
            string saved = MemoryLibraryUiPolicy.HasCapturedCurrentStatus(current)
                ? T("PawnDiary.Memory.Library.CurrentStatusSaved", status,
                    DateLabel(current.capturedTick), Join(current.frozenDisplayFields))
                : status;
            return T("PawnDiary.Memory.Library.ThreadDetailHeader", saved,
                threadDetail.shownManageableCount, threadDetail.totalManageableCount);
        }

        private void CacheDate(long tick)
        {
            if (tick < 0 || cachedDateLabels.ContainsKey(tick)) return;
            cachedDateLabels[tick] = FormatDateLabel(tick);
        }

        /// <summary>
        /// Uses the same full in-game calendar format on both cache hits and defensive cache misses.
        /// Long values are clamped before entering RimWorld's int-based date helpers.
        /// </summary>
        private static string FormatDateLabel(long tick)
        {
            int gameTick = (int)Math.Min(int.MaxValue, tick);
            return GenDate.DateFullStringAt(
                GenDate.TickGameToAbs(gameTick), Vector2.zero);
        }

        private static string RecordKey(MemoryRecordHandle handle)
        {
            return handle == null ? string.Empty : (handle.ownerPawnId ?? string.Empty) + "\n"
                + (handle.epochToken ?? string.Empty) + "\n" + (handle.recordId ?? string.Empty);
        }

        private static string EmptyFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string T(string key, params object[] values)
        {
            string frame = key.Translate().Resolve();
            if (values == null || values.Length == 0) return frame;
            try { return string.Format(frame, values); }
            catch (FormatException) { return frame; }
        }
    }
}
