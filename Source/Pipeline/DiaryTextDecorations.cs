// Public facade for diary text decorations.
//
// The implementation is split by concern: contracts live in DiaryTextDecorationContracts.cs,
// rule matching in DiaryTextDecorationMatcher.cs, pawn-fact serialization in
// DiaryTextDecorationFactCodec.cs, and rich-text mutation in DiaryRichTextDecorators.cs. Keeping
// this facade preserves the stable API used by capture, generation, UI, XML tests, and mod saves.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Stable public entry point for selecting, serializing, and applying diary text decorations.
    /// </summary>
    internal static class DiaryTextDecorations
    {
        /// <summary>
        /// Returns the matching rules for a text scope, sorted by sequence.
        /// </summary>
        public static DiaryTextDecorationPlan Select(
            DiaryTextDecorationContext context,
            IEnumerable<DiaryTextDecorationRule> rules,
            string scope)
        {
            return DiaryTextDecorationMatcher.Select(context, rules, scope);
        }

        /// <summary>
        /// True when a saved hediff fact matches an enabled staggered-word decoration rule.
        /// </summary>
        public static bool HediffMatchesStaggeredRules(
            IEnumerable<DiaryTextDecorationRule> rules,
            DiaryTextDecorationHediffFact fact)
        {
            return DiaryTextDecorationMatcher.HediffMatchesStaggeredRules(rules, fact);
        }

        /// <summary>
        /// Applies an ordered plan to a Unity rich-text string, preserving existing tags.
        /// </summary>
        public static string ApplyToRichText(string rich, DiaryTextDecorationPlan plan, int seed, int baseFontSize)
        {
            return DiaryRichTextDecorators.ApplyToRichText(rich, plan, seed, baseFontSize);
        }

        /// <summary>
        /// Adds deterministic variable-size words while preserving existing rich-text tags.
        /// </summary>
        public static string ApplyStaggeredWordSizes(string rich, int intensity, int seed, int baseFontSize)
        {
            return DiaryRichTextDecorators.ApplyStaggeredWordSizes(rich, intensity, seed, baseFontSize);
        }

        /// <summary>
        /// Darkens selected visible words while preserving existing rich-text tags.
        /// </summary>
        public static string ApplyDimmedWordsToRichText(string rich, int intensity, int seed)
        {
            return DiaryRichTextDecorators.ApplyDimmedWordsToRichText(rich, intensity, seed);
        }

        /// <summary>
        /// Adds deterministic combining marks to visible letters while preserving rich-text tags.
        /// </summary>
        public static string ApplyZalgoToRichText(string rich, int intensity, int seed)
        {
            return DiaryRichTextDecorators.ApplyZalgoToRichText(rich, intensity, seed);
        }

        /// <summary>
        /// Serializes only hediff/trait facts. Event fields are already saved on DiaryEvent.
        /// </summary>
        public static string SerializePawnFacts(DiaryTextDecorationContext context)
        {
            return DiaryTextDecorationFactCodec.SerializePawnFacts(context);
        }

        /// <summary>
        /// Adds serialized pawn facts back onto a decoration context.
        /// </summary>
        public static void AddSerializedPawnFacts(DiaryTextDecorationContext context, string serialized)
        {
            DiaryTextDecorationFactCodec.AddSerializedPawnFacts(context, serialized);
        }

        /// <summary>
        /// Adds simple key and key=value tags from a semicolon-delimited gameContext string.
        /// </summary>
        public static void AddEventTagsFromContext(DiaryTextDecorationContext context, string gameContext)
        {
            DiaryTextDecorationMatcher.AddEventTagsFromContext(context, gameContext);
        }

        /// <summary>
        /// True when a captured decoration context satisfies every populated condition category.
        /// </summary>
        public static bool Matches(DiaryTextDecorationContext context, DiaryTextDecorationCondition condition)
        {
            return DiaryTextDecorationMatcher.Matches(context, condition);
        }

        /// <summary>
        /// Reads one key from a semicolon-delimited gameContext string.
        /// </summary>
        public static string ContextValue(string gameContext, string key)
        {
            return DiaryTextDecorationMatcher.ContextValue(gameContext, key);
        }
    }
}
