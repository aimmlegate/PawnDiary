// PawnDiaryMemoryM11NoDlcRuntimeTests.cs — base-game-only loaded M11 lifecycle coverage.
//
// RimTest has no skip result, so this fixture logs and returns when any paid DLC is active. The user
// must run it in a separate base-only profile; an all-DLC green run is not evidence for this branch.
using System;
using System.IO;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Current memory Scribe, Library, capture, and reset with no paid DLC active.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11NoDlcRuntimeTests
    {
        /// <summary>
        /// A base-only pawn completes current-state Scribe, all Library views, Brainwipe reclamation,
        /// and an ordinary base arrival capture without touching a DLC tracker or Def.
        /// </summary>
        [Test]
        public static void BaseOnlyProfileCompletesMemoryLifecycle()
        {
            if (ModsConfig.RoyaltyActive
                || ModsConfig.IdeologyActive
                || ModsConfig.BiotechActive
                || ModsConfig.AnomalyActive
                || ModsConfig.OdysseyActive)
            {
                Log.Message("[Pawn Diary RimTest] SKIP Memory M11 base-only lifecycle: disable all "
                    + "paid DLC and rerun this fixture.");
                return;
            }

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin("arrival");
            DiaryGameComponent component = scope.Component;
            PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot allocatorSnapshot = null;
            try
            {
                allocatorSnapshot =
                    new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(component);
                Pawn pawn = scope.CreateAdultColonist();
                string ownerId = pawn.GetUniqueLoadID();
                Require(string.IsNullOrEmpty(DlcContext.Xenotype(pawn))
                        && string.IsNullOrEmpty(DlcContext.XenotypeDefName(pawn))
                        && string.IsNullOrEmpty(DlcContext.RoyalTitle(pawn))
                        && string.IsNullOrEmpty(DlcContext.RoyalTitleDefName(pawn))
                        && string.IsNullOrEmpty(DlcContext.Ideoligion(pawn))
                        && string.IsNullOrEmpty(DlcContext.IdeologicalRole(pawn))
                        && DlcContext.IdeologyPreceptDefNames(pawn).Count == 0
                        && !DlcContext.IsCreepJoiner(pawn)
                        && !DlcContext.IsHauntedByUnnaturalCorpse(pawn)
                        && !DlcContext.IsGhoul(pawn),
                    "A paid-DLC accessor returned live data in the base-only profile.");

                PawnKnowledgeState state =
                    PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                        ownerId,
                        "PawnDiary_Base_Exact_Subject",
                        "Base subject",
                        31);
                string display = "PawnDiary M11 Base " + Guid.NewGuid().ToString("N");
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, pawn, state, display);
                RoundTripCurrentState(state);

                MemoryLibraryOwnerResult owners;
                MemoryLibraryOwnerRow owner =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, display, out owners);
                Require(PawnDiaryMemoryM11RuntimeFixture.RequireList(
                            scope.Component,
                            owner,
                            owners.directoryRevision,
                            MemoryLibraryViews.Threads).rows.Count == 1
                        && PawnDiaryMemoryM11RuntimeFixture.RequireList(
                            scope.Component,
                            owner,
                            owners.directoryRevision,
                            MemoryLibraryViews.Standalone).rows.Count == 1
                        && PawnDiaryMemoryM11RuntimeFixture.RequireList(
                            scope.Component,
                            owner,
                            owners.directoryRevision,
                            MemoryLibraryViews.Imported).rows.Count == 1,
                    "The base-only Library lost a current Memory view.");

                string oldEpoch = state.autobiographicalEpochToken;
                string origin = state.originCultureDefName;
                string adopted = state.adoptedCultureDefName;
                bool removedBackground = scope.Component.ForgetDiaryHistory(pawn);
                PawnKnowledgeState reset = scope.RequireDiaryRecord(pawn).knowledgeState;
                Require(!removedBackground
                        && reset != null
                        && reset.IsCurrentSchema()
                        && reset.epochFenceOnly
                        && reset.autobiographicalEpochToken != oldEpoch
                        && reset.threadRoots.Count == 0
                        && reset.standaloneBlocks.Count == 0
                        && reset.importedArchiveRows.Count == 0
                        && reset.originCultureDefName == origin
                        && reset.adoptedCultureDefName == adopted,
                    "The base-only reset lost culture/fence state or retained autobiography.");

                int pagesBefore = scope.RequireDiaryRecord(pawn).eventIds.Count;
                int memoriesBefore = reset.threadRoots.Count + reset.standaloneBlocks.Count;
                scope.Component.CaptureEventKnowledgeWithoutPage(
                    pawn,
                    null,
                    "PawnDiary_Arrival",
                    "arrival_description=true; arrival_pawn_id=" + ownerId
                        + "; arrival_source=rimtest_base",
                    Math.Max(0, Find.TickManager.TicksGame),
                    OrdinalSegmentCodec.Segment("rimtest-base-arrival-v1")
                        + OrdinalSegmentCodec.Segment(Guid.NewGuid().ToString("N")));
                int memoriesAfter = reset.threadRoots.Count + reset.standaloneBlocks.Count;
                bool captureAllowed = MemoryEffectivePolicyProvider.Current.AllowsCapture(
                    MemoryCategoryBits.Personal);
                Require(scope.RequireDiaryRecord(pawn).eventIds.Count == pagesBefore
                        && (captureAllowed
                            ? memoriesAfter > memoriesBefore
                            : memoriesAfter == memoriesBefore),
                    "Base arrival no-page capture ignored the effective capture gate or created a page.");
            }
            finally
            {
                try
                {
                    PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
                }
                finally
                {
                    allocatorSnapshot?.Restore(component);
                }
            }
        }

        private static void RoundTripCurrentState(PawnKnowledgeState source)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_m11_base_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                PawnKnowledgeState saved = source;
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref saved, "memory");
                Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;

                PawnKnowledgeState loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "memory");
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
                Require(loaded != null
                        && loaded.IsCurrentSchema()
                        && loaded.autobiographicalEpochToken
                            == source.autobiographicalEpochToken
                        && loaded.threadRoots.Count == 1
                        && loaded.standaloneBlocks.Count == 1
                        && loaded.importedArchiveRows.Count == 1,
                    "The base-only current Memory envelope did not round-trip exactly.");
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // A locked temp file must not hide the lifecycle assertion that came first.
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}
