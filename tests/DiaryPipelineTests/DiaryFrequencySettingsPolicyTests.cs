// Standalone coverage for legacy frequency-settings migration and sparse override normalization.
// These tests link the production policy directly and therefore run without RimWorld/Verse/Unity.
using System;
using System.Collections.Generic;
using System.IO;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryFrequencySettingsPolicy()
        {
            List<DiaryFrequencyMigrationGroupSnapshot> migrationGroups =
                new List<DiaryFrequencyMigrationGroupSnapshot>
                {
                    MigrationGroup("workRoutine", work: true),
                    MigrationGroup("abilityUsed", ability: true),
                    MigrationGroup("smalltalk", promotion: true),
                    MigrationGroup("raid")
                };

            DiaryFrequencyMigrationResult untouched =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot(),
                    migrationGroups);
            AssertEqual("missing legacy frequency keys keep Standard sparse", 0,
                untouched.groupOverrides.Count);
            AssertTrue("missing legacy frequency keys do not show migrated Custom",
                !untouched.hasMigratedCustomIntent);

            DiaryFrequencyMigrationResult defaultUnified =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasGenerationChanceWeight = true,
                        generationChanceWeight = 1f
                    },
                    migrationGroups);
            AssertEqual("explicit default unified weight remains sparse", 0,
                defaultUnified.groupOverrides.Count);

            DiaryFrequencyMigrationResult unified =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasGenerationChanceWeight = true,
                        generationChanceWeight = 0.4f,
                        hasWorkGenerationWeight = true,
                        workGenerationWeight = 2f,
                        hasSocialGenerationWeight = true,
                        socialGenerationWeight = 3f
                    },
                    migrationGroups);
            AssertEqual("unified migration targets exactly three affected families", 3,
                unified.groupOverrides.Count);
            AssertNear("unified migration maps Work", 0.4f, unified.groupOverrides["workRoutine"]);
            AssertNear("unified migration maps Ability", 0.4f, unified.groupOverrides["abilityUsed"]);
            AssertNear("unified migration maps promotion", 0.4f, unified.groupOverrides["smalltalk"]);
            AssertTrue("unaffected event does not receive unified migration",
                !unified.groupOverrides.ContainsKey("raid"));
            AssertTrue("non-default unified intent shows migrated Custom",
                unified.hasMigratedCustomIntent);

            DiaryFrequencyMigrationResult split =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasWorkGenerationWeight = true,
                        workGenerationWeight = 0.2f,
                        hasSocialGenerationWeight = true,
                        socialGenerationWeight = 1.8f
                    },
                    migrationGroups);
            AssertNear("split migration retains Work side", 0.2f,
                split.groupOverrides["workRoutine"]);
            AssertNear("split migration retains Social promotion side", 1.8f,
                split.groupOverrides["smalltalk"]);
            AssertTrue("split compatibility average of one stays sparse for Ability",
                !split.groupOverrides.ContainsKey("abilityUsed"));

            DiaryFrequencyMigrationResult workOnly =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasWorkGenerationWeight = true,
                        workGenerationWeight = 0.2f
                    },
                    migrationGroups);
            AssertNear("work-only migration maps Work", 0.2f,
                workOnly.groupOverrides["workRoutine"]);
            AssertNear("work-only migration gives Ability compatibility average", 0.6f,
                workOnly.groupOverrides["abilityUsed"]);
            AssertTrue("missing Social side remains Standard for promotion",
                !workOnly.groupOverrides.ContainsKey("smalltalk"));

            DiaryFrequencyMigrationResult socialOnly =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasSocialGenerationWeight = true,
                        socialGenerationWeight = 0.4f
                    },
                    migrationGroups);
            AssertTrue("missing Work side remains Standard",
                !socialOnly.groupOverrides.ContainsKey("workRoutine"));
            AssertNear("social-only migration gives Ability compatibility average", 0.7f,
                socialOnly.groupOverrides["abilityUsed"]);
            AssertNear("social-only migration maps promotion", 0.4f,
                socialOnly.groupOverrides["smalltalk"]);

            DiaryFrequencyMigrationResult corruptUnified =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasGenerationChanceWeight = true,
                        generationChanceWeight = float.PositiveInfinity
                    },
                    migrationGroups);
            AssertNear("positive-infinite legacy intent clamps", 5f,
                corruptUnified.groupOverrides["workRoutine"]);
            DiaryFrequencyMigrationResult nanUnified =
                DiaryFrequencySettingsPolicy.MigrateLegacy(
                    new DiaryFrequencyLegacySettingsSnapshot
                    {
                        hasGenerationChanceWeight = true,
                        generationChanceWeight = float.NaN
                    },
                    migrationGroups);
            AssertEqual("NaN legacy intent safely becomes Standard", 0,
                nanUnified.groupOverrides.Count);

            DiaryFrequencyPresetSnapshot lite = new DiaryFrequencyPresetSnapshot();
            lite.tierMultipliers[DiaryFrequencyTiers.Routine] = 0.3f;
            lite.tierMultipliers[DiaryFrequencyTiers.Ambient] = 0.15f;
            List<DiaryFrequencyGroupSnapshot> known = new List<DiaryFrequencyGroupSnapshot>
            {
                new DiaryFrequencyGroupSnapshot
                {
                    groupKey = "workRoutine",
                    frequencyTier = DiaryFrequencyTiers.Routine
                },
                new DiaryFrequencyGroupSnapshot
                {
                    groupKey = "smalltalk",
                    frequencyTier = DiaryFrequencyTiers.Ambient
                }
            };
            Dictionary<string, float> raw = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { " workroutine ", 0.3f },
                { "SMALLTALK", 0.2f },
                { "futureGroup", float.PositiveInfinity },
                { "futureNan", float.NaN },
                { " ", 4f }
            };
            Dictionary<string, float> normalized =
                DiaryFrequencySettingsPolicy.NormalizeOverrides(
                    raw,
                    known,
                    lite,
                    resparsifyKnownValues: true);
            AssertTrue("known override equal to preset is re-sparsified",
                !normalized.ContainsKey("workRoutine"));
            AssertNear("known custom override canonicalizes key", 0.2f,
                normalized["smalltalk"]);
            AssertNear("unknown future key is preserved and positive infinity clamps", 5f,
                normalized["futureGroup"]);
            AssertNear("unknown future NaN is preserved as safe Standard", 1f,
                normalized["futureNan"]);
            AssertEqual("blank override key is dropped", 3, normalized.Count);

            Dictionary<string, float> unresolvedPresetValues =
                DiaryFrequencySettingsPolicy.NormalizeOverrides(
                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "workRoutine", 1f },
                        { "smalltalk", float.NaN }
                    },
                    known,
                    new DiaryFrequencyPresetSnapshot(),
                    resparsifyKnownValues: false);
            AssertNear("unresolved preset retains an explicit Standard-equivalent override", 1f,
                unresolvedPresetValues["workRoutine"]);
            AssertNear("unresolved preset normalizes but retains corrupt known intent", 1f,
                unresolvedPresetValues["smalltalk"]);
            AssertEqual("unresolved preset cleanup never destructively re-sparsifies known rows", 2,
                unresolvedPresetValues.Count);

            AssertNear("negative infinity clamps to zero", 0f,
                DiaryFrequencySettingsPolicy.NormalizeMultiplier(
                    float.NegativeInfinity,
                    1f));
            AssertNear("finite high value clamps to maximum", 5f,
                DiaryFrequencySettingsPolicy.NormalizeMultiplier(99f, 1f));
            AssertNear("NaN uses inherited preset value", 0.3f,
                DiaryFrequencySettingsPolicy.NormalizeMultiplier(float.NaN, 0.3f));

            TestFrequencySettingsDefLoadOrderingContract();
        }

        private static void TestFrequencySettingsDefLoadOrderingContract()
        {
            string settingsSource = File.ReadAllText(RepoPath(
                "Source", "Settings", "PawnDiarySettings.cs"));
            string integrationSettingsSource = File.ReadAllText(RepoPath(
                "Source", "Settings", "IntegrationApiSettings.cs"));
            string startupSource = File.ReadAllText(RepoPath(
                "Source", "Patches", "DiaryModStartup.cs"));
            AssertContains(
                "pre-Def settings load retains raw legacy frequency presence",
                settingsSource,
                "pendingLegacyFrequencyMigration = legacyFrequency");
            AssertContains(
                "an early settings write preserves deferred legacy split keys",
                settingsSource,
                "ExposePendingLegacyFrequencySettings(pendingLegacyFrequencyMigration)");
            AssertContains(
                "an early write preserves explicit default-valued legacy-key presence",
                settingsSource,
                "forceSave: true");
            AssertTrue(
                "settings load never migrates directly before Def readiness",
                !settingsSource.Contains("MigrateLegacyFrequencySettings(legacyFrequency)"));
            AssertTrue(
                "frequency settings code never initializes the permanent interaction catalog cache",
                !settingsSource.Contains("InteractionGroups.All"));
            AssertContains(
                "post-Def startup completes deferred frequency migration",
                startupSource,
                "TryFinalizeFrequencySettingsAfterDefsLoaded()");
            AssertTrue(
                "saved Advanced promotion policy is applied before legacy Social migration",
                settingsSource.IndexOf("AdvancedFieldCatalog.EnsureApplied(advancedOverrides)",
                        settingsSource.IndexOf("TryFinalizeFrequencySettingsAfterDefsLoaded()",
                            StringComparison.Ordinal),
                        StringComparison.Ordinal)
                    < settingsSource.IndexOf("MigrateLegacyFrequencySettings(legacy)",
                        StringComparison.Ordinal));
            AssertContains(
                "post-Def frequency finalization also invokes independent enable cleanup",
                settingsSource,
                "TryFinalizeGroupEnabledSettingsAfterDefsLoaded();");
            AssertContains(
                "enable cleanup has an independent completion latch",
                settingsSource,
                "groupEnabledDefBackedNormalizationComplete");
            AssertContains(
                "deferred migration waits for the complete Advanced Def snapshot",
                settingsSource,
                "AdvancedFieldCatalog.DefaultsCaptured");
            AssertFrequencyWriteValidationPrecedesFinalization(
                integrationSettingsSource,
                "public static bool TrySetEventFrequencyMultiplier(",
                "invalid frequency-set tokens are rejected before lazy settings finalization");
            AssertFrequencyWriteValidationPrecedesFinalization(
                integrationSettingsSource,
                "public static bool TryResetEventFrequencyMultiplier(",
                "invalid frequency-reset tokens are rejected before lazy settings finalization");
        }

        private static void AssertFrequencyWriteValidationPrecedesFinalization(
            string source,
            string methodSignature,
            string assertionName)
        {
            int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
            int readinessCheck = methodStart < 0
                ? -1
                : source.IndexOf(
                    "PawnDiarySettings.FrequencyDefinitionsReady()",
                    methodStart,
                    StringComparison.Ordinal);
            int groupValidation = methodStart < 0
                ? -1
                : source.IndexOf(
                    "SettingsFrequencyGroup(key)",
                    methodStart,
                    StringComparison.Ordinal);
            int finalization = methodStart < 0
                ? -1
                : source.IndexOf(
                    "settings.TryFinalizeFrequencySettingsAfterDefsLoaded()",
                    methodStart,
                    StringComparison.Ordinal);

            AssertTrue(
                assertionName,
                methodStart >= 0
                    && readinessCheck > methodStart
                    && groupValidation > readinessCheck
                    && finalization > groupValidation);
        }

        private static DiaryFrequencyMigrationGroupSnapshot MigrationGroup(
            string key,
            bool work = false,
            bool ability = false,
            bool promotion = false)
        {
            return new DiaryFrequencyMigrationGroupSnapshot
            {
                groupKey = key,
                affectedByWorkWeight = work,
                affectedByAbilityWeight = ability,
                affectedByInteractionPromotionWeight = promotion
            };
        }
    }
}
