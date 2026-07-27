// Pure optional-voice-cue policy. The game adapter snapshots trait/passion facts, then this helper
// owns the non-cumulative chance multiplier, deterministic private RNG seed, and each writer's
// stable Light/Gallows repertoire. It deliberately has no Verse, DefDatabase, or localized prose.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>
    /// Resolves temperament multipliers, stable seeds, and per-writer cue repertoires without
    /// Verse/Unity/Pawn dependencies.
    /// </summary>
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

        /// <summary>
        /// Returns at most <paramref name="maximumCount"/> stable cue keys owned by one writer in one
        /// tier. Ownership depends only on writer, tier, and canonical Def name: event IDs, reroll salts,
        /// XML order, cue weights, and localized label/rule prose cannot reshuffle it.
        /// </summary>
        public static List<string> StableRepertoire(
            IList<string> candidateKeys,
            string writerStableId,
            string tier,
            int maximumCount)
        {
            List<string> canonicalKeys = CanonicalKeys(candidateKeys);
            if (canonicalKeys.Count <= 1
                || string.IsNullOrWhiteSpace(writerStableId)
                || maximumCount <= 0
                || maximumCount >= canonicalKeys.Count)
            {
                return canonicalKeys;
            }

            List<RankedKey> ranked = new List<RankedKey>(canonicalKeys.Count);
            for (int i = 0; i < canonicalKeys.Count; i++)
            {
                string key = canonicalKeys[i];
                ranked.Add(new RankedKey
                {
                    Key = key,
                    Score = RepertoireScore(writerStableId, tier, key),
                });
            }

            // Highest unsigned rendezvous score wins. The ordinal key tie-break makes even a rare
            // 32-bit score collision deterministic; neither comparison can depend on current culture.
            ranked.Sort(delegate(RankedKey left, RankedKey right)
            {
                int scoreOrder = right.Score.CompareTo(left.Score);
                return scoreOrder != 0 ? scoreOrder : string.CompareOrdinal(left.Key, right.Key);
            });

            List<string> selected = new List<string>(maximumCount);
            for (int i = 0; i < maximumCount; i++)
            {
                selected.Add(ranked[i].Key);
            }

            // The event-time weighted picker receives this stable ordinal order, so merely reordering
            // DiaryHumorCueDefs.xml cannot change the selected cue for an otherwise identical event.
            selected.Sort(StringComparer.Ordinal);
            return selected;
        }

        private static List<string> CanonicalKeys(IList<string> candidateKeys)
        {
            Dictionary<string, string> byIdentity =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (candidateKeys != null)
            {
                for (int i = 0; i < candidateKeys.Count; i++)
                {
                    string key = candidateKeys[i];
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    string existing;
                    if (!byIdentity.TryGetValue(key, out existing)
                        || string.CompareOrdinal(key, existing) < 0)
                    {
                        // Differently cased duplicates represent one Def identity. Retaining the
                        // ordinal-smallest spelling prevents input order from choosing its casing.
                        byIdentity[key] = key;
                    }
                }
            }

            List<string> canonical = new List<string>(byIdentity.Values);
            canonical.Sort(StringComparer.Ordinal);
            return canonical;
        }

        private static uint RepertoireScore(string writerStableId, string tier, string canonicalKey)
        {
            uint hash = 2166136261u;
            AddFramed(ref hash, writerStableId);
            AddFramed(ref hash, tier);
            AddFramed(ref hash, canonicalKey);
            return hash;
        }

        private static void AddFramed(ref uint hash, string value)
        {
            string text = value ?? string.Empty;
            // An invariant length prefix makes tuple boundaries unambiguous even when a modded ID
            // itself contains punctuation such as '|'. C# string length and Add both operate on the
            // same UTF-16 code units, so the framing contract is stable across supported runtimes.
            Add(ref hash, text.Length.ToString(CultureInfo.InvariantCulture));
            AddChar(ref hash, ':');
            Add(ref hash, text);
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

        private sealed class RankedKey
        {
            public string Key;
            public uint Score;
        }
    }
}
