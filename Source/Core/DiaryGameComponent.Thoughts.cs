// Thoughts — the MemoryThoughtHandler.TryGainMemory hook's diary flow. A pawn gaining a temporary
// memory thought becomes a solo DiaryEvent, but only after filtering: permanent thoughts
// (durationDays <= 0), configurable ignore tokens, and sub-threshold magnitudes are dropped (eating
// thoughts use a higher bar; "bypass" thoughts like death/banishment skip the threshold). The event
// carries a "thought=" game-context marker so the UI classifies it into the Thought domain. The
// helpers below match the configurable token lists and read the thought's current-stage mood offset.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records a diary event when a pawn gains a temporary thought with expiration.
        /// Filters out thoughts below magnitude thresholds (±5 general, ±15 eating),
        /// room stat thoughts, and deduplicates within the configured window.
        /// </summary>
        public void RecordThought(Pawn pawn, Thought_Memory thought)
        {
            if (pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsThoughtEnabled(thought.def))
            {
                return;
            }

            if (thought.def.durationDays <= 0f)
            {
                return;
            }

            if (MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtIgnoreTokens))
            {
                return;
            }

            float moodOffset = GetThoughtMoodOffset(thought);
            bool isEatingThought = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtEatingTokens);
            bool bypassThreshold = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtBypassThresholdTokens);

            // Bypass thoughts (death, banishment, etc.) are always recorded regardless of magnitude.
            // Everything else must clear its threshold; eating thoughts use the higher bar.
            if (!bypassThreshold)
            {
                float minMoodOffset = isEatingThought
                    ? DiaryTuning.Current.thoughtEatingMinMoodOffset
                    : DiaryTuning.Current.thoughtMinMoodOffset;
                if (Mathf.Abs(moodOffset) < minMoodOffset)
                {
                    return;
                }
            }

            string dedupKey = "thought|" + pawn.GetUniqueLoadID() + "|" + thought.def.defName;
            if (RecentlyRecorded(recentThoughtEvents, dedupKey, DiaryTuning.Current.thoughtDedupTicks))
            {
                return;
            }

            string thoughtDefName = thought.def.defName;
            string label = DiaryContextBuilder.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = PawnDiaryMod.Settings.InstructionForThought(thought.def);

            string moodImpact = MoodImpact.Classify(moodOffset);

            string gameContext = "thought=" + thoughtDefName
                + "; label=" + label
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset.ToString("F1")
                + "; duration_days=" + thought.def.durationDays.ToString("F1");

            string text = MoodImpact.PickText(moodImpact,
                "PawnDiary.Event.ThoughtPositive", "PawnDiary.Event.ThoughtNegative", "PawnDiary.Event.Thought",
                pawn.LabelShortCap, label);

            DiaryEvent thoughtEvent = AddSoloEvent(pawn, null, thoughtDefName, label, text, instruction, gameContext);
            thoughtEvent.moodImpact = moodImpact;
            QueueLlmRewrite(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Returns true if the thought's defName contains any of the tokens in the given list (case-insensitive).
        /// Used for configurable ignore/bypass/classification token lists from DiaryTuningDef.
        /// </summary>
        private static bool MatchesAnyToken(ThoughtDef thoughtDef, List<string> tokens)
        {
            if (thoughtDef == null || string.IsNullOrEmpty(thoughtDef.defName) || tokens == null)
            {
                return false;
            }

            string defName = thoughtDef.defName;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i])
                    && defName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the mood offset for a thought's current stage. Uses Thought.MoodOffset()
        /// which returns the active stage's baseMoodEffect (not a sum of all stages).
        /// </summary>
        private static float GetThoughtMoodOffset(Thought_Memory thought)
        {
            if (thought == null)
            {
                return 0f;
            }

            return thought.MoodOffset();
        }
    }
}
