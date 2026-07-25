// Raid "combat beats" rules (Quality Wave §7.1, H1).
//
// A raid page waits for the POV pawn's own fight to go quiet, then quotes the strongest one or two
// moments from RimWorld's combat log. Everything that decides WHICH moments, WHEN to stop waiting,
// and HOW the result is folded into the saved context string is pure and lives in
// Source/Pipeline/BattleBeatsPolicy.cs, so it is verified here without RimWorld: score lookup,
// deterministic selection and chronological output, prompt-safe sanitizing and capping, the
// retry/quiet/deadline rule, and the once-only context fields.
using System.Collections.Generic;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestBattleBeatsPolicy()
        {
            TestBattleBeatsScoring();
            TestBattleBeatsSelection();
            TestBattleBeatsFormatting();
            TestBattleBeatsWaitDecision();
            TestBattleBeatsContextFields();
        }

        /// <summary>The shipped default ordering: a down/kill outranks a hit, which outranks a graze.</summary>
        private static List<BattleBeatScoreRule> BattleBeatRules()
        {
            return new List<BattleBeatScoreRule>
            {
                new BattleBeatScoreRule { kind = BattleBeatsPolicy.KindTransition, score = 100 },
                new BattleBeatScoreRule { kind = BattleBeatsPolicy.KindHit, score = 60 },
                new BattleBeatScoreRule { kind = BattleBeatsPolicy.KindDeflected, score = 25 },
                new BattleBeatScoreRule { kind = BattleBeatsPolicy.KindMiss, score = 10 },
                new BattleBeatScoreRule { kind = BattleBeatsPolicy.KindOther, score = 5 }
            };
        }

        private static BattleBeatCandidate BattleBeat(int tick, string kind, string text, int sequence)
        {
            return new BattleBeatCandidate { tick = tick, kind = kind, text = text, sequence = sequence };
        }

        // --- Score lookup -----------------------------------------------------------------------------
        private static void TestBattleBeatsScoring()
        {
            List<BattleBeatScoreRule> rules = BattleBeatRules();

            AssertEqual("H1 a down/kill scores highest",
                100, BattleBeatsPolicy.ScoreFor(BattleBeatsPolicy.KindTransition, rules, 1));
            AssertEqual("H1 a landed hit outranks a graze",
                60, BattleBeatsPolicy.ScoreFor(BattleBeatsPolicy.KindHit, rules, 1));
            AssertEqual("H1 XML casing still matches a kind token",
                25, BattleBeatsPolicy.ScoreFor("Deflected", rules, 1));
            AssertEqual("H1 an unlisted kind uses the caller fallback",
                1, BattleBeatsPolicy.ScoreFor("modded_kind", rules, 1));
            AssertEqual("H1 a blank kind uses the caller fallback",
                1, BattleBeatsPolicy.ScoreFor("  ", rules, 1));
            AssertEqual("H1 absent rules use the caller fallback",
                7, BattleBeatsPolicy.ScoreFor(BattleBeatsPolicy.KindHit, null, 7));
        }

        // --- Selection: strongest beats, returned chronologically --------------------------------------
        private static void TestBattleBeatsSelection()
        {
            List<BattleBeatScoreRule> rules = BattleBeatRules();
            List<BattleBeatCandidate> candidates = new List<BattleBeatCandidate>
            {
                BattleBeat(100, BattleBeatsPolicy.KindMiss, "I missed", 0),
                BattleBeat(400, BattleBeatsPolicy.KindTransition, "the raider went down", 3),
                BattleBeat(200, BattleBeatsPolicy.KindHit, "I hit an arm", 1),
                BattleBeat(300, BattleBeatsPolicy.KindDeflected, "armour turned it", 2)
            };

            List<BattleBeatCandidate> selected = BattleBeatsPolicy.Select(candidates, 2, rules, 0);
            AssertEqual("H1 selection honours the count cap", 2, selected.Count);
            AssertEqual("H1 the strongest beats are chosen (hit)",
                BattleBeatsPolicy.KindHit, selected[0].kind);
            AssertEqual("H1 the strongest beats are chosen (down)",
                BattleBeatsPolicy.KindTransition, selected[1].kind);
            AssertTrue("H1 selected beats are returned in chronological order",
                selected[0].tick < selected[1].tick);

            // The same evidence in a different list order must select and order identically, because a
            // battle's entry list is not guaranteed to arrive sorted.
            List<BattleBeatCandidate> shuffled = new List<BattleBeatCandidate>
            {
                candidates[3], candidates[1], candidates[0], candidates[2]
            };
            List<BattleBeatCandidate> reselected = BattleBeatsPolicy.Select(shuffled, 2, rules, 0);
            AssertEqual("H1 selection does not depend on input order (first)",
                selected[0].text, reselected[0].text);
            AssertEqual("H1 selection does not depend on input order (second)",
                selected[1].text, reselected[1].text);

            // Equal scores: the later moment wins, because a raid's closing beat reads as its outcome.
            List<BattleBeatCandidate> ties = new List<BattleBeatCandidate>
            {
                BattleBeat(100, BattleBeatsPolicy.KindHit, "early hit", 0),
                BattleBeat(900, BattleBeatsPolicy.KindHit, "late hit", 1)
            };
            List<BattleBeatCandidate> tieWinner = BattleBeatsPolicy.Select(ties, 1, rules, 0);
            AssertEqual("H1 an equal-score tie prefers the later moment",
                "late hit", tieWinner[0].text);

            // A same-tick tie still resolves deterministically through the entry's own position.
            List<BattleBeatCandidate> exactTies = new List<BattleBeatCandidate>
            {
                BattleBeat(500, BattleBeatsPolicy.KindHit, "first entry", 0),
                BattleBeat(500, BattleBeatsPolicy.KindHit, "second entry", 1)
            };
            AssertEqual("H1 a same-tick tie prefers the later battle entry",
                "second entry", BattleBeatsPolicy.Select(exactTies, 1, rules, 0)[0].text);

            AssertEqual("H1 blank-text candidates never occupy a slot",
                1,
                BattleBeatsPolicy.Select(
                    new List<BattleBeatCandidate>
                    {
                        BattleBeat(100, BattleBeatsPolicy.KindTransition, "   ", 0),
                        BattleBeat(200, BattleBeatsPolicy.KindMiss, "a real line", 1)
                    },
                    2, rules, 0).Count);
            AssertEqual("H1 a zero cap selects nothing",
                0, BattleBeatsPolicy.Select(candidates, 0, rules, 0).Count);
            AssertEqual("H1 absent candidates select nothing",
                0, BattleBeatsPolicy.Select(null, 2, rules, 0).Count);
        }

        // --- Prompt-safe joining and capping ----------------------------------------------------------
        private static void TestBattleBeatsFormatting()
        {
            AssertEqual("H1 a beat's semicolon cannot end its context field",
                "I fired, then reloaded", BattleBeatsPolicy.SanitizeBeatText("I fired; then reloaded"));
            AssertEqual("H1 a beat's equals sign cannot look like a nested key",
                "damage-12", BattleBeatsPolicy.SanitizeBeatText("damage=12"));
            AssertEqual("H1 a beat collapses onto one line",
                "shot fired hit landed",
                BattleBeatsPolicy.SanitizeBeatText("shot fired\r\n\thit  landed"));
            AssertEqual("H1 a blank beat sanitizes to empty",
                string.Empty, BattleBeatsPolicy.SanitizeBeatText("   "));

            List<BattleBeatCandidate> selected = new List<BattleBeatCandidate>
            {
                BattleBeat(100, BattleBeatsPolicy.KindHit, "I shot the raider", 0),
                BattleBeat(200, BattleBeatsPolicy.KindTransition, "the raider fell", 1)
            };
            AssertEqual("H1 beats join with the separator",
                "I shot the raider | the raider fell",
                BattleBeatsPolicy.FormatBeats(selected, 240));
            AssertEqual("H1 an over-long join is capped with an explicit ellipsis",
                "I shot the raider...",
                BattleBeatsPolicy.FormatBeats(selected, 20));
            AssertEqual("H1 a cap never freezes a partial word or separator",
                "I shot the...",
                BattleBeatsPolicy.FormatBeats(selected, 19));
            AssertEqual("H1 a tiny cap returns a bounded ellipsis",
                "..",
                BattleBeatsPolicy.FormatBeats(selected, 2));
            AssertEqual("H1 no beats format to an empty field",
                string.Empty, BattleBeatsPolicy.FormatBeats(new List<BattleBeatCandidate>(), 240));
            AssertEqual("H1 absent beats format to an empty field",
                string.Empty, BattleBeatsPolicy.FormatBeats(null, 240));

            string formatted = BattleBeatsPolicy.FormatBeats(selected, 240);
            AssertTrue("H1 a formatted field never contains a context delimiter",
                formatted.IndexOf(';') < 0 && formatted.IndexOf('=') < 0);
        }

        // --- Retry versus emit ------------------------------------------------------------------------
        private static void TestBattleBeatsWaitDecision()
        {
            // No POV battle yet: the raiders may still be walking in, so keep waiting.
            AssertTrue("H1 waits while no POV battle exists yet",
                BattleBeatsPolicy.Decide(false, 0, 1000, 1500, 1200, 30000) == BattleBeatsDecision.Retry);

            // A battle exists but is still producing entries for this pawn.
            AssertTrue("H1 waits while the POV's combat is still active",
                BattleBeatsPolicy.Decide(true, 1400, 1000, 2000, 1200, 30000) == BattleBeatsDecision.Retry);
            AssertTrue("H1 the quiet interval is inclusive at its boundary",
                BattleBeatsPolicy.Decide(true, 1400, 1000, 2600, 1200, 30000) == BattleBeatsDecision.Emit);
            AssertTrue("H1 emits once the POV's combat has gone quiet",
                BattleBeatsPolicy.Decide(true, 1400, 1000, 9000, 1200, 30000) == BattleBeatsDecision.Emit);

            // The hard deadline outranks everything: an endless siege must still produce a page.
            AssertTrue("H1 the hard deadline emits even with no battle found",
                BattleBeatsPolicy.Decide(false, 0, 1000, 31000, 1200, 30000) == BattleBeatsDecision.Emit);
            AssertTrue("H1 the hard deadline emits even during active combat",
                BattleBeatsPolicy.Decide(true, 30999, 1000, 31000, 1200, 30000) == BattleBeatsDecision.Emit);
            AssertTrue("H1 the deadline boundary itself emits",
                BattleBeatsPolicy.Decide(false, 0, 1000, 31000, 1200, 30000) == BattleBeatsDecision.Emit);
            AssertTrue("H1 one tick before the deadline still waits",
                BattleBeatsPolicy.Decide(false, 0, 1000, 30999, 1200, 30000) == BattleBeatsDecision.Retry);

            // Disabled tunables degrade to "do not wait" rather than to an infinite wait.
            AssertTrue("H1 a zero quiet interval emits as soon as a battle is found",
                BattleBeatsPolicy.Decide(true, 2000, 1000, 2000, 0, 30000) == BattleBeatsDecision.Emit);
            AssertTrue("H1 a zero deadline never forces an early emit",
                BattleBeatsPolicy.Decide(false, 0, 1000, 999000, 1200, 0) == BattleBeatsDecision.Retry);
        }

        // --- Saved context fields ---------------------------------------------------------------------
        private static void TestBattleBeatsContextFields()
        {
            string raid = "raid=RaidEnemy; label=Raid; faction=Pirate; points=350";

            AssertTrue("H1 a fresh raid context has not been mined",
                !BattleBeatsPolicy.AlreadyChecked(raid));

            string withBeats = BattleBeatsPolicy.ApplyToContext(raid, "I shot the raider | the raider fell");
            AssertEqual("H1 mining appends the beats then the checked marker",
                raid + "; battle_beats=I shot the raider | the raider fell; battle_beats_checked=true",
                withBeats);
            AssertTrue("H1 a mined context reports itself as checked",
                BattleBeatsPolicy.AlreadyChecked(withBeats));
            AssertEqual("H1 the beats survive context parsing intact",
                "I shot the raider | the raider fell",
                DiaryContextFields.Value(withBeats, BattleBeatsPolicy.BeatsContextKey));

            // A peaceful raid still records that mining ran, so a reload cannot restart the scan
            // against a combat log that has since been pruned.
            string withoutBeats = BattleBeatsPolicy.ApplyToContext(raid, string.Empty);
            AssertEqual("H1 finding no beats still records the checked marker",
                raid + "; battle_beats_checked=true", withoutBeats);
            AssertTrue("H1 finding no beats omits the beats field entirely",
                !DiaryContextFields.HasField(withoutBeats, BattleBeatsPolicy.BeatsContextKey));

            AssertEqual("H1 re-applying to an already mined context changes nothing",
                withBeats, BattleBeatsPolicy.ApplyToContext(withBeats, "a later line"));
            AssertEqual("H1 a trailing separator is not doubled",
                raid + "; battle_beats_checked=true",
                BattleBeatsPolicy.ApplyToContext(raid + "; ", string.Empty));
            AssertEqual("H1 an empty context still gains the checked marker",
                "battle_beats_checked=true", BattleBeatsPolicy.ApplyToContext(string.Empty, string.Empty));
            AssertEqual("H1 an absent context still gains the checked marker",
                "battle_beats_checked=true", BattleBeatsPolicy.ApplyToContext(null, null));
        }
    }
}
