using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryAdmissionRandomTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            DiaryAdmissionRandom first = new DiaryAdmissionRandom(0u);
            DiaryAdmissionRandom replay = new DiaryAdmissionRandom(0u);
            DiaryAdmissionRandom other = new DiaryAdmissionRandom(1u);
            DiaryAdmissionRandom psychotype = new DiaryAdmissionRandom(0u ^ 0xB5297A4Du);
            HashSet<float> seen = new HashSet<float>();
            bool differsFromOtherSeed = false;
            bool differsFromPsychotypeDomain = false;

            for (int i = 0; i < 64; i++)
            {
                float value = first.NextUnitFloat();
                float replayValue = replay.NextUnitFloat();
                float otherValue = other.NextUnitFloat();
                float psychotypeValue = psychotype.NextUnitFloat();

                Assert(value >= 0f && value < 1f,
                    "admission random stays inside the policy's half-open unit interval");
                Assert(value == replayValue,
                    "an admission stream reproduces when a pure fixture supplies the same seed");
                seen.Add(value);
                differsFromOtherSeed |= value != otherValue;
                differsFromPsychotypeDomain |= value != psychotypeValue;
            }

            Assert(seen.Count > 60,
                "successive admission draws evolve instead of replaying one restored RNG value");
            Assert(differsFromOtherSeed,
                "different component seeds do not collapse to one fixed admission sequence");
            Assert(differsFromPsychotypeDomain,
                "the psychotype domain stream does not replay page-admission decisions");

            Console.WriteLine("DiaryAdmissionRandomTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void Assert(bool condition, string message)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + message);
            }
        }
    }
}
