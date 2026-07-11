// Pure unit tests for the VSIE gathering bridge's decision logic. Mirrors the other tests/* console
// projects: a static Main runs focused assertions and returns non-zero when any fail.
//
// These run without RimWorld/Verse/Unity — the logic under test (gathering defName -> capture plan) is
// deliberately pure so its edge cases (which gatherings are captured, exact-match behavior, null input)
// are covered without booting the game.
using System;
using System.Collections.Generic;
using PawnDiaryVsie.Pure;

namespace VsieBridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestBirthdayPlan();
            TestFuneralPlan();
            TestKeysAreDistinctAndPrefixed();
            TestFlavorGatheringsNotCaptured();
            TestExactMatchOnly();
            TestNullAndBlankReturnNull();
            TestConstantsMatchDefNames();

            Console.WriteLine();
            Console.WriteLine("VsieBridgeLogicTests: " + passed + " passed, " + failed + " failed.");
            return failed > 0 ? 1 : 0;
        }

        private static void TestBirthdayPlan()
        {
            VsieGatheringPlan plan = VsieGatheringMap.Plan("VSIE_BirthdayParty");
            Check("birthday -> non-null plan", plan != null);
            Check("birthday eventKey", plan != null && plan.EventKey == "vsie_birthday");
            Check("birthday labelKey", plan != null && plan.LabelKey == "PawnDiaryVsie.Event.BirthdayLabel");
            Check("birthday summaryKey", plan != null && plan.SummaryKey == "PawnDiaryVsie.Event.BirthdaySummary");
        }

        private static void TestFuneralPlan()
        {
            VsieGatheringPlan plan = VsieGatheringMap.Plan("VSIE_Funeral");
            Check("funeral -> non-null plan", plan != null);
            Check("funeral eventKey", plan != null && plan.EventKey == "vsie_funeral");
            Check("funeral labelKey", plan != null && plan.LabelKey == "PawnDiaryVsie.Event.FuneralLabel");
            Check("funeral summaryKey", plan != null && plan.SummaryKey == "PawnDiaryVsie.Event.FuneralSummary");
        }

        private static void TestKeysAreDistinctAndPrefixed()
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (string defName in new[] { "VSIE_BirthdayParty", "VSIE_Funeral" })
            {
                VsieGatheringPlan plan = VsieGatheringMap.Plan(defName);
                Check(defName + " eventKey uses the vsie_ prefix", plan != null && plan.EventKey.StartsWith("vsie_"));
                keys.Add(plan == null ? null : plan.EventKey);
            }

            Check("the two eventKeys are distinct", keys.Count == 2);
        }

        private static void TestFlavorGatheringsNotCaptured()
        {
            // Every other VSIE gathering (verified from the installed 1.6 GatheringDefs) must be left to
            // the ambient thought capture, so Plan returns null for them.
            string[] flavor =
            {
                "VSIE_MealTogether", "VSIE_MovieNight", "VSIE_BuildingSnowmen", "VSIE_ViewingArtTogether",
                "VSIE_Skygazing", "VSIE_GoingForAWalk", "VSIE_GrabbingBeer", "VSIE_BingeParty", "VSIE_OutdoorParty",
            };
            foreach (string defName in flavor)
            {
                Check(defName + " is not captured (null plan)", VsieGatheringMap.Plan(defName) == null);
            }
        }

        private static void TestExactMatchOnly()
        {
            // GatheringDef defNames are exact, case-sensitive identifiers. A different casing must NOT
            // match, so we never accidentally claim a look-alike def from another mod.
            Check("lower-case birthday does not match", VsieGatheringMap.Plan("vsie_birthdayparty") == null);
            Check("upper-case funeral does not match", VsieGatheringMap.Plan("VSIE_FUNERAL") == null);
            Check("unknown def -> null", VsieGatheringMap.Plan("SomeOtherMod_Gathering") == null);
        }

        private static void TestNullAndBlankReturnNull()
        {
            Check("null -> null", VsieGatheringMap.Plan(null) == null);
            Check("empty -> null", VsieGatheringMap.Plan(string.Empty) == null);
        }

        private static void TestConstantsMatchDefNames()
        {
            // The defName and eventKey constants are the single source of truth shared by the External
            // group XML and the settings toggles, so the plan a defName resolves to must carry the
            // matching eventKey constant.
            Check("BirthdayDefName resolves to BirthdayEventKey",
                VsieGatheringMap.Plan(VsieGatheringMap.BirthdayDefName)?.EventKey == VsieGatheringMap.BirthdayEventKey);
            Check("FuneralDefName resolves to FuneralEventKey",
                VsieGatheringMap.Plan(VsieGatheringMap.FuneralDefName)?.EventKey == VsieGatheringMap.FuneralEventKey);
            Check("eventKey constants keep their frozen values",
                VsieGatheringMap.BirthdayEventKey == "vsie_birthday" && VsieGatheringMap.FuneralEventKey == "vsie_funeral");
        }

        // ---- tiny assert harness ----

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("  FAIL  " + name);
            }
        }
    }
}
