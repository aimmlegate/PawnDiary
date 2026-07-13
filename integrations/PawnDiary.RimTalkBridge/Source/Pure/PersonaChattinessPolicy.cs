// Pure chattiness inference for Pawn Diary -> RimTalk persona ownership. XML supplies one baseline
// per psychotype; this helper selects it and adds stable per-pawn variation without Verse/RimTalk.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>One XML-authored psychotype-to-chattiness baseline.</summary>
    public sealed class PersonaChattinessProfile
    {
        /// <summary>Exact Pawn Diary psychotype Def identity.</summary>
        public string psychotypeDefName = string.Empty;

        /// <summary>RimTalk talk-initiation weight before stable pawn variation, in [0,1].</summary>
        public float baseline = 0.5f;
    }

    /// <summary>Selects and deterministically varies a RimTalk talk-initiation weight.</summary>
    internal static class PersonaChattinessPolicy
    {
        /// <summary>Returns a stable clamped baseline × relative pawn variation.</summary>
        public static float Resolve(string pawnStableId, string psychotypeDefName,
            IList<PersonaChattinessProfile> profiles, float defaultBaseline, float variationFraction)
        {
            float baseline = Clamp01(defaultBaseline);
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    PersonaChattinessProfile profile = profiles[i];
                    if (profile != null && string.Equals(profile.psychotypeDefName,
                            psychotypeDefName, StringComparison.OrdinalIgnoreCase))
                    {
                        baseline = Clamp01(profile.baseline);
                        break;
                    }
                }
            }

            float spread = Clamp01(variationFraction);
            if (spread <= 0f || baseline <= 0f)
            {
                return baseline;
            }

            // FNV-1a is stable across Mono/processes, unlike string.GetHashCode. Normalize case so a
            // defensive case-insensitive Def match cannot produce a different variation.
            uint hash = 2166136261u;
            HashText(ref hash, pawnStableId);
            HashChar(ref hash, '|');
            HashText(ref hash, psychotypeDefName);
            float unit = (hash & 0x00FFFFFFu) / 16777215f;
            float relativeOffset = (unit * 2f - 1f) * spread;
            return Clamp01(baseline * (1f + relativeOffset));
        }

        private static void HashText(ref uint hash, string text)
        {
            string value = text ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                HashChar(ref hash, char.ToUpperInvariant(value[i]));
            }
        }

        private static void HashChar(ref uint hash, char value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
