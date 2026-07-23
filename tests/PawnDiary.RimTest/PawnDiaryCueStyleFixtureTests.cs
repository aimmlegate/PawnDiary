// Loaded-Def fixture for the C4 per-cue page tints and header rules
// (design/QUALITY_WAVE_IMPLEMENTATION_PLAN.md §3.2).
//
// Before C4 only three cues (combat, socialFight, mentalBreak) could tint a diary page, through a
// hardcoded if-chain in DiaryUiStyleDef. Now every cue declares its own optional pageTint/headerRule
// beside its accent in the cueColors list, and PageTintForCue/HeaderRuleForCue are a single lookup with
// "no row, or no spec on the row" meaning "inherit the shared parchment tint/rule".
//
// This suite asserts the RESOLVED colors from the loaded Diary_UiStyle Def — the half a pure test cannot
// reach, since DiaryUiStyleDef needs Verse/UnityEngine and the values only become real after XML load.
// The shipped XML row values themselves are pinned deterministically by the pure suite
// (DiaryPipelineTests.TestCueTintXmlPolicy), so nothing here re-asserts hand-copied numbers except the
// three legacy pairs whose exact preservation is the whole point of the migration.
//
// Everything here is read-only: no game state is touched, so the suite is safe at the main menu and
// needs no scope, pawn, or cleanup.
using System;
using System.Collections.Generic;
using RimTestRedux;
using UnityEngine;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves per-cue page tints and header rules resolve from the loaded <c>DiaryUiStyleDef</c>, that an
    /// unknown or blank cue falls back to the shared parchment tint/rule, that a cue with no header rule
    /// of its own inherits the shared one, and that the XML-absent C# fallback keeps the three legacy cue
    /// values exactly.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryCueStyleFixtureTests
    {
        // The three tint/rule pairs that shipped before per-cue tints existed. §0.10 of the plan locks
        // these values: the migration must be invisible on a combat, social-fight, or mental-break page.
        private static readonly Color LegacyCombatTint = new Color(0.70f, 0.10f, 0.07f, 0.18f);
        private static readonly Color LegacyCombatRule = new Color(0.95f, 0.18f, 0.12f, 0.65f);
        private static readonly Color LegacySocialFightTint = new Color(0.90f, 0.34f, 0.05f, 0.16f);
        private static readonly Color LegacySocialFightRule = new Color(1f, 0.52f, 0.16f, 0.68f);
        private static readonly Color LegacyMentalBreakTint = new Color(0.18f, 0.34f, 0.22f, 0.09f);
        private static readonly Color LegacyMentalBreakRule = new Color(0.40f, 0.58f, 0.40f, 0.42f);

        /// <summary>
        /// The three cues that could tint a page before C4 resolve to exactly the same colors through
        /// the new per-cue lookup. A drift here is a visible regression on the loudest diary pages.
        /// </summary>
        [Test]
        public static void LegacyThreeCuesKeepTheirExactTintsAndRules()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;

            RequireColor("combat page tint", LegacyCombatTint, style.PageTintForCue(DiaryEvent.CombatColorCue));
            RequireColor("combat header rule", LegacyCombatRule, style.HeaderRuleForCue(DiaryEvent.CombatColorCue));
            RequireColor("social fight page tint", LegacySocialFightTint,
                style.PageTintForCue(DiaryEvent.SocialFightColorCue));
            RequireColor("social fight header rule", LegacySocialFightRule,
                style.HeaderRuleForCue(DiaryEvent.SocialFightColorCue));
            RequireColor("mental break page tint", LegacyMentalBreakTint,
                style.PageTintForCue(DiaryEvent.MentalBreakColorCue));
            RequireColor("mental break header rule", LegacyMentalBreakRule,
                style.HeaderRuleForCue(DiaryEvent.MentalBreakColorCue));
        }

        /// <summary>
        /// The C# fallback list — what a player sees if DiaryUiStyleDef.xml is missing or fails to load —
        /// carries the same three legacy pairs, so a broken XML never silently flattens a combat page to
        /// plain parchment.
        /// </summary>
        [Test]
        public static void XmlAbsentFallbackKeepsTheLegacyThreeCueValues()
        {
            // A bare instance is exactly the no-XML shape: field initializers only, no Def loading.
            DiaryUiStyleDef fallback = new DiaryUiStyleDef();

            RequireColor("fallback combat page tint", LegacyCombatTint,
                fallback.PageTintForCue(DiaryEvent.CombatColorCue));
            RequireColor("fallback combat header rule", LegacyCombatRule,
                fallback.HeaderRuleForCue(DiaryEvent.CombatColorCue));
            RequireColor("fallback social fight page tint", LegacySocialFightTint,
                fallback.PageTintForCue(DiaryEvent.SocialFightColorCue));
            RequireColor("fallback social fight header rule", LegacySocialFightRule,
                fallback.HeaderRuleForCue(DiaryEvent.SocialFightColorCue));
            RequireColor("fallback mental break page tint", LegacyMentalBreakTint,
                fallback.PageTintForCue(DiaryEvent.MentalBreakColorCue));
            RequireColor("fallback mental break header rule", LegacyMentalBreakRule,
                fallback.HeaderRuleForCue(DiaryEvent.MentalBreakColorCue));

            // And the fallback must agree with the shipped XML, or deleting the file would change the look
            // of every other cue too.
            DiaryUiStyleDef loaded = DiaryUiStyles.Current;
            foreach (string cue in LiveGroupCues())
            {
                RequireColor("fallback page tint matches XML for cue '" + cue + "'",
                    loaded.PageTintForCue(cue), fallback.PageTintForCue(cue));
                RequireColor("fallback header rule matches XML for cue '" + cue + "'",
                    loaded.HeaderRuleForCue(cue), fallback.HeaderRuleForCue(cue));
            }
        }

        /// <summary>
        /// Every cue a shipped interaction group can actually save resolves to its own row with its own
        /// page tint — the check that would have caught "eventful" quietly rendering as plain parchment.
        /// </summary>
        [Test]
        public static void EveryLiveGroupCueResolvesToItsOwnPageTint()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            List<string> cues = LiveGroupCues();
            PawnDiaryRimTestScope.Require(
                cues.Count > 0, "No interaction group declared a color cue; the Defs did not load.");

            foreach (string cue in cues)
            {
                PawnDiaryRimTestScope.Require(
                    !ColorsEqual(style.PageTintForCue(cue), style.PageTintColor),
                    "Cue '" + cue + "' has no page tint of its own and falls back to the shared parchment "
                    + "tint, so its pages are visually indistinguishable from every other entry.");
            }
        }

        /// <summary>
        /// A cue with no row at all — a modded or misspelled one — and a blank/null cue fall back to the
        /// shared parchment tint and rule rather than throwing or rendering an unset color.
        /// </summary>
        [Test]
        public static void UnknownAndBlankCuesFallBackToTheSharedTintAndRule()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            string[] unresolvable = { "PawnDiaryTest_NotACue", null, string.Empty, "   " };

            foreach (string cue in unresolvable)
            {
                RequireColor("page tint for unresolvable cue [" + (cue ?? "<null>") + "]",
                    style.PageTintColor, style.PageTintForCue(cue));
                RequireColor("header rule for unresolvable cue [" + (cue ?? "<null>") + "]",
                    style.HeaderRuleColor, style.HeaderRuleForCue(cue));
            }
        }

        /// <summary>
        /// A row may declare a page tint without a header rule: the quiet cues get their own wash but keep
        /// the shared rule, so only genuinely loud events draw a colored line under the title. The lookup
        /// is also case-insensitive, matching how saved cue strings are compared everywhere else.
        /// </summary>
        [Test]
        public static void CueWithoutItsOwnHeaderRuleInheritsTheSharedOne()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;

            PawnDiaryRimTestScope.Require(
                !ColorsEqual(style.PageTintForCue(DiaryEvent.QuietColorCue), style.PageTintColor),
                "The quiet cue should declare its own neutral-gray page tint.");
            RequireColor("quiet cue inherits the shared header rule",
                style.HeaderRuleColor, style.HeaderRuleForCue(DiaryEvent.QuietColorCue));

            RequireColor("cue lookup is case-insensitive",
                style.PageTintForCue(DiaryEvent.CombatColorCue),
                style.PageTintForCue(DiaryEvent.CombatColorCue.ToUpperInvariant()));
        }

        // ----- helpers ----------------------------------------------------------------------------------

        // Every distinct colorCue the loaded interaction groups can stamp on a saved DiaryEvent.
        private static List<string> LiveGroupCues()
        {
            List<string> cues = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DiaryInteractionGroupDef group in DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.colorCue))
                {
                    continue;
                }

                if (seen.Add(group.colorCue.Trim()))
                {
                    cues.Add(group.colorCue.Trim());
                }
            }

            return cues;
        }

        private static void RequireColor(string label, Color expected, Color actual)
        {
            PawnDiaryRimTestScope.Require(
                ColorsEqual(expected, actual),
                label + " mismatch.\nExpected: " + Describe(expected) + "\nActual:   " + Describe(actual));
        }

        // Unity's Color == is already approximate, but comparing components keeps the failure message
        // precise about which channel drifted.
        private static bool ColorsEqual(Color left, Color right)
        {
            return Near(left.r, right.r) && Near(left.g, right.g)
                && Near(left.b, right.b) && Near(left.a, right.a);
        }

        private static bool Near(float left, float right)
        {
            return Math.Abs(left - right) <= 0.0005f;
        }

        private static string Describe(Color color)
        {
            return "(" + color.r + ", " + color.g + ", " + color.b + ", " + color.a + ")";
        }
    }
}
