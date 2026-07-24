// Pure Quality Wave B2 policy: mood-band probability, stable event/POV sampling, exact eligible
// context markers, compact text formatting, and "most extreme moment" batch retention. Live Pawn
// reads stay in DiaryContextBuilder; this file consumes and returns detached primitive data only.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One XML-authored mood band. <see cref="maxPercent"/> is the inclusive upper boundary; the
    /// final shipped row remains 101 as the authored defensive catch-all above a clamped 100 percent.
    /// </summary>
    public sealed class MoodSnapshotChanceRule
    {
        public int maxPercent;
        public float chance;
    }

    /// <summary>
    /// Plain event-time mood evidence retained by an in-memory batch. The text is already localized
    /// and prompt-safe; no live Pawn or Thought object crosses the capture boundary.
    /// </summary>
    internal sealed class MoodSnapshotCandidate
    {
        public int moodPercent;
        public int tick;
        public string text;
    }

    /// <summary>
    /// Owns every pure B2 decision so event capture, batch flush, and prompt template routing use the
    /// same marker vocabulary and deterministic probability contract.
    /// </summary>
    internal static class MoodSnapshotPolicy
    {
        public const string MoodEventMarker = "mood_event=";
        public const string ThoughtMarker = "thought=";
        public const string InspirationMarker = "inspiration=";
        public const string WorkMarker = "work=";
        public const string HediffMarker = "hediff=";
        public const string BatchMarker = "batch=";

        /// <summary>
        /// Returns the first authored inclusive upper band containing the mood. Rules are expected in
        /// ascending boundary order. Missing coverage uses the caller's defensive fallback.
        /// </summary>
        public static float BandChance(
            int moodPercent,
            IReadOnlyList<MoodSnapshotChanceRule> rules,
            float fallback)
        {
            if (rules == null)
            {
                return ClampChance(fallback);
            }

            for (int i = 0; i < rules.Count; i++)
            {
                MoodSnapshotChanceRule rule = rules[i];
                if (rule == null || moodPercent > rule.maxPercent)
                {
                    continue;
                }

                return ClampChance(rule.chance);
            }

            return ClampChance(fallback);
        }

        /// <summary>
        /// Applies the exact product of the global apply chance and the mood-band chance. Equality
        /// with the threshold is excluded, matching a roll in the half-open range [0, 1).
        /// </summary>
        public static bool ShouldInclude(float roll, float applyChance, float bandChance)
        {
            if (float.IsNaN(roll) || float.IsInfinity(roll) || roll < 0f || roll >= 1f)
            {
                return false;
            }

            float probability = ClampChance(applyChance) * ClampChance(bandChance);
            return roll < probability;
        }

        /// <summary>
        /// Returns a process-stable FNV-1a roll in [0, 1) for one final event, pawn, and POV role.
        /// Length-prefixing each component prevents ambiguous concatenations.
        /// </summary>
        public static float DeterministicRoll(string eventId, string pawnId, string povRole)
        {
            uint hash = 2166136261u;
            AddStableText(ref hash, eventId);
            AddStableText(ref hash, pawnId);
            AddStableText(ref hash, povRole);
            // Twenty-four retained bits map exactly into a float and cannot round the maximum to 1.
            return (hash >> 8) / 16777216f;
        }

        /// <summary>True for the five internal-state context keys routed to SoloInternalState.</summary>
        public static bool IsInternalStateContext(string gameContext)
        {
            return HasExactMarker(gameContext, MoodEventMarker)
                || HasExactMarker(gameContext, ThoughtMarker)
                || HasExactMarker(gameContext, InspirationMarker)
                || HasExactMarker(gameContext, WorkMarker)
                || HasExactMarker(gameContext, HediffMarker);
        }

        /// <summary>True when the structured context has an exact <c>batch</c> field.</summary>
        public static bool IsBatchedContext(string gameContext)
        {
            return HasExactMarker(gameContext, BatchMarker);
        }

        /// <summary>
        /// True when B2 may sample a mood snapshot. This deliberately shares the exact internal/batch
        /// marker helpers used by template routing, so eligibility cannot drift from prompt shape.
        /// </summary>
        public static bool IsEligibleContext(string gameContext)
        {
            return IsInternalStateContext(gameContext) || IsBatchedContext(gameContext);
        }

        /// <summary>
        /// Retains the candidate farthest from neutral mood (50). Ties prefer lower mood, then the
        /// earlier event tick; a final exact tie keeps the already-retained candidate.
        /// </summary>
        public static MoodSnapshotCandidate PreferBatchSnapshot(
            MoodSnapshotCandidate retained,
            MoodSnapshotCandidate candidate)
        {
            if (candidate == null)
            {
                return retained;
            }

            if (retained == null)
            {
                return candidate;
            }

            // Widen before subtraction so even adversarial int.MinValue test data cannot overflow.
            long retainedDistance = Math.Abs((long)retained.moodPercent - 50L);
            long candidateDistance = Math.Abs((long)candidate.moodPercent - 50L);
            if (candidateDistance != retainedDistance)
            {
                return candidateDistance > retainedDistance ? candidate : retained;
            }

            if (candidate.moodPercent != retained.moodPercent)
            {
                return candidate.moodPercent < retained.moodPercent ? candidate : retained;
            }

            return candidate.tick < retained.tick ? candidate : retained;
        }

        /// <summary>
        /// Builds the intentionally slim saved fact: one qualitative mood and, when present, one
        /// already-formatted top thought. Blank mood omits the whole field.
        /// </summary>
        public static string FormatText(string moodLabel, string topThought)
        {
            string mood = (moodLabel ?? string.Empty).Trim();
            if (mood.Length == 0)
            {
                return string.Empty;
            }

            string text = "mood=" + mood;
            string thought = (topThought ?? string.Empty).Trim();
            return thought.Length == 0 ? text : text + "; thoughts=" + thought;
        }

        private static bool HasExactMarker(string gameContext, string marker)
        {
            return DiaryContextFields.HasMarker(gameContext, marker);
        }

        private static float ClampChance(float chance)
        {
            if (float.IsNaN(chance) || float.IsInfinity(chance))
            {
                return 0f;
            }

            if (chance <= 0f) return 0f;
            return chance >= 1f ? 1f : chance;
        }

        private static void AddStableText(ref uint hash, string value)
        {
            string text = value ?? string.Empty;
            unchecked
            {
                // Hash the length as four fixed bytes. This is culture-independent and keeps the
                // component boundary unambiguous without allocating a formatted number string.
                int length = text.Length;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(length >> shift);
                    hash *= 16777619u;
                }
            }

            AddStableChars(ref hash, text);
        }

        private static void AddStableChars(ref uint hash, string text)
        {
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    // Hash both bytes of the UTF-16 code unit so non-ASCII pawn IDs remain stable.
                    char value = text[i];
                    hash ^= (byte)(value & 0xFF);
                    hash *= 16777619u;
                    hash ^= (byte)(value >> 8);
                    hash *= 16777619u;
                }
            }
        }
    }
}
