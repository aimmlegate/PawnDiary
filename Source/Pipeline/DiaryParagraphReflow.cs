// Pure render-time paragraph reflow for diary prose.
//
// The LLM is asked only for sentence counts ("1-3 sentences", "2-4", "4-7"), never for explicit
// paragraph breaks. A multi-sentence entry therefore arrives as one line and would otherwise wrap
// as a single dense block. This helper splits one already-trimmed prose line into 1..N paragraph
// chunks using punctuation cues and a length cap, so the Diary tab can render readable paragraphs.
//
// It is display-only: the saved DiaryEvent.GeneratedText is never mutated. The render path runs
// every line through ReflowLine() and turns each chunk into its own RoleplayLineBlock, with a
// blank-line gap between chunks (see ITab_Pawn_Diary.RoleplayText.NormalRoleplayBlocks). Because
// both the measure pass and the draw pass use this same helper, wrapped heights stay in sync.
//
// Pure by design: no RimWorld/Verse/Unity types, no randomness, no IO. Covered by
// tests/DiaryParagraphReflowTests. New to C#/RimWorld? See AGENTS.md ("Pipeline").
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Tunable knobs for <see cref="DiaryParagraphReflow.ReflowLine"/>. All primitive so the helper
    /// stays Verse/Unity-free and testable from a plain console harness.
    /// </summary>
    internal struct DiaryParagraphReflowOptions
    {
        /// <summary>Master toggle. When false, ReflowLine always returns the input line whole.</summary>
        public bool enabled;

        /// <summary>Preferred chunk length. Lines at or below this length are returned whole.</summary>
        public int targetChars;

        /// <summary>Hard limit. A chunk never grows past this; the nearest earlier break is used.</summary>
        public int maxChars;

        /// <summary>Split at sentence endings (.!?). Primary, most natural cue.</summary>
        public bool splitOnSentenceEnd;

        /// <summary>Split at a RimWorld year mention (55xx) followed by a clause boundary.</summary>
        public bool splitOnDateYear;

        /// <summary>Split at semicolons. Soft cue, used when no higher-priority break is near.</summary>
        public bool splitOnSemicolon;

        /// <summary>Split at em-dashes (— and --). Soft cue, same as semicolon.</summary>
        public bool splitOnEmDash;

        /// <summary>
        /// A trailing chunk shorter than this (in chars) is merged back into the previous chunk so
        /// a reflow never leaves a tiny orphan line.
        /// </summary>
        public int minBreakSpacing;
    }

    /// <summary>
    /// Stateless paragraph splitter for diary prose. Verse/Unity-free.
    /// </summary>
    internal static class DiaryParagraphReflow
    {
        // RimWorld's in-game calendar starts at year 5500, so a plausible year is "55" followed by
        // two digits. Matching only that range avoids treating arbitrary 4-digit numbers (a wall
        // length, a body count, "2024") as date markers. Culture-invariant and compiled once.
        // Word boundary on both sides so "5502" inside "x5502x" is ignored.
        private static readonly Regex YearPattern = new Regex(
            @"\b55\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Break-cue priorities. Higher = preferred break point.
        private const int PrioritySentenceEnd = 4;
        private const int PriorityDateYear = 3;
        private const int PrioritySemicolon = 2;
        private const int PriorityEmDash = 1;

        /// <summary>
        /// Splits one trimmed prose line into one or more paragraph chunks. A short line, an empty
        /// input, or a disabled <paramref name="options"/> returns a single-element list.
        /// </summary>
        public static List<string> ReflowLine(string line, DiaryParagraphReflowOptions options)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(line) || !options.enabled
                || options.targetChars <= 0 || options.maxChars <= 0
                || line.Length <= options.targetChars)
            {
                result.Add(line ?? string.Empty);
                return result;
            }

            List<BreakCandidate> candidates = CollectCandidates(line, options);
            List<int> splitAt = ChooseSplits(line, options, candidates);

            int start = 0;
            for (int i = 0; i < splitAt.Count; i++)
            {
                int end = splitAt[i];
                string chunk = line.Substring(start, end - start).Trim();
                if (chunk.Length > 0)
                {
                    result.Add(chunk);
                }

                start = end;
            }

            if (start < line.Length)
            {
                string tail = line.Substring(start).Trim();
                if (tail.Length > 0)
                {
                    result.Add(tail);
                }
            }

            MergeTinyTrailingChunk(result, options.minBreakSpacing);
            if (result.Count == 0)
            {
                result.Add(line);
            }

            return result;
        }

        // Records every viable break index in the line, each tagged with its priority. The break
        // index is the position immediately AFTER the punctuation/clause-boundary to consume, so a
        // chunk runs [chunkStart, breakIndex) and the next chunk starts at breakIndex.
        private static List<BreakCandidate> CollectCandidates(string line, DiaryParagraphReflowOptions options)
        {
            List<BreakCandidate> candidates = new List<BreakCandidate>();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                // Sentence end: . ! ? plus any trailing closing punctuation/quotes (e.g. `)."` ).
                if (options.splitOnSentenceEnd && (c == '.' || c == '!' || c == '?'))
                {
                    int end = i + 1;
                    while (end < line.Length && IsClosingSentenceMark(line[end]))
                    {
                        end++;
                    }

                    AddCandidate(candidates, end, PrioritySentenceEnd);
                    i = end - 1; // skip consumed trailers; for-loop ++ takes us to the next char
                    continue;
                }

                if (options.splitOnSemicolon && c == ';')
                {
                    AddCandidate(candidates, i + 1, PrioritySemicolon);
                    continue;
                }

                // Em-dash handling. U+2014 is one char; "--" is two ASCII hyphens.
                if (options.splitOnEmDash)
                {
                    if (c == '\u2014')
                    {
                        AddCandidate(candidates, i + 1, PriorityEmDash);
                        continue;
                    }

                    if (c == '-' && i + 1 < line.Length && line[i + 1] == '-')
                    {
                        AddCandidate(candidates, i + 2, PriorityEmDash);
                        i++; // consume the second hyphen here too
                        continue;
                    }
                }
            }

            if (options.splitOnDateYear)
            {
                AddDateYearCandidates(line, candidates);
            }

            return candidates;
        }

        // Breaks after a year mention's following clause boundary (comma+space, or the next
        // whitespace after "in/then/since <year>"). We never split mid-token: the candidate lands
        // on a whitespace boundary so chunks start cleanly.
        private static void AddDateYearCandidates(string line, List<BreakCandidate> candidates)
        {
            MatchCollection matches = YearPattern.Matches(line);
            for (int m = 0; m < matches.Count; m++)
            {
                Match match = matches[m];
                int after = match.Index + match.Length;
                if (after >= line.Length)
                {
                    continue;
                }

                int boundary = -1;
                // Preferred: ", " right after the year ("In 5502, we rebuilt.").
                if (line[after] == ',')
                {
                    int ws = after + 1;
                    while (ws < line.Length && line[ws] == ' ')
                    {
                        ws++;
                    }

                    if (ws < line.Length)
                    {
                        boundary = ws;
                    }
                }
                // Otherwise: a space right after the year ("spring of 5502 came and...") -> break
                // at the next whitespace so we keep the year with its clause.
                else if (line[after] == ' ')
                {
                    int nextSpace = line.IndexOf(' ', after + 1);
                    boundary = nextSpace < 0 ? -1 : nextSpace + 1;
                }

                if (boundary > 0 && boundary <= line.Length)
                {
                    AddCandidate(candidates, boundary, PriorityDateYear);
                }
            }
        }

        private static void AddCandidate(List<BreakCandidate> candidates, int index, int priority)
        {
            // Keep the highest priority at any given index; duplicates at lower priority are dropped.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].index == index)
                {
                    if (priority > candidates[i].priority)
                    {
                        candidates[i] = new BreakCandidate { index = index, priority = priority };
                    }

                    return;
                }
            }

            candidates.Add(new BreakCandidate { index = index, priority = priority });
        }

        // Greedy walk: at each step pick the candidate whose break index is closest to the ideal
        // (cursor + targetChars), so chunks stay near the preferred length. Ties are broken by
        // higher priority (a sentence end beats a semicolon at the same distance), then by smaller
        // index. Candidates that would leave a chunk smaller than minBreakSpacing are skipped so a
        // high-priority cue near the very start can't fragment the line into a tiny stub. If no
        // candidate survives, hard-break at the last space before maxChars. Returns absolute split
        // indices (positions where the next chunk begins).
        private static List<int> ChooseSplits(string line, DiaryParagraphReflowOptions options, List<BreakCandidate> candidates)
        {
            List<int> splits = new List<int>();
            int lineLength = line.Length;

            candidates.Sort(BreakIndexComparison);

            int cursor = 0;
            while (cursor < lineLength)
            {
                int target = cursor + options.targetChars;
                int hardLimit = cursor + options.maxChars;
                // Stop once the remaining text fits inside one target-sized chunk — there's nothing
                // left to gain by splitting.
                if (target >= lineLength)
                {
                    break;
                }

                if (hardLimit > lineLength)
                {
                    hardLimit = lineLength;
                }

                BreakCandidate best = default(BreakCandidate);
                bool found = false;
                int bestDistance = int.MaxValue;

                for (int i = 0; i < candidates.Count; i++)
                {
                    BreakCandidate cur = candidates[i];
                    if (cur.index <= cursor)
                    {
                        continue;
                    }

                    if (cur.index > hardLimit)
                    {
                        break; // sorted by index; nothing further can qualify
                    }

                    // Skip a candidate that would leave a chunk shorter than minBreakSpacing, so we
                    // never fragment off a tiny leading stub just because a cue sat near the start.
                    if (cur.index - cursor < options.minBreakSpacing)
                    {
                        continue;
                    }

                    int distance = Math.Abs(cur.index - target);
                    // Closest to target wins; ties go to higher priority, then to the earlier index.
                    if (!found
                        || distance < bestDistance
                        || (distance == bestDistance && IsBetterTie(cur, best)))
                    {
                        best = cur;
                        bestDistance = distance;
                        found = true;
                    }
                }

                if (found)
                {
                    splits.Add(best.index);
                    cursor = best.index;
                    continue;
                }

                // No punctuation cue in the window: hard-break at the last space before the limit so
                // words are never split mid-token.
                int hard = LastSpaceBefore(line, cursor + 1, hardLimit);
                if (hard <= cursor)
                {
                    hard = hardLimit; // one very long token: break exactly at the limit
                }

                if (hard >= lineLength)
                {
                    break;
                }

                splits.Add(hard);
                cursor = hard;
            }

            return splits;
        }

        // Tie-breaker for two candidates equidistant from target: prefer higher priority, then the
        // earlier index (smaller chunk overshoot).
        private static bool IsBetterTie(BreakCandidate candidate, BreakCandidate current)
        {
            if (candidate.priority != current.priority)
            {
                return candidate.priority > current.priority;
            }

            return candidate.index < current.index;
        }

        // Returns the index of the last single-space boundary in (from, limit], i.e. a position to
        // start the next chunk. A "space boundary" is a space char itself — splitting there drops
        // it via the later Trim(). If no space exists, returns -1.
        private static int LastSpaceBefore(string line, int from, int limit)
        {
            for (int i = limit; i >= from; i--)
            {
                if (i < line.Length && line[i] == ' ')
                {
                    return i;
                }
            }

            return -1;
        }

        // Merge a final chunk shorter than minBreakSpacing back into the previous chunk.
        private static void MergeTinyTrailingChunk(List<string> result, int minBreakSpacing)
        {
            if (minBreakSpacing <= 0 || result.Count < 2)
            {
                return;
            }

            int last = result.Count - 1;
            if (result[last].Length < minBreakSpacing)
            {
                result[last - 1] = (result[last - 1] + " " + result[last]).Trim();
                result.RemoveAt(last);
            }
        }

        private static bool IsClosingSentenceMark(char c)
        {
            return c == '"'
                || c == '\''
                || c == ')'
                || c == ']'
                || c == '\u2019'   // right single quote
                || c == '\u201d';  // right double quote
        }

        private static int BreakIndexComparison(BreakCandidate a, BreakCandidate b)
        {
            if (a.index < b.index) return -1;
            if (a.index > b.index) return 1;
            return 0;
        }

        private struct BreakCandidate
        {
            public int index;
            public int priority;
        }
    }
}
