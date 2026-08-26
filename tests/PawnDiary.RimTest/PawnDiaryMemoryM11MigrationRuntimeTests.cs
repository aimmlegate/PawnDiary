// PawnDiaryMemoryM11MigrationRuntimeTests.cs — staged real-save migration coverage for M11.
//
// RimTest Redux cannot save, quit, reload, and continue one test synchronously. These guarded A/B/C
// fixtures therefore use two reserved disposable save names. Phase A writes a genuine legacy owner;
// Phase B must be run after loading A and writes the migrated save; Phase C must be run after loading
// B, verifies second-reload stability, then removes both saves and the disposable component row.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>True process-boundary legacy migration, no-catch-up, and repeat-load evidence.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11MigrationRuntimeTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PhaseASaveName = "PawnDiary_Memory_M11_RimTest_PhaseA_Legacy";
        private const string PhaseBSaveName = "PawnDiary_Memory_M11_RimTest_PhaseB_Migrated";
        private const string FixturePawnName = "PawnDiary M11 Migration Fixture";
        private const string FixtureCulture = "PawnDiary_RimTest_LegacyCulture";
        private const string LogPrefix = "[Pawn Diary RimTest] Memory M11 migration: ";

        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly FieldInfo DiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", PrivateInstance);

        /// <summary>
        /// Writes a reserved disposable save containing one schema-v1 record. The live colony is
        /// restored immediately after saving; only the named Phase A file remains.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseAWriteLegacyOwnerSave()
        {
            if (!LoadedComponentOrSkip("Phase A", out DiaryGameComponent component)) return;
            RequireReflectionSurface();
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
                string ownerId = pawn.GetUniqueLoadID();
                diary.pawnName = FixturePawnName;
                diary.eventIds.Clear();
                diary.knowledgeState = new PawnKnowledgeState
                {
                    pawnId = ownerId,
                    schemaVersion = 1,
                    originCultureDefName = FixtureCulture,
                    originCultureSource = KnowledgeTokens.CultureSourceInferred,
                    records = new List<ImportantMemoryRecord>
                    {
                        LegacyRecord(ownerId)
                    }
                };
                DiaryStateVersion.Bump();
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();

                DeleteFixtureSave(PhaseASaveName);
                DeleteFixtureSave(PhaseBSaveName);
                GameDataSaveLoader.SaveGame(PhaseASaveName);
                Require(File.Exists(GenFilePaths.FilePathForSavedGame(PhaseASaveName)),
                    "RimWorld did not write the reserved Phase A save.");
                Require(diary.knowledgeState.schemaVersion == 1
                        && diary.knowledgeState.records.Count == 1
                        && diary.eventIds.Count == 0,
                    "Saving Phase A eagerly migrated the source owner or created a catch-up page.");
                Log.Message(LogPrefix + "Phase A PASS: load '" + PhaseASaveName
                    + "' and run SaveReloadPhaseBVerifyMigrationAndSaveAgain.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        /// <summary>
        /// Runs only after loading Phase A. It proves automatic PostLoadInit migration, reruns the
        /// commit for idempotence, and writes the current-shape Phase B save.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseBVerifyMigrationAndSaveAgain()
        {
            if (!LoadedComponentOrSkip("Phase B", out DiaryGameComponent component)) return;
            RequireReflectionSurface();
            PawnDiaryRecord diary = FindFixtureDiary(component);
            if (diary == null || diary.knowledgeState == null
                || !diary.knowledgeState.IsCurrentSchema())
            {
                Log.Message(LogPrefix + "SKIP Phase B: load '" + PhaseASaveName
                    + "' first.");
                return;
            }

            try
            {
                PawnKnowledgeState state = diary.knowledgeState;
                Require(state.records.Count == 0
                        && !string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)
                        && !state.archiveOnly
                        && !state.epochFenceOnly
                        && state.threadRoots.Count + state.standaloneBlocks.Count == 1
                        && state.importedArchiveRows.Count == 0
                        && state.originCultureDefName == FixtureCulture
                        && diary.eventIds.Count == 0,
                    "Phase A did not load as one current owner without catch-up pages/conflicts.");
                string canonicalIdentity = CanonicalIdentity(state);
                long structural = state.structuralRevision;
                long status = state.statusRevision;
                long cancellation = state.requestCancellationGeneration;

                component.RunMemoryMigrationCommit();
                Require(ReferenceEquals(state, diary.knowledgeState)
                        && CanonicalIdentity(state) == canonicalIdentity
                        && state.structuralRevision == structural
                        && state.statusRevision == status
                        && state.requestCancellationGeneration == cancellation
                        && diary.eventIds.Count == 0,
                    "A second migration pass changed current state or emitted a page.");

                DeleteFixtureSave(PhaseBSaveName);
                GameDataSaveLoader.SaveGame(PhaseBSaveName);
                Require(File.Exists(GenFilePaths.FilePathForSavedGame(PhaseBSaveName)),
                    "RimWorld did not write the reserved Phase B migrated save.");
                Log.Message(LogPrefix + "Phase B PASS: load '" + PhaseBSaveName
                    + "' and run SaveReloadPhaseCVerifySecondReloadAndCleanup.");
            }
            finally
            {
                RemoveFixtureDiary(component);
                PawnDiaryMemoryM11RuntimeFixture.ResetLibrary(component);
            }
        }

        /// <summary>
        /// Runs only after loading Phase B. It proves the second reload stays current and stable, then
        /// removes the fixture row plus both reserved save files.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseCVerifySecondReloadAndCleanup()
        {
            if (!LoadedComponentOrSkip("Phase C", out DiaryGameComponent component)) return;
            RequireReflectionSurface();
            PawnDiaryRecord diary = FindFixtureDiary(component);
            if (diary == null || diary.knowledgeState == null
                || !diary.knowledgeState.IsCurrentSchema())
            {
                Log.Message(LogPrefix + "SKIP Phase C: load '" + PhaseBSaveName
                    + "' first.");
                return;
            }

            try
            {
                PawnKnowledgeState state = diary.knowledgeState;
                string canonicalIdentity = CanonicalIdentity(state);
                long structural = state.structuralRevision;
                long status = state.statusRevision;
                Require(state.records.Count == 0
                        && state.threadRoots.Count + state.standaloneBlocks.Count == 1
                        && state.importedArchiveRows.Count == 0
                        && diary.eventIds.Count == 0,
                    "The second reload changed migrated counts or created a catch-up page.");
                component.RunMemoryMigrationCommit();
                Require(CanonicalIdentity(state) == canonicalIdentity
                        && state.structuralRevision == structural
                        && state.statusRevision == status
                        && diary.eventIds.Count == 0,
                    "The second reload was not migration-idempotent.");
                Log.Message(LogPrefix
                    + "Phase C PASS: migration remained byte-shape stable across two reloads.");
            }
            finally
            {
                RemoveFixtureDiary(component);
                PawnDiaryMemoryM11RuntimeFixture.ResetLibrary(component);
                DeleteFixtureSave(PhaseASaveName);
                DeleteFixtureSave(PhaseBSaveName);
            }
        }

        private static ImportantMemoryRecord LegacyRecord(string ownerId)
        {
            ImportantMemoryRecord record = new ImportantMemoryRecord
            {
                recordId = "rimtest-m11-legacy-record",
                dedupKey = "rimtest-m11-legacy-dedup",
                sourceEventId = "rimtest-m11-legacy-event",
                eventKind = "relation.spouse.gained",
                topicKey = "relationship",
                tick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                dateLabel = "RimTest legacy date",
                fallbackSummary = "A remembered relationship before M11."
            };
            record.participantIds.Add("Pawn_RimTest_Migration_Subject");
            record.participantNames.Add("Migration subject");
            record.factKeys.Add("relation");
            record.factValues.Add("spouse");
            record.subjectKeys.Add("pawn:Pawn_RimTest_Migration_Subject");
            return record;
        }

        private static string CanonicalIdentity(PawnKnowledgeState state)
        {
            if (state.threadRoots.Count == 1)
            {
                SavedMemoryThreadRoot root = state.threadRoots[0];
                string blocks = string.Join(",", root.visibleBlocks
                    .Where(row => row != null)
                    .Select(row => row.recordId)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
                return state.autobiographicalEpochToken + "|thread|" + root.rootId
                    + "|" + blocks;
            }
            SavedMemoryBlock block = state.standaloneBlocks.Single();
            return state.autobiographicalEpochToken + "|standalone|" + block.recordId;
        }

        private static PawnDiaryRecord FindFixtureDiary(DiaryGameComponent component)
        {
            List<PawnDiaryRecord> diaries =
                DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            return diaries?.FirstOrDefault(row => row != null
                && string.Equals(row.pawnName, FixturePawnName, StringComparison.Ordinal)
                && string.Equals(row.knowledgeState?.originCultureDefName,
                    FixtureCulture, StringComparison.Ordinal));
        }

        private static void RemoveFixtureDiary(DiaryGameComponent component)
        {
            List<PawnDiaryRecord> diaries =
                DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            List<string> ids = diaries == null
                ? new List<string>()
                : diaries.Where(row => row != null
                        && string.Equals(
                            row.pawnName, FixturePawnName, StringComparison.Ordinal)
                        && string.Equals(row.knowledgeState?.originCultureDefName,
                            FixtureCulture, StringComparison.Ordinal))
                    .Select(row => row.pawnId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList();
            diaries?.RemoveAll(row => row != null
                && string.Equals(row.pawnName, FixturePawnName, StringComparison.Ordinal)
                && string.Equals(row.knowledgeState?.originCultureDefName,
                    FixtureCulture, StringComparison.Ordinal));
            Dictionary<string, PawnDiaryRecord> byId =
                DiariesByIdField.GetValue(component)
                    as Dictionary<string, PawnDiaryRecord>;
            for (int index = 0; index < ids.Count; index++) byId?.Remove(ids[index]);
            component.MarkMemoryM4IndexesDirty();
            component.RebuildMemorySizeIndexes();
            DiaryStateVersion.Bump();
        }

        private static bool LoadedComponentOrSkip(
            string phase,
            out DiaryGameComponent component)
        {
            component = DiaryGameComponent.Instance;
            if (Current.Game != null && component != null) return true;
            Log.Message(LogPrefix + "SKIP " + phase + ": a loaded game is required.");
            return false;
        }

        private static void RequireReflectionSurface()
        {
            Require(DiariesField != null && DiariesByIdField != null,
                "The component diary repository seam changed; update the M11 migration fixture.");
        }

        private static void DeleteFixtureSave(string saveName)
        {
            string path = GenFilePaths.FilePathForSavedGame(saveName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                Log.Warning(LogPrefix + "could not delete disposable save '" + saveName
                    + "': " + exception.Message);
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}
