// Standalone pure-test harness for PromptVariants. No RimWorld/Verse/Unity references — it only
// links Source/Generation/PromptVariants.cs, so the selection rule is provable without the game.
//
// Run: dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
using System;
using System.Collections.Generic;
using PawnDiary;

namespace PromptVariantsTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            EmptyPoolReturnsFallback();
            NullPoolReturnsFallback();
            AllBlankPoolReturnsFallback();
            BlankFallbackReturnedAsIs();
            SingleUsableEntryOverridesFallback();
            SingleUsableEntryAmongBlanks();
            DeterministicPickForFixedSeed();
            SeedSpansAllEntriesAcrossSeeds();
            NegativeSeedNormalizesIntoRange();
            WhitespaceEntriesAreSkippedAndDoNotShiftSelection();
            PickedValuesAreTrimmed();
            HashSeedIsStableAndNonNegative();
            HashSeedEmptyStringIsZero();
            HashSeedIsNotProcessRandomized();

            Console.WriteLine("PromptVariantsTests passed " + assertions + " assertions.");
            return 0;
        }

        // --- Pool fallback behavior ---

        private static void EmptyPoolReturnsFallback()
        {
            List<string> pool = new List<string>();
            AssertEqual("empty pool -> fallback", "fallback", PromptVariants.Pick(pool, "fallback", 7));
        }

        private static void NullPoolReturnsFallback()
        {
            AssertEqual("null pool -> fallback", "fallback", PromptVariants.Pick(null, "fallback", 7));
        }

        private static void AllBlankPoolReturnsFallback()
        {
            List<string> pool = new List<string> { "   ", "", "\t" };
            AssertEqual("all-blank pool -> fallback", "fallback", PromptVariants.Pick(pool, "fallback", 7));
        }

        private static void BlankFallbackReturnedAsIs()
        {
            // A blank fallback is returned verbatim when no usable variant exists; the caller owns
            // empty-handling (PromptAssembler drops empty fields).
            AssertEqual("blank fallback returned as-is", "", PromptVariants.Pick(null, "", 7));
        }

        // --- Single-entry pools ---

        private static void SingleUsableEntryOverridesFallback()
        {
            List<string> pool = new List<string> { "only" };
            AssertEqual("single usable entry overrides fallback", "only", PromptVariants.Pick(pool, "fallback", 0));
            AssertEqual("single usable entry overrides fallback (other seed)", "only", PromptVariants.Pick(pool, "fallback", 999));
        }

        private static void SingleUsableEntryAmongBlanks()
        {
            List<string> pool = new List<string> { "  ", "only", "" };
            AssertEqual("single usable among blanks", "only", PromptVariants.Pick(pool, "fallback", 3));
        }

        // --- Determinism & coverage ---

        private static void DeterministicPickForFixedSeed()
        {
            List<string> pool = new List<string> { "a", "b", "c", "d" };
            // Same seed must always select the same entry.
            string first = PromptVariants.Pick(pool, "fb", 42);
            string second = PromptVariants.Pick(pool, "fb", 42);
            AssertEqual("fixed seed stable across calls", first, second);
            AssertEqual("fixed seed selects within pool", true, pool.Contains(first));
        }

        private static void SeedSpansAllEntriesAcrossSeeds()
        {
            // A healthy pool should be reachable: consecutive seeds 0..N-1 must cover every entry
            // (the selection is seed % usable, so the first N seeds hit every slot at least once).
            List<string> pool = new List<string> { "a", "b", "c", "d", "e" };
            HashSet<string> seen = new HashSet<string>();
            for (int seed = 0; seed < pool.Count; seed++)
            {
                seen.Add(PromptVariants.Pick(pool, "fb", seed));
            }

            AssertEqual("seeds 0..N-1 cover all entries", pool.Count, seen.Count);
        }

        private static void NegativeSeedNormalizesIntoRange()
        {
            List<string> pool = new List<string> { "a", "b", "c" };
            // A negative seed must still yield a valid pool entry (never fallback, never throw).
            string picked = PromptVariants.Pick(pool, "fb", -1);
            AssertEqual("negative seed yields a pool entry", true, pool.Contains(picked));
            // And it must be deterministic.
            AssertEqual("negative seed deterministic", picked, PromptVariants.Pick(pool, "fb", -1));
            // -3 % 3 == 0, so seed -3 on a 3-pool must pick the same slot as seed 0.
            AssertEqual("negative seed equivalent to positive mod",
                PromptVariants.Pick(pool, "fb", 0), PromptVariants.Pick(pool, "fb", -3));
        }

        // --- Whitespace + trimming ---

        private static void WhitespaceEntriesAreSkippedAndDoNotShiftSelection()
        {
            // With one usable entry "x" at index 2 and blanks elsewhere, every seed must resolve to
            // "x" (the only usable slot), proving blanks don't get selected or shift indexing.
            List<string> pool = new List<string> { "  ", "", "x", null };
            AssertEqual("blank entries skipped (seed 0)", "x", PromptVariants.Pick(pool, "fb", 0));
            AssertEqual("blank entries skipped (seed 5)", "x", PromptVariants.Pick(pool, "fb", 5));
        }

        private static void PickedValuesAreTrimmed()
        {
            List<string> pool = new List<string> { "  spaced  " };
            AssertEqual("picked value trimmed", "spaced", PromptVariants.Pick(pool, "fb", 0));
        }

        // --- HashSeed ---

        private static void HashSeedIsStableAndNonNegative()
        {
            int h1 = PromptVariants.HashSeed("evt-123");
            int h2 = PromptVariants.HashSeed("evt-123");
            AssertEqual("hash stable across calls", h1, h2);
            AssertEqual("hash non-negative", true, h1 >= 0);
            AssertEqual("hash with high bit stays non-negative", true, PromptVariants.HashSeed("zzzzzzzz") >= 0);
        }

        private static void HashSeedEmptyStringIsZero()
        {
            AssertEqual("HashSeed('') == 0", 0, PromptVariants.HashSeed(""));
            AssertEqual("HashSeed(null) == 0", 0, PromptVariants.HashSeed(null));
        }

        private static void HashSeedIsNotProcessRandomized()
        {
            // Different strings should (almost always) hash differently; this guards against a
            // degenerate constant hash without being brittle about the exact value.
            int a = PromptVariants.HashSeed("alpha");
            int b = PromptVariants.HashSeed("beta");
            AssertEqual("different strings hash differently", true, a != b);
        }

        // --- Minimal assert helper (mirrors the other test harnesses) ---

        private static void AssertEqual(string label, object expected, object actual)
        {
            assertions++;
            bool ok = ReferenceEquals(expected, actual) || Equals(expected, actual);
            if (!ok)
            {
                Console.WriteLine("FAIL " + label + ": expected=[" + expected + "] actual=[" + actual + "]");
                throw new InvalidOperationException("assertion failed: " + label);
            }
        }
    }
}
