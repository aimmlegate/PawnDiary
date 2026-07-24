// Loaded-game regression fixtures for the reusable journal's session-only filter lifecycle. These tests do
// not draw Unity GUI: reflection invokes the exact private lifecycle seams with a hidden panel rect,
// which is enough to prove pawn/year changes cannot leave invisible filters active.
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
        private static readonly FieldInfo FilterPawnIdField =
            typeof(DiaryJournalView).GetField("filterPanelPawnId", PrivateInstance);
        private static readonly FieldInfo FavoritesOnlyField =
            typeof(DiaryJournalView).GetField("filterFavoritesOnly", PrivateInstance);
        private static readonly FieldInfo ActiveTagsField =
            typeof(DiaryJournalView).GetField("filterActiveTags", PrivateInstance);
        private static readonly FieldInfo SelectedYearField =
            typeof(DiaryJournalView).GetField("selectedYear", PrivateInstance);
        private static readonly MethodInfo DrawFilterPanelMethod =
            typeof(DiaryJournalView).GetMethod("DrawFilterPanel", PrivateInstance);
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

        /// <summary>A zero-width/hidden panel must still reset the shared tab when its pawn changes.</summary>
        [Test]
        public static void HiddenPanelResetsFiltersBeforeGeometryReturn()
        {
            DiaryJournalView journal = new DiaryJournalView();
            FilterPawnIdField.SetValue(journal, firstPawn.GetUniqueLoadID());
            FavoritesOnlyField.SetValue(journal, true);
            ActiveTags(journal).Add("Social");

            DrawFilterPanelMethod.Invoke(journal, new object[]
            {
                new Rect(0f, 0f, 0f, 100f),
                DiaryReaderSubject.FromPawn(secondPawn),
                scope.Component,
                null,
                null,
                null
            });

            PawnDiaryRimTestScope.Require(
                string.Equals(FilterPawnIdField.GetValue(journal) as string,
                    secondPawn.GetUniqueLoadID(), StringComparison.Ordinal),
                "The hidden Diary filter panel did not advance its pawn lifecycle key.");
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

        private static HashSet<string> ActiveTags(DiaryJournalView journal)
        {
            return ActiveTagsField.GetValue(journal) as HashSet<string>;
        }

        private static void RequireReflectionSeams()
        {
            PawnDiaryRimTestScope.Require(FilterPawnIdField != null && FavoritesOnlyField != null
                    && ActiveTagsField != null && SelectedYearField != null
                    && DrawFilterPanelMethod != null && SelectYearMethod != null,
                "The Diary filter fixture could not resolve one or more private lifecycle seams.");
        }
    }
}
