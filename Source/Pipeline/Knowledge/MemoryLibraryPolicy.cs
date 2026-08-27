// MemoryLibraryPolicy.cs — pure M5 normalization, cursor, TTL, filtering, and command rules.
//
// This file owns every rule that can be decided without the game repository. Keeping Unicode and
// cursor behavior here prevents UI, cache, and component adapters from developing subtly different
// search identities or continuation arithmetic.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    internal sealed class MemoryLibraryLimits
    {
        public int libraryWindowRows = 64;
        public int libraryWindowCeiling = 256;
        public int chapterHeaderRows = 32;
        public int searchScalars = 80;
        public int searchUtf16Units = 160;
        public int normalizedFieldUtf16Units = 120;
        public int rowProjectionUtf16Units = 480;
        public int frozenDisplayLabelUtf16Units = 80;
        public int blockTextUtf16Units = 480;
        public int currentStatusFieldCount = 4;
        public int currentStatusFieldTextUtf16Units = 240;
        public int devReasonCount = 8;
        public int devReasonTextUtf16Units = 80;
        public int copyDiagnosticUtf16Units = 2000;
        public int importedPreviewUtf16Units = 240;
        public int importedSearchScratchUtf16Units = 49152;
        public int importedTextChunkUtf16Units = 1000;
        public int commandEntries = 32;
    }

    /// <summary>
    /// Stable defensive code ceilings mirrored by the canonical capacity registry. Production Def
    /// reads and standalone fixtures share these names so the two layers cannot silently drift.
    /// </summary>
    internal static class MemoryLibraryLimitCeilings
    {
        public const int LibraryWindowRows = 256;
        public const int ChapterHeaderRows = 128;
        public const int SearchScalars = 320;
        public const int SearchUtf16Units = 640;
        public const int NormalizedFieldUtf16Units = 480;
        public const int RowProjectionUtf16Units = 1920;
        public const int FrozenDisplayLabelUtf16Units = 320;
        public const int BlockTextUtf16Units = 1200;
        public const int CurrentStatusFieldCount = 16;
        public const int CurrentStatusFieldTextUtf16Units = 1200;
        public const int DevReasonCount = 32;
        public const int DevReasonTextUtf16Units = 320;
        public const int CopyDiagnosticUtf16Units = 8000;
        public const int ImportedPreviewUtf16Units = 1200;
        public const int ImportedSearchScratchUtf16Units = 262144;
        public const int ImportedTextChunkUtf16Units = 4000;
        public const int CommandEntries = 128;
        public const int OwnerEntries = 8192;
        public const int SliceWorkItems = 240;
        public const int SliceTargetMicroseconds = 1000;
    }

    /// <summary>Pure exact allocation counts for data, raw-status, then zero-owner publication.</summary>
    internal sealed class MemoryLibraryDirectoryCapPlan
    {
        public int includedData;
        public int includedRaw;
        public int includedZero;
        public long omittedRaw;
        public long omittedZero;
    }

    /// <summary>Stable pure directory tiers; zero means the owner is omitted.</summary>
    internal static class MemoryLibraryDirectoryTiers
    {
        public const int Omitted = 0;
        public const int Data = 1;
        public const int CompatibilityRaw = 2;
        public const int ActiveZero = 3;
    }

    internal sealed class MemoryLibraryCursorPlan
    {
        public bool valid;
        public int start;
        public int count;
        public int returnedCount;
        public int nextStart;
        public bool hasPrevious;
        public bool hasMore;
    }

    internal sealed class MemoryLibraryTextCursorPlan
    {
        public bool valid;
        public int start;
        public int count;
        public int end;
        public int previousStart;
        public bool hasPrevious;
        public bool hasMore;
    }

    internal sealed class MemoryLibraryMutationEligibility
    {
        public bool validAction;
        public bool eligible;
        public string reasonToken = string.Empty;
    }

    /// <summary>One checked positive sequence shared by every loaded-session Library publication.</summary>
    internal sealed class MemoryLibraryPublicationClock
    {
        private long lastIssuedRevision;

        public long LastIssuedRevision => lastIssuedRevision;

        public void Reset()
        {
            lastIssuedRevision = 0;
        }

        public bool TryAllocate(out long revision)
        {
            if (lastIssuedRevision == long.MaxValue)
            {
                revision = 0;
                return false;
            }
            revision = ++lastIssuedRevision;
            return true;
        }
    }

    internal static class MemoryLibraryPolicy
    {
        /// <summary>
        /// Applies the canonical directory priority. The capacity equation guarantees dataCount fits;
        /// a corrupt over-cap input still never misreports omitted data as legacy raw status.
        /// </summary>
        public static MemoryLibraryDirectoryCapPlan PlanDirectoryCap(
            int dataCount,
            int rawCount,
            int zeroCount,
            int capacity)
        {
            int cap = Math.Max(1, capacity);
            int data = Math.Max(0, Math.Min(dataCount, cap));
            int remaining = cap - data;
            int raw = Math.Max(0, Math.Min(rawCount, remaining));
            remaining -= raw;
            int zero = Math.Max(0, Math.Min(zeroCount, remaining));
            return new MemoryLibraryDirectoryCapPlan
            {
                includedData = data,
                includedRaw = raw,
                includedZero = zero,
                omittedRaw = Math.Max(0L, (long)rawCount - raw),
                omittedZero = Math.Max(0L, (long)zeroCount - zero)
            };
        }

        /// <summary>
        /// Classifies one directory header without live game state. Inactive current-schema owners
        /// retain saved culture as useful directory data even when they have no memory rows.
        /// </summary>
        public static int DirectoryTier(
            bool unknown,
            bool hasMemoryData,
            bool inactiveCurrentWithSavedCulture,
            bool hasCompatibilityRaw,
            bool active)
        {
            if (unknown || hasMemoryData || inactiveCurrentWithSavedCulture)
                return MemoryLibraryDirectoryTiers.Data;
            if (hasCompatibilityRaw) return MemoryLibraryDirectoryTiers.CompatibilityRaw;
            return active ? MemoryLibraryDirectoryTiers.ActiveZero
                : MemoryLibraryDirectoryTiers.Omitted;
        }

        /// <summary>Exact scalar fence for a private list job before it may publish.</summary>
        public static bool LibraryBuildFenceMatches(
            int expectedDiaryStateVersion,
            int currentDiaryStateVersion,
            long expectedDirectoryRevision,
            long currentDirectoryRevision,
            long expectedSettingsRevision,
            long currentSettingsRevision,
            long expectedLanguageRevision,
            long currentLanguageRevision,
            long expectedTtlDayRevision,
            long currentTtlDayRevision)
        {
            return expectedDiaryStateVersion == currentDiaryStateVersion
                && expectedDirectoryRevision == currentDirectoryRevision
                && expectedSettingsRevision == currentSettingsRevision
                && expectedLanguageRevision == currentLanguageRevision
                && expectedTtlDayRevision == currentTtlDayRevision;
        }

        /// <summary>Chooses the exact structural fence copied into an actionable block DTO.</summary>
        public static long TargetStructuralRevision(
            bool threaded,
            long ownerStructuralRevision,
            long rootStructuralRevision)
        {
            return threaded ? rootStructuralRevision : ownerStructuralRevision;
        }

        /// <summary>True when the three saved culture display inputs changed ordinally.</summary>
        public static bool CultureProjectionChanged(
            string oldOrigin,
            string oldSource,
            string oldAdopted,
            string newOrigin,
            string newSource,
            string newAdopted)
        {
            return !string.Equals(oldOrigin ?? string.Empty, newOrigin ?? string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(oldSource ?? string.Empty, newSource ?? string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(oldAdopted ?? string.Empty, newAdopted ?? string.Empty,
                    StringComparison.Ordinal);
        }

        public const string StreamFingerprintSchema = "memory-library-stream-fingerprint-v1";
        public const int ImportanceMinor = 1;
        public const int ImportanceRegular = 2;
        public const int ImportanceImportant = 4;

        /// <summary>Exact shared search normalization from §T15.4.1.</summary>
        public static string NormalizeSearch(string source, int scalarLimit, int utf16Limit)
        {
            if (scalarLimit <= 0 || utf16Limit <= 0 || string.IsNullOrEmpty(source))
                return string.Empty;
            string repaired = RepairMalformedUtf16(source);
            string normalized;
            try
            {
                normalized = repaired.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                normalized = repaired;
            }

            StringBuilder collapsed = new StringBuilder(Math.Min(normalized.Length, utf16Limit));
            bool pendingSpace = false;
            for (int index = 0; index < normalized.Length; index++)
            {
                char value = normalized[index];
                if (char.IsWhiteSpace(value))
                {
                    pendingSpace = collapsed.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    collapsed.Append(' ');
                    pendingSpace = false;
                }
                collapsed.Append(value);
            }
            return ClampScalars(
                collapsed.ToString().ToUpperInvariant(), scalarLimit, utf16Limit);
        }

        /// <summary>Repairs unpaired surrogates deterministically without altering valid pairs.</summary>
        public static string RepairMalformedUtf16(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            StringBuilder result = new StringBuilder(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                char value = source[index];
                if (char.IsHighSurrogate(value))
                {
                    if (index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]))
                    {
                        result.Append(value);
                        result.Append(source[++index]);
                    }
                    else result.Append('\uFFFD');
                }
                else if (char.IsLowSurrogate(value)) result.Append('\uFFFD');
                else result.Append(value);
            }
            return result.ToString();
        }

        /// <summary>Clamps a string to both scalar and UTF-16 ceilings at a complete scalar.</summary>
        public static string ClampScalars(string source, int scalarLimit, int utf16Limit)
        {
            if (string.IsNullOrEmpty(source) || scalarLimit <= 0 || utf16Limit <= 0)
                return string.Empty;
            int scalars = 0;
            int end = 0;
            while (end < source.Length && scalars < scalarLimit)
            {
                int width = char.IsHighSurrogate(source[end])
                    && end + 1 < source.Length
                    && char.IsLowSurrogate(source[end + 1]) ? 2 : 1;
                if (end + width > utf16Limit) break;
                end += width;
                scalars++;
            }
            return end == source.Length ? source : source.Substring(0, end);
        }

        public static string ClampUtf16CompleteScalar(string source, int utf16Limit)
        {
            string repaired = RepairMalformedUtf16(source ?? string.Empty);
            return ClampScalars(repaired, int.MaxValue, Math.Max(0, utf16Limit));
        }

        /// <summary>
        /// Appends one detached display field while enforcing both the row-count and combined-text
        /// capacity. The combined cap counts only stored field text; UI separators are not DTO data.
        /// </summary>
        public static bool TryAppendBoundedText(
            List<string> target,
            string value,
            int countLimit,
            int totalUtf16Limit)
        {
            if (target == null || target.Count >= Math.Max(0, countLimit)
                || totalUtf16Limit <= 0) return false;
            int used = 0;
            for (int index = 0; index < target.Count; index++)
                used += target[index]?.Length ?? 0;
            int remaining = totalUtf16Limit - used;
            if (remaining <= 0) return false;
            string bounded = ClampUtf16CompleteScalar(value, remaining);
            if (bounded.Length == 0) return false;
            target.Add(bounded);
            return true;
        }

        /// <summary>Allocates bounded normalized row fields in the canonical priority order.</summary>
        public static string BuildSearchProjection(
            IEnumerable<string> priorityFields,
            int perFieldUtf16Limit,
            int totalUtf16Limit)
        {
            if (priorityFields == null || totalUtf16Limit <= 0) return string.Empty;
            StringBuilder result = new StringBuilder(totalUtf16Limit);
            foreach (string field in priorityFields)
            {
                int separator = result.Length == 0 ? 0 : 1;
                int remaining = totalUtf16Limit - result.Length - separator;
                if (remaining <= 0) break;
                string normalized = NormalizeSearch(
                    field, int.MaxValue, Math.Min(perFieldUtf16Limit, remaining));
                if (normalized.Length == 0) continue;
                if (separator != 0) result.Append(' ');
                result.Append(ClampUtf16CompleteScalar(normalized, remaining));
            }
            return result.ToString();
        }

        public static bool SearchMatches(string normalizedProjection, string normalizedQuery)
        {
            return string.IsNullOrEmpty(normalizedQuery)
                || (!string.IsNullOrEmpty(normalizedProjection)
                    && normalizedProjection.IndexOf(
                        normalizedQuery, StringComparison.Ordinal) >= 0);
        }

        /// <summary>Canonical SHA-256 over length-prefixed stream fields, excluding cursors/revisions.</summary>
        public static string StreamFingerprint(params string[] fields)
        {
            StringBuilder encoded = new StringBuilder();
            AppendFingerprintField(encoded, StreamFingerprintSchema);
            if (fields != null)
            {
                for (int index = 0; index < fields.Length; index++)
                    AppendFingerprintField(encoded, fields[index] ?? string.Empty);
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(encoded.ToString());
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void AppendFingerprintField(StringBuilder target, string field)
        {
            string value = field ?? string.Empty;
            target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            target.Append(':');
            target.Append(value);
        }

        /// <summary>Shared owner/list/detail row cursor equations.</summary>
        public static MemoryLibraryCursorPlan NormalizeRowCursor(
            int start,
            int requestedCount,
            int countCap,
            long expectedRevision,
            int completeTotal,
            bool allowFreshPositiveStart = false)
        {
            MemoryLibraryCursorPlan result = new MemoryLibraryCursorPlan();
            if (start < 0 || requestedCount <= 0 || countCap <= 0 || completeTotal < 0
                || expectedRevision < 0 || (expectedRevision == 0 && start != 0
                    && !allowFreshPositiveStart)) return result;
            int count = Math.Min(requestedCount, countCap);
            try { checked { int ignored = start + count; } }
            catch (OverflowException) { return result; }
            if (start > completeTotal) return result;
            int returned = Math.Min(count, completeTotal - start);
            result.valid = true;
            result.start = start;
            result.count = count;
            result.returnedCount = returned;
            result.nextStart = checked(start + returned);
            result.hasPrevious = start > 0 || (start == completeTotal && completeTotal > 0);
            result.hasMore = result.nextStart < completeTotal;
            return result;
        }

        /// <summary>Normalizes text page size so a requested one-unit page can carry a scalar pair.</summary>
        public static int NormalizeTextCount(int requestedCount, int countCap)
        {
            if (requestedCount <= 0 || countCap < 2) return 0;
            return requestedCount == 1 ? 2 : Math.Min(requestedCount, countCap);
        }

        /// <summary>Shared scalar-safe Imported text cursor.</summary>
        public static MemoryLibraryTextCursorPlan NormalizeTextCursor(
            string completeText,
            int start,
            int requestedCount,
            int countCap,
            long expectedRevision)
        {
            string text = completeText ?? string.Empty;
            MemoryLibraryTextCursorPlan result = new MemoryLibraryTextCursorPlan();
            if (start < 0 || start > text.Length || requestedCount <= 0 || countCap < 2
                || expectedRevision < 0 || (expectedRevision == 0 && start != 0)
                || SplitsScalar(text, start)) return result;
            int count = NormalizeTextCount(requestedCount, countCap);
            int end;
            try { end = Math.Min(text.Length, checked(start + count)); }
            catch (OverflowException) { return result; }
            if (SplitsScalar(text, end)) end--;
            result.valid = true;
            result.start = start;
            result.count = end - start;
            result.end = end;
            result.previousStart = Math.Max(0, start - Math.Max(2, count));
            if (SplitsScalar(text, result.previousStart)) result.previousStart--;
            result.hasPrevious = start > 0;
            result.hasMore = end < text.Length;
            return result;
        }

        private static bool SplitsScalar(string text, int boundary)
        {
            return boundary > 0 && boundary < text.Length
                && char.IsHighSurrogate(text[boundary - 1])
                && char.IsLowSurrogate(text[boundary]);
        }

        public static bool ValidOwnerHandle(MemoryLibraryOwnerHandle handle)
        {
            if (handle == null) return false;
            string scope = handle.scopeToken ?? string.Empty;
            bool knownScope = scope == MemoryLibraryScopes.Active
                || scope == MemoryLibraryScopes.ArchiveOnly
                || scope == MemoryLibraryScopes.UnresolvedImported
                || scope == MemoryLibraryScopes.LegacyRawExact
                || scope == MemoryLibraryScopes.LegacyRawUnknown
                || scope == MemoryLibraryScopes.InertCurrentExact;
            if (!knownScope) return false;
            bool unknown = scope == MemoryLibraryScopes.UnresolvedImported
                || scope == MemoryLibraryScopes.LegacyRawUnknown;
            return unknown
                ? string.IsNullOrEmpty(handle.exactOwnerPawnIdOrEmpty)
                    && string.IsNullOrEmpty(handle.epochTokenOrEmpty)
                : !string.IsNullOrWhiteSpace(handle.exactOwnerPawnIdOrEmpty);
        }

        public static bool HandlesMatch(
            MemoryLibraryOwnerHandle owner,
            MemoryOwnerEpochKey key)
        {
            return owner != null && key != null
                && owner.scopeToken == MemoryLibraryScopes.Active
                && string.Equals(owner.exactOwnerPawnIdOrEmpty,
                    key.ownerPawnId, StringComparison.Ordinal)
                && string.Equals(owner.epochTokenOrEmpty,
                    key.epochToken, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(key.ownerPawnId)
                && !string.IsNullOrWhiteSpace(key.epochToken);
        }

        /// <summary>Validates the three exact current Imported handle shapes.</summary>
        public static bool ValidArchiveHandle(MemoryArchiveHandle handle)
        {
            if (handle == null || string.IsNullOrWhiteSpace(handle.archiveRecordId)) return false;
            if (handle.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported)
                return string.IsNullOrEmpty(handle.exactOwnerPawnIdOrEmpty);
            return (handle.archiveScopeToken == MemoryLibraryScopes.Active
                    || handle.archiveScopeToken == MemoryLibraryScopes.ArchiveOnly)
                && !string.IsNullOrWhiteSpace(handle.exactOwnerPawnIdOrEmpty);
        }

        /// <summary>Plans one per-source compatibility revision without wrapping Max.</summary>
        public static bool TryNextCompatibilityRevision(
            long existingRevision,
            bool byteEquivalent,
            out long revision)
        {
            if (existingRevision <= 0)
            {
                revision = 1;
                return true;
            }
            if (byteEquivalent)
            {
                revision = existingRevision;
                return true;
            }
            if (existingRevision == long.MaxValue)
            {
                revision = 0;
                return false;
            }
            revision = existingRevision + 1;
            return true;
        }

        public static int ImportanceMask(string importance)
        {
            if (string.Equals(importance, "Minor", StringComparison.OrdinalIgnoreCase))
                return ImportanceMinor;
            if (string.Equals(importance, "Regular", StringComparison.OrdinalIgnoreCase))
                return ImportanceRegular;
            if (string.Equals(importance, "Important", StringComparison.OrdinalIgnoreCase))
                return ImportanceImportant;
            return 0;
        }

        public static bool MatchesFilters(MemoryBlockRow row, MemoryLibraryFilters filters)
        {
            if (row == null) return false;
            MemoryLibraryFilters value = filters ?? new MemoryLibraryFilters();
            int importanceMask = row.projectedImportanceMask != 0
                ? row.projectedImportanceMask : row.projectedHighestImportanceMask;
            if (value.importanceMask != 0
                && (importanceMask & value.importanceMask) == 0) return false;
            if (value.categoryMask != 0
                && (row.projectedCategoryMask & value.categoryMask) == 0) return false;
            switch (value.stateToken ?? "all")
            {
                case "all": return true;
                case "edited": return row.playerEdited;
                case "suppressed": return row.suppressed;
                case "unsuppressed": return !row.suppressed;
                default: return false;
            }
        }

        /// <summary>
        /// Returns the exact row projection selected by filter/search. Summary contributions are
        /// intersected rather than treating the container's highest importance as every contribution.
        /// </summary>
        public static bool TryProjectRow(
            MemoryBlockRow row,
            MemoryLibraryFilters filters,
            string normalizedSearch,
            bool wholeOrHeaderSearchHit,
            MemoryLibraryLimits limits,
            out MemoryBlockRow projected)
        {
            projected = null;
            if (row == null || !MatchesState(row, filters)) return false;
            if (row.summaryContributions == null || row.summaryContributions.Count == 0)
            {
                if (!MatchesFilters(row, filters)) return false;
                string legacySearch = string.IsNullOrEmpty(row.normalizedWholeSearch)
                    ? row.normalizedSearch : row.normalizedWholeSearch;
                if (!wholeOrHeaderSearchHit && !SearchMatches(legacySearch, normalizedSearch))
                    return false;
                projected = row;
                return true;
            }

            MemoryLibraryFilters value = filters ?? new MemoryLibraryFilters();
            bool contributionFiltersActive = value.importanceMask != 0
                || value.categoryMask != 0;
            bool wholeSearch = wholeOrHeaderSearchHit || string.IsNullOrEmpty(normalizedSearch)
                || SearchMatches(row.normalizedWholeSearch, normalizedSearch);
            if (!contributionFiltersActive && wholeSearch)
            {
                projected = row;
                return true;
            }
            List<MemorySummaryContributionDescriptor> eligible =
                new List<MemorySummaryContributionDescriptor>();
            for (int index = 0; index < row.summaryContributions.Count; index++)
            {
                MemorySummaryContributionDescriptor contribution = row.summaryContributions[index];
                if (contribution == null) continue;
                if (value.importanceMask != 0
                    && (contribution.importanceMask & value.importanceMask) == 0) continue;
                if (value.categoryMask != 0
                    && (contribution.categoryMask & value.categoryMask) == 0) continue;
                eligible.Add(contribution);
            }
            if (eligible.Count == 0) return false;

            bool wholeHit = wholeSearch;
            List<MemorySummaryContributionDescriptor> selected = eligible;
            if (!wholeHit)
            {
                selected = new List<MemorySummaryContributionDescriptor>();
                for (int index = 0; index < eligible.Count; index++)
                    if (ContributionSearchMatches(eligible[index], normalizedSearch, limits))
                        selected.Add(eligible[index]);
                if (selected.Count == 0) return false;
            }
            projected = ProjectSummary(row, selected, limits);
            return true;
        }

        private static bool MatchesState(MemoryBlockRow row, MemoryLibraryFilters filters)
        {
            string state = filters?.stateToken ?? "all";
            switch (state)
            {
                case "all": return true;
                case "edited": return row.playerEdited;
                case "suppressed": return row.suppressed;
                case "unsuppressed": return !row.suppressed;
                default: return false;
            }
        }

        private static bool ContributionSearchMatches(
            MemorySummaryContributionDescriptor contribution,
            string normalizedSearch,
            MemoryLibraryLimits limits)
        {
            if (string.IsNullOrEmpty(normalizedSearch)) return true;
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            for (int index = 0; contribution?.searchFields != null
                && index < contribution.searchFields.Count; index++)
            {
                string scratch = NormalizeSearch(
                    contribution.searchFields[index], int.MaxValue,
                    cap.normalizedFieldUtf16Units);
                if (SearchMatches(scratch, normalizedSearch)) return true;
            }
            return false;
        }

        private static MemoryBlockRow ProjectSummary(
            MemoryBlockRow source,
            List<MemorySummaryContributionDescriptor> selected,
            MemoryLibraryLimits limits)
        {
            MemoryBlockRow result = CopyRow(source);
            result.summaryContributions = new List<MemorySummaryContributionDescriptor>(selected);
            result.projectedCategoryMask = 0;
            result.projectedImportanceMask = 0;
            result.projectedHighestImportanceMask = 0;
            result.originalTick = long.MaxValue;
            result.activityTick = 0;
            result.projectedNextExpiryTick = long.MaxValue;
            List<string> preview = new List<string>();
            for (int index = 0; index < selected.Count; index++)
            {
                MemorySummaryContributionDescriptor contribution = selected[index];
                result.projectedCategoryMask |= contribution.categoryMask;
                result.projectedImportanceMask |= contribution.importanceMask;
                result.projectedHighestImportanceMask = HigherImportance(
                    result.projectedHighestImportanceMask, contribution.importanceMask);
                result.originalTick = Math.Min(result.originalTick, contribution.originalTick);
                result.activityTick = Math.Max(result.activityTick, contribution.originalTick);
                result.projectedNextExpiryTick = Math.Min(
                    result.projectedNextExpiryTick, contribution.nextExpiryTick);
                if (!string.IsNullOrWhiteSpace(contribution.browsePreview))
                    preview.Add(contribution.browsePreview);
            }
            if (result.originalTick == long.MaxValue) result.originalTick = 0;
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            result.displayWording = ClampUtf16CompleteScalar(
                string.Join("; ", preview.ToArray()), cap.blockTextUtf16Units);
            result.normalizedSearch = BuildSearchProjection(
                preview, cap.normalizedFieldUtf16Units, cap.rowProjectionUtf16Units);
            return result;
        }

        private static int HigherImportance(int left, int right)
        {
            if ((left & ImportanceImportant) != 0 || (right & ImportanceImportant) != 0)
                return ImportanceImportant;
            if ((left & ImportanceRegular) != 0 || (right & ImportanceRegular) != 0)
                return ImportanceRegular;
            return (left & ImportanceMinor) != 0 || (right & ImportanceMinor) != 0
                ? ImportanceMinor : 0;
        }

        private static MemoryBlockRow CopyRow(MemoryBlockRow source)
        {
            return new MemoryBlockRow
            {
                recordHandle = source.recordHandle,
                rootHandle = source.rootHandle,
                chapterId = source.chapterId,
                targetStructuralRevision = source.targetStructuralRevision,
                kind = source.kind,
                summaryRole = source.summaryRole,
                projectedCategoryMask = source.projectedCategoryMask,
                projectedImportanceMask = source.projectedImportanceMask,
                projectedHighestImportanceMask = source.projectedHighestImportanceMask,
                originalTick = source.originalTick,
                activityTick = source.activityTick,
                projectedNextExpiryTick = source.projectedNextExpiryTick,
                displayWording = source.displayWording,
                primarySubjectLabel = source.primarySubjectLabel,
                playerEdited = source.playerEdited,
                suppressed = source.suppressed,
                canSuppress = source.canSuppress,
                canSaveWording = source.canSaveWording,
                canUseOriginal = source.canUseOriginal,
                canDevForget = source.canDevForget,
                lastAutomaticIncludedTick = source.lastAutomaticIncludedTick,
                automaticInclusionCount = source.automaticInclusionCount,
                providerExposureState = source.providerExposureState,
                normalizedSearch = source.normalizedSearch,
                normalizedWholeSearch = source.normalizedWholeSearch,
                rollingSummary = source.rollingSummary,
                closedSummary = source.closedSummary,
                ageUnknown = source.ageUnknown
            };
        }

        /// <summary>Future expiry transition; already-due/unknown/edited/Important yields Max.</summary>
        public static long FutureExpiryTick(
            long originalTick,
            bool ageUnknown,
            bool playerEdited,
            int importanceMask,
            long minorLifetimeTicks,
            long regularLifetimeTicks,
            long nowTick)
        {
            if (ageUnknown || playerEdited || originalTick < 0
                || importanceMask == 0
                || (importanceMask & ImportanceImportant) != 0) return long.MaxValue;
            long lifetime = (importanceMask & ImportanceRegular) != 0
                ? regularLifetimeTicks : minorLifetimeTicks;
            if (lifetime <= 0 || originalTick > long.MaxValue - lifetime) return long.MaxValue;
            long expiry = originalTick + lifetime;
            return expiry > nowTick ? expiry : long.MaxValue;
        }

        /// <summary>
        /// Applies the reducer's exact TTL boundary while a detached Library snapshot is built.
        /// Maintenance is deliberately sliced, so projection must not republish a row that is
        /// already due merely because the saved reducer has not reached that owner yet.
        /// </summary>
        public static bool RetainedAtSnapshot(
            long nowTick,
            long originalTick,
            bool ageUnknown,
            bool playerEdited,
            int importanceMask,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            if (playerEdited || ageUnknown
                || (importanceMask & ImportanceImportant) != 0) return true;
            long lifetime;
            if ((importanceMask & ImportanceMinor) != 0)
                lifetime = Math.Max(0, minorLifetimeTicks);
            else if ((importanceMask & ImportanceRegular) != 0)
                lifetime = Math.Max(0, regularLifetimeTicks);
            else
                return true;
            return MemoryChapterPolicy.Elapsed(
                    Math.Max(0, nowTick), Math.Max(0, originalTick)) < lifetime;
        }

        /// <summary>
        /// Rebuilds a Summary row from a snapshot-safe contribution subset. This is used when TTL
        /// removes only some contributions so expired wording/search/count metadata cannot survive.
        /// </summary>
        public static MemoryBlockRow ProjectSummaryForSnapshot(
            MemoryBlockRow source,
            List<MemorySummaryContributionDescriptor> retained,
            MemoryLibraryLimits limits)
        {
            if (source == null || retained == null || retained.Count == 0) return null;
            MemoryBlockRow result = ProjectSummary(source, retained, limits);
            // ProjectSummary normally preserves the container-wide search text so a filtered row
            // can still match its original whole Summary. A TTL projection is different: removed
            // contributions are no longer browseable truth, so their wording must leave BOTH
            // search fields. Rebuild the safe whole-row index from retained wording plus the stable
            // subject label instead of copying the saved container's pre-expiry index.
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            result.normalizedWholeSearch = BuildSearchProjection(
                new List<string> { result.displayWording, result.primarySubjectLabel },
                cap.normalizedFieldUtf16Units,
                cap.rowProjectionUtf16Units);
            // The saved Summary still contains the expired contributions until maintenance reaches
            // it. Editing now would set playerEdited on that full payload and protect/reveal those
            // hidden facts again, so wording edits fail closed for this transient partial shape.
            result.canSaveWording = false;
            result.canUseOriginal = false;
            return result;
        }

        public static long TtlValidUntil(long nextDayBoundary, long earliestFutureExpiry)
        {
            long day = nextDayBoundary > 0 ? nextDayBoundary : long.MaxValue;
            long expiry = earliestFutureExpiry > 0
                ? earliestFutureExpiry : long.MaxValue;
            return Math.Min(day, expiry);
        }

        /// <summary>
        /// A detached owner snapshot must be rebuilt at its first exact finite expiry. Day changes
        /// alone are insufficient because a memory can cross its retention boundary mid-day.
        /// </summary>
        public static bool OwnerSnapshotReachedExpiry(
            long nowTick,
            long earliestFiniteExpiryTickExclusive)
        {
            return nowTick >= 0
                && earliestFiniteExpiryTickExclusive > 0
                && earliestFiniteExpiryTickExclusive != long.MaxValue
                && nowTick >= earliestFiniteExpiryTickExclusive;
        }

        /// <summary>Pure action matrix; repository identity/revision checks happen before this.</summary>
        public static MemoryLibraryMutationEligibility CheckEligibility(
            string action,
            MemoryBlockRow row,
            bool imported,
            bool hasDesiredSuppressed,
            string wordingDraft,
            int blockTextCap)
        {
            MemoryLibraryMutationEligibility result = new MemoryLibraryMutationEligibility();
            bool known = action == MemoryLibraryActions.SetSuppressed
                || action == MemoryLibraryActions.SaveWording
                || action == MemoryLibraryActions.UseOriginalWording
                || action == MemoryLibraryActions.ForgetPermanent;
            result.validAction = known;
            if (!known) { result.reasonToken = "unknown_action"; return result; }
            if (action == MemoryLibraryActions.ForgetPermanent)
            {
                result.eligible = imported || row != null;
                return result;
            }
            if (imported || row == null) { result.reasonToken = "read_only"; return result; }
            if (action == MemoryLibraryActions.SetSuppressed)
            {
                result.eligible = hasDesiredSuppressed && row.canSuppress;
                result.reasonToken = result.eligible ? string.Empty : "suppression_ineligible";
                return result;
            }
            if (action == MemoryLibraryActions.SaveWording)
            {
                string bounded = ClampUtf16CompleteScalar(wordingDraft ?? string.Empty, blockTextCap);
                result.validAction = !string.IsNullOrWhiteSpace(wordingDraft)
                    && string.Equals(bounded, wordingDraft ?? string.Empty, StringComparison.Ordinal);
                result.eligible = result.validAction && row.canSaveWording;
                result.reasonToken = result.validAction
                    ? result.eligible ? string.Empty : "edit_ineligible"
                    : "invalid_wording";
                return result;
            }
            result.eligible = row.canUseOriginal && row.playerEdited && !row.rollingSummary;
            result.reasonToken = result.eligible ? string.Empty : "original_ineligible";
            return result;
        }

        /// <summary>
        /// Shared terminal precedence for a resolved command: malformed, Missing, Stale, authorization,
        /// then exact action eligibility. Capacity/saturation remain repository mutation-plan outcomes.
        /// </summary>
        public static string PlanCommandStatus(
            MemoryLibraryCommand command,
            MemoryLibraryCommandTargetState target,
            int blockTextCap)
        {
            if (command == null || target == null || !target.commandShapeValid)
                return MemoryLibraryCommandStatuses.Invalid;
            if (!target.exists) return MemoryLibraryCommandStatuses.Missing;
            if (command.targetStructuralRevision != target.currentStructuralRevision)
                return MemoryLibraryCommandStatuses.Stale;
            if (command.actionToken == MemoryLibraryActions.ForgetPermanent
                && !target.devAuthorized)
                return MemoryLibraryCommandStatuses.Unauthorized;
            MemoryLibraryMutationEligibility eligibility = CheckEligibility(
                command.actionToken,
                target.activeRow,
                target.imported,
                command.hasDesiredSuppressed,
                command.wordingDraft,
                blockTextCap);
            if (!eligibility.validAction) return MemoryLibraryCommandStatuses.Invalid;
            return eligibility.eligible
                ? MemoryLibraryCommandStatuses.Success
                : MemoryLibraryCommandStatuses.Ineligible;
        }

        public static string CultureStateToken(string savedDefName, bool resolved)
        {
            return string.IsNullOrWhiteSpace(savedDefName)
                ? "none" : resolved ? "resolved" : "unavailable";
        }

        public static string CultureProvenanceToken(string savedSource)
        {
            if (string.IsNullOrWhiteSpace(savedSource)) return "none";
            if (string.Equals(savedSource, "captured", StringComparison.Ordinal)) return "captured";
            if (string.Equals(savedSource, "inferred", StringComparison.Ordinal)) return "inferred";
            return "unknown";
        }
    }
}
