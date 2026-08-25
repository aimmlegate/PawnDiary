// MemorySettingsDraft.cs — detached, session-only state for the future Memory settings pages.
//
// RimWorld redraws settings several times per frame. Keeping these values outside PawnDiarySettings
// means Layout/Repaint and half-finished text edits cannot alter the durable or effective policy.
// This file is deliberately plain C#: the standalone memory harness exercises every dependency and
// numeric-input rule without loading Verse or Unity.
using System;
using System.Text;

namespace PawnDiary
{
    /// <summary>Stable identifiers for the five approved integer fields in Memory settings.</summary>
    internal static class MemoryNumericSettingKeys
    {
        public const string MinorLifetimeDays = "minorLifetimeDays";
        public const string RegularLifetimeDays = "regularLifetimeDays";
        public const string ThreadTarget = "threadTarget";
        public const string ReuseDays = "reuseDays";
        public const string RevisitEntries = "revisitEntries";
    }

    /// <summary>One bounded text buffer plus the last value accepted for that field.</summary>
    internal sealed class MemoryIntegerDraft
    {
        public string rawBuffer = string.Empty;
        public int lastValidValue;

        /// <summary>Starts with one already normalized integer and matching invariant text.</summary>
        public MemoryIntegerDraft(int value)
        {
            SetNormalized(value);
        }

        /// <summary>Replaces both the displayed text and last-valid value after normalization.</summary>
        public void SetNormalized(int value)
        {
            lastValidValue = value;
            rawBuffer = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Detached editable Memory policy. It owns only plain values and bounded text buffers; the
    /// settings adapter decides when a finalized copy reaches persistence and runtime publication.
    /// </summary>
    internal sealed class MemorySettingsDraft
    {
        /// <summary>Defensive session cap from CAP-SETTINGS-NUMERIC-DRAFT.</summary>
        public const int NumericDraftUtf16Units = 32;

        private MemorySettingsPolicyFieldsV1 fields;
        private readonly MemoryIntegerDraft minorLifetime;
        private readonly MemoryIntegerDraft regularLifetime;
        private readonly MemoryIntegerDraft threadTarget;
        private readonly MemoryIntegerDraft reuseDays;
        private readonly MemoryIntegerDraft revisitEntries;

        /// <summary>Shows an invalid pair until a completed edit repairs it; Save repairs stay visible.</summary>
        public bool invalidLifetimeOrderWarning;

        private MemorySettingsDraft(MemorySettingsPolicyFieldsV1 normalized)
        {
            fields = MemoryPolicyNormalizer.Copy(normalized);
            minorLifetime = new MemoryIntegerDraft(fields.minorMemoryLifetimeDays);
            regularLifetime = new MemoryIntegerDraft(fields.regularMemoryLifetimeDays);
            threadTarget = new MemoryIntegerDraft(fields.memoryThreadTarget);
            reuseDays = new MemoryIntegerDraft(fields.memoryReuseDays);
            revisitEntries = new MemoryIntegerDraft(fields.memoryRevisitEntryCount);
        }

        /// <summary>Creates a draft from one current immutable policy snapshot.</summary>
        public static MemorySettingsDraft FromSnapshot(
            MemoryPolicySnapshot snapshot,
            MemorySettingsBounds bounds)
        {
            MemorySettingsPolicyFieldsV1 source = snapshot == null
                ? new MemorySettingsPolicyFieldsV1()
                : snapshot.ToFields();
            MemoryPolicySnapshot normalized = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                source,
                bounds);
            return new MemorySettingsDraft(normalized.ToFields());
        }

        public bool SaveNewMemories => fields.saveNewMemories;
        public bool UseMemoriesInWriting => fields.useMemoriesInWriting;
        public bool UsePawnBackground => fields.usePawnBackground;
        public bool AllowExtraMemoryAiRequests => fields.allowExtraMemoryAiRequests;
        public bool OccasionalMemoryReflections => fields.occasionalMemoryReflections;

        /// <summary>Changes future capture without coupling it to any output setting.</summary>
        public void SetSaveNewMemories(bool value)
        {
            fields.saveNewMemories = value;
        }

        /// <summary>Changes normal recall and explicitly clears both dependent request switches.</summary>
        public void SetUseMemoriesInWriting(bool value)
        {
            fields.useMemoriesInWriting = value;
            if (!value)
            {
                fields.allowExtraMemoryAiRequests = false;
                fields.occasionalMemoryReflections = false;
            }
        }

        /// <summary>Changes the independent player-authored background gate.</summary>
        public void SetUsePawnBackground(bool value)
        {
            fields.usePawnBackground = value;
        }

        /// <summary>Changes optional request permission and clears its child when disabled.</summary>
        public void SetAllowExtraMemoryAiRequests(bool value)
        {
            fields.allowExtraMemoryAiRequests = fields.useMemoriesInWriting && value;
            if (!fields.allowExtraMemoryAiRequests)
                fields.occasionalMemoryReflections = false;
        }

        /// <summary>Enables quiet reflections only while both parent gates are currently enabled.</summary>
        public void SetOccasionalMemoryReflections(bool value)
        {
            fields.occasionalMemoryReflections = fields.useMemoriesInWriting
                && fields.allowExtraMemoryAiRequests
                && value;
        }

        /// <summary>Returns whether one known capture/recall category is selected.</summary>
        public bool CategoryEnabled(int categoryBit)
        {
            int known = categoryBit & MemoryCategoryBits.KnownMask;
            return known != 0 && (fields.memoryCategoryMask & known) == known;
        }

        /// <summary>Changes one known category without deleting or otherwise touching stored rows.</summary>
        public void SetCategoryEnabled(int categoryBit, bool enabled)
        {
            int known = categoryBit & MemoryCategoryBits.KnownMask;
            if (known == 0) return;
            if (enabled)
                fields.memoryCategoryMask |= known;
            else
                fields.memoryCategoryMask &= ~known;
            fields.memoryCategoryMask &= MemoryCategoryBits.KnownMask;
        }

        /// <summary>Returns the bounded text currently displayed for one approved integer field.</summary>
        public string NumericBuffer(string key)
        {
            return NumericDraft(key).rawBuffer;
        }

        /// <summary>
        /// Retains at most 32 UTF-16 units from a GUI edit, repairing malformed surrogate input and
        /// never cutting a valid pair in half. Parsing waits until the edit is completed.
        /// </summary>
        public void UpdateNumericBuffer(string key, string rawValue)
        {
            NumericDraft(key).rawBuffer = ClampCompleteUtf16(rawValue, NumericDraftUtf16Units);
        }

        /// <summary>Completes one field edit, restoring last-valid text or displaying a clamped value.</summary>
        public void CompleteNumericEdit(string key, MemorySettingsBounds sourceBounds)
        {
            MemorySettingsBounds bounds = MemoryPolicyNormalizer.NormalizeBounds(sourceBounds);
            CompleteNumericEditCore(key, bounds);
            // Keep the two lifetime candidates independent until Save. Clamping Minor against the
            // old Regular while focus moves between their fields would discard a valid 100/200 edit.
            UpdateLifetimeOrderWarning();
        }

        private void CompleteNumericEditCore(string key, MemorySettingsBounds bounds)
        {
            MemoryIntegerDraft draft = NumericDraft(key);
            int minimum;
            int maximum;
            BoundsFor(key, bounds, out minimum, out maximum);
            int value;
            if (!TryParseIntegerClamped(draft.rawBuffer, minimum, maximum, out value))
            {
                draft.SetNormalized(draft.lastValidValue);
            }
            else
            {
                draft.SetNormalized(value);
                WriteNumericValue(key, value);
            }
        }

        /// <summary>
        /// Completes every pending edit and returns a detached normalized tuple for PrepareCommit.
        /// Repeating this method is idempotent and never advances a generation.
        /// </summary>
        public MemorySettingsPolicyFieldsV1 BuildCommitFields(MemorySettingsBounds bounds)
        {
            MemorySettingsBounds normalizedBounds = MemoryPolicyNormalizer.NormalizeBounds(bounds);
            // Parse both lifetimes before enforcing their relationship. If the user pasted 100/200
            // and closed the window without moving focus, checking Minor against the old Regular=60
            // first would incorrectly discard the valid 100-day draft.
            CompleteNumericEditCore(MemoryNumericSettingKeys.MinorLifetimeDays, normalizedBounds);
            CompleteNumericEditCore(MemoryNumericSettingKeys.RegularLifetimeDays, normalizedBounds);
            CompleteNumericEditCore(MemoryNumericSettingKeys.ThreadTarget, normalizedBounds);
            CompleteNumericEditCore(MemoryNumericSettingKeys.ReuseDays, normalizedBounds);
            CompleteNumericEditCore(MemoryNumericSettingKeys.RevisitEntries, normalizedBounds);
            NormalizeLifetimeOrder();

            MemoryPolicySnapshot normalized = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                normalizedBounds);
            fields = normalized.ToFields();
            SyncNumericDrafts();
            return MemoryPolicyNormalizer.Copy(fields);
        }

        /// <summary>Returns a detached preview without parsing unfinished numeric buffers.</summary>
        public MemorySettingsPolicyFieldsV1 PreviewFields()
        {
            return MemoryPolicyNormalizer.Copy(fields);
        }

        private MemoryIntegerDraft NumericDraft(string key)
        {
            switch (key)
            {
                case MemoryNumericSettingKeys.MinorLifetimeDays: return minorLifetime;
                case MemoryNumericSettingKeys.RegularLifetimeDays: return regularLifetime;
                case MemoryNumericSettingKeys.ThreadTarget: return threadTarget;
                case MemoryNumericSettingKeys.ReuseDays: return reuseDays;
                case MemoryNumericSettingKeys.RevisitEntries: return revisitEntries;
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Memory numeric field.");
            }
        }

        private static void BoundsFor(
            string key,
            MemorySettingsBounds bounds,
            out int minimum,
            out int maximum)
        {
            switch (key)
            {
                case MemoryNumericSettingKeys.MinorLifetimeDays:
                    minimum = bounds.minorMinimumDays;
                    maximum = bounds.minorMaximumDays;
                    return;
                case MemoryNumericSettingKeys.RegularLifetimeDays:
                    minimum = bounds.regularMinimumDays;
                    maximum = bounds.regularMaximumDays;
                    return;
                case MemoryNumericSettingKeys.ThreadTarget:
                    minimum = bounds.threadTargetMinimum;
                    maximum = bounds.threadTargetMaximum;
                    return;
                case MemoryNumericSettingKeys.ReuseDays:
                    minimum = bounds.reuseMinimumDays;
                    maximum = bounds.reuseMaximumDays;
                    return;
                case MemoryNumericSettingKeys.RevisitEntries:
                    minimum = bounds.revisitMinimumEntries;
                    maximum = bounds.revisitMaximumEntries;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Memory numeric field.");
            }
        }

        private void WriteNumericValue(string key, int value)
        {
            switch (key)
            {
                case MemoryNumericSettingKeys.MinorLifetimeDays:
                    fields.minorMemoryLifetimeDays = value;
                    return;
                case MemoryNumericSettingKeys.RegularLifetimeDays:
                    fields.regularMemoryLifetimeDays = value;
                    return;
                case MemoryNumericSettingKeys.ThreadTarget:
                    fields.memoryThreadTarget = value;
                    return;
                case MemoryNumericSettingKeys.ReuseDays:
                    fields.memoryReuseDays = value;
                    return;
                case MemoryNumericSettingKeys.RevisitEntries:
                    fields.memoryRevisitEntryCount = value;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Memory numeric field.");
            }
        }

        private void NormalizeLifetimeOrder()
        {
            if (fields.minorMemoryLifetimeDays <= fields.regularMemoryLifetimeDays) return;
            fields.minorMemoryLifetimeDays = fields.regularMemoryLifetimeDays;
            minorLifetime.SetNormalized(fields.minorMemoryLifetimeDays);
            invalidLifetimeOrderWarning = true;
        }

        private void UpdateLifetimeOrderWarning()
        {
            invalidLifetimeOrderWarning =
                fields.minorMemoryLifetimeDays > fields.regularMemoryLifetimeDays;
        }

        private void SyncNumericDrafts()
        {
            minorLifetime.SetNormalized(fields.minorMemoryLifetimeDays);
            regularLifetime.SetNormalized(fields.regularMemoryLifetimeDays);
            threadTarget.SetNormalized(fields.memoryThreadTarget);
            reuseDays.SetNormalized(fields.memoryReuseDays);
            revisitEntries.SetNormalized(fields.memoryRevisitEntryCount);
        }

        private static bool TryParseIntegerClamped(
            string source,
            int minimum,
            int maximum,
            out int value)
        {
            value = minimum;
            if (string.IsNullOrEmpty(source)) return false;
            int start = 0;
            bool negative = false;
            if (source[0] == '-' || source[0] == '+')
            {
                negative = source[0] == '-';
                start = 1;
                if (start == source.Length) return false;
            }

            int parsed = 0;
            bool aboveMaximum = false;
            for (int index = start; index < source.Length; index++)
            {
                char character = source[index];
                if (character < '0' || character > '9') return false;
                // Every valid negative integer is below these normalized non-negative ranges. Once
                // its syntax is known to be numeric, clamp it directly without parsing its magnitude.
                if (negative) continue;
                int digit = character - '0';
                if (aboveMaximum) continue;
                if (parsed > (maximum - digit) / 10)
                {
                    aboveMaximum = true;
                    continue;
                }
                parsed = parsed * 10 + digit;
                if (parsed > maximum) aboveMaximum = true;
            }

            if (negative)
            {
                value = minimum;
                return true;
            }

            if (aboveMaximum)
            {
                value = maximum;
                return true;
            }
            value = parsed < minimum ? minimum : parsed;
            return true;
        }

        private static string ClampCompleteUtf16(string source, int maximumUnits)
        {
            if (string.IsNullOrEmpty(source) || maximumUnits <= 0) return string.Empty;
            int sourceLimit = Math.Min(source.Length, maximumUnits);
            StringBuilder result = new StringBuilder(sourceLimit);
            for (int index = 0; index < sourceLimit; index++)
            {
                char character = source[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]))
                    {
                        if (index + 1 >= sourceLimit) break;
                        result.Append(character);
                        result.Append(source[++index]);
                    }
                    else
                    {
                        result.Append('\uFFFD');
                    }
                }
                else if (char.IsLowSurrogate(character))
                {
                    result.Append('\uFFFD');
                }
                else
                {
                    result.Append(character);
                }
            }
            return result.ToString();
        }
    }
}
