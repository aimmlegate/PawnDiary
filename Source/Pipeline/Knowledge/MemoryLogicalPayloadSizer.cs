// MemoryLogicalPayloadSizer.cs — the one canonical logical-byte encoding for every saved memory row
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T17.5 sizing rules, schema token
// "memory-logical-payload-v1").
//
// Exact charges per current-schema field, in one checked 64-bit accumulator:
//   - one byte per nullable-row presence;
//   - four bytes per list count or UTF-8 byte-length prefix;
//   - exact UTF-8 bytes for every well-formed string;
//   - fixed 1/4/8-byte widths for Boolean/32-bit/64-bit scalars;
//   - the same rule recursively for nested bounded rows;
//   - a fixed 64-byte per-row schema/framing allowance.
// Unpaired surrogates, negative counts, unknown rows/fields, out-of-order fields, or arithmetic
// overflow return an INVALID result; byte totals never wrap. Both ActiveMemoryPayloadBudget and
// ImportedPayloadBudget delegate to this sizer — neither keeps a parallel byte formula.
//
// Pure plain C#: no Verse/Unity. Saved row classes implement IMemoryLogicalSizeSource by pushing
// their fields in declaration order; the collector validates every push against the
// MemorySavedScalarSchema registry, so a field added without a registry entry fails at runtime too.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Implemented by saved memory rows that can report their logical size.</summary>
    internal interface IMemoryLogicalSizeSource
    {
        /// <summary>Pushes this row's fields to the collector in exact declaration order.</summary>
        void CollectFields(MemoryLogicalSizeCollector collector);
    }

    /// <summary>The outcome of one logical sizing walk.</summary>
    internal struct MemoryLogicalSizeResult
    {
        public bool valid;
        /// <summary>Total logical bytes when valid; unchecked/partial otherwise.</summary>
        public long totalBytes;
        /// <summary>Dotted field path of the first failure, for diagnostics only.</summary>
        public string errorPath;

        public static MemoryLogicalSizeResult Invalid(string errorPath)
        {
            return new MemoryLogicalSizeResult { valid = false, totalBytes = -1, errorPath = errorPath };
        }
    }

    /// <summary>
    /// Validates pushes against the frozen scalar-schema registry while accumulating checked bytes.
    /// One collector instance performs exactly one complete top-level walk.
    /// </summary>
    internal sealed class MemoryLogicalSizeCollector
    {
        private const long RowFramingAllowanceBytes = 64;
        private const long LengthPrefixBytes = 4;

        private sealed class Frame
        {
            public string rowName;
            public MemorySavedRowFields fields;
            public int nextAtom;
            /// <summary>Composite field awaiting nested child row(s); children validate by name.</summary>
            public string pendingChildField;
        }

        /// <summary>Registered element-row type per composite field. A child pushed under the
        /// wrong field is a shape violation, exactly like a wrong scalar (§T6.0).</summary>
        private static readonly Dictionary<string, string> CompositeFieldRows =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "stateFacts", "SavedMemoryStateFact" },
                { "baselineFacts", "SavedMemoryStateFact" },
                { "currentFacts", "SavedMemoryStateFact" },
                { "contributions", "SavedMemoryFactContribution" },
                { "factBuckets", "SavedMemoryFactBucket" },
                { "subjectRefs", "SavedMemorySubjectRef" },
                { "provenanceRefs", "SavedMemoryProvenance" },
                { "secondarySubjects", "SavedMemorySubjectRef" },
                { "facts", "SavedMemoryCanonicalFact" },
                { "provenance", "SavedMemoryProvenance" },
                { "primarySubject", "SavedMemorySubjectRef" },
                { "rollingSummaryBlock", "SavedMemoryBlock" },
                { "chapters", "SavedMemoryChapter" },
                { "visibleBlocks", "SavedMemoryBlock" },
                { "summaryPayload", "SavedMemorySummaryPayload" },
                { "standaloneBlocks", "SavedMemoryBlock" },
                { "threadRoots", "SavedMemoryThreadRoot" },
                { "ownerAwarenessSnapshots", "SavedMemoryAwarenessSnapshot" },
                { "openCaptureEpisodes", "SavedMemoryCaptureEpisode" },
                { "repetitionGuardRows", "SavedMemoryRepetitionGuardRow" },
                { "importedArchiveRows", "SavedImportedMemoryRow" },
                { "globalFactionSnapshots", "SavedGlobalFactionSnapshot" },
                { "legacyOwnerEpochReservations", "SavedLegacyOwnerEpochReservation" },
                { "unresolvedOwnerArchiveRows", "SavedImportedMemoryRow" },
                { "rawUnresolvedOwnerArchiveInput", "SavedLegacyUnresolvedOwnerArchiveInputV1" },
                { "summaryContributionEvidence", "SavedImportedSummaryContributionEvidenceV1" },
                { "summaryWordingOpportunities", "SavedSummaryWordingOpportunityV1" },
                { "memoryDiagnosticCounters", "SavedMemoryDiagnosticCounter" },
                { "memoryAttemptAuditRows", "SavedMemoryAttemptAuditRow" },
                { "activeMemoryCoordinatorRequests", "SavedActiveLogicalRequestV1" },
                { "frozenVariants", "SavedFrozenPromptVariantV1" },
                { "activeAttempts", "SavedActiveLogicalAttemptV1" },
                { "reservedEvidenceEntries", "SavedFrozenEvidenceEntryV1" },
                { "reservedGuardEntries", "SavedFrozenGuardEntryV1" },
                { "diagnosticProvenance", "SavedFrozenDiagnosticProvenanceV1" },
                { "receiptPlan", "SavedFrozenEvidenceReceiptPlanV1" },
                { "evidenceEntries", "SavedFrozenEvidenceEntryV1" },
                { "guardEntries", "SavedFrozenGuardEntryV1" }
            };

        private readonly Stack<Frame> stack = new Stack<Frame>();
        private readonly Stack<string> pathStack = new Stack<string>();
        private long total;

        /// <summary>Checked running total; meaningful only after a complete valid walk.</summary>
        public long Total()
        {
            return total;
        }

        public string CurrentPath()
        {
            return pathStack.Count == 0 ? string.Empty : pathStack.Peek();
        }

        /// <summary>Begins one row: validates registration and adds the framing allowance.</summary>
        public void BeginRow(string rowName)
        {
            MemorySavedRowFields fields = MemorySavedScalarSchema.Row(rowName);
            string parentPath = CurrentPath();
            string path = parentPath.Length == 0 ? rowName : parentPath + "." + rowName;
            if (fields == null)
            {
                throw MemoryLogicalSizeException(path + ": row is not registered");
            }

            if (stack.Count > 0)
            {
                Frame parent = stack.Peek();
                if (parent.pendingChildField == null)
                {
                    throw MemoryLogicalSizeException(
                        path + ": nested row outside a composite field");
                }

                if (!CompositeFieldRows.TryGetValue(
                        parent.pendingChildField, out string expectedRow)
                    || !string.Equals(expectedRow, rowName, StringComparison.Ordinal))
                {
                    throw MemoryLogicalSizeException(
                        path + ": expected '" + expectedRow + "' for field '"
                        + parent.pendingChildField + "'");
                }

                // Nullable singletons bind exactly one child; lists keep binding until EndRow.
                int lastAtom = Math.Max(0, parent.nextAtom - 1);
                if (parent.fields.atoms[lastAtom].atomKind == MemorySavedAtomKind.NullableRow)
                {
                    parent.pendingChildField = null;
                }
            }

            if (!AddChecked(RowFramingAllowanceBytes, path))
            {
                throw MemoryLogicalSizeException(path + ": overflow");
            }

            Frame frame = new Frame { rowName = rowName, fields = fields, nextAtom = 0 };
            stack.Push(frame);
            pathStack.Push(path);
        }

        /// <summary>Ends the current row: every registered field sized, no dangling child binding,
        /// and (at top level) exactly one fully closed root row.</summary>
        public void EndRow()
        {
            if (stack.Count == 0
                || stack.Peek().nextAtom != stack.Peek().fields.atoms.Length
                || stack.Peek().pendingChildField != null)
            {
                throw MemoryLogicalSizeException(
                    CurrentPath() + ": not every registered field/child was sized");
            }

            stack.Pop();
            pathStack.Pop();
        }

        /// <summary>True when no row is currently open — a completed top-level walk.</summary>
        public bool IsComplete()
        {
            return stack.Count == 0;
        }

        public void Boolean(string fieldName, bool ignoredValue)
        {
            Next(MemorySavedAtomKind.Bool, fieldName);
            AddOrThrow(1);
        }

        public void Int32(string fieldName, int ignoredValue)
        {
            Next(MemorySavedAtomKind.Int32, fieldName);
            AddOrThrow(4);
        }

        public void Int64(string fieldName, long ignoredValue)
        {
            Next(MemorySavedAtomKind.Int64, fieldName);
            AddOrThrow(8);
        }

        /// <summary>Charges the 4-byte UTF-8 length prefix plus exact well-formed UTF-8 bytes.</summary>
        public void String(string fieldName, string value)
        {
            Next(MemorySavedAtomKind.String, fieldName);
            AddOrThrow(StringCharge(fieldName, value));
        }

        /// <summary>One element of a LookMode.Value string list (same charging rule).</summary>
        public void ValueListStringElement(string value)
        {
            AddOrThrow(StringCharge(CurrentPath(), value));
        }

        /// <summary>Charges the 4-byte list-count prefix of a list field and binds subsequent
        /// nested child rows to this field's registered element type.</summary>
        public void ListCount(string fieldName, int count)
        {
            Next(MemorySavedAtomKind.List, fieldName);
            if (count < 0)
            {
                throw MemoryLogicalSizeException(PathFor(fieldName) + ": negative count");
            }

            AddOrThrow(LengthPrefixBytes);
            if (stack.Count > 0)
            {
                stack.Peek().pendingChildField = fieldName;
            }
        }

        /// <summary>Charges one nullable-presence byte and binds the single following child row
        /// (when present) to this field's registered type.</summary>
        public void NullablePresence(string fieldName, bool present)
        {
            Next(MemorySavedAtomKind.NullableRow, fieldName);
            AddOrThrow(1);
            if (present && stack.Count > 0)
            {
                stack.Peek().pendingChildField = fieldName;
            }
        }

        /// <summary>
        /// Escape hatch for RAW pre-schema legacy leaves only (§T6.8 raw wrappers): charges the
        /// caller-computed EXACT legacy logical bytes plus the shared 4-byte length prefix. The
        /// wrapper derives bytes with the complete frozen §T6.8 walker (framing, scalars, string
        /// prefixes, exact UTF-8). Never valid for current-schema rows.
        /// </summary>
        public void UnregisteredRawBytes(long rawBytes)
        {
            if (rawBytes < 0)
            {
                throw new MemoryLogicalSizeValidationException(
                    CurrentPath() + ": negative raw byte count");
            }

            long bytes = LengthPrefixBytes + rawBytes;
            string path = CurrentPath();
            if (bytes < 0 || !AddChecked(bytes, path))
            {
                throw new MemoryLogicalSizeValidationException(path + ": overflow");
            }
        }

        /// <summary>Unchecked fixed-byte charge used only by the standalone singleton helper;
        /// never valid inside a registered row walk.</summary>
        public void UncheckedBytes(long bytes, string errorPath)
        {
            if (!AddChecked(bytes, errorPath))
            {
                throw new MemoryLogicalSizeValidationException(errorPath + ": overflow");
            }
        }

        /// <summary>Clears a pending composite-child binding. Required after the RAW-bytes
        /// escape (no registered child follows); registered NestedRow paths consume it instead.</summary>
        public void ClearPendingChild()
        {
            if (stack.Count > 0)
            {
                stack.Peek().pendingChildField = null;
            }
        }

        /// <summary>Sizes one nested row (deep list element or singleton payload).</summary>
        public void NestedRow(IMemoryLogicalSizeSource child)
        {
            child.CollectFields(this);
        }

        private void Next(MemorySavedAtomKind expected, string fieldName)
        {
            if (stack.Count == 0)
            {
                throw MemoryLogicalSizeException(fieldName + ": no open row");
            }

            Frame frame = stack.Peek();
            // A scalar/list-count push after nested children ends that composite's binding.
            frame.pendingChildField = null;
            if (frame.nextAtom >= frame.fields.atoms.Length)
            {
                throw MemoryLogicalSizeException(PathFor(fieldName) + ": extra field");
            }

            MemorySavedFieldAtom atom = frame.fields.atoms[frame.nextAtom];
            if (!string.Equals(atom.fieldNameToken, fieldName, StringComparison.Ordinal))
            {
                throw MemoryLogicalSizeException(
                    PathFor(fieldName) + ": expected '" + atom.fieldNameToken + "'");
            }

            if (atom.atomKind != expected)
            {
                throw MemoryLogicalSizeException(
                    PathFor(fieldName) + ": kind mismatch");
            }

            frame.nextAtom++;
        }

        private string PathFor(string fieldName)
        {
            string parent = CurrentPath();
            return parent.Length == 0 ? fieldName : parent + "." + fieldName;
        }

        private void AddOrThrow(long bytes)
        {
            string path = CurrentPath();
            if (!AddChecked(bytes, path))
            {
                throw MemoryLogicalSizeException(path + ": overflow");
            }
        }

        private bool AddChecked(long bytes, string errorPath)
        {
            if (bytes < 0 || total > long.MaxValue - bytes)
            {
                total = -1;
                return false;
            }

            total += bytes;
            return true;
        }

        private static long StringCharge(string errorPath, string value)
        {
            if (value == null)
            {
                throw MemoryLogicalSizeException(errorPath + ": null string");
            }

            if (!MemoryIdentityCodec.IsWellFormedUtf16(value))
            {
                throw MemoryLogicalSizeException(errorPath + ": unpaired surrogate");
            }

            int utf8Bytes;
            try
            {
                utf8Bytes = new System.Text.UTF8Encoding(false, true).GetByteCount(value);
            }
            catch (ArgumentException)
            {
                // Cannot occur after the surrogate check; kept for defense in depth.
                throw MemoryLogicalSizeException(errorPath + ": invalid UTF-16");
            }

            long charge = LengthPrefixBytes + utf8Bytes;
            if (charge < 0)
            {
                throw MemoryLogicalSizeException(errorPath + ": overflow");
            }

            return charge;
        }

        private static Exception MemoryLogicalSizeException(string message)
        {
            return new MemoryLogicalSizeValidationException(message);
        }
    }

    /// <summary>Thrown by the collector on any shape/order/encoding violation; callers catch it and
    /// convert to the typed Invalid result so sizing never mutates or throws through adapters.</summary>
    internal sealed class MemoryLogicalSizeValidationException : Exception
    {
        public MemoryLogicalSizeValidationException(string message)
            : base(message)
        {
        }
    }

    internal static class MemoryLogicalPayloadSizer
    {
        public const string SchemaToken = "memory-logical-payload-v1";

        /// <summary>Sizes one complete top-level row.</summary>
        public static MemoryLogicalSizeResult Size(IMemoryLogicalSizeSource source)
        {
            if (source == null)
            {
                return MemoryLogicalSizeResult.Invalid("null-source");
            }

            MemoryLogicalSizeCollector collector = new MemoryLogicalSizeCollector();
            try
            {
                source.CollectFields(collector);
                if (!collector.IsComplete())
                {
                    throw new MemoryLogicalSizeValidationException(
                        "unclosed row at end of walk");
                }
            }
            catch (MemoryLogicalSizeValidationException exception)
            {
                return MemoryLogicalSizeResult.Invalid(exception.Message);
            }

            MemoryLogicalSizeResult result = new MemoryLogicalSizeResult
            {
                valid = true,
                totalBytes = collector.Total(),
                errorPath = string.Empty
            };
            return result;
        }

        /// <summary>Sizes an optional singleton row outside any enclosing row context: charges the
        /// one presence byte plus the full nested row when present (§T6.0 singleton rule).</summary>
        public static MemoryLogicalSizeResult SizeNullableSingleton(
            string fieldName,
            IMemoryLogicalSizeSource sourceOrNull)
        {
            MemoryLogicalSizeCollector collector = new MemoryLogicalSizeCollector();
            try
            {
                collector.UncheckedBytes(1, fieldName);
                if (sourceOrNull != null)
                {
                    collector.NestedRow(sourceOrNull);
                }
            }
            catch (MemoryLogicalSizeValidationException exception)
            {
                return MemoryLogicalSizeResult.Invalid(exception.Message);
            }

            return new MemoryLogicalSizeResult
            {
                valid = true,
                totalBytes = collector.Total(),
                errorPath = string.Empty
            };
        }
    }
}
