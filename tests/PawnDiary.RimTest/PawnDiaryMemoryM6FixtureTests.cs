// Loaded Scribe fixtures for the M6 component-global current-truth boundary.
//
// The pure M6 harness owns reconciliation and identity policy. This fixture exercises the actual
// private component Scribe adapter with an inert, constructor-free shell, so it never starts an LLM
// session or needs a live map. Equal display labels deliberately prove that exact faction instance
// identity and allocator generations survive independently.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded persistence coverage for M6 component-global faction current truth.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM6FixtureTests
    {
        private static readonly MethodInfo ExposeMemoryComponentDataMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ExposeMemoryComponentData",
                BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// The component's real field tokens preserve the allocator high-water mark and two exact
        /// same-label faction instances as distinct rows across Scribe.
        /// </summary>
        [Test]
        public static void GlobalFactionCurrentTruthRoundTripsExactInstances()
        {
            Require(ExposeMemoryComponentDataMethod != null,
                "The M6 component Scribe seam was renamed or removed.");

            DiaryGameComponent source = NewPersistenceShell();
            SetPrivateField(source, "globalFactionSnapshotAllocatorGeneration", 29L);
            SetPrivateField(source, "globalFactionSnapshots",
                new List<SavedGlobalFactionSnapshot>
                {
                    new SavedGlobalFactionSnapshot
                    {
                        factionInstanceId = "Faction_Exact_A",
                        allocatorGeneration = 7,
                        factionDefName = "OutlanderCivil",
                        frozenDisplayLabel = "The Union",
                        goodwill = 45,
                        relationKindToken = "ally",
                        leaderPawnId = "Pawn_Leader_A",
                        observedTick = 123456,
                        trackingStateToken = "tracked",
                        snapshotRevision = 3
                    },
                    new SavedGlobalFactionSnapshot
                    {
                        factionInstanceId = "Faction_Exact_B",
                        allocatorGeneration = 8,
                        factionDefName = "OutlanderCivil",
                        frozenDisplayLabel = "The Union",
                        goodwill = -80,
                        relationKindToken = "hostile",
                        leaderPawnId = "Pawn_Leader_B",
                        defeated = true,
                        removed = true,
                        observedTick = 234567,
                        trackingStateToken = "tracked",
                        snapshotRevision = 4
                    }
                });

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => ExposeMemoryComponentDataMethod.Invoke(source, null));

                DiaryGameComponent loaded = NewPersistenceShell();
                LoadVarsWithScribe(path,
                    () => ExposeMemoryComponentDataMethod.Invoke(loaded, null));

                long allocator = GetPrivateField<long>(
                    loaded, "globalFactionSnapshotAllocatorGeneration");
                List<SavedGlobalFactionSnapshot> rows =
                    GetPrivateField<List<SavedGlobalFactionSnapshot>>(
                        loaded, "globalFactionSnapshots");

                Require(allocator == 29,
                    "The component-global faction allocator high-water mark did not round-trip.");
                Require(rows != null && rows.Count == 2,
                    "The component-global faction snapshot list did not round-trip both rows.");

                SavedGlobalFactionSnapshot first = rows[0];
                SavedGlobalFactionSnapshot second = rows[1];
                Require(first.factionInstanceId == "Faction_Exact_A"
                        && first.allocatorGeneration == 7
                        && first.factionDefName == "OutlanderCivil"
                        && first.frozenDisplayLabel == "The Union"
                        && first.goodwill == 45
                        && first.relationKindToken == "ally"
                        && first.leaderPawnId == "Pawn_Leader_A"
                        && first.observedTick == 123456
                        && first.trackingStateToken == "tracked"
                        && first.snapshotRevision == 3,
                    "The first exact faction snapshot lost current-truth fields.");
                Require(second.factionInstanceId == "Faction_Exact_B"
                        && second.allocatorGeneration == 8
                        && second.factionDefName == "OutlanderCivil"
                        && second.frozenDisplayLabel == "The Union"
                        && second.goodwill == -80
                        && second.relationKindToken == "hostile"
                        && second.leaderPawnId == "Pawn_Leader_B"
                        && second.defeated
                        && second.removed
                        && second.observedTick == 234567
                        && second.trackingStateToken == "tracked"
                        && second.snapshotRevision == 4,
                    "The second exact faction snapshot lost current-truth fields.");
                Require(first.factionInstanceId != second.factionInstanceId
                        && first.allocatorGeneration != second.allocatorGeneration,
                    "Equal faction labels collapsed distinct exact instances across Scribe.");
            });
        }

        private static DiaryGameComponent NewPersistenceShell()
        {
            // The real constructor starts an LLM/game session. Scribe needs only field storage here.
            return (DiaryGameComponent)FormatterServices.GetUninitializedObject(
                typeof(DiaryGameComponent));
        }

        private static void SetPrivateField<T>(DiaryGameComponent component, string name, T value)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Missing private component field '" + name + "'.");
            field.SetValue(component, value);
        }

        private static T GetPrivateField<T>(DiaryGameComponent component, string name)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Missing private component field '" + name + "'.");
            return (T)field.GetValue(component);
        }

        private static void RunWithTempFile(Action<string> body)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_mem_m6_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                body(path);
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // A locked temp file must not conceal the fixture assertion that came first.
                }
            }
        }

        private static void SaveWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                started = true;
                expose();
            }
            finally
            {
                if (started) Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private static void LoadVarsWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.loader.InitLoading(path);
                started = true;
                Scribe.mode = LoadSaveMode.LoadingVars;
                expose();
            }
            finally
            {
                if (started) Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
