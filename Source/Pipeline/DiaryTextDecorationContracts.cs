// Plain contracts for XML-owned diary text decorations.
//
// These types intentionally carry only primitive values and lists. RimWorld/Unity code snapshots pawn
// health, traits, and event metadata into these DTOs before the pure matcher and rich-text decorators
// run, so tests can exercise decoration policy without live game objects, settings, IO, or random state.
using System.Collections.Generic;

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
}
