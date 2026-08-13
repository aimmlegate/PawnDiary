// Focused pure coverage for rebuilding transient ambient one-per-day guards from saved hot and
// archived history. No RimWorld state is needed: both stores project the same detached fields into
// DailyEmissionGuardPolicy after load.
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDailyEmissionGuardPolicy()
        {
            string interactionKey;
            string thoughtKey;
            bool recognized = DailyEmissionGuardPolicy.TryBuildCurrentDayKeys(
                "Pawn_A",
                "QuietSocialDay",
                "group=quietSocial; batch=ambient_day_note; events=3; day=44",
                fallbackDay: 44,
                currentDay: 44,
                out interactionKey,
                out thoughtKey);
            AssertTrue("saved hot ambient interaction is recognized", recognized);
            AssertEqual(
                "saved hot ambient interaction rebuilds its exact runtime key",
                "quietSocial|ambient|Pawn_A|44",
                interactionKey);
            AssertEqual("interaction history does not build a thought key", string.Empty, thoughtKey);

            recognized = DailyEmissionGuardPolicy.TryBuildCurrentDayKeys(
                "Pawn_B",
                DailyEmissionGuardPolicy.AmbientThoughtDefName,
                "thought=ThoughtAmbientDay; batch=ambient_day_note; events=2; day=44",
                fallbackDay: 44,
                currentDay: 44,
                out interactionKey,
                out thoughtKey);
            AssertTrue("saved archived ambient thought is recognized", recognized);
            AssertEqual("thought history does not build an interaction key", string.Empty, interactionKey);
            AssertEqual(
                "saved archived ambient thought rebuilds its exact runtime key",
                "thoughtAmbient|Pawn_B|44",
                thoughtKey);

            // A note can flush just after midnight. Its saved day marker, not the later event tick's
            // fallback day, owns the one-per-day guard and keeps the current-day set bounded.
            recognized = DailyEmissionGuardPolicy.TryBuildCurrentDayKeys(
                "Pawn_C",
                "QuietSocialDay",
                "group=quietSocial; batch=ambient_day_note; day=43",
                fallbackDay: 44,
                currentDay: 44,
                out interactionKey,
                out thoughtKey);
            AssertTrue("previous-day late flush is not rebuilt into today's guards", !recognized);

            recognized = DailyEmissionGuardPolicy.TryBuildCurrentDayKeys(
                "Pawn_D",
                "Chat",
                "group=quietSocial; batch=pair; day=44",
                fallbackDay: 44,
                currentDay: 44,
                out interactionKey,
                out thoughtKey);
            AssertTrue("ordinary saved event cannot become an ambient guard", !recognized);

            string rejectedKey = DailyEmissionGuardPolicy.InteractionKey(
                "quietSocial", "Pawn_E", 44);
            AssertTrue(
                "a persisted current-day frequency rejection is recognized",
                DailyEmissionGuardPolicy.IsInteractionKeyForDay(rejectedKey, 44));
            AssertTrue(
                "a persisted frequency rejection expires after its day",
                !DailyEmissionGuardPolicy.IsInteractionKeyForDay(rejectedKey, 45));
            AssertTrue(
                "an unrelated pipe-delimited token cannot impersonate an interaction guard",
                !DailyEmissionGuardPolicy.IsInteractionKeyForDay("quietSocial|pair|Pawn_E|44", 44));
            AssertTrue(
                "a malformed frequency-rejection day fails closed",
                !DailyEmissionGuardPolicy.IsInteractionKeyForDay(
                    "quietSocial|ambient|Pawn_E|not-a-day", 44));
        }
    }
}
