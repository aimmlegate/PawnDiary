// Pure identity check for durable Biotech growth pages. Runtime adapters may find a page in the hot
// event store or the compact archive; this helper verifies the stable defName, child ID, and birthday
// age without knowing which pawn owned the first-person page.
using System;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>Matches one saved event/context tuple to an exact child growth moment.</summary>
    internal static class GrowthRecordPolicy
    {
        /// <summary>
        /// Returns true only for the canonical growth def and exact saved child/age fields.
        /// </summary>
        public static bool Matches(
            string interactionDefName,
            string recordedChildId,
            string recordedBirthdayAge,
            string expectedChildId,
            int expectedBirthdayAge)
        {
            string childId = (expectedChildId ?? string.Empty).Trim();
            return childId.Length > 0
                && BiotechGrowthStageTokens.ForAge(expectedBirthdayAge).Length > 0
                && string.Equals(
                    interactionDefName,
                    BiotechEventDefNames.GrowthMoment,
                    StringComparison.Ordinal)
                && string.Equals(recordedChildId, childId, StringComparison.Ordinal)
                && string.Equals(
                    recordedBirthdayAge,
                    expectedBirthdayAge.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
        }
    }
}
