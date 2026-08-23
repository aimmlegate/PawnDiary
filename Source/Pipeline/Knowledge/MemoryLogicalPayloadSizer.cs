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
        }

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
            string parent = CurrentPath();
            string path = parent.Length == 0 ? rowName : parent + "." + rowName;
            if (fields == null)
            {
                throw MemoryLogicalSizeException(path + ": row is not registered");
            }

            if (!AddChecked(RowFramingAllowanceBytes, path))
            {
                throw MemoryLogicalSizeException(path + ": overflow");
            }

            Frame frame = new Frame { rowName = rowName, fields = fields, nextAtom = 0 };
            stack.Push(frame);
            pathStack.Push(path);
        }

        /// <summary>Ends the current row; every registered field must have been pushed exactly once.</summary>
        public void EndRow()
        {
            if (stack.Count == 0 || stack.Peek().nextAtom != stack.Peek().fields.atoms.Length)
            {
                throw MemoryLogicalSizeException(
                    CurrentPath() + ": not every registered field was sized");
            }

            stack.Pop();
            pathStack.Pop();
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

        /// <summary>Charges the 4-byte list-count prefix of a list field.</summary>
        public void ListCount(string fieldName, int count)
        {
            Next(MemorySavedAtomKind.List, fieldName);
            if (count < 0)
            {
                throw MemoryLogicalSizeException(PathFor(fieldName) + ": negative count");
            }

            AddOrThrow(LengthPrefixBytes);
        }

        /// <summary>Charges one nullable-presence byte; the caller then sizes the payload when present.</summary>
        public void NullablePresence(string fieldName, bool present)
        {
            Next(MemorySavedAtomKind.NullableRow, fieldName);
            AddOrThrow(1);
        }

        /// <summary>
        /// Escape hatch for RAW pre-schema legacy leaves only (§T6.8 raw wrappers): charges
        /// 4 length-prefix bytes plus 2 bytes per UTF-16 unit. Never valid for current-schema rows,
        /// which must use the registered field methods.
        /// </summary>
        public void UnregisteredRawRow(int utf16Units)
        {
            if (utf16Units < 0)
            {
                throw new MemoryLogicalSizeValidationException(
                    CurrentPath() + ": negative raw unit count");
            }

            long bytes = LengthPrefixBytes + 2L * utf16Units;
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
