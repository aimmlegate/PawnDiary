// Focused pure coverage for the loaded hot-event repair plan.
//
// The fixtures use only detached identity facts. RimWorld/Scribe deserialization and mutation stay
// in DiaryEventRepository, which maps these selected source indexes back to real save models.
using System.Collections.Generic;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestLoadedEventRepairPolicy()
        {
            LoadedEventRepairPlan plan = LoadedEventRepairPolicy.Plan(
                new List<LoadedEventIdentity>
                {
                    Row(0, 50, "later", repaired: true),
                    Row(1, 10, "duplicate"),
                    null,
                    Row(3, 10, "equal-tick"),
                    Row(4, 5, "DUPLICATE", repaired: true),
                    Row(5, 1, " "),
                    Row(6, 10, "tail", repaired: true)
                });

            AssertSequence(
                "loaded repair keeps unique valid rows in stable tick order",
                new[] { 4, 3, 6, 0 },
                plan.retainedSourceIndexes);
            AssertSequence(
                "loaded repair reports only retained rows whose IDs were minted on load",
                new[] { 4, 6, 0 },
                plan.repairedIdSourceIndexes);

            plan = LoadedEventRepairPolicy.Plan(
                new List<LoadedEventIdentity>
                {
                    Row(0, 20, "first"),
                    Row(1, 20, "second"),
                    Row(2, 20, "third")
                });
            AssertSequence(
                "equal-tick rows retain original source order",
                new[] { 0, 1, 2 },
                plan.retainedSourceIndexes);

            plan = LoadedEventRepairPolicy.Plan(null);
            AssertEqual(
                "null loaded identity input yields an empty retained plan",
                0,
                plan.retainedSourceIndexes.Count);
            AssertEqual(
                "null loaded identity input yields an empty repaired-id plan",
                0,
                plan.repairedIdSourceIndexes.Count);

            plan = LoadedEventRepairPolicy.Plan(
                new List<LoadedEventIdentity>
                {
                    Row(-1, 1, "invalid-source"),
                    Row(1, 1, null),
                    Row(9, 1, "out-of-range"),
                    Row(3, 1, "valid"),
                    Row(3, 2, "repeated-source")
                });
            AssertSequence(
                "invalid, out-of-range, and repeated source indexes plus blank IDs are rejected",
                new[] { 3 },
                plan.retainedSourceIndexes);
        }

        private static LoadedEventIdentity Row(
            int sourceIndex,
            int tick,
            string eventId,
            bool repaired = false)
        {
            return new LoadedEventIdentity
            {
                sourceIndex = sourceIndex,
                tick = tick,
                eventId = eventId,
                eventIdWasRepairedOnLoad = repaired
            };
        }

        private static void AssertSequence(
            string name,
            IList<int> expected,
            IList<int> actual)
        {
            AssertEqual(name + " count", expected.Count, actual.Count);
            for (int i = 0; i < expected.Count && i < actual.Count; i++)
            {
                AssertEqual(name + " item " + i, expected[i], actual[i]);
            }
        }
    }
}
