// Pure unit tests for the RimTalk bridge's decision logic. Mirrors the other tests/* console
// projects: a static Main runs focused assertions and returns non-zero when any fail.
//
// These run without RimWorld/RimTalk/Verse/Unity — the logic under test (conversation assembly,
// candidate selection, throttling, context formatting) is deliberately pure so its edge cases are covered
// without booting the game.
using System;
using System.Collections.Generic;
using PawnDiaryRimTalkBridge;

namespace RimTalkBridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Assert(PawnDiaryRimTalkBridge.Pure.PersonaTransferText.Combine("outlook", "style") == "outlook\nstyle",
                "persona transfer combines outlook and style");
            Assert(PawnDiaryRimTalkBridge.Pure.PersonaTransferText.Combine(" ", " style ") == "style",
                "persona transfer ignores blank fields and trims text");

            // ContextFormat
            TestBuildDiarySection_BasicNoStyle();
            TestBuildDiarySection_WithStyle();
            TestBuildDiarySection_EmptyReturnsBlank();
            TestBuildDiarySection_AllBlankEntriesReturnBlank();
            TestBuildDiarySection_TruncatesWholeLines();
            TestBuildDiarySection_TooSmallForOneLineReturnsBlank();
            TestBuildDiarySection_StyleDroppedWhenNoRoom();
            TestFirstSentenceCap();
            TestCapAtWord();

            // ColonyEventsFormat
            TestColony_WeightOrdering();
            TestColony_EmptyAndBlankHandling();
            TestColony_MaxLinesCap();
            TestColony_MaxCharsWholeLineTruncation();
            TestColony_EqualWeightKeepsInsertionOrder();

            // SharedMemorySelection
            TestPairKey_OrderIndependentAndStable();
            TestSelect_EdgeCases();
            TestSelect_RecencyBias();
            TestSelect_ImportanceBias();
            TestSelect_DeterministicForSeed();

            // ConversationAssembler
            TestAssembly_ReplyChainJoins();
            TestAssembly_UnknownParentStartsNewRoot();
            TestAssembly_InterleavedStaySeparate();
            TestAssembly_QuietFlushReturnsOnlyStale();
            TestAssembly_RunawayCapForceFlushes();
            TestAssembly_FlushAllDrainsEverything();

            // Bounded conversation-assessment funnel
            AssessmentTests.Run(Assert);

            // ThrottlePolicy
            TestThrottle_PerPawnCap();
            TestThrottle_ColonyCap();
            TestThrottle_PairGap();
            TestThrottle_DayRolloverResets();
            TestThrottle_ReleaseRefundsCaps();
            TestThrottle_ReleaseClearsPairGap();
            TestThrottle_ReleaseIsSafeNoop();
            TestThrottle_RollingPawnCooldown();
            TestThrottle_CooldownChargesBothPawns();
            TestThrottle_CooldownReleaseAndPersistence();

            // ConversationContext
            TestContext_ExtraContextShape();
            TestContext_TranscriptLineCap();
            TestContext_TranscriptLineCapZeroSuppresses();
            TestContext_DominantKindTieFirstSeen();

            Console.WriteLine("==================================================");
            Console.WriteLine("RimTalkBridge logic tests: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        // ---- ContextFormat -------------------------------------------------------

        private const string Header = "recent diary memories:";
        private const string LineFmt = "- {0} ({1}): {2}";
        private const string VoiceFmt = "diary voice: {0}";

        private static void TestBuildDiarySection_BasicNoStyle()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "Storm", Summary = "It rained hard.", Date = "5th" },
                new DiaryMemoryLine { Title = "Meal", Summary = "Ate together.", Date = "6th" }
            };

            string result = ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, "spare", false, 1000);
            string expected = "recent diary memories:\n- Storm (5th): It rained hard.\n- Meal (6th): Ate together.";
            Assert(result == expected, "basic section no style; got:\n" + result);
        }

        private static void TestBuildDiarySection_WithStyle()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "Storm", Summary = "It rained.", Date = "5th" }
            };

            string result = ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, "spare and clipped", true, 1000);
            Assert(result.EndsWith("\ndiary voice: spare and clipped"), "voice line appended; got:\n" + result);
            Assert(result.StartsWith("recent diary memories:\n- Storm"), "header + memory precede voice; got:\n" + result);
        }

        private static void TestBuildDiarySection_EmptyReturnsBlank()
        {
            Assert(ContextFormat.BuildDiarySection(null, Header, LineFmt, VoiceFmt, "x", true, 1000) == string.Empty,
                "null entries → empty");
            Assert(ContextFormat.BuildDiarySection(new List<DiaryMemoryLine>(), Header, LineFmt, VoiceFmt, "x", true, 1000) == string.Empty,
                "no entries → empty (no lone header, no lone voice line)");
        }

        private static void TestBuildDiarySection_AllBlankEntriesReturnBlank()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "", Summary = "", Date = "5th" },
                null
            };
            Assert(ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, "x", true, 1000) == string.Empty,
                "entries with no title/summary → empty");
        }

        private static void TestBuildDiarySection_TruncatesWholeLines()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "One", Summary = "first", Date = "1" },
                new DiaryMemoryLine { Title = "Two", Summary = "second", Date = "2" }
            };

            // Header (22) + \n + "- One (1): first" (16) = 39. Adding the second line would exceed 45,
            // so only the first memory line survives — and it survives WHOLE (no mid-line cut).
            string result = ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, null, false, 45);
            Assert(result == "recent diary memories:\n- One (1): first",
                "budget drops the whole second line; got:\n" + result);
            Assert(!result.Contains("Two"), "second line fully dropped, never partially included");
        }

        private static void TestBuildDiarySection_TooSmallForOneLineReturnsBlank()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "One", Summary = "first", Date = "1" }
            };
            // Budget smaller than header + first line → nothing worth emitting.
            Assert(ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, null, false, 25) == string.Empty,
                "no memory line fits under budget → empty");
        }

        private static void TestBuildDiarySection_StyleDroppedWhenNoRoom()
        {
            List<DiaryMemoryLine> entries = new List<DiaryMemoryLine>
            {
                new DiaryMemoryLine { Title = "One", Summary = "first", Date = "1" }
            };
            // Exactly enough for header + one line (39), but not the voice line.
            string result = ContextFormat.BuildDiarySection(entries, Header, LineFmt, VoiceFmt, "spare", true, 39);
            Assert(result == "recent diary memories:\n- One (1): first",
                "voice line dropped when it would overflow; got:\n" + result);
        }

        private static void TestFirstSentenceCap()
        {
            Assert(ContextFormat.FirstSentenceCap("Hello world. Second sentence.", 200) == "Hello world.",
                "first sentence extracted");
            Assert(ContextFormat.FirstSentenceCap("No terminator here", 200) == "No terminator here",
                "no terminator → whole string");
            Assert(ContextFormat.FirstSentenceCap("  messy\n\ttext  here.  ", 200) == "messy text here.",
                "whitespace collapsed and trimmed");
            Assert(ContextFormat.FirstSentenceCap("aaaa bbbb cccc", 6) == "aaaa",
                "capped at word boundary");
            Assert(ContextFormat.FirstSentenceCap(null, 10) == string.Empty, "null → empty");
            Assert(ContextFormat.FirstSentenceCap("   ", 10) == string.Empty, "blank → empty");
        }

        private static void TestCapAtWord()
        {
            Assert(ContextFormat.CapAtWord("short", 100) == "short", "fits → unchanged");
            Assert(ContextFormat.CapAtWord("aaaa bbbb", 6) == "aaaa", "cut at whitespace");
            Assert(ContextFormat.CapAtWord("aaaaaaaa", 4) == "aaaa", "no whitespace → hard cut");
            Assert(ContextFormat.CapAtWord("anything", 0) == "anything", "non-positive cap → unchanged");
            Assert(ContextFormat.CapAtWord(null, 5) == string.Empty, "null → empty");
        }

        // ---- ColonyEventsFormat --------------------------------------------------

        private const string ColonyHeader = "colony situation:";

        private static void TestColony_WeightOrdering()
        {
            List<ColonyEventLine> lines = new List<ColonyEventLine>
            {
                new ColonyEventLine { Text = "a quest is open", Weight = 30 },
                new ColonyEventLine { Text = "the colony is under attack", Weight = 100 },
                new ColonyEventLine { Text = "toxic fallout blankets the area", Weight = 50 }
            };

            string result = ColonyEventsFormat.BuildColonySituation(lines, ColonyHeader, 6, 1000);
            string expected = "colony situation:\n- the colony is under attack\n- toxic fallout blankets the area\n- a quest is open";
            Assert(result == expected, "lines ordered by weight desc; got:\n" + result);
        }

        private static void TestColony_EmptyAndBlankHandling()
        {
            Assert(ColonyEventsFormat.BuildColonySituation(null, ColonyHeader, 6, 1000) == string.Empty,
                "null lines → empty");
            Assert(ColonyEventsFormat.BuildColonySituation(new List<ColonyEventLine>(), ColonyHeader, 6, 1000) == string.Empty,
                "no lines → empty (no lone header)");

            List<ColonyEventLine> blanks = new List<ColonyEventLine>
            {
                new ColonyEventLine { Text = "   ", Weight = 100 },
                null,
                new ColonyEventLine { Text = "", Weight = 50 }
            };
            Assert(ColonyEventsFormat.BuildColonySituation(blanks, ColonyHeader, 6, 1000) == string.Empty,
                "all-blank/null lines → empty");

            Assert(ColonyEventsFormat.BuildColonySituation(
                new List<ColonyEventLine> { new ColonyEventLine { Text = "x", Weight = 1 } }, ColonyHeader, 0, 1000) == string.Empty,
                "maxLines 0 → empty");
        }

        private static void TestColony_MaxLinesCap()
        {
            List<ColonyEventLine> lines = new List<ColonyEventLine>
            {
                new ColonyEventLine { Text = "one", Weight = 100 },
                new ColonyEventLine { Text = "two", Weight = 90 },
                new ColonyEventLine { Text = "three", Weight = 80 }
            };
            string result = ColonyEventsFormat.BuildColonySituation(lines, ColonyHeader, 2, 1000);
            Assert(result == "colony situation:\n- one\n- two", "maxLines keeps only the top 2; got:\n" + result);
            Assert(!result.Contains("three"), "third line dropped by the line cap");
        }

        private static void TestColony_MaxCharsWholeLineTruncation()
        {
            List<ColonyEventLine> lines = new List<ColonyEventLine>
            {
                new ColonyEventLine { Text = "first", Weight = 100 },
                new ColonyEventLine { Text = "second", Weight = 90 }
            };
            // Header (17) + \n + "- first" (7) = 25. Adding "- second" (1+8) would exceed 30, so only
            // the first line survives — and it survives WHOLE (no mid-line cut).
            string result = ColonyEventsFormat.BuildColonySituation(lines, ColonyHeader, 6, 30);
            Assert(result == "colony situation:\n- first", "budget drops the whole second line; got:\n" + result);
            Assert(!result.Contains("second"), "second line fully dropped, never partially included");

            // Budget smaller than header + one line → nothing worth emitting.
            Assert(ColonyEventsFormat.BuildColonySituation(lines, ColonyHeader, 6, 20) == string.Empty,
                "no line fits under budget → empty");
        }

        private static void TestColony_EqualWeightKeepsInsertionOrder()
        {
            List<ColonyEventLine> lines = new List<ColonyEventLine>
            {
                new ColonyEventLine { Text = "alpha", Weight = 50 },
                new ColonyEventLine { Text = "bravo", Weight = 50 },
                new ColonyEventLine { Text = "charlie", Weight = 50 }
            };
            string result = ColonyEventsFormat.BuildColonySituation(lines, ColonyHeader, 6, 1000);
            Assert(result == "colony situation:\n- alpha\n- bravo\n- charlie",
                "equal weights keep insertion order (stable sort); got:\n" + result);
        }

        // ---- SharedMemorySelection -----------------------------------------------

        private static void TestPairKey_OrderIndependentAndStable()
        {
            Assert(SharedMemorySelection.PairKey("bob", "alice") == SharedMemorySelection.PairKey("alice", "bob"),
                "pair key is order-independent");
            Assert(SharedMemorySelection.PairKey("alice", "bob") == "alice|bob", "ordered min|max");
            Assert(SharedMemorySelection.PairKey("alice", "alice") == "alice|alice", "self-pair is stable");
            Assert(SharedMemorySelection.PairKey(null, "bob") == "|bob", "null id treated as empty, order-independent");
        }

        private static void TestSelect_EdgeCases()
        {
            Assert(SharedMemorySelection.Select(null, 3, 1).Count == 0, "null → empty");
            Assert(SharedMemorySelection.Select(new List<SharedMemoryCandidate>(), 3, 1).Count == 0, "empty → empty");

            List<SharedMemoryCandidate> three = Candidates(3);
            Assert(SharedMemorySelection.Select(three, 0, 1).Count == 0, "maxPick 0 → empty");
            Assert(SharedMemorySelection.Select(three, -1, 1).Count == 0, "maxPick < 0 → empty");

            List<SharedMemoryCandidate> all = SharedMemorySelection.Select(three, 5, 1);
            Assert(all.Count == 3, "maxPick >= count returns all; got " + all.Count);
            // Returned newest-first: ticks 3,2,1 (Candidates builds ascending tick by index).
            Assert(all[0].Tick == 3 && all[1].Tick == 2 && all[2].Tick == 1,
                "all returned newest-first; got " + all[0].Tick + "," + all[1].Tick + "," + all[2].Tick);
        }

        private static void TestSelect_RecencyBias()
        {
            // Two candidates, equal importance, different age. Across many seeds the newer one (higher
            // tick) should be picked first far more often than the older one.
            int newerFirst = 0;
            int olderFirst = 0;
            for (int seed = 0; seed < 4000; seed++)
            {
                List<SharedMemoryCandidate> c = new List<SharedMemoryCandidate>
                {
                    new SharedMemoryCandidate { Title = "old", Tick = 10 },
                    new SharedMemoryCandidate { Title = "new", Tick = 100 }
                };
                List<SharedMemoryCandidate> picked = SharedMemorySelection.Select(c, 1, seed);
                if (picked[0].Title == "new") newerFirst++; else olderFirst++;
            }
            // recencyWeight 1.0 vs 0.5 → newer expected ~2x. Assert a clear margin.
            Assert(newerFirst > olderFirst * 3 / 2, "newer picked far more often; new=" + newerFirst + " old=" + olderFirst);
            Assert(olderFirst > 0, "older still occasionally picked (weighted, not deterministic); old=" + olderFirst);
        }

        private static void TestSelect_ImportanceBias()
        {
            // Same age, one has both bonuses (cue + conversation), one is plain. The boosted one should
            // win the first pick far more often.
            int boostedFirst = 0;
            int plainFirst = 0;
            for (int seed = 0; seed < 4000; seed++)
            {
                List<SharedMemoryCandidate> c = new List<SharedMemoryCandidate>
                {
                    new SharedMemoryCandidate { Title = "plain", Tick = 50 },
                    new SharedMemoryCandidate { Title = "boosted", Tick = 50, HasAtmosphereCue = true, IsConversationEntry = true }
                };
                List<SharedMemoryCandidate> picked = SharedMemorySelection.Select(c, 1, seed);
                if (picked[0].Title == "boosted") boostedFirst++; else plainFirst++;
            }
            Assert(boostedFirst > plainFirst, "cued+conversation candidate outranks plain of equal age; boosted="
                + boostedFirst + " plain=" + plainFirst);
        }

        private static void TestSelect_DeterministicForSeed()
        {
            List<SharedMemoryCandidate> a = Candidates(5);
            List<SharedMemoryCandidate> b = Candidates(5);
            List<SharedMemoryCandidate> pa = SharedMemorySelection.Select(a, 3, 12345);
            List<SharedMemoryCandidate> pb = SharedMemorySelection.Select(b, 3, 12345);
            Assert(pa.Count == 3 && pb.Count == 3, "maxPick respected; got " + pa.Count + "," + pb.Count);
            bool same = true;
            for (int i = 0; i < pa.Count; i++)
            {
                if (pa[i].Tick != pb[i].Tick) same = false;
            }
            Assert(same, "same seed + inputs → identical selection (deterministic)");
        }

        private static List<SharedMemoryCandidate> Candidates(int n)
        {
            List<SharedMemoryCandidate> list = new List<SharedMemoryCandidate>();
            for (int i = 0; i < n; i++)
            {
                list.Add(new SharedMemoryCandidate { Title = "m" + i, Summary = "s" + i, Date = "d" + i, Tick = i + 1 });
            }
            return list;
        }

        // ---- ConversationAssembler ----------------------------------------------

        private static void TestAssembly_ReplyChainJoins()
        {
            ConversationAssembler asm = new ConversationAssembler();
            asm.Record(Line("A", "", "alice", "bob", 100));
            asm.Record(Line("B", "A", "bob", "alice", 110));
            asm.Record(Line("C", "B", "alice", "bob", 120));

            List<Conversation> flushed = asm.FlushQuiet(10000, 500);
            Assert(flushed.Count == 1, "reply chain forms one conversation; got " + flushed.Count);
            Assert(flushed[0].Lines.Count == 3, "all three lines grouped; got " + flushed[0].Lines.Count);
            Assert(flushed[0].RootTalkId == "A", "root is the opening line id; got " + flushed[0].RootTalkId);
        }

        private static void TestAssembly_UnknownParentStartsNewRoot()
        {
            ConversationAssembler asm = new ConversationAssembler();
            asm.Record(Line("C", "X", "alice", "bob", 100)); // parent X never seen
            List<Conversation> flushed = asm.FlushQuiet(10000, 500);
            Assert(flushed.Count == 1 && flushed[0].RootTalkId == "C",
                "unknown parent opens a new conversation rooted at the line; got " + flushed.Count);
        }

        private static void TestAssembly_InterleavedStaySeparate()
        {
            ConversationAssembler asm = new ConversationAssembler();
            asm.Record(Line("A", "", "alice", "bob", 100));
            asm.Record(Line("P", "", "carol", "dave", 101));
            asm.Record(Line("A2", "A", "bob", "alice", 110));
            asm.Record(Line("P2", "P", "dave", "carol", 111));

            List<Conversation> flushed = asm.FlushQuiet(10000, 500);
            Assert(flushed.Count == 2, "two interleaved conversations stay separate; got " + flushed.Count);
            foreach (Conversation c in flushed)
            {
                Assert(c.Lines.Count == 2, "each conversation has its own two lines; got " + c.Lines.Count);
            }
        }

        private static void TestAssembly_QuietFlushReturnsOnlyStale()
        {
            ConversationAssembler asm = new ConversationAssembler();
            asm.Record(Line("A", "", "alice", "bob", 100));   // last tick 100
            asm.Record(Line("P", "", "carol", "dave", 500));  // last tick 500

            // now=1000, quiet=600: A (900 idle) is stale, P (500 idle) is not.
            List<Conversation> flushed = asm.FlushQuiet(1000, 600);
            Assert(flushed.Count == 1 && flushed[0].RootTalkId == "A",
                "only the quiet conversation flushes; got " + flushed.Count);

            // P still pending until it too goes quiet.
            List<Conversation> later = asm.FlushQuiet(2000, 600);
            Assert(later.Count == 1 && later[0].RootTalkId == "P", "the other flushes once quiet; got " + later.Count);
        }

        private static void TestAssembly_RunawayCapForceFlushes()
        {
            ConversationAssembler asm = new ConversationAssembler();
            // 64 lines in one chain should trip the runaway cap and be handed back on the next flush,
            // even though the conversation is not quiet (now == lastTick).
            asm.Record(Line("root", "", "alice", "bob", 0));
            string parent = "root";
            for (int i = 1; i < 64; i++)
            {
                string id = "n" + i;
                asm.Record(Line(id, parent, i % 2 == 0 ? "alice" : "bob", i % 2 == 0 ? "bob" : "alice", i));
                parent = id;
            }

            List<Conversation> flushed = asm.FlushQuiet(63, 100000);
            Assert(flushed.Count == 1 && flushed[0].Lines.Count == 64,
                "runaway 64-line chain is force-flushed; got " + flushed.Count);
        }

        private static void TestAssembly_FlushAllDrainsEverything()
        {
            ConversationAssembler asm = new ConversationAssembler();
            asm.Record(Line("A", "", "alice", "bob", 100));
            asm.Record(Line("P", "", "carol", "dave", 500));
            List<Conversation> all = asm.FlushAll();
            Assert(all.Count == 2, "FlushAll returns every pending conversation; got " + all.Count);
            Assert(asm.FlushAll().Count == 0, "state cleared after FlushAll");
        }

        // ---- ThrottlePolicy ------------------------------------------------------

        private static void TestThrottle_PerPawnCap()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 2, colonyDailyCap = 999, pairMinGapTicks = 0 };
            Assert(t.TryReserve("alice", "", 0, 0, limits), "1st solo entry for alice reserved");
            Assert(t.TryReserve("alice", "", 1, 0, limits), "2nd solo entry for alice reserved");
            Assert(!t.TryReserve("alice", "", 2, 0, limits), "3rd entry blocked by per-pawn cap");
        }

        private static void TestThrottle_ColonyCap()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 99, colonyDailyCap = 2, pairMinGapTicks = 0 };
            Assert(t.TryReserve("a", "b", 0, 0, limits), "1st colony entry reserved");
            Assert(t.TryReserve("c", "d", 0, 0, limits), "2nd colony entry reserved");
            Assert(!t.TryReserve("e", "f", 0, 0, limits), "3rd blocked by colony cap");
        }

        private static void TestThrottle_PairGap()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 99, colonyDailyCap = 99, pairMinGapTicks = 100 };
            Assert(t.TryReserve("a", "b", 0, 0, limits), "first pair entry reserved");
            Assert(!t.TryReserve("b", "a", 50, 0, limits), "same pair within gap blocked (order-insensitive)");
            Assert(t.TryReserve("a", "b", 100, 0, limits), "same pair after the gap reserved");
        }

        private static void TestThrottle_DayRolloverResets()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 1, colonyDailyCap = 99, pairMinGapTicks = 0 };
            Assert(t.TryReserve("alice", "", 0, 0, limits), "day 0 entry reserved");
            Assert(!t.TryReserve("alice", "", 1, 0, limits), "day 0 second entry blocked by per-pawn cap");
            Assert(t.TryReserve("alice", "", 60000, 1, limits), "day 1 resets the per-pawn counter");
        }

        private static void TestThrottle_ReleaseRefundsCaps()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 99, colonyDailyCap = 1, pairMinGapTicks = 0 };
            Assert(t.TryReserve("a", "b", 0, 0, limits), "colony entry reserved");
            Assert(!t.TryReserve("c", "d", 0, 0, limits), "colony cap blocks a second reservation");
            t.Release("a", "b", 0, limits);
            Assert(t.TryReserve("c", "d", 0, 0, limits), "release refunded the colony slot");
        }

        private static void TestThrottle_ReleaseClearsPairGap()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 99, colonyDailyCap = 99, pairMinGapTicks = 100 };
            Assert(t.TryReserve("a", "b", 0, 0, limits), "pair entry reserved");
            t.Release("a", "b", 0, limits);
            // The entry never happened, so the pair is not blocked by its gap on the very next tick.
            Assert(t.TryReserve("b", "a", 1, 0, limits), "release cleared the pair gap (order-insensitive)");
        }

        private static void TestThrottle_ReleaseIsSafeNoop()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits { perPawnDailyCap = 1, colonyDailyCap = 99, pairMinGapTicks = 0 };
            // Release without a matching reserve must not underflow into negative headroom.
            t.Release("alice", "", 0, limits);
            Assert(t.TryReserve("alice", "", 0, 0, limits), "release-with-nothing left the cap intact (1st ok)");
            Assert(!t.TryReserve("alice", "", 1, 0, limits), "release-with-nothing added no headroom (2nd blocked)");
            // A release naming a day that already rolled is ignored (those counters were cleared already).
            t.Release("alice", "", 5, limits);
            Assert(!t.TryReserve("alice", "", 2, 0, limits), "stale-day release did not refund day 0");
        }

        private static void TestThrottle_RollingPawnCooldown()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits
            {
                perPawnDailyCap = 99,
                colonyDailyCap = 99,
                pairMinGapTicks = 0,
                perPawnCooldownTicks = 60000
            };
            Assert(t.TryReserve("alice", "bob", 100, 0, limits), "cooldown: first pair event reserved");
            Assert(!t.TryReserve("alice", "carol", 60099, 1, limits),
                "cooldown: pawn stays blocked until a full game day of ticks elapsed");
            Assert(t.TryReserve("alice", "carol", 60100, 1, limits),
                "cooldown: exact one-day boundary permits the next event");
        }

        private static void TestThrottle_CooldownChargesBothPawns()
        {
            ThrottlePolicy t = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits
            {
                perPawnDailyCap = 99,
                colonyDailyCap = 99,
                perPawnCooldownTicks = 60000
            };
            Assert(t.TryReserve("alice", "bob", 10, 0, limits), "cooldown pair: first event reserved");
            Assert(t.IsEitherPawnOnCooldown("carol", "bob", 20, 60000),
                "cooldown pair: partner POV is charged too");
            Assert(!t.TryReserve("carol", "bob", 20, 0, limits),
                "cooldown pair: either cooling participant blocks a new event");
        }

        private static void TestThrottle_CooldownReleaseAndPersistence()
        {
            ThrottlePolicy source = new ThrottlePolicy();
            ThrottleLimits limits = new ThrottleLimits
            {
                perPawnDailyCap = 99,
                colonyDailyCap = 99,
                perPawnCooldownTicks = 60000
            };
            Assert(source.TryReserve("alice", "bob", 1234, 0, limits),
                "cooldown persistence: source reservation succeeds");
            Dictionary<string, int> snapshot = source.PawnCooldownSnapshot();
            ThrottlePolicy restored = new ThrottlePolicy();
            restored.RestorePawnCooldowns(snapshot);
            Assert(restored.IsEitherPawnOnCooldown("alice", "bob", 2000, 60000),
                "cooldown persistence: saved timestamps restore for both pawns");
            restored.PruneExpiredPawnCooldowns(61234, 60000);
            Assert(restored.PawnCooldownSnapshot().Count == 0,
                "cooldown persistence: expired timestamps are pruned before saving");
            restored.RestorePawnCooldowns(snapshot);
            restored.Reset();
            Assert(!restored.IsPawnOnCooldown("alice", 2000, 60000),
                "cooldown persistence: new-game reset clears restored state");

            source.Release("alice", "bob", 0, limits);
            Assert(!source.IsEitherPawnOnCooldown("alice", "bob", 1235, 60000),
                "cooldown release: rejected diary submission refunds both timestamps");
        }

        // ---- ConversationContext -------------------------------------------------

        private static void TestContext_ExtraContextShape()
        {
            Conversation c = Conv(
                LineText("alice", "bob", BridgeTalkKind.Chitchat, "Hello there"),
                LineText("bob", "alice", BridgeTalkKind.Chitchat, "Hi back"),
                LineText("alice", "bob", BridgeTalkKind.Event, "Big news"));

            List<string> ctx = ConversationContext.BuildExtraContext(c, 4);
            Assert(ctx[0] == "talk_type=chitchat", "dominant kind first; got " + ctx[0]);
            Assert(ctx[1] == "exchanges=3", "exchange count second; got " + ctx[1]);
            Assert(ctx[2] == "said_1=alice: Hello there", "first quote; got " + ctx[2]);
            Assert(ctx[4] == "said_3=alice: Big news", "third quote; got " + ctx[4]);
        }

        private static void TestContext_TranscriptLineCap()
        {
            Conversation c = Conv(
                LineText("alice", "bob", BridgeTalkKind.Chitchat, "one"),
                LineText("bob", "alice", BridgeTalkKind.Chitchat, "two"),
                LineText("alice", "bob", BridgeTalkKind.Chitchat, "three"));

            List<string> ctx = ConversationContext.BuildExtraContext(c, 2);
            // 2 header lines + at most 2 quoted lines.
            Assert(ctx.Count == 4, "transcript capped to 2 quoted lines; got " + ctx.Count);
            Assert(ctx[3] == "said_2=bob: two", "second quote is the last; got " + ctx[3]);
        }

        private static void TestContext_TranscriptLineCapZeroSuppresses()
        {
            Conversation c = Conv(
                LineText("alice", "bob", BridgeTalkKind.Chitchat, "one"),
                LineText("bob", "alice", BridgeTalkKind.Chitchat, "two"),
                LineText("alice", "bob", BridgeTalkKind.Chitchat, "three"));

            // cap = 0 means "no transcript": only the two summary headers (talk_type, exchanges).
            List<string> ctx0 = ConversationContext.BuildExtraContext(c, 0);
            Assert(ctx0.Count == 2, "cap 0 emits only the 2 headers; got " + ctx0.Count);
            Assert(ctx0[0] == "talk_type=chitchat", "header still present at cap 0; got " + ctx0[0]);
            Assert(ctx0[1] == "exchanges=3", "exchange count still present at cap 0; got " + ctx0[1]);

            // Negative cap behaves the same as 0 (defensive: treat <= 0 as "none").
            List<string> ctxNeg = ConversationContext.BuildExtraContext(c, -1);
            Assert(ctxNeg.Count == 2, "cap -1 emits only the 2 headers; got " + ctxNeg.Count);
        }

        private static void TestContext_DominantKindTieFirstSeen()
        {
            Conversation c = Conv(
                LineText("alice", "bob", BridgeTalkKind.Thought, "a"),
                LineText("bob", "alice", BridgeTalkKind.Chitchat, "b"));
            // One each → tie → first seen (Thought) wins.
            Assert(ConversationContext.DominantKind(c) == BridgeTalkKind.Thought, "tie resolves to first-seen kind");
        }

        // ---- builders / helpers --------------------------------------------------

        private static ConversationLine Line(string talkId, string parentId, string speaker, string target, int tick)
        {
            return new ConversationLine
            {
                TalkId = talkId,
                ParentTalkId = parentId,
                SpeakerId = speaker,
                SpeakerName = speaker,
                TargetId = target,
                TargetName = target,
                Text = "line",
                Kind = BridgeTalkKind.Chitchat,
                Social = BridgeSocialKind.None,
                Tick = tick
            };
        }

        private static ConversationLine LineKind(string speaker, string target, BridgeTalkKind kind, BridgeSocialKind social)
        {
            return new ConversationLine
            {
                SpeakerId = speaker,
                SpeakerName = speaker,
                TargetId = target,
                TargetName = target,
                Text = "line",
                Kind = kind,
                Social = social
            };
        }

        private static ConversationLine LineText(string speaker, string target, BridgeTalkKind kind, string text)
        {
            return new ConversationLine
            {
                SpeakerId = speaker,
                SpeakerName = speaker,
                TargetId = target,
                TargetName = target,
                Text = text,
                Kind = kind,
                Social = BridgeSocialKind.None
            };
        }

        private static Conversation Conv(params ConversationLine[] lines)
        {
            Conversation c = new Conversation();
            c.Lines.AddRange(lines);
            if (lines.Length > 0)
            {
                c.FirstTick = lines[0].Tick;
                c.LastTick = lines[lines.Length - 1].Tick;
            }

            return c;
        }

        private static void Assert(bool condition, string message)
        {
            if (condition)
            {
                passed++;
                return;
            }

            failed++;
            Console.WriteLine("FAIL: " + message);
        }
    }
}
