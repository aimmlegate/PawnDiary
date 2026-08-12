// Runtime-boundary regression tests for optional-mod Hediff label getters. These tests need the
// RimWorld assemblies for Verse.Hediff, but they do not mutate a loaded game or require a real pawn.
using System;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a third-party hediff whose virtual label getter throws cannot escape the guarded
    /// capture boundary, while an ordinary label still passes through unchanged.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryHediffLabelCaptureTests
    {
        private const string StableLabel = "stable test condition";

        /// <summary>A type-initialization fault degrades both label forms to optional empty text.</summary>
        [Test]
        public static void ThrowingThirdPartyLabelDegradesToEmpty()
        {
            Hediff broken = new ThrowingLabelHediff();

            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(HediffLabelCapture.ReadLabel(broken)),
                "A throwing Hediff.Label getter escaped the guarded label boundary.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(HediffLabelCapture.ReadLabelCap(broken)),
                "A throwing Hediff.Label getter escaped through Hediff.LabelCap.");
        }

        /// <summary>A healthy virtual label is not discarded or rewritten by the guard.</summary>
        [Test]
        public static void HealthyLabelPassesThroughUnchanged()
        {
            string actual = HediffLabelCapture.ReadLabel(new StableLabelHediff());
            PawnDiaryRimTestScope.Require(
                string.Equals(actual, StableLabel, StringComparison.Ordinal),
                "The guarded label boundary changed a healthy Hediff.Label value.");
        }

        private sealed class ThrowingLabelHediff : Hediff
        {
            public override string Label
            {
                get
                {
                    throw new TypeInitializationException(
                        "OptionalMod.BrokenInitializer",
                        new InvalidOperationException("intentional regression-test fault"));
                }
            }
        }

        private sealed class StableLabelHediff : Hediff
        {
            public override string Label => StableLabel;
        }
    }
}
