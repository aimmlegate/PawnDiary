// Pure string-matching helpers for interaction-group classification. RimWorld Defs use CamelCase
// (and sometimes underscore-separated) defNames like "AteWithoutTable" or "NeedFood". The blunt
// instrument used to be a case-insensitive SUBSTRING token check (still in DiaryInteractionGroupDef
// as `matchTokens`), but substring tokens are easy to misfire: the token "Good" claims the negative
// grief thought "PawnWithGoodOpinionDied", and "Bad" claims the positive "PawnWithBadOpinionDied".
//
// The matchers here give XML policy four precise alternatives — ordinal exact, prefix, suffix, and
// CamelCase SEGMENT equality — so a group can claim a defName only when the intended identifier or
// whole word matches, not when a few letters happen to appear inside another word. They are pure (plain strings in,
// bool out, no RimWorld/Verse/Unity types) so the test harness can exercise them without loading
// the game. DiaryInteractionGroupDef.Matches is the only runtime caller; it forwards its loaded
// XML lists to these helpers.
//
// Segment example: "PawnWithGoodOpinionDied" splits into Pawn|With|Good|Opinion|Died, so a segment
// test for "Food" does NOT match it but a segment test for "Good" WOULD. Segment matching is more
// precise than substring but is still a word list — pick segments that are unambiguously valenced.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Pure prefix/suffix/segment matchers shared by every interaction-group classifier. Kept free
    /// of RimWorld types so the decision logic is unit-testable on its own.
    /// </summary>
    internal static class GroupNameMatcher
    {
        /// <summary>
        /// True if <paramref name="defName"/> exactly equals one configured identifier, including
        /// case. Use this for verified framework/DLC outcome names where a case variant must not be
        /// classified more broadly than the downstream policy which interprets it.
        /// </summary>
        internal static bool MatchesOrdinalExact(string defName, IReadOnlyList<string> exactNames)
        {
            if (string.IsNullOrEmpty(defName) || exactNames == null)
            {
                return false;
            }

            for (int i = 0; i < exactNames.Count; i++)
            {
                if (!string.IsNullOrEmpty(exactNames[i])
                    && string.Equals(defName, exactNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if <paramref name="defName"/> starts with any of the <paramref name="prefixes"/>
        /// (case-insensitive, ordinal). Empty/null inputs never match. Use this for defName families
        /// that share a leading word, e.g. ritual-quality thoughts "TerribleParty"/"TerribleFuneral".
        /// </summary>
        internal static bool MatchesPrefix(string defName, IReadOnlyList<string> prefixes)
        {
            if (string.IsNullOrEmpty(defName) || prefixes == null)
            {
                return false;
            }

            for (int i = 0; i < prefixes.Count; i++)
            {
                string prefix = prefixes[i];
                if (!string.IsNullOrEmpty(prefix)
                    && defName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if <paramref name="defName"/> ends with any of the <paramref name="suffixes"/>
        /// (case-insensitive, ordinal). Empty/null inputs never match. Use this for defName families
        /// that share a trailing word, e.g. "...Died" grief thoughts.
        /// </summary>
        internal static bool MatchesSuffix(string defName, IReadOnlyList<string> suffixes)
        {
            if (string.IsNullOrEmpty(defName) || suffixes == null)
            {
                return false;
            }

            for (int i = 0; i < suffixes.Count; i++)
            {
                string suffix = suffixes[i];
                if (!string.IsNullOrEmpty(suffix)
                    && defName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if any CamelCase/underscore/digit SEGMENT of <paramref name="defName"/> equals any of
        /// the <paramref name="segments"/> (case-insensitive, ordinal). Empty/null inputs never match.
        /// Segment equality is stricter than substring: "Food" matches "NeedFood" and "AteRawFood" but
        /// not "Foodstuff" or "Bloodfood". Prefer this over <c>matchTokens</c> whenever a whole word
        /// is the intent.
        /// </summary>
        internal static bool MatchesSegment(string defName, IReadOnlyList<string> segments)
        {
            if (string.IsNullOrEmpty(defName) || segments == null)
            {
                return false;
            }

            // Fast path: nothing to compare against.
            int matchedCount = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                if (!string.IsNullOrEmpty(segments[i]))
                {
                    matchedCount++;
                    break;
                }
            }
            if (matchedCount == 0)
            {
                return false;
            }

            List<string> parts = SplitSegments(defName);
            if (parts.Count == 0)
            {
                return false;
            }

            for (int s = 0; s < segments.Count; s++)
            {
                string segment = segments[s];
                if (string.IsNullOrEmpty(segment))
                {
                    continue;
                }

                for (int p = 0; p < parts.Count; p++)
                {
                    if (string.Equals(parts[p], segment, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Splits a CamelCase / underscore-separated / digit-bearing identifier into its whole-word
        /// segments. Underscore is a hard separator and is not emitted as a segment. Digit runs are
        /// treated as part of the preceding lowercase run so names like "GR_UwUTalkingToHumans" keep
        /// their intent. Examples:
        ///  "NeedFood"                    -> Need | Food
        ///  "AteRawFood"                  -> Ate | Raw | Food
        ///  "PsychicArchotechEmanator_Major" -> Psychic | Archotech | Emanator | Major
        ///  "PawnWithGoodOpinionDied"     -> Pawn | With | Good | Opinion | Died
        /// Pure on purpose so it can be locked down by unit tests.
        /// </summary>
        internal static List<string> SplitSegments(string value)
        {
            List<string> segments = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return segments;
            }

            int start = 0;
            int length = value.Length;
            for (int i = 0; i < length; i++)
            {
                char c = value[i];

                // Underscore is a hard separator: emit the run before it, skip the underscore.
                if (c == '_')
                {
                    if (i > start)
                    {
                        segments.Add(value.Substring(start, i - start));
                    }
                    start = i + 1;
                    continue;
                }

                if (i == 0)
                {
                    continue;
                }

                char prev = value[i - 1];

                // lowercase-or-digit followed by Upper opens a new word: "needFood", "food1Bar".
                if (IsLowerOrDigit(prev) && char.IsUpper(c))
                {
                    segments.Add(value.Substring(start, i - start));
                    start = i;
                    continue;
                }

                // Inside an upper-case run, an Upper followed (next) by a lower ends the acronym:
                // "XMLParser" -> XML | Parser. We split BEFORE the last upper so it joins the next
                // word, matching how humans read CamelCase.
                if (char.IsUpper(prev) && char.IsUpper(c)
                    && i + 1 < length && char.IsLower(value[i + 1]))
                {
                    segments.Add(value.Substring(start, i - start));
                    start = i;
                    continue;
                }
            }

            if (start < length)
            {
                segments.Add(value.Substring(start));
            }

            return segments;
        }

        private static bool IsLowerOrDigit(char c)
        {
            return char.IsLower(c) || char.IsDigit(c);
        }
    }
}
