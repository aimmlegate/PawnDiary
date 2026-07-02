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
            TestDimmedWordsPreserveTagsWithoutZalgoMarks();
            TestDarkAndStrangeSelectDifferentDecorations();
            TestPsychicRitualInvokerUsesFormatterWithDarkCue();
            TestNameHighlighterColorsAndBoldsKnownPawns();
            TestNameHighlighterRespectsBoundariesAndLongestName();
            TestNameHighlighterSetComparison();
            TestPawnFactSerializationRoundTrip();
            TestEventTagsFromContext();
            TestIntoxicationHediffReusesStaggeredRules();
            TestCleanLineStripsTagsCollapsesNewlinesAndTrims();

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

        private static void TestDimmedWordsPreserveTagsWithoutZalgoMarks()
        {
            string input = "<b>Alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima</b>";
            string dimmed = DiaryTextDecorations.ApplyDimmedWordsToRichText(input, 4, 123);

            AssertContains("dimmed keeps bold open tag", dimmed, "<b>");
            AssertContains("dimmed keeps bold close tag", dimmed, "</b>");
            AssertContains("dimmed adds color tag", dimmed, "<color=#56514D>");
            AssertContains("dimmed adds italic tag", dimmed, "<i>");
            AssertTrue("dimmed does not add combining marks", CountCombiningMarks(dimmed) == 0);
        }

        private static void TestDarkAndStrangeSelectDifferentDecorations()
        {
            List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>
            {
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.DimmedWords,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 30,
                    intensity = 3,
                    when = new DiaryTextDecorationCondition
                    {
                        anyColorCue = new List<string> { "extremeDark" }
                    }
                },
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
                }
            };

            DiaryTextDecorationPlan dark = DiaryTextDecorations.Select(
                new DiaryTextDecorationContext { colorCue = "extremeDark" },
                rules,
                DiaryTextDecorationScopes.DirectSpeech);
            DiaryTextDecorationPlan strange = DiaryTextDecorations.Select(
                new DiaryTextDecorationContext { colorCue = "strangeChat" },
                rules,
                DiaryTextDecorationScopes.DirectSpeech);

            AssertEqual("dark rule count", 1, dark.rules.Count);
            AssertEqual("strange rule count", 1, strange.rules.Count);
            AssertEqual("dark uses dimming", DiaryTextDecorationKinds.DimmedWords, dark.rules[0].decoration);
            AssertEqual("strange keeps zalgo", DiaryTextDecorationKinds.Zalgo, strange.rules[0].decoration);
        }

        private static void TestPsychicRitualInvokerUsesFormatterWithDarkCue()
        {
            List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>
            {
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.DimmedWords,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 30,
                    intensity = 3,
                    when = new DiaryTextDecorationCondition
                    {
                        anyColorCue = new List<string> { "extremeDark" }
                    }
                },
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.Zalgo,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 35,
                    intensity = 1,
                    when = new DiaryTextDecorationCondition
                    {
                        anyContextKey = new List<string> { "psychic_ritual" },
                        anyContextValueContains = new List<string> { "psychic_ritual_perspective=invoker" }
                    }
                }
            };

            DiaryTextDecorationPlan plan = DiaryTextDecorations.Select(
                new DiaryTextDecorationContext
                {
                    colorCue = "extremeDark",
                    gameContext = "psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker"
                },
                rules,
                DiaryTextDecorationScopes.DirectSpeech);

            AssertEqual("psychic invoker dark formatter rule count", 2, plan.rules.Count);
            AssertEqual("psychic invoker keeps dark first", DiaryTextDecorationKinds.DimmedWords, plan.rules[0].decoration);
            AssertEqual("psychic invoker applies formatter second", DiaryTextDecorationKinds.Zalgo, plan.rules[1].decoration);
        }

        private static void TestNameHighlighterColorsAndBoldsKnownPawns()
        {
            List<DiaryNameHighlight> highlights = new List<DiaryNameHighlight>
            {
                new DiaryNameHighlight { name = "Alice", colorHex = "aabbcc" },
                new DiaryNameHighlight { name = "Bob", colorHex = string.Empty }
            };

            string highlighted = DiaryNameHighlighter.ApplyToRichText(
                "Alice met <i>Bob</i>. Alice's pack was missing.",
                highlights);

            AssertContains("colored Alice", highlighted, "<color=#AABBCC><b>Alice</b></color>");
            AssertContains("bold-only Bob preserves tag", highlighted, "<i><b>Bob</b></i>");
            AssertContains("possessive Alice", highlighted, "<color=#AABBCC><b>Alice</b></color>'s");
        }

        private static void TestNameHighlighterRespectsBoundariesAndLongestName()
        {
            List<DiaryNameHighlight> highlights = new List<DiaryNameHighlight>
            {
                new DiaryNameHighlight { name = "Ann", colorHex = "112233" },
                new DiaryNameHighlight { name = "Anna", colorHex = "445566" }
            };

            string highlighted = DiaryNameHighlighter.ApplyToRichText(
                "Anna spoke to Ann near the annex.",
                highlights);

            AssertContains("longest name wins", highlighted, "<color=#445566><b>Anna</b></color>");
            AssertContains("shorter name still matches", highlighted, "<color=#112233><b>Ann</b></color>");
            AssertTrue("does not match inside annex", highlighted.IndexOf("<b>ann</b>ex", StringComparison.OrdinalIgnoreCase) < 0);
        }

        // Locks the change-detection contract the Diary tab relies on: the highlight version (which
        // invalidates the expensive card-height cache) may only advance when the rebuilt set REALLY
        // differs, and every ambiguous input must read as "changed" (the safe direction).
        private static void TestNameHighlighterSetComparison()
        {
            List<DiaryNameHighlight> baseline = new List<DiaryNameHighlight>
            {
                new DiaryNameHighlight { name = "Alice", colorHex = "AABBCC" },
                new DiaryNameHighlight { name = "Bob", colorHex = string.Empty }
            };

            List<DiaryNameHighlight> reorderedDifferentCase = new List<DiaryNameHighlight>
            {
                new DiaryNameHighlight { name = "bob", colorHex = string.Empty },
                new DiaryNameHighlight { name = "ALICE", colorHex = "aabbcc" }
            };

            AssertTrue("same reference is same",
                DiaryNameHighlighter.SameHighlights(baseline, baseline));
            AssertTrue("reordered case-insensitive set is same",
                DiaryNameHighlighter.SameHighlights(baseline, reorderedDifferentCase));
            AssertTrue("null color equals empty color",
                DiaryNameHighlighter.SameHighlights(
                    new List<DiaryNameHighlight> { new DiaryNameHighlight { name = "Bob", colorHex = null } },
                    new List<DiaryNameHighlight> { new DiaryNameHighlight { name = "Bob", colorHex = string.Empty } }));

            AssertTrue("recolored name is changed",
                !DiaryNameHighlighter.SameHighlights(baseline, new List<DiaryNameHighlight>
                {
                    new DiaryNameHighlight { name = "Alice", colorHex = "112233" },
                    new DiaryNameHighlight { name = "Bob", colorHex = string.Empty }
                }));
            AssertTrue("added name is changed",
                !DiaryNameHighlighter.SameHighlights(baseline, new List<DiaryNameHighlight>
                {
                    new DiaryNameHighlight { name = "Alice", colorHex = "AABBCC" },
                    new DiaryNameHighlight { name = "Bob", colorHex = string.Empty },
                    new DiaryNameHighlight { name = "Cara", colorHex = string.Empty }
                }));
            AssertTrue("null list is changed",
                !DiaryNameHighlighter.SameHighlights(baseline, null));
            AssertTrue("ambiguous duplicate names are treated as changed",
                !DiaryNameHighlighter.SameHighlights(
                    new List<DiaryNameHighlight>
                    {
                        new DiaryNameHighlight { name = "Alice", colorHex = "AABBCC" },
                        new DiaryNameHighlight { name = "alice", colorHex = "AABBCC" }
                    },
                    new List<DiaryNameHighlight>
                    {
                        new DiaryNameHighlight { name = "Alice", colorHex = "AABBCC" },
                        new DiaryNameHighlight { name = "alice", colorHex = "AABBCC" }
                    }));
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

        private static void TestEventTagsFromContext()
        {
            string gameContext = "mood_event=Aurora; death_description=true; lonely_tag";
            DiaryTextDecorationContext context = new DiaryTextDecorationContext();
            DiaryTextDecorations.AddEventTagsFromContext(context, gameContext);

            AssertTrue("context tag key", context.eventTags.Contains("mood_event"));
            AssertTrue("context tag key value", context.eventTags.Contains("mood_event=Aurora"));
            AssertTrue("context tag bare value", context.eventTags.Contains("lonely_tag"));
            AssertEqual("shared context value", "Aurora", DiaryTextDecorations.ContextValue(gameContext, "mood_event"));
        }

        // Capture-time intoxication detection now routes through the same XML-owned matcher list as
        // render-time decoration (HediffMatchesStaggeredRules), instead of a parallel hardcoded
        // keyword list. This mirrors the production Diary_TextDecorations staggered rule shape.
        private static void TestIntoxicationHediffReusesStaggeredRules()
        {
            DiaryTextDecorationRule staggeredRule = new DiaryTextDecorationRule
            {
                decoration = DiaryTextDecorationKinds.StaggeredWordSizes,
                scope = DiaryTextDecorationScopes.DirectSpeech,
                enabled = true,
                when = new DiaryTextDecorationCondition
                {
                    anyHediffDefName = new List<string> { "AlcoholHigh" },
                    anyHediffLabelContains = new List<string> { "drunk", "hangover" }
                }
            };
            // An unrelated non-stagger rule must not classify a hediff as intoxicating.
            DiaryTextDecorationRule dimmedRule = new DiaryTextDecorationRule
            {
                decoration = DiaryTextDecorationKinds.DimmedWords,
                enabled = true,
                when = new DiaryTextDecorationCondition
                {
                    anyHediffLabelContains = new List<string> { "drunk" }
                }
            };
            List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule> { staggeredRule, dimmedRule };

            // Matched by exact defName.
            AssertTrue("defName match intoxicated",
                DiaryTextDecorations.HediffMatchesStaggeredRules(rules, new DiaryTextDecorationHediffFact
                {
                    defName = "AlcoholHigh",
                    label = "alcohol",
                    visible = true
                }));
            // Matched case-insensitively by label substring (hangover / drunk).
            AssertTrue("label contains hangover",
                DiaryTextDecorations.HediffMatchesStaggeredRules(rules, new DiaryTextDecorationHediffFact
                {
                    defName = "AlcoholHangover",
                    label = "alcohol hangover",
                    visible = true
                }));
            // A non-intoxicating hediff does not match.
            AssertTrue("flu not intoxicated",
                !DiaryTextDecorations.HediffMatchesStaggeredRules(rules, new DiaryTextDecorationHediffFact
                {
                    defName = "Flu",
                    label = "flu",
                    visible = true
                }));
            // A hidden hediff never matches even if its name would.
            AssertTrue("hidden hediff ignored",
                !DiaryTextDecorations.HediffMatchesStaggeredRules(rules, new DiaryTextDecorationHediffFact
                {
                    defName = "AlcoholHigh",
                    label = "drunk",
                    visible = false
                }));
            // The DimmedWords rule's "drunk" matcher must not bleed into intoxication classification.
            AssertTrue("dimmed-only rule not intoxicating",
                !DiaryTextDecorations.HediffMatchesStaggeredRules(new List<DiaryTextDecorationRule> { dimmedRule },
                    new DiaryTextDecorationHediffFact { defName = "X", label = "drunk", visible = true }));
            // Null/empty inputs are safe.
            AssertTrue("null rules safe", !DiaryTextDecorations.HediffMatchesStaggeredRules(null,
                new DiaryTextDecorationHediffFact { defName = "AlcoholHigh", visible = true }));
            AssertTrue("null fact safe", !DiaryTextDecorations.HediffMatchesStaggeredRules(rules, null));
        }

        // DiaryLineCleaner is the one context helper that is genuinely free of RimWorld/Verse state,
        // so it is unit-tested here to lock in its tag-stripping, newline-collapsing, and trimming
        // behavior. The production LLM context builders and capture code all route labels/thoughts
        // through it before embedding them in prompt strings.
        private static void TestCleanLineStripsTagsCollapsesNewlinesAndTrims()
        {
            AssertEqual("null returns empty", string.Empty, DiaryLineCleaner.CleanLine(null));
            AssertEqual("empty returns empty", string.Empty, DiaryLineCleaner.CleanLine(string.Empty));
            AssertEqual("whitespace returns empty", string.Empty, DiaryLineCleaner.CleanLine("   \t  "));
            AssertEqual("plain text unchanged", "Hello world", DiaryLineCleaner.CleanLine("Hello world"));
            AssertEqual("trims surrounding space", "Hi", DiaryLineCleaner.CleanLine("  Hi  "));

            // Rich-text tags are removed; inner text survives.
            AssertEqual("strips bold tags", "Hello", DiaryLineCleaner.CleanLine("<b>Hello</b>"));
            AssertEqual("strips nested and valued tags", "x", DiaryLineCleaner.CleanLine("<size=20><b>x</b></size>"));
            AssertEqual("strips color tags keeps words", "Hello world", DiaryLineCleaner.CleanLine("<color=#FF00FF>Hello</color> world"));

            // Newlines and carriage returns become spaces, then get trimmed.
            AssertEqual("newline collapses to space", "a b", DiaryLineCleaner.CleanLine("a\nb"));
            AssertEqual("carriage return collapses to space", "a b", DiaryLineCleaner.CleanLine("a\rb"));
            // \r and \n are each replaced by a space, so a CRLF pair yields two spaces (preserved
            // pre-existing behavior — the cleaner does not collapse runs of spaces).
            AssertEqual("crlf yields two spaces", "a  b", DiaryLineCleaner.CleanLine("a\r\nb"));
            AssertEqual("trailing newline trimmed", "line", DiaryLineCleaner.CleanLine("line\n"));

            // Tags + newlines together.
            AssertEqual("tags and newlines together", "a b", DiaryLineCleaner.CleanLine("<b>a</b>\n<b>b</b>"));
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
