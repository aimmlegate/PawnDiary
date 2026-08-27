// Pure Phase M5 settings fixtures. These compile the production normalizer/provider without Verse and
// exercise migration, dependency, generation, fail-closed, and deferred-reconciliation boundaries.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace MemoryThreadTests
{
    internal static class MemoryM5Fixtures
    {
        private static int assertions;

        public static int Run()
        {
            assertions = 0;
            BoundsAndNumericNormalization();
            VersionZeroMigration();
            DependenciesAndCategoryGates();
            CommitGenerationsAreExactAndSticky();
            FutureVersionsFailClosed();
            ReconciliationIsIdempotentAndBounded();
            DurableSettingsPredecessorRules();
            PublicationIsIndivisible();
            LibraryUnicodeCursorAndFingerprintRules();
            LibraryDefensiveCeilingParityAndDirectoryPriority();
            LibraryFilteringPagingAndTtlRules();
            LibraryIndexVisibilityAndCounts();
            LibraryProjectionAndDetailReviewRules();
            LibraryHandleAndRevisionRules();
            LibraryMutationRules();
            return assertions;
        }

        private static void BoundsAndNumericNormalization()
        {
            MemorySettingsBounds malformed = new MemorySettingsBounds
            {
                minorMinimumDays = 999999,
                minorDefaultDays = -5,
                minorMaximumDays = 0,
                regularMinimumDays = 50,
                regularDefaultDays = 2,
                regularMaximumDays = 20,
                threadTargetMinimum = 100,
                threadTargetDefault = -1,
                threadTargetMaximum = 2,
                reuseMinimumDays = 40000,
                reuseDefaultDays = 0,
                reuseMaximumDays = int.MaxValue,
                revisitMinimumEntries = int.MaxValue,
                revisitDefaultEntries = -1,
                revisitMaximumEntries = 0
            };
            MemorySettingsBounds bounds = MemoryPolicyNormalizer.NormalizeBounds(malformed);
            Equal("m5.bounds.minor", 1, bounds.minorMinimumDays);
            Equal("m5.bounds.minor.max", 1, bounds.minorMaximumDays);
            Equal("m5.bounds.regular.min", 20, bounds.regularMinimumDays);
            Equal("m5.bounds.regular.default", 20, bounds.regularDefaultDays);
            Equal("m5.bounds.target.min", 4, bounds.threadTargetMinimum);
            Equal("m5.bounds.target.max", 4, bounds.threadTargetMaximum);
            Equal("m5.bounds.reuse.max", 35791, bounds.reuseMaximumDays);
            Equal("m5.bounds.revisit.min", 1, bounds.revisitMinimumEntries);

            MemorySettingsPolicyFieldsV1 fields = new MemorySettingsPolicyFieldsV1
            {
                minorMemoryLifetimeDays = 100,
                regularMemoryLifetimeDays = 10,
                memoryThreadTarget = 1000,
                memoryReuseDays = -1,
                memoryRevisitEntryCount = int.MaxValue,
                memoryCategoryMask = -1,
                captureInvalidationGenerationPersonal = 0
            };
            MemoryPolicySnapshot normalized = MemoryPolicyNormalizer.Normalize(1, fields,
                new MemorySettingsBounds());
            Equal("m5.normalize.minor.le.regular", 10, normalized.minorMemoryLifetimeDays);
            Equal("m5.normalize.target", 64, normalized.memoryThreadTarget);
            Equal("m5.normalize.reuse", 1, normalized.memoryReuseDays);
            Equal("m5.normalize.revisit", 1000, normalized.memoryRevisitEntryCount);
            Equal("m5.normalize.mask", 15, normalized.memoryCategoryMask);
            Equal("m5.normalize.generation", 1L,
                normalized.captureInvalidationGenerationPersonal);
            Equal("m5.normalize.ticks", 600000L, normalized.minorMemoryLifetimeTicks);
        }

        private static void VersionZeroMigration()
        {
            for (int mode = 0; mode <= 3; mode++)
            {
                MemorySettingsPolicyFieldsV1 on = MemoryPolicyNormalizer.MigrateVersionZero(
                    true, mode, new MemorySettingsBounds());
                Equal("m5.migrate.mode." + mode, mode == 0 || mode == 1,
                    on.useMemoriesInWriting);
                Equal("m5.migrate.extra." + mode, false, on.allowExtraMemoryAiRequests);
                Equal("m5.migrate.categories." + mode, 15, on.memoryCategoryMask);
                Equal("m5.migrate.ttl." + mode, 15, on.minorMemoryLifetimeDays);
            }
            MemorySettingsPolicyFieldsV1 masterOff = MemoryPolicyNormalizer.MigrateVersionZero(
                false, 0, new MemorySettingsBounds());
            Equal("m5.migrate.master.off", false, masterOff.useMemoriesInWriting);
            Equal("m5.migrate.save.independent", true, masterOff.saveNewMemories);
            Equal("m5.migrate.background.independent", true, masterOff.usePawnBackground);

            MemorySettingsBounds xmlBounds = new MemorySettingsBounds
            {
                minorMinimumDays = 2,
                minorDefaultDays = 7,
                minorMaximumDays = 30,
                regularMinimumDays = 8,
                regularDefaultDays = 45,
                regularMaximumDays = 90,
                threadTargetMinimum = 5,
                threadTargetDefault = 17,
                threadTargetMaximum = 40,
                reuseMinimumDays = 2,
                reuseDefaultDays = 6,
                reuseMaximumDays = 20,
                revisitMinimumEntries = 2,
                revisitDefaultEntries = 8,
                revisitMaximumEntries = 20
            };
            MemorySettingsPolicyFieldsV1 deferred = MemoryPolicyNormalizer.MigrateVersionZero(
                true, 0, xmlBounds);
            Equal("m5.migrate.deferred.xml.minor", 7, deferred.minorMemoryLifetimeDays);
            Equal("m5.migrate.deferred.xml.regular", 45, deferred.regularMemoryLifetimeDays);
            Equal("m5.migrate.deferred.xml.target", 17, deferred.memoryThreadTarget);
        }

        private static void DependenciesAndCategoryGates()
        {
            MemorySettingsPolicyFieldsV1 values = new MemorySettingsPolicyFieldsV1
            {
                useMemoriesInWriting = false,
                allowExtraMemoryAiRequests = true,
                occasionalMemoryReflections = true,
                memoryCategoryMask = MemoryCategoryBits.Personal | MemoryCategoryBits.Family
            };
            MemoryPolicySnapshot off = MemoryPolicyNormalizer.Normalize(1, values,
                new MemorySettingsBounds());
            Equal("m5.dependency.extra", false, off.allowExtraMemoryAiRequests);
            Equal("m5.dependency.quiet", false, off.occasionalMemoryReflections);
            Equal("m5.recall.use.off", false, off.AllowsRecall(MemoryCategoryBits.Personal));
            Equal("m5.capture.personal", true, off.AllowsCapture(MemoryCategoryBits.Personal));
            Equal("m5.capture.family", true, off.AllowsCapture(MemoryCategoryBits.Family));
            Equal("m5.capture.relationships", false,
                off.AllowsCapture(MemoryCategoryBits.Relationships));

            values.useMemoriesInWriting = true;
            values.allowExtraMemoryAiRequests = true;
            values.occasionalMemoryReflections = true;
            MemoryPolicySnapshot on = MemoryPolicyNormalizer.Normalize(1, values,
                new MemorySettingsBounds());
            Equal("m5.optional.on", true, on.AllowsOptionalRequests);
            Equal("m5.quiet.on", true, on.AllowsOccasionalReflections);
            Equal("m5.recall.family", true, on.AllowsRecall(MemoryCategoryBits.Family));
            Equal("m5.recall.unknown", false, on.AllowsRecall(16));
        }

        private static void CommitGenerationsAreExactAndSticky()
        {
            MemorySettingsPolicyFieldsV1 prior = new MemorySettingsPolicyFieldsV1
            {
                allowExtraMemoryAiRequests = true,
                optionalRequestInvalidationGeneration = 7,
                captureInvalidationGenerationPersonal = 10,
                captureInvalidationGenerationRelationships = 20,
                captureInvalidationGenerationFamily = 30,
                captureInvalidationGenerationFactions = 40
            };
            MemorySettingsPolicyFieldsV1 draft = MemoryPolicyNormalizer.Copy(prior);
            draft.saveNewMemories = false;
            draft.allowExtraMemoryAiRequests = false;
            MemorySettingsCommitPlan plan = MemoryPolicyNormalizer.PrepareCommit(
                1, prior, draft, new MemorySettingsBounds());
            Equal("m5.commit.valid", true, plan.valid);
            Equal("m5.commit.mask", 15, plan.changedCaptureMask);
            Equal("m5.commit.personal", 11L,
                plan.candidate.captureInvalidationGenerationPersonal);
            Equal("m5.commit.relationships", 21L,
                plan.candidate.captureInvalidationGenerationRelationships);
            Equal("m5.commit.family", 31L,
                plan.candidate.captureInvalidationGenerationFamily);
            Equal("m5.commit.factions", 41L,
                plan.candidate.captureInvalidationGenerationFactions);
            Equal("m5.commit.optional", 8L,
                plan.candidate.optionalRequestInvalidationGeneration);

            MemorySettingsCommitPlan retry = MemoryPolicyNormalizer.PrepareCommit(
                1, plan.candidate, plan.candidate, new MemorySettingsBounds());
            Equal("m5.commit.retry.capture", 11L,
                retry.candidate.captureInvalidationGenerationPersonal);
            Equal("m5.commit.retry.optional", 8L,
                retry.candidate.optionalRequestInvalidationGeneration);

            MemorySettingsPolicyFieldsV1 saturated = MemoryPolicyNormalizer.Copy(prior);
            saturated.captureInvalidationGenerationPersonal = long.MaxValue;
            saturated.optionalRequestInvalidationGeneration = long.MaxValue;
            MemorySettingsPolicyFieldsV1 saturatedDraft = MemoryPolicyNormalizer.Copy(saturated);
            saturatedDraft.memoryCategoryMask &= ~MemoryCategoryBits.Personal;
            saturatedDraft.allowExtraMemoryAiRequests = false;
            MemorySettingsCommitPlan max = MemoryPolicyNormalizer.PrepareCommit(
                1, saturated, saturatedDraft, new MemorySettingsBounds());
            Equal("m5.commit.max.capture", long.MaxValue,
                max.candidate.captureInvalidationGenerationPersonal);
            Equal("m5.commit.max.optional", long.MaxValue,
                max.candidate.optionalRequestInvalidationGeneration);
            Equal("m5.commit.max.gate", false,
                max.snapshot.AllowsCapture(MemoryCategoryBits.Personal));
        }

        private static void FutureVersionsFailClosed()
        {
            MemorySettingsPolicyFieldsV1 allOn =
                MemorySettingsPolicyFieldsV1.CreateBenchmarkProfile(12);
            MemoryPolicySnapshot future = MemoryPolicyNormalizer.Normalize(2, allOn,
                new MemorySettingsBounds());
            Equal("m5.future.compat", true, future.compatibilityFailClosed);
            Equal("m5.future.capture", false,
                future.AllowsCapture(MemoryCategoryBits.Personal));
            Equal("m5.future.recall", false,
                future.AllowsRecall(MemoryCategoryBits.Personal));
            Equal("m5.future.optional", false, future.AllowsOptionalRequests);
            MemorySettingsCommitPlan refused = MemoryPolicyNormalizer.PrepareCommit(
                2, allOn, allOn, new MemorySettingsBounds());
            Equal("m5.future.write.refused", false, refused.valid);
            Equal("m5.future.write.marker", true, refused.futureVersion);
        }

        private static void ReconciliationIsIdempotentAndBounded()
        {
            MemoryPolicySnapshot published = MemoryPolicyNormalizer.Normalize(
                1, new MemorySettingsPolicyFieldsV1(), new MemorySettingsBounds());
            MemoryPolicyReconciliationPlan first = MemoryPolicyNormalizer.PlanReconciliation(
                null, string.Empty, 0, published);
            Equal("m5.reconcile.first.valid", true, first.valid);
            Equal("m5.reconcile.first.capture", 15, first.captureGenerationMismatchMask);
            Equal("m5.reconcile.first.purge", true, first.purgeUnsentOptionalWork);
            Equal("m5.reconcile.first.revision", 1L, first.nextAppliedRevision);

            MemoryPolicyReconciliationPlan same = MemoryPolicyNormalizer.PlanReconciliation(
                published.ToFields(), published.fingerprint, 1, published);
            Equal("m5.reconcile.same", true, same.alreadyApplied);
            Equal("m5.reconcile.same.revision", 1L, same.nextAppliedRevision);

            MemorySettingsPolicyFieldsV1 old = published.ToFields();
            old.captureInvalidationGenerationFamily--;
            old.optionalRequestInvalidationGeneration--;
            old.minorMemoryLifetimeDays++;
            old.memoryThreadTarget++;
            MemoryPolicyReconciliationPlan delta = MemoryPolicyNormalizer.PlanReconciliation(
                old, "different", 5, published);
            Equal("m5.reconcile.family", MemoryCategoryBits.Family,
                delta.captureGenerationMismatchMask);
            Equal("m5.reconcile.purge", true, delta.purgeUnsentOptionalWork);
            Equal("m5.reconcile.ttl", true, delta.markLifetimeMaintenanceDirty);
            Equal("m5.reconcile.target", true, delta.markThreadTargetMaintenanceDirty);

            MemoryPolicyReconciliationPlan max = MemoryPolicyNormalizer.PlanReconciliation(
                old, "different", long.MaxValue, published);
            Equal("m5.reconcile.max", true, max.revisionSaturated);
            Equal("m5.reconcile.max.value", long.MaxValue, max.nextAppliedRevision);
            Equal("m5.reconcile.cancel.max", long.MaxValue,
                MemoryPolicyNormalizer.AdvanceSaturatingGeneration(long.MaxValue));
            Equal("m5.reconcile.cancel.zero", 2L,
                MemoryPolicyNormalizer.AdvanceSaturatingGeneration(0));
        }

        private static void PublicationIsIndivisible()
        {
            MemorySettingsPolicyFieldsV1 first = new MemorySettingsPolicyFieldsV1();
            MemoryEffectivePolicyProvider.Reset(1, first, new MemorySettingsBounds());
            Equal("m5.publish.reset.revision", 1L,
                MemoryEffectivePolicyProvider.PublicationRevision);
            string firstFingerprint = MemoryEffectivePolicyProvider.Current.fingerprint;
            MemoryPolicySnapshot equivalent = MemoryPolicyNormalizer.Normalize(
                1, first, new MemorySettingsBounds());
            Equal("m5.publish.same.ok", true,
                MemoryEffectivePolicyProvider.Publish(equivalent));
            Equal("m5.publish.same.revision", 1L,
                MemoryEffectivePolicyProvider.PublicationRevision);

            first.memoryThreadTarget = 13;
            MemoryPolicySnapshot changed = MemoryPolicyNormalizer.Normalize(
                1, first, new MemorySettingsBounds());
            Equal("m5.publish.changed.ok", true,
                MemoryEffectivePolicyProvider.Publish(changed));
            Equal("m5.publish.changed.preflight", true,
                MemoryEffectivePolicyProvider.CanPublish(changed));
            Equal("m5.publish.changed.revision", 2L,
                MemoryEffectivePolicyProvider.PublicationRevision);
            Equal("m5.publish.changed.target", 13,
                MemoryEffectivePolicyProvider.Current.memoryThreadTarget);
            Equal("m5.publish.changed.fingerprint", false,
                string.Equals(firstFingerprint,
                    MemoryEffectivePolicyProvider.Current.fingerprint,
                    StringComparison.Ordinal));
        }

        private static void DurableSettingsPredecessorRules()
        {
            Equal("m5.settings.predecessor.absent", true,
                MemorySettingsCommitPolicy.PredecessorMatches(
                    false, string.Empty, false, string.Empty));
            Equal("m5.settings.predecessor.created.race", false,
                MemorySettingsCommitPolicy.PredecessorMatches(
                    false, string.Empty, true, "A"));
            Equal("m5.settings.predecessor.same", true,
                MemorySettingsCommitPolicy.PredecessorMatches(true, "A", true, "A"));
            Equal("m5.settings.predecessor.changed", false,
                MemorySettingsCommitPolicy.PredecessorMatches(true, "A", true, "B"));
            Equal("m5.settings.predecessor.deleted", false,
                MemorySettingsCommitPolicy.PredecessorMatches(true, "A", false, string.Empty));
        }

        private static void LibraryUnicodeCursorAndFingerprintRules()
        {
            string normalized = MemoryLibraryPolicy.NormalizeSearch(
                "  \uFB00oo\tBAR\uD800  baz  ", 80, 160);
            Equal("m5.library.search.formkc", "FFOO BAR\uFFFD BAZ", normalized);
            Equal("m5.library.search.scalar.clamp", "A\uD83D\uDE00",
                MemoryLibraryPolicy.NormalizeSearch("a\uD83D\uDE00b", 2, 3));

            string first = MemoryLibraryPolicy.StreamFingerprint("active", "Threads", "64");
            string same = MemoryLibraryPolicy.StreamFingerprint("active", "Threads", "64");
            string changed = MemoryLibraryPolicy.StreamFingerprint("active", "Threads", "32");
            Equal("m5.library.fingerprint.stable", first, same);
            Equal("m5.library.fingerprint.changed", false,
                string.Equals(first, changed, StringComparison.Ordinal));
            Equal("m5.library.fingerprint.shape", 64, first.Length);

            MemoryLibraryCursorPlan invalidFresh = MemoryLibraryPolicy.NormalizeRowCursor(
                1, 10, 64, 0, 20);
            Equal("m5.library.cursor.fresh.nonzero", false, invalidFresh.valid);
            MemoryLibraryCursorPlan clamped = MemoryLibraryPolicy.NormalizeRowCursor(
                5, 200, 64, 7, 70);
            Equal("m5.library.cursor.clamp", 64, clamped.count);
            Equal("m5.library.cursor.return", 64, clamped.returnedCount);
            Equal("m5.library.cursor.more", true, clamped.hasMore);
            MemoryLibraryCursorPlan end = MemoryLibraryPolicy.NormalizeRowCursor(
                70, 5, 64, 7, 70);
            Equal("m5.library.cursor.end.valid", true, end.valid);
            Equal("m5.library.cursor.end.empty", 0, end.returnedCount);
            Equal("m5.library.cursor.end.previous", true, end.hasPrevious);
            Equal("m5.library.cursor.end.more", false, end.hasMore);

            const string text = "A\uD83D\uDE00BC";
            MemoryLibraryTextCursorPlan textFirst = MemoryLibraryPolicy.NormalizeTextCursor(
                text, 0, 2, 4, 0);
            Equal("m5.library.text.first.end", 1, textFirst.end);
            Equal("m5.library.text.first.more", true, textFirst.hasMore);
            MemoryLibraryTextCursorPlan split = MemoryLibraryPolicy.NormalizeTextCursor(
                text, 2, 2, 4, 1);
            Equal("m5.library.text.split", false, split.valid);
            MemoryLibraryTextCursorPlan emoji = MemoryLibraryPolicy.NormalizeTextCursor(
                text, 1, 2, 4, 1);
            Equal("m5.library.text.emoji.width", 2, emoji.count);
            MemoryLibraryTextCursorPlan boundedBack = MemoryLibraryPolicy.NormalizeTextCursor(
                "ABCDEFGHIJK", 8, 100, 4, 1);
            Equal("m5.library.text.previous.effective", 4, boundedBack.previousStart);
            Equal("m5.library.text.count.identity", 2,
                MemoryLibraryPolicy.NormalizeTextCount(1, 1000));
            Equal("m5.library.text.count.identity.same",
                MemoryLibraryPolicy.StreamFingerprint("archive", "2"),
                MemoryLibraryPolicy.StreamFingerprint("archive",
                    MemoryLibraryPolicy.NormalizeTextCount(1, 1000).ToString()));

            MemoryLibraryPublicationClock clock = new MemoryLibraryPublicationClock();
            Equal("m5.library.clock.first", true, clock.TryAllocate(out long revision1));
            Equal("m5.library.clock.first.value", 1L, revision1);
            Equal("m5.library.clock.second", true, clock.TryAllocate(out long revision2));
            Equal("m5.library.clock.second.value", 2L, revision2);
            clock.Reset();
            Equal("m5.library.clock.reset", 0L, clock.LastIssuedRevision);
        }

        private static void LibraryDefensiveCeilingParityAndDirectoryPriority()
        {
            Equal("m5.library.ceiling.window", DefensiveValue("libraryWindowRows"),
                MemoryLibraryLimitCeilings.LibraryWindowRows.ToString());
            Equal("m5.library.ceiling.chapters", DefensiveValue("chapterHeaderWindowRows"),
                MemoryLibraryLimitCeilings.ChapterHeaderRows.ToString());
            Equal("m5.library.ceiling.normalized", DefensiveValue("normalizedSearchFieldUnits"),
                MemoryLibraryLimitCeilings.NormalizedFieldUtf16Units.ToString());
            Equal("m5.library.ceiling.projection", DefensiveValue("rowSearchProjectionUnits"),
                MemoryLibraryLimitCeilings.RowProjectionUtf16Units.ToString());
            Equal("m5.library.ceiling.slice.items", DefensiveValue("sliceWorkItems"),
                MemoryLibraryLimitCeilings.SliceWorkItems.ToString());
            Equal("m5.library.ceiling.slice.time", DefensiveValue("sliceTargetMicroseconds"),
                MemoryLibraryLimitCeilings.SliceTargetMicroseconds.ToString());
            Equal("m5.library.ceiling.preview", DefensiveTuplePart(
                    "importedPreviewChunkUnits", 0),
                MemoryLibraryLimitCeilings.ImportedPreviewUtf16Units.ToString());
            Equal("m5.library.ceiling.imported.search.scratch",
                DefensiveValue("importedSearchScratchUnits"),
                MemoryLibraryLimitCeilings.ImportedSearchScratchUtf16Units.ToString());
            Equal("m5.library.ceiling.owners", DefensiveValue("libraryOwnerEntries"),
                MemoryLibraryLimitCeilings.OwnerEntries.ToString());
            Equal("m5.library.ceiling.commands", DefensiveValue("libraryCommandEntries"),
                MemoryLibraryLimitCeilings.CommandEntries.ToString());

            MemoryLibraryDirectoryCapPlan plan = MemoryLibraryPolicy.PlanDirectoryCap(3, 4, 5, 6);
            Equal("m5.library.directory.data.first", 3, plan.includedData);
            Equal("m5.library.directory.raw.second", 3, plan.includedRaw);
            Equal("m5.library.directory.zero.third", 0, plan.includedZero);
            Equal("m5.library.directory.raw.omitted", 1L, plan.omittedRaw);
            Equal("m5.library.directory.zero.omitted", 5L, plan.omittedZero);
            MemoryLibraryDirectoryCapPlan corrupt = MemoryLibraryPolicy.PlanDirectoryCap(7, 2, 3, 6);
            Equal("m5.library.directory.corrupt.data", 6, corrupt.includedData);
            Equal("m5.library.directory.corrupt.raw.exact", 2L, corrupt.omittedRaw);

            Equal("m5.library.revision.thread.root", 9L,
                MemoryLibraryPolicy.TargetStructuralRevision(true, 4, 9));
            Equal("m5.library.revision.standalone.owner", 4L,
                MemoryLibraryPolicy.TargetStructuralRevision(false, 4, 9));
            Equal("m5.library.culture.same", false,
                MemoryLibraryPolicy.CultureProjectionChanged(
                    "origin", "captured", "adopted", "origin", "captured", "adopted"));
            Equal("m5.library.culture.adopted.changed", true,
                MemoryLibraryPolicy.CultureProjectionChanged(
                    "origin", "captured", "old", "origin", "captured", "new"));
            Equal("m5.library.directory.tier.unknown", MemoryLibraryDirectoryTiers.Data,
                MemoryLibraryPolicy.DirectoryTier(true, false, false, true, false));
            Equal("m5.library.directory.tier.dead.culture", MemoryLibraryDirectoryTiers.Data,
                MemoryLibraryPolicy.DirectoryTier(false, false, true, false, false));
            Equal("m5.library.directory.tier.away.culture", MemoryLibraryDirectoryTiers.Data,
                MemoryLibraryPolicy.DirectoryTier(false, false, true, false, false));
            Equal("m5.library.directory.tier.dead.empty", MemoryLibraryDirectoryTiers.Omitted,
                MemoryLibraryPolicy.DirectoryTier(false, false, false, false, false));
            Equal("m5.library.directory.tier.active.empty",
                MemoryLibraryDirectoryTiers.ActiveZero,
                MemoryLibraryPolicy.DirectoryTier(false, false, false, false, true));
            Equal("m5.library.directory.tier.raw",
                MemoryLibraryDirectoryTiers.CompatibilityRaw,
                MemoryLibraryPolicy.DirectoryTier(false, false, false, true, false));
            Equal("m5.library.build.fence.same", true,
                MemoryLibraryPolicy.LibraryBuildFenceMatches(
                    1, 1, 2, 2, 3, 3, 4, 4, 5, 5));
            Equal("m5.library.build.fence.diary.changed", false,
                MemoryLibraryPolicy.LibraryBuildFenceMatches(
                    1, 2, 2, 2, 3, 3, 4, 4, 5, 5));
        }

        private static void LibraryFilteringPagingAndTtlRules()
        {
            MemoryBlockRow row = NewLibraryBlock("r1", 100, "Minor", false, false, "ALPHA");
            row.projectedCategoryMask = MemoryCategoryBits.Personal;
            Equal("m5.library.filter.all", true,
                MemoryLibraryPolicy.MatchesFilters(row, new MemoryLibraryFilters()));
            Equal("m5.library.filter.category", false,
                MemoryLibraryPolicy.MatchesFilters(row, new MemoryLibraryFilters
                {
                    categoryMask = MemoryCategoryBits.Family
                }));
            row.playerEdited = true;
            Equal("m5.library.filter.edited", true,
                MemoryLibraryPolicy.MatchesFilters(row, new MemoryLibraryFilters
                {
                    stateToken = "edited"
                }));
            Equal("m5.library.filter.unknown", false,
                MemoryLibraryPolicy.MatchesFilters(row, new MemoryLibraryFilters
                {
                    stateToken = "future"
                }));

            Equal("m5.library.ttl.minor", 700L,
                MemoryLibraryPolicy.FutureExpiryTick(100, false, false,
                    MemoryLibraryPolicy.ImportanceMinor, 600, 1200, 200));
            Equal("m5.library.ttl.due", long.MaxValue,
                MemoryLibraryPolicy.FutureExpiryTick(100, false, false,
                    MemoryLibraryPolicy.ImportanceMinor, 50, 1200, 200));
            Equal("m5.library.ttl.edited", long.MaxValue,
                MemoryLibraryPolicy.FutureExpiryTick(100, false, true,
                    MemoryLibraryPolicy.ImportanceMinor, 600, 1200, 200));
            Equal("m5.library.ttl.daywins", 500L,
                MemoryLibraryPolicy.TtlValidUntil(500, 700));
        }

        private static void LibraryIndexVisibilityAndCounts()
        {
            MemoryLibraryOwnerIndexInput input = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = new MemoryLibraryOwnerHandle("active", "pawn", "epoch"),
                ownerEpochKey = new MemoryOwnerEpochKey
                    { ownerPawnId = "pawn", epochToken = "epoch" },
                displayName = "Owner",
                lifecycleToken = "active",
                structuralRevision = 4,
                statusRevision = 5
            };
            MemoryLibraryRootIndexInput root = new MemoryLibraryRootIndexInput
            {
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = new MemoryRootHandle
                        { ownerPawnId = "pawn", epochToken = "epoch", rootId = "root" },
                    subjectLabel = "Subject",
                    normalizedSearch = "SUBJECT",
                    latestActivityTick = 200,
                    structuralRevision = 9
                }
            };
            root.children.Add(NewLibraryBlock("c1", 100, "Minor", false, false, "ALPHA"));
            root.children.Add(NewLibraryBlock("c2", 200, "Regular", false, true, "BETA"));
            input.roots.Add(root);
            input.standalone.Add(NewLibraryBlock("s1", 150, "Important", false, false, "GAMMA"));
            MemoryLibraryOwnerIndexSnapshot snapshot = MemoryLibraryIndexPolicy.BuildOwner(
                input, new MemoryLibraryLimits());
            Equal("m5.library.index.thread.count", 1, snapshot.ownerRow.threadCount);
            Equal("m5.library.index.manageable", 2,
                snapshot.roots[0].header.manageableMemoryCount);
            Equal("m5.library.index.suppressed", 1,
                snapshot.roots[0].header.suppressedCount);

            MemoryLibraryListQuery headerHit = new MemoryLibraryListQuery
            {
                primaryHandle = input.primaryHandle,
                activeOwnerEpochKey = input.ownerEpochKey,
                viewTag = MemoryLibraryViews.Threads,
                search = "subject",
                listCount = 10
            };
            MemoryLibraryListResult found = MemoryLibraryIndexPolicy.QueryList(
                snapshot, headerHit, 1, 2, 3, 4, 5, 1000, new MemoryLibraryLimits());
            Equal("m5.library.index.header.hit", MemoryLibraryStatuses.Ready, found.status);
            Equal("m5.library.index.header.total", 1, found.totalMatchedRows);

            headerHit.filters.categoryMask = MemoryCategoryBits.Family;
            MemoryLibraryListResult filtered = MemoryLibraryIndexPolicy.QueryList(
                snapshot, headerHit, 1, 3, 3, 4, 5, 1000, new MemoryLibraryLimits());
            Equal("m5.library.index.header.cannot.bypass", 0, filtered.totalEligibleRows);
            Equal("m5.library.index.empty", "no_memories", filtered.emptyStateToken);

            List<MemoryLibraryOwnerRow> owners = new List<MemoryLibraryOwnerRow>
                { snapshot.ownerRow };
            MemoryLibraryOwnerResult ownerPage = MemoryLibraryIndexPolicy.QueryOwners(
                owners, new MemoryLibraryOwnerQuery { count = 10 }, 8,
                new MemoryLibraryLimits(), 2, 3);
            Equal("m5.library.owner.ready", MemoryLibraryStatuses.Ready, ownerPage.status);
            Equal("m5.library.owner.omitted.raw", 2L,
                ownerPage.additionalLegacyRawOwnersNotShown);
            Equal("m5.library.owner.omitted.zero", 3L,
                ownerPage.additionalZeroMemoryOwnersNotShown);
            Equal("m5.library.index.highest", MemoryLibraryPolicy.ImportanceRegular,
                snapshot.roots[0].header.highestImportanceMask);
        }

        private static void LibraryProjectionAndDetailReviewRules()
        {
            MemoryLibraryLimits limits = new MemoryLibraryLimits();
            MemoryBlockRow summary = NewLibraryBlock(
                "summary", 200, "Important", false, false, "SUMMARY SUBJECT");
            summary.kind = "Summary";
            summary.normalizedWholeSearch = "SUMMARY SUBJECT";
            summary.projectedCategoryMask = MemoryCategoryBits.Personal | MemoryCategoryBits.Family;
            summary.projectedImportanceMask = MemoryLibraryPolicy.ImportanceMinor
                | MemoryLibraryPolicy.ImportanceImportant;
            summary.summaryContributions.Add(NewContribution(
                0, MemoryCategoryBits.Personal, MemoryLibraryPolicy.ImportanceMinor,
                100, 500, "minor alpha", "alpha detail"));
            summary.summaryContributions.Add(NewContribution(
                1, MemoryCategoryBits.Family, MemoryLibraryPolicy.ImportanceImportant,
                200, 900, "important beta", "beta detail"));
            bool projected = MemoryLibraryPolicy.TryProjectRow(
                summary,
                new MemoryLibraryFilters
                {
                    categoryMask = MemoryCategoryBits.Personal,
                    importanceMask = MemoryLibraryPolicy.ImportanceMinor
                },
                MemoryLibraryPolicy.NormalizeSearch("alpha", 80, 160),
                false,
                limits,
                out MemoryBlockRow minor);
            Equal("m5.library.summary.projected", true, projected);
            Equal("m5.library.summary.projected.count", 1, minor.summaryContributions.Count);
            Equal("m5.library.summary.projected.category", MemoryCategoryBits.Personal,
                minor.projectedCategoryMask);
            Equal("m5.library.summary.projected.importance", MemoryLibraryPolicy.ImportanceMinor,
                minor.projectedHighestImportanceMask);
            Equal("m5.library.summary.projected.expiry", 500L,
                minor.projectedNextExpiryTick);
            Equal("m5.library.summary.unmatched.search", false,
                MemoryLibraryPolicy.TryProjectRow(
                    summary,
                    new MemoryLibraryFilters { categoryMask = MemoryCategoryBits.Personal },
                    MemoryLibraryPolicy.NormalizeSearch("beta", 80, 160),
                    false,
                    limits,
                    out MemoryBlockRow ignored));

            MemoryRootHandle rootHandle = new MemoryRootHandle
                { ownerPawnId = "pawn", epochToken = "epoch", rootId = "detail" };
            MemoryLibraryRootIndexInput root = new MemoryLibraryRootIndexInput
            {
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = rootHandle,
                    subjectLabel = "Subject",
                    normalizedSearch = "SUBJECT",
                    structuralRevision = 12
                }
            };
            root.chapters.Add(NewChapter("personal", 0));
            root.chapters.Add(NewChapter("family-a", 1));
            root.chapters.Add(NewChapter("family-b", 2));
            MemoryBlockRow personal = NewLibraryBlock(
                "d0", 50, "Minor", false, false, "PERSONAL");
            personal.rootHandle = rootHandle;
            personal.chapterId = "personal";
            personal.projectedNextExpiryTick = 500;
            root.children.Add(personal);
            for (int index = 1; index <= 3; index++)
            {
                MemoryBlockRow family = NewLibraryBlock(
                    "d" + index, 100 + index,
                    index == 1 ? "Important" : "Regular", false, false, "FAMILY");
                family.rootHandle = rootHandle;
                family.chapterId = index < 3 ? "family-a" : "family-b";
                family.projectedCategoryMask = MemoryCategoryBits.Family;
                family.projectedNextExpiryTick = 900 + index;
                root.children.Add(family);
            }
            MemoryBlockRow rolling = NewLibraryBlock(
                "rolling", 200, "Regular", false, false, "FAMILY");
            rolling.kind = "Summary";
            rolling.rollingSummary = true;
            rolling.rootHandle = rootHandle;
            rolling.projectedCategoryMask = MemoryCategoryBits.Family;
            root.children.Add(rolling);
            MemoryLibraryOwnerIndexInput input = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = new MemoryLibraryOwnerHandle("active", "pawn", "epoch"),
                ownerEpochKey = new MemoryOwnerEpochKey
                    { ownerPawnId = "pawn", epochToken = "epoch" }
            };
            input.roots.Add(root);
            MemoryLibraryOwnerIndexSnapshot snapshot = MemoryLibraryIndexPolicy.BuildOwner(input, limits);
            Equal("m5.library.detail.header.highest", MemoryLibraryPolicy.ImportanceImportant,
                snapshot.roots[0].header.highestImportanceMask);

            MemoryThreadDetailQuery query = new MemoryThreadDetailQuery
            {
                rootHandle = rootHandle,
                filters = new MemoryLibraryFilters { categoryMask = MemoryCategoryBits.Family },
                search = "subject",
                detailCount = 1
            };
            MemoryThreadDetailResult first = MemoryLibraryIndexPolicy.QueryThreadDetail(
                snapshot, query, 10, 1000, limits);
            Equal("m5.library.detail.target-visible-count", 4,
                snapshot.roots[0].header.targetCountedVisibleBlockCount);
            Equal("m5.library.detail.manageable-count", 5,
                snapshot.roots[0].header.manageableMemoryCount);
            // Footer totals count the three matching target-visible blocks and the rolling summary
            // exactly once. Chapter context is never a manageable-memory row, even across windows.
            Equal("m5.library.detail.header.hit.count", 4, first.shownManageableCount);
            Equal("m5.library.detail.root.ttl", 500L, first.ttlValidUntilTickExclusive);
            Equal("m5.library.detail.chapter.next", true, first.chapters[0].continuesInNext);

            query.detailStart = 1;
            query.detailCount = 3;
            query.expectedDetailSnapshotRevision = 10;
            MemoryLibraryLimits oneHeader = new MemoryLibraryLimits { chapterHeaderRows = 1 };
            MemoryThreadDetailResult capped = MemoryLibraryIndexPolicy.QueryThreadDetail(
                snapshot, query, 10, 1000, oneHeader);
            Equal("m5.library.detail.count-stable-across-windows", 4,
                capped.shownManageableCount);
            Equal("m5.library.detail.chapter.cap.count", 1, capped.returnedCount);
            Equal("m5.library.detail.chapter.previous", true,
                capped.chapters[0].continuedFromPrevious);
            Equal("m5.library.detail.chapter.actual.next", false,
                capped.chapters[0].continuesInNext);

            snapshot.imported.Add(new MemoryImportedSearchDescriptor
            {
                row = new MemoryImportedRow
                {
                    archiveHandle = new MemoryArchiveHandle
                    {
                        archiveScopeToken = MemoryLibraryScopes.Active,
                        exactOwnerPawnIdOrEmpty = "pawn",
                        archiveRecordId = "long"
                    },
                    preview = "short",
                    targetStructuralRevision = 1
                },
                rawSearchText = "short long needle"
            });
            MemoryLibraryListResult imported = MemoryLibraryIndexPolicy.QueryList(
                snapshot,
                new MemoryLibraryListQuery
                {
                    primaryHandle = input.primaryHandle,
                    viewTag = MemoryLibraryViews.Imported,
                    search = "needle",
                    listCount = 10
                },
                1, 2, 3, 4, 5, 1000, limits);
            Equal("m5.library.imported.long.search", 1, imported.totalMatchedRows);
            Equal("m5.library.imported.result.preview.only", "short",
                imported.rows[0].imported.preview);

            snapshot.imported.Clear();
            snapshot.imported.Add(new MemoryImportedSearchDescriptor
            {
                row = new MemoryImportedRow { preview = "first" },
                rawSearchText = "ordinary"
            });
            snapshot.imported.Add(new MemoryImportedSearchDescriptor
            {
                row = new MemoryImportedRow { preview = "expanded" },
                // U+FB03 expands from one raw UTF-16 unit to three NFKC units. The suffix is past
                // the old 2,000-unit row cap but inside the canonical search-scratch capacity.
                rawSearchText = new string('\uFB03', 700) + "needle"
            });
            MemoryLibraryListQuery expandedQuery = new MemoryLibraryListQuery
            {
                primaryHandle = input.primaryHandle,
                viewTag = MemoryLibraryViews.Imported,
                search = "needle",
                listCount = 10
            };
            MemoryImportedListSelectionJob expandedJob =
                MemoryLibraryIndexPolicy.BeginImportedListSelection(
                    snapshot, expandedQuery, limits);
            Equal("m5.library.imported.slice.first.incomplete", false,
                MemoryLibraryIndexPolicy.AdvanceImportedListSelection(expandedJob, limits));
            Equal("m5.library.imported.slice.one.row", 1, expandedJob.cursor);
            Equal("m5.library.imported.slice.second.complete", true,
                MemoryLibraryIndexPolicy.AdvanceImportedListSelection(expandedJob, limits));
            MemoryLibraryListSelection expandedSelection =
                MemoryLibraryIndexPolicy.CompleteImportedListSelection(expandedJob);
            MemoryLibraryListResult expanded = MemoryLibraryIndexPolicy.QueryListSelection(
                snapshot, expandedQuery, expandedSelection, 1, 2, 3, 4, 5, 1000, limits);
            Equal("m5.library.imported.nfkc.suffix.match", 1, expanded.totalMatchedRows);
            Equal("m5.library.imported.nfkc.preview.only", "expanded",
                expanded.rows[0].imported.preview);
            expandedQuery.listStart = 1;
            Equal("m5.library.imported.fresh.cursor.must.start.zero", null,
                MemoryLibraryIndexPolicy.BeginImportedListSelection(
                    snapshot, expandedQuery, limits));
            expandedQuery.listStart = 0;
            expandedQuery.expectedListSnapshotRevision = -1;
            Equal("m5.library.imported.negative.revision.invalid", null,
                MemoryLibraryIndexPolicy.BeginImportedListSelection(
                    snapshot, expandedQuery, limits));
            expandedQuery.expectedListSnapshotRevision = 2;
            expandedQuery.expectedDirectoryRevision = -1;
            MemoryLibraryListResult negativeDirectory =
                MemoryLibraryIndexPolicy.QueryListSelection(
                    snapshot, expandedQuery, expandedSelection,
                    1, 2, 3, 4, 5, 1000, limits);
            Equal("m5.library.imported.negative.directory.invalid",
                MemoryLibraryStatuses.Invalid, negativeDirectory.status);

            MemoryLibraryRootIndexInput orphanRoot = new MemoryLibraryRootIndexInput
            {
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = new MemoryRootHandle
                        { ownerPawnId = "pawn", epochToken = "epoch", rootId = "orphan" },
                    latestActivityTick = 999
                }
            };
            orphanRoot.chapters.Add(NewChapter("kept-chapter", 1));
            MemoryBlockRow kept = NewLibraryBlock(
                "kept-child", 10, "Regular", false, false, "KEPT");
            kept.rootHandle = orphanRoot.header.rootHandle;
            kept.chapterId = "kept-chapter";
            orphanRoot.children.Add(kept);
            MemoryBlockRow orphan = NewLibraryBlock(
                "orphan-child", 999, "Important", false, false, "ORPHAN");
            orphan.rootHandle = orphanRoot.header.rootHandle;
            orphan.chapterId = "missing-chapter";
            orphanRoot.children.Add(orphan);
            MemoryLibraryOwnerIndexInput orphanInput = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = input.primaryHandle,
                ownerEpochKey = input.ownerEpochKey
            };
            orphanInput.roots.Add(orphanRoot);
            MemoryLibraryOwnerIndexSnapshot repairedOrphan =
                MemoryLibraryIndexPolicy.BuildOwner(orphanInput, limits);
            Equal("m5.library.detail.orphan.header.manageable", 1,
                repairedOrphan.roots[0].header.manageableMemoryCount);
            Equal("m5.library.detail.orphan.header.counted", 1,
                repairedOrphan.roots[0].header.targetCountedVisibleBlockCount);
            Equal("m5.library.detail.orphan.header.importance",
                MemoryLibraryPolicy.ImportanceRegular,
                repairedOrphan.roots[0].header.highestImportanceMask);
            Equal("m5.library.detail.orphan.header.latest", 10L,
                repairedOrphan.roots[0].header.latestActivityTick);
            Equal("m5.library.detail.orphan.owner.latest", 10L,
                repairedOrphan.ownerRow.latestActivityTick);
            Equal("m5.library.detail.orphan.owner.ttl", 1010L,
                repairedOrphan.ownerEarliestFiniteExpiryTickExclusive);
            MemoryThreadDetailResult orphanResult = MemoryLibraryIndexPolicy.QueryThreadDetail(
                repairedOrphan,
                new MemoryThreadDetailQuery
                {
                    rootHandle = orphanRoot.header.rootHandle,
                    detailCount = 1
                },
                1,
                1000,
                new MemoryLibraryLimits { libraryWindowRows = 0, chapterHeaderRows = 0 });
            Equal("m5.library.detail.orphan.ready", MemoryLibraryStatuses.Ready,
                orphanResult.status);
            Equal("m5.library.detail.orphan.not.returned", 1, orphanResult.returnedCount);
            Equal("m5.library.detail.orphan.kept.child", "kept-child",
                orphanResult.blocks[0].recordHandle.recordId);
            Equal("m5.library.detail.orphan.kept.context", 1, orphanResult.chapters.Count);
            Equal("m5.library.detail.orphan.terminal", false, orphanResult.hasMore);
            Equal("m5.library.detail.orphan.total", 1, orphanResult.totalManageableCount);
            Equal("m5.library.detail.orphan.shown", 1, orphanResult.shownManageableCount);
            Equal("m5.library.detail.orphan.suppression", false,
                orphanResult.allBlocksSuppressedForWriting);
            Equal("m5.library.detail.orphan.ttl", 1000L,
                orphanResult.ttlValidUntilTickExclusive);
        }

        private static void LibraryHandleAndRevisionRules()
        {
            Equal("m5.library.archive.unresolved.valid", true,
                MemoryLibraryPolicy.ValidArchiveHandle(new MemoryArchiveHandle
                {
                    archiveScopeToken = MemoryLibraryScopes.UnresolvedImported,
                    archiveRecordId = "row"
                }));
            Equal("m5.library.archive.unresolved.owner.invalid", false,
                MemoryLibraryPolicy.ValidArchiveHandle(new MemoryArchiveHandle
                {
                    archiveScopeToken = MemoryLibraryScopes.UnresolvedImported,
                    exactOwnerPawnIdOrEmpty = "pawn",
                    archiveRecordId = "row"
                }));
            Equal("m5.library.archive.active.owner.required", false,
                MemoryLibraryPolicy.ValidArchiveHandle(new MemoryArchiveHandle
                {
                    archiveScopeToken = MemoryLibraryScopes.Active,
                    archiveRecordId = "row"
                }));
            Equal("m5.library.compat.first", true,
                MemoryLibraryPolicy.TryNextCompatibilityRevision(0, false, out long first));
            Equal("m5.library.compat.first.value", 1L, first);
            Equal("m5.library.compat.same", true,
                MemoryLibraryPolicy.TryNextCompatibilityRevision(7, true, out long same));
            Equal("m5.library.compat.same.value", 7L, same);
            Equal("m5.library.compat.changed", true,
                MemoryLibraryPolicy.TryNextCompatibilityRevision(7, false, out long changed));
            Equal("m5.library.compat.changed.value", 8L, changed);
            Equal("m5.library.compat.max", false,
                MemoryLibraryPolicy.TryNextCompatibilityRevision(
                    long.MaxValue, false, out long saturated));
            Equal("m5.library.compat.max.value", 0L, saturated);
        }

        private static void LibraryMutationRules()
        {
            MemoryBlockRow standaloneEvent = NewLibraryBlock(
                "event", 1, "Regular", false, false, "EVENT");
            standaloneEvent.kind = "Event";
            standaloneEvent.canSuppress = true;
            standaloneEvent.canSaveWording = true;
            standaloneEvent.canUseOriginal = true;
            Equal("m5.library.command.suppress", true,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.SetSuppressed, standaloneEvent, false,
                    true, string.Empty, 480).eligible);
            Equal("m5.library.command.blank", false,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.SaveWording, standaloneEvent, false,
                    false, "   ", 480).validAction);
            Equal("m5.library.command.edit", true,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.SaveWording, standaloneEvent, false,
                    false, "kept wording", 480).eligible);
            standaloneEvent.playerEdited = true;
            Equal("m5.library.command.original", true,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.UseOriginalWording, standaloneEvent, false,
                    false, string.Empty, 480).eligible);
            standaloneEvent.rollingSummary = true;
            Equal("m5.library.command.rolling.original", false,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.UseOriginalWording, standaloneEvent, false,
                    false, string.Empty, 480).eligible);
            Equal("m5.library.command.imported.forget", true,
                MemoryLibraryPolicy.CheckEligibility(
                    MemoryLibraryActions.ForgetPermanent, null, true,
                    false, string.Empty, 480).eligible);

            MemoryLibraryCommand command = new MemoryLibraryCommand
            {
                libraryClientToken = "client",
                commandId = 1,
                actionToken = MemoryLibraryActions.ForgetPermanent,
                targetStructuralRevision = 5,
                archiveHandle = new MemoryArchiveHandle
                    { archiveScopeToken = "unresolvedImported", archiveRecordId = "a" }
            };
            MemoryLibraryCommandTargetState target = new MemoryLibraryCommandTargetState
            {
                commandShapeValid = true,
                exists = false,
                currentStructuralRevision = 6,
                imported = true,
                devAuthorized = false
            };
            Equal("m5.library.command.precedence.missing",
                MemoryLibraryCommandStatuses.Missing,
                MemoryLibraryPolicy.PlanCommandStatus(command, target, 480));
            target.exists = true;
            Equal("m5.library.command.precedence.stale",
                MemoryLibraryCommandStatuses.Stale,
                MemoryLibraryPolicy.PlanCommandStatus(command, target, 480));
            target.currentStructuralRevision = 5;
            Equal("m5.library.command.precedence.unauthorized",
                MemoryLibraryCommandStatuses.Unauthorized,
                MemoryLibraryPolicy.PlanCommandStatus(command, target, 480));
            target.devAuthorized = true;
            Equal("m5.library.command.precedence.success",
                MemoryLibraryCommandStatuses.Success,
                MemoryLibraryPolicy.PlanCommandStatus(command, target, 480));

            Equal("m5.library.culture.none", "none",
                MemoryLibraryPolicy.CultureStateToken(string.Empty, false));
            Equal("m5.library.culture.unavailable", "unavailable",
                MemoryLibraryPolicy.CultureStateToken("missing", false));
            Equal("m5.library.culture.resolved", "resolved",
                MemoryLibraryPolicy.CultureStateToken("known", true));
            Equal("m5.library.culture.provenance", "unknown",
                MemoryLibraryPolicy.CultureProvenanceToken("future"));
        }

        private static MemoryBlockRow NewLibraryBlock(
            string id,
            long tick,
            string importance,
            bool edited,
            bool suppressed,
            string search)
        {
            return new MemoryBlockRow
            {
                recordHandle = new MemoryRecordHandle
                    { ownerPawnId = "pawn", epochToken = "epoch", recordId = id },
                kind = "Event",
                projectedCategoryMask = MemoryCategoryBits.Personal,
                projectedHighestImportanceMask = MemoryLibraryPolicy.ImportanceMask(importance),
                originalTick = tick,
                activityTick = tick,
                projectedNextExpiryTick = tick + 1000,
                playerEdited = edited,
                suppressed = suppressed,
                normalizedSearch = search,
                canSuppress = true,
                canSaveWording = true,
                canUseOriginal = edited
            };
        }

        private static MemorySummaryContributionDescriptor NewContribution(
            int ordinal,
            int category,
            int importance,
            long tick,
            long expiry,
            string preview,
            string search)
        {
            MemorySummaryContributionDescriptor result = new MemorySummaryContributionDescriptor
            {
                sourceOrdinal = ordinal,
                categoryMask = category,
                importanceMask = importance,
                originalTick = tick,
                nextExpiryTick = expiry,
                browsePreview = preview
            };
            result.searchFields.Add(search);
            return result;
        }

        private static MemoryChapterRow NewChapter(string id, long ordinal)
        {
            return new MemoryChapterRow
            {
                chapterId = id,
                ordinal = ordinal,
                phaseToken = "open"
            };
        }

        private static string DefensiveValue(string name)
        {
            List<MemoryCapacityContractRow> rows = MemoryCapacityContracts.DefensiveCeilings();
            for (int index = 0; index < rows.Count; index++)
                if (string.Equals(rows[index].name, name, StringComparison.Ordinal))
                    return rows[index].valueEncoding;
            throw new InvalidOperationException("Missing defensive contract row: " + name);
        }

        private static string DefensiveTuplePart(string name, int index)
        {
            string[] parts = DefensiveValue(name).Split('/');
            if (index < 0 || index >= parts.Length)
                throw new InvalidOperationException("Missing defensive tuple part: " + name);
            return parts[index];
        }

        private static void Equal<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
                throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
        }
    }
}
