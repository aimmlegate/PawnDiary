// Pure private random stream for runtime diary decisions. The game-facing component supplies one
// gameplay-RNG-isolated seed when a loaded Game is constructed, then owns separate instances for page
// admission and psychotype rolls. Every later decision advances its own stream instead of borrowing
// Verse.Rand. SplitMix64 is small, deterministic, and requires no runtime dependency beyond the CLR
// types RimWorld's Mono already provides. The historical class name is retained for save/source
// compatibility; instances are transient and never serialized.
namespace PawnDiary
{
    /// <summary>An independently evolving random stream used for one-shot diary decisions.</summary>
    internal sealed class DiaryAdmissionRandom
    {
        // The increment is odd, so adding it visits every 64-bit state before repeating.
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private ulong state;

        public DiaryAdmissionRandom(uint seed)
        {
            // Fold the 32-bit seed away from an all-zero initial value. SplitMix64 itself permits zero,
            // but keeping a visibly non-degenerate state makes hand-written/default fixture seeds safe.
            state = GoldenGamma ^ seed;
        }

        /// <summary>Returns the next value in [0,1), advancing this stream exactly once.</summary>
        public float NextUnitFloat()
        {
            ulong value = NextUInt64();
            // A float has 24 bits of integer precision. Use the high 24 mixed bits and divide by 2^24;
            // the maximum is therefore 1 - 2^-24, never the closed upper boundary.
            return (float)(value >> 40) * (1f / 16777216f);
        }

        private ulong NextUInt64()
        {
            unchecked
            {
                ulong value = state += GoldenGamma;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
