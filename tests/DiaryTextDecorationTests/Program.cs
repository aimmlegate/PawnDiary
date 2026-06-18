using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary;

namespace DiaryTextDecorationTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestHediffRuleSelectsDirectSpeechOnly();
            TestTraitAndColorRulesAreOrdered();
            TestDecorationsApplyInSequenceAndPreserveTags();
            TestZalgoIntensity();
            TestPawnFactSerializationRoundTrip();

            Console.WriteLine("DiaryTextDecorationTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestHediffRuleSelectsDirectSpeechOnly()
        {
            DiaryTextDecorationContext context = new DiaryTextDecorationContext();
            context.hediffs.Add(new DiaryTextDecorationHediffFact
            {
                defName = "AlcoholHigh",
                label = "drunk",
                severity = 0.7f,
                visible = true
            });

            List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>
            {
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.StaggeredWordSizes,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 20,
                    intensity = 2,
                    when = new DiaryTextDecorationCondition
                    {
                        anyHediffDefName = new List<string> { "AlcoholHigh" }
                    }
                }
            };

            DiaryTextDecorationPlan speech = DiaryTextDecorations.Select(context, rules, DiaryTextDecorationScopes.DirectSpeech);
            DiaryTextDecorationPlan body = DiaryTextDecorations.Select(context, rules, DiaryTextDecorationScopes.Body);

            AssertEqual("direct speech rule count", 1, speech.rules.Count);
            AssertEqual("body rule count", 0, body.rules.Count);
        }

        private static void TestTraitAndColorRulesAreOrdered()
        {
            DiaryTextDecorationContext context = new DiaryTextDecorationContext
            {
                colorCue = "strangeChat"
            };
            context.traits.Add(new DiaryTextDecorationTraitFact { defName = "Wimp", label = "wimp" });

            List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>
            {
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.Zalgo,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 30,
                    intensity = 1,
                    when = new DiaryTextDecorationCondition
                    {
                        anyColorCue = new List<string> { "strangeChat" }
                    }
                },
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.StaggeredWordSizes,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 10,
                    intensity = 4,
                    when = new DiaryTextDecorationCondition
                    {
                        anyTraitDefName = new List<string> { "Wimp" }
                    }
                }
            };

            DiaryTextDecorationPlan plan = DiaryTextDecorations.Select(context, rules, DiaryTextDecorationScopes.DirectSpeech);
            AssertEqual("ordered rule count", 2, plan.rules.Count);
            AssertEqual("first ordered rule", DiaryTextDecorationKinds.StaggeredWordSizes, plan.rules[0].decoration);
            AssertEqual("second ordered rule", DiaryTextDecorationKinds.Zalgo, plan.rules[1].decoration);
        }

        private static void TestDecorationsApplyInSequenceAndPreserveTags()
        {
            DiaryTextDecorationPlan plan = new DiaryTextDecorationPlan();
            plan.rules.Add(new DiaryTextDecorationRule
            {
                decoration = DiaryTextDecorationKinds.StaggeredWordSizes,
                sequence = 10,
                intensity = 4
            });
            plan.rules.Add(new DiaryTextDecorationRule
            {
                decoration = DiaryTextDecorationKinds.Zalgo,
                sequence = 20,
                intensity = 2
            });

            string decorated = DiaryTextDecorations.ApplyToRichText(
                "<b>Alpha bravo charlie delta echo foxtrot</b>",
                plan,
                12345,
                13);

            AssertContains("stagger size tag", decorated, "<size=");
            AssertContains("stagger close tag", decorated, "</size>");
            AssertContains("bold open tag preserved", decorated, "<b>");
            AssertContains("bold close tag preserved", decorated, "</b>");
            AssertTrue("zalgo marks added", CountCombiningMarks(decorated) > 0);
        }

        private static void TestZalgoIntensity()
        {
            string input = "<i>abcdefghijklmnopqrstuvwxyz</i>";
            string mild = DiaryTextDecorations.ApplyZalgoToRichText(input, 1, 99);
            string strong = DiaryTextDecorations.ApplyZalgoToRichText(input, 4, 99);

            AssertContains("mild keeps tag", mild, "<i>");
            AssertContains("strong keeps tag", strong, "</i>");
            AssertTrue("stronger intensity adds more marks", CountCombiningMarks(strong) > CountCombiningMarks(mild));
        }

        private static void TestPawnFactSerializationRoundTrip()
        {
            DiaryTextDecorationContext source = new DiaryTextDecorationContext();
            source.hediffs.Add(new DiaryTextDecorationHediffFact
            {
                defName = "Anesthetic",
                label = "under anesthesia",
                severity = 0.5f,
                visible = true
            });
            source.traits.Add(new DiaryTextDecorationTraitFact
            {
                defName = "PsychicallySensitive",
                label = "psychically sensitive",
                degree = 1
            });

            string serialized = DiaryTextDecorations.SerializePawnFacts(source);
            DiaryTextDecorationContext roundTrip = new DiaryTextDecorationContext();
            DiaryTextDecorations.AddSerializedPawnFacts(roundTrip, serialized);

            AssertEqual("round-trip hediff count", 1, roundTrip.hediffs.Count);
            AssertEqual("round-trip trait count", 1, roundTrip.traits.Count);
            AssertEqual("round-trip hediff def", "Anesthetic", roundTrip.hediffs[0].defName);
            AssertEqual("round-trip trait def", "PsychicallySensitive", roundTrip.traits[0].defName);

            DiaryTextDecorationRule anestheticRule = new DiaryTextDecorationRule
            {
                when = new DiaryTextDecorationCondition
                {
                    anyHediffLabelContains = new List<string> { "anesth" },
                    minHediffSeverity = 0.4f
                }
            };
            AssertTrue("round-trip hediff matches condition", DiaryTextDecorations.Matches(roundTrip, anestheticRule.when));
        }

        private static int CountCombiningMarks(string text)
        {
            int count = 0;
            for (int i = 0; i < (text ?? string.Empty).Length; i++)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertContains(string name, string actual, string expectedSubstring)
        {
            assertions++;
            if (actual == null || actual.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
            {
                throw new Exception(name + " expected to contain '" + expectedSubstring + "' but was '" + actual + "'.");
            }
        }

        private static void AssertTrue(string name, bool value)
        {
            assertions++;
            if (!value)
            {
                throw new Exception(name + " expected true.");
            }
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new Exception(name + " expected '" + expected + "' but got '" + actual + "'.");
            }
        }
    }
}
