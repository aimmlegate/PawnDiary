// Loaded-game regression fixtures for the reusable journal's per-pawn filter lifecycle. These tests do
// not draw Unity GUI: reflection invokes the exact private state-transition seams, which is enough to
// prove a first visit starts clean, year changes cannot leave invisible filters active, and the
// filter-panel prompt selector never adds its pair fixture to the context partner's diary.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using UnityEngine;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Pins hidden-panel pawn reset and year-specific tag reset behavior.</summary>
    [TestSuite]
    public static class PawnDiaryDiaryTabFilterFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo ActivePawnIdField =
            typeof(DiaryJournalView).GetField("activePawnUiStateId", PrivateInstance);
        private static readonly FieldInfo FavoritesOnlyField =
            typeof(DiaryJournalView).GetField("filterFavoritesOnly", PrivateInstance);
        private static readonly FieldInfo ActiveTagsField =
            typeof(DiaryJournalView).GetField("filterActiveTags", PrivateInstance);
        private static readonly FieldInfo SelectedYearField =
            typeof(DiaryJournalView).GetField("selectedYear", PrivateInstance);
        private static readonly MethodInfo ActivatePawnStateMethod =
            typeof(DiaryJournalView).GetMethod("ActivatePawnUiState", PrivateInstance);
        private static readonly MethodInfo SelectYearMethod =
            typeof(DiaryJournalView).GetMethod("SelectYear", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
            RequireReflectionSeams();
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>A first visit to another pawn starts with clean filters even before any geometry draw.</summary>
        [Test]
        public static void HiddenPanelResetsFiltersBeforeGeometryReturn()
        {
            DiaryJournalView journal = new DiaryJournalView();
            ActivatePawnStateMethod.Invoke(journal, new object[] { firstPawn.GetUniqueLoadID() });
            FavoritesOnlyField.SetValue(journal, true);
            ActiveTags(journal).Add("Social");

            ActivatePawnStateMethod.Invoke(journal, new object[] { secondPawn.GetUniqueLoadID() });

            PawnDiaryRimTestScope.Require(
                string.Equals(ActivePawnIdField.GetValue(journal) as string,
                    secondPawn.GetUniqueLoadID(), StringComparison.Ordinal),
                "The Diary journal did not advance its active per-pawn state key.");
            PawnDiaryRimTestScope.Require(!(bool)FavoritesOnlyField.GetValue(journal)
                    && ActiveTags(journal).Count == 0,
                "The hidden Diary filter panel leaked the previous pawn's active filters.");
        }

        /// <summary>Changing years clears only year-specific tag chips, not favorites-only selection.</summary>
        [Test]
        public static void YearChangeClearsInvisibleTagSelections()
        {
            DiaryJournalView journal = new DiaryJournalView();
            SelectedYearField.SetValue(journal, 5501);
            FavoritesOnlyField.SetValue(journal, true);
            ActiveTags(journal).Add("Raid");

            SelectYearMethod.Invoke(journal, new object[] { 5502 });

            PawnDiaryRimTestScope.Require((int)SelectedYearField.GetValue(journal) == 5502,
                "The Diary journal did not select the requested year.");
            PawnDiaryRimTestScope.Require(ActiveTags(journal).Count == 0,
                "A tag absent from the new year remained invisibly active.");
            PawnDiaryRimTestScope.Require((bool)FavoritesOnlyField.GetValue(journal),
                "Changing years unexpectedly cleared the independent favorites-only filter.");
        }

        /// <summary>
        /// A pair fixture may read the second pawn for realistic context, but the filter selector owns
        /// and queues the prompt only for the pawn whose diary is currently open.
        /// </summary>
        [Test]
        public static void PromptSelectorPairFixtureAddsOnlyTheSelectedPawnDiaryReference()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);

            DiaryGameComponent.DevPromptSuiteEntry pairEntry = FirstPairEntry();
            PawnDiaryRimTestScope.Require(pairEntry != null,
                "The prompt-suite catalog did not expose a pair fixture.");
            bool shown = scope.Component.ShowPromptSuiteEntryForCurrentPawnForDev(
                firstPawn,
                pairEntry,
                secondPawn);

            PawnDiaryRimTestScope.Require(shown,
                "The current-pawn prompt selector did not build its pair fixture.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(firstPawn).eventIds.Count == 1,
                "The selected pawn did not receive exactly one prompt-fixture diary reference.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(secondPawn).eventIds.Count == 0,
                "The context partner incorrectly received the selected pawn's prompt fixture.");
        }

        /// <summary>
        /// Purging one pawn removes that pawn's diary reference while preserving a shared pair page still
        /// owned by the other pawn.
        /// </summary>
        [Test]
        public static void PurgeHistoryAffectsOnlyTheSelectedPawn()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);

            DiaryGameComponent.DevPromptSuiteEntry pairEntry = FirstPairEntry();
            PawnDiaryRimTestScope.Require(pairEntry != null,
                "The prompt-suite catalog did not expose a pair fixture.");
            bool shown = scope.Component.ShowPromptSuiteEntryForDev(
                firstPawn,
                pairEntry,
                secondPawn);
            PawnDiaryRimTestScope.Require(shown
                    && scope.RequireDiaryRecord(firstPawn).eventIds.Count == 1
                    && scope.RequireDiaryRecord(secondPawn).eventIds.Count == 1,
                "The shared pair fixture did not establish both pre-purge diary references.");

            int removed = scope.Component.PurgeDiaryHistoryForPawnForDev(firstPawn);

            PawnDiaryRimTestScope.Require(removed == 1,
                "The purge did not report the selected pawn's single removed page.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(firstPawn).eventIds.Count == 0,
                "The purge left a page in the selected pawn's diary.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(secondPawn).eventIds.Count == 1,
                "The purge incorrectly removed the other pawn's shared pair page.");
        }

        private static HashSet<string> ActiveTags(DiaryJournalView journal)
        {
            return ActiveTagsField.GetValue(journal) as HashSet<string>;
        }

        private static DiaryGameComponent.DevPromptSuiteEntry FirstPairEntry()
        {
            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries =
                DiaryGameComponent.AllSuiteEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i]?.pair == true)
                {
                    return entries[i];
                }
            }

            return null;
        }

        private static void RequireReflectionSeams()
        {
            PawnDiaryRimTestScope.Require(ActivePawnIdField != null && FavoritesOnlyField != null
                    && ActiveTagsField != null && SelectedYearField != null
                    && ActivatePawnStateMethod != null && SelectYearMethod != null,
                "The Diary filter fixture could not resolve one or more private lifecycle seams.");
        }
    }
}
