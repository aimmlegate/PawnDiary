// Pure, game-independent selection from prompt variant pools.
//
// A DiaryInteractionGroupDef may carry a LIST of instruction/tone wordings (the variant pool) in
// addition to the single `instruction`/`tone` fallback string. Picking a different variant per
// entry is what keeps the 50th raid from reading identically to the 1st. The selection itself is
// deterministic given a seed, so:
//   - the build-time tone pick stays stable for one event across save/load and regeneration
//     (seeded by the event id), and
//   - the capture-time instruction pick can be rolled once with RimWorld's RNG and then persisted.
//
// "Pure" means: no RimWorld / Verse / Unity types, no DefDatabase, no .Translate(), no IO, and no
// RNG. The only inputs are the pool, a fallback, and an integer seed; the same inputs always yield
// the same output. That makes the selection rule unit-testable without the game (see
// tests/PromptVariantsTests). The impure callers (InteractionGroups.InstructionForGroup and
// DiaryPipelineAdapters.ToneFor) own the RNG/seed and call into here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Selects one wording from an optional variant pool, deterministically by seed. Pure: no RNG,
    /// no game types. Used for the per-group instruction and tone pools.
    /// </summary>
    internal static class PromptVariants
    {
        /// <summary>
        /// Returns one non-empty entry from <paramref name="variants"/>, chosen deterministically by
        /// <paramref name="seed"/>. When the pool is null/empty/whitespace-only, returns
        /// <paramref name="fallback"/> (which may be null or empty). Whitespace-only entries are
        /// skipped so a modder can leave a slot blank without shifting indices at runtime.
        /// </summary>
        public static string Pick(List<string> variants, string fallback, int seed)
        {
            // First pass: count usable entries without allocating, so small/empty pools are cheap and
            // we only build an index list when more than one variant is actually present.
            int usable = CountUsable(variants);
            if (usable <= 0)
            {
                return fallback;
            }

            // Single usable entry: no choice to make, but still prefer it over the fallback so a
            // one-element pool cleanly overrides the legacy singular field.
            if (usable == 1)
            {
                return FirstUsable(variants);
            }

            // Map the seed into [0, usable) with a non-negative remainder. C#'s % can be negative
            // when the seed is negative, so normalize before taking the modulus.
            int remaining = ((seed % usable) + usable) % usable;

            // Second pass: walk the pool, skipping whitespace entries, until we land on the chosen
            // usable slot. This keeps selection stable regardless of where blanks sit in the list.
            for (int i = 0; i < variants.Count; i++)
            {
                string entry = variants[i];
                if (!IsUsable(entry))
                {
                    continue;
                }

                if (remaining == 0)
                {
                    return entry.Trim();
                }

                remaining--;
            }

            // Unreachable given usable >= 1, but keeps the compiler happy and stays defensive.
            return fallback;
        }

        /// <summary>
        /// Like <see cref="Pick"/>, but avoids returning <paramref name="currentValue"/> when the
        /// pool offers at least one other usable wording: when the seeded pick lands on the current
        /// wording, the NEXT usable pool entry is taken instead (wrapping around). Used by the
        /// anti-repetition guard to reroll a variant without risking the same wording again. The
        /// comparison is trimmed and case-insensitive, matching how prompt text is rendered.
        /// </summary>
        public static string PickDifferent(List<string> variants, string fallback, int seed, string currentValue)
        {
            string picked = Pick(variants, fallback, seed);
            if (!TextEquals(picked, currentValue))
            {
                return picked;
            }

            int usable = CountUsable(variants);
            if (usable <= 1 || variants == null || variants.Count == 0)
            {
                // No alternative wording exists (the fallback is already the current value), so keep
                // the pick; the anti-repetition guard stays best-effort.
                return picked;
            }

            // Walk the pool from the current wording's position and take the next usable entry.
            int currentIndex = IndexOfUsable(variants, currentValue);
            for (int step = 1; step <= variants.Count; step++)
            {
                int index = (currentIndex + step) % variants.Count;
                string entry = variants[index];
                if (IsUsable(entry) && !TextEquals(entry, currentValue))
                {
                    return entry.Trim();
                }
            }

            return picked;
        }

        /// <summary>
        /// Returns true when <paramref name="value"/> matches the fallback or any usable pool entry
        /// (trimmed, case-insensitive). The anti-repetition instruction reroll uses this as a safety
        /// check: it only re-picks a wording that provably came from this pool, so event-window or
        /// external instructions are never replaced by group pool text.
        /// </summary>
        public static bool Contains(List<string> variants, string fallback, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (TextEquals(fallback, value))
            {
                return true;
            }

            if (variants == null)
            {
                return false;
            }

            for (int i = 0; i < variants.Count; i++)
            {
                if (IsUsable(variants[i]) && TextEquals(variants[i], value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Stable, non-negative hash for a seed string. Used to turn an event id into a deterministic
        /// seed so the same event always picks the same variant. Deliberately NOT string.GetHashCode:
        /// that is randomized across .NET/Mono process starts (DoS randomization), which would make
        /// the build-time pick drift between runs. This is a simple djb2-style polynomial hash.
        /// </summary>
        public static int HashSeed(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            unchecked
            {
                const uint Prime = 5381u;
                uint hash = Prime;
                for (int i = 0; i < text.Length; i++)
                {
                    // hash * 33 + c — classic djb2, cheap and well-distributed for short keys.
                    hash = (hash << 5) + hash + text[i];
                }

                // Mask off the sign bit so callers can safely use this as a non-negative seed.
                return (int)(hash & 0x7FFFFFFFu);
            }
        }

        private static int CountUsable(List<string> variants)
        {
            if (variants == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                if (IsUsable(variants[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static string FirstUsable(List<string> variants)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                if (IsUsable(variants[i]))
                {
                    return variants[i].Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsUsable(string entry)
        {
            return !string.IsNullOrWhiteSpace(entry);
        }

        // Index of the pool entry matching the value (trimmed, case-insensitive), or 0 when absent
        // so the "next usable" walk in PickDifferent simply starts at the pool head.
        private static int IndexOfUsable(List<string> variants, string value)
        {
            if (variants == null || string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            for (int i = 0; i < variants.Count; i++)
            {
                if (IsUsable(variants[i]) && TextEquals(variants[i], value))
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool TextEquals(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
