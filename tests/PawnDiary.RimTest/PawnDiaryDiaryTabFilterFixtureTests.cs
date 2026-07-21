// Loaded-game regression fixtures for the Diary tab's session-only filter lifecycle. These tests do
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
            typeof(ITab_Pawn_Diary).GetField("filterPanelPawnId", PrivateInstance);
        private static readonly FieldInfo FavoritesOnlyField =
            typeof(ITab_Pawn_Diary).GetField("filterFavoritesOnly", PrivateInstance);
        private static readonly FieldInfo ActiveTagsField =
            typeof(ITab_Pawn_Diary).GetField("filterActiveTags", PrivateInstance);
        private static readonly FieldInfo SelectedYearField =
            typeof(ITab_Pawn_Diary).GetField("selectedYear", PrivateInstance);
        private static readonly MethodInfo DrawFilterPanelMethod =
            typeof(ITab_Pawn_Diary).GetMethod("DrawFilterPanel", PrivateInstance);
        private static readonly MethodInfo SelectYearMethod =
            typeof(ITab_Pawn_Diary).GetMethod("SelectYear", PrivateInstance);

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
            ITab_Pawn_Diary tab = new ITab_Pawn_Diary();
            FilterPawnIdField.SetValue(tab, firstPawn.GetUniqueLoadID());
            FavoritesOnlyField.SetValue(tab, true);
            ActiveTags(tab).Add("Social");

            DrawFilterPanelMethod.Invoke(tab, new object[]
            {
                new Rect(0f, 0f, 0f, 100f),
                secondPawn,
                scope.Component,
                null,
                null,
                null
            });

            PawnDiaryRimTestScope.Require(
                string.Equals(FilterPawnIdField.GetValue(tab) as string,
                    secondPawn.GetUniqueLoadID(), StringComparison.Ordinal),
                "The hidden Diary filter panel did not advance its pawn lifecycle key.");
            PawnDiaryRimTestScope.Require(!(bool)FavoritesOnlyField.GetValue(tab)
                    && ActiveTags(tab).Count == 0,
                "The hidden Diary filter panel leaked the previous pawn's active filters.");
        }

        /// <summary>Changing years clears only year-specific tag chips, not favorites-only selection.</summary>
        [Test]
        public static void YearChangeClearsInvisibleTagSelections()
        {
            ITab_Pawn_Diary tab = new ITab_Pawn_Diary();
            SelectedYearField.SetValue(tab, 5501);
            FavoritesOnlyField.SetValue(tab, true);
            ActiveTags(tab).Add("Raid");

            SelectYearMethod.Invoke(tab, new object[] { 5502 });

            PawnDiaryRimTestScope.Require((int)SelectedYearField.GetValue(tab) == 5502,
                "The Diary tab did not select the requested year.");
            PawnDiaryRimTestScope.Require(ActiveTags(tab).Count == 0,
                "A tag absent from the new year remained invisibly active.");
            PawnDiaryRimTestScope.Require((bool)FavoritesOnlyField.GetValue(tab),
                "Changing years unexpectedly cleared the independent favorites-only filter.");
        }

        private static HashSet<string> ActiveTags(ITab_Pawn_Diary tab)
        {
            return ActiveTagsField.GetValue(tab) as HashSet<string>;
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
