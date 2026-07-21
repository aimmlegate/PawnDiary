// Queue-time prompt anti-repetition guard. Before a first-person prompt is dispatched to the LLM,
// this partial compares the assembled user prompt against the POV pawn's most recent stored prompts
// (queued or completed — anything that will produce / has produced a diary entry). When the pure
// PromptSimilarity score reaches the XML-tuned threshold, the guard rerolls the prompt's variable
// "enhancements" and rebuilds, up to a bounded number of attempts:
//   - instruction  — re-picked from the classified group's `instructions` variant pool
//                    (DiaryPipelineAdapters.TryRerollInstruction; persisted on the event),
//   - tone         — re-picked from the group's `tones` pool via the salted deterministic seed
//                    (DiaryEvent.promptVariantRerolls -> DiaryPipelineAdapters.ToneFor),
//   - prompt enchantment — re-rolled from the live weighted candidate pool,
//   - humor cue    — re-picked via the salted stable seed (HumorCues.CueFor).
//
// The guard NEVER blocks generation: if every reroll still looks similar (or there is nothing to
// reroll), the entry generates with the last built prompt. All thresholds live in DiaryTuningDef
// (promptAntiRepeat*); the similarity math itself is pure (Source/Pipeline/PromptSimilarity.cs) and
// unit-tested in tests/DiaryPipelineTests. This is one piece of the partial DiaryGameComponent
// class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Enchantment re-roll retries per guard attempt. The weighted pool can legitimately return
        // the same text twice (single dominant candidate); a few extra rolls are cheap and avoid
        // declaring "nothing changed" prematurely. Defensive cap, not player policy — hence a code
        // constant rather than XML.
        private const int AntiRepeatEnchantmentRerollTries = 3;

        /// <summary>
        /// Builds a trial prompt for this POV and, when it looks too similar to the pawn's recent
        /// prompts, rerolls the per-entry enhancements (up to the XML-tuned attempt cap) and returns
        /// the final enchantment/humor strings the caller should dispatch with. The trial builder is
        /// the SAME plan builder the real dispatch uses (Full detail level), so the similarity
        /// decision is made on exactly the prompt shape that would be sent.
        /// </summary>
        private void ApplyPromptAntiRepeatGuard(
            DiaryEvent diaryEvent,
            string povRole,
            Func<string, string, DiaryPromptPlan> trialPlanBuilder,
            Dictionary<string, Pawn> livePawnsById,
            ref string promptEnchantment,
            ref string humorCue)
        {
            if (diaryEvent == null || trialPlanBuilder == null)
            {
                return;
            }

            if (!DiaryTuning.PromptAntiRepeatEnabled)
            {
                return;
            }

            int maxRerolls = DiaryTuning.PromptAntiRepeatMaxRerolls;
            int recentCount = DiaryTuning.PromptAntiRepeatRecentPrompts;
            if (maxRerolls <= 0 || recentCount <= 0)
            {
                return;
            }

            // Same gate the enchantment/humor resolvers use: only first-person templates that render
            // persona/enchantment fields participate. Neutral death/arrival/title prompts are
            // style-free factual notes where similarity is expected and correct.
            if (!DiaryPromptBuilder.ShouldResolvePromptEnchantment(diaryEvent))
            {
                return;
            }

            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            List<string> recentPrompts = RecentPromptsForAntiRepeat(diaryEvent, pawnId, recentCount);
            if (recentPrompts.Count == 0)
            {
                return;
            }

            float threshold = DiaryTuning.PromptAntiRepeatSimilarityThreshold;
            DiaryPromptPlan trial = trialPlanBuilder(promptEnchantment, humorCue);
            float strongest;
            if (trial == null
                || !PromptSimilarity.TooSimilar(trial.userPrompt, recentPrompts, threshold, out strongest))
            {
                return;
            }

            LogApiDebug(
                "Anti-repeat: prompt too similar (score=" + strongest.ToString("F2")
                + " >= " + threshold.ToString("F2") + ") event=" + diaryEvent.eventId
                + " role=" + povRole + "; rerolling enhancements");

            for (int attempt = 1; attempt <= maxRerolls; attempt++)
            {
                // The persisted counter salts the tone and humor seeds, so the rerolled picks stay
                // stable across save/load and manual regeneration. The instruction reroll is
                // persisted on the event itself and only fires when the current wording provably
                // came from the group's variant pool.
                diaryEvent.promptVariantRerolls++;
                DiaryPipelineAdapters.TryRerollInstruction(diaryEvent, diaryEvent.promptVariantRerolls);
                promptEnchantment = RerollPromptEnchantment(diaryEvent, povRole, promptEnchantment, livePawnsById);
                humorCue = HumorCueFor(diaryEvent, povRole, livePawnsById);

                trial = trialPlanBuilder(promptEnchantment, humorCue);
                if (trial == null
                    || !PromptSimilarity.TooSimilar(trial.userPrompt, recentPrompts, threshold, out strongest))
                {
                    LogApiDebug(
                        "Anti-repeat: reroll " + attempt + " resolved similarity event="
                        + diaryEvent.eventId + " role=" + povRole);
                    return;
                }
            }

            // Best-effort by design: dispatch the last built prompt rather than dropping the entry.
            LogApiDebug(
                "Anti-repeat: still similar (score=" + strongest.ToString("F2") + ") after "
                + maxRerolls + " reroll(s) event=" + diaryEvent.eventId + " role=" + povRole
                + "; generating anyway");
        }

        /// <summary>
        /// Collects the pawn's most recent stored prompts (newest first), excluding the event
        /// currently being queued. Both queued and completed prompts count: each one represents a
        /// diary entry the fresh prompt should not echo.
        /// </summary>
        private List<string> RecentPromptsForAntiRepeat(DiaryEvent currentEvent, string pawnId, int maxCount)
        {
            List<string> recent = new List<string>();
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
            if (allEvents == null)
            {
                return recent;
            }

            for (int i = allEvents.Count - 1; i >= 0 && recent.Count < maxCount; i--)
            {
                DiaryEvent candidate = allEvents[i];
                if (candidate == null
                    || ReferenceEquals(candidate, currentEvent)
                    || string.Equals(candidate.eventId, currentEvent?.eventId, StringComparison.Ordinal))
                {
                    continue;
                }

                string role = candidate.RoleForPawn(pawnId);
                if (string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                string prompt = candidate.PromptForRole(role);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    recent.Add(prompt);
                }
            }

            return recent;
        }

        /// <summary>
        /// Re-rolls the live prompt enchantment, preferring a text that differs from the current
        /// one. The weighted candidate pool may have only one strong candidate, in which case the
        /// same text is legitimately returned again (the guard's other rerolls still apply).
        /// </summary>
        private string RerollPromptEnchantment(DiaryEvent diaryEvent, string povRole,
            string currentEnchantment, Dictionary<string, Pawn> livePawnsById)
        {
            string latest = currentEnchantment;
            for (int i = 0; i < AntiRepeatEnchantmentRerollTries; i++)
            {
                string rolled = PromptEnchantmentRuleFor(diaryEvent, povRole, livePawnsById);
                latest = rolled;
                if (!string.Equals(
                        (rolled ?? string.Empty).Trim(),
                        (currentEnchantment ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return rolled;
                }
            }

            return latest;
        }
    }
}
