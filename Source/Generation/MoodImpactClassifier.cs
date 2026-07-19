// Mood-impact policy for map-wide GameConditions (aurora, eclipse, psychic drone, toxic fallout,
// …): decides whether a given condition feels positive, negative, or neutral to a specific pawn.
// The direction is per-pawn because some conditions target one gender (psychic drone / Royalty's
// psychic suppression) or apply their thoughts programmatically. This pulls together the condition
// thought-offset scan (DefDatabase<ThoughtDef>), the pawn's own active thoughts, gender targeting
// via reflection, and the XML-owned known-positive/known-negative defName fallbacks.
//
// Split out of DiaryContextBuilder.cs (Run Card 6) so DiaryContextBuilder is left with only impure
// Pawn/Map context collection. The shared direction tokens and the ±0.5 threshold live on MoodImpact.
// See DOCUMENTATION.md. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Determines the mood direction (<see cref="MoodImpact.Positive"/> / Negative / Neutral) a
    /// <see cref="GameCondition"/> has on an individual pawn. Stateless; reads live pawn/condition
    /// state and the ThoughtDef database, so it is an impure policy helper despite living apart from
    /// the context collectors.
    /// </summary>
    internal static class MoodImpactClassifier
    {
        /// <summary>
        /// Determines whether a GameCondition has a positive, negative, or neutral mood impact on a
        /// specific pawn. Uses the condition's thought definitions for direction, then checks
        /// pawn-specific factors (e.g. PsychicDrone targets one gender; PsychicSuppressor only hurts
        /// the suppressed gender). Returns "positive", "negative", or "neutral" for use in the
        /// gameContext mood_impact field and the event text.
        /// <paramref name="conditionThoughtOffset"/> is the pawn-independent offset from the
        /// condition's thoughts, computed once by the caller via
        /// <see cref="GetMoodOffsetFromConditionThoughts"/> (it scans the whole ThoughtDef database,
        /// so we pass it in rather than repeat the scan for every affected colonist).
        /// </summary>
        public static string DetermineMoodImpact(GameCondition condition, Pawn pawn, float conditionThoughtOffset)
        {
            if (condition == null || condition.def == null || pawn == null)
            {
                return MoodImpact.Neutral;
            }

            GameConditionDef def = condition.def;
            string defName = def.defName;

            // First, check if this pawn is unaffected by the condition due to gender targeting.
            // GameCondition_PsychicEmanation (psychic drone / soothe) targets one gender.
            if (condition is GameCondition_PsychicEmanation emanationCondition)
            {
                if (emanationCondition.gender != pawn.gender)
                {
                    return MoodImpact.Neutral;
                }
            }

            // Royalty's psychic suppression also targets one gender. Avoid naming the DLC type; if a
            // live condition's defName says suppression, reflect the same gender field vanilla uses.
            if (defName != null
                && defName.IndexOf("PsychicSuppress", StringComparison.OrdinalIgnoreCase) >= 0
                && TryReadConditionGender(condition, out Gender suppressionGender))
            {
                if (suppressionGender != pawn.gender)
                {
                    return MoodImpact.Neutral;
                }
            }

            // Combine the pawn-independent condition offset (precomputed by the caller) with this
            // pawn's own active thoughts from the condition; the larger magnitude wins. RimWorld's
            // relationship is inverted: GameConditionDef does not hold thought references; instead
            // ThoughtDef has a `gameCondition` field that references the GameConditionDef it belongs to.
            float bestOffset = conditionThoughtOffset;
            float pawnOffset = GetMoodOffsetFromPawnThoughts(condition, pawn);
            if (Mathf.Abs(pawnOffset) > Mathf.Abs(bestOffset))
            {
                bestOffset = pawnOffset;
            }

            // If we found a meaningful mood offset, return the direction. (We can't use
            // MoodImpact.Classify here because a within-threshold offset must fall through to the
            // name-based heuristics below rather than short-circuit to neutral.)
            if (bestOffset > MoodImpact.MeaningfulThreshold)
            {
                return MoodImpact.Positive;
            }

            if (bestOffset < -MoodImpact.MeaningfulThreshold)
            {
                return MoodImpact.Negative;
            }

            // If no thought offsets found (some conditions apply thoughts programmatically),
            // fall back to name-based heuristics for known condition families.
            if (IsKnownPositiveCondition(defName))
            {
                return MoodImpact.Positive;
            }

            if (IsKnownNegativeCondition(defName))
            {
                return MoodImpact.Negative;
            }

            // If we can't determine the impact, default to neutral. The LLM will use the
            // condition label and gameContext to figure out how the pawn feels.
            return MoodImpact.Neutral;
        }

        /// <summary>
        /// Reads the <c>gender</c> field a gender-targeted GameCondition carries, without binding to
        /// any one DLC's condition subclass. Returns false (leaving <paramref name="gender"/> as
        /// None) when the field is absent or not a <see cref="Gender"/>.
        /// </summary>
        private static bool TryReadConditionGender(GameCondition condition, out Gender gender)
        {
            gender = Gender.None;
            if (condition == null)
            {
                return false;
            }

            FieldInfo field = condition.GetType().GetField("gender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Gender))
            {
                return false;
            }

            gender = (Gender)field.GetValue(condition);
            return true;
        }

        /// <summary>
        /// Scans <c>DefDatabase&lt;ThoughtDef&gt;</c> for any thoughts that reference the given
        /// GameConditionDef via their <c>gameCondition</c> field, then sums the baseMoodEffect across
        /// all stages. RimWorld's relationship is inverted: ThoughtDef points to GameConditionDef,
        /// not vice versa. Returns 0 if no matching thoughts are found. Caller hoists this out of the
        /// per-pawn loop since it walks the whole thought database.
        /// </summary>
        public static float GetMoodOffsetFromConditionThoughts(GameConditionDef conditionDef)
        {
            if (conditionDef == null)
            {
                return 0f;
            }

            float totalOffset = 0f;
            List<ThoughtDef> allThoughtDefs = DefDatabase<ThoughtDef>.AllDefsListForReading;
            for (int i = 0; i < allThoughtDefs.Count; i++)
            {
                ThoughtDef td = allThoughtDefs[i];
                if (td == null || td.gameCondition != conditionDef)
                {
                    continue;
                }

                if (td.stages == null)
                {
                    continue;
                }

                for (int j = 0; j < td.stages.Count; j++)
                {
                    if (td.stages[j] != null)
                    {
                        totalOffset += td.stages[j].baseMoodEffect;
                    }
                }
            }

            return totalOffset;
        }

        // Checks the pawn's current mood thoughts for any that come from the given GameCondition,
        // matching by the thought's `gameCondition` field pointing to our condition def.
        // Returns the total mood offset from matching thoughts, or 0 if none are found.
        private static float GetMoodOffsetFromPawnThoughts(GameCondition condition, Pawn pawn)
        {
            if (condition == null || condition.def == null || pawn?.needs?.mood?.thoughts == null)
            {
                return 0f;
            }

            float totalOffset = 0f;
            List<Thought> thoughts = new List<Thought>();
            try
            {
                // GetAllMoodThoughts calls MoodOffset() on every thought; a modded thought class can
                // throw inside that enumeration. Treat an unreadable mood state as "no offset" rather
                // than aborting the game-condition diary event. Do not log: it rethrows while the
                // broken thought persists.
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            }
            catch (Exception)
            {
                return 0f;
            }

            for (int i = 0; i < thoughts.Count; i++)
            {
                Thought thought = thoughts[i];
                if (thought == null || thought.def == null)
                {
                    continue;
                }

                // Match thoughts whose def references this condition (DefDatabase link).
                if (thought.def.gameCondition == condition.def)
                {
                    totalOffset += thought.MoodOffset();
                }
            }

            return totalOffset;
        }

        // Known GameConditionDefs that are always positive for every colonist. The list is XML-tuned
        // (DiaryTuningDef.positiveMoodConditionDefNames); entries are plain strings so a DLC-only
        // condition simply never appears without its DLC. See AGENTS.md ("DLC-safety").
        private static bool IsKnownPositiveCondition(string defName)
        {
            return InteractionGroups.ContainsDefName(DiaryTuning.Current.positiveMoodConditionDefNames, defName);
        }

        // Known GameConditionDefs that are always negative for affected colonists. Excludes condition
        // causers and gender-targeted effects (those vary per pawn). XML-tuned via
        // DiaryTuningDef.negativeMoodConditionDefNames.
        private static bool IsKnownNegativeCondition(string defName)
        {
            return InteractionGroups.ContainsDefName(DiaryTuning.Current.negativeMoodConditionDefNames, defName);
        }
    }
}
