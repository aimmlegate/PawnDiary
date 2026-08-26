// PawnDiaryMemorySocialFamilyFactionTests.cs — loaded M11 exact-owner and exact-subject isolation.
//
// The fixture models a mixed Relationship/Family person root and two equal-label faction roots. It
// proves Library projection/filtering never merges by display text and never searches another POV.
using System;
using System.Linq;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exact private POV, mixed person categories, and equal-label faction identities.</summary>
    [TestSuite]
    public static class PawnDiaryMemorySocialFamilyFactionTests
    {
        /// <summary>
        /// Relationship and Family facts share one exact person root, while equal labels never merge
        /// different pawn/faction identities or leak wording across owners.
        /// </summary>
        [Test]
        public static void ExactOwnersAndSubjectsRemainIsolatedAcrossMixedCategories()
        {
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            try
            {
                Pawn socialOwnerPawn = scope.CreateAdultColonist();
                Pawn privateOwnerPawn = scope.CreateAdultColonist();
                Pawn factionOwnerPawn = scope.CreateAdultColonist();
                string socialOwner = socialOwnerPawn.GetUniqueLoadID();
                string privateOwner = privateOwnerPawn.GetUniqueLoadID();
                string factionOwner = factionOwnerPawn.GetUniqueLoadID();

                PawnKnowledgeState social =
                    PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                        socialOwner,
                        "Pawn_Exact_Counterpart_A",
                        "Alex",
                        21);
                SavedMemoryThreadRoot socialRoot = social.threadRoots[0];
                // Re-home this owner's own canonical Standalone record, preserving its record ID's
                // embedded owner/epoch identity while modeling mixed categories in one person root.
                SavedMemoryBlock family = social.standaloneBlocks[0];
                family.rootId = socialRoot.rootId;
                family.chapterId = socialRoot.chapters[0].chapterId;
                family.category = MemoryContractTokens.CategoryFamily;
                family.automaticWording = "M11 known relative evidence";
                social.standaloneBlocks.Clear();
                socialRoot.visibleBlocks.Add(family);
                socialRoot.structuralRevision++;
                social.structuralRevision++;
                SavedMemoryThreadRoot equalLabelOtherPerson =
                    PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                        social,
                        "Pawn_Exact_Counterpart_C",
                        "Alex",
                        22,
                        MemoryContractTokens.SubjectPawn,
                        MemoryContractTokens.CategoryRelationships);

                PawnKnowledgeState privatePov =
                    PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                        privateOwner,
                        "Pawn_Exact_Counterpart_B",
                        "Alex",
                        23);
                privatePov.threadRoots[0].visibleBlocks[0].automaticWording =
                    "M11 other POV private wording";
                string factionSubjectA;
                string factionSubjectB;
                Require(MemoryIdentityCodec.TryCreateFactionSubjectId(
                            "Faction_Exact_A", 1, out factionSubjectA),
                    "The first faction fixture could not create an exact subject.");
                Require(MemoryIdentityCodec.TryCreateFactionSubjectId(
                            "Faction_Exact_B", 2, out factionSubjectB),
                    "The second faction fixture could not create an exact subject.");
                PawnKnowledgeState factionA =
                    PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                        factionOwner,
                        factionSubjectA,
                        "The Union",
                        24,
                        0,
                        MemoryContractTokens.SubjectFaction);
                SavedMemoryThreadRoot factionRootB =
                    PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                        factionA,
                        factionSubjectB,
                        "The Union",
                        25,
                        MemoryContractTokens.SubjectFaction,
                        MemoryContractTokens.CategoryFactions);
                factionA.threadRoots[0].visibleBlocks[0].category =
                    MemoryContractTokens.CategoryFactions;

                string socialName = "PawnDiary M11 Social " + Guid.NewGuid().ToString("N");
                string privateName = "PawnDiary M11 Private " + Guid.NewGuid().ToString("N");
                string factionName = "PawnDiary M11 Faction " + Guid.NewGuid().ToString("N");
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, socialOwnerPawn, social, socialName);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, privateOwnerPawn, privatePov, privateName);
                PawnDiaryMemoryM11RuntimeFixture.InstallOwner(
                    scope, factionOwnerPawn, factionA, factionName);

                MemoryLibraryOwnerResult socialOwners;
                MemoryLibraryOwnerRow socialRow =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, socialName, out socialOwners);
                MemoryLibraryListResult socialThreads =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        socialRow,
                        socialOwners.directoryRevision,
                        MemoryLibraryViews.Threads);
                Require(socialThreads.rows.Count == 2,
                    "Equal person labels collapsed two exact subjects into one root.");
                MemoryThreadHeaderRow mixedHeader = socialThreads.rows
                    .Select(row => row.thread)
                    .Single(row => row.rootHandle.rootId == socialRoot.rootId);
                MemoryThreadHeaderRow otherPersonHeader = socialThreads.rows
                    .Select(row => row.thread)
                    .Single(row => row.rootHandle.rootId == equalLabelOtherPerson.rootId);
                Require(mixedHeader.chapterCount == 1
                        && mixedHeader.manageableMemoryCount == 2
                        && mixedHeader.subjectLabel == otherPersonHeader.subjectLabel,
                    "Mixed Relationship/Family evidence split or duplicated its exact person root.");

                MemoryRootHandle personRoot = mixedHeader.rootHandle;
                MemoryThreadDetailResult relationshipDetail =
                    scope.Component.QueryMemoryThreadDetail(new MemoryThreadDetailQuery
                    {
                        rootHandle = personRoot,
                        filters = new MemoryLibraryFilters
                        {
                            categoryMask = MemoryCategoryBits.Relationships
                        },
                        detailStart = 0,
                        detailCount = 64
                    });
                MemoryThreadDetailResult familyDetail =
                    scope.Component.QueryMemoryThreadDetail(new MemoryThreadDetailQuery
                    {
                        rootHandle = personRoot,
                        filters = new MemoryLibraryFilters
                        {
                            categoryMask = MemoryCategoryBits.Family
                        },
                        detailStart = 0,
                        detailCount = 64
                    });
                Require(relationshipDetail.status == MemoryLibraryStatuses.Ready
                        && relationshipDetail.blocks.Count == 1
                        && familyDetail.status == MemoryLibraryStatuses.Ready
                        && familyDetail.blocks.Count == 1
                        && relationshipDetail.blocks[0].recordHandle.recordId
                            != familyDetail.blocks[0].recordHandle.recordId,
                    "Category filtering merged or lost mixed person-root facts.");

                MemoryLibraryListResult leakedSearch =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        socialRow,
                        socialOwners.directoryRevision,
                        MemoryLibraryViews.Threads,
                        "other POV private wording");
                Require(leakedSearch.rows.Count == 0,
                    "A Library search crossed the selected owner's private POV boundary.");

                MemoryLibraryOwnerResult factionOwners;
                MemoryLibraryOwnerRow factionRow =
                    PawnDiaryMemoryM11RuntimeFixture.RequireOwnerRow(
                        scope.Component, factionName, out factionOwners);
                MemoryLibraryListResult factionThreads =
                    PawnDiaryMemoryM11RuntimeFixture.RequireList(
                        scope.Component,
                        factionRow,
                        factionOwners.directoryRevision,
                        MemoryLibraryViews.Threads);
                Require(factionThreads.rows.Count == 2
                        && factionRow.primaryHandle.exactOwnerPawnIdOrEmpty == factionOwner
                        && factionA.threadRoots[0].subjectId == factionSubjectA
                        && factionRootB.subjectId == factionSubjectB
                        && factionA.threadRoots[0].frozenSubjectLabel
                            == factionRootB.frozenSubjectLabel
                        && factionThreads.rows[0].thread.rootHandle.rootId
                            != factionThreads.rows[1].thread.rootHandle.rootId,
                    "Equal faction labels collapsed exact instances or owner handles.");
            }
            finally
            {
                PawnDiaryMemoryM11RuntimeFixture.ResetLibraryAndTearDown(scope);
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}
