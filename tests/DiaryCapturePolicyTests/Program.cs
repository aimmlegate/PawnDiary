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
            TestThoughtBuildGameContextFormat();
            TestInspirationDecide();
            TestInspirationBuildGameContextFormat();
            TestMoodEventDecide();
            TestMoodEventBuildGameContextFormat();
            TestCatalogDispatch();
            TestCatalogContract();
            TestMigrationSentinel();

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

        private static void TestThoughtBuildGameContextFormat()
        {
            // The leading "thought=" marker and the field order/precision are load-bearing: the UI
            // parses the marker to recover the Thought domain, and the LLM reads the rest. Locking the
            // format here means a future migration cannot silently drift it.
            string ctx = ThoughtEventData.BuildGameContext(
                "AteWithoutTable", "ate without table", "negative", -5.27f, 0.4f);
            AssertEqual("thought context full format",
                "thought=AteWithoutTable; label=ate without table; mood_impact=negative; mood_offset=-5.3; duration_days=0.4",
                ctx);

            // Positive mood impact, integer-valued offset formats with one decimal.
            string positive = ThoughtEventData.BuildGameContext(
                "GotMarried", "got married", "positive", 12f, 2f);
            AssertEqual("thought context positive integer floats",
                "thought=GotMarried; label=got married; mood_impact=positive; mood_offset=12.0; duration_days=2.0",
                positive);

            // Neutral impact passes through verbatim — classification happens in the caller, the build
            // helper just embeds whatever it receives.
            string neutral = ThoughtEventData.BuildGameContext(
                "Foo", "bar", "neutral", 0f, 1f);
            AssertEqual("thought context neutral impact",
                "thought=Foo; label=bar; mood_impact=neutral; mood_offset=0.0; duration_days=1.0",
                neutral);
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

        private static void TestInspirationBuildGameContextFormat()
        {
            // The leading "inspiration=" marker is load-bearing for UI domain classification.
            // With a reason: the optional "reason=" field is appended.
            string withReason = InspirationEventData.BuildGameContext(
                "Inspired_Recruitment", "recruitment drive", 8.0f, "felt inspired");
            AssertEqual("inspiration context with reason",
                "inspiration=Inspired_Recruitment; label=recruitment drive; duration_days=8.0; reason=felt inspired",
                withReason);

            // Without a reason: the field is omitted entirely (not emitted as empty), matching
            // pre-refactor behavior.
            string noReason = InspirationEventData.BuildGameContext(
                "Inspired_Creativity", "creative inspiration", 8.5f, "");
            AssertEqual("inspiration context without reason",
                "inspiration=Inspired_Creativity; label=creative inspiration; duration_days=8.5",
                noReason);

            // Whitespace-only reason is also treated as absent (the helper guards against it).
            string whitespaceReason = InspirationEventData.BuildGameContext(
                "Inspired_Trade", "trade deal", 8.0f, "   ");
            AssertEqual("inspiration context whitespace reason omitted",
                "inspiration=Inspired_Trade; label=trade deal; duration_days=8.0",
                whitespaceReason);
        }

        // ── MoodEvent ──

        private static void TestMoodEventDecide()
        {
            // MoodEvent has multi-pawn fan-out, but the catalog sees one event at a time — so the
            // Decider is the same trivial shape as Inspiration: eligibility + user toggle only.
            AssertEqual("mood event null data drops", CaptureDecision.Drop,
                MoodEventData.Decide(null, Ctx()));
            AssertEqual("mood event null ctx drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), null));
            AssertEqual("mood event ineligible drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), Ctx(eligible: false)));
            AssertEqual("mood event user disabled drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), Ctx(user: false)));
            AssertEqual("mood event eligible records", CaptureDecision.GenerateSolo,
                MoodEventData.Decide(MoodEvent("PsychicDrone"), Ctx()));
        }

        private static void TestMoodEventBuildGameContextFormat()
        {
            // The leading "mood_event=" marker is load-bearing for UI domain classification. The
            // format has no numeric fields (unlike Thought/Inspiration), so the test focuses on
            // marker position, field order, and mood-impact token pass-through.
            string positive = MoodEventData.BuildGameContext("Aurora", "aurora", "positive");
            AssertEqual("mood event positive context",
                "mood_event=Aurora; label=aurora; mood_impact=positive",
                positive);

            string negative = MoodEventData.BuildGameContext("PsychicDrone", "psychic drone", "negative");
            AssertEqual("mood event negative context",
                "mood_event=PsychicDrone; label=psychic drone; mood_impact=negative",
                negative);

            string neutral = MoodEventData.BuildGameContext("Eclipse", "eclipse", "neutral");
            AssertEqual("mood event neutral context",
                "mood_event=Eclipse; label=eclipse; mood_impact=neutral",
                neutral);
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

        // ── Catalog contract (invariants every migration must preserve) ──

        private static void TestCatalogContract()
        {
            DiaryEventCatalog.Reset();

            // (1) Every value declared in DiaryEventType MUST have a registered Spec. Catches the
            // classic "I added the enum entry but forgot Register()" half-finished migration.
            foreach (DiaryEventType type in (DiaryEventType[])Enum.GetValues(typeof(DiaryEventType)))
            {
                DiaryEventSpec spec = DiaryEventCatalog.Get(type);
                AssertTrue("catalog has spec for " + type, spec != null);
                // (2) The spec's own EventType must match the key it is registered under. Catches a
                // spec being registered against the wrong enum value.
                AssertEqual("spec EventType matches key for " + type, type, spec.EventType);
            }

            // (3) Reset is idempotent: re-resetting leaves the catalog in the same shape.
            DiaryEventCatalog.Reset();
            foreach (DiaryEventType type in (DiaryEventType[])Enum.GetValues(typeof(DiaryEventType)))
            {
                AssertTrue("catalog still has spec after re-reset for " + type,
                    DiaryEventCatalog.Get(type) != null);
            }
        }

        // ── Migration sentinel — list of planned sources NOT yet migrated ──
        // Every name in this list is a DiaryEventType we PLAN to add in future slices. The test
        // asserts each one is still ABSENT from the enum. When you migrate a source:
        //   1. Add the value to DiaryEventType.
        //   2. Write XxxEventData + XxxEventSpec + Register.
        //   3. REMOVE its name from this list.
        // The TestCatalogContract step (1) above already enforces "every enum value has a Spec", so
        // this sentinel only protects against accidentally-landed enum values that nobody migrated.
        private static readonly string[] PlannedNotYetMigratedSources =
        {
            "Quest", "Raid", "MajorThreat", "RandomEvent", "WorldEvent",
            "AnomalyEvent", "IncidentEvent", "Health", "Romance",
            "MentalState", "Tale", "Crafted", "Hediff",
            "Interaction", "Arrival", "Death",
        };

        private static void TestMigrationSentinel()
        {
            foreach (string name in PlannedNotYetMigratedSources)
            {
                // Each planned-but-not-yet-migrated source MUST NOT be defined in the enum yet. If this
                // fails: either remove the name from the list above (you finished migrating it), or
                // check that you actually completed the migration (Spec registered, Decide tested).
                bool defined = Enum.IsDefined(typeof(DiaryEventType), name);
                AssertTrue("sentinel: " + name + " is still future (not in enum)", !defined);
            }
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

        private static MoodEventData MoodEvent(string defName, string moodImpact = "neutral")
        {
            return new MoodEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                Label = "test label",
                MoodImpact = moodImpact,
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
