// MemoryFrozenRecallSelectionCodec.cs — bounded save codec for an event-time Recall-v2 shortlist.
//
// A diary event can wait across save/load before transport starts. Persisting only the selected
// detached shortlist prevents reload from either substituting newly-ranked memory or silently
// deleting the event-time choice. Current wording/status/guards are still refreshed immediately
// before the prompt is frozen by ImportantMemorySelector.RevalidateFrozenV2.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Encodes/decodes the immutable half of one bounded Recall-v2 selection.</summary>
    internal static class MemoryFrozenRecallSelectionCodec
    {
        private const string Domain = "memory-frozen-recall-selection-v1";
        private const int MaximumSelectedRows = 8;
        private const int MaximumListRows = 32;

        public static string Encode(MemoryRecallSelectionResultV2 selection)
        {
            if (selection == null
                || string.IsNullOrWhiteSpace(selection.ownerPawnId)
                || string.IsNullOrWhiteSpace(selection.ownerEpochToken)
                || string.IsNullOrWhiteSpace(selection.consumerId)
                || selection.selected == null
                || selection.selected.Count > MaximumSelectedRows) return string.Empty;
            var fields = new List<string>
            {
                Domain,
                selection.ownerPawnId,
                selection.ownerEpochToken,
                selection.consumerId,
                Canonical(selection.selected.Count)
            };
            for (int index = 0; index < selection.selected.Count; index++)
            {
                MemoryRecallCandidateSnapshot row = selection.selected[index]?.candidate;
                if (row == null) return string.Empty;
                fields.Add(row.recordId ?? string.Empty);
                fields.Add(row.sourceOccurrenceId ?? string.Empty);
                fields.Add(row.sourceEventId ?? string.Empty);
                fields.Add(row.rootId ?? string.Empty);
                fields.Add(row.chapterOrNoveltyId ?? string.Empty);
                fields.Add(row.kind ?? string.Empty);
                fields.Add(row.importance ?? string.Empty);
                fields.Add(Canonical(row.originalEventTick));
                fields.Add(Bool(row.isThreadMember));
                fields.Add(Bool(row.isCurrentThreadProjection));
                fields.Add(Bool(row.directExactEventReference));
                fields.Add(Canonical(row.narrativeFitScore));
                if (!AddStrings(fields, row.categories)
                    || !AddStrings(fields, row.topicKeys)
                    || !AddStrings(fields, row.representedSourceOccurrenceIds)
                    || !AddRoutes(fields, row.exactRoutes)
                    || !AddGuards(fields, row.requiredStructuralGuards)) return string.Empty;
            }

            StringBuilder encoded = new StringBuilder();
            for (int index = 0; index < fields.Count; index++)
            {
                string field = fields[index];
                if (field == null
                    || field.Length > MemoryIdentityCodec.MaximumCompleteKeyCharacters
                    || !MemoryIdentityCodec.IsWellFormedUtf16(field)) return string.Empty;
                string segment = OrdinalSegmentCodec.Segment(field);
                if (encoded.Length > MemoryIdentityCodec.MaximumFrozenPromptCharacters
                    - segment.Length) return string.Empty;
                encoded.Append(segment);
            }
            return encoded.ToString();
        }

        public static MemoryRecallSelectionResultV2 Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)
                || encoded.Length > MemoryIdentityCodec.MaximumFrozenPromptCharacters) return null;
            int offset = 0;
            string domain;
            string owner;
            string epoch;
            string consumer;
            int count;
            if (!Read(encoded, ref offset, out domain) || domain != Domain
                || !Read(encoded, ref offset, out owner)
                || !Read(encoded, ref offset, out epoch)
                || !Read(encoded, ref offset, out consumer)
                || !ReadCount(encoded, ref offset, MaximumSelectedRows, out count)
                || string.IsNullOrWhiteSpace(owner)
                || string.IsNullOrWhiteSpace(consumer)
                || !MemoryIdentityCodec.TryValidateEpochToken(epoch, out var ignoredFallback))
                return null;

            var result = new MemoryRecallSelectionResultV2
            {
                ownerPawnId = owner,
                ownerEpochToken = epoch,
                consumerId = consumer
            };
            for (int index = 0; index < count; index++)
            {
                var candidate = new MemoryRecallCandidateSnapshot
                {
                    ownerPawnId = owner,
                    ownerEpochToken = epoch
                };
                string tick;
                string thread;
                string current;
                string direct;
                string fit;
                if (!Read(encoded, ref offset, out candidate.recordId)
                    || !Read(encoded, ref offset, out candidate.sourceOccurrenceId)
                    || !Read(encoded, ref offset, out candidate.sourceEventId)
                    || !Read(encoded, ref offset, out candidate.rootId)
                    || !Read(encoded, ref offset, out candidate.chapterOrNoveltyId)
                    || !Read(encoded, ref offset, out candidate.kind)
                    || !Read(encoded, ref offset, out candidate.importance)
                    || !Read(encoded, ref offset, out tick)
                    || !TryParseLong(tick, out candidate.originalEventTick)
                    || !Read(encoded, ref offset, out thread)
                    || !TryParseBool(thread, out candidate.isThreadMember)
                    || !Read(encoded, ref offset, out current)
                    || !TryParseBool(current, out candidate.isCurrentThreadProjection)
                    || !Read(encoded, ref offset, out direct)
                    || !TryParseBool(direct, out candidate.directExactEventReference)
                    || !Read(encoded, ref offset, out fit)
                    || !TryParseInt(fit, out candidate.narrativeFitScore)
                    || !ReadStrings(encoded, ref offset, candidate.categories)
                    || !ReadStrings(encoded, ref offset, candidate.topicKeys)
                    || !ReadStrings(
                        encoded, ref offset, candidate.representedSourceOccurrenceIds)
                    || !ReadRoutes(encoded, ref offset, candidate.exactRoutes)
                    || !ReadGuards(
                        encoded, ref offset, candidate.requiredStructuralGuards)
                    || string.IsNullOrWhiteSpace(candidate.recordId)
                    || string.IsNullOrWhiteSpace(candidate.sourceOccurrenceId)) return null;
                result.selected.Add(new MemoryRecallSelectedCandidate { candidate = candidate });
            }
            return offset == encoded.Length ? result : null;
        }

        private static bool AddStrings(List<string> fields, List<string> values)
        {
            int count = values?.Count ?? 0;
            if (count > MaximumListRows) return false;
            fields.Add(Canonical(count));
            for (int index = 0; index < count; index++)
                fields.Add(values[index] ?? string.Empty);
            return true;
        }

        private static bool AddRoutes(List<string> fields, List<MemoryRecallRouteIdentity> values)
        {
            int count = values?.Count ?? 0;
            if (count > MaximumListRows) return false;
            fields.Add(Canonical(count));
            for (int index = 0; index < count; index++)
            {
                MemoryRecallRouteIdentity row = values[index];
                if (row == null) return false;
                fields.Add(row.routeKind ?? string.Empty);
                fields.Add(row.subjectKind ?? string.Empty);
                fields.Add(row.subjectId ?? string.Empty);
                fields.Add(row.routeKey ?? string.Empty);
            }
            return true;
        }

        private static bool AddGuards(List<string> fields, List<MemoryGuardIdentity> values)
        {
            int count = values?.Count ?? 0;
            if (count > MaximumListRows) return false;
            fields.Add(Canonical(count));
            for (int index = 0; index < count; index++)
            {
                MemoryGuardIdentity row = values[index];
                if (row == null) return false;
                fields.Add(row.guardKind ?? string.Empty);
                fields.Add(row.guardKey ?? string.Empty);
            }
            return true;
        }

        private static bool ReadStrings(string encoded, ref int offset, List<string> values)
        {
            int count;
            if (!ReadCount(encoded, ref offset, MaximumListRows, out count)) return false;
            for (int index = 0; index < count; index++)
            {
                string value;
                if (!Read(encoded, ref offset, out value)) return false;
                values.Add(value);
            }
            return true;
        }

        private static bool ReadRoutes(
            string encoded, ref int offset, List<MemoryRecallRouteIdentity> values)
        {
            int count;
            if (!ReadCount(encoded, ref offset, MaximumListRows, out count)) return false;
            for (int index = 0; index < count; index++)
            {
                var row = new MemoryRecallRouteIdentity();
                if (!Read(encoded, ref offset, out row.routeKind)
                    || !Read(encoded, ref offset, out row.subjectKind)
                    || !Read(encoded, ref offset, out row.subjectId)
                    || !Read(encoded, ref offset, out row.routeKey)) return false;
                values.Add(row);
            }
            return true;
        }

        private static bool ReadGuards(
            string encoded, ref int offset, List<MemoryGuardIdentity> values)
        {
            int count;
            if (!ReadCount(encoded, ref offset, MaximumListRows, out count)) return false;
            for (int index = 0; index < count; index++)
            {
                var row = new MemoryGuardIdentity();
                if (!Read(encoded, ref offset, out row.guardKind)
                    || !Read(encoded, ref offset, out row.guardKey)) return false;
                values.Add(row);
            }
            return true;
        }

        private static bool ReadCount(
            string encoded, ref int offset, int maximum, out int count)
        {
            count = 0;
            string value;
            return Read(encoded, ref offset, out value)
                && TryParseInt(value, out count)
                && count >= 0
                && count <= maximum;
        }

        private static bool Read(string encoded, ref int offset, out string value)
        {
            return OrdinalSegmentCodec.TryReadCanonicalSegment(
                encoded,
                ref offset,
                MemoryIdentityCodec.MaximumCompleteKeyCharacters,
                true,
                out value);
        }

        private static string Bool(bool value) { return value ? "1" : "0"; }

        private static bool TryParseBool(string value, out bool parsed)
        {
            parsed = value == "1";
            return parsed || value == "0";
        }

        private static string Canonical(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseInt(string value, out int parsed)
        {
            parsed = 0;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                && string.Equals(Canonical(parsed), value, StringComparison.Ordinal);
        }

        private static bool TryParseLong(string value, out long parsed)
        {
            parsed = 0;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                && string.Equals(Canonical(parsed), value, StringComparison.Ordinal);
        }
    }
}
