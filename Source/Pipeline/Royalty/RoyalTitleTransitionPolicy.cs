// Pure faction-aware title transition classification. Localized labels are carried for later prompt
// use but seniority and stable IDs—not English rank words—decide promotion, demotion, and loss.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Classifies an exact before/after title edge and a bounded structural duty delta.</summary>
    internal static class RoyalTitleTransitionPolicy
    {
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
