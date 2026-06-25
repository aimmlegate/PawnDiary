// Pure text-decoration contracts and deterministic rich-text transforms.
//
// RimWorld/Unity code captures pawn health, traits, and event metadata into the plain data classes
// below. After that point rule selection and string mutation are pure: no Pawn, DefDatabase, settings,
// Translate(), GUI, IO, or random state. This keeps unusual diary typography testable and XML-driven.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Stable decoration names used by XML and the pure decorator.
    /// </summary>
    public static class DiaryTextDecorationKinds
    {
        public const string StaggeredWordSizes = "StaggeredWordSizes";
        public const string DimmedWords = "DimmedWords";
        public const string Zalgo = "Zalgo";
    }

    /// <summary>
    /// Stable text scopes. DirectSpeech is the default so visual distortions stay inside explicit
    /// speech blocks unless XML opts into a wider scope.
    /// </summary>
    public static class DiaryTextDecorationScopes
    {
        public const string All = "All";
        public const string Body = "Body";
        public const string DirectSpeech = "DirectSpeech";
    }

    /// <summary>
    /// Plain, saveable fact about one active hediff on the diary POV pawn.
    /// </summary>
    public class DiaryTextDecorationHediffFact
    {
        public string defName;
        public string label;
        public float severity;
        public bool visible = true;
    }

    /// <summary>
    /// Plain, saveable fact about one trait on the diary POV pawn.
    /// </summary>
    public class DiaryTextDecorationTraitFact
    {
        public string defName;
        public string label;
        public int degree;
    }

    /// <summary>
    /// Plain context used to select XML text-decoration rules. It intentionally carries primitive
    /// values only, so tests and response formatting do not depend on live game objects.
    /// </summary>
    public class DiaryTextDecorationContext
    {
        public string povRole;
        public string defName;
        public string colorCue;
        public string atmosphereCue;
        public string domain;
        public string gameContext;
        public List<string> eventTags = new List<string>();
        public List<DiaryTextDecorationHediffFact> hediffs = new List<DiaryTextDecorationHediffFact>();
        public List<DiaryTextDecorationTraitFact> traits = new List<DiaryTextDecorationTraitFact>();
    }

    /// <summary>
    /// XML-friendly condition block. Values inside each list are ORed; populated categories are ANDed.
    /// For example: colorCue=strangeChat AND any matching trait.
    /// </summary>
    public class DiaryTextDecorationCondition
    {
        public List<string> anyPovRole;
        public List<string> anyDefName;
        public List<string> anyDomain;
        public List<string> anyColorCue;
        public List<string> anyAtmosphereCue;
        public List<string> anyEventTag;
        public List<string> anyContextKey;
        public List<string> anyContextValueContains;
        public List<string> anyHediffDefName;
        public List<string> anyHediffDefNameContains;
        public List<string> anyHediffLabelContains;
        public float minHediffSeverity = -1f;
        public List<string> anyTraitDefName;
        public List<string> anyTraitDefNameContains;
        public List<string> anyTraitLabelContains;
    }

    /// <summary>
    /// One XML-defined decoration rule. The decorator sorts matching rules by sequence and applies
    /// them in that order.
    /// </summary>
    public class DiaryTextDecorationRule
    {
        public bool enabled = true;
        public string decoration = DiaryTextDecorationKinds.StaggeredWordSizes;
        public string scope = DiaryTextDecorationScopes.DirectSpeech;
        public int sequence;
        public int intensity = 1;
        public DiaryTextDecorationCondition when = new DiaryTextDecorationCondition();
    }

    /// <summary>
    /// Ordered pure decoration plan for one roleplay text scope.
    /// </summary>
    public class DiaryTextDecorationPlan
    {
        public List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>();

        public bool Empty
        {
            get { return rules == null || rules.Count == 0; }
        }
    }

    /// <summary>
    /// Pure rule selector plus deterministic rich-text transforms used by the diary UI and tests.
    /// </summary>
    public static class DiaryTextDecorations
    {
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
        /// Returns the matching rules for a text scope, sorted by sequence.
        /// </summary>
        public static DiaryTextDecorationPlan Select(
            DiaryTextDecorationContext context,
            IEnumerable<DiaryTextDecorationRule> rules,
            string scope)
        {
            DiaryTextDecorationPlan plan = new DiaryTextDecorationPlan();
            if (rules == null)
            {
                return plan;
            }

            foreach (DiaryTextDecorationRule rule in rules)
            {
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                if (!ScopeMatches(rule.scope, scope))
                {
                    continue;
                }

                if (!Matches(context, rule.when))
                {
                    continue;
                }

                plan.rules.Add(rule);
            }

            plan.rules.Sort(CompareRules);
            return plan;
        }

        /// <summary>
        /// True when a single saved hediff fact matches the name condition of any enabled
        /// <see cref="DiaryTextDecorationKinds.StaggeredWordSizes"/> rule. This lets capture-time
        /// intoxication detection reuse the SAME XML-owned matcher list as render-time decoration
        /// (see <c>Diary_TextDecorations</c>), so there is one source of truth for "which hediffs
        /// count as intoxicating" instead of a parallel hardcoded keyword list. A non-visible fact
        /// never matches. Rules without a hediff-name criterion are skipped (an unconditional
        /// stagger rule must not classify every hediff as intoxicating). Pure.
        /// </summary>
        public static bool HediffMatchesStaggeredRules(
            IEnumerable<DiaryTextDecorationRule> rules,
            DiaryTextDecorationHediffFact fact)
        {
            if (rules == null || fact == null || !fact.visible)
            {
                return false;
            }

            foreach (DiaryTextDecorationRule rule in rules)
            {
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                if (!KindEquals(rule.decoration, DiaryTextDecorationKinds.StaggeredWordSizes))
                {
                    continue;
                }

                DiaryTextDecorationCondition when = rule.when;
                if (when == null)
                {
                    continue;
                }

                // A rule "names" a hediff only via a populated list that the fact actually hits.
                // Unlike the render-time matcher (where an unset category means "no constraint"),
                // here an unset list must NOT count as a match — otherwise one populated list plus
                // two unset ones would classify every hediff as intoxicating. This keeps partial
                // modder rules correct.
                bool named = (HasAny(when.anyHediffDefName) && MatchesAny(when.anyHediffDefName, fact.defName))
                    || (HasAny(when.anyHediffDefNameContains) && MatchesAnyContains(when.anyHediffDefNameContains, fact.defName))
                    || (HasAny(when.anyHediffLabelContains) && MatchesAnyContains(when.anyHediffLabelContains, fact.label));
                if (named)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies an ordered plan to a Unity rich-text string, preserving existing tags.
        /// </summary>
        public static string ApplyToRichText(string rich, DiaryTextDecorationPlan plan, int seed, int baseFontSize)
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

                int ruleSeed = seed ^ MixHash(rule.sequence, i + 17, rule.intensity);
                if (KindEquals(rule.decoration, DiaryTextDecorationKinds.StaggeredWordSizes))
                {
                    result = ApplyStaggeredWordSizes(result, rule.intensity, ruleSeed, baseFontSize);
                    continue;
                }

                if (KindEquals(rule.decoration, DiaryTextDecorationKinds.DimmedWords))
                {
                    result = ApplyDimmedWordsToRichText(result, rule.intensity, ruleSeed);
                    continue;
                }

                if (KindEquals(rule.decoration, DiaryTextDecorationKinds.Zalgo))
                {
                    result = ApplyZalgoToRichText(result, rule.intensity, ruleSeed);
                }
            }

            return result;
        }

        /// <summary>
        /// Adds deterministic variable-size words. The input is already rich text, so existing tags are
        /// copied as tags and only visible words can be wrapped in &lt;size&gt; spans.
        /// </summary>
        public static string ApplyStaggeredWordSizes(string rich, int intensity, int seed, int baseFontSize)
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

        /// <summary>
        /// Darkens selected visible words without corrupting the letters. This replaces the extreme
        /// darkness Zalgo treatment so darkness feels muffled/fading while strange chat stays uncanny.
        /// </summary>
        public static string ApplyDimmedWordsToRichText(string rich, int intensity, int seed)
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

        /// <summary>
        /// Adds deterministic combining marks to visible letters while leaving rich-text tags intact.
        /// Intensity 1 is light; intensity 4 is deliberately more disturbing.
        /// </summary>
        public static string ApplyZalgoToRichText(string rich, int intensity, int seed)
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

        /// <summary>
        /// Serializes only hediff/trait facts. Event fields are already saved on DiaryEvent.
        /// </summary>
        public static string SerializePawnFacts(DiaryTextDecorationContext context)
        {
            if (context == null)
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder();
            if (context.hediffs != null)
            {
                for (int i = 0; i < context.hediffs.Count; i++)
                {
                    DiaryTextDecorationHediffFact hediff = context.hediffs[i];
                    if (hediff == null)
                    {
                        continue;
                    }

                    result.Append("h|");
                    result.Append(CleanSerializedToken(hediff.defName));
                    result.Append("|");
                    result.Append(CleanSerializedToken(hediff.label));
                    result.Append("|");
                    result.Append(hediff.severity.ToString(CultureInfo.InvariantCulture));
                    result.Append("|");
                    result.Append(hediff.visible ? "1" : "0");
                    result.Append("\n");
                }
            }

            if (context.traits != null)
            {
                for (int i = 0; i < context.traits.Count; i++)
                {
                    DiaryTextDecorationTraitFact trait = context.traits[i];
                    if (trait == null)
                    {
                        continue;
                    }

                    result.Append("t|");
                    result.Append(CleanSerializedToken(trait.defName));
                    result.Append("|");
                    result.Append(CleanSerializedToken(trait.label));
                    result.Append("|");
                    result.Append(trait.degree.ToString(CultureInfo.InvariantCulture));
                    result.Append("\n");
                }
            }

            return result.ToString().TrimEnd();
        }

        /// <summary>
        /// Adds serialized pawn facts back onto a decoration context.
        /// </summary>
        public static void AddSerializedPawnFacts(DiaryTextDecorationContext context, string serialized)
        {
            if (context == null || string.IsNullOrWhiteSpace(serialized))
            {
                return;
            }

            if (context.hediffs == null)
            {
                context.hediffs = new List<DiaryTextDecorationHediffFact>();
            }

            if (context.traits == null)
            {
                context.traits = new List<DiaryTextDecorationTraitFact>();
            }

            string normalized = serialized.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length >= 5 && string.Equals(parts[0], "h", StringComparison.Ordinal))
                {
                    float severity;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out severity))
                    {
                        severity = 0f;
                    }

                    context.hediffs.Add(new DiaryTextDecorationHediffFact
                    {
                        defName = parts[1],
                        label = parts[2],
                        severity = severity,
                        visible = !string.Equals(parts[4], "0", StringComparison.Ordinal)
                    });
                    continue;
                }

                if (parts.Length >= 4 && string.Equals(parts[0], "t", StringComparison.Ordinal))
                {
                    int degree;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out degree))
                    {
                        degree = 0;
                    }

                    context.traits.Add(new DiaryTextDecorationTraitFact
                    {
                        defName = parts[1],
                        label = parts[2],
                        degree = degree
                    });
                }
            }
        }

        /// <summary>
        /// Adds simple key and key=value tags from a semicolon-delimited gameContext string.
        /// </summary>
        public static void AddEventTagsFromContext(DiaryTextDecorationContext context, string gameContext)
        {
            if (context == null)
            {
                return;
            }

            if (context.eventTags == null)
            {
                context.eventTags = new List<string>();
            }

            if (string.IsNullOrWhiteSpace(gameContext))
            {
                return;
            }

            string[] parts = gameContext.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? string.Empty : parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                int equals = part.IndexOf('=');
                if (equals > 0)
                {
                    AddUnique(context.eventTags, part.Substring(0, equals).Trim());
                    AddUnique(context.eventTags, part);
                }
                else
                {
                    AddUnique(context.eventTags, part);
                }
            }
        }

        public static bool Matches(DiaryTextDecorationContext context, DiaryTextDecorationCondition condition)
        {
            if (condition == null)
            {
                return true;
            }

            context = context ?? new DiaryTextDecorationContext();
            if (!MatchesAny(condition.anyPovRole, context.povRole)) return false;
            if (!MatchesAny(condition.anyDefName, context.defName)) return false;
            if (!MatchesAny(condition.anyDomain, context.domain)) return false;
            if (!MatchesAny(condition.anyColorCue, context.colorCue)) return false;
            if (!MatchesAny(condition.anyAtmosphereCue, context.atmosphereCue)) return false;
            if (!MatchesAnyInList(condition.anyEventTag, context.eventTags)) return false;
            if (!MatchesAnyContextKey(condition.anyContextKey, context.gameContext)) return false;
            if (!MatchesAnyContains(condition.anyContextValueContains, context.gameContext)) return false;
            if (!MatchesHediff(condition, context.hediffs)) return false;
            if (!MatchesTrait(condition, context.traits)) return false;
            return true;
        }

        public static string ContextValue(string gameContext, string key)
        {
            return DiaryContextFields.Value(gameContext, key);
        }

        private static bool MatchesHediff(DiaryTextDecorationCondition condition, List<DiaryTextDecorationHediffFact> hediffs)
        {
            bool hasCriterion = HasAny(condition.anyHediffDefName)
                || HasAny(condition.anyHediffDefNameContains)
                || HasAny(condition.anyHediffLabelContains)
                || condition.minHediffSeverity >= 0f;
            if (!hasCriterion)
            {
                return true;
            }

            if (hediffs == null)
            {
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                DiaryTextDecorationHediffFact hediff = hediffs[i];
                if (hediff == null || !hediff.visible)
                {
                    continue;
                }

                if (condition.minHediffSeverity >= 0f && hediff.severity < condition.minHediffSeverity)
                {
                    continue;
                }

                if (HediffNameMatches(condition, hediff))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HediffNameMatches(DiaryTextDecorationCondition condition, DiaryTextDecorationHediffFact hediff)
        {
            bool hasNameCriterion = HasAny(condition.anyHediffDefName)
                || HasAny(condition.anyHediffDefNameContains)
                || HasAny(condition.anyHediffLabelContains);
            if (!hasNameCriterion)
            {
                return true;
            }

            return MatchesAny(condition.anyHediffDefName, hediff.defName)
                || MatchesAnyContains(condition.anyHediffDefNameContains, hediff.defName)
                || MatchesAnyContains(condition.anyHediffLabelContains, hediff.label);
        }

        private static bool MatchesTrait(DiaryTextDecorationCondition condition, List<DiaryTextDecorationTraitFact> traits)
        {
            bool hasCriterion = HasAny(condition.anyTraitDefName)
                || HasAny(condition.anyTraitDefNameContains)
                || HasAny(condition.anyTraitLabelContains);
            if (!hasCriterion)
            {
                return true;
            }

            if (traits == null)
            {
                return false;
            }

            for (int i = 0; i < traits.Count; i++)
            {
                DiaryTextDecorationTraitFact trait = traits[i];
                if (trait == null)
                {
                    continue;
                }

                if (MatchesAny(condition.anyTraitDefName, trait.defName)
                    || MatchesAnyContains(condition.anyTraitDefNameContains, trait.defName)
                    || MatchesAnyContains(condition.anyTraitLabelContains, trait.label))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ScopeMatches(string ruleScope, string requestedScope)
        {
            string normalizedRule = string.IsNullOrWhiteSpace(ruleScope)
                ? DiaryTextDecorationScopes.DirectSpeech
                : ruleScope.Trim();
            string normalizedRequested = string.IsNullOrWhiteSpace(requestedScope)
                ? DiaryTextDecorationScopes.Body
                : requestedScope.Trim();

            return string.Equals(normalizedRule, DiaryTextDecorationScopes.All, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedRule, normalizedRequested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAny(List<string> expected, string actual)
        {
            if (!HasAny(expected))
            {
                return true;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (string.Equals(Trim(expected[i]), Trim(actual), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAnyContains(List<string> needles, string actual)
        {
            if (!HasAny(needles))
            {
                return true;
            }

            string text = actual ?? string.Empty;
            for (int i = 0; i < needles.Count; i++)
            {
                string needle = Trim(needles[i]);
                if (needle.Length > 0 && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAnyInList(List<string> expected, List<string> actual)
        {
            if (!HasAny(expected))
            {
                return true;
            }

            if (actual == null)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                string expectedValue = Trim(expected[i]);
                for (int j = 0; j < actual.Count; j++)
                {
                    if (string.Equals(expectedValue, Trim(actual[j]), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesAnyContextKey(List<string> keys, string gameContext)
        {
            if (!HasAny(keys))
            {
                return true;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(ContextValue(gameContext, keys[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCopyRichTextTag(string rich, ref int index, StringBuilder result)
        {
            if (index >= rich.Length || rich[index] != '<')
            {
                return false;
            }

            int tagEnd = rich.IndexOf('>', index);
            if (tagEnd < 0)
            {
                return false;
            }

            result.Append(rich, index, tagEnd - index + 1);
            index = tagEnd + 1;
            return true;
        }

        private static int CompareRules(DiaryTextDecorationRule left, DiaryTextDecorationRule right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null) return -1;
            if (right == null) return 1;
            int sequence = left.sequence.CompareTo(right.sequence);
            if (sequence != 0)
            {
                return sequence;
            }

            return string.Compare(left.decoration, right.decoration, StringComparison.OrdinalIgnoreCase);
        }

        private static bool KindEquals(string actual, string expected)
        {
            return string.Equals(Trim(actual), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static int ClampIntensity(int intensity)
        {
            if (intensity < 0) return 0;
            if (intensity > 4) return 4;
            return intensity;
        }

        private static void AddUnique(List<string> values, string value)
        {
            value = Trim(value);
            if (value.Length == 0)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static bool HasAny(List<string> values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static string CleanSerializedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
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
