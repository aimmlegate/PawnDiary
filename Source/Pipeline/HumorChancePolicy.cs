// Pure humor-frequency policy. The game adapter snapshots trait/passion facts, then this helper owns
// the documented non-cumulative elevated/reduced/base decision and deterministic private RNG seed.
using System;

namespace PawnDiary
{
    /// <summary>Resolves temperament multipliers and stable seeds without Verse/Unity/Pawn dependencies.</summary>
    internal static class HumorChancePolicy
    {
        public static float Multiplier(bool upbeatTrait, bool socialPassion, bool dourTrait,
            float elevatedMultiplier, float reducedMultiplier)
        {
            bool elevated = upbeatTrait || socialPassion;
            if (elevated == dourTrait) return 1f;
            return elevated ? elevatedMultiplier : reducedMultiplier;
        }

        /// <summary>Stable FNV-1a seed for one event+POV; never uses process-randomized GetHashCode.
        /// The optional <paramref name="salt"/> is the anti-repetition reroll counter persisted on the
        /// event: salt 0 produces byte-identical input to the original two-field hash (so existing
        /// entries keep their humor decision), while salt &gt; 0 derives a different stable seed so the
        /// guard's reroll re-picks the cue deterministically.</summary>
        public static int StableSeed(string eventId, string writerStableId, int salt = 0)
        {
            uint hash = 2166136261u;
            Add(ref hash, eventId);
            AddChar(ref hash, '|');
            Add(ref hash, writerStableId);
            if (salt > 0)
            {
                AddChar(ref hash, '|');
                Add(ref hash, salt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return unchecked((int)hash);
        }

        private static void Add(ref uint hash, string value)
        {
            string text = value ?? string.Empty;
            for (int i = 0; i < text.Length; i++) AddChar(ref hash, text[i]);
        }

        private static void AddChar(ref uint hash, char value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }
    }
}
