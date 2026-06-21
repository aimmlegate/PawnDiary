// Pure unit tests for the Event Catalog decision layer. These exercise ThoughtEventData.Decide and
// InspirationEventData.Decide without RimWorld assemblies, plus the DiaryEventCatalog dispatch. Run
// via: build DiaryCapturePolicyTests.csproj, then execute the resulting exe (exit code 0 = pass).
using System;
using System.Collections.Generic;
using PawnDiary.Capture;

namespace DiaryCapturePolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestThoughtNullGuards();
            TestThoughtEligibilityGates();
            TestThoughtPermanentDrop();
            TestThoughtIgnoreToken();
            TestThoughtGeneralThreshold();
            TestThoughtEatingThreshold();
            TestThoughtBypassToken();
            TestThoughtAmbientRouting();
            TestThoughtPolicyNullDefaults();
            TestInspirationDecide();
            TestCatalogDispatch();

            Console.WriteLine("DiaryCapturePolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        // ── Thought: guards and gates ──

        private static void TestThoughtNullGuards()
        {
            AssertEqual("null data drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(null, Ctx()));
            AssertEqual("null ctx drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Any", mood: 10f), null));
        }

        private static void TestThoughtEligibilityGates()
        {
            AssertEqual("ineligible drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(eligible: false)));
            AssertEqual("signal disabled drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(signal: false)));
            AssertEqual("user disabled drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(user: false)));
            AssertEqual("all enabled records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx()));
        }

        // ── Thought: source-specific filters ──

        private static void TestThoughtPermanentDrop()
        {
            AssertEqual("duration zero drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f, duration: 0f), Ctx()));
            AssertEqual("duration negative drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f, duration: -1f), Ctx()));
        }

        private static void TestThoughtIgnoreToken()
        {
            // Default policy ignore token is "Insult" (substring, case-insensitive).
            AssertEqual("ignore token match drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Insulted", mood: 10f), Ctx()));
            // Default policy does NOT include "Foo", so a "Foo" thought is not ignored — every other
            // passing test already proves that path returns GenerateSolo.
        }

        private static void TestThoughtGeneralThreshold()
        {
            // Default general bar is +/-5.
            AssertEqual("below general threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 4.9f), Ctx()));
            AssertEqual("negative below threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: -4.9f), Ctx()));
            AssertEqual("at general threshold records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: 5f), Ctx()));
            AssertEqual("above general threshold records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: -10f), Ctx()));
        }

        private static void TestThoughtEatingThreshold()
        {
            // Default eating bar is +/-15; eating token is "Ate".
            AssertEqual("eating below eating-bar drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("AteWithoutTable", mood: 14f), Ctx()));
            AssertEqual("eating above eating-bar records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("AteFineMeal", mood: 16f), Ctx()));
            AssertEqual("eating at eating-bar records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("AteGood", mood: 15f), Ctx()));
        }

        private static void TestThoughtBypassToken()
        {
            // Bypass token is "Death" — magnitude threshold is skipped entirely. defName must
            // actually contain the token (substring match), so we use DeathOfFriend here.
            AssertEqual("bypass with low magnitude records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("DeathOfFriend", mood: 0.1f), Ctx()));
            AssertEqual("bypass with zero magnitude records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("DeathOfFriend", mood: 0f), Ctx()));
        }

        private static void TestThoughtAmbientRouting()
        {
            // Ambient token is "Nuzzle".
            AssertEqual("ambient token + ambient enabled routes", CaptureDecision.RouteAmbient,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 6f), Ctx(ambient: true)));
            // Same thought but ambient disabled → still recorded as solo (ambient is optional routing,
            // not a filter; if ambient is off the thought still qualifies as a normal solo event).
            AssertEqual("ambient token + ambient disabled records solo", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 6f), Ctx(ambient: false)));
            // Ambient token below general threshold still drops: ambient routing only runs after the
            // magnitude gate, matching pre-refactor order.
            AssertEqual("ambient below threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 1f), Ctx(ambient: true)));
        }

        private static void TestThoughtPolicyNullDefaults()
        {
            // A null policy means "no tokens, no thresholds" — every eligible expiring thought records.
            ThoughtEventData data = new ThoughtEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = "Anything",
                MoodOffset = 0f,
                DurationDays = 1f,
                MoodImpact = MoodImpact("neutral"),
                Policy = null,
            };
            AssertEqual("null policy records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(data, Ctx()));
        }

        // ── Inspiration ──

        private static void TestInspirationDecide()
        {
            AssertEqual("inspiration null data drops", CaptureDecision.Drop,
                InspirationEventData.Decide(null, Ctx()));
            AssertEqual("inspiration null ctx drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), null));
            AssertEqual("inspiration ineligible drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), Ctx(eligible: false)));
            AssertEqual("inspiration user disabled drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), Ctx(user: false)));
            AssertEqual("inspiration eligible records", CaptureDecision.GenerateSolo,
                InspirationEventData.Decide(Inspiration("Inspired_Recruitment"), Ctx()));
        }

        // ── Catalog dispatch ──

        private static void TestCatalogDispatch()
        {
            DiaryEventCatalog.Reset();

            DiaryEventSpec thoughtSpec = DiaryEventCatalog.Get(DiaryEventType.Thought);
            AssertTrue("catalog has Thought spec", thoughtSpec is ThoughtEventSpec);
            AssertEqual("catalog dispatches Thought decision",
                CaptureDecision.GenerateSolo,
                thoughtSpec.Decide(Thought("Foo", mood: 10f), Ctx()));

            DiaryEventSpec inspirationSpec = DiaryEventCatalog.Get(DiaryEventType.Inspiration);
            AssertTrue("catalog has Inspiration spec", inspirationSpec is InspirationEventSpec);
            AssertEqual("catalog dispatches Inspiration decision",
                CaptureDecision.GenerateSolo,
                inspirationSpec.Decide(Inspiration("Foo"), Ctx()));

            // Unregistered spec returns null — callers must treat as Drop.
            // (We can't construct a DiaryEventType that isn't registered because all values are
            // registered today, so verify Reset re-registers defaults.)
            AssertTrue("catalog spec is non-null after reset", DiaryEventCatalog.Get(DiaryEventType.Thought) != null);
        }

        // ── Factory helpers ──

        private const string MoodPositive = "positive";
        private const string MoodNegative = "negative";
        private const string MoodNeutral = "neutral";

        private static string MoodImpact(string token)
        {
            // Pure-side tests don't run MoodImpact.Classify (Verse-using); we only need the string.
            return token;
        }

        /// <summary>
        /// Builds a ThoughtEventData with the default test policy. Mood is classified into a token
        /// here purely for completeness — the pure Decider does not branch on the MoodImpact string,
        /// only on the numeric MoodOffset.
        /// </summary>
        private static ThoughtEventData Thought(string defName, float mood, float duration = 1f)
        {
            string impact = mood > 0.5f ? MoodPositive : (mood < -0.5f ? MoodNegative : MoodNeutral);
            return new ThoughtEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                MoodOffset = mood,
                DurationDays = duration,
                MoodImpact = impact,
                Policy = DefaultThoughtPolicy(),
            };
        }

        private static InspirationEventData Inspiration(string defName)
        {
            return new InspirationEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                DurationDays = 8f,
                Reason = "test reason",
            };
        }

        /// <summary>
        /// Default test policy mirrors the shipped DiarySignalPolicyDef defaults: ±5 general, ±15
        /// eating, plus tokens for testing each branch. Tokens are case-insensitive substrings.
        /// </summary>
        private static ThoughtCapturePolicy DefaultThoughtPolicy()
        {
            return new ThoughtCapturePolicy
            {
                IgnoreTokens = new List<string> { "Insult" },
                BypassThresholdTokens = new List<string> { "Death" },
                EatingTokens = new List<string> { "Ate" },
                AmbientTokens = new List<string> { "Nuzzle" },
                MinMoodOffset = 5f,
                EatingMinMoodOffset = 15f,
            };
        }

        private static CaptureContext Ctx(
            bool eligible = true,
            bool user = true,
            bool signal = true,
            bool ambient = true)
        {
            return new CaptureContext
            {
                Eligible = eligible,
                UserEnabled = user,
                SignalEnabled = signal,
                AmbientSignalEnabled = ambient,
                Now = 1000,
            };
        }

        // ── Assert helpers (mirrors DiaryPipelineTests) ──

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(name + " failed.");
            }
        }
    }
}
