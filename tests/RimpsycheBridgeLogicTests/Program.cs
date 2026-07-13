// RimWorld-free console tests for the Rimpsyche bridge's pure policy.
//
// The tiny harness mirrors the repository's other tests/* projects: every assertion prints PASS/FAIL
// and Main returns non-zero on failure. No external test framework or game assembly is required.
using System;
using System.Collections.Generic;
using PawnDiaryRimpsyche.Pure;

namespace RimpsycheBridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestDefinitions_All34KnownAndDistinct();
            TestFamilyRuleTable_AllCellsNonBlankAndBehavioral();
            TestDominantPair_UsesDifferentFamiliesAndDeterministicTies();
            TestDominantPair_AlignedPolarity();
            TestDominantPair_StableUnderTinyJitter();
            TestStableHash_OrderUnknownAndFixture();
            TestInternalPsychotypeMapping_AllFamiliesAndSigns();

            TestSummary_FloorTopThreeAndNoRawFloats();
            TestSummary_ExactFloorExcludedAndRounded();
            TestSummary_EmptyInputReturnsEmpty();
            TestSummary_InterestsCappedDeduplicatedAndCleaned();
            TestTransformInput_CombinesAndHandlesBlank();

            TestConversation_AlignmentThreshold();
            TestConversation_Cooldown();
            TestConversation_PairKeyOrderIndependent();

            Console.WriteLine();
            Console.WriteLine("RimpsycheBridgeLogicTests: " + passed + " passed, " + failed + " failed.");
            return failed > 0 ? 1 : 0;
        }

        // ---- PsycheLensMapping ----

        private static void TestDefinitions_All34KnownAndDistinct()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PsycheNodeDefinition definition in PsycheLensMapping.Definitions)
            {
                names.Add(definition.DefName);
                Check(definition.DefName + " has both adjectives",
                    !string.IsNullOrWhiteSpace(definition.HighAdjective)
                    && !string.IsNullOrWhiteSpace(definition.LowAdjective));
            }

            Check("exactly 34 known node definitions", PsycheLensMapping.Definitions.Count == 34);
            Check("all 34 node defNames are distinct", names.Count == 34);
        }

        private static void TestFamilyRuleTable_AllCellsNonBlankAndBehavioral()
        {
            HashSet<string> rules = new HashSet<string>(StringComparer.Ordinal);
            foreach (PsycheFamily family in Enum.GetValues(typeof(PsycheFamily)))
            {
                foreach (bool positive in new[] { false, true })
                {
                    string rule = PsycheLensMapping.EnglishRuleFor(family, positive);
                    string key = PsycheLensMapping.RuleKeyFor(family, positive);
                    Check(family + "/" + positive + " has a nonblank rule",
                        !string.IsNullOrWhiteSpace(rule));
                    Check(family + "/" + positive + " uses psychotype register",
                        rule.StartsWith("This pawn", StringComparison.Ordinal));
                    Check(family + "/" + positive + " has a Keyed key",
                        key.StartsWith(PsycheLensMapping.OutlookKeyPrefix, StringComparison.Ordinal));
                    string lower = rule.ToLowerInvariant();
                    Check(family + "/" + positive + " names no mechanic/node",
                        !lower.Contains("rimpsyche")
                        && !lower.Contains("personality node")
                        && !lower.Contains("score"));
                    rules.Add(rule);
                }
            }

            Check("all 12 family/sign rules are distinct", rules.Count == 12);
        }

        private static void TestDominantPair_UsesDifferentFamiliesAndDeterministicTies()
        {
            List<PsycheNodeValue> vector = new List<PsycheNodeValue>
            {
                new PsycheNodeValue("Rimpsyche_Talkativeness", 0.90f),
                new PsycheNodeValue("Rimpsyche_Sociability", 0.89f), // same Social family: must skip
                new PsycheNodeValue("Rimpsyche_Openness", -0.80f),
                new PsycheNodeValue("Rimpsyche_Discipline", 0.70f)
            };

            PsycheLensPlan plan = PsycheLensMapping.SelectDominantPair(vector);
            Check("dominant pair exists", plan != null && plan.HasSecondary);
            Check("largest node picks Social positive",
                plan.PrimaryFamily == PsycheFamily.Social && plan.PrimaryPositive);
            Check("second node comes from different Mind family",
                plan.SecondaryFamily == PsycheFamily.Mind && !plan.SecondaryPositive);

            // Exact magnitude ties use canonical installed-node table order, not input enumeration order.
            PsycheLensPlan tie = PsycheLensMapping.SelectDominantPair(new[]
            {
                new PsycheNodeValue("Rimpsyche_Organization", 0.80f),
                new PsycheNodeValue("Rimpsyche_Talkativeness", -0.80f),
                new PsycheNodeValue("Rimpsyche_Openness", 0.80f)
            });
            Check("tie primary follows canonical table order",
                tie.PrimaryFamily == PsycheFamily.Social && !tie.PrimaryPositive);
            Check("tie secondary follows canonical table order",
                tie.SecondaryFamily == PsycheFamily.Mind && tie.SecondaryPositive);
        }

        private static void TestDominantPair_AlignedPolarity()
        {
            PsycheLensPlan plan = PsycheLensMapping.SelectDominantPair(new[]
            {
                // Raw high SelfInterest maps AWAY from the pro-social Moral rule.
                new PsycheNodeValue("Rimpsyche_SelfInterest", 0.90f),
                // Raw high Stability maps AWAY from the emotionally reactive rule.
                new PsycheNodeValue("Rimpsyche_Stability", 0.80f)
            });

            Check("reversed SelfInterest polarity -> Moral negative",
                plan.PrimaryFamily == PsycheFamily.Moral && !plan.PrimaryPositive);
            Check("reversed Stability polarity -> Emotion negative",
                plan.SecondaryFamily == PsycheFamily.Emotion && !plan.SecondaryPositive);
        }

        private static void TestDominantPair_StableUnderTinyJitter()
        {
            List<PsycheNodeValue> first = new List<PsycheNodeValue>
            {
                new PsycheNodeValue("Rimpsyche_Sociability", 0.804f),
                new PsycheNodeValue("Rimpsyche_Openness", -0.703f),
                new PsycheNodeValue("Rimpsyche_Discipline", 0.602f)
            };
            List<PsycheNodeValue> second = new List<PsycheNodeValue>
            {
                new PsycheNodeValue("Rimpsyche_Sociability", 0.803f),
                new PsycheNodeValue("Rimpsyche_Openness", -0.702f),
                new PsycheNodeValue("Rimpsyche_Discipline", 0.604f)
            };

            PsycheLensPlan a = PsycheLensMapping.SelectDominantPair(first);
            PsycheLensPlan b = PsycheLensMapping.SelectDominantPair(second);
            Check("tiny jitter keeps primary family/sign",
                a.PrimaryFamily == b.PrimaryFamily && a.PrimaryPositive == b.PrimaryPositive);
            Check("tiny jitter keeps secondary family/sign",
                a.SecondaryFamily == b.SecondaryFamily && a.SecondaryPositive == b.SecondaryPositive);
            Check("tiny jitter within hundredths keeps vector hash",
                PsycheLensMapping.StableVectorHash(first) == PsycheLensMapping.StableVectorHash(second));
        }

        private static void TestStableHash_OrderUnknownAndFixture()
        {
            List<PsycheNodeValue> first = new List<PsycheNodeValue>
            {
                new PsycheNodeValue("Rimpsyche_Talkativeness", 0.81f),
                new PsycheNodeValue("Rimpsyche_Openness", -0.46f),
                new PsycheNodeValue("Rimpsyche_Discipline", 0.33f)
            };
            List<PsycheNodeValue> reordered = new List<PsycheNodeValue>
            {
                new PsycheNodeValue("Future_Rimpsyche_Node", 0.99f), // unknown must be ignored
                new PsycheNodeValue("Rimpsyche_Discipline", 0.33f),
                new PsycheNodeValue("Rimpsyche_Talkativeness", 0.81f),
                new PsycheNodeValue("Rimpsyche_Openness", -0.46f)
            };

            string hash = PsycheLensMapping.StableVectorHash(first);
            Check("hash is input-order independent and skips unknown nodes",
                hash == PsycheLensMapping.StableVectorHash(reordered));
            Check("hash is fixed-width lowercase hex", hash.Length == 16 && hash == hash.ToLowerInvariant());
            // Golden value makes implementation drift visible across machines/processes/runtime versions.
            Check("fixture hash is stable across runs", hash == "cfd7df58c63c22d1");
            Console.WriteLine("  INFO  fixture vector hash = " + hash);
        }

        private static void TestInternalPsychotypeMapping_AllFamiliesAndSigns()
        {
            foreach (PsycheFamily family in Enum.GetValues(typeof(PsycheFamily)))
            {
                foreach (bool positive in new[] { false, true })
                {
                    PsycheLensPlan plan = PsycheLensMapping.SelectDominantPair(new[]
                    {
                        NodeForFamily(family, positive)
                    });
                    string mapped = PsycheLensMapping.InternalPsychotypeForPlan(plan);
                    Check(family + "/" + positive + " maps to built-in psychotype",
                        !string.IsNullOrWhiteSpace(mapped) && mapped.StartsWith("DiaryPsychotype_", StringComparison.Ordinal));
                }
            }
        }

        private static PsycheNodeValue NodeForFamily(PsycheFamily family, bool positive)
        {
            foreach (PsycheNodeDefinition definition in PsycheLensMapping.Definitions)
                if (definition.Family == family)
                    return new PsycheNodeValue(definition.DefName, positive == (definition.LensPolarity > 0) ? 0.9f : -0.9f);
            return default(PsycheNodeValue);
        }

        // ---- PsycheSummaryFormat ----

        private static void TestSummary_FloorTopThreeAndNoRawFloats()
        {
            string line = PsycheSummaryFormat.Format(
                new[]
                {
                    new PsycheNodeValue("Rimpsyche_Talkativeness", 0.90f),
                    new PsycheNodeValue("Rimpsyche_Bravery", -0.80f),
                    new PsycheNodeValue("Rimpsyche_Organization", 0.70f),
                    new PsycheNodeValue("Rimpsyche_Openness", 0.60f),
                    new PsycheNodeValue("Rimpsyche_Tact", 0.20f)
                },
                null,
                0.35f,
                3,
                2);

            Check("summary takes top three in magnitude order",
                line == "psyche=outspoken, fearful, organized");
            bool hasDigit = false;
            foreach (char character in line)
            {
                hasDigit |= char.IsDigit(character);
            }

            Check("summary contains no raw numeric values", !hasDigit && !line.Contains("0."));
        }

        private static void TestSummary_ExactFloorExcludedAndRounded()
        {
            string line = PsycheSummaryFormat.Format(
                new[]
                {
                    new PsycheNodeValue("Rimpsyche_Tact", 0.350f),
                    new PsycheNodeValue("Rimpsyche_Diligence", 0.354f), // rounds to .35: excluded
                    new PsycheNodeValue("Rimpsyche_Openness", -0.356f) // rounds to -.36: included
                },
                null,
                0.35f,
                3,
                0);

            Check("exact/rounded floor excluded, rounded-above included", line == "psyche=traditional");
        }

        private static void TestSummary_EmptyInputReturnsEmpty()
        {
            Check("null vector -> empty",
                PsycheSummaryFormat.Format(null, null, 0.35f, 3, 2) == string.Empty);
            Check("all below floor -> empty",
                PsycheSummaryFormat.Format(
                    new[] { new PsycheNodeValue("Rimpsyche_Tact", 0.2f) },
                    new[] { "machines" },
                    0.35f,
                    3,
                    2) == string.Empty);
            Check("unknown nodes -> empty",
                PsycheSummaryFormat.Format(
                    new[] { new PsycheNodeValue("Rimpsyche_Future", 1f) },
                    null,
                    0.35f,
                    3,
                    2) == string.Empty);
        }

        private static void TestSummary_InterestsCappedDeduplicatedAndCleaned()
        {
            string line = PsycheSummaryFormat.Format(
                new[] { new PsycheNodeValue("Rimpsyche_Tact", -0.8f) },
                new[] { " machines ", "MACHINES", "stories; legends", "animals" },
                0.35f,
                3,
                2);

            Check("interests are cleaned/deduped/capped",
                line == "psyche=brash; interests=machines, stories, legends");
            Check("localized resolver replaces adjective",
                PsycheSummaryFormat.Format(
                    new[] { new PsycheNodeValue("Rimpsyche_Tact", -0.8f) },
                    null,
                    0.35f,
                    3,
                    0,
                    key => key.EndsWith(".Low", StringComparison.Ordinal) ? "резкий" : string.Empty)
                == "psyche=резкий");
        }

        private static void TestTransformInput_CombinesAndHandlesBlank()
        {
            Check("transform input combines summary and outlook",
                PsycheSummaryFormat.BuildTransformInput("psyche=focused", "This pawn values order.")
                == "psyche=focused\nbase outlook: This pawn values order.");
            Check("blank transform input returns null",
                PsycheSummaryFormat.BuildTransformInput(" ", null) == null);
        }

        // ---- ConversationCapturePolicy ----

        private static void TestConversation_AlignmentThreshold()
        {
            Check("positive charged alignment passes", ConversationCapturePolicy.PassesAlignment(0.56f, 0.55f));
            Check("negative charged alignment passes", ConversationCapturePolicy.PassesAlignment(-0.56f, 0.55f));
            Check("exact threshold is excluded", !ConversationCapturePolicy.PassesAlignment(0.55f, 0.55f));
            Check("ordinary alignment is excluded", !ConversationCapturePolicy.PassesAlignment(-0.2f, 0.55f));
            Check("NaN alignment is excluded", !ConversationCapturePolicy.PassesAlignment(float.NaN, 0.55f));
        }

        private static void TestConversation_Cooldown()
        {
            Check("no prior accepted tick passes cooldown",
                ConversationCapturePolicy.CooldownElapsed(100, false, 0, 60000));
            Check("inside cooldown is blocked",
                !ConversationCapturePolicy.CooldownElapsed(60999, true, 1000, 60000));
            Check("exact cooldown boundary passes",
                ConversationCapturePolicy.CooldownElapsed(61000, true, 1000, 60000));
            Check("backwards tick does not bypass cooldown",
                !ConversationCapturePolicy.CooldownElapsed(500, true, 1000, 60000));
            Check("zero cooldown disables pair gate",
                ConversationCapturePolicy.CooldownElapsed(500, true, 1000, 0));
            Check("combined policy requires both conditions",
                !ConversationCapturePolicy.ShouldCapture(0.9f, 0.55f, 2000, true, 1000, 60000));
        }

        private static void TestConversation_PairKeyOrderIndependent()
        {
            string forward = ConversationCapturePolicy.PairKey("Pawn_B", "Pawn_A");
            string reverse = ConversationCapturePolicy.PairKey("Pawn_A", "Pawn_B");
            Check("pair key is order-independent", forward == reverse);
            Check("pair key uses ordinal canonical order", forward == "Pawn_A|Pawn_B");
            Check("same pawn is invalid", ConversationCapturePolicy.PairKey("Pawn_A", "Pawn_A") == string.Empty);
            Check("blank pawn is invalid", ConversationCapturePolicy.PairKey(" ", "Pawn_B") == string.Empty);
        }

        // ---- tiny assertion harness ----

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
