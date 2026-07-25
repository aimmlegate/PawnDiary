// Pure Quality Wave H1 policy: which combat-log moments become a raid page's "combat beats" field,
// how long generation waits for the fight to finish, and how the result is folded into the saved
// game-context string.
//
// The impure half (Source/Generation/BattleBeatsBuilder.cs) reads Verse.BattleLog, classifies each
// log entry into one of the stable kind tokens below, and hands plain candidates here. Everything in
// this file is primitive-in / primitive-out so the pure test harness can cover it without RimWorld.
//
// Tick contract: every tick reaching this file is a GAME tick (Find.TickManager.TicksGame). RimWorld's
// combat log stores ABSOLUTE ticks (LogEntry.Tick returns the private ticksAbs field), so the builder
// converts before calling in. Mixing the two frames would silently make every fight look ancient.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One XML-authored score for a combat-log beat kind. Higher scores win a selection slot; the
    /// tokens are the stable <c>Kind*</c> constants on <see cref="BattleBeatsPolicy"/>.
    /// </summary>
    public sealed class BattleBeatScoreRule
    {
        public string kind;
        public int score;
    }

    /// <summary>
    /// One candidate combat moment, already detached from the game. <see cref="text"/> is the
    /// POV-rendered log line with rich-text tags removed; <see cref="sequence"/> is the entry's index
    /// inside its battle and only exists to break exact ties deterministically.
    /// </summary>
    internal sealed class BattleBeatCandidate
    {
        public int tick;
        public string kind;
        public string text;
        public int sequence;
    }

    /// <summary>What the generation queue should do with a raid page that is still mining beats.</summary>
    internal enum BattleBeatsDecision
    {
        /// <summary>Combat is unresolved and the deadline is not reached — check again later.</summary>
        Retry,

        /// <summary>Quiet interval passed or the hard deadline hit — write what we have and queue.</summary>
        Emit
    }

    /// <summary>
    /// Owns every pure H1 decision: beat scoring/selection, the retry-versus-emit rule, prompt-safe
    /// formatting, and the saved context fields. Shared by the runtime builder and the tests.
    /// </summary>
    internal static class BattleBeatsPolicy
    {
        /// <summary>Context key holding the joined beats. Also the template's <c>contextKey</c>.</summary>
        public const string BeatsContextKey = "battle_beats";

        /// <summary>
        /// Context key proving mining already ran for this event. Saved (not session-only) on purpose:
        /// a reloaded game must not restart mining on a raid whose combat log has long since been
        /// pruned, which would strand the page in a permanent retry loop.
        /// </summary>
        public const string CheckedContextKey = "battle_beats_checked";

        // Stable classification tokens. These are structured schema words, not player-facing text, so
        // they stay English by the AGENTS.md section 12 carve-out.
        /// <summary>A pawn went down or died — the strongest kind of beat.</summary>
        public const string KindTransition = "transition";

        /// <summary>An attack that actually damaged a body part.</summary>
        public const string KindHit = "hit";

        /// <summary>An attack stopped by armour.</summary>
        public const string KindDeflected = "deflected";

        /// <summary>An attack that missed outright.</summary>
        public const string KindMiss = "miss";

        /// <summary>Anything else in the battle (stuns, ability use, unclassifiable entries).</summary>
        public const string KindOther = "other";

        /// <summary>Joins selected beats. Contains no ';' or '=' so it survives context parsing.</summary>
        public const string BeatSeparator = " | ";

        /// <summary>
        /// Returns the authored score for a beat kind, or <paramref name="fallback"/> when the kind is
        /// blank or unlisted. Matching is case-insensitive so XML casing cannot silently miss.
        /// </summary>
        public static int ScoreFor(string kind, IReadOnlyList<BattleBeatScoreRule> rules, int fallback)
        {
            if (rules == null || string.IsNullOrWhiteSpace(kind))
            {
                return fallback;
            }

            string wanted = kind.Trim();
            for (int i = 0; i < rules.Count; i++)
            {
                BattleBeatScoreRule rule = rules[i];
                if (rule != null && string.Equals(rule.kind, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.score;
                }
            }

            return fallback;
        }

        /// <summary>
        /// Picks the strongest <paramref name="maxCount"/> beats and returns them in chronological
        /// order. Selection ranks by score, then by the later tick (a raid's closing moments read as
        /// its outcome), then by the later position in the battle — so the result never depends on
        /// list order alone. Candidates with blank text are ignored.
        /// </summary>
        public static List<BattleBeatCandidate> Select(
            IReadOnlyList<BattleBeatCandidate> candidates,
            int maxCount,
            IReadOnlyList<BattleBeatScoreRule> rules,
            int fallbackScore)
        {
            List<BattleBeatCandidate> selected = new List<BattleBeatCandidate>();
            if (candidates == null || maxCount <= 0)
            {
                return selected;
            }

            List<BattleBeatCandidate> usable = new List<BattleBeatCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                BattleBeatCandidate candidate = candidates[i];
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.text))
                {
                    usable.Add(candidate);
                }
            }

            // List.Sort is not a stable sort, which is exactly why every comparison below ends on the
            // entry's own sequence: two candidates can never compare equal, so the order is total.
            usable.Sort((left, right) =>
            {
                int leftScore = ScoreFor(left.kind, rules, fallbackScore);
                int rightScore = ScoreFor(right.kind, rules, fallbackScore);
                if (leftScore != rightScore)
                {
                    return rightScore.CompareTo(leftScore);
                }

                if (left.tick != right.tick)
                {
                    return right.tick.CompareTo(left.tick);
                }

                return right.sequence.CompareTo(left.sequence);
            });

            int take = Math.Min(maxCount, usable.Count);
            for (int i = 0; i < take; i++)
            {
                selected.Add(usable[i]);
            }

            selected.Sort((left, right) => left.tick != right.tick
                ? left.tick.CompareTo(right.tick)
                : left.sequence.CompareTo(right.sequence));
            return selected;
        }

        /// <summary>
        /// Joins the selected beats into one prompt-safe context value, capped to
        /// <paramref name="maxChars"/>. Returns an empty string when nothing survives sanitizing, so
        /// the caller can omit the field entirely.
        /// </summary>
        public static string FormatBeats(IReadOnlyList<BattleBeatCandidate> selected, int maxChars)
        {
            if (selected == null || selected.Count == 0)
            {
                return string.Empty;
            }

            string joined = string.Empty;
            for (int i = 0; i < selected.Count; i++)
            {
                BattleBeatCandidate candidate = selected[i];
                string text = SanitizeBeatText(candidate == null ? null : candidate.text);
                if (text.Length == 0)
                {
                    continue;
                }

                joined = joined.Length == 0 ? text : joined + BeatSeparator + text;
            }

            if (joined.Length == 0 || maxChars <= 0)
            {
                return joined.Length == 0 ? string.Empty : joined;
            }

            return TextTruncation.SafePrefix(joined, maxChars).TrimEnd();
        }

        /// <summary>
        /// Makes one log line safe to store inside the semicolon/equals-delimited game-context string:
        /// newlines collapse to spaces, ';' becomes ',' (it would end the field) and '=' becomes '-'
        /// (it would look like a nested key). Mirrors the other context formatters in this repo.
        /// </summary>
        public static string SanitizeBeatText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return CollapseWhitespace(value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace(';', ',')
                .Replace('=', '-'));
        }

        /// <summary>
        /// The retry-versus-emit rule, in game ticks. The hard deadline is checked first and always
        /// wins: an endless siege must still produce a page from the best evidence available. Below
        /// the deadline we wait while no POV battle exists yet (the raiders may still be walking in)
        /// and while the POV's own combat is still producing entries, so an early miss cannot hide a
        /// later down or kill.
        /// </summary>
        public static BattleBeatsDecision Decide(
            bool battleFound,
            int latestRelevantTick,
            int eventTick,
            int now,
            int quietTicks,
            int maxAgeTicks)
        {
            if (maxAgeTicks > 0 && now - eventTick >= maxAgeTicks)
            {
                return BattleBeatsDecision.Emit;
            }

            if (!battleFound)
            {
                return BattleBeatsDecision.Retry;
            }

            if (quietTicks > 0 && now - latestRelevantTick < quietTicks)
            {
                return BattleBeatsDecision.Retry;
            }

            return BattleBeatsDecision.Emit;
        }

        /// <summary>
        /// True when this event has already been mined. Every emitted raid page carries the marker,
        /// including one that found nothing, so a reload cannot restart the scan.
        /// </summary>
        public static bool AlreadyChecked(string gameContext)
        {
            return DiaryContextFields.IsTrue(gameContext, CheckedContextKey);
        }

        /// <summary>
        /// Folds the mining result into the saved context: the beats field when non-empty, then the
        /// permanent checked marker. Existing keys are never duplicated, so a defensive second call
        /// leaves the context unchanged.
        /// </summary>
        public static string ApplyToContext(string gameContext, string beats)
        {
            string context = (gameContext ?? string.Empty).Trim();
            string value = (beats ?? string.Empty).Trim();

            if (value.Length > 0 && !DiaryContextFields.HasField(context, BeatsContextKey))
            {
                context = AppendField(context, BeatsContextKey, value);
            }

            if (!AlreadyChecked(context))
            {
                context = AppendField(context, CheckedContextKey, "true");
            }

            return context;
        }

        private static string AppendField(string context, string key, string value)
        {
            return context.Length == 0
                ? key + "=" + value
                : context.TrimEnd(';', ' ') + "; " + key + "=" + value;
        }

        /// <summary>
        /// Squeezes runs of spaces into one and trims the ends. Written as a manual scan rather than a
        /// regex because this runs once per selected beat on the generation path.
        /// </summary>
        private static string CollapseWhitespace(string value)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }
    }
}
