// Social interactions — patch-side helpers. The PlayLog.Add capture+emit flow moved to
// InteractionSignal (Source/Ingestion/Sources/); the recorder body now lives there and enters through
// DiaryEvents.Submit. What remains here is the PRE-FLIGHT the Harmony patch needs before it renders
// RimWorld's grammar strings: whether to capture this row at all, and whether it is safe to render
// the row's POV text (some conversation-framework mods schedule follow-ups during grammar rendering).
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Cheap preflight used by the PlayLog.Add patch before it renders RimWorld grammar strings.
        /// The InteractionSignal repeats these checks because settings/XML may change between call sites.
        /// </summary>
        internal bool ShouldCaptureInteractionFromPlayLog(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            return CanRecordGameplayEventNow()
                && initiator != null
                && recipient != null
                && interactionDef != null
                && IsInteractionSignificant(interactionDef)
                && (IsDiaryEligible(initiator) || IsDiaryEligible(recipient));
        }

        /// <summary>
        /// Returns whether the PlayLog capture hook may render RimWorld's POV text for this
        /// interaction. Most vanilla rows are safe to render. Some compatibility groups intentionally
        /// skip rendering because another mod's grammar renderer can have gameplay side effects.
        /// </summary>
        internal bool ShouldRenderInteractionTextFromPlayLog(InteractionDef interactionDef)
        {
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            if (group != null && !group.captureRenderedGameText)
            {
                return false;
            }

            return !HasTaggedLogGrammar(interactionDef)
                || SpeakUpReplySchedulingGuardPatch.CanRenderTaggedGrammarSafely;
        }

        // Memoized result of the recursive rule-pack walk below, keyed by InteractionDef. The walk is
        // pure over an InteractionDef's immutable rule graph, but it runs on the PlayLog.Add hot path
        // (per captured interaction) and allocates a HashSet each call — so cache it once per def.
        private static readonly Dictionary<InteractionDef, bool> taggedLogGrammarCache =
            new Dictionary<InteractionDef, bool>();

        // Conversation-framework mods can attach grammar tags to social-log rules and use those
        // tags to schedule follow-up interactions while RimWorld renders the log text. Pawn Diary is
        // only observing the row, so it avoids rendering tagged grammar here and lets the recorder
        // fall back to neutral interaction text instead.
        private static bool HasTaggedLogGrammar(InteractionDef interactionDef)
        {
            if (interactionDef == null)
            {
                return false;
            }

            bool tagged;
            if (taggedLogGrammarCache.TryGetValue(interactionDef, out tagged))
            {
                return tagged;
            }

            try
            {
                tagged = HasTaggedLogGrammar(interactionDef.logRulesInitiator, new HashSet<RulePack>());
            }
            catch
            {
                // If a third-party rule pack is malformed, prefer neutral fallback text over risking
                // an exception inside RimWorld's PlayLog.Add flow.
                tagged = true;
            }

            taggedLogGrammarCache[interactionDef] = tagged;
            return tagged;
        }

        private static bool HasTaggedLogGrammar(RulePack rulePack, HashSet<RulePack> visited)
        {
            if (rulePack == null || visited == null || visited.Contains(rulePack))
            {
                return false;
            }

            visited.Add(rulePack);

            if (HasTaggedRule(rulePack.Rules)
                || HasTaggedRule(rulePack.UntranslatedRules))
            {
                return true;
            }

            if (rulePack.include == null)
            {
                return false;
            }

            for (int i = 0; i < rulePack.include.Count; i++)
            {
                RulePackDef included = rulePack.include[i];
                if (included != null
                    && (HasTaggedRule(included.RulesPlusIncludes)
                        || HasTaggedRule(included.UntranslatedRulesPlusIncludes)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTaggedRule(List<Rule> rules)
        {
            if (rules == null)
            {
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                Rule_String stringRule = rules[i] as Rule_String;
                if (stringRule != null && !string.IsNullOrWhiteSpace(stringRule.tag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An interaction is recorded only if its group (see InteractionGroups) is enabled in settings.
        /// internal so the InteractionSignal capture in PawnDiary.Ingestion can reuse the same gate.
        /// </summary>
        internal static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }
    }
}
