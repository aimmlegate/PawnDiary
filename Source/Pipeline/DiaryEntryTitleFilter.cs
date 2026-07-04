// Pure filter policy for the public integration title-read API. Runtime code snapshots one
// DiaryEntryView into plain facts, then this helper decides whether a query includes it.
using System;
using PawnDiary.Integration;

namespace PawnDiary
{
    /// <summary>
    /// Plain facts used to evaluate a title-read filter without live game objects.
    /// </summary>
    internal struct DiaryEntryTitleFilterFacts
    {
        public int tick;
        public string date;
        public string povRole;
        public string domain;
        public string atmosphereCue;
        public bool archived;
    }

    /// <summary>
    /// Stateless matching rules for <see cref="DiaryEntryTitleQuery"/>.
    /// </summary>
    internal static class DiaryEntryTitleFilter
    {
        public static bool Matches(DiaryEntryTitleFilterFacts facts, DiaryEntryTitleQuery query)
        {
            if (query == null)
            {
                return true;
            }

            if (facts.archived)
            {
                if (!query.includeArchived)
                {
                    return false;
                }
            }
            else if (!query.includeActive)
            {
                return false;
            }

            if (query.minTick >= 0 && facts.tick < query.minTick)
            {
                return false;
            }

            if (query.maxTick >= 0 && facts.tick > query.maxTick)
            {
                return false;
            }

            if (!MatchesToken(facts.povRole, query.povRole))
            {
                return false;
            }

            if (!MatchesToken(facts.domain, query.domain))
            {
                return false;
            }

            if (!MatchesToken(facts.atmosphereCue, query.atmosphereCue))
            {
                return false;
            }

            if (!ContainsText(facts.date, query.dateContains))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesToken(string actual, string expected)
        {
            return string.IsNullOrWhiteSpace(expected)
                || string.Equals((actual ?? string.Empty).Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsText(string actual, string expectedFragment)
        {
            return string.IsNullOrWhiteSpace(expectedFragment)
                || (actual ?? string.Empty).IndexOf(expectedFragment.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
