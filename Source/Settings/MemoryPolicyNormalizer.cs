// MemoryPolicyNormalizer.cs — pure Phase M5 settings normalization and reconciliation planning.
//
// RimWorld settings/Scribe objects stay at the adapter edge. This file accepts only detached values,
// repairs XML-authored ranges under code-owned ceilings, and returns one complete immutable policy.
// The same policy drives settings persistence, deferred per-save reconciliation, maintenance, capture,
// recall, and Library status so none of those consumers can observe a half-edited settings window.
using System;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    /// <summary>Stable category bits stored in the M5 settings policy mask.</summary>
    internal static class MemoryCategoryBits
    {
        public const int Personal = 1;
        public const int Relationships = 2;
        public const int Family = 4;
        public const int Factions = 8;
        public const int KnownMask = Personal | Relationships | Family | Factions;

        /// <summary>Maps one saved category token to its unique settings bit, or zero when unknown.</summary>
        public static int ForToken(string token)
        {
            if (token == MemoryContractTokens.CategoryPersonal) return Personal;
            if (token == MemoryContractTokens.CategoryRelationships) return Relationships;
            if (token == MemoryContractTokens.CategoryFamily) return Family;
            if (token == MemoryContractTokens.CategoryFactions) return Factions;
            return 0;
        }
    }

    /// <summary>
    /// Detached XML minima/defaults/maxima. The pure normalizer first repairs this range and only then
    /// clamps saved values, so malformed Def values cannot expand a code-owned defensive ceiling.
    /// </summary>
    internal sealed class MemorySettingsBounds
    {
        public int minorMinimumDays = 1;
        public int minorDefaultDays = 15;
        public int minorMaximumDays = 3600;
        public int regularMinimumDays = 1;
        public int regularDefaultDays = 60;
        public int regularMaximumDays = 3600;
        public int threadTargetMinimum = 4;
        public int threadTargetDefault = 12;
        public int threadTargetMaximum = 64;
        public int reuseMinimumDays = 1;
        public int reuseDefaultDays = 5;
        public int reuseMaximumDays = 3600;
        public int revisitMinimumEntries = 1;
        public int revisitDefaultEntries = 3;
        public int revisitMaximumEntries = 1000;

        public MemorySettingsBounds Clone()
        {
            return (MemorySettingsBounds)MemberwiseClone();
        }
    }

    /// <summary>One complete immutable runtime policy publication.</summary>
    internal sealed class MemoryPolicySnapshot
    {
        public readonly int settingsSchemaVersion;
        public readonly bool compatibilityFailClosed;
        public readonly bool saveNewMemories;
        public readonly bool useMemoriesInWriting;
        public readonly bool usePawnBackground;
        public readonly bool allowExtraMemoryAiRequests;
        public readonly bool occasionalMemoryReflections;
        public readonly int memoryCategoryMask;
        public readonly long captureInvalidationGenerationPersonal;
        public readonly long captureInvalidationGenerationRelationships;
        public readonly long captureInvalidationGenerationFamily;
        public readonly long captureInvalidationGenerationFactions;
        public readonly long optionalRequestInvalidationGeneration;
        public readonly int minorMemoryLifetimeDays;
        public readonly int regularMemoryLifetimeDays;
        public readonly int memoryThreadTarget;
        public readonly int memoryReuseDays;
        public readonly int memoryRevisitEntryCount;
        public readonly long minorMemoryLifetimeTicks;
        public readonly long regularMemoryLifetimeTicks;
        public readonly string fingerprint;

        internal MemoryPolicySnapshot(
            int schemaVersion,
            bool futureVersion,
            MemorySettingsPolicyFieldsV1 fields,
            string policyFingerprint)
        {
            settingsSchemaVersion = schemaVersion;
            compatibilityFailClosed = futureVersion;
            saveNewMemories = !futureVersion && fields.saveNewMemories;
            useMemoriesInWriting = !futureVersion && fields.useMemoriesInWriting;
            usePawnBackground = !futureVersion && fields.usePawnBackground;
            allowExtraMemoryAiRequests = !futureVersion && fields.allowExtraMemoryAiRequests;
            occasionalMemoryReflections = !futureVersion && fields.occasionalMemoryReflections;
            memoryCategoryMask = futureVersion ? 0 : fields.memoryCategoryMask;
            captureInvalidationGenerationPersonal = fields.captureInvalidationGenerationPersonal;
            captureInvalidationGenerationRelationships = fields.captureInvalidationGenerationRelationships;
            captureInvalidationGenerationFamily = fields.captureInvalidationGenerationFamily;
            captureInvalidationGenerationFactions = fields.captureInvalidationGenerationFactions;
            optionalRequestInvalidationGeneration = fields.optionalRequestInvalidationGeneration;
            minorMemoryLifetimeDays = fields.minorMemoryLifetimeDays;
            regularMemoryLifetimeDays = fields.regularMemoryLifetimeDays;
            memoryThreadTarget = fields.memoryThreadTarget;
            memoryReuseDays = fields.memoryReuseDays;
            memoryRevisitEntryCount = fields.memoryRevisitEntryCount;
            minorMemoryLifetimeTicks = MemoryPolicyNormalizer.DaysToTicks(fields.minorMemoryLifetimeDays);
            regularMemoryLifetimeTicks = MemoryPolicyNormalizer.DaysToTicks(fields.regularMemoryLifetimeDays);
            fingerprint = policyFingerprint ?? string.Empty;
        }

        /// <summary>True only when this category may accumulate a new episodic memory.</summary>
        public bool AllowsCapture(int categoryBit)
        {
            return !compatibilityFailClosed
                && saveNewMemories
                && IsSingleKnownBit(categoryBit)
                && (memoryCategoryMask & categoryBit) != 0
                && CaptureGeneration(categoryBit) > 0
                && CaptureGeneration(categoryBit) < long.MaxValue;
        }

        /// <summary>True only when this category may participate in future natural recall.</summary>
        public bool AllowsRecall(int categoryBit)
        {
            return !compatibilityFailClosed
                && useMemoriesInWriting
                && IsSingleKnownBit(categoryBit)
                && (memoryCategoryMask & categoryBit) != 0;
        }

        /// <summary>Effective gate for optional memory-created requests.</summary>
        public bool AllowsOptionalRequests => !compatibilityFailClosed
            && useMemoriesInWriting
            && allowExtraMemoryAiRequests
            && optionalRequestInvalidationGeneration > 0
            && optionalRequestInvalidationGeneration < long.MaxValue;

        /// <summary>Effective gate for the dependent quiet-reflection opportunity.</summary>
        public bool AllowsOccasionalReflections => AllowsOptionalRequests && occasionalMemoryReflections;

        /// <summary>Returns the settings-file generation for one exact category bit.</summary>
        public long CaptureGeneration(int categoryBit)
        {
            if (categoryBit == MemoryCategoryBits.Personal)
                return captureInvalidationGenerationPersonal;
            if (categoryBit == MemoryCategoryBits.Relationships)
                return captureInvalidationGenerationRelationships;
            if (categoryBit == MemoryCategoryBits.Family)
                return captureInvalidationGenerationFamily;
            if (categoryBit == MemoryCategoryBits.Factions)
                return captureInvalidationGenerationFactions;
            return 0;
        }

        /// <summary>Copies this immutable publication back to the exact persisted tuple shape.</summary>
        public MemorySettingsPolicyFieldsV1 ToFields()
        {
            return new MemorySettingsPolicyFieldsV1
            {
                saveNewMemories = saveNewMemories,
                useMemoriesInWriting = useMemoriesInWriting,
                usePawnBackground = usePawnBackground,
                allowExtraMemoryAiRequests = allowExtraMemoryAiRequests,
                occasionalMemoryReflections = occasionalMemoryReflections,
                memoryCategoryMask = memoryCategoryMask,
                captureInvalidationGenerationPersonal = captureInvalidationGenerationPersonal,
                captureInvalidationGenerationRelationships = captureInvalidationGenerationRelationships,
                captureInvalidationGenerationFamily = captureInvalidationGenerationFamily,
                captureInvalidationGenerationFactions = captureInvalidationGenerationFactions,
                optionalRequestInvalidationGeneration = optionalRequestInvalidationGeneration,
                minorMemoryLifetimeDays = minorMemoryLifetimeDays,
                regularMemoryLifetimeDays = regularMemoryLifetimeDays,
                memoryThreadTarget = memoryThreadTarget,
                memoryReuseDays = memoryReuseDays,
                memoryRevisitEntryCount = memoryRevisitEntryCount
            };
        }

        private static bool IsSingleKnownBit(int bit)
        {
            return bit != 0 && (bit & (bit - 1)) == 0 && (bit & MemoryCategoryBits.KnownMask) != 0;
        }
    }

    /// <summary>Prepared settings candidate; retries persist this exact tuple without advancing again.</summary>
    internal sealed class MemorySettingsCommitPlan
    {
        public bool valid;
        public bool futureVersion;
        public MemorySettingsPolicyFieldsV1 candidate = new MemorySettingsPolicyFieldsV1();
        public MemoryPolicySnapshot snapshot;
        public int changedCaptureMask;
        public bool optionalGenerationChanged;
    }

    /// <summary>Pure idempotent delta between one saved applied row and the published settings policy.</summary>
    internal sealed class MemoryPolicyReconciliationPlan
    {
        public bool valid;
        public bool alreadyApplied;
        public bool revisionSaturated;
        public bool advanceGlobalOptionalCancellation;
        public bool purgeUnsentOptionalWork;
        public int captureGenerationMismatchMask;
        public bool markLifetimeMaintenanceDirty;
        public bool markThreadTargetMaintenanceDirty;
        public long nextAppliedRevision;
    }

    /// <summary>Pure normalization, migration, commit, and deferred-reconciliation rules for M5.</summary>
    internal static class MemoryPolicyNormalizer
    {
        public const int CurrentSettingsSchemaVersion = 1;
        public const int TicksPerDay = 60000;
        public const int LifetimeDayDefensiveCeiling = 35791;
        public const int RevisitEntryDefensiveCeiling = 1000000;
        public const int ThreadTargetMinimum = 4;
        public const int ThreadTargetDefault = 12;
        public const int ThreadTargetMaximum = 64;

        /// <summary>Returns repaired bounds; source is never mutated.</summary>
        public static MemorySettingsBounds NormalizeBounds(MemorySettingsBounds source)
        {
            MemorySettingsBounds value = (source ?? new MemorySettingsBounds()).Clone();
            RepairRange(ref value.minorMinimumDays, ref value.minorDefaultDays,
                ref value.minorMaximumDays, 1, LifetimeDayDefensiveCeiling, 15);
            RepairRange(ref value.regularMinimumDays, ref value.regularDefaultDays,
                ref value.regularMaximumDays, 1, LifetimeDayDefensiveCeiling, 60);
            RepairRange(ref value.threadTargetMinimum, ref value.threadTargetDefault,
                ref value.threadTargetMaximum, ThreadTargetMinimum, ThreadTargetMaximum,
                ThreadTargetDefault);
            RepairRange(ref value.reuseMinimumDays, ref value.reuseDefaultDays,
                ref value.reuseMaximumDays, 1, LifetimeDayDefensiveCeiling, 5);
            RepairRange(ref value.revisitMinimumEntries, ref value.revisitDefaultEntries,
                ref value.revisitMaximumEntries, 1, RevisitEntryDefensiveCeiling, 3);
            return value;
        }

        /// <summary>
        /// Migrates the exact version-0 legacy master/mode contract. Legacy mode values are the stable
        /// PromptContextDetailLevel ordinals Full=0, Balanced=1, Compact=2; an unknown value fails Off.
        /// </summary>
        public static MemorySettingsPolicyFieldsV1 MigrateVersionZero(
            bool legacyMasterEnabled,
            int legacyContextDetailLevel,
            MemorySettingsBounds sourceBounds)
        {
            MemorySettingsBounds bounds = NormalizeBounds(sourceBounds);
            bool legacyUse = legacyMasterEnabled
                && (legacyContextDetailLevel == 0 || legacyContextDetailLevel == 1);
            return new MemorySettingsPolicyFieldsV1
            {
                saveNewMemories = true,
                useMemoriesInWriting = legacyUse,
                usePawnBackground = true,
                allowExtraMemoryAiRequests = false,
                occasionalMemoryReflections = false,
                memoryCategoryMask = MemoryCategoryBits.KnownMask,
                captureInvalidationGenerationPersonal = 1,
                captureInvalidationGenerationRelationships = 1,
                captureInvalidationGenerationFamily = 1,
                captureInvalidationGenerationFactions = 1,
                optionalRequestInvalidationGeneration = 1,
                minorMemoryLifetimeDays = bounds.minorDefaultDays,
                regularMemoryLifetimeDays = bounds.regularDefaultDays,
                memoryThreadTarget = bounds.threadTargetDefault,
                memoryReuseDays = bounds.reuseDefaultDays,
                memoryRevisitEntryCount = bounds.revisitDefaultEntries
            };
        }

        /// <summary>Normalizes one current tuple without advancing transition generations.</summary>
        public static MemoryPolicySnapshot Normalize(
            int settingsSchemaVersion,
            MemorySettingsPolicyFieldsV1 source,
            MemorySettingsBounds sourceBounds)
        {
            bool future = settingsSchemaVersion > CurrentSettingsSchemaVersion;
            MemorySettingsBounds bounds = NormalizeBounds(sourceBounds);
            MemorySettingsPolicyFieldsV1 value = Copy(source ?? new MemorySettingsPolicyFieldsV1());
            value.memoryCategoryMask &= MemoryCategoryBits.KnownMask;
            value.captureInvalidationGenerationPersonal = PositiveGeneration(
                value.captureInvalidationGenerationPersonal);
            value.captureInvalidationGenerationRelationships = PositiveGeneration(
                value.captureInvalidationGenerationRelationships);
            value.captureInvalidationGenerationFamily = PositiveGeneration(
                value.captureInvalidationGenerationFamily);
            value.captureInvalidationGenerationFactions = PositiveGeneration(
                value.captureInvalidationGenerationFactions);
            value.optionalRequestInvalidationGeneration = PositiveGeneration(
                value.optionalRequestInvalidationGeneration);
            value.minorMemoryLifetimeDays = Clamp(value.minorMemoryLifetimeDays,
                bounds.minorMinimumDays, bounds.minorMaximumDays);
            value.regularMemoryLifetimeDays = Clamp(value.regularMemoryLifetimeDays,
                bounds.regularMinimumDays, bounds.regularMaximumDays);
            if (value.minorMemoryLifetimeDays > value.regularMemoryLifetimeDays)
                value.minorMemoryLifetimeDays = value.regularMemoryLifetimeDays;
            value.memoryThreadTarget = Clamp(value.memoryThreadTarget,
                bounds.threadTargetMinimum, bounds.threadTargetMaximum);
            value.memoryReuseDays = Clamp(value.memoryReuseDays,
                bounds.reuseMinimumDays, bounds.reuseMaximumDays);
            value.memoryRevisitEntryCount = Clamp(value.memoryRevisitEntryCount,
                bounds.revisitMinimumEntries, bounds.revisitMaximumEntries);
            if (!value.useMemoriesInWriting)
            {
                value.allowExtraMemoryAiRequests = false;
                value.occasionalMemoryReflections = false;
            }
            if (!value.allowExtraMemoryAiRequests)
                value.occasionalMemoryReflections = false;
            if (value.optionalRequestInvalidationGeneration == long.MaxValue)
            {
                value.allowExtraMemoryAiRequests = false;
                value.occasionalMemoryReflections = false;
            }
            string fingerprint = Fingerprint(value);
            return new MemoryPolicySnapshot(settingsSchemaVersion, future, value, fingerprint);
        }

        /// <summary>
        /// Normalizes a draft and advances only generations whose effective gates changed. The returned
        /// candidate owns those advances so persistence retries reuse it instead of incrementing twice.
        /// </summary>
        public static MemorySettingsCommitPlan PrepareCommit(
            int settingsSchemaVersion,
            MemorySettingsPolicyFieldsV1 priorSource,
            MemorySettingsPolicyFieldsV1 draftSource,
            MemorySettingsBounds bounds)
        {
            MemorySettingsCommitPlan result = new MemorySettingsCommitPlan();
            if (settingsSchemaVersion > CurrentSettingsSchemaVersion)
            {
                result.futureVersion = true;
                return result;
            }
            MemoryPolicySnapshot prior = Normalize(settingsSchemaVersion, priorSource, bounds);
            MemoryPolicySnapshot draft = Normalize(settingsSchemaVersion, draftSource, bounds);
            MemorySettingsPolicyFieldsV1 candidate = draft.ToFields();
            MemorySettingsPolicyFieldsV1 priorFields = prior.ToFields();
            candidate.captureInvalidationGenerationPersonal =
                priorFields.captureInvalidationGenerationPersonal;
            candidate.captureInvalidationGenerationRelationships =
                priorFields.captureInvalidationGenerationRelationships;
            candidate.captureInvalidationGenerationFamily =
                priorFields.captureInvalidationGenerationFamily;
            candidate.captureInvalidationGenerationFactions =
                priorFields.captureInvalidationGenerationFactions;
            candidate.optionalRequestInvalidationGeneration =
                priorFields.optionalRequestInvalidationGeneration;

            int oldCapture = prior.saveNewMemories ? prior.memoryCategoryMask : 0;
            int newCapture = draft.saveNewMemories ? draft.memoryCategoryMask : 0;
            result.changedCaptureMask = oldCapture ^ newCapture;
            if ((result.changedCaptureMask & MemoryCategoryBits.Personal) != 0)
                candidate.captureInvalidationGenerationPersonal = AdvanceGeneration(
                    candidate.captureInvalidationGenerationPersonal);
            if ((result.changedCaptureMask & MemoryCategoryBits.Relationships) != 0)
                candidate.captureInvalidationGenerationRelationships = AdvanceGeneration(
                    candidate.captureInvalidationGenerationRelationships);
            if ((result.changedCaptureMask & MemoryCategoryBits.Family) != 0)
                candidate.captureInvalidationGenerationFamily = AdvanceGeneration(
                    candidate.captureInvalidationGenerationFamily);
            if ((result.changedCaptureMask & MemoryCategoryBits.Factions) != 0)
                candidate.captureInvalidationGenerationFactions = AdvanceGeneration(
                    candidate.captureInvalidationGenerationFactions);

            bool oldOptional = prior.AllowsOptionalRequests;
            bool newOptional = draft.AllowsOptionalRequests;
            result.optionalGenerationChanged = oldOptional != newOptional;
            if (result.optionalGenerationChanged)
                candidate.optionalRequestInvalidationGeneration = AdvanceGeneration(
                    candidate.optionalRequestInvalidationGeneration);

            result.candidate = candidate;
            result.snapshot = Normalize(settingsSchemaVersion, candidate, bounds);
            result.valid = true;
            return result;
        }

        /// <summary>Plans the one per-save delta applied after policy publication or on next load.</summary>
        public static MemoryPolicyReconciliationPlan PlanReconciliation(
            MemorySettingsPolicyFieldsV1 appliedSource,
            string appliedFingerprint,
            long appliedRevision,
            MemoryPolicySnapshot published)
        {
            MemoryPolicyReconciliationPlan plan = new MemoryPolicyReconciliationPlan();
            if (published == null || published.compatibilityFailClosed || appliedRevision < 0)
                return plan;
            plan.valid = true;
            string oldFingerprint = (appliedFingerprint ?? string.Empty).Trim();
            if (oldFingerprint.Length > 0
                && string.Equals(oldFingerprint, published.fingerprint, StringComparison.Ordinal))
            {
                plan.alreadyApplied = true;
                plan.nextAppliedRevision = appliedRevision;
                return plan;
            }
            if (appliedRevision == long.MaxValue)
            {
                plan.revisionSaturated = true;
                plan.nextAppliedRevision = long.MaxValue;
                return plan;
            }

            MemorySettingsPolicyFieldsV1 prior = appliedSource == null
                ? null
                : Copy(appliedSource);
            MemorySettingsPolicyFieldsV1 current = published.ToFields();
            plan.nextAppliedRevision = appliedRevision + 1;
            if (prior == null)
            {
                plan.advanceGlobalOptionalCancellation = true;
                plan.purgeUnsentOptionalWork = true;
                plan.captureGenerationMismatchMask = MemoryCategoryBits.KnownMask;
                plan.markLifetimeMaintenanceDirty = true;
                plan.markThreadTargetMaintenanceDirty = true;
                return plan;
            }

            bool useTurnedOff = prior.useMemoriesInWriting && !current.useMemoriesInWriting;
            bool extraTurnedOff = prior.allowExtraMemoryAiRequests
                && !current.allowExtraMemoryAiRequests;
            plan.advanceGlobalOptionalCancellation = useTurnedOff || extraTurnedOff;
            plan.purgeUnsentOptionalWork = prior.optionalRequestInvalidationGeneration
                != current.optionalRequestInvalidationGeneration;
            if (prior.captureInvalidationGenerationPersonal
                != current.captureInvalidationGenerationPersonal)
                plan.captureGenerationMismatchMask |= MemoryCategoryBits.Personal;
            if (prior.captureInvalidationGenerationRelationships
                != current.captureInvalidationGenerationRelationships)
                plan.captureGenerationMismatchMask |= MemoryCategoryBits.Relationships;
            if (prior.captureInvalidationGenerationFamily
                != current.captureInvalidationGenerationFamily)
                plan.captureGenerationMismatchMask |= MemoryCategoryBits.Family;
            if (prior.captureInvalidationGenerationFactions
                != current.captureInvalidationGenerationFactions)
                plan.captureGenerationMismatchMask |= MemoryCategoryBits.Factions;
            plan.markLifetimeMaintenanceDirty = current.minorMemoryLifetimeDays
                    < prior.minorMemoryLifetimeDays
                || current.regularMemoryLifetimeDays < prior.regularMemoryLifetimeDays;
            plan.markThreadTargetMaintenanceDirty =
                current.memoryThreadTarget < prior.memoryThreadTarget;
            return plan;
        }

        /// <summary>Canonical lowercase SHA-256 fingerprint of the frozen M0 tuple encoding.</summary>
        public static string Fingerprint(MemorySettingsPolicyFieldsV1 fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(MemorySettingsPolicyCodec.Encode(fields));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary>Checked day-to-tick conversion after normalization.</summary>
        public static long DaysToTicks(int days)
        {
            int bounded = Clamp(days, 1, LifetimeDayDefensiveCeiling);
            return checked((long)bounded * TicksPerDay);
        }

        public static MemorySettingsPolicyFieldsV1 Copy(MemorySettingsPolicyFieldsV1 source)
        {
            MemorySettingsPolicyFieldsV1 value = source ?? new MemorySettingsPolicyFieldsV1();
            return new MemorySettingsPolicyFieldsV1
            {
                saveNewMemories = value.saveNewMemories,
                useMemoriesInWriting = value.useMemoriesInWriting,
                usePawnBackground = value.usePawnBackground,
                allowExtraMemoryAiRequests = value.allowExtraMemoryAiRequests,
                occasionalMemoryReflections = value.occasionalMemoryReflections,
                memoryCategoryMask = value.memoryCategoryMask,
                captureInvalidationGenerationPersonal = value.captureInvalidationGenerationPersonal,
                captureInvalidationGenerationRelationships = value.captureInvalidationGenerationRelationships,
                captureInvalidationGenerationFamily = value.captureInvalidationGenerationFamily,
                captureInvalidationGenerationFactions = value.captureInvalidationGenerationFactions,
                optionalRequestInvalidationGeneration = value.optionalRequestInvalidationGeneration,
                minorMemoryLifetimeDays = value.minorMemoryLifetimeDays,
                regularMemoryLifetimeDays = value.regularMemoryLifetimeDays,
                memoryThreadTarget = value.memoryThreadTarget,
                memoryReuseDays = value.memoryReuseDays,
                memoryRevisitEntryCount = value.memoryRevisitEntryCount
            };
        }

        private static long PositiveGeneration(long value)
        {
            return value <= 0 ? 1 : value;
        }

        private static long AdvanceGeneration(long value)
        {
            value = PositiveGeneration(value);
            return value == long.MaxValue ? long.MaxValue : value + 1;
        }

        private static void RepairRange(
            ref int minimum,
            ref int defaultValue,
            ref int maximum,
            int codeMinimum,
            int codeMaximum,
            int codeDefault)
        {
            maximum = Clamp(maximum, codeMinimum, codeMaximum);
            minimum = Clamp(minimum, codeMinimum, codeMaximum);
            if (minimum > maximum) minimum = maximum;
            defaultValue = Clamp(defaultValue, minimum, maximum);
            if (defaultValue < codeMinimum || defaultValue > codeMaximum)
                defaultValue = Clamp(codeDefault, minimum, maximum);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}
