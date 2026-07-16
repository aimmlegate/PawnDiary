// Deterministic pure POV selection for Odyssey landing chapters. Pilot and copilot have priority;
// exact eligible crew provide stable-ID fallback. Missing/dead/ineligible/no-diary pawns never write.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Selects at most two distinct eligible landing writers without global randomness.</summary>
    internal static class OdysseyWriterPolicy
    {
        /// <summary>Returns copied, ordered candidates so later mutation cannot change event-time truth.</summary>
        public static List<OdysseyWriterCandidate> Select(
            List<OdysseyWriterCandidate> candidates,
            int requestedMaximum)
        {
            int maximum = requestedMaximum <= 0 ? 2 : Math.Min(2, requestedMaximum);
            List<OdysseyWriterCandidate> ranked = new List<OdysseyWriterCandidate>();
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    OdysseyWriterCandidate candidate = candidates[i];
                    if (IsEligible(candidate))
                    {
                        ranked.Add(Copy(candidate));
                    }
                }
            }

            ranked.Sort(Compare);
            List<OdysseyWriterCandidate> selected = new List<OdysseyWriterCandidate>();
            for (int i = 0; i < ranked.Count && selected.Count < maximum; i++)
            {
                if (!ContainsPawn(selected, ranked[i].pawnId))
                {
                    selected.Add(ranked[i]);
                }
            }

            return selected;
        }

        private static bool IsEligible(OdysseyWriterCandidate candidate)
        {
            return candidate != null
                && candidate.eligible
                && candidate.present
                && candidate.hasDiary
                && !string.IsNullOrWhiteSpace(candidate.pawnId)
                && OdysseyJourneyRoleTokens.Rank(candidate.roleToken) != int.MaxValue;
        }

        private static int Compare(OdysseyWriterCandidate left, OdysseyWriterCandidate right)
        {
            int value = OdysseyJourneyRoleTokens.Rank(left.roleToken)
                .CompareTo(OdysseyJourneyRoleTokens.Rank(right.roleToken));
            if (value != 0) return value;

            value = string.Compare(left.pawnId, right.pawnId, StringComparison.Ordinal);
            if (value != 0) return value;

            return string.Compare(left.displayName, right.displayName, StringComparison.Ordinal);
        }

        private static bool ContainsPawn(List<OdysseyWriterCandidate> selected, string pawnId)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (string.Equals(selected[i].pawnId, pawnId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static OdysseyWriterCandidate Copy(OdysseyWriterCandidate source)
        {
            return new OdysseyWriterCandidate
            {
                pawnId = (source.pawnId ?? string.Empty).Trim(),
                displayName = source.displayName ?? string.Empty,
                roleToken = source.roleToken ?? string.Empty,
                eligible = source.eligible,
                present = source.present,
                hasDiary = source.hasDiary
            };
        }
    }
}
