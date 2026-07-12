// Pure planning and text cleanup for the SpeakUp whole-conversation bridge. The runtime adapter
// observes SpeakUp Talk/Statement objects through reflection, then hands only primitive facts and
// rendered strings to this file. Keeping the threshold decision and sample cleanup here means their
// edge cases can be tested without loading RimWorld, Verse, Unity, Harmony, or SpeakUp.
//
// New to C#? See the repository AGENTS.md. This is the equivalent of a small pure formatter: it has
// no global state and performs no I/O, translation, reflection, game lookup, or random selection.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiarySpeakUp.Pure
{
    /// <summary>
    /// Immutable decision returned for a conversation that crossed the configured reply threshold.
    /// A null plan means the exchange should remain in Tier 1 ambient capture only.
    /// </summary>
    public sealed class TalkSummaryPlan
    {
        public TalkSummaryPlan(int exchangeCount, IList<string> sampledLines)
        {
            ExchangeCount = exchangeCount;
            SampledLines = new List<string>(sampledLines ?? new string[0]).AsReadOnly();
        }

        /// <summary>The verified SpeakUp latestReplyCount stored on the completed Talk.</summary>
        public int ExchangeCount { get; }

        /// <summary>Clean, unique, bounded rendered lines, kept in delivery order.</summary>
        public IReadOnlyList<string> SampledLines { get; }

        /// <summary>
        /// Joins the sampled evidence for a localized summary template. The separator is punctuation,
        /// not English prose; all player/LLM-facing words stay in Keyed localization.
        /// </summary>
        public string JoinedSamples()
        {
            return string.Join(" | ", SampledLines);
        }
    }

    /// <summary>
    /// Decides whether a SpeakUp Talk is substantial enough for Tier 2 and sanitizes its sampled lines.
    /// </summary>
    public static class TalkSummaryFormat
    {
        /// <summary>Stable External eventKey persisted by Pawn Diary. Never rename after release.</summary>
        public const string ConversationEventKey = "speakupbridge_conversation";

        /// <summary>Default matching SpeakUp's normal three scheduled replies.</summary>
        public const int DefaultMinimumReplies = 3;

        /// <summary>Small evidence cap used by the runtime bridge; enough to show a conversation arc.</summary>
        public const int DefaultSampleLineLimit = 3;

        // Defensive caps, not tuning policy: even a corrupt setting or third-party renderer cannot make
        // one external event balloon without bound. Normal settings never approach either ceiling.
        private const int HardSampleLineLimit = 8;
        private const int HardLineLength = 300;

        /// <summary>
        /// Returns a submission plan when <paramref name="latestReplyCount"/> reaches the configured
        /// minimum; otherwise returns null. Blank and duplicate lines are dropped, whitespace is folded,
        /// and the remaining evidence is kept in original order up to the requested bounded limit.
        /// </summary>
        public static TalkSummaryPlan Plan(
            int latestReplyCount,
            int minimumReplies,
            int maxSampleLines,
            IEnumerable<string> renderedLines)
        {
            int effectiveMinimum = Math.Max(1, minimumReplies);
            if (latestReplyCount < effectiveMinimum)
            {
                return null;
            }

            int effectiveLineLimit = Math.Max(0, Math.Min(HardSampleLineLimit, maxSampleLines));
            List<string> samples = new List<string>(effectiveLineLimit);
            if (effectiveLineLimit > 0 && renderedLines != null)
            {
                foreach (string renderedLine in renderedLines)
                {
                    string clean = CleanLine(renderedLine);
                    if (clean.Length == 0 || ContainsOrdinal(samples, clean))
                    {
                        continue;
                    }

                    samples.Add(clean);
                    if (samples.Count >= effectiveLineLimit)
                    {
                        break;
                    }
                }
            }

            return new TalkSummaryPlan(latestReplyCount, samples);
        }

        private static string CleanLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder clean = new StringBuilder(Math.Min(value.Length, HardLineLength));
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = clean.Length > 0;
                    continue;
                }

                if (pendingSpace && clean.Length < HardLineLength)
                {
                    clean.Append(' ');
                }

                pendingSpace = false;
                clean.Append(c);
                if (clean.Length >= HardLineLength)
                {
                    break;
                }
            }

            string result = clean.ToString().Trim();
            if (result.Length >= HardLineLength && value.Length > result.Length)
            {
                result = result.Substring(0, HardLineLength - 1).TrimEnd() + "…";
            }

            return result;
        }

        private static bool ContainsOrdinal(List<string> values, string candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
