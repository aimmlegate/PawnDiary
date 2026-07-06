// XML-backed tuning for the prompt context-detail presets. The pure PromptContextSelector owns the
// selection algorithm; this Def only supplies the Balanced/Compact character budgets so they can be
// retuned from data (AGENTS.md rule 3: thresholds/feature policy belong in XML Defs with code
// fallbacks) without recompiling. Every field matches the pure code default, so omitting the Def or
// a field keeps the built-in behavior.
//
// New to C#/RimWorld? See AGENTS.md ("Def", "DefDatabase").
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Single tuning Def for context-detail budgets. Values mirror <see cref="PromptContextBudgets"/>
    /// defaults; non-positive values are clamped back to the code default by <see cref="ContextDetailPolicy"/>.
    /// </summary>
    public class DiaryContextDetailDef : Def
    {
        // Standard interaction / solo entries.
        public int balancedDefaultBudget = 1400;
        public int compactDefaultBudget = 750;

        // Day / quadrum / arc reflections (longer, so a larger budget).
        public int balancedReflectionBudget = 1900;
        public int compactReflectionBudget = 1150;

        // Neutral death / arrival descriptions.
        public int balancedNeutralBudget = 1250;
        public int compactNeutralBudget = 850;
    }

    /// <summary>
    /// Accessor mapping the single <see cref="DiaryContextDetailDef"/> to the pure
    /// <see cref="PromptContextBudgets"/>, with safe code fallbacks when XML is absent. Mirrors the
    /// <c>DiaryUiStyles</c> pattern: cache the named Def once, fall back to a default instance.
    /// </summary>
    internal static class ContextDetailPolicy
    {
        private static DiaryContextDetailDef cached;
        private static readonly DiaryContextDetailDef Fallback = new DiaryContextDetailDef();

        public static DiaryContextDetailDef Current
        {
            get
            {
                if (cached == null)
                {
                    cached = DefDatabase<DiaryContextDetailDef>.GetNamedSilentFail("Diary_ContextDetail");
                }

                return cached ?? Fallback;
            }
        }

        /// <summary>
        /// Builds the pure budgets from the current Def, clamping each to a positive value so a
        /// misconfigured XML cannot make the selector drop every optional field.
        /// </summary>
        public static PromptContextBudgets Budgets()
        {
            DiaryContextDetailDef def = Current;
            PromptContextBudgets defaults = PromptContextBudgets.Defaults;
            return new PromptContextBudgets
            {
                balancedDefault = PositiveOr(def.balancedDefaultBudget, defaults.balancedDefault),
                compactDefault = PositiveOr(def.compactDefaultBudget, defaults.compactDefault),
                balancedReflection = PositiveOr(def.balancedReflectionBudget, defaults.balancedReflection),
                compactReflection = PositiveOr(def.compactReflectionBudget, defaults.compactReflection),
                balancedNeutral = PositiveOr(def.balancedNeutralBudget, defaults.balancedNeutral),
                compactNeutral = PositiveOr(def.compactNeutralBudget, defaults.compactNeutral)
            };
        }

        private static int PositiveOr(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }
    }
}
