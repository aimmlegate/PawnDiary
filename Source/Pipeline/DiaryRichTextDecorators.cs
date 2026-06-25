// Pure rich-text transforms for diary text decorations.
//
// Unity rich text uses angle-bracket tags such as <b> and <color=#...>. These decorators copy tags
// through unchanged and mutate only visible words/letters, so generated diary text can be styled
// without corrupting existing UI formatting.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static PawnDiary.DiaryTextDecorationText;

namespace PawnDiary
{
    /// <summary>
    /// Deterministic rich-text mutation helpers used by the diary decoration facade.
    /// </summary>
    internal static class DiaryRichTextDecorators
    {
        // Maps each decoration-kind id to the transform that renders it. Rule SELECTION is fully
        // data-driven (XML DiaryTextDecorationDefs, matched generically), so the APPLY side is a
        // registry too: adding a kind is one entry here instead of a new branch in an if-ladder, and
        // a kind that gets selected but is not registered is a single lookup miss rather than a silent
        // fall-through. DiaryTextDecorationDefs surfaces any such gap once, at Def load. The signature
        // is uniform (rich, intensity, seed, baseFontSize) even though only the staggered sizer uses
        // baseFontSize, so every applier plugs in the same way.
        private static readonly Dictionary<string, Func<string, int, int, int, string>> Appliers =
            new Dictionary<string, Func<string, int, int, int, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { DiaryTextDecorationKinds.StaggeredWordSizes, (rich, intensity, seed, baseFontSize) => ApplyStaggeredWordSizes(rich, intensity, seed, baseFontSize) },
                { DiaryTextDecorationKinds.DimmedWords, (rich, intensity, seed, baseFontSize) => ApplyDimmedWordsToRichText(rich, intensity, seed) },
                { DiaryTextDecorationKinds.Zalgo, (rich, intensity, seed, baseFontSize) => ApplyZalgoToRichText(rich, intensity, seed) }
            };

        private static readonly char[] ZalgoMarks =
        {
            (char)0x0307, // dot above
            (char)0x0301, // acute accent
            (char)0x0300, // grave accent
            (char)0x0302, // circumflex
            (char)0x0303, // tilde
            (char)0x0323, // dot below
            (char)0x0324, // diaeresis below
            (char)0x0331, // macron below
            (char)0x0315, // comma above right
            (char)0x0336  // long stroke overlay
        };

        /// <summary>
        /// True when a decoration-kind id has a registered renderer in <see cref="Appliers"/>. The
        /// impure Def loader (<see cref="DiaryTextDecorationDefs"/>) uses this to warn once about a
        /// rule whose kind would otherwise be selected but produce no visible effect.
        /// </summary>
        internal static bool IsKnownKind(string decoration)
        {
            return Appliers.ContainsKey(Trim(decoration));
        }

        internal static string ApplyToRichText(string rich, DiaryTextDecorationPlan plan, int seed, int baseFontSize)
        {
            string result = rich ?? string.Empty;
            if (plan == null || plan.Empty)
            {
                return result;
            }

            for (int i = 0; i < plan.rules.Count; i++)
            {
                DiaryTextDecorationRule rule = plan.rules[i];
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                // Unknown kinds are skipped here; DiaryTextDecorationDefs has already warned about them
                // at load, so this stays a quiet no-op on the per-frame draw path.
                if (!Appliers.TryGetValue(Trim(rule.decoration), out Func<string, int, int, int, string> applier))
                {
                    continue;
                }

                int ruleSeed = seed ^ MixHash(rule.sequence, i + 17, rule.intensity);
                result = applier(result, rule.intensity, ruleSeed, baseFontSize);
            }

            return result;
        }

        internal static string ApplyStaggeredWordSizes(string rich, int intensity, int seed, int baseFontSize)
        {
            intensity = ClampIntensity(intensity);
            if (string.IsNullOrEmpty(rich) || intensity <= 0)
            {
                return rich ?? string.Empty;
            }

            if (baseFontSize <= 0)
            {
                baseFontSize = 13;
            }

            int selectionModulo = intensity >= 4 ? 3 : (intensity == 3 ? 4 : (intensity == 2 ? 6 : 9));
            int maxDelta = Math.Max(1, intensity + 1);
            StringBuilder result = new StringBuilder(rich.Length + 32);
            int wordIndex = 0;

            for (int i = 0; i < rich.Length;)
            {
                char c = rich[i];
                if (TryCopyRichTextTag(rich, ref i, result))
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < rich.Length && char.IsLetterOrDigit(rich[i]))
                    {
                        i++;
                    }

                    int length = i - start;
                    string word = rich.Substring(start, length);
                    int hash = PositiveHash(MixHash(seed, wordIndex, length));
                    if (length > 2 && hash % selectionModulo == 0)
                    {
                        int magnitude = 1 + (PositiveHash(hash / 17) % maxDelta);
                        int direction = (hash & 2) == 0 ? -1 : 1;
                        int size = Math.Max(8, baseFontSize + direction * magnitude);
                        result.Append("<size=");
                        result.Append(size.ToString(CultureInfo.InvariantCulture));
                        result.Append(">");
                        result.Append(word);
                        result.Append("</size>");
                    }
                    else
                    {
                        result.Append(word);
                    }

                    wordIndex++;
                    continue;
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }

        internal static string ApplyDimmedWordsToRichText(string rich, int intensity, int seed)
        {
            intensity = ClampIntensity(intensity);
            if (string.IsNullOrEmpty(rich) || intensity <= 0)
            {
                return rich ?? string.Empty;
            }

            int selectionModulo = intensity >= 4 ? 2 : (intensity == 3 ? 3 : (intensity == 2 ? 4 : 6));
            StringBuilder result = new StringBuilder(rich.Length + rich.Length / 3);
            int wordIndex = 0;

            for (int i = 0; i < rich.Length;)
            {
                char c = rich[i];
                if (TryCopyRichTextTag(rich, ref i, result))
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < rich.Length && char.IsLetterOrDigit(rich[i]))
                    {
                        i++;
                    }

                    int length = i - start;
                    string word = rich.Substring(start, length);
                    int hash = PositiveHash(MixHash(seed, wordIndex, length));
                    if (length > 2 && hash % selectionModulo == 0)
                    {
                        result.Append("<color=#56514D><i>");
                        result.Append(word);
                        result.Append("</i></color>");
                    }
                    else
                    {
                        result.Append(word);
                    }

                    wordIndex++;
                    continue;
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }

        internal static string ApplyZalgoToRichText(string rich, int intensity, int seed)
        {
            intensity = ClampIntensity(intensity);
            if (string.IsNullOrEmpty(rich) || intensity <= 0)
            {
                return rich ?? string.Empty;
            }

            StringBuilder result = new StringBuilder(rich.Length + rich.Length / 2);
            int visibleIndex = 0;
            for (int i = 0; i < rich.Length;)
            {
                if (TryCopyRichTextTag(rich, ref i, result))
                {
                    continue;
                }

                char c = rich[i++];
                result.Append(c);
                if (!char.IsLetter(c))
                {
                    continue;
                }

                int hash = PositiveHash(MixHash(seed, visibleIndex, c));
                int selectionModulo = intensity >= 4 ? 1 : (intensity == 3 ? 2 : (intensity == 2 ? 3 : 4));
                if (hash % selectionModulo == 0)
                {
                    int markCount = 1;
                    for (int extra = 1; extra < intensity; extra++)
                    {
                        if (PositiveHash(hash / (extra * 37 + 11)) % 3 != 0)
                        {
                            markCount++;
                        }
                    }

                    for (int mark = 0; mark < markCount; mark++)
                    {
                        int markIndex = PositiveHash(hash / (mark * 97 + 1)) % ZalgoMarks.Length;
                        result.Append(ZalgoMarks[markIndex]);
                    }
                }

                visibleIndex++;
            }

            return result.ToString();
        }

        // Trim, KindEquals, and TryCopyRichTextTag now live in DiaryTextDecorationText (shared with
        // the rule matcher and the name highlighter); see the `using static` at the top of this file.

        private static int ClampIntensity(int intensity)
        {
            if (intensity < 0) return 0;
            if (intensity > 4) return 4;
            return intensity;
        }

        private static int MixHash(int seed, int wordIndex, int length)
        {
            unchecked
            {
                int hash = seed;
                hash = (hash * 397) ^ wordIndex;
                hash = (hash * 397) ^ length;
                hash ^= hash >> 13;
                hash *= 1274126177;
                return hash;
            }
        }

        private static int PositiveHash(int value)
        {
            return value & 0x7fffffff;
        }
    }
}
