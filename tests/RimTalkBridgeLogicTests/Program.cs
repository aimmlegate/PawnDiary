// Pure unit tests for the RimTalk bridge's decision logic. Mirrors the other tests/* console
// projects: a static Main runs focused assertions and returns non-zero when any fail.
//
// These run without RimWorld/RimTalk/Verse/Unity — the logic under test (conversation assembly,
// importance, throttling, context formatting) is deliberately pure so its edge cases are covered
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

            // ConversationAssembler
            TestAssembly_ReplyChainJoins();
            TestAssembly_UnknownParentStartsNewRoot();
            TestAssembly_InterleavedStaySeparate();
            TestAssembly_QuietFlushReturnsOnlyStale();
            TestAssembly_RunawayCapForceFlushes();
            TestAssembly_FlushAllDrainsEverything();

            // ImportancePolicy
            TestImportance_TalkKindTrigger();
            TestImportance_SocialTrigger();
            TestImportance_LengthTriggerAndBoundary();
            TestImportance_MonologueNeverImportant();
            TestImportance_ShortChitchatNeverImportant();

            // ThrottlePolicy
            TestThrottle_PerPawnCap();
            TestThrottle_ColonyCap();
            TestThrottle_PairGap();
            TestThrottle_DayRolloverResets();

            // ConversationContext
            TestContext_ExtraContextShape();
            TestContext_TranscriptLineCap();
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

        // ---- ImportancePolicy ----------------------------------------------------

        private static void TestImportance_TalkKindTrigger()
        {
            Conversation c = Conv(
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("bob", "alice", BridgeTalkKind.Urgent, BridgeSocialKind.None));
            // minReplies high so only the talk-kind path can trigger.
            Assert(ImportancePolicy.IsImportant(c, 99), "urgent talk kind makes it important");
            Assert(ImportancePolicy.Explain(c, 99).StartsWith("talk_kind=Urgent"), "reason names the kind");
        }

        private static void TestImportance_SocialTrigger()
        {
            Conversation c = Conv(
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("bob", "alice", BridgeTalkKind.Chitchat, BridgeSocialKind.Insult));
            Assert(ImportancePolicy.IsImportant(c, 99), "an insult makes it important");
            Assert(ImportancePolicy.Explain(c, 99).StartsWith("social=Insult"), "reason names the social kind");
        }

        private static void TestImportance_LengthTriggerAndBoundary()
        {
            Conversation four = Conv(
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("bob", "alice", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("bob", "alice", BridgeTalkKind.Chitchat, BridgeSocialKind.None));
            Assert(ImportancePolicy.IsImportant(four, 4), "exactly minReplies lines is important");

            Conversation three = Conv(
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("bob", "alice", BridgeTalkKind.Chitchat, BridgeSocialKind.None),
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.None));
            Assert(!ImportancePolicy.IsImportant(three, 4), "below minReplies and otherwise plain → not important");
        }

        private static void TestImportance_MonologueNeverImportant()
        {
            // Single participant (no target), many lines, urgent kind — still never important.
            Conversation c = Conv(
                LineKind("alice", "", BridgeTalkKind.Urgent, BridgeSocialKind.Kind),
                LineKind("alice", "", BridgeTalkKind.Urgent, BridgeSocialKind.Kind),
                LineKind("alice", "", BridgeTalkKind.Urgent, BridgeSocialKind.Kind));
            Assert(!ImportancePolicy.IsImportant(c, 2), "a monologue is never important");
        }

        private static void TestImportance_ShortChitchatNeverImportant()
        {
            Conversation c = Conv(
                LineKind("alice", "bob", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat),
                LineKind("bob", "alice", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat));
            Assert(!ImportancePolicy.IsImportant(c, 4), "plain 2-line chitchat is not important");
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
