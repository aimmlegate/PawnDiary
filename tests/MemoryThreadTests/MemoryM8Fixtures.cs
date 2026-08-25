// Pure Phase M8 settings fixtures. The production draft is linked directly into this no-Verse
// harness so dependency transitions and hostile numeric text are proven without opening RimWorld.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace MemoryThreadTests
{
    internal static class MemoryM8Fixtures
    {
        private static int assertions;

        public static int Run()
        {
            assertions = 0;
            ParentChildAndIndependentSwitches();
            CategoryDraftsAreIndependentAndDetached();
            NumericBuffersAreBoundedAndLastValid();
            NumericBoundsAndLifetimeRelationship();
            FinalizationParsesTheCompleteDraftAtomically();
            LocalizationAndUiBoundaryContracts();
            return assertions;
        }

        private static readonly string[] M8LocalizationKeys =
        {
            "PawnDiary.Memory.Settings.Page",
            "PawnDiary.Memory.Settings.SaveNew",
            "PawnDiary.Memory.Settings.SaveNew.Desc",
            "PawnDiary.Memory.Settings.UseWriting",
            "PawnDiary.Memory.Settings.UseWriting.Desc",
            "PawnDiary.Memory.Settings.UseBackground",
            "PawnDiary.Memory.Settings.UseBackground.Desc",
            "PawnDiary.Memory.Settings.AllowExtra",
            "PawnDiary.Memory.Settings.AllowExtra.Desc",
            "PawnDiary.Memory.Settings.Occasional",
            "PawnDiary.Memory.Settings.Occasional.Desc",
            "PawnDiary.Memory.Settings.OpenLibrary",
            "PawnDiary.Memory.Settings.NoGame",
            "PawnDiary.Memory.Settings.Privacy",
            "PawnDiary.Memory.Settings.ExtraCost",
            "PawnDiary.Memory.Settings.Disabled.UseWriting",
            "PawnDiary.Memory.Settings.Disabled.AllowExtra",
            "PawnDiary.Memory.Settings.Compatibility",
            "PawnDiary.Memory.Settings.Categories.Header",
            "PawnDiary.Memory.Settings.Category.Personal",
            "PawnDiary.Memory.Settings.Category.Relationships",
            "PawnDiary.Memory.Settings.Category.Family",
            "PawnDiary.Memory.Settings.Category.Factions",
            "PawnDiary.Memory.Settings.Category.Desc",
            "PawnDiary.Memory.Settings.Retention.Header",
            "PawnDiary.Memory.Settings.Retention.Minor",
            "PawnDiary.Memory.Settings.Retention.Regular",
            "PawnDiary.Memory.Settings.Retention.ThreadTarget",
            "PawnDiary.Memory.Settings.Retention.DayUnit",
            "PawnDiary.Memory.Settings.Retention.DaySuffix",
            "PawnDiary.Memory.Settings.Retention.AgeHelp",
            "PawnDiary.Memory.Settings.Retention.ThreadHelp",
            "PawnDiary.Memory.Settings.Retention.InvalidOrder",
            "PawnDiary.Memory.Settings.Retention.Important",
            "PawnDiary.Memory.Settings.Retention.Edited",
            "PawnDiary.Memory.Settings.Repetition.Header",
            "PawnDiary.Memory.Settings.Repetition.Days",
            "PawnDiary.Memory.Settings.Repetition.Entries",
            "PawnDiary.Memory.Settings.Repetition.Help",
            "PawnDiary.Memory.SettingsSaveFailed",
            "PawnDiary.Memory.SettingsFutureVersion"
        };

        private static void ParentChildAndIndependentSwitches()
        {
            MemorySettingsPolicyFieldsV1 fields = new MemorySettingsPolicyFieldsV1
            {
                saveNewMemories = true,
                useMemoriesInWriting = true,
                usePawnBackground = true,
                allowExtraMemoryAiRequests = true,
                occasionalMemoryReflections = true
            };
            MemorySettingsDraft draft = Draft(fields);

            draft.SetSaveNewMemories(false);
            Equal("m8.save.independent.use", true, draft.UseMemoriesInWriting);
            Equal("m8.save.independent.extra", true, draft.AllowExtraMemoryAiRequests);
            draft.SetUsePawnBackground(false);
            Equal("m8.background.independent.save", false, draft.SaveNewMemories);
            Equal("m8.background.independent.use", true, draft.UseMemoriesInWriting);

            draft.SetUseMemoriesInWriting(false);
            Equal("m8.use.off.extra", false, draft.AllowExtraMemoryAiRequests);
            Equal("m8.use.off.quiet", false, draft.OccasionalMemoryReflections);
            draft.SetUseMemoriesInWriting(true);
            Equal("m8.use.on.no-extra-restore", false, draft.AllowExtraMemoryAiRequests);
            Equal("m8.use.on.no-quiet-restore", false, draft.OccasionalMemoryReflections);

            draft.SetAllowExtraMemoryAiRequests(true);
            draft.SetOccasionalMemoryReflections(true);
            Equal("m8.quiet.parents.on", true, draft.OccasionalMemoryReflections);
            draft.SetAllowExtraMemoryAiRequests(false);
            Equal("m8.extra.off.quiet", false, draft.OccasionalMemoryReflections);
            draft.SetAllowExtraMemoryAiRequests(true);
            Equal("m8.extra.on.no-quiet-restore", false, draft.OccasionalMemoryReflections);
            draft.SetUseMemoriesInWriting(false);
            draft.SetAllowExtraMemoryAiRequests(true);
            draft.SetOccasionalMemoryReflections(true);
            Equal("m8.disabled-child.setter.extra", false, draft.AllowExtraMemoryAiRequests);
            Equal("m8.disabled-child.setter.quiet", false, draft.OccasionalMemoryReflections);
        }

        private static void CategoryDraftsAreIndependentAndDetached()
        {
            MemorySettingsPolicyFieldsV1 source = new MemorySettingsPolicyFieldsV1
            {
                memoryCategoryMask = MemoryCategoryBits.KnownMask,
                captureInvalidationGenerationPersonal = 10,
                captureInvalidationGenerationRelationships = 20,
                captureInvalidationGenerationFamily = 30,
                captureInvalidationGenerationFactions = 40,
                optionalRequestInvalidationGeneration = 50
            };
            MemoryPolicySnapshot snapshot = MemoryPolicyNormalizer.Normalize(
                1, source, new MemorySettingsBounds());
            MemorySettingsDraft draft = MemorySettingsDraft.FromSnapshot(
                snapshot, new MemorySettingsBounds());
            draft.SetSaveNewMemories(false);
            draft.SetUseMemoriesInWriting(false);
            draft.SetCategoryEnabled(MemoryCategoryBits.Relationships, false);
            draft.SetCategoryEnabled(MemoryCategoryBits.Factions, false);

            Equal("m8.category.personal", true,
                draft.CategoryEnabled(MemoryCategoryBits.Personal));
            Equal("m8.category.relationships", false,
                draft.CategoryEnabled(MemoryCategoryBits.Relationships));
            Equal("m8.category.family", true,
                draft.CategoryEnabled(MemoryCategoryBits.Family));
            Equal("m8.category.factions", false,
                draft.CategoryEnabled(MemoryCategoryBits.Factions));
            Equal("m8.category.unknown", false, draft.CategoryEnabled(16));
            draft.SetCategoryEnabled(16, true);

            MemorySettingsPolicyFieldsV1 preview = draft.PreviewFields();
            Equal("m8.category.mask", MemoryCategoryBits.Personal | MemoryCategoryBits.Family,
                preview.memoryCategoryMask);
            Equal("m8.category.generation.personal", 10L,
                preview.captureInvalidationGenerationPersonal);
            Equal("m8.category.generation.optional", 50L,
                preview.optionalRequestInvalidationGeneration);
            Equal("m8.detached.source.mask", MemoryCategoryBits.KnownMask,
                snapshot.memoryCategoryMask);
            preview.memoryCategoryMask = 0;
            Equal("m8.detached.preview-copy", true,
                draft.CategoryEnabled(MemoryCategoryBits.Personal));
        }

        private static void NumericBuffersAreBoundedAndLastValid()
        {
            MemorySettingsDraft draft = Draft(new MemorySettingsPolicyFieldsV1());
            MemorySettingsBounds bounds = new MemorySettingsBounds();

            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays, string.Empty);
            draft.CompleteNumericEdit(MemoryNumericSettingKeys.MinorLifetimeDays, bounds);
            Equal("m8.numeric.blank.last-valid", "15",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));

            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays, "12a");
            draft.CompleteNumericEdit(MemoryNumericSettingKeys.MinorLifetimeDays, bounds);
            Equal("m8.numeric.mixed.last-valid", "15",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));

            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.ThreadTarget,
                new string('9', MemorySettingsDraft.NumericDraftUtf16Units + 1));
            Equal("m8.numeric.cap", MemorySettingsDraft.NumericDraftUtf16Units,
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget).Length);
            draft.CompleteNumericEdit(MemoryNumericSettingKeys.ThreadTarget, bounds);
            Equal("m8.numeric.overflow.clamp", "64",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));

            string boundary = new string('1', 31) + "\U0001F600";
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays, boundary);
            string retained = draft.NumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays);
            Equal("m8.numeric.surrogate.cap", 31, retained.Length);
            True("m8.numeric.surrogate.complete",
                retained.Length == 0 || !char.IsHighSurrogate(retained[retained.Length - 1]));
            draft.CompleteNumericEdit(MemoryNumericSettingKeys.RegularLifetimeDays, bounds);
            Equal("m8.numeric.surrogate.numeric-clamp", "3600",
                draft.NumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays));

            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.ReuseDays, "5\uDC00");
            Equal("m8.numeric.malformed.repaired", "5\uFFFD",
                draft.NumericBuffer(MemoryNumericSettingKeys.ReuseDays));
            draft.CompleteNumericEdit(MemoryNumericSettingKeys.ReuseDays, bounds);
            Equal("m8.numeric.malformed.last-valid", "5",
                draft.NumericBuffer(MemoryNumericSettingKeys.ReuseDays));
        }

        private static void NumericBoundsAndLifetimeRelationship()
        {
            MemorySettingsDraft draft = Draft(new MemorySettingsPolicyFieldsV1());
            MemorySettingsBounds bounds = new MemorySettingsBounds();

            Complete(draft, MemoryNumericSettingKeys.MinorLifetimeDays, "0", bounds);
            Equal("m8.numeric.minor.low", "1",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));
            Complete(draft, MemoryNumericSettingKeys.MinorLifetimeDays, "-9999999999999999999999999999999", bounds);
            Equal("m8.numeric.minor.negative", "1",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));
            Complete(draft, MemoryNumericSettingKeys.RegularLifetimeDays, "3601", bounds);
            Equal("m8.numeric.regular.high", "3600",
                draft.NumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays));
            Complete(draft, MemoryNumericSettingKeys.ThreadTarget, "+12", bounds);
            Equal("m8.numeric.target.explicit-positive", "12",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));
            Complete(draft, MemoryNumericSettingKeys.ThreadTarget, "-1", bounds);
            Equal("m8.numeric.target.negative", "4",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));
            Complete(draft, MemoryNumericSettingKeys.ThreadTarget, "-x", bounds);
            Equal("m8.numeric.target.signed-nonnumeric", "4",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));
            Complete(draft, MemoryNumericSettingKeys.ThreadTarget, "3", bounds);
            Equal("m8.numeric.target.low", "4",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));
            Complete(draft, MemoryNumericSettingKeys.ThreadTarget, "65", bounds);
            Equal("m8.numeric.target.high", "64",
                draft.NumericBuffer(MemoryNumericSettingKeys.ThreadTarget));
            Complete(draft, MemoryNumericSettingKeys.RevisitEntries, "99999999999999999999999999999999", bounds);
            Equal("m8.numeric.revisit.overflow", "1000",
                draft.NumericBuffer(MemoryNumericSettingKeys.RevisitEntries));

            Complete(draft, MemoryNumericSettingKeys.RegularLifetimeDays, "20", bounds);
            Complete(draft, MemoryNumericSettingKeys.MinorLifetimeDays, "21", bounds);
            Equal("m8.numeric.order.pending-minor", "21",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));
            Equal("m8.numeric.order.warning", true, draft.invalidLifetimeOrderWarning);
            Complete(draft, MemoryNumericSettingKeys.RegularLifetimeDays, "30", bounds);
            Equal("m8.numeric.order.pending-regular", "30",
                draft.NumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays));
            Equal("m8.numeric.order.warning-cleared", false,
                draft.invalidLifetimeOrderWarning);
            Complete(draft, MemoryNumericSettingKeys.RegularLifetimeDays, "20", bounds);
            MemorySettingsPolicyFieldsV1 repaired = draft.BuildCommitFields(bounds);
            Equal("m8.numeric.order.repaired-at-save", 20,
                repaired.minorMemoryLifetimeDays);
            Equal("m8.numeric.order.repaired-regular", 20,
                repaired.regularMemoryLifetimeDays);
            Equal("m8.numeric.order.repaired-warning", true,
                draft.invalidLifetimeOrderWarning);
            MemorySettingsPolicyFieldsV1 repeatedRepair = draft.BuildCommitFields(bounds);
            Equal("m8.numeric.order.repair-idempotent", 20,
                repeatedRepair.minorMemoryLifetimeDays);
            Equal("m8.numeric.order.warning-idempotent", true,
                draft.invalidLifetimeOrderWarning);
        }

        private static void FinalizationParsesTheCompleteDraftAtomically()
        {
            MemorySettingsPolicyFieldsV1 source = new MemorySettingsPolicyFieldsV1
            {
                minorMemoryLifetimeDays = 15,
                regularMemoryLifetimeDays = 60,
                captureInvalidationGenerationPersonal = 9,
                optionalRequestInvalidationGeneration = 11
            };
            MemorySettingsDraft draft = Draft(source);
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays, "100");
            draft.CompleteNumericEdit(
                MemoryNumericSettingKeys.MinorLifetimeDays,
                new MemorySettingsBounds());
            Equal("m8.final.focus.minor-preserved", "100",
                draft.NumericBuffer(MemoryNumericSettingKeys.MinorLifetimeDays));
            Equal("m8.final.focus.interim-warning", true,
                draft.invalidLifetimeOrderWarning);
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays, "200");
            draft.CompleteNumericEdit(
                MemoryNumericSettingKeys.RegularLifetimeDays,
                new MemorySettingsBounds());
            Equal("m8.final.focus.regular-preserved", "200",
                draft.NumericBuffer(MemoryNumericSettingKeys.RegularLifetimeDays));
            Equal("m8.final.focus.warning-cleared", false,
                draft.invalidLifetimeOrderWarning);
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.ThreadTarget, "32");
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.ReuseDays, "9");
            draft.UpdateNumericBuffer(MemoryNumericSettingKeys.RevisitEntries, "7");
            MemorySettingsPolicyFieldsV1 committed = draft.BuildCommitFields(
                new MemorySettingsBounds());
            Equal("m8.final.minor", 100, committed.minorMemoryLifetimeDays);
            Equal("m8.final.regular", 200, committed.regularMemoryLifetimeDays);
            Equal("m8.final.target", 32, committed.memoryThreadTarget);
            Equal("m8.final.reuse", 9, committed.memoryReuseDays);
            Equal("m8.final.revisit", 7, committed.memoryRevisitEntryCount);
            Equal("m8.final.generation.capture", 9L,
                committed.captureInvalidationGenerationPersonal);
            Equal("m8.final.generation.optional", 11L,
                committed.optionalRequestInvalidationGeneration);
            MemorySettingsPolicyFieldsV1 repeated = draft.BuildCommitFields(
                new MemorySettingsBounds());
            Equal("m8.final.idempotent", committed.minorMemoryLifetimeDays,
                repeated.minorMemoryLifetimeDays);
        }

        private static void LocalizationAndUiBoundaryContracts()
        {
            string root = Program.RepoRoot();
            Dictionary<string, string> english = ReadKeyed(
                Path.Combine(root, "Languages", "English", "Keyed", "PawnDiary.xml"));
            Dictionary<string, string> russian = ReadKeyed(
                Path.Combine(root, "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            foreach (string key in M8LocalizationKeys)
            {
                True("m8.localization.english." + key,
                    english.ContainsKey(key) && !string.IsNullOrWhiteSpace(english[key]));
                True("m8.localization.russian." + key,
                    russian.ContainsKey(key) && !string.IsNullOrWhiteSpace(russian[key]));
            }
            Equal("m8.localization.day-placeholder.en", true,
                english["PawnDiary.Memory.Settings.Retention.DayUnit"].Contains("{0}"));
            Equal("m8.localization.day-placeholder.ru", true,
                russian["PawnDiary.Memory.Settings.Retention.DayUnit"].Contains("{0}"));
            Equal("m8.localization.day-suffix.ru", "дн.",
                russian["PawnDiary.Memory.Settings.Retention.DaySuffix"]);
            ContainsIgnoreCase(
                "m8.copy.use.culture",
                english["PawnDiary.Memory.Settings.UseWriting.Desc"],
                "cultural references");
            ContainsIgnoreCase(
                "m8.copy.use.independent",
                english["PawnDiary.Memory.Settings.UseWriting.Desc"],
                "tracking culture stay independent");
            ContainsIgnoreCase(
                "m8.copy.privacy.identity",
                english["PawnDiary.Memory.Settings.Privacy"],
                "culture identity");
            ContainsIgnoreCase(
                "m8.copy.privacy.xml",
                english["PawnDiary.Memory.Settings.Privacy"],
                "XML-authored cultural interpretations");

            string settingsWindow = File.ReadAllText(Path.Combine(
                root, "Source", "Settings", "PawnDiaryMod.SettingsWindow.cs"));
            ContainsIgnoreCase("m8.ui.release-gate", settingsWindow,
                "MemorySystemActivationGate.IsCurrentRelease");
            int applyPendingIndex = settingsWindow.IndexOf(
                "apiConnectionController.ApplyPendingResults()",
                StringComparison.Ordinal);
            int tuningDispatchIndex = settingsWindow.IndexOf(
                "if (settingsTab == PawnDiarySettingsTab.Tuning)",
                StringComparison.Ordinal);
            True("m8.ui.tuning-drains-api-results",
                applyPendingIndex >= 0 && tuningDispatchIndex > applyPendingIndex);
            Equal("m8.ui.tab-label-keeps-line-height", false,
                settingsWindow.Contains("LabelFit(rect.ContractedBy(6f)"));
            string memoryUi = File.ReadAllText(Path.Combine(
                root, "Source", "Settings", "PawnDiaryMod.MemorySettingsTab.cs"));
            ContainsIgnoreCase("m8.ui.detached-draft", memoryUi, "MemorySettingsDraft");
            Equal("m8.ui.no-component-mutation", false,
                memoryUi.Contains("DiaryGameComponent.Instance"));
            Equal("m8.ui.no-settings-commit", false,
                memoryUi.Contains("PersistSettingsImmediately"));
            Equal("m8.ui.advanced-label-keeps-line-height", false,
                memoryUi.Contains("LabelFit(rect.ContractedBy(6f)"));
            ContainsIgnoreCase("m8.ui.no-game-state", memoryUi, "ProgramState.Playing");
            ContainsIgnoreCase("m8.ui.no-game-object", memoryUi, "Current.Game != null");

            string modSource = File.ReadAllText(Path.Combine(
                root, "Source", "Settings", "PawnDiaryMod.cs"));
            ContainsIgnoreCase("m8.commit.window-close", modSource, "WriteSettingsCore(true)");
            ContainsIgnoreCase("m8.commit.programmatic-detached", modSource,
                "WriteSettingsCore(false)");
            Equal("m8.commit.window-close-single-boundary", 1,
                CountOccurrences(modSource, "WriteSettingsCore(true)"));
            Equal("m8.commit.programmatic-single-boundary", 1,
                CountOccurrences(modSource, "WriteSettingsCore(false)"));
            ContainsIgnoreCase("m8.commit.drains-api-results", modSource,
                "apiConnectionController.ApplyPendingResults()");
            int durableWriteIndex = modSource.IndexOf(
                "MemorySettingsDurableWriter.TryWrite(", StringComparison.Ordinal);
            int failedWriteIndex = modSource.IndexOf(
                "if (!write.persisted)", StringComparison.Ordinal);
            int consumeDraftIndex = modSource.IndexOf(
                "memorySettingsDraft = null;", StringComparison.Ordinal);
            int publishPolicyIndex = modSource.IndexOf(
                "MemoryEffectivePolicyProvider.Publish(memoryCommit.snapshot)",
                StringComparison.Ordinal);
            int reconcilePolicyIndex = modSource.IndexOf(
                "ReconcilePublishedMemoryPolicy(", StringComparison.Ordinal);
            True("m8.commit.persistence-before-draft-consumption",
                durableWriteIndex >= 0
                && failedWriteIndex > durableWriteIndex
                && consumeDraftIndex > failedWriteIndex);
            True("m8.commit.publish-after-draft-consumption",
                publishPolicyIndex > consumeDraftIndex);
            True("m8.commit.reconcile-after-publication",
                reconcilePolicyIndex > publishPolicyIndex);
            ContainsIgnoreCase(
                "m8.library.callback-game-boundary-reset",
                File.ReadAllText(Path.Combine(root, "Source", "Core", "DiaryGameComponent.cs")),
                "PawnDiaryMod.ResetMemoryLibraryAction()");

            XDocument style = XDocument.Load(Path.Combine(
                root, "1.6", "Defs", "DiaryUiStyleDef.xml"));
            XElement styleDef = style.Root?.Element("PawnDiary.DiaryUiStyleDef");
            string[] styleKeys =
            {
                "settingsMemoryControlHeight",
                "settingsMemoryCheckboxSize",
                "settingsMemorySectionGap",
                "settingsMemoryBlockGap",
                "settingsMemoryChildIndent",
                "settingsMemoryNumericFieldWidth",
                "settingsMemoryOpenLibraryButtonWidth",
                "settingsMemoryAdvancedSelectorHeight",
                "settingsMemoryAdvancedSelectorGap",
                "settingsMemoryWarningText"
            };
            foreach (string key in styleKeys)
                True("m8.ui-style." + key, styleDef?.Element(key) != null);
        }

        private static Dictionary<string, string> ReadKeyed(string path)
        {
            XElement root = XDocument.Load(path).Root
                ?? throw new InvalidOperationException("Missing LanguageData root: " + path);
            return root.Elements().ToDictionary(
                element => element.Name.LocalName,
                element => element.Value.Trim(),
                StringComparer.Ordinal);
        }

        private static void ContainsIgnoreCase(string name, string source, string expected)
        {
            assertions++;
            if ((source ?? string.Empty).IndexOf(
                    expected ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    name + ": expected text containing " + expected + ", got " + source);
            }
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while (source != null && value != null
                && value.Length > 0
                && (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static MemorySettingsDraft Draft(MemorySettingsPolicyFieldsV1 fields)
        {
            MemoryPolicySnapshot snapshot = MemoryPolicyNormalizer.Normalize(
                1, fields, new MemorySettingsBounds());
            return MemorySettingsDraft.FromSnapshot(snapshot, new MemorySettingsBounds());
        }

        private static void Complete(
            MemorySettingsDraft draft,
            string key,
            string raw,
            MemorySettingsBounds bounds)
        {
            draft.UpdateNumericBuffer(key, raw);
            draft.CompleteNumericEdit(key, bounds);
        }

        private static void Equal<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!Equals(expected, actual))
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
        }

        private static void True(string name, bool value)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(name + ": expected true");
        }
    }
}
