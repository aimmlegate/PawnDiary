// Thoughts — the MemoryThoughtHandler.TryGainMemory hook's diary flow. A pawn gaining a temporary
// memory thought becomes a solo DiaryEvent, but only after filtering: permanent thoughts
// (durationDays <= 0), configurable ignore tokens, and sub-threshold magnitudes are dropped (eating
// thoughts use a higher bar; "bypass" thoughts like death/banishment skip the threshold). The event
// carries a "thought=" game-context marker so the UI classifies it into the Thought domain. The
// helpers below match the configurable token lists and read the thought's current-stage mood offset.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
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
            if (!CanRecordGameplayEventNow() || pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null
                || !DiarySignalPolicies.Enabled(DiarySignalPolicies.Thought)
                || !PawnDiaryMod.Settings.IsThoughtEnabled(thought.def))
            {
                return;
            }

            if (thought.def.durationDays <= 0f)
            {
                return;
            }

            if (MatchesAnyToken(thought.def, DiarySignalPolicies.ThoughtIgnoreTokens))
            {
                return;
            }

            float moodOffset = GetThoughtMoodOffset(thought);
            bool isEatingThought = MatchesAnyToken(thought.def, DiarySignalPolicies.ThoughtEatingTokens);
            bool bypassThreshold = MatchesAnyToken(thought.def, DiarySignalPolicies.ThoughtBypassThresholdTokens);

            // Bypass thoughts (death, banishment, etc.) are always recorded regardless of magnitude.
            // Everything else must clear its threshold; eating thoughts use the higher bar.
            if (!bypassThreshold)
            {
                float minMoodOffset = isEatingThought
                    ? DiarySignalPolicies.ThoughtEatingMinMoodOffset
                    : DiarySignalPolicies.ThoughtMinMoodOffset;
                if (Mathf.Abs(moodOffset) < minMoodOffset)
                {
                    return;
                }
            }

            string dedupKey = "thought|" + pawn.GetUniqueLoadID() + "|" + thought.def.defName;
            if (RecentlyRecorded(recentThoughtEvents, dedupKey, DiarySignalPolicies.ThoughtDedupTicks))
            {
                return;
            }

            string thoughtDefName = thought.def.defName;
            string label = DiaryContextBuilder.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = PawnDiaryMod.Settings.InstructionForThought(thought.def);

            string moodImpact = MoodImpact.Classify(moodOffset);

            if (DiarySignalPolicies.Enabled(DiarySignalPolicies.AmbientThought)
                && MatchesAnyToken(thought.def, DiarySignalPolicies.ThoughtAmbientTokens))
            {
                RecordAmbientThought(pawn, thought.def, label, moodOffset, moodImpact, instruction);
                return;
            }

            string gameContext = "thought=" + thoughtDefName
                + "; label=" + label
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset.ToString("F1", CultureInfo.InvariantCulture)
                + "; duration_days=" + thought.def.durationDays.ToString("F1", CultureInfo.InvariantCulture);

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
