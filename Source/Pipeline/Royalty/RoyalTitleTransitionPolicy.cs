// Pure faction-aware title transition classification. Localized labels are carried for later prompt
// use but seniority and stable IDs—not English rank words—decide promotion, demotion, and loss.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Classifies an exact before/after title edge and a bounded structural duty delta.</summary>
    internal static class RoyalTitleTransitionPolicy
    {
        /// <summary>
        /// Builds the stable duplicate-suppression identity for one exact title edge. The transition
        /// and both title Def names are part of the key so two different mutations in one paused game
        /// tick (for example, promotion followed by loss) remain two legitimate diary events.
        /// </summary>
        public static string BuildEventDedupKey(
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current,
            string transitionToken,
            int tick)
        {
            bool hasPrevious = Valid(previous);
            bool hasCurrent = Valid(current);
            if (tick < 0 || !RoyalTitleTransitionTokens.IsNarrative(transitionToken)
                || (!hasPrevious && !hasCurrent)) return string.Empty;

            RoyalTitleSnapshot identity = hasCurrent ? current : previous;
            if (hasPrevious && hasCurrent
                && (!Same(previous.pawnId, current.pawnId)
                    || !Same(previous.factionId, current.factionId))) return string.Empty;

            // "none" is a stable internal absence token, not player-facing text. Keeping it explicit
            // makes first-title and title-loss keys readable while avoiding ambiguous empty segments.
            string beforeDefName = hasPrevious ? previous.titleDefName.Trim() : "none";
            string afterDefName = hasCurrent ? current.titleDefName.Trim() : "none";
            return "royalty-title|" + identity.pawnId.Trim()
                + "|" + identity.factionId.Trim()
                + "|" + transitionToken
                + "|" + beforeDefName
                + "|" + afterDefName
                + "|" + tick;
        }

        public static RoyalTitleTransitionDecision Classify(
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current,
            bool claimedByRicherOwner,
            bool groupEnabled,
            RoyaltyPolicySnapshot policy)
        {
            RoyalTitleTransitionDecision result = new RoyalTitleTransitionDecision();
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled || (!Valid(previous) && !Valid(current))) return result;

            if (Valid(previous) && Valid(current)
                && (!Same(previous.pawnId, current.pawnId) || !Same(previous.factionId, current.factionId)))
                return result;

            if (!Valid(previous))
                result.transitionToken = RoyalTitleTransitionTokens.FirstTitle;
            else if (!Valid(current))
                result.transitionToken = RoyalTitleTransitionTokens.Loss;
            else if (Same(previous.titleDefName, current.titleDefName))
                result.transitionToken = RoyalTitleTransitionTokens.NoChange;
            else if (current.seniority > previous.seniority)
                result.transitionToken = RoyalTitleTransitionTokens.Promotion;
            else if (current.seniority < previous.seniority)
                result.transitionToken = RoyalTitleTransitionTokens.Demotion;
            else
                result.transitionToken = RoyalTitleTransitionTokens.NoChange;

            if (result.transitionToken == RoyalTitleTransitionTokens.NoChange) return result;
            result.shouldAdvanceObservation = true;
            result.claimedByRicherOwner = claimedByRicherOwner;
            result.shouldEmit = groupEnabled && !claimedByRicherOwner;
            result.introducedDutyCategoryTokens = DutyDelta(
                previous == null ? null : previous.dutyCategoryTokens,
                current == null ? null : current.dutyCategoryTokens,
                effective.maximumDutyCategoryTokens);
            return result;
        }

        private static List<string> DutyDelta(IList<string> before, IList<string> after, int requestedCap)
        {
            List<string> result = new List<string>();
            if (after == null) return result;
            HashSet<string> old = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (before != null)
            {
                for (int i = 0; i < before.Count; i++)
                {
                    string token = CleanToken(before[i]);
                    if (token.Length > 0) old.Add(token);
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < after.Count; i++)
            {
                string token = CleanToken(after[i]);
                if (token.Length > 0 && !old.Contains(token) && seen.Add(token)) result.Add(token);
            }
            result.Sort(StringComparer.Ordinal.Compare);
            int cap = requestedCap < 1 || requestedCap > 8 ? 2 : requestedCap;
            if (result.Count > cap) result.RemoveRange(cap, result.Count - cap);
            return result;
        }

        private static bool Valid(RoyalTitleSnapshot value)
        {
            return value != null && SafeId(value.pawnId) && SafeId(value.factionId)
                && SafeId(value.titleDefName);
        }

        private static bool SafeId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0;
        }

        private static bool Same(string left, string right)
        {
            string a = (left ?? string.Empty).Trim();
            string b = (right ?? string.Empty).Trim();
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string CleanToken(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }
    }
}
