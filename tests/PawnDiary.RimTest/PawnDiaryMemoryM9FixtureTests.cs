// Nonvisual loaded-assembly checks for the M9 Library lifecycle seams.
//
// These tests never open or draw a Window. They prove the static M8 callback and the Library's
// process-local session generation can be cleared between games, while the shipped build remains
// LegacyShadow. Live RimTest execution is intentionally left to the player-owned manual test run.
using System;
using System.Reflection;
using RimTestRedux;

namespace PawnDiary.RimTests
{
    /// <summary>Static activation and game-transition cleanup contracts for the M9 UI adapter.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM9FixtureTests
    {
        /// <summary>Reset removes a stale callback without exposing Library internals through settings.</summary>
        [Test]
        public static void MemoryLibraryCallbackResetIsExact()
        {
            Action prior = PawnDiaryMod.OpenMemoryLibraryAction;
            try
            {
                PawnDiaryMod.OpenMemoryLibraryAction = delegate { };
                PawnDiaryMod.ResetMemoryLibraryAction();
                Require(PawnDiaryMod.OpenMemoryLibraryAction == null,
                    "The M8 Library callback reset retained a stale delegate.");
                Require(MemorySystemActivationGate.BuildState
                        == MemorySystemActivationGate.LegacyShadow,
                    "M9 tests must not expose the Library by changing the activation gate.");
            }
            finally
            {
                PawnDiaryMod.OpenMemoryLibraryAction = prior;
            }
        }

        /// <summary>Game transition invalidates every previously opened detached Library session.</summary>
        [Test]
        public static void MemoryLibrarySessionGenerationAdvancesOnReset()
        {
            FieldInfo generation = typeof(Dialog_MemoryLibrary).GetField(
                "lifecycleGeneration", BindingFlags.Static | BindingFlags.NonPublic);
            Require(generation != null, "The M9 Library lifecycle generation seam was renamed.");
            long before = (long)generation.GetValue(null);
            Dialog_MemoryLibrary.ResetForGameTransition();
            long after = (long)generation.GetValue(null);
            Require(after > before || before == long.MaxValue,
                "Game-transition cleanup did not invalidate prior Library sessions.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
